using System.Drawing;
using System.Globalization;
using Broiler.Fond.Kernel.Domain.Transactions.Query;

namespace Broiler.Fond.Host.Windows.Models;

/// <summary>
/// Accessible, truthful presentation model for a transaction item.
/// Implements high-DPI scaling, non-color status badges, and exact decimal money display.
/// </summary>
public sealed class TransactionViewModel
{
    private static readonly CultureInfo DeCulture = CultureInfo.GetCultureInfo("de-DE");

    public TransactionViewModel(TransactionItemRecord record)
    {
        Record = record ?? throw new ArgumentNullException(nameof(record));
    }

    public TransactionItemRecord Record { get; }

    public ulong TransactionId => Record.TransactionId;
    public ulong AccountId => Record.AccountId;
    public string AccountDisplay => Record.AccountDisplayName;
    public string? AccountIban => Record.AccountIban;

    public DateOnly BookingDate => Record.BookingDate;
    public string BookingDateDisplay => Record.BookingDate.ToString("dd.MM.yyyy", CultureInfo.InvariantCulture);
    public string DateDisplay => BookingDateDisplay;

    public DateOnly? ValueDate => Record.ValueDate;
    public string ValueDateDisplay => Record.ValueDate.HasValue
        ? Record.ValueDate.Value.ToString("dd.MM.yyyy", CultureInfo.InvariantCulture)
        : "--";

    public string Status => Record.Status;
    public bool IsBooked => string.Equals(Record.Status, "Booked", StringComparison.OrdinalIgnoreCase);
    public bool IsPending => string.Equals(Record.Status, "Pending", StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Accessible status display with non-color indicator prefix.
    /// </summary>
    public string StatusDisplay => IsBooked ? "[✓ Booked]" : (IsPending ? "[⏳ Pending]" : $"[{Record.Status}]");

    public string Kind => Record.Kind;

    public decimal Amount => Record.Amount;
    public string Currency => Record.Currency;
    public bool IsCredit => Record.IsCredit;

    /// <summary>
    /// Exact decimal amount display with non-color sign indicator and German number format.
    /// </summary>
    public string AmountDisplay
    {
        get
        {
            string formattedAmount = Record.Amount.ToString("N2", DeCulture);
            string sign = Record.IsCredit ? "[+] +" : "[-] -";
            return $"{sign}{formattedAmount} {Record.Currency}";
        }
    }

    /// <summary>
    /// Accessible semantic color for Windows Forms high-contrast / normal rendering.
    /// </summary>
    public Color AmountColor => Record.IsCredit ? Color.DarkGreen : Color.DarkRed;

    public string? CounterpartyName => Record.CounterpartyName;
    public string CounterpartyDisplay => !string.IsNullOrWhiteSpace(Record.CounterpartyName)
        ? Record.CounterpartyName
        : (Record.IsCredit ? "(Payer Not Disclosed)" : "(Payee Not Disclosed)");

    public string? CounterpartyIban => Record.CounterpartyIban;
    public string? CounterpartyBic => Record.CounterpartyBic;

    public string? RemittanceText => Record.RemittanceText;
    public string RemittanceSummary
    {
        get
        {
            if (string.IsNullOrWhiteSpace(Record.RemittanceText))
            {
                return "--";
            }

            string firstLine = Record.RemittanceText.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries).FirstOrDefault() ?? string.Empty;
            return firstLine.Length > 60 ? string.Concat(firstLine.AsSpan(0, 57), "...") : firstLine;
        }
    }

    public string? EndToEndId => Record.EndToEndId;
    public string? MandateId => Record.MandateId;
    public string? CreditorId => Record.CreditorId;
    public string? CustomerReference => Record.CustomerReference;
    public string? BankReference => Record.BankReference;
    public string? PurposeCode => Record.PurposeCode;
    public string? ReturnReasonCode => Record.ReturnReasonCode;
    public string? RawBankCode => Record.RawBankCode;

    public string? UserNote => Record.UserNote;
    public string? Category => Record.Category;
    public string CategoryDisplay => !string.IsNullOrWhiteSpace(Record.Category) ? Record.Category : "--";

    public IReadOnlyList<string> Tags => Record.Tags;
    public string TagsDisplay => Record.Tags is { Count: > 0 } ? string.Join(", ", Record.Tags) : "--";

    public string? StatementFormat => Record.StatementFormat;
    public string? StatementNumber => Record.StatementNumber;
    public DateTimeOffset RetrievedAt => Record.RetrievedAt;
    public string RetrievedAtDisplay => Record.RetrievedAt.ToLocalTime().ToString("dd.MM.yyyy HH:mm:ss", CultureInfo.InvariantCulture);

    public ulong? LinkedPendingTransactionId => Record.LinkedPendingTransactionId;
    public ulong? LinkedBookedTransactionId => Record.LinkedBookedTransactionId;
}
