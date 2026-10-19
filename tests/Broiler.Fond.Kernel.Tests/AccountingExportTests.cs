using System.Text;
using Broiler.Fond.Kernel.Domain;
using Broiler.Fond.Kernel.Professional;

namespace Broiler.Fond.Kernel.Tests;

public static class AccountingExportTests
{
    public static void RunAllTests()
    {
        Console.WriteLine("Running Accounting Exports & DATEV Interoperability verification (P-05)...");
        int checks = 0;

        // 1. Chart of accounts defaults
        Assert(ChartOfAccounts.SKR03.GetDefaultBankAccount() == 1200, "SKR03 default bank account is 1200");
        Assert(ChartOfAccounts.SKR04.GetDefaultBankAccount() == 1800, "SKR04 default bank account is 1800");
        checks += 2;

        // 2. DatevBookingRecord invariants
        var receiptDate = new DateOnly(2026, 10, 15);
        var record1 = new DatevBookingRecord(
            new Money(1250.75m, "EUR"),
            DatevDebitCreditIndicator.Debit,
            4900,
            1200,
            receiptDate,
            "Office Supplies & IT Equipment; Invoice #991",
            postingKey: "9",
            receiptField1: "RE-2026-991");

        Assert(record1.Amount.Amount == 1250.75m, "Amount matches");
        Assert(record1.DebitCredit == DatevDebitCreditIndicator.Debit, "DebitCredit is Debit (S)");
        Assert(record1.Account == 4900, "Account matches");
        Assert(record1.ContraAccount == 1200, "ContraAccount matches");
        Assert(!record1.PostingText.Contains(';'), "Posting text must not contain semicolons");
        checks += 5;

        // Negative or zero amount rejected
        bool negativeAmountCaught = false;
        try
        {
            _ = new DatevBookingRecord(new Money(-100.00m, "EUR"), DatevDebitCreditIndicator.Debit, 4000, 1200, receiptDate, "Test");
        }
        catch (ArgumentOutOfRangeException)
        {
            negativeAmountCaught = true;
        }
        Assert(negativeAmountCaught, "Negative amounts in DatevBookingRecord must be rejected");
        checks++;

        // 3. DatevHeaderConfig invariants
        var headerConfig = new DatevHeaderConfig(
            consultantNumber: 10452,
            clientNumber: 99100,
            fiscalYearStart: new DateOnly(2026, 1, 1),
            batchName: "Bank October 2026",
            accountLength: 4);

        Assert(headerConfig.ConsultantNumber == 10452, "Consultant number matches");
        Assert(headerConfig.ClientNumber == 99100, "Client number matches");
        Assert(headerConfig.AccountLength == 4, "Account length matches");
        checks += 3;

        bool invalidConsultantCaught = false;
        try
        {
            _ = new DatevHeaderConfig(12, 100, new DateOnly(2026, 1, 1), "Test");
        }
        catch (ArgumentOutOfRangeException)
        {
            invalidConsultantCaught = true;
        }
        Assert(invalidConsultantCaught, "Invalid consultant number must be rejected");
        checks++;

        // 4. DatevExportGenerator CSV output verification
        var record2 = new DatevBookingRecord(
            new Money(8400.00m, "EUR"),
            DatevDebitCreditIndicator.Credit,
            1200,
            8400,
            new DateOnly(2026, 10, 18),
            "Customer Payment Invoice 4001",
            postingKey: null,
            receiptField1: "RE-4001");

        DateTimeOffset now = DateTimeOffset.UtcNow;
        string datevCsv = DatevExportGenerator.GenerateDatevCsv(headerConfig, [record1, record2], now);

        Assert(datevCsv.StartsWith("\"EXTF\";700;21;\"Buchungsstapel\";1;"), "Must start with EXTF 700 header line");
        Assert(datevCsv.Contains(";10452;99100;20260101;4;"), "Header must include consultant, client, fiscal year, and account length");
        Assert(datevCsv.Contains("\"Umsatz (ohne Soll/Haben-Kz)\";\"Soll/Haben-Kennzeichen\""), "Header line 2 must contain standard column names");

        // Verify German comma decimal formatting
        Assert(datevCsv.Contains("\"1250,75\";\"S\";\"EUR\";4900;1200;\"9\";\"1510\";\"RE-2026-991\""), "Record 1 must format with German comma, ddMM date, and quotes");
        Assert(datevCsv.Contains("\"8400,00\";\"H\";\"EUR\";1200;8400;\"\";\"1810\";\"RE-4001\""), "Record 2 must format with German comma, credit indicator H, and empty tax key");
        checks += 5;

        // 5. Transaction mapping helper
        var mappedDebit = DatevExportGenerator.MapBankTransaction(
            receiptDate,
            new Money(-450.00m, "EUR"),
            "Software License",
            ChartOfAccounts.SKR03,
            4900,
            "9",
            "INV-55");

        Assert(mappedDebit.Amount.Amount == 450.00m, "Mapped debit has positive amount");
        Assert(mappedDebit.DebitCredit == DatevDebitCreditIndicator.Debit, "Mapped debit indicator is Debit (S)");
        Assert(mappedDebit.Account == 4900, "Mapped account is expense account");
        Assert(mappedDebit.ContraAccount == 1200, "Mapped contra-account is SKR03 bank account 1200");
        checks += 4;

        var mappedCredit = DatevExportGenerator.MapBankTransaction(
            receiptDate,
            new Money(3500.00m, "EUR"),
            "Consulting Services",
            ChartOfAccounts.SKR04,
            4400,
            "9",
            "INV-60");

        Assert(mappedCredit.Amount.Amount == 3500.00m, "Mapped credit has positive amount");
        Assert(mappedCredit.DebitCredit == DatevDebitCreditIndicator.Credit, "Mapped credit indicator is Credit (H)");
        Assert(mappedCredit.Account == 1800, "Mapped account is SKR04 bank account 1800");
        Assert(mappedCredit.ContraAccount == 4400, "Mapped contra-account is revenue account 4400");
        checks += 4;

        // 6. EnterpriseAuditExporter manifest creation and verification
        byte[] auditLogBytes = Encoding.UTF8.GetBytes("[{\"Seq\": 1, \"Action\": \"InitiateOrder\"}]");
        byte[] batchBytes = Encoding.UTF8.GetBytes("<pain.001.001.09><GrpHdr>...</GrpHdr></pain.001.001.09>");
        byte[] datevBytes = Encoding.UTF8.GetBytes(datevCsv);

        var components = new List<(string, byte[])>
        {
            ("audit_ledger.json", auditLogBytes),
            ("batch_orders.xml", batchBytes),
            ("datev_export.csv", datevBytes)
        };

        string manifestJson = EnterpriseAuditExporter.CreateManifestJson(components, now);
        Assert(manifestJson.Contains("Broiler.Fond.Professional"), "Manifest includes product identifier");
        Assert(manifestJson.Contains("audit_ledger.json"), "Manifest lists audit ledger");
        Assert(manifestJson.Contains("batch_orders.xml"), "Manifest lists batch orders");
        Assert(manifestJson.Contains("datev_export.csv"), "Manifest lists datev export");
        checks += 4;

        var fileMap = new Dictionary<string, byte[]>
        {
            ["audit_ledger.json"] = auditLogBytes,
            ["batch_orders.xml"] = batchBytes,
            ["datev_export.csv"] = datevBytes
        };

        bool manifestValid = EnterpriseAuditExporter.VerifyManifest(manifestJson, fileMap, out string? verifyFailure);
        Assert(manifestValid && verifyFailure == null, $"Manifest must verify: {verifyFailure}");
        checks++;

        // Tamper detection in audit manifest
        var tamperedFileMap = new Dictionary<string, byte[]>(fileMap)
        {
            ["datev_export.csv"] = Encoding.UTF8.GetBytes(datevCsv + "\nTAMPERED_ROW")
        };
        bool caughtTamperedManifest = !EnterpriseAuditExporter.VerifyManifest(manifestJson, tamperedFileMap, out string? tamperFailure);
        Assert(caughtTamperedManifest && tamperFailure != null && (tamperFailure.Contains("Cryptographic hash verification failed") || tamperFailure.Contains("Byte count mismatch")), "Tampered file in manifest must fail verification");
        checks++;

        // 7. OrganizationKeyRecoveryPolicy
        var policy = new OrganizationKeyRecoveryPolicy(true, "KEY-ESCROW-2026-HQ", ProfessionalRole.Administrator);
        Assert(policy.RequiresEscrowRecovery, "Policy requires escrow recovery");
        Assert(policy.RecoveryKeyIdentifier == "KEY-ESCROW-2026-HQ", "Recovery key identifier matches");
        Assert(policy.EscrowAuthorizedRole == ProfessionalRole.Administrator, "Escrow authorized role is Administrator");
        checks += 3;

        bool invalidRoleCaught = false;
        try
        {
            _ = new OrganizationKeyRecoveryPolicy(true, "KEY-ESCROW", ProfessionalRole.Operator);
        }
        catch (InvalidOperationException)
        {
            invalidRoleCaught = true;
        }
        Assert(invalidRoleCaught, "Non-administrator role for escrow recovery must be rejected");
        checks++;

        Console.WriteLine($"Accounting Exports & DATEV Interoperability checks passed ({checks} checks).");
    }

    private static void Assert(bool condition, string message)
    {
        if (!condition)
        {
            throw new InvalidOperationException($"Assertion failed: {message}");
        }
    }
}
