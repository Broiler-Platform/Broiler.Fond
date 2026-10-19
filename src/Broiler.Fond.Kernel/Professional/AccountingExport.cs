using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Broiler.Fond.Kernel.Domain;

namespace Broiler.Fond.Kernel.Professional;

/// <summary>
/// Standard German charts of accounts (Kontenrahmen).
/// </summary>
public enum ChartOfAccounts
{
    /// <summary>SKR03 (process-oriented chart of accounts)</summary>
    SKR03 = 0,

    /// <summary>SKR04 (balance-sheet-oriented chart of accounts)</summary>
    SKR04 = 1
}

public static class ChartOfAccountsExtensions
{
    /// <summary>
    /// Default cash transit / bank account number.
    /// SKR03: 1200 (Bank)
    /// SKR04: 1800 (Bank)
    /// </summary>
    public static int GetDefaultBankAccount(this ChartOfAccounts chart) => chart switch
    {
        ChartOfAccounts.SKR03 => 1200,
        ChartOfAccounts.SKR04 => 1800,
        _ => 1200
    };
}

/// <summary>
/// DATEV Debit/Credit indicator.
/// </summary>
public enum DatevDebitCreditIndicator
{
    /// <summary>Debit (Soll) — outgoing money movement</summary>
    Debit = 'S',

    /// <summary>Credit (Haben) — incoming money movement</summary>
    Credit = 'H'
}

/// <summary>
/// An individual posting line in the DATEV format.
/// </summary>
public sealed record DatevBookingRecord
{
    public Money Amount { get; }
    public DatevDebitCreditIndicator DebitCredit { get; }
    public int Account { get; }
    public int ContraAccount { get; }
    public string? PostingKey { get; }
    public DateOnly ReceiptDate { get; }
    public string? ReceiptField1 { get; }
    public string PostingText { get; }

    public DatevBookingRecord(
        Money amount,
        DatevDebitCreditIndicator debitCredit,
        int account,
        int contraAccount,
        DateOnly receiptDate,
        string postingText,
        string? postingKey = null,
        string? receiptField1 = null)
    {
        ArgumentNullException.ThrowIfNull(amount);
        ArgumentException.ThrowIfNullOrWhiteSpace(postingText);

        if (amount.Amount <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(amount), "DATEV record amount must be strictly positive (Debit/Credit indicator indicates direction).");
        }

        if (account <= 0 || account > 99999999)
        {
            throw new ArgumentOutOfRangeException(nameof(account), "Account number must be a positive integer.");
        }

        if (contraAccount <= 0 || contraAccount > 99999999)
        {
            throw new ArgumentOutOfRangeException(nameof(contraAccount), "Contra-account number must be a positive integer.");
        }

        Amount = amount;
        DebitCredit = debitCredit;
        Account = account;
        ContraAccount = contraAccount;
        PostingKey = postingKey?.Trim();
        ReceiptDate = receiptDate;
        ReceiptField1 = receiptField1?.Trim();
        PostingText = SanitizePostingText(postingText);
    }

    private static string SanitizePostingText(string text)
    {
        string cleaned = text.Trim().Replace(";", " ").Replace("\"", "'");
        return cleaned.Length <= 60 ? cleaned : cleaned[..60];
    }
}

/// <summary>
/// Configuration for the standardized DATEV file header line.
/// </summary>
public sealed record DatevHeaderConfig
{
    public int ConsultantNumber { get; }
    public int ClientNumber { get; }
    public DateOnly FiscalYearStart { get; }
    public int AccountLength { get; }
    public string BatchName { get; }

    public DatevHeaderConfig(
        int consultantNumber,
        int clientNumber,
        DateOnly fiscalYearStart,
        string batchName,
        int accountLength = 4)
    {
        if (consultantNumber < 1000 || consultantNumber > 9999999)
        {
            throw new ArgumentOutOfRangeException(nameof(consultantNumber), "Consultant number (Beraternummer) must be between 1000 and 9999999.");
        }

        if (clientNumber < 1 || clientNumber > 99999)
        {
            throw new ArgumentOutOfRangeException(nameof(clientNumber), "Client number (Mandantennummer) must be between 1 and 99999.");
        }

        if (accountLength < 4 || accountLength > 8)
        {
            throw new ArgumentOutOfRangeException(nameof(accountLength), "Account length must be between 4 and 8 digits.");
        }

        ArgumentException.ThrowIfNullOrWhiteSpace(batchName);

        ConsultantNumber = consultantNumber;
        ClientNumber = clientNumber;
        FiscalYearStart = fiscalYearStart;
        BatchName = batchName.Trim();
        AccountLength = accountLength;
    }
}

/// <summary>
/// Generator for official DATEV-Format CSV / ASCII Buchungsstapel (Version 700).
/// </summary>
public static class DatevExportGenerator
{
    private static readonly CultureInfo GermanCulture = CultureInfo.GetCultureInfo("de-DE");

    /// <summary>
    /// Generates a standardized DATEV CSV export (EXTF 700 Buchungsstapel) using pure BCL formatting.
    /// </summary>
    public static string GenerateDatevCsv(
        DatevHeaderConfig headerConfig,
        IEnumerable<DatevBookingRecord> records,
        DateTimeOffset exportTimestampUtc)
    {
        ArgumentNullException.ThrowIfNull(headerConfig);
        ArgumentNullException.ThrowIfNull(records);

        var recordList = records.ToArray();
        DateOnly dateFrom = recordList.Length > 0 ? recordList.Min(r => r.ReceiptDate) : headerConfig.FiscalYearStart;
        DateOnly dateTo = recordList.Length > 0 ? recordList.Max(r => r.ReceiptDate) : headerConfig.FiscalYearStart;

        var sb = new StringBuilder();

        // 1. DATEV EXTF Header Line (Category 21 = Buchungsstapel, Version 700)
        sb.Append("\"EXTF\";700;21;\"Buchungsstapel\";1;");
        sb.Append(exportTimestampUtc.ToString("yyyyMMddHHmmssfff", CultureInfo.InvariantCulture));
        sb.Append(";\"\";\"Broiler.Fond\";\"\";\"\";");
        sb.Append(headerConfig.ConsultantNumber.ToString(CultureInfo.InvariantCulture)).Append(';');
        sb.Append(headerConfig.ClientNumber.ToString(CultureInfo.InvariantCulture)).Append(';');
        sb.Append(headerConfig.FiscalYearStart.ToString("yyyyMMdd", CultureInfo.InvariantCulture)).Append(';');
        sb.Append(headerConfig.AccountLength.ToString(CultureInfo.InvariantCulture)).Append(';');
        sb.Append("\"\";\"\";");
        sb.Append(dateFrom.ToString("yyyyMMdd", CultureInfo.InvariantCulture)).Append(';');
        sb.Append(dateTo.ToString("yyyyMMdd", CultureInfo.InvariantCulture)).Append(';');
        sb.Append('"').Append(headerConfig.BatchName.Replace("\"", string.Empty)).Append("\";\"\";1;\"EUR\";\"\";\"\";\"\";\"\";\"\";\"\";\"\"");
        sb.AppendLine();

        // 2. Column Headers
        sb.AppendLine("\"Umsatz (ohne Soll/Haben-Kz)\";\"Soll/Haben-Kennzeichen\";\"WKZ\";\"Konto\";\"Gegenkonto (ohne BU-Schlüssel)\";\"BU-Schlüssel\";\"Belegdatum\";\"Belegfeld 1\";\"Buchungstext\"");

        // 3. Data Rows
        foreach (var record in recordList)
        {
            // Amount in German format with comma (e.g. 1250,50)
            string amountStr = record.Amount.Amount.ToString("0.00", GermanCulture);
            string shStr = ((char)record.DebitCredit).ToString();
            string receiptDateStr = record.ReceiptDate.ToString("ddMM", CultureInfo.InvariantCulture);

            sb.Append('"').Append(amountStr).Append("\";");
            sb.Append('"').Append(shStr).Append("\";");
            sb.Append('"').Append(record.Amount.Currency).Append("\";");
            sb.Append(record.Account.ToString(CultureInfo.InvariantCulture)).Append(';');
            sb.Append(record.ContraAccount.ToString(CultureInfo.InvariantCulture)).Append(';');
            sb.Append('"').Append(record.PostingKey ?? string.Empty).Append("\";");
            sb.Append('"').Append(receiptDateStr).Append("\";");
            sb.Append('"').Append(record.ReceiptField1 ?? string.Empty).Append("\";");
            sb.Append('"').Append(record.PostingText).Append('"');
            sb.AppendLine();
        }

        return sb.ToString();
    }

    /// <summary>
    /// Helper to map a bank transaction to a DATEV record based on chart of accounts conventions.
    /// </summary>
    public static DatevBookingRecord MapBankTransaction(
        DateOnly bookingDate,
        Money amount,
        string description,
        ChartOfAccounts chart,
        int expenseOrRevenueAccount,
        string? taxKey = null,
        string? reference = null)
    {
        ArgumentNullException.ThrowIfNull(amount);
        int bankAccount = chart.GetDefaultBankAccount();

        if (amount.Amount < 0)
        {
            // Outgoing debit: Soll = Expense/Vendor account, Haben = Bank account
            return new DatevBookingRecord(
                new Money(Math.Abs(amount.Amount), amount.Currency),
                DatevDebitCreditIndicator.Debit,
                expenseOrRevenueAccount,
                bankAccount,
                bookingDate,
                description,
                taxKey,
                reference);
        }
        else
        {
            // Incoming credit: Soll = Bank account, Haben = Revenue/Customer account
            return new DatevBookingRecord(
                new Money(amount.Amount, amount.Currency),
                DatevDebitCreditIndicator.Credit,
                bankAccount,
                expenseOrRevenueAccount,
                bookingDate,
                description,
                taxKey,
                reference);
        }
    }
}

/// <summary>
/// Cryptographic manifest item representing an exported file in an enterprise audit bundle.
/// </summary>
public sealed record AuditManifestItem(
    string FileName,
    string Sha256Hash,
    long ByteCount);

/// <summary>
/// Exporter and validator for tamper-evident enterprise audit bundles.
/// </summary>
public static class EnterpriseAuditExporter
{
    public static string CreateManifestJson(
        IEnumerable<(string FileName, byte[] Content)> components,
        DateTimeOffset exportTimestampUtc)
    {
        ArgumentNullException.ThrowIfNull(components);

        var items = new List<AuditManifestItem>();
        foreach (var (fileName, content) in components)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(fileName);
            ArgumentNullException.ThrowIfNull(content);

            string hash = Convert.ToHexString(SHA256.HashData(content)).ToLowerInvariant();
            items.Add(new AuditManifestItem(fileName, hash, content.LongLength));
        }

        var sb = new StringBuilder();
        sb.AppendLine("{");
        sb.AppendLine("  \"Product\": \"Broiler.Fond.Professional\",");
        sb.AppendLine($"  \"ExportedAtUtc\": \"{exportTimestampUtc:O}\",");
        sb.AppendLine($"  \"FileCount\": {items.Count},");
        sb.AppendLine("  \"Files\": [");
        for (int i = 0; i < items.Count; i++)
        {
            var item = items[i];
            sb.Append("    { ");
            sb.Append($"\"FileName\": \"{item.FileName}\", ");
            sb.Append($"\"Sha256Hash\": \"{item.Sha256Hash}\", ");
            sb.Append($"\"ByteCount\": {item.ByteCount}");
            sb.Append(" }");
            if (i < items.Count - 1)
            {
                sb.Append(',');
            }
            sb.AppendLine();
        }
        sb.AppendLine("  ]");
        sb.Append('}');

        return sb.ToString();
    }

    public static bool VerifyManifest(
        string manifestJson,
        IReadOnlyDictionary<string, byte[]> fileMap,
        out string? failureReason)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(manifestJson);
        ArgumentNullException.ThrowIfNull(fileMap);

        using var doc = JsonDocument.Parse(manifestJson);
        var filesElem = doc.RootElement.GetProperty("Files");

        foreach (var fileElem in filesElem.EnumerateArray())
        {
            string fileName = fileElem.GetProperty("FileName").GetString()!;
            string expectedHash = fileElem.GetProperty("Sha256Hash").GetString()!;
            long expectedByteCount = fileElem.GetProperty("ByteCount").GetInt64();

            if (!fileMap.TryGetValue(fileName, out byte[]? actualContent))
            {
                failureReason = $"Missing manifest file in audit bundle: '{fileName}'.";
                return false;
            }

            if (actualContent.LongLength != expectedByteCount)
            {
                failureReason = $"Byte count mismatch for '{fileName}': expected {expectedByteCount}, found {actualContent.LongLength}.";
                return false;
            }

            string actualHash = Convert.ToHexString(SHA256.HashData(actualContent)).ToLowerInvariant();
            if (!string.Equals(actualHash, expectedHash, StringComparison.OrdinalIgnoreCase))
            {
                failureReason = $"Cryptographic hash verification failed for '{fileName}': expected {expectedHash}, found {actualHash}.";
                return false;
            }
        }

        failureReason = null;
        return true;
    }
}

/// <summary>
/// Organization-level key recovery policy separating operational keys from corporate recovery escrow.
/// </summary>
public sealed record OrganizationKeyRecoveryPolicy
{
    public bool RequiresEscrowRecovery { get; }
    public string RecoveryKeyIdentifier { get; }
    public ProfessionalRole EscrowAuthorizedRole { get; }

    public OrganizationKeyRecoveryPolicy(
        bool requiresEscrowRecovery,
        string recoveryKeyIdentifier,
        ProfessionalRole escrowAuthorizedRole = ProfessionalRole.Administrator)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(recoveryKeyIdentifier);

        // Strict security rule: Only Administrator can be designated for escrow recovery authorization
        if (escrowAuthorizedRole != ProfessionalRole.Administrator)
        {
            throw new InvalidOperationException($"Only '{ProfessionalRole.Administrator}' may be designated for organization escrow key recovery. Role '{escrowAuthorizedRole}' is forbidden.");
        }

        RequiresEscrowRecovery = requiresEscrowRecovery;
        RecoveryKeyIdentifier = recoveryKeyIdentifier.Trim();
        EscrowAuthorizedRole = escrowAuthorizedRole;
    }
}
