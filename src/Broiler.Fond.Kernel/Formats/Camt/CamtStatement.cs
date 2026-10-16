using System.Collections.ObjectModel;
using Broiler.Fond.Kernel.Domain;
using Broiler.Fond.Kernel.Domain.Transactions;

namespace Broiler.Fond.Kernel.Formats.Camt;

/// <summary>
/// A single transaction entry parsed from ISO 20022 CAMT XML (Ntry / TxDtls).
/// </summary>
public sealed class CamtEntry
{
    public CamtEntry(
        string? entryReference,
        Money amount,
        bool isCredit,
        TransactionStatus status,
        DateOnly bookingDate,
        DateOnly valueDate,
        string? bankTransactionCode,
        string? endToEndId,
        string? mandateId,
        string? creditorId,
        string? counterpartyName,
        string? counterpartyIban,
        string? counterpartyBic,
        string remittanceText,
        string? returnReasonCode)
    {
        EntryReference = entryReference;
        Amount = amount;
        IsCredit = isCredit;
        Status = status;
        BookingDate = bookingDate;
        ValueDate = valueDate;
        BankTransactionCode = bankTransactionCode;
        EndToEndId = endToEndId;
        MandateId = mandateId;
        CreditorId = creditorId;
        CounterpartyName = counterpartyName;
        CounterpartyIban = counterpartyIban;
        CounterpartyBic = counterpartyBic;
        RemittanceText = remittanceText ?? string.Empty;
        ReturnReasonCode = returnReasonCode;
    }

    public string? EntryReference { get; }
    public Money Amount { get; }
    public bool IsCredit { get; }
    public TransactionStatus Status { get; }
    public DateOnly BookingDate { get; }
    public DateOnly ValueDate { get; }
    public string? BankTransactionCode { get; }
    public string? EndToEndId { get; }
    public string? MandateId { get; }
    public string? CreditorId { get; }
    public string? CounterpartyName { get; }
    public string? CounterpartyIban { get; }
    public string? CounterpartyBic { get; }
    public string RemittanceText { get; }
    public string? ReturnReasonCode { get; }
}

/// <summary>
/// A balance snapshot from ISO 20022 CAMT XML (Bal element).
/// </summary>
public sealed class CamtBalance
{
    public CamtBalance(
        string balanceType,
        Money amount,
        bool isCredit,
        DateOnly date)
    {
        BalanceType = balanceType ?? string.Empty;
        Amount = amount;
        IsCredit = isCredit;
        Date = date;
    }

    /// <summary>e.g. "OPBD" (Opening), "CLBD" (Closing), "PRCD" (Previously Closed), "ITBD" (Interim).</summary>
    public string BalanceType { get; }
    public Money Amount { get; }
    public bool IsCredit { get; }
    public DateOnly Date { get; }
}

/// <summary>
/// A complete parsed ISO 20022 CAMT statement or account report (camt.052 or camt.053).
/// </summary>
public sealed class CamtStatement
{
    public CamtStatement(
        string messageId,
        DateTimeOffset creationDateTime,
        string statementId,
        string accountIban,
        string accountCurrency,
        CamtBalance? openingBalance,
        CamtBalance? closingBalance,
        IReadOnlyList<CamtEntry> entries)
    {
        MessageId = messageId ?? string.Empty;
        CreationDateTime = creationDateTime;
        StatementId = statementId ?? string.Empty;
        AccountIban = accountIban ?? string.Empty;
        AccountCurrency = accountCurrency ?? string.Empty;
        OpeningBalance = openingBalance;
        ClosingBalance = closingBalance;
        Entries = new ReadOnlyCollection<CamtEntry>(entries != null ? [.. entries] : []);
    }

    public string MessageId { get; }
    public DateTimeOffset CreationDateTime { get; }
    public string StatementId { get; }
    public string AccountIban { get; }
    public string AccountCurrency { get; }
    public CamtBalance? OpeningBalance { get; }
    public CamtBalance? ClosingBalance { get; }
    public ReadOnlyCollection<CamtEntry> Entries { get; }
}
