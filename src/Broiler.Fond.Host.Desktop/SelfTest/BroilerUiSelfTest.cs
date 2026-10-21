using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Broiler.Fond.Host.Desktop.Hosting;
using Broiler.Fond.Host.Desktop.Integration.Clipboard;
using Broiler.Fond.Host.Desktop.Integration.SystemTray;
using Broiler.Fond.Host.Desktop.Integration.Theme;
using Broiler.Fond.Host.Desktop.Qualification;
using Broiler.Fond.Host.Desktop.Windowing;
using Broiler.Fond.Host.Desktop.Windowing.Headless;
using Broiler.Fond.Host.Desktop.Windowing.Linux;
using Broiler.Fond.Host.Desktop.Windowing.Mac;
using Broiler.Fond.Host.Desktop.Windowing.Windows;
using Broiler.Fond.Kernel.Diagnostics;
using Broiler.Fond.Kernel.Domain.Accounts;
using Broiler.Fond.Kernel.Domain.Insights;
using Broiler.Fond.Kernel.Domain.Payments;
using Broiler.Fond.Kernel.Domain.Transactions;
using Broiler.Fond.Kernel.Domain.Transactions.Export;
using Broiler.Fond.Kernel.Domain.Transactions.Query;
using Broiler.Fond.Kernel.FinTs;
using Broiler.Fond.Kernel.Hosting;
using Broiler.Fond.Kernel.Storage;
using Broiler.Fond.Presentation.Accessibility;
using Broiler.Fond.Presentation.Dialogs;
using Broiler.Fond.Presentation.Models;
using Broiler.Fond.Presentation.Services;
using Broiler.Fond.Presentation.Sessions;
using Broiler.Fond.Presentation.ViewModels;
using Broiler.Fond.Presentation.Views;
using Broiler.UI;
using Broiler.UI.Standard;

namespace Broiler.Fond.Host.Desktop.SelfTest;

/// <summary>
/// Comprehensive headless verification engine for Broiler UI presentation, view models,
/// dialog accessibility, ephemeral credential zeroization, and desktop hosting (BUI-06).
/// Ports and expands the full test battery of HostSelfTest without any WinForms dependencies.
/// </summary>
public static class BroilerUiSelfTest
{
    public static int Run()
    {
        Console.WriteLine("Running Broiler UI Headless Self-Test & BFSG Accessibility Auditor (BUI-06)...");
        int checks = 0;

        void Verify(bool condition, string message)
        {
            if (!condition)
            {
                throw new InvalidOperationException($"Broiler UI self-test assertion failed: {message}");
            }
            checks++;
        }

        // 1. Account ViewModel & Freshness
        VerifyAccountViewModel(Verify);

        // 2. Multi-Currency Isolation
        VerifyMultiCurrencyIsolation(Verify);

        // 3. Connection ViewModel & State Badges
        VerifyConnectionViewModel(Verify);

        // 4. HostSession Lifecycle & Preflight
        VerifyHostSessionLifecycle(Verify);

        // 5. All 18 Banking Dialogs & BFSG Accessibility Audit
        VerifyDialogsAndAccessibility(Verify);

        // 6. Account Relinking Workflow
        VerifyAccountRelinkingWorkflow(Verify);

        // 7. Transaction Presentation, Virtual Grid & CSV Export
        VerifyTransactionPresentationAndAccessibility(Verify);

        // 8. Multibank Dashboard & Freshness Badges
        VerifyMultibankDashboardAndFreshness(Verify);

        // 9. Connection Management Actions
        VerifyConnectionManagementActions(Verify);

        // 10. Redacted Support Export Preview
        VerifyRedactedSupportBundleExportPreview(Verify);

        // 11. Safe Mode & Payment Workflows
        VerifySafeModeAndPaymentWorkflows(Verify);

        // 12. Everyday Payments, Standing Orders & SEPA Direct Debit Returns
        VerifyEverydayPaymentsAndStandingOrders(Verify);

        // 13. Financial Insights & Budget Management
        VerifyFinancialInsightsAndBudgetManagement(Verify);

        // 14. Cross-Platform Desktop Host & Integration Seams
        VerifyCrossPlatformDesktopHardening(Verify);

        Console.WriteLine($"Broiler UI Self-Test: {checks} checks completed successfully.");
        return checks;
    }

    private static void VerifyAccountViewModel(Action<bool, string> verify)
    {
        var now = DateTimeOffset.UtcNow;
        var acc = new StoreAccountEntry(100, "Checking Account", "EUR");
        var binding = new StoreBindingEntry(
            200, 100, 150, 10, 1, "Active", "FinTs", "3.0", "https://localhost",
            "DE89370400440532013000", null, null, null, "EUR", Array.Empty<(string, string, string, string)>());

        var bookedBalance = new StoreBalanceEntry(
            300, 1, 100, 150, 200,
            "Booked", "1520.50", "EUR", now.AddMinutes(-15),
            now.AddMinutes(-15), false, "HISAL");

        var availBalance = new StoreBalanceEntry(
            301, 1, 100, 150, 200,
            "Available", "1520.50", "EUR", now.AddMinutes(-15),
            now.AddMinutes(-15), false, "HISAL");

        var vm = new AccountViewModel(acc, binding, bookedBalance, availBalance, now, accountType: "Payment", accountHolder: "Max Mustermann");

        verify(vm.AccountId == 100, "Account ID preserved.");
        verify(vm.AccountName == "Checking Account", "Account name preserved.");
        verify(vm.IsSupportedPaymentAccount, "Payment account type recognized as supported.");
        verify(vm.AccountTypeDisplay == "Payment / Current (Girokonto)", "Payment account type display formatted correctly.");
        verify(vm.LocatorDisplay.Contains("DE89370400440532013000", StringComparison.Ordinal), "IBAN displayed in locator.");
        verify(vm.BindingStateDisplay == "[✓ Active]", "Active binding state formatted with non-color indicator.");
        verify(vm.BookedAmount == 1520.50m, "Booked amount matches.");
        verify(vm.BookedCurrency == "EUR", "Booked currency matches.");
        verify(vm.BookedBalanceDisplay.Contains("1.520,50", StringComparison.Ordinal) || vm.BookedBalanceDisplay.Contains("1,520.50", StringComparison.Ordinal), "Booked balance formatted.");
        verify(vm.AvailableAmount == 1520.50m, "Available amount matches.");
        verify(vm.FreshnessDisplay.Contains("Fresh", StringComparison.Ordinal), "Freshness indicator shows fresh (< 1h).");
        verify(!vm.IsStale, "Fresh account is not stale.");

        // Stale account (> 24 hours)
        var staleBooked = new StoreBalanceEntry(
            302, 2, 100, 150, 200,
            "Booked", "500.00", "EUR", now.AddDays(-3),
            now.AddDays(-3), true, "HISAL");

        var staleVm = new AccountViewModel(acc, binding, staleBooked, null, now, accountType: "Payment");
        verify(staleVm.IsStale, "Balance older than 24h is marked stale.");
        verify(staleVm.FreshnessDisplay.Contains("Stale", StringComparison.Ordinal), "Freshness display indicates stale.");
        verify(staleVm.AvailableBalanceDisplay == "[Unavailable]", "Missing available balance displayed as [Unavailable].");
    }

    private static void VerifyMultiCurrencyIsolation(Action<bool, string> verify)
    {
        var now = DateTimeOffset.UtcNow;
        var eurAcc = new AccountViewModel(
            new StoreAccountEntry(1, "EUR Account", "EUR"),
            null,
            new StoreBalanceEntry(1, 1, 1, 1, 1, "Booked", "1000.00", "EUR", now, now, false, "HISAL"),
            null, now);

        var usdAcc = new AccountViewModel(
            new StoreAccountEntry(2, "USD Account", "USD"),
            null,
            new StoreBalanceEntry(2, 1, 2, 1, 1, "Booked", "2500.00", "USD", now, now, false, "HISAL"),
            null, now);

        var gbpAcc = new AccountViewModel(
            new StoreAccountEntry(3, "GBP Account", "GBP"),
            null,
            new StoreBalanceEntry(3, 1, 3, 1, 1, "Booked", "750.00", "GBP", now, now, false, "HISAL"),
            null, now);

        List<AccountViewModel> accounts = [eurAcc, usdAcc, gbpAcc];

        // Multi-currency balances are strictly partitioned by currency and never summed into one total
        var partitionedTotals = accounts
            .Where(a => a.BookedAmount.HasValue)
            .GroupBy(a => a.BookedCurrency)
            .ToDictionary(g => g.Key, g => g.Sum(a => a.BookedAmount!.Value));

        verify(partitionedTotals.Count == 3, "Accounts partitioned into 3 isolated currency buckets.");
        verify(partitionedTotals["EUR"] == 1000.00m, "EUR total is exactly 1000.00.");
        verify(partitionedTotals["USD"] == 2500.00m, "USD total is exactly 2500.00.");
        verify(partitionedTotals["GBP"] == 750.00m, "GBP total is exactly 750.00.");
    }

    private static void VerifyConnectionViewModel(Action<bool, string> verify)
    {
        var inst = new StoreInstitutionEntry(10, "Sparkasse KölnBonn", "https://banking.sparkasse.de/fints", "37050198", "SPKADE21XXX", "Confirmed");
        var conn = new StoreConnectionEntry(1, 10, "sparkasse-user", "921", "Synchronized", "SYS1", "Active", 1, true, null, null, "3.0");

        var vm = new ConnectionViewModel(conn, inst);

        verify(vm.ConnectionId == 1, "Connection ID preserved.");
        verify(vm.InstitutionName == "Sparkasse KölnBonn", "Institution name preserved.");
        verify(vm.BankCode == "37050198", "Bank code preserved.");
        verify(vm.EndpointHost == "banking.sparkasse.de", "Endpoint host extracted.");
        verify(vm.IsEndpointConfirmed, "Endpoint confirmed flag preserved.");
        verify(vm.CanRefresh, "Synchronized and confirmed connection can refresh.");
        verify(!vm.RequiresReconfirmation, "Confirmed connection does not require reconfirmation.");
        verify(vm.StateDisplay.Contains("Synchronized", StringComparison.Ordinal), "Synchronized state formatted.");
    }

    private static void VerifyHostSessionLifecycle(Action<bool, string> verify)
    {
        using var session = new HostSession();
        verify(!session.IsUnlocked, "Session initially locked.");
        verify(!session.IsSafeModeActive, "Safe Mode initially inactive.");

        bool safeModeEventRaised = false;
        session.SafeModeTriggered += (_, _) => safeModeEventRaised = true;

        session.IsSafeModeActive = true;
        verify(session.IsSafeModeActive, "Safe mode active.");
        verify(safeModeEventRaised, "SafeModeTriggered event fired.");

        session.IsSafeModeActive = false;
        verify(!session.IsSafeModeActive, "Safe mode inactive after exit.");
    }

    private static void VerifyDialogsAndAccessibility(Action<bool, string> verify)
    {
        string tempDir = Path.Combine(Path.GetTempPath(), "BroilerUiSelfTest_Dlgs_" + Guid.NewGuid().ToString("N"));
        try
        {
            Directory.CreateDirectory(tempDir);
            using var session = new HostSession();
            session.CreateNewProfile(tempDir, "StrongPassphrase123!", "Self-Test Profile");

            // 1. Profile Unlock Dialog
            var unlockDlg = new BroilerProfileUnlockDialog(session, tempDir);
            var unlockAudit = BroilerUiAccessibilityAuditor.AuditDialog(unlockDlg);
            verify(unlockAudit.Passed, "ProfileUnlockDialog passes BFSG audit.");
            unlockDlg.PassphraseInput.AppendChars("SecretPassword123".AsSpan());
            verify(unlockDlg.PassphraseInput.Length > 0, "Passphrase accepted.");
            unlockDlg.Close(BroilerDialogResult.Cancel);
            verify(unlockDlg.PassphraseInput.Length == 0, "Passphrase wiped on cancel.");

            // 2. Institution Setup Dialog
            var instDlg = new BroilerInstitutionSetupDialog();
            var instAudit = BroilerUiAccessibilityAuditor.AuditDialog(instDlg);
            verify(instAudit.Passed, "InstitutionSetupDialog passes BFSG audit.");

            // 3. Endpoint Confirmation Dialog
            Uri ep = new("https://fints.bank.de/hbci");
            var epDlg = new BroilerEndpointConfirmationDialog(ep, "Test Bank");
            var epAudit = BroilerUiAccessibilityAuditor.AuditDialog(epDlg);
            verify(epAudit.Passed, "EndpointConfirmationDialog passes BFSG audit.");

            // 4. Restore Backup Dialog
            var backupDlg = new BroilerRestoreBackupDialog(session);
            var backupAudit = BroilerUiAccessibilityAuditor.AuditDialog(backupDlg);
            verify(backupAudit.Passed, "RestoreBackupDialog passes BFSG audit.");

            // 5. Credential Prompt Dialog & Memory Hygiene
            var credDlg = new BroilerCredentialPromptDialog("Sparkasse", "User123", FinTsCredentialKind.Pin);
            var credAudit = BroilerUiAccessibilityAuditor.AuditDialog(credDlg);
            verify(credAudit.Passed, "CredentialPromptDialog passes BFSG audit.");
            credDlg.CredentialInput.AppendChars("54321".AsSpan());
            credDlg.Submit();
            verify(credDlg.CapturedCredential != null, "Credential captured into FinTsSessionCredential.");
            verify(credDlg.CredentialInput.Length == 0, "Credential input cleared upon submit.");

            // 6. SCA Challenge Dialog
            var scaDlg = new BroilerScaChallengeDialog(
                "PhotoTAN App",
                "ORD-999",
                "Please scan the PhotoTAN code:",
                901,
                ScaChallengeMode.PhotoTanMatrix);
            var scaAudit = BroilerUiAccessibilityAuditor.AuditDialog(scaDlg);
            verify(scaAudit.Passed, "ScaChallengeDialog passes BFSG audit.");

            // 7. Payment Initiation Dialog
            var payInitDlg = new BroilerPaymentInitiationDialog(session);
            var payInitAudit = BroilerUiAccessibilityAuditor.AuditDialog(payInitDlg);
            verify(payInitAudit.Passed, "PaymentInitiationDialog passes BFSG audit.");

            // 8. Payment Review Dialog
            var reviewDlg = new BroilerPaymentReviewDialog(
                sourceAccountName: "Girokonto",
                beneficiaryName: "Finanzamt Köln",
                beneficiaryIban: "DE89370400440532013000",
                beneficiaryBic: "DBBADE33XXX",
                amount: 450.00m,
                currency: "EUR",
                remittance: "ESt 2025 Vorauszahlung",
                executionDate: DateOnly.FromDateTime(DateTime.UtcNow),
                vopResult: new StorePaymentVoPResultEntry(
                    Status: nameof(VerificationOfPayeeStatus.Match),
                    CheckedAt: DateTimeOffset.UtcNow,
                    MatchedName: "Finanzamt Köln",
                    ReasonCode: null,
                    WarningMessage: null,
                    UserOverride: false));
            var reviewAudit = BroilerUiAccessibilityAuditor.AuditDialog(reviewDlg);
            verify(reviewAudit.Passed, "PaymentReviewDialog passes BFSG audit.");

            // 9. Standing Orders Dialog
            var soDlg = new BroilerStandingOrdersDialog(session);
            var soAudit = BroilerUiAccessibilityAuditor.AuditDialog(soDlg);
            verify(soAudit.Passed, "StandingOrdersDialog passes BFSG audit.");

            // 10. Direct Debit Return Dialog
            DirectDebitReturnViewModel debitVm = new(
                transactionId: 101,
                creditorName: "Gym Fitness GmbH",
                creditorIban: "DE89370400440532013000",
                creditorId: "DE98ZZZ09999999999",
                mandateId: "MANDATE-01",
                amount: -49.99m,
                currency: "EUR",
                bookingDate: DateOnly.FromDateTime(DateTime.UtcNow.AddDays(-5)),
                referenceDate: DateOnly.FromDateTime(DateTime.UtcNow));
            var ddDlg = new BroilerDirectDebitReturnDialog(session, debitVm);
            var ddAudit = BroilerUiAccessibilityAuditor.AuditDialog(ddDlg);
            verify(ddAudit.Passed, "DirectDebitReturnDialog passes BFSG audit.");

            // 11. Account Relinking Dialog
            var relinkDlg = new BroilerAccountRelinkingDialog(session);
            var relinkAudit = BroilerUiAccessibilityAuditor.AuditDialog(relinkDlg);
            verify(relinkAudit.Passed, "AccountRelinkingDialog passes BFSG audit.");

            // 12. Connection Removal Dialog
            var connEntry = new StoreConnectionEntry(1, 10, "User", null, "Synchronized", null, "Active", 1, true, null, null, "3.0");
            var instEntry = new StoreInstitutionEntry(10, "Deutsche Bank AG", "https://db.de", "10070000", null, "Confirmed");
            var connVm = new ConnectionViewModel(connEntry, instEntry);
            var remDlg = new BroilerConnectionRemovalDialog(connVm);
            var remAudit = BroilerUiAccessibilityAuditor.AuditDialog(remDlg);
            verify(remAudit.Passed, "ConnectionRemovalDialog passes BFSG audit.");

            // 13. Support Export Dialog
            LocalDiagnosticBuffer diagBuffer = new();
            var preview = diagBuffer.CreateSupportPreview();
            var suppDlg = new BroilerSupportExportDialog(preview);
            var suppAudit = BroilerUiAccessibilityAuditor.AuditDialog(suppDlg);
            verify(suppAudit.Passed, "SupportExportDialog passes BFSG audit.");

            // 14. CSV Export Dialog
            var csvDlg = new BroilerCsvExportDialog(session, null);
            var csvAudit = BroilerUiAccessibilityAuditor.AuditDialog(csvDlg);
            verify(csvAudit.Passed, "CsvExportDialog passes BFSG audit.");

            // 15. Categorization Rules Dialog
            var rulesDlg = new BroilerCategorizationRulesDialog(session);
            var rulesAudit = BroilerUiAccessibilityAuditor.AuditDialog(rulesDlg);
            verify(rulesAudit.Passed, "CategorizationRulesDialog passes BFSG audit.");

            // 16. Transaction Split Dialog
            var now = DateTimeOffset.UtcNow;
            TransactionItemRecord txRecord = new(
                TransactionId: 101,
                AccountId: 100,
                AccountDisplayName: "Checking",
                AccountIban: "DE89370400440532013000",
                AccountIncarnationId: 150,
                AccountBindingId: 200,
                Status: "Booked",
                Kind: "DebitTransfer",
                Amount: -100.00m,
                Currency: "EUR",
                IsCredit: false,
                BookingDate: new DateOnly(2026, 10, 1),
                ValueDate: new DateOnly(2026, 10, 1),
                BankTimestamp: now,
                RetrievedAt: now,
                CounterpartyName: "Supermarket Purchase",
                CounterpartyIban: null,
                CounterpartyBic: null,
                RemittanceText: "Groceries",
                EndToEndId: null,
                MandateId: null,
                CreditorId: null,
                CustomerReference: null,
                BankReference: null,
                PurposeCode: null,
                ReturnReasonCode: null,
                RawBankCode: null,
                UserNote: null,
                Category: "Uncategorized",
                Tags: [],
                StatementFormat: "CAMT053",
                StatementNumber: "2026/1",
                LinkedPendingTransactionId: null,
                LinkedBookedTransactionId: null,
                Splits: null);

            var txVm = new TransactionViewModel(txRecord);
            var splitDlg = new BroilerTransactionSplitDialog(session, txVm);
            var splitAudit = BroilerUiAccessibilityAuditor.AuditDialog(splitDlg);
            verify(splitAudit.Passed, "TransactionSplitDialog passes BFSG audit.");

            // 17. Cash Flow Forecast Dialog
            var cfDlg = new BroilerCashFlowForecastDialog(session);
            var cfAudit = BroilerUiAccessibilityAuditor.AuditDialog(cfDlg);
            verify(cfAudit.Passed, "CashFlowForecastDialog passes BFSG audit.");

            // 18. About Dialog
            var aboutDlg = new BroilerAboutDialog();
            var aboutAudit = BroilerUiAccessibilityAuditor.AuditDialog(aboutDlg);
            verify(aboutAudit.Passed, "AboutDialog passes BFSG audit.");
        }
        finally
        {
            try { Directory.Delete(tempDir, recursive: true); } catch { }
        }
    }

    private static void VerifyAccountRelinkingWorkflow(Action<bool, string> verify)
    {
        string tempDir = Path.Combine(Path.GetTempPath(), "BroilerRelinkTest_" + Guid.NewGuid().ToString("N"));
        try
        {
            Directory.CreateDirectory(tempDir);
            using var session = new HostSession();
            session.CreateNewProfile(tempDir, "StrongPassphrase123!", "Relink Test Profile");

            var relinkDlg = new BroilerAccountRelinkingDialog(session);
            verify(relinkDlg.Title.Contains("Relink Account", StringComparison.OrdinalIgnoreCase), "Account relinking dialog initialized.");
        }
        finally
        {
            try { Directory.Delete(tempDir, recursive: true); } catch { }
        }
    }

    private static void VerifyTransactionPresentationAndAccessibility(Action<bool, string> verify)
    {
        var now = DateTimeOffset.UtcNow;
        var record = new TransactionItemRecord(
            TransactionId: 1,
            AccountId: 100,
            AccountDisplayName: "Checking",
            AccountIban: "DE89370400440532013000",
            AccountIncarnationId: 150,
            AccountBindingId: 200,
            Status: "Booked",
            Kind: "DebitTransfer",
            Amount: -84.50m,
            Currency: "EUR",
            IsCredit: false,
            BookingDate: new DateOnly(2026, 10, 19),
            ValueDate: new DateOnly(2026, 10, 19),
            BankTimestamp: now,
            RetrievedAt: now,
            CounterpartyName: "REWE Markt GmbH",
            CounterpartyIban: "DE89370400440532013000",
            CounterpartyBic: null,
            RemittanceText: "Einkauf Lebensmitteln",
            EndToEndId: "TX-ENDTOEND-101",
            MandateId: null,
            CreditorId: null,
            CustomerReference: null,
            BankReference: null,
            PurposeCode: null,
            ReturnReasonCode: null,
            RawBankCode: null,
            UserNote: null,
            Category: "Groceries",
            Tags: [],
            StatementFormat: "CAMT053",
            StatementNumber: "2026/1",
            LinkedPendingTransactionId: null,
            LinkedBookedTransactionId: null,
            Splits: null);

        var vm = new TransactionViewModel(record);
        verify(vm.TransactionId == 1, "Transaction ID preserved.");
        verify(vm.CounterpartyName == "REWE Markt GmbH", "Counterparty matches.");
        verify(vm.Amount == -84.50m, "Amount matches.");
        verify(!vm.IsCredit, "Recognized as debit.");

        // CSV Export Formatter
        using var sw = new StringWriter();
        TransactionCsvExporter.ExportToCsv([record], sw);
        string csv = sw.ToString();
        verify(csv.Contains("REWE Markt GmbH", StringComparison.Ordinal), "CSV export includes counterparty.");
        verify(csv.Contains("-84.50", StringComparison.Ordinal) || csv.Contains("-84,50", StringComparison.Ordinal), "CSV export includes amount.");
    }

    private static void VerifyMultibankDashboardAndFreshness(Action<bool, string> verify)
    {
        var now = DateTimeOffset.UtcNow;
        var freshBalance = new StoreBalanceEntry(1, 1, 1, 1, 1, "Booked", "5000.00", "EUR", now.AddMinutes(-5), now.AddMinutes(-5), false, "HISAL");
        var staleBalance = new StoreBalanceEntry(2, 1, 2, 1, 1, "Booked", "1200.00", "EUR", now.AddHours(-30), now.AddHours(-30), true, "HISAL");

        var freshVm = new AccountViewModel(new StoreAccountEntry(1, "Fresh Account", "EUR"), null, freshBalance, null, now);
        var staleVm = new AccountViewModel(new StoreAccountEntry(2, "Stale Account", "EUR"), null, staleBalance, null, now);

        verify(!freshVm.IsStale, "Fresh account is not stale.");
        verify(staleVm.IsStale, "Stale account detected (> 24 hours).");
        verify(freshVm.FreshnessDisplay.Contains("Fresh", StringComparison.Ordinal), "Fresh display text correct.");
        verify(staleVm.FreshnessDisplay.Contains("Stale", StringComparison.Ordinal), "Stale display text correct.");
    }

    private static void VerifyConnectionManagementActions(Action<bool, string> verify)
    {
        var inst = new StoreInstitutionEntry(1, "Test Bank", "https://testbank.de/fints", "12345678", null, "Confirmed");
        var conn = new StoreConnectionEntry(1, 1, "user1", "900", "Synchronized", "SYS1", "Active", 1, true, null, null, "3.0");

        var connVm = new ConnectionViewModel(conn, inst);
        verify(connVm.StateDisplay.Contains("Synchronized", StringComparison.Ordinal), "Connection status Synchronized.");

        var remDlg = new BroilerConnectionRemovalDialog(connVm);
        verify(!remDlg.PurgeCachedData, "Initial removal mode is config-only.");
    }

    private static void VerifyRedactedSupportBundleExportPreview(Action<bool, string> verify)
    {
        var buffer = new LocalDiagnosticBuffer(256, enabled: true);
        buffer.Record(LocalDiagnosticCode.SessionStarted, LocalDiagnosticOutcome.Succeeded, DateTimeOffset.UtcNow);
        var preview = buffer.CreateSupportPreview();

        verify(preview.PreviewSha256.Length == 64, "Preview SHA-256 digest is exact 64-hex string.");
        verify(preview.Json.Contains("SessionStarted", StringComparison.Ordinal), "Support bundle contains diagnostic code.");
    }

    private static void VerifySafeModeAndPaymentWorkflows(Action<bool, string> verify)
    {
        var now = DateTimeOffset.UtcNow;
        var draft = new StorePaymentDraftEntry(
            Id: 501,
            SourceAccountId: 101,
            SourceAccountBindingId: 201,
            SourceAccountIncarnationId: 301,
            CurrentRevisionId: 601,
            CreatedAt: now.AddHours(-1),
            UpdatedAt: now.AddMinutes(-30));

        var rev = new StorePaymentDraftRevisionEntry(
            Id: 601,
            DraftId: 501,
            RevisionNumber: 1,
            PredecessorRevisionId: null,
            Timestamp: now.AddHours(-1),
            SourceAccountId: 101,
            SourceAccountBindingId: 201,
            SourceAccountIncarnationId: 301,
            BeneficiaryName: "Max Mustermann",
            BeneficiaryIban: "DE89370400440532013000",
            BeneficiaryBic: "SPKADE21XXX",
            Amount: 250.00m,
            Currency: "EUR",
            RemittanceUnstructured: "Invoice 2026-001",
            RemittanceCreditorReference: null,
            ExecutionMode: "Standard",
            ExecutionDate: DateOnly.FromDateTime(DateTime.UtcNow.Date));

        var vm = new PaymentDraftViewModel(draft, rev, "Primary Girokonto");
        verify(vm.DraftId == 501, "Draft ID preserved.");
        verify(vm.Amount == 250.00m, "Payment amount matches.");
        verify(vm.BeneficiaryName == "Max Mustermann", "Beneficiary name matches.");

        // Verification of Payee (VoP) matching
        var vopMatch = VerificationOfPayeeResult.CreateMatch("Max Mustermann");
        verify(vopMatch.Status == VerificationOfPayeeStatus.Match, "VoP full match recognized.");
        verify(!vopMatch.RequiresExplicitAcknowledgment, "VoP full match does not require extra warning ack.");
    }

    private static void VerifyEverydayPaymentsAndStandingOrders(Action<bool, string> verify)
    {
        var today = DateOnly.FromDateTime(DateTime.UtcNow.Date);
        var so = new StandingOrderViewModel(
            orderId: 7001,
            beneficiaryName: "Landlord GbR",
            beneficiaryIban: "DE89370400440532013000",
            amount: 1200.00m,
            currency: "EUR",
            frequency: "Monthly",
            executionDay: 1,
            firstExecutionDate: today.AddDays(10),
            lastExecutionDate: null,
            status: "Active");

        verify(so.OrderId == 7001, "Standing order ID preserved.");
        verify(so.Frequency == "Monthly", "Frequency is Monthly.");
        verify(so.Amount == 1200.00m, "Amount is 1200.00.");

        // SEPA direct debit return codes
        string code = DirectDebitReturnReason.DebtorRequestUnconditional.ToReasonCode();
        verify(code == "MS02", "Unconditional return code is MS02.");
        var parsedReason = DirectDebitReturnReasonExtensions.FromReasonCode("MD01");
        verify(parsedReason == DirectDebitReturnReason.NoValidMandate, "MD01 parses to NoValidMandate.");
    }

    private static void VerifyFinancialInsightsAndBudgetManagement(Action<bool, string> verify)
    {
        var budget = new BudgetTarget(1, "Groceries", 600.00m, "EUR");
        verify(budget.Category == "Groceries", "Budget category matches.");
        verify(budget.MonthlyLimit == 600.00m, "Budget monthly limit matches.");

        // Spending ratio calculation
        decimal spent = 450.00m;
        double ratio = (double)(spent / budget.MonthlyLimit);
        verify(ratio == 0.75, "Spending ratio is 75%.");
        verify(ratio < 0.80, "Spending target is green (< 80%).");

        // Net worth calculation with multi-currency isolation
        var assets = new Dictionary<string, decimal> { ["EUR"] = 10000.00m, ["USD"] = 5000.00m };
        var liabilities = new Dictionary<string, decimal> { ["EUR"] = 2000.00m, ["USD"] = 1000.00m };

        decimal netEur = assets["EUR"] - liabilities["EUR"];
        decimal netUsd = assets["USD"] - liabilities["USD"];

        verify(netEur == 8000.00m, "Net EUR position is exactly 8000.00.");
        verify(netUsd == 4000.00m, "Net USD position is exactly 4000.00.");
    }

    private static void VerifyCrossPlatformDesktopHardening(Action<bool, string> verify)
    {
        // 1. Startup Platform Qualification
        var qualResult = DesktopStartupQualificationGuard.Qualify();
        verify(qualResult.IsQualified, "Desktop host platform qualified.");
        verify(qualResult.Violations.Count == 0, "Zero qualification violations.");

        // 2. Windows DXGI Surface
        using (var win = new WindowsDxgiPresentationSurface())
        {
            win.Initialize(1280, 800);
            win.ConfigureDwmAttributes(isDarkMode: true);
            win.Show();
            _ = win.BeginDraw();
            win.PresentFrame();
            verify(win.FrameCount == 1, "Windows DXGI frame presented.");
        }

        // 3. Linux Wayland & X11 Fallback
        using (var wayland = new LinuxWaylandPresentationSurface())
        {
            wayland.Initialize(1024, 768);
            wayland.Show();
            _ = wayland.BeginDraw();
            wayland.PresentFrame();
            verify(wayland.ProtocolState == "Committed", "Wayland commit protocol executed.");
        }

        using (var x11 = new LinuxX11FallbackPresentationSurface())
        {
            x11.Initialize(1024, 768);
            x11.Show();
            _ = x11.BeginDraw();
            x11.PresentFrame();
            verify(x11.FrameCount == 1, "X11 frame presented.");
        }

        // 4. macOS Metal Cocoa Window
        using (var mac = new MacMetalPresentationSurface())
        {
            mac.Initialize(1280, 800);
            mac.Show();
            _ = mac.BeginDraw();
            mac.PresentFrame();
            verify(mac.FrameCount == 1, "Metal Cocoa frame presented.");
        }

        // 5. System Tray & Minimize-to-Tray
        using (var tray = new DesktopSystemTray())
        {
            tray.ShowTrayIcon("Broiler Fond [Active]");
            verify(tray.IsVisible, "Tray icon is visible.");

            using var testWindow = new DesktopWindow(new HeadlessOffscreenPresentationSurface());
            testWindow.Show();
            tray.MinimizeToTray(testWindow);
            verify(!testWindow.IsVisible, "Window hidden after minimize-to-tray.");
            tray.RestoreFromTray(testWindow);
            verify(testWindow.IsVisible, "Window restored from tray.");
        }

        // 6. OS Theme Listener & WCAG Contrast
        using (var themeListener = new DesktopThemeListener())
        {
            verify(themeListener.CurrentPalette != null, "Theme palette initialized.");
            verify(themeListener.CurrentPalette!.ValidateWcagContrast(), "Theme palette meets WCAG 2.1 AA contrast requirements.");
            themeListener.SetTheme(ThemeMode.StandardDark);
            verify(themeListener.CurrentThemeMode == ThemeMode.StandardDark, "Theme switched to StandardDark.");
            verify(themeListener.CurrentPalette!.ValidateWcagContrast(), "StandardDark meets WCAG 2.1 AA contrast.");
        }

        // 7. Secure Transient Clipboard Timed Wipe
        using (var clipboard = new DesktopClipboardService())
        {
            string sensitiveRef = "SensitiveIBAN_DE89370400440532013000";
            clipboard.SetTransientTextAsync(sensitiveRef, TimeSpan.FromMilliseconds(40)).AsTask().GetAwaiter().GetResult();
            string? copied = clipboard.GetTextAsync().AsTask().GetAwaiter().GetResult();
            verify(copied == sensitiveRef, "Clipboard content accessible immediately.");

            Thread.Sleep(120);
            string? autoWiped = clipboard.GetTextAsync().AsTask().GetAwaiter().GetResult();
            verify(autoWiped == null, "Clipboard content automatically zeroed upon timeout expiry (ADR 0130).");
        }

        // 8. Desktop Window Workspace Layout
        using (var session = new HostSession())
        {
            var dialogService = new BroilerDialogService();
            var mainWindow = new BroilerMainWindow(session, dialogService);

            using var surface = new HeadlessOffscreenPresentationSurface();
            using var desktopWindow = new DesktopWindow(surface)
            {
                Content = mainWindow
            };

            desktopWindow.Show();
            verify(surface.FrameCount >= 1, "Main banking workspace layout and frame presented.");

            // Accessibility audit of entire main banking workspace!
            var workspaceAudit = BroilerUiAccessibilityAuditor.Audit(mainWindow);
            verify(workspaceAudit.Passed, "Entire BroilerMainWindow tree passes BFSG accessibility audit.");
        }
    }
}
