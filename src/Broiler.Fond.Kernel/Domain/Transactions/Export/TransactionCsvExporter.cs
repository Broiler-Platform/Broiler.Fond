using System.Globalization;
using System.Text;
using Broiler.Fond.Kernel.Domain.Transactions.Query;

namespace Broiler.Fond.Kernel.Domain.Transactions.Export;

/// <summary>
/// Privacy-conscious, RFC 4180-compliant CSV exporter for transaction ledgers.
/// Guarantees exact decimal money formatting, proper character escaping,
/// zero persistence of secrets (PINs/TANs), and zero network telemetry.
/// </summary>
public static class TransactionCsvExporter
{
    private static readonly string[] Headers =
    [
        "BookingDate",
        "ValueDate",
        "Status",
        "Account",
        "AccountIban",
        "CounterpartyName",
        "CounterpartyIban",
        "CounterpartyBic",
        "Amount",
        "Currency",
        "Direction",
        "RemittanceText",
        "EndToEndId",
        "MandateId",
        "CreditorId",
        "CustomerReference",
        "BankReference",
        "PurposeCode",
        "ReturnReasonCode",
        "RawBankCode",
        "Category",
        "Tags",
        "UserNote",
    ];

    /// <summary>
    /// Writes the exported transactions as RFC 4180 CSV to the provided <see cref="TextWriter"/>.
    /// </summary>
    public static void ExportToCsv(IEnumerable<TransactionItemRecord> items, TextWriter writer)
    {
        ArgumentNullException.ThrowIfNull(items);
        ArgumentNullException.ThrowIfNull(writer);

        // Write header row
        WriteLine(writer, Headers);

        foreach (TransactionItemRecord item in items)
        {
            string tagsStr = item.Tags is { Count: > 0 } ? string.Join(";", item.Tags) : string.Empty;
            string valueDateStr = item.ValueDate.HasValue ? item.ValueDate.Value.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) : string.Empty;

            string[] row =
            [
                item.BookingDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                valueDateStr,
                item.Status,
                item.AccountDisplayName,
                item.AccountIban ?? string.Empty,
                item.CounterpartyName ?? string.Empty,
                item.CounterpartyIban ?? string.Empty,
                item.CounterpartyBic ?? string.Empty,
                item.Amount.ToString("0.00", CultureInfo.InvariantCulture),
                item.Currency,
                item.IsCredit ? "Credit" : "Debit",
                item.RemittanceText ?? string.Empty,
                item.EndToEndId ?? string.Empty,
                item.MandateId ?? string.Empty,
                item.CreditorId ?? string.Empty,
                item.CustomerReference ?? string.Empty,
                item.BankReference ?? string.Empty,
                item.PurposeCode ?? string.Empty,
                item.ReturnReasonCode ?? string.Empty,
                item.RawBankCode ?? string.Empty,
                item.Category ?? string.Empty,
                tagsStr,
                item.UserNote ?? string.Empty,
            ];

            WriteLine(writer, row);
        }
    }

    /// <summary>
    /// Exports the transactions to a self-contained RFC 4180 CSV string.
    /// </summary>
    public static string ExportToCsv(IEnumerable<TransactionItemRecord> items)
    {
        using StringWriter writer = new(CultureInfo.InvariantCulture);
        ExportToCsv(items, writer);
        return writer.ToString();
    }

    /// <summary>
    /// Generates a preview snippet of the CSV export (header plus up to <paramref name="maxRows"/> rows)
    /// alongside the total matching row count.
    /// </summary>
    public static (string PreviewText, int TotalCount) GeneratePreview(
        IEnumerable<TransactionItemRecord> items,
        int maxRows = 5)
    {
        ArgumentNullException.ThrowIfNull(items);
        if (maxRows < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(maxRows), "Maximum preview rows must be at least 1.");
        }

        List<TransactionItemRecord> list = items.ToList();
        using StringWriter writer = new(CultureInfo.InvariantCulture);
        ExportToCsv(list.Take(maxRows), writer);

        return (writer.ToString(), list.Count);
    }

    private static void WriteLine(TextWriter writer, string[] fields)
    {
        for (int i = 0; i < fields.Length; i++)
        {
            if (i > 0)
            {
                writer.Write(',');
            }

            writer.Write(EscapeField(fields[i]));
        }

        writer.Write("\r\n");
    }

    /// <summary>
    /// Escapes a CSV field according to RFC 4180 rules.
    /// Fields containing comma, quote, or newline are wrapped in double quotes, with internal quotes doubled.
    /// </summary>
    public static string EscapeField(string? field)
    {
        if (string.IsNullOrEmpty(field))
        {
            return string.Empty;
        }

        bool requiresQuotes = field.Contains(',') ||
                              field.Contains('"') ||
                              field.Contains('\r') ||
                              field.Contains('\n');

        if (!requiresQuotes)
        {
            return field;
        }

        StringBuilder sb = new(field.Length + 10);
        sb.Append('"');
        foreach (char c in field)
        {
            if (c == '"')
            {
                sb.Append("\"\"");
            }
            else
            {
                sb.Append(c);
            }
        }
        sb.Append('"');
        return sb.ToString();
    }
}
