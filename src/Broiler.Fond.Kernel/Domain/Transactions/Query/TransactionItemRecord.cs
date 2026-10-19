using System.Globalization;
using System.Text;

namespace Broiler.Fond.Kernel.Domain.Transactions.Query;

/// <summary>
/// Unified, immutable projected view of a financial transaction correlating
/// its economic transaction entity, latest bank-fact revision, latest user annotations,
/// source observation provenance, and account details.
/// </summary>
public sealed record TransactionItemRecord(
    ulong TransactionId,
    ulong AccountId,
    string AccountDisplayName,
    string? AccountIban,
    ulong AccountIncarnationId,
    ulong AccountBindingId,
    string Status,
    string Kind,
    decimal Amount,
    string Currency,
    bool IsCredit,
    DateOnly BookingDate,
    DateOnly? ValueDate,
    DateTimeOffset? BankTimestamp,
    DateTimeOffset RetrievedAt,
    string? CounterpartyName,
    string? CounterpartyIban,
    string? CounterpartyBic,
    string? RemittanceText,
    string? EndToEndId,
    string? MandateId,
    string? CreditorId,
    string? CustomerReference,
    string? BankReference,
    string? PurposeCode,
    string? ReturnReasonCode,
    string? RawBankCode,
    string? UserNote,
    string? Category,
    IReadOnlyList<string> Tags,
    string? StatementFormat,
    string? StatementNumber,
    ulong? LinkedPendingTransactionId,
    ulong? LinkedBookedTransactionId,
    IReadOnlyList<TransactionSplitItem>? Splits = null)
{
    private string? _searchCorpus;

    /// <summary>
    /// Gets whether this transaction has user-defined split line items.
    /// </summary>
    public bool IsSplit => Splits is not null && Splits.Count > 1;

    /// <summary>
    /// Gets the pre-computed or lazily evaluated lowercase normalized search corpus containing
    /// all user-searchable text fields for fast substring and token matching.
    /// </summary>
    public string SearchCorpus => _searchCorpus ??= BuildSearchCorpus();

    private string BuildSearchCorpus()
    {
        StringBuilder sb = new();
        void Append(string? val)
        {
            if (!string.IsNullOrWhiteSpace(val))
            {
                sb.Append(val).Append(' ');
            }
        }

        Append(AccountDisplayName);
        Append(AccountIban);
        Append(Status);
        Append(Kind);
        Append(Amount.ToString(CultureInfo.InvariantCulture));
        Append(Currency);
        Append(CounterpartyName);
        Append(CounterpartyIban);
        Append(CounterpartyBic);
        Append(RemittanceText);
        Append(EndToEndId);
        Append(MandateId);
        Append(CreditorId);
        Append(CustomerReference);
        Append(BankReference);
        Append(PurposeCode);
        Append(ReturnReasonCode);
        Append(RawBankCode);
        Append(UserNote);
        Append(Category);
        if (Tags is not null)
        {
            foreach (string tag in Tags)
            {
                Append(tag);
            }
        }

        if (Splits is not null)
        {
            foreach (var split in Splits)
            {
                Append(split.Category);
                Append(split.Note);
                if (split.Tags is not null)
                {
                    foreach (string splitTag in split.Tags)
                    {
                        Append(splitTag);
                    }
                }
            }
        }

        return sb.ToString().ToLowerInvariant();
    }
}
