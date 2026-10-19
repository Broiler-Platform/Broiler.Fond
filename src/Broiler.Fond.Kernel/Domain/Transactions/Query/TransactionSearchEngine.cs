using System.Globalization;
using Broiler.Fond.Kernel.Storage;

namespace Broiler.Fond.Kernel.Domain.Transactions.Query;

/// <summary>
/// High-performance, in-memory search and query engine for transaction ledgers.
/// Satisfies the Milestone 2 local search performance requirement (&lt; 300 ms p95 with 100,000 cached records)
/// with zero telemetry and zero external service calls.
/// </summary>
public sealed class TransactionSearchEngine
{
    private readonly IReadOnlyList<TransactionItemRecord> _items;

    public TransactionSearchEngine(IEnumerable<TransactionItemRecord> items)
    {
        ArgumentNullException.ThrowIfNull(items);
        _items = items
            .OrderByDescending(t => t.BookingDate)
            .ThenByDescending(t => t.RetrievedAt)
            .ThenByDescending(t => t.TransactionId)
            .ToList()
            .AsReadOnly();
    }

    /// <summary>
    /// Gets all indexed transaction records ordered chronologically descending.
    /// </summary>
    public IReadOnlyList<TransactionItemRecord> AllTransactions => _items;

    /// <summary>
    /// Gets the total count of indexed transactions.
    /// </summary>
    public int Count => _items.Count;

    /// <summary>
    /// Queries the search engine using the specified filter criteria.
    /// Returns matching records ordered by BookingDate descending.
    /// </summary>
    public IReadOnlyList<TransactionItemRecord> Search(TransactionQueryFilter? filter)
    {
        if (filter is null || filter == TransactionQueryFilter.Empty)
        {
            return _items;
        }

        List<TransactionItemRecord> results = new();
        foreach (TransactionItemRecord item in _items)
        {
            if (filter.Matches(item))
            {
                results.Add(item);
            }
        }

        return results.AsReadOnly();
    }

    /// <summary>
    /// Projects and constructs a new <see cref="TransactionSearchEngine"/> from store snapshot documents.
    /// </summary>
    public static TransactionSearchEngine FromSnapshot(
        StoreAccountsDocument accountsDoc,
        StoreTransactionsDocument? transactionsDoc)
    {
        ArgumentNullException.ThrowIfNull(accountsDoc);

        if (transactionsDoc is null || transactionsDoc.Transactions.Count == 0)
        {
            return new TransactionSearchEngine(Array.Empty<TransactionItemRecord>());
        }

        Dictionary<ulong, (string DisplayName, string? Iban)> accountsMap = [];
        foreach (StoreAccountEntry acc in accountsDoc.Accounts)
        {
            StoreBindingEntry? binding = accountsDoc.Bindings.FirstOrDefault(b => b.AccountId == acc.Id);
            accountsMap[acc.Id] = (acc.DisplayName, binding?.Iban);
        }

        Dictionary<ulong, StoreTransactionObservationEntry> obsMap =
            transactionsDoc.Observations.ToDictionary(o => o.Id);

        Dictionary<ulong, StoreTransactionRevisionEntry> latestRevs = transactionsDoc.Revisions
            .GroupBy(r => r.TransactionId)
            .ToDictionary(
                g => g.Key,
                g => g.OrderByDescending(r => r.RetrievedAt).ThenByDescending(r => r.Id).First());

        Dictionary<ulong, StoreAnnotationRevisionEntry> latestAnns = transactionsDoc.Annotations
            .GroupBy(a => a.TransactionId)
            .ToDictionary(
                g => g.Key,
                g => g.OrderByDescending(a => a.RevisionTimestamp).ThenByDescending(a => a.Id).First());

        Dictionary<ulong, ulong> pendingToBooked = [];
        Dictionary<ulong, ulong> bookedToPending = [];
        foreach (StorePendingToBookedLinkEntry link in transactionsDoc.Links)
        {
            pendingToBooked[link.PendingTransactionId] = link.BookedTransactionId;
            bookedToPending[link.BookedTransactionId] = link.PendingTransactionId;
        }

        List<TransactionItemRecord> records = new(transactionsDoc.Transactions.Count);

        foreach (StoreTransactionEntry tx in transactionsDoc.Transactions)
        {
            accountsMap.TryGetValue(tx.AccountId, out var accInfo);
            latestRevs.TryGetValue(tx.Id, out StoreTransactionRevisionEntry? rev);
            latestAnns.TryGetValue(tx.Id, out StoreAnnotationRevisionEntry? ann);

            StoreTransactionObservationEntry? obs = null;
            if (rev is not null)
            {
                obsMap.TryGetValue(rev.ObservationId, out obs);
            }

            pendingToBooked.TryGetValue(tx.Id, out ulong linkedBookedId);
            bookedToPending.TryGetValue(tx.Id, out ulong linkedPendingId);

            decimal amount = 0m;
            if (rev is not null && !string.IsNullOrWhiteSpace(rev.Amount))
            {
                _ = decimal.TryParse(rev.Amount, NumberStyles.Number, CultureInfo.InvariantCulture, out amount);
            }

            string currency = rev?.Currency ?? "EUR";
            bool isCredit = rev?.IsCredit ?? false;
            DateOnly bookingDate = rev?.BookingDate ?? DateOnly.FromDateTime(DateTime.UtcNow);
            DateOnly? valueDate = rev?.ValueDate;
            DateTimeOffset? bankTimestamp = rev?.BankTimestamp;
            DateTimeOffset retrievedAt = rev?.RetrievedAt ?? obs?.RetrievedAt ?? DateTimeOffset.UtcNow;

            records.Add(new TransactionItemRecord(
                TransactionId: tx.Id,
                AccountId: tx.AccountId,
                AccountDisplayName: accInfo.DisplayName ?? $"Account #{tx.AccountId}",
                AccountIban: accInfo.Iban,
                AccountIncarnationId: tx.AccountIncarnationId,
                AccountBindingId: rev?.AccountBindingId ?? obs?.AccountBindingId ?? 0UL,
                Status: tx.Status,
                Kind: tx.Kind,
                Amount: amount,
                Currency: currency,
                IsCredit: isCredit,
                BookingDate: bookingDate,
                ValueDate: valueDate,
                BankTimestamp: bankTimestamp,
                RetrievedAt: retrievedAt,
                CounterpartyName: rev?.CounterpartyName,
                CounterpartyIban: rev?.CounterpartyIban,
                CounterpartyBic: rev?.CounterpartyBic,
                RemittanceText: rev?.RemittanceText,
                EndToEndId: rev?.EndToEndId,
                MandateId: rev?.MandateId,
                CreditorId: rev?.CreditorId,
                CustomerReference: rev?.CustomerReference,
                BankReference: rev?.BankReference,
                PurposeCode: rev?.PurposeCode,
                ReturnReasonCode: rev?.ReturnReasonCode,
                RawBankCode: rev?.RawBankCode,
                UserNote: ann?.UserNote,
                Category: ann?.Category,
                Tags: ann?.Tags ?? Array.Empty<string>(),
                StatementFormat: obs?.StatementFormat,
                StatementNumber: obs?.StatementNumber,
                LinkedPendingTransactionId: linkedPendingId != 0 ? linkedPendingId : null,
                LinkedBookedTransactionId: linkedBookedId != 0 ? linkedBookedId : null,
                Splits: ann?.Splits?.Select(s => new TransactionSplitItem(s.Index, s.Amount, s.Category, s.Note, s.Tags)).ToList()));
        }

        return new TransactionSearchEngine(records);
    }
}
