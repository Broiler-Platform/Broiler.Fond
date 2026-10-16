using System.Collections.ObjectModel;
using Broiler.Fond.Kernel.Domain;

namespace Broiler.Fond.Kernel.Formats.Mt940;

/// <summary>
/// A single statement entry parsed from SWIFT MT940 Field 61 and Field 86.
/// </summary>
public sealed class Mt940Entry
{
    public Mt940Entry(
        DateOnly valueDate,
        DateOnly bookingDate,
        bool isCredit,
        bool isReversal,
        Money amount,
        string transactionCode,
        string? customerReference,
        string? bankReference,
        string remittanceText,
        IReadOnlyDictionary<string, string> structuredSubfields)
    {
        ValueDate = valueDate;
        BookingDate = bookingDate;
        IsCredit = isCredit;
        IsReversal = isReversal;
        Amount = amount;
        TransactionCode = transactionCode ?? string.Empty;
        CustomerReference = customerReference;
        BankReference = bankReference;
        RemittanceText = remittanceText ?? string.Empty;
        StructuredSubfields = new ReadOnlyDictionary<string, string>(
            structuredSubfields != null
                ? new Dictionary<string, string>(structuredSubfields, StringComparer.Ordinal)
                : []);
    }

    public DateOnly ValueDate { get; }
    public DateOnly BookingDate { get; }
    public bool IsCredit { get; }
    public bool IsReversal { get; }
    public Money Amount { get; }
    public string TransactionCode { get; }
    public string? CustomerReference { get; }
    public string? BankReference { get; }
    public string RemittanceText { get; }
    public ReadOnlyDictionary<string, string> StructuredSubfields { get; }

    /// <summary>Counterparty Name from SEPA subfield ?32/?33 if available.</summary>
    public string? CounterpartyName
    {
        get
        {
            StructuredSubfields.TryGetValue("32", out string? name1);
            StructuredSubfields.TryGetValue("33", out string? name2);
            if (string.IsNullOrWhiteSpace(name1))
            {
                return string.IsNullOrWhiteSpace(name2) ? null : name2.Trim();
            }

            return string.IsNullOrWhiteSpace(name2) ? name1.Trim() : $"{name1.Trim()} {name2.Trim()}";
        }
    }

    /// <summary>Counterparty IBAN from SEPA subfield ?31 if available.</summary>
    public string? CounterpartyIban
    {
        get
        {
            if (StructuredSubfields.TryGetValue("31", out string? iban) && !string.IsNullOrWhiteSpace(iban))
            {
                return iban.Trim();
            }

            return null;
        }
    }

    /// <summary>Counterparty BIC from SEPA subfield ?30 if available.</summary>
    public string? CounterpartyBic
    {
        get
        {
            if (StructuredSubfields.TryGetValue("30", out string? bic) && !string.IsNullOrWhiteSpace(bic))
            {
                return bic.Trim();
            }

            return null;
        }
    }

    /// <summary>SEPA End-to-End Reference from subfield ?21 (or ?20) if available.</summary>
    public string? EndToEndId
    {
        get
        {
            if (StructuredSubfields.TryGetValue("21", out string? e2e) && !string.IsNullOrWhiteSpace(e2e))
            {
                return e2e.Trim();
            }

            return null;
        }
    }

    /// <summary>SEPA Mandate ID from subfield ?34 if available.</summary>
    public string? MandateId
    {
        get
        {
            if (StructuredSubfields.TryGetValue("34", out string? mandate) && !string.IsNullOrWhiteSpace(mandate))
            {
                return mandate.Trim();
            }

            return null;
        }
    }

    /// <summary>SEPA Creditor ID from subfield ?35 if available.</summary>
    public string? CreditorId
    {
        get
        {
            if (StructuredSubfields.TryGetValue("35", out string? credId) && !string.IsNullOrWhiteSpace(credId))
            {
                return credId.Trim();
            }

            return null;
        }
    }
}

/// <summary>
/// Parsed balance snapshot from SWIFT MT940 Field 60F/60M or 62F/62M.
/// </summary>
public sealed class Mt940Balance
{
    public Mt940Balance(
        bool isCredit,
        DateOnly date,
        Money amount,
        string balanceType)
    {
        IsCredit = isCredit;
        Date = date;
        Amount = amount;
        BalanceType = balanceType;
    }

    public bool IsCredit { get; }
    public DateOnly Date { get; }
    public Money Amount { get; }
    /// <summary>"60F", "60M", "62F", "62M", etc.</summary>
    public string BalanceType { get; }
}

/// <summary>
/// A complete parsed SWIFT MT940 / MT942 statement document.
/// </summary>
public sealed class Mt940Statement
{
    public Mt940Statement(
        string transactionReference,
        string? relatedReference,
        string accountIdentification,
        string statementNumber,
        string? sequenceNumber,
        Mt940Balance openingBalance,
        Mt940Balance? closingBalance,
        IReadOnlyList<Mt940Entry> entries)
    {
        TransactionReference = transactionReference ?? string.Empty;
        RelatedReference = relatedReference;
        AccountIdentification = accountIdentification ?? string.Empty;
        StatementNumber = statementNumber ?? string.Empty;
        SequenceNumber = sequenceNumber;
        OpeningBalance = openingBalance ?? throw new ArgumentNullException(nameof(openingBalance));
        ClosingBalance = closingBalance;
        Entries = new ReadOnlyCollection<Mt940Entry>(entries != null ? [.. entries] : []);
    }

    public string TransactionReference { get; }
    public string? RelatedReference { get; }
    public string AccountIdentification { get; }
    public string StatementNumber { get; }
    public string? SequenceNumber { get; }
    public Mt940Balance OpeningBalance { get; }
    public Mt940Balance? ClosingBalance { get; }
    public ReadOnlyCollection<Mt940Entry> Entries { get; }
}
