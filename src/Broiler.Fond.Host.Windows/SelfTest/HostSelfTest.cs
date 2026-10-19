using System.Drawing;
using System.Windows.Forms;
using Broiler.Fond.Host.Windows.Dialogs;
using Broiler.Fond.Host.Windows.Models;
using Broiler.Fond.Host.Windows.Views;
using Broiler.Fond.Kernel.Diagnostics;
using Broiler.Fond.Kernel.Domain.Accounts;
using Broiler.Fond.Kernel.Domain.Insights;
using Broiler.Fond.Kernel.Domain.Payments;
using Broiler.Fond.Kernel.Domain.Transactions;
using Broiler.Fond.Kernel.Domain.Transactions.Export;
using Broiler.Fond.Kernel.Domain.Transactions.Query;
using Broiler.Fond.Kernel.FinTs;
using Broiler.Fond.Kernel.Storage;

namespace Broiler.Fond.Host.Windows.SelfTest;

/// <summary>
/// Headless automated self-test validating Windows host presentation, view models, multi-currency isolation,
/// dialog accessibility, and credential zeroization for M1-06, M1-07, and M1-08.
/// </summary>
public static class HostSelfTest
{
    public static int Run()
    {
        int checkCount = 0;
        Action<bool, string> verify = (condition, message) =>
        {
            if (!condition)
            {
                throw new InvalidOperationException($"Host self-test assertion failed: {message}");
            }
            checkCount++;
        };

        VerifyAccountViewModel(verify);
        VerifyMultiCurrencyIsolation(verify);
        VerifyConnectionViewModel(verify);
        VerifyHostSessionLifecycle(verify);
        VerifyDialogsAndAccessibility(verify);
        VerifyAccountRelinkingWorkflow(verify);
        VerifyTransactionPresentationAndAccessibility(verify);
        VerifyMultibankDashboardAndFreshness(verify);
        VerifyConnectionManagementActions(verify);
        VerifyRedactedSupportBundleExportPreview(verify);
        VerifySafeModeAndPaymentWorkflows(verify);
        VerifyEverydayPaymentsAndStandingOrders(verify);
        VerifyFinancialInsightsAndBudgetManagement(verify);
        VerifyWindowsHost10Hardening(verify);

        return checkCount;
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

        // Identity-only shell (unsupported account type)
        var loanAcc = new StoreAccountEntry(102, "Car Loan", "EUR");
        var loanVm = new AccountViewModel(loanAcc, null, null, null, now, accountType: "Loan");
        verify(!loanVm.IsSupportedPaymentAccount, "Loan account is not a supported payment account.");
        verify(loanVm.AccountTypeDisplay.Contains("Identity-only Shell", StringComparison.Ordinal), "Loan account shows identity-only shell.");
        verify(loanVm.BookedBalanceDisplay == "[No Balance Recorded]", "Empty balance shows [No Balance Recorded].");

        // Quarantined candidate binding
        var quarBinding = new StoreBindingEntry(
            201, 100, 150, 10, 1, "Quarantined", "FinTs", "3.0", "https://localhost",
            "DE89370400440532013000", null, null, null, "EUR", Array.Empty<(string, string, string, string)>());
        var quarVm = new AccountViewModel(acc, quarBinding, bookedBalance, availBalance, now, accountType: "Payment");
        verify(quarVm.BindingStateDisplay.Contains("Quarantined Candidate", StringComparison.Ordinal), "Quarantined binding displayed.");
    }

    private static void VerifyMultiCurrencyIsolation(Action<bool, string> verify)
    {
        var now = DateTimeOffset.UtcNow;
        var accEur1 = new StoreAccountEntry(1, "EUR Account 1", "EUR");
        var bEur1 = new StoreBindingEntry(11, 1, 101, 10, 1, "Active", "FinTs", "3.0", "https://localhost", "DE01", null, null, null, "EUR", Array.Empty<(string, string, string, string)>());
        var balEur1 = new StoreBalanceEntry(111, 1, 1, 101, 11, "Booked", "1000.00", "EUR", now, now, false, "HISAL");

        var accEur2 = new StoreAccountEntry(2, "EUR Account 2", "EUR");
        var bEur2 = new StoreBindingEntry(12, 2, 102, 10, 1, "Active", "FinTs", "3.0", "https://localhost", "DE02", null, null, null, "EUR", Array.Empty<(string, string, string, string)>());
        var balEur2 = new StoreBalanceEntry(112, 2, 2, 102, 12, "Booked", "2500.00", "EUR", now, now, false, "HISAL");

        var accUsd = new StoreAccountEntry(3, "USD Account", "USD");
        var bUsd = new StoreBindingEntry(13, 3, 103, 10, 1, "Active", "FinTs", "3.0", "https://localhost", "US01", null, null, null, "USD", Array.Empty<(string, string, string, string)>());
        var balUsd = new StoreBalanceEntry(113, 3, 3, 103, 13, "Booked", "500.00", "USD", now, now, false, "HISAL");

        var viewModels = new[]
        {
            new AccountViewModel(accEur1, bEur1, balEur1, null, now),
            new AccountViewModel(accEur2, bEur2, balEur2, null, now),
            new AccountViewModel(accUsd, bUsd, balUsd, null, now),
        };

        var totals = AccountViewModel.ComputeMultiCurrencyTotals(viewModels);

        verify(totals.Count == 2, "Totals dictionary contains exactly 2 distinct currencies.");
        verify(totals["EUR"] == 3500m, "EUR total correctly sums EUR accounts only (1000 + 2500 = 3500).");
        verify(totals["USD"] == 500m, "USD total isolated to USD account only (500).");
        verify(!totals.ContainsKey("EURUSD"), "Currencies are never mixed or conflated.");
    }

    private static void VerifyConnectionViewModel(Action<bool, string> verify)
    {
        var inst = new StoreInstitutionEntry(10, "Example Bank", "https://fints.examplebank.de/fints3", "12345678", null, "Confirmed");
        var conn = new StoreConnectionEntry(
            1, 10, "User123", "Cust456",
            "Synchronized", "SYS-001", "Active",
            2, true, DateTimeOffset.UtcNow, DateTimeOffset.UtcNow, "3.0");

        var vm = new ConnectionViewModel(conn, inst);

        verify(vm.ConnectionId == 1, "Connection ID preserved.");
        verify(vm.InstitutionName == "Example Bank", "Institution name preserved.");
        verify(vm.EndpointHost == "fints.examplebank.de", "Endpoint host extracted.");
        verify(vm.IsEndpointConfirmed, "Endpoint confirmed flag preserved.");
        verify(vm.CanRefresh, "Synchronized and confirmed connection can refresh.");
        verify(!vm.RequiresReconfirmation, "Confirmed connection does not require reconfirmation.");
        verify(vm.StateDisplay.Contains("Synchronized", StringComparison.Ordinal), "Synchronized state formatted.");

        // Quarantined / unconfirmed connection
        var quarInst = new StoreInstitutionEntry(10, "Example Bank", "https://fints.examplebank.de/fints3", "12345678", null, "Unconfirmed");
        var quarConn = new StoreConnectionEntry(
            2, 10, "User123", null,
            "Quarantined", null, "Pending",
            1, false, null, null, "3.0");

        var quarVm = new ConnectionViewModel(quarConn, quarInst);
        verify(quarVm.RequiresReconfirmation, "Unconfirmed/quarantined connection requires reconfirmation.");
        verify(!quarVm.CanRefresh, "Quarantined connection cannot refresh.");
        verify(quarVm.StateDisplay.Contains("Quarantined", StringComparison.Ordinal), "Quarantined state display formatted.");
    }

    private static void VerifyHostSessionLifecycle(Action<bool, string> verify)
    {
        using var session = new HostSession();
        verify(!session.IsUnlocked, "Host session starts in locked state.");
        verify(session.Accounts.Count == 0, "No accounts when locked.");
        verify(session.Connections.Count == 0, "No connections when locked.");

        bool stateChangedFired = false;
        session.StateChanged += (s, e) => stateChangedFired = true;

        session.IsDiagnosticsEnabled = true;
        verify(session.IsDiagnosticsEnabled, "Diagnostic setting toggled.");
        verify(stateChangedFired, "StateChanged fired on diagnostic toggle.");
    }

    private static void VerifyDialogsAndAccessibility(Action<bool, string> verify)
    {
        // 1. EndpointConfirmationDialog accessibility
        var ep = FinTsEndpoint.Create("https://fints.mybank.de/hbci");
        using (var dlg = new EndpointConfirmationDialog(ep, "My Bank"))
        {
            verify(dlg.AccessibleRole == AccessibleRole.Dialog, "Endpoint dialog role is Dialog.");
            verify(!string.IsNullOrWhiteSpace(dlg.AccessibleName), "Endpoint dialog has AccessibleName.");
            verify(!dlg.IsConfirmed, "Dialog starts unconfirmed.");
            verify(dlg.AcceptButton is not null, "AcceptButton is configured for Enter key.");
            verify(dlg.CancelButton is not null, "CancelButton is configured for Escape key.");
        }

        // 2. ProfileUnlockDialog accessibility
        using var session = new HostSession();
        using (var dlg = new ProfileUnlockDialog(session))
        {
            verify(dlg.AccessibleRole == AccessibleRole.Dialog, "Profile dialog role is Dialog.");
            verify(!string.IsNullOrWhiteSpace(dlg.AccessibleName), "Profile dialog has AccessibleName.");
            verify(dlg.AcceptButton is not null, "AcceptButton is configured for Enter key.");
            verify(dlg.CancelButton is not null, "CancelButton is configured for Escape key.");
        }

        // 3. CredentialPromptDialog accessibility and zeroization
        using (var dlg = new CredentialPromptDialog("Test Bank", "User01", FinTsCredentialKind.Pin))
        {
            verify(dlg.AccessibleRole == AccessibleRole.Dialog, "Credential dialog role is Dialog.");
            verify(!string.IsNullOrWhiteSpace(dlg.AccessibleName), "Credential dialog has AccessibleName.");
            verify(dlg.CapturedCredential is null, "No credential captured initially.");
        }

        // 4. ScaChallengeDialog accessibility
        using (var dlg = new ScaChallengeDialog("photoTAN", "ORD-12345", "Please confirm transfer of 100 EUR."))
        {
            verify(dlg.AccessibleRole == AccessibleRole.Dialog, "SCA dialog role is Dialog.");
            verify(!string.IsNullOrWhiteSpace(dlg.AccessibleName), "SCA dialog has AccessibleName.");
            verify(dlg.CapturedTan is null, "No TAN captured initially.");
        }

        // 5. ConnectionRemovalDialog accessibility
        var storeConn = new StoreConnectionEntry(1, 10, "User", null, "Synchronized", null, "Active", 1, true, null, null, "3.0");
        var storeInst = new StoreInstitutionEntry(10, "Bank", "https://bank.de", "12345678", null, "Confirmed");
        var connVm = new ConnectionViewModel(storeConn, storeInst);
        using (var dlg = new ConnectionRemovalDialog(connVm))
        {
            verify(dlg.AccessibleRole == AccessibleRole.Dialog, "Removal dialog role is Dialog.");
            verify(!dlg.PurgeCachedData, "Default removal choice is config-only (no purge).");
        }

        // 6. SupportExportDialog accessibility & SHA-256 binding
        var diagStore = new EncryptedLocalDiagnosticStore(Path.GetTempPath(), enabled: false);
        var preview = diagStore.CreateSupportPreview();
        using (var dlg = new SupportExportDialog(preview))
        {
            verify(dlg.AccessibleRole == AccessibleRole.Dialog, "Support export dialog role is Dialog.");
            verify(preview.PreviewSha256.Length == 64, "Preview SHA-256 digest is exact 64-hex string.");
        }

        // 7. MainForm accessibility
        using (var mainForm = new MainForm(session))
        {
            verify(mainForm.AccessibleRole == AccessibleRole.Client, "MainForm role is Client.");
            verify(!string.IsNullOrWhiteSpace(mainForm.AccessibleName), "MainForm has AccessibleName.");
            verify(mainForm.MainMenuStrip is not null, "MainForm has MainMenuStrip.");
        }
    }

    private static void VerifyAccountRelinkingWorkflow(Action<bool, string> verify)
    {
        string tempDir = Path.Combine(Path.GetTempPath(), "BroilerFondSelfTestRelink", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);
        const string passphrase = "SelfTest-Relink-Passphrase-2026!";

        try
        {
            using var session = new HostSession();
            session.CreateNewProfile(tempDir, passphrase, "Relink Test Profile");

            var store = session.GetStore() ?? throw new InvalidOperationException("Store is null");
            var snapshot = store.CurrentSnapshot ?? throw new InvalidOperationException("Snapshot is null");

            // Setup: Account 100 (Primary, Active binding 200, balance 300) and Account 101 (Candidate Shell, Quarantined binding 201)
            var now = DateTimeOffset.UtcNow;
            var inst = new StoreInstitutionEntry(1, "Sparkasse Test", "https://localhost", "37040044", null, "Confirmed");
            var conn = new StoreConnectionEntry(2, 1, "User", null, "Synchronized", "SYS1", "Active", 2, true, now, now, "3.0");

            var acc1 = new StoreAccountEntry(100, "Primary Girokonto", "EUR");
            var inc1 = new StoreIncarnationEntry(150, 100, "Active");
            var binding1 = new StoreBindingEntry(
                200, 100, 150, 1, 2, "Active", "FinTs 3.0", "1.0", "https://localhost",
                "DE89370400440532013000", "0532013000", "37040044", null, "EUR", Array.Empty<(string, string, string, string)>());
            var balance1 = new StoreBalanceEntry(
                300, 299, 100, 150, 200, "Booked", "1250.00", "EUR", now, now, false, "HISAL");

            var acc2 = new StoreAccountEntry(101, "Candidate Discovery Shell", "EUR");
            var inc2 = new StoreIncarnationEntry(151, 101, "Active");
            var binding2 = new StoreBindingEntry(
                201, 101, 151, 1, 2, "Quarantined", "FinTs 3.0", "1.0", "https://localhost",
                "DE89370400440532013999", "0532013999", "37040044", null, "EUR", Array.Empty<(string, string, string, string)>());

            List<StoreLedgerRecord> ledger =
            [
                new StoreLedgerRecord(1, "Institution", null, []),
                new StoreLedgerRecord(2, "Connection", null, [(1, "Institution")]),
                new StoreLedgerRecord(100, "Account", null, []),
                new StoreLedgerRecord(150, "AccountIncarnation", null, [(100, "Account")]),
                new StoreLedgerRecord(200, "AccountBinding", null, [(150, "AccountIncarnation"), (2, "Connection")]),
                new StoreLedgerRecord(299, "Observation", null, [(200, "AccountBinding")]),
                new StoreLedgerRecord(300, "BalanceSnapshot", null, [(299, "Observation")]),
                new StoreLedgerRecord(101, "Account", null, []),
                new StoreLedgerRecord(151, "AccountIncarnation", null, [(101, "Account")]),
                new StoreLedgerRecord(201, "AccountBinding", null, [(151, "AccountIncarnation"), (2, "Connection")]),
            ];

            var setupSnapshot = snapshot with
            {
                Profile = snapshot.Profile with { NextId = 500 },
                Institutions = new StoreInstitutionsDocument(snapshot.Profile.ProfileId, snapshot.Profile.BranchId, snapshot.Profile.Generation + 1, [inst]),
                Connections = new StoreConnectionsDocument(snapshot.Profile.ProfileId, snapshot.Profile.BranchId, snapshot.Profile.Generation + 1, [conn]),
                Accounts = new StoreAccountsDocument(
                    snapshot.Profile.ProfileId, snapshot.Profile.BranchId, snapshot.Profile.Generation + 1,
                    [acc1, acc2], [inc1, inc2], [binding1, binding2], []),
                Balances = new StoreBalancesDocument(
                    snapshot.Profile.ProfileId, snapshot.Profile.BranchId, snapshot.Profile.Generation + 1,
                    [balance1]),
                Ledger = new StoreLedgerDocument(
                    snapshot.Profile.ProfileId, snapshot.Profile.BranchId, snapshot.Profile.Generation + 1,
                    ledger),
            };

            store.Save(setupSnapshot);
            session.ReloadData();

            verify(session.Accounts.Count == 2, "Two accounts loaded in session initially.");
            var quarAcc = session.Accounts.First(a => a.AccountId == 101);
            verify(quarAcc.BindingStateDisplay.Contains("Quarantined Candidate", StringComparison.Ordinal), "Account 101 has quarantined candidate binding.");

            // 1. Verify AccountRelinkingDialog accessibility & initial selection
            using (var dlg = new AccountRelinkingDialog(session, 201))
            {
                verify(dlg.AccessibleRole == AccessibleRole.Dialog, "AccountRelinkingDialog role is Dialog.");
                verify(!string.IsNullOrWhiteSpace(dlg.AccessibleName), "AccountRelinkingDialog has AccessibleName.");
                verify(dlg.AcceptButton is not null, "AccountRelinkingDialog has AcceptButton.");
                verify(dlg.CancelButton is not null, "AccountRelinkingDialog has CancelButton.");
            }

            // 2. Deterministic planning
            var currentSnap = store.CurrentSnapshot!;
            var plan = AccountRelinkingPlanner.PlanRelink(currentSnap, 201, 100, pruneOrphanedAccount: true);
            verify(plan.RelinkedBindingId == 201, "Relinked binding ID is 201.");
            verify(plan.TargetAccountId == 100, "Target account ID is 100.");
            verify(plan.TargetIncarnationId == 150, "Target incarnation ID is 150.");
            verify(plan.PrunedAccountId == 101, "Candidate account 101 planned for pruning.");
            verify(plan.PrunedIncarnationId == 151, "Candidate incarnation 151 planned for pruning.");

            // 3. Execution via IProfileStore.RelinkAccountBinding
            var result = store.RelinkAccountBinding(201, 100, passphrase, pruneOrphanedAccount: true);
            verify(result.PrunedAccountId == 101, "Pruned account ID is 101 in result.");

            // Verify post-relinking snapshot
            var postSnap = store.CurrentSnapshot!;
            verify(postSnap.Accounts.Accounts.Count == 1, "Only 1 account remains in profile after pruning.");
            verify(postSnap.Accounts.Accounts[0].Id == 100, "Target account 100 retained.");
            verify(!postSnap.Accounts.Accounts.Any(a => a.Id == 101), "Candidate account 101 was pruned from store.");
            var relinkedBinding = postSnap.Accounts.Bindings.First(b => b.Id == 201);
            verify(relinkedBinding.AccountId == 100, "Binding 201 now linked to account 100.");
            verify(relinkedBinding.State == "Active", "Binding 201 transitioned to Active state.");

            // Verify balance provenance preserved
            var bal = postSnap.Balances.Balances.First(b => b.Id == 300);
            verify(bal.AccountId == 100, "Balance account ID is 100.");
            verify(bal.BindingId == 200, "Balance binding ID permanently retains original origin binding 200.");

            // Verify privacy purge: exactly 1 generation file exists on disk
            string[] genFiles = Directory.GetFiles(tempDir, "generation-*.bffond");
            verify(genFiles.Length == 1, "Privacy purge left exactly 1 generation file on disk.");

            // 4. Session reload reflection
            session.ReloadData();
            verify(session.Accounts.Count == 1, "Session accounts count updated to 1 after relinking.");
            verify(session.Accounts[0].AccountId == 100, "Session account is account 100.");
        }
        finally
        {
            try
            {
                if (Directory.Exists(tempDir))
                {
                    Directory.Delete(tempDir, recursive: true);
                }
            }
            catch
            {
                // Best-effort cleanup.
            }
        }
    }

    private static void VerifyTransactionPresentationAndAccessibility(Action<bool, string> verify)
    {
        var bookingDate = new DateOnly(2026, 10, 15);
        var valDate = new DateOnly(2026, 10, 16);
        var now = DateTimeOffset.UtcNow;

        // 1. Test TransactionViewModel for Credit item
        var creditRecord = new TransactionItemRecord(
            TransactionId: 1001,
            AccountId: 100,
            AccountDisplayName: "Checking Account",
            AccountIban: "DE89370400440532013000",
            AccountIncarnationId: 150,
            AccountBindingId: 200,
            Status: "Booked",
            Kind: "Transfer",
            Amount: 1250.50m,
            Currency: "EUR",
            IsCredit: true,
            BookingDate: bookingDate,
            ValueDate: valDate,
            BankTimestamp: now,
            RetrievedAt: now,
            CounterpartyName: "Arbeitgeber GmbH",
            CounterpartyIban: "DE12500105170648489890",
            CounterpartyBic: "HELAADEF100",
            RemittanceText: "Gehalt Oktober 2026",
            EndToEndId: "E2E-SALARY-2026-10",
            MandateId: null,
            CreditorId: null,
            CustomerReference: "CUST-REF-1",
            BankReference: "BANK-REF-99",
            PurposeCode: "SALA",
            ReturnReasonCode: null,
            RawBankCode: "166",
            UserNote: "Monthly salary",
            Category: "Salary",
            Tags: new[] { "Income", "Fixed" },
            StatementFormat: "camt.053",
            StatementNumber: "2026-10",
            LinkedPendingTransactionId: null,
            LinkedBookedTransactionId: null);

        var creditVm = new TransactionViewModel(creditRecord);
        verify(creditVm.TransactionId == 1001, "Credit transaction ID preserved.");
        verify(creditVm.AccountId == 100, "Account ID preserved.");
        verify(creditVm.IsBooked, "Status Booked isBooked is true.");
        verify(!creditVm.IsPending, "Status Booked isPending is false.");
        verify(creditVm.StatusDisplay == "[✓ Booked]", "Booked status display has non-color checkmark.");
        verify(creditVm.AmountDisplay.Contains("[+] +"), "Credit amount has non-color [+] + indicator.");
        verify(creditVm.AmountDisplay.Contains("1.250,50") || creditVm.AmountDisplay.Contains("1,250.50"), "Credit amount formatted.");
        verify(creditVm.AmountColor == Color.DarkGreen, "Credit amount color is DarkGreen.");
        verify(creditVm.BookingDateDisplay == "15.10.2026", "Booking date formatted as dd.MM.yyyy.");
        verify(creditVm.ValueDateDisplay == "16.10.2026", "Value date formatted as dd.MM.yyyy.");
        verify(creditVm.CounterpartyDisplay == "Arbeitgeber GmbH", "Counterparty name displayed.");
        verify(creditVm.RemittanceSummary == "Gehalt Oktober 2026", "Remittance summary matches.");
        verify(creditVm.CategoryDisplay == "Salary", "Category display matches.");
        verify(creditVm.TagsDisplay == "Income, Fixed", "Tags display matches.");

        // 2. Test TransactionViewModel for Debit / Pending item with null counterparty & long remittance
        string longRemittance = "Rechnung Nr. 2026-9918237198273918273918273918273918273981723981729387192837192837192837";
        var debitRecord = new TransactionItemRecord(
            TransactionId: 1002,
            AccountId: 100,
            AccountDisplayName: "Checking Account",
            AccountIban: "DE89370400440532013000",
            AccountIncarnationId: 150,
            AccountBindingId: 200,
            Status: "Pending",
            Kind: "Debit",
            Amount: 49.95m,
            Currency: "EUR",
            IsCredit: false,
            BookingDate: bookingDate,
            ValueDate: null,
            BankTimestamp: null,
            RetrievedAt: now,
            CounterpartyName: null,
            CounterpartyIban: null,
            CounterpartyBic: null,
            RemittanceText: longRemittance,
            EndToEndId: null,
            MandateId: null,
            CreditorId: null,
            CustomerReference: null,
            BankReference: null,
            PurposeCode: null,
            ReturnReasonCode: null,
            RawBankCode: null,
            UserNote: null,
            Category: null,
            Tags: Array.Empty<string>(),
            StatementFormat: "camt.052",
            StatementNumber: null,
            LinkedPendingTransactionId: null,
            LinkedBookedTransactionId: null);

        var debitVm = new TransactionViewModel(debitRecord);
        verify(debitVm.IsPending, "Pending item isPending is true.");
        verify(!debitVm.IsBooked, "Pending item isBooked is false.");
        verify(debitVm.StatusDisplay == "[⏳ Pending]", "Pending status display has non-color hourglass.");
        verify(debitVm.AmountDisplay.Contains("[-] -"), "Debit amount has non-color [-] - indicator.");
        verify(debitVm.AmountColor == Color.DarkRed, "Debit amount color is DarkRed.");
        verify(debitVm.CounterpartyDisplay == "(Payee Not Disclosed)", "Null counterparty on debit displays '(Payee Not Disclosed)'.");
        verify(debitVm.ValueDateDisplay == "--", "Null value date displays '--'.");
        verify(debitVm.RemittanceSummary.Length <= 60 && debitVm.RemittanceSummary.EndsWith("..."), "Long remittance summary truncated to 60 chars ending in '...'.");

        // 3. Test HostSession search and CSV Export dialog
        string tempDir = Path.Combine(Path.GetTempPath(), "BroilerFondHostTxTest", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);
        const string passphrase = "TxTest-Passphrase-2026!";

        try
        {
            using var session = new HostSession();
            session.CreateNewProfile(tempDir, passphrase, "Tx Test Profile");
            var store = session.GetStore() ?? throw new InvalidOperationException("Store is null");
            var snap = store.CurrentSnapshot ?? throw new InvalidOperationException("Snapshot is null");

            var inst = new StoreInstitutionEntry(1, "Test Bank", "https://localhost", "12345678", null, "Confirmed");
            var conn = new StoreConnectionEntry(2, 1, "User", null, "Synchronized", "SYS1", "Active", 1, true, now, now, "3.0");

            var acc = new StoreAccountEntry(100, "Checking Account", "EUR");
            var inc = new StoreIncarnationEntry(150, 100, "Active");
            var binding = new StoreBindingEntry(
                200, 100, 150, 1, 2, "Active", "FinTs", "3.0", "https://localhost",
                "DE89370400440532013000", null, null, null, "EUR", []);

            var obs = new StoreTransactionObservationEntry(
                Id: 300,
                AccountBindingId: 200,
                RetrievedAt: now,
                StatementFormat: "camt.053",
                StatementNumber: "1",
                PageToken: null);

            var tx1 = new StoreTransactionEntry(1001, 100, 150, "Booked", "Transfer");
            var rev1 = new StoreTransactionRevisionEntry(
                Id: 2001,
                TransactionId: 1001,
                ObservationId: 300,
                AccountId: 100,
                AccountIncarnationId: 150,
                AccountBindingId: 200,
                Status: "Booked",
                Kind: "Transfer",
                Amount: "1250.50",
                Currency: "EUR",
                IsCredit: true,
                BookingDate: bookingDate,
                ValueDate: valDate,
                BankTimestamp: now,
                RetrievedAt: now,
                CounterpartyName: "Arbeitgeber GmbH",
                CounterpartyIban: "DE12500105170648489890",
                CounterpartyBic: "HELAADEF100",
                RemittanceText: "Gehalt Oktober 2026",
                EndToEndId: "E2E-SALARY-2026-10",
                MandateId: null,
                CreditorId: null,
                CustomerReference: "CUST-REF-1",
                BankReference: "BANK-REF-99",
                PurposeCode: "SALA",
                ReturnReasonCode: null,
                RawBankCode: "166");

            var ann1 = new StoreAnnotationRevisionEntry(
                Id: 4001,
                TransactionId: 1001,
                RevisionTimestamp: now,
                UserNote: "Monthly salary",
                Category: "Salary",
                Tags: ["Income", "Fixed"]);

            var ledgerRecords = new List<StoreLedgerRecord>
            {
                new(1, "Institution", null, []),
                new(2, "Connection", null, [(1, "Institution")]),
                new(100, "Account", null, []),
                new(150, "AccountIncarnation", null, [(100, "Account")]),
                new(200, "AccountBinding", null, [(150, "AccountIncarnation"), (2, "Connection")]),
                new(300, "Observation", null, [(200, "AccountBinding")]),
                new(1001, "Transaction", null, [(100, "Account"), (150, "AccountIncarnation")]),
                new(2001, "TransactionRevision", null, [(1001, "Transaction"), (300, "Observation")]),
                new(4001, "AnnotationRevision", null, [(1001, "Transaction")]),
            };

            var txDoc = new StoreTransactionsDocument(
                snap.Profile.ProfileId, snap.Profile.BranchId, snap.Profile.Generation + 1,
                [obs], [tx1], [rev1], [ann1], [], []);

            var updatedSnap = snap with
            {
                Profile = snap.Profile with { NextId = 5000 },
                Institutions = new StoreInstitutionsDocument(snap.Profile.ProfileId, snap.Profile.BranchId, snap.Profile.Generation + 1, [inst]),
                Connections = new StoreConnectionsDocument(snap.Profile.ProfileId, snap.Profile.BranchId, snap.Profile.Generation + 1, [conn]),
                Accounts = new StoreAccountsDocument(snap.Profile.ProfileId, snap.Profile.BranchId, snap.Profile.Generation + 1, [acc], [inc], [binding], []),
                Transactions = txDoc,
                Ledger = new StoreLedgerDocument(snap.Profile.ProfileId, snap.Profile.BranchId, snap.Profile.Generation + 1, ledgerRecords)
            };

            store.Save(updatedSnap);
            session.ReloadData();

            verify(session.Transactions.Count == 1, "Session loaded 1 transaction.");
            var loadedTx = session.Transactions[0];
            verify(loadedTx.TransactionId == 1001, "Loaded transaction ID matches.");
            verify(loadedTx.Amount == 1250.50m, "Loaded transaction amount is exact decimal 1250.50.");

            // Search filter tests
            var filterAll = session.SearchTransactions(TransactionQueryFilter.Empty);
            verify(filterAll.Count == 1, "Empty filter returns all transactions.");

            var filterMatchingText = session.SearchTransactions(new TransactionQueryFilter(TextQuery: "Gehalt"));
            verify(filterMatchingText.Count == 1, "Text query 'Gehalt' matches remittance.");

            var filterNonMatching = session.SearchTransactions(new TransactionQueryFilter(TextQuery: "NonExistentKeyword"));
            verify(filterNonMatching.Count == 0, "Non-matching text query returns 0 results.");

            var filterCredit = session.SearchTransactions(new TransactionQueryFilter(IsCredit: true));
            verify(filterCredit.Count == 1, "IsCredit = true returns 1 result.");

            var filterDebit = session.SearchTransactions(new TransactionQueryFilter(IsCredit: false));
            verify(filterDebit.Count == 0, "IsCredit = false returns 0 results.");

            // CSV Preview test
            (string previewText, int totalCount) = session.GenerateCsvPreview(null, maxRows: 5);
            verify(totalCount == 1, "Preview reports totalCount == 1.");
            verify(previewText.Contains("BookingDate,ValueDate,Status,Account,AccountIban"), "CSV header present in preview.");
            verify(previewText.Contains("1250.50"), "Exact decimal amount 1250.50 present in CSV preview.");
            verify(previewText.Contains("Arbeitgeber GmbH"), "Counterparty present in CSV preview.");

            // CSV Export to disk test
            string exportPath = Path.Combine(tempDir, "test-export.csv");
            int exportedCount = session.ExportTransactionsToCsv(null, exportPath);
            verify(exportedCount == 1, "Export wrote 1 record.");
            verify(File.Exists(exportPath), "Export file exists on local storage.");
            string exportedCsv = File.ReadAllText(exportPath);
            verify(exportedCsv.Contains("1250.50"), "Exported CSV contains exact money amount.");
            verify(!exportedCsv.Contains("PIN", StringComparison.Ordinal) && !exportedCsv.Contains("Passphrase", StringComparison.OrdinalIgnoreCase), "Export contains zero PIN/passphrase secrets.");

            // 4. CsvExportDialog accessibility
            using (var csvDlg = new CsvExportDialog(session))
            {
                verify(csvDlg.AccessibleRole == AccessibleRole.Dialog, "CsvExportDialog role is Dialog.");
                verify(!string.IsNullOrWhiteSpace(csvDlg.AccessibleName), "CsvExportDialog has AccessibleName.");
                verify(csvDlg.AcceptButton is not null, "CsvExportDialog has AcceptButton configured.");
                verify(csvDlg.CancelButton is not null, "CsvExportDialog has CancelButton configured.");
            }

            // 5. MainForm with Transactions accessibility
            using (var mainForm = new MainForm(session))
            {
                verify(mainForm.AccessibleRole == AccessibleRole.Client, "MainForm role is Client.");
                verify(mainForm.MainMenuStrip is not null, "MainForm has MainMenuStrip.");
            }
        }
        finally
        {
            try
            {
                if (Directory.Exists(tempDir))
                {
                    Directory.Delete(tempDir, recursive: true);
                }
            }
            catch
            {
                // Best-effort cleanup.
            }
        }
    }

    private static void VerifyMultibankDashboardAndFreshness(Action<bool, string> verify)
    {
        var now = DateTimeOffset.UtcNow;

        var inst1 = new StoreInstitutionEntry(10, "Sparkasse KölnBonn", "https://fints.sparkasse.de", "37050198", null, "Confirmed");
        var inst2 = new StoreInstitutionEntry(20, "Deutsche Bank", "https://fints.deutsche-bank.de", "10070000", null, "Confirmed");

        // Verify M3 connection states on ConnectionViewModel
        var connConnected = new StoreConnectionEntry(1, 10, "User1", null, "Connected", "SYS1", "Active", 1, true, now, now, "3.0");
        var vmConnected = new ConnectionViewModel(connConnected, inst1);
        verify(vmConnected.StateDisplay.Contains("Connected"), "Connected state badge rendered.");
        verify(vmConnected.CanRefresh, "Connected and confirmed connection can refresh.");
        verify(!vmConnected.CanReauthenticate, "Connected connection does not need reauthentication.");

        var connReauth = new StoreConnectionEntry(2, 10, "User2", null, "ReauthenticationRequired", "SYS2", "Pending", 1, true, now, now, "3.0");
        var vmReauth = new ConnectionViewModel(connReauth, inst1);
        verify(vmReauth.StateDisplay.Contains("Reauthentication Required"), "ReauthenticationRequired badge rendered.");
        verify(vmReauth.CanReauthenticate, "ReauthenticationRequired connection can be reauthenticated.");
        verify(!vmReauth.CanRefresh, "ReauthenticationRequired connection cannot refresh without fresh PIN.");

        var connTemp = new StoreConnectionEntry(3, 20, "User3", null, "TemporarilyUnavailable", "SYS3", "Active", 1, true, now, now, "3.0");
        var vmTemp = new ConnectionViewModel(connTemp, inst2);
        verify(vmTemp.StateDisplay.Contains("Temporarily Unavailable"), "TemporarilyUnavailable badge rendered.");
        verify(vmTemp.CanReauthenticate, "TemporarilyUnavailable connection can be reauthenticated.");

        var connChallenge = new StoreConnectionEntry(4, 20, "User4", null, "ChallengeRequired", "SYS4", "Pending", 1, true, now, now, "3.0");
        var vmChallenge = new ConnectionViewModel(connChallenge, inst2);
        verify(vmChallenge.StateDisplay.Contains("Challenge Required"), "ChallengeRequired badge rendered.");

        var connAuth = new StoreConnectionEntry(5, 20, "User5", null, "Authenticating", "SYS5", "Pending", 1, true, now, now, "3.0");
        var vmAuth = new ConnectionViewModel(connAuth, inst2);
        verify(vmAuth.StateDisplay.Contains("Authenticating"), "Authenticating badge rendered.");

        var connDisabled = new StoreConnectionEntry(6, 20, "User6", null, "Disabled", "SYS6", "Inactive", 1, false, now, now, "3.0");
        var vmDisabled = new ConnectionViewModel(connDisabled, inst2);
        verify(vmDisabled.StateDisplay.Contains("Disabled"), "Disabled badge rendered.");
        verify(!vmDisabled.CanRefresh, "Disabled connection cannot refresh.");

        // Verify account freshness indicators
        var acc1 = new StoreAccountEntry(101, "Girokonto Sparkasse", "EUR");
        var bin1 = new StoreBindingEntry(201, 101, 301, 10, 1, "Active", "FinTS 3.0 PIN/TAN", "1.0", "https://fints.sparkasse.de", "DE89370501980000000001", null, null, null, "EUR", []);
        var balFresh = new StoreBalanceEntry(401, 501, 101, 301, 201, "Booked", "2500.00", "EUR", now.AddMinutes(-10), now.AddMinutes(-10), false, "HISAL");
        var vmFresh = new AccountViewModel(acc1, bin1, balFresh, null, now);
        verify(vmFresh.FreshnessDisplay.Contains("Fresh"), "Account balance < 1h is marked Fresh.");
        verify(!vmFresh.IsStale, "Fresh account is not marked stale.");

        var balRecent = new StoreBalanceEntry(402, 502, 101, 301, 201, "Booked", "2500.00", "EUR", now.AddHours(-4), now.AddHours(-4), false, "HISAL");
        var vmRecent = new AccountViewModel(acc1, bin1, balRecent, null, now);
        verify(vmRecent.FreshnessDisplay.Contains("Recent"), "Account balance between 1h and 24h is marked Recent.");
        verify(!vmRecent.IsStale, "Recent account is not marked stale.");

        var balStale = new StoreBalanceEntry(403, 503, 101, 301, 201, "Booked", "2500.00", "EUR", now.AddHours(-30), now.AddHours(-30), false, "HISAL");
        var vmStale = new AccountViewModel(acc1, bin1, balStale, null, now);
        verify(vmStale.FreshnessDisplay.Contains("Stale"), "Account balance > 24h is marked Stale.");
        verify(vmStale.IsStale, "Stale account is marked stale.");
    }

    private static void VerifyConnectionManagementActions(Action<bool, string> verify)
    {
        // 1. InstitutionSetupDialog accessibility and verification
        using (var setupDlg = new InstitutionSetupDialog())
        {
            verify(setupDlg.AccessibleRole == AccessibleRole.Dialog, "InstitutionSetupDialog role is Dialog.");
            verify(!string.IsNullOrWhiteSpace(setupDlg.AccessibleName), "InstitutionSetupDialog has AccessibleName.");
            verify(setupDlg.AcceptButton is not null, "InstitutionSetupDialog has AcceptButton configured.");
            verify(setupDlg.CancelButton is not null, "InstitutionSetupDialog has CancelButton configured.");
        }

        // 2. EndpointConfirmationDialog accessibility and confirmation logic
        var testEndpoint = FinTsEndpoint.Create("https://fints.testbank.de/hbci");
        using (var confirmDlg = new EndpointConfirmationDialog(testEndpoint, "Test Bank"))
        {
            verify(confirmDlg.AccessibleRole == AccessibleRole.Dialog, "EndpointConfirmationDialog role is Dialog.");
            verify(!string.IsNullOrWhiteSpace(confirmDlg.AccessibleName), "EndpointConfirmationDialog has AccessibleName.");
            verify(!confirmDlg.IsConfirmed, "EndpointConfirmationDialog starts unconfirmed.");
        }

        // 3. ConnectionRemovalDialog accessibility
        var inst = new StoreInstitutionEntry(1, "Test Bank", "https://fints.testbank.de", "10020030", null, "Confirmed");
        var conn = new StoreConnectionEntry(2, 1, "user1", null, "Connected", "SYS1", "Active", 1, true, null, null, "3.0");
        var connVm = new ConnectionViewModel(conn, inst);
        using (var removeDlg = new ConnectionRemovalDialog(connVm))
        {
            verify(removeDlg.AccessibleRole == AccessibleRole.Dialog, "ConnectionRemovalDialog role is Dialog.");
            verify(!string.IsNullOrWhiteSpace(removeDlg.AccessibleName), "ConnectionRemovalDialog has AccessibleName.");
            verify(removeDlg.AcceptButton is not null, "ConnectionRemovalDialog has AcceptButton configured.");
            verify(removeDlg.CancelButton is not null, "ConnectionRemovalDialog has CancelButton configured.");
        }
    }

    private static void VerifyRedactedSupportBundleExportPreview(Action<bool, string> verify)
    {
        string tempDir = Path.Combine(Path.GetTempPath(), "bf-host-support-" + Guid.NewGuid().ToString("N"));
        try
        {
            Directory.CreateDirectory(tempDir);
            var diagStore = new EncryptedLocalDiagnosticStore(tempDir, enabled: true);
            diagStore.Record(
                LocalDiagnosticCode.SessionStarted,
                LocalDiagnosticOutcome.Succeeded,
                DateTimeOffset.UtcNow);

            var preview = diagStore.CreateSupportPreview();
            verify(!string.IsNullOrWhiteSpace(preview.PreviewSha256), "Support preview produces non-empty SHA-256 digest.");
            verify(preview.PreviewSha256.Length == 64, "SHA-256 digest is exactly 64 hex characters.");
            verify(!string.IsNullOrWhiteSpace(preview.Json), "Support preview produces non-empty JSON payload.");

            // Zero-secret invariant: diagnostic export never includes credentials
            string jsonLower = preview.Json.ToLowerInvariant();
            verify(!jsonLower.Contains("pincode") && !jsonLower.Contains("tanvalue") && !jsonLower.Contains("pin") && !jsonLower.Contains("tan"),
                "Diagnostic support preview contains zero PIN or TAN secrets.");

            // Dialog review accessibility
            using (var exportDlg = new SupportExportDialog(preview))
            {
                verify(exportDlg.AccessibleRole == AccessibleRole.Dialog, "SupportExportDialog role is Dialog.");
                verify(!string.IsNullOrWhiteSpace(exportDlg.AccessibleName), "SupportExportDialog has AccessibleName.");
                verify(exportDlg.CancelButton is not null, "SupportExportDialog has CancelButton configured.");
            }
        }
        finally
        {
            try
            {
                if (Directory.Exists(tempDir))
                {
                    Directory.Delete(tempDir, recursive: true);
                }
            }
            catch { }
        }
    }

    private static void VerifySafeModeAndPaymentWorkflows(Action<bool, string> verify)
    {
        var now = DateTimeOffset.UtcNow;

        // 1. PaymentDraftViewModel presentation
        var draftEntry = new StorePaymentDraftEntry(
            Id: 501,
            CurrentRevisionId: 601,
            SourceAccountId: 101,
            SourceAccountBindingId: 201,
            SourceAccountIncarnationId: 301,
            CreatedAt: now.AddHours(-1),
            UpdatedAt: now.AddMinutes(-30));

        var revEntry = new StorePaymentDraftRevisionEntry(
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
            Amount: 125.50m,
            Currency: "EUR",
            RemittanceUnstructured: "Invoice 2026-10",
            RemittanceCreditorReference: null,
            ExecutionMode: "Standard",
            ExecutionDate: DateOnly.FromDateTime(DateTime.UtcNow.Date));

        var draftVm = new PaymentDraftViewModel(draftEntry, revEntry, "Primary Girokonto");
        verify(draftVm.DraftId == 501, "Draft ID matches.");
        verify(draftVm.CurrentRevisionId == 601, "Draft current revision matches.");
        verify(draftVm.SourceAccountId == 101, "Source account ID matches.");
        verify(draftVm.SourceAccountName == "Primary Girokonto", "Source account name matches.");
        verify(draftVm.BeneficiaryName == "Max Mustermann", "Beneficiary name matches.");
        verify(draftVm.BeneficiaryIban == "DE89370400440532013000", "Beneficiary IBAN matches.");
        verify(draftVm.Amount == 125.50m, "Draft amount matches.");
        verify(draftVm.Currency == "EUR", "Draft currency is EUR.");
        verify(draftVm.AmountDisplay.Contains("125,50", StringComparison.Ordinal) || draftVm.AmountDisplay.Contains("125.50", StringComparison.Ordinal), "Amount display formatted.");
        verify(draftVm.RemittanceDisplay == "Invoice 2026-10", "Unstructured remittance display matches.");

        // Structured remittance
        var revStructured = revEntry with
        {
            RemittanceUnstructured = null,
            RemittanceCreditorReference = "RF18539007547034"
        };
        var draftStructuredVm = new PaymentDraftViewModel(draftEntry, revStructured, "Primary Girokonto");
        verify(draftStructuredVm.RemittanceDisplay.Contains("RF18539007547034", StringComparison.Ordinal), "Structured creditor reference displayed.");

        // 2. PaymentAttemptViewModel presentation & state flags
        var vopMatch = new StorePaymentVoPResultEntry(
            Status: nameof(VerificationOfPayeeStatus.Match),
            CheckedAt: now,
            MatchedName: "Max Mustermann",
            ReasonCode: null,
            WarningMessage: null,
            UserOverride: false);

        var attemptEntry = new StorePaymentAttemptEntry(
            Id: 701,
            DraftId: 501,
            DraftRevisionId: 601,
            SourceAccountId: 101,
            SourceAccountBindingId: 201,
            SourceAccountIncarnationId: 301,
            EndToEndId: "BF-20261019-ABCD1234EF56",
            State: nameof(PaymentState.Accepted),
            VoPResult: vopMatch,
            BankReference: "BANK-REF-9999",
            ResponseCode: "0010",
            StatusDescription: "Order executed successfully",
            CreatedAt: now,
            UpdatedAt: now);

        var transitionEntry = new StorePaymentStateTransitionEntry(
            Id: 801,
            AttemptId: 701,
            FromState: nameof(PaymentState.Submitting),
            ToState: nameof(PaymentState.Accepted),
            Timestamp: now,
            Reason: "Order accepted by bank",
            BankCode: "0010");

        var attemptVm = new PaymentAttemptViewModel(attemptEntry, draftVm, [transitionEntry]);
        verify(attemptVm.AttemptId == 701, "Attempt ID matches.");
        verify(attemptVm.EndToEndId == "BF-20261019-ABCD1234EF56", "EndToEndId matches.");
        verify(attemptVm.IsAccepted, "Attempt state is accepted.");
        verify(attemptVm.CanReconcile, "Accepted attempt can be reconciled.");
        verify(!attemptVm.CanRetry, "Accepted attempt cannot be retried.");
        verify(attemptVm.StateDisplay.Contains("Accepted by Bank", StringComparison.Ordinal), "Accepted state formatted with badge.");
        verify(attemptVm.VoPStatusDisplay.Contains("Match", StringComparison.Ordinal), "VoP Match status formatted with badge.");
        verify(attemptVm.Transitions.Count == 1, "One transition in audit trail.");
        verify(attemptVm.Transitions[0].TransitionDisplay == "Submitting -> Accepted", "Transition display formatted.");

        // Canonical states and flags
        var unknownAttempt = new PaymentAttemptViewModel(attemptEntry with { State = nameof(PaymentState.Unknown) }, draftVm, []);
        verify(unknownAttempt.IsUnknown, "Unknown state recognized.");
        verify(unknownAttempt.CanReconcile, "Unknown state can be reconciled.");
        verify(!unknownAttempt.CanRetry, "Unknown state cannot be automatically retried.");

        var rejectedAttempt = new PaymentAttemptViewModel(attemptEntry with { State = nameof(PaymentState.Rejected) }, draftVm, []);
        verify(rejectedAttempt.IsRejected, "Rejected state recognized.");
        verify(rejectedAttempt.CanRetry, "Rejected attempt can be retried.");
        verify(!rejectedAttempt.CanReconcile, "Rejected attempt cannot be reconciled.");

        var cancelledAttempt = new PaymentAttemptViewModel(attemptEntry with { State = nameof(PaymentState.AuthorizationCancelled) }, draftVm, []);
        verify(cancelledAttempt.IsCancelled, "Cancelled state recognized.");
        verify(cancelledAttempt.CanRetry, "Cancelled attempt can be retried.");

        // VoP badge checks
        var closeMatchAttempt = new PaymentAttemptViewModel(
            attemptEntry with { VoPResult = vopMatch with { Status = nameof(VerificationOfPayeeStatus.CloseMatch), MatchedName = "Maximilian Mustermann" } },
            draftVm, []);
        verify(closeMatchAttempt.VoPStatusDisplay.Contains("Close Match", StringComparison.Ordinal), "Close Match displayed.");

        var noMatchAttempt = new PaymentAttemptViewModel(
            attemptEntry with { VoPResult = vopMatch with { Status = nameof(VerificationOfPayeeStatus.NoMatch) } },
            draftVm, []);
        verify(noMatchAttempt.VoPStatusDisplay.Contains("Mismatch Warning", StringComparison.Ordinal), "VoP Mismatch warning displayed.");

        var unavailAttempt = new PaymentAttemptViewModel(
            attemptEntry with { VoPResult = vopMatch with { Status = nameof(VerificationOfPayeeStatus.Unavailable) } },
            draftVm, []);
        verify(unavailAttempt.VoPStatusDisplay.Contains("Unavailable", StringComparison.Ordinal), "VoP Unavailable displayed.");

        // 3. IBAN validation logic
        verify(SepaIbanValidator.Validate("DE89 3704 0044 0532 0130 00", out string normalizedIban), "Valid DE IBAN passes validation.");
        verify(normalizedIban == "DE89370400440532013000", "IBAN spaces removed and normalized.");
        verify(!SepaIbanValidator.Validate("DE89370400440532013001", out _), "Invalid checksum DE IBAN fails.");
        verify(!SepaIbanValidator.Validate("INVALID_IBAN", out _), "Invalid format IBAN fails.");
        verify(!SepaIbanValidator.Validate(string.Empty, out _), "Empty IBAN fails.");

        // 4. PaymentReviewDialog accessibility and VoP acknowledgment
        using (var dlgMatch = new PaymentReviewDialog(
            "Primary Girokonto", "Max Mustermann", "DE89370400440532013000", null,
            100.00m, "EUR", "Test payment", DateOnly.FromDateTime(DateTime.UtcNow.Date),
            vopMatch))
        {
            verify(dlgMatch.AccessibleRole == AccessibleRole.Dialog, "PaymentReviewDialog role is Dialog.");
            verify(!string.IsNullOrWhiteSpace(dlgMatch.AccessibleName), "PaymentReviewDialog has AccessibleName.");
            verify(dlgMatch.AcceptButton is not null && dlgMatch.IsAuthorizeEnabled, "PaymentReviewDialog with VoP Match has enabled Authorize button.");
            verify(!dlgMatch.WasWarningAcknowledged, "Warning ack not checked for match.");
        }

        using (var dlgNoMatch = new PaymentReviewDialog(
            "Primary Girokonto", "Fraudster Impersonator", "DE89370400440532013000", null,
            100.00m, "EUR", "Test payment", DateOnly.FromDateTime(DateTime.UtcNow.Date),
            vopMatch with { Status = nameof(VerificationOfPayeeStatus.NoMatch) }))
        {
            verify(dlgNoMatch.AcceptButton is not null && !dlgNoMatch.IsAuthorizeEnabled,
                "PaymentReviewDialog with VoP NoMatch disables Authorize button until warning is acknowledged.");
        }

        // 5. HostSession payment lifecycle, durable commit, safe mode toggle, and reconciliation
        string tempDir = Path.Combine(Path.GetTempPath(), "BroilerFondSelfTestPayments", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);
        const string passphrase = "SelfTest-Payments-Passphrase-2026!";

        try
        {
            using var session = new HostSession();
            session.CreateNewProfile(tempDir, passphrase, "Payment Test Profile");

            var store = session.GetStore() ?? throw new InvalidOperationException("Store is null");
            var snap = store.CurrentSnapshot ?? throw new InvalidOperationException("Snapshot is null");

            var inst = new StoreInstitutionEntry(1, "Sparkasse Test", "https://localhost", "37040044", null, "Confirmed");
            var conn = new StoreConnectionEntry(2, 1, "User", null, "Synchronized", "SYS1", "Active", 2, true, now, now, "3.0");
            var acc = new StoreAccountEntry(100, "Primary Girokonto", "EUR");
            var inc = new StoreIncarnationEntry(150, 100, "Active");
            var binding = new StoreBindingEntry(
                200, 100, 150, 1, 2, "Active", "FinTs 3.0", "1.0", "https://localhost",
                "DE89370400440532013000", "0532013000", "37040044", null, "EUR", []);
            var balance = new StoreBalanceEntry(
                300, 299, 100, 150, 200, "Booked", "5000.00", "EUR", now, now, false, "HISAL");

            List<StoreLedgerRecord> ledger =
            [
                new StoreLedgerRecord(1, "Institution", null, []),
                new StoreLedgerRecord(2, "Connection", null, [(1, "Institution")]),
                new StoreLedgerRecord(100, "Account", null, []),
                new StoreLedgerRecord(150, "AccountIncarnation", null, [(100, "Account")]),
                new StoreLedgerRecord(200, "AccountBinding", null, [(150, "AccountIncarnation"), (2, "Connection")]),
                new StoreLedgerRecord(299, "Observation", null, [(200, "AccountBinding")]),
                new StoreLedgerRecord(300, "BalanceSnapshot", null, [(299, "Observation")]),
            ];

            var setupSnap = snap with
            {
                Profile = snap.Profile with { NextId = 500 },
                Institutions = new StoreInstitutionsDocument(snap.Profile.ProfileId, snap.Profile.BranchId, snap.Profile.Generation + 1, [inst]),
                Connections = new StoreConnectionsDocument(snap.Profile.ProfileId, snap.Profile.BranchId, snap.Profile.Generation + 1, [conn]),
                Accounts = new StoreAccountsDocument(
                    snap.Profile.ProfileId, snap.Profile.BranchId, snap.Profile.Generation + 1,
                    [acc], [inc], [binding], []),
                Balances = new StoreBalancesDocument(
                    snap.Profile.ProfileId, snap.Profile.BranchId, snap.Profile.Generation + 1,
                    [balance]),
                Ledger = new StoreLedgerDocument(
                    snap.Profile.ProfileId, snap.Profile.BranchId, snap.Profile.Generation + 1,
                    ledger),
            };

            store.Save(setupSnap);
            session.ReloadData();

            // Safe Mode verification
            verify(!session.IsSafeModeActive, "Safe Mode is initially inactive.");
            session.IsSafeModeActive = true;
            verify(session.IsSafeModeActive, "Safe Mode toggled to active.");

            bool draftSafeModeBlocked = false;
            try
            {
                session.CreatePaymentDraft(100, "Recipient", "DE89370400440532013000", null, 50m, "Remittance", null, "Standard", DateOnly.FromDateTime(DateTime.UtcNow.Date));
            }
            catch (InvalidOperationException)
            {
                draftSafeModeBlocked = true;
            }
            verify(draftSafeModeBlocked, "CreatePaymentDraft blocked when Safe Mode is active.");

            session.IsSafeModeActive = false;
            verify(!session.IsSafeModeActive, "Safe Mode toggled back to inactive.");

            // Dialog accessibility check
            using (var dlgInit = new PaymentInitiationDialog(session))
            {
                verify(dlgInit.AccessibleRole == AccessibleRole.Dialog, "PaymentInitiationDialog role is Dialog.");
                verify(!string.IsNullOrWhiteSpace(dlgInit.AccessibleName), "PaymentInitiationDialog has AccessibleName.");
                verify(dlgInit.CancelButton is not null, "PaymentInitiationDialog has CancelButton.");
            }

            // Create draft
            var draft = session.CreatePaymentDraft(
                sourceAccountId: 100,
                beneficiaryName: "Recipient Partner",
                beneficiaryIban: "DE89370400440532013000",
                beneficiaryBic: null,
                amount: 75.00m,
                remittanceUnstructured: "Invoice 123",
                remittanceCreditorReference: null,
                executionMode: "Standard",
                executionDate: DateOnly.FromDateTime(DateTime.UtcNow.Date));

            verify(session.PaymentDrafts.Count == 1, "Payment draft added to session.");
            verify(draft.BeneficiaryName == "Recipient Partner", "Draft beneficiary name preserved.");
            verify(draft.Amount == 75.00m, "Draft amount preserved.");

            // Create attempt (pre-send durable commit)
            var attempt = session.CreatePaymentAttempt(
                draftId: draft.DraftId,
                initialState: nameof(PaymentState.AwaitingAuthorization),
                vopResult: vopMatch);

            verify(session.PaymentAttempts.Count == 1, "Payment attempt added to session.");
            verify(attempt.DraftId == draft.DraftId, "Attempt linked to draft ID.");
            verify(!string.IsNullOrWhiteSpace(attempt.EndToEndId), "Pre-send durable EndToEndId allocated.");
            verify(attempt.Transitions.Count == 1, "Initial state transition recorded.");

            // Submission sequence: AwaitingAuthorization -> Submitting -> Accepted
            session.RecordPaymentTransition(attempt.AttemptId, nameof(PaymentState.Submitting), "Order submitting across wire");
            session.RecordPaymentTransition(attempt.AttemptId, nameof(PaymentState.Accepted), "Bank accepted order", bankCode: "0010", bankReference: "REF-100200");

            var acceptedAttempt = session.PaymentAttempts.First(a => a.AttemptId == attempt.AttemptId);
            verify(acceptedAttempt.IsAccepted, "Attempt transitioned to Accepted.");
            verify(acceptedAttempt.BankReference == "REF-100200", "Bank reference recorded.");
            verify(acceptedAttempt.ResponseCode == "0010", "Bank return code recorded.");
            verify(acceptedAttempt.CanReconcile, "Accepted attempt can be reconciled.");
            verify(acceptedAttempt.Transitions.Count == 3, "Audit trail contains 3 transitions.");

            // Reconciliation against booking
            session.ReconcilePaymentAttempt(acceptedAttempt.AttemptId, 999, "Statement booking confirmed");
            var reconciledAttempt = session.PaymentAttempts.First(a => a.AttemptId == attempt.AttemptId);
            verify(reconciledAttempt.IsReconciled, "Payment attempt transitioned to Reconciled.");
            verify(!reconciledAttempt.CanReconcile, "Reconciled attempt cannot be reconciled again.");

            // Durable Unknown outcome verification
            var draft2 = session.CreatePaymentDraft(100, "Unknown Wire Test", "DE89370400440532013000", null, 10m, null, null, "Standard", DateOnly.FromDateTime(DateTime.UtcNow.Date));
            var attempt2 = session.CreatePaymentAttempt(draft2.DraftId, nameof(PaymentState.Submitting));
            session.RecordPaymentTransition(attempt2.AttemptId, nameof(PaymentState.Unknown), "Network timeout after byte transfer; durable unknown");

            var unknownAttemptFromSession = session.PaymentAttempts.First(a => a.AttemptId == attempt2.AttemptId);
            verify(unknownAttemptFromSession.IsUnknown, "Payment outcome durably Unknown.");
            verify(unknownAttemptFromSession.CanReconcile, "Unknown outcome requires user reconciliation.");
            verify(!unknownAttemptFromSession.CanRetry, "Unknown outcome cannot be automatically retried.");

            // Rejected attempt retry verification
            var draft3 = session.CreatePaymentDraft(100, "Rejected Wire Test", "DE89370400440532013000", null, 20m, null, null, "Standard", DateOnly.FromDateTime(DateTime.UtcNow.Date));
            var attempt3 = session.CreatePaymentAttempt(draft3.DraftId, nameof(PaymentState.Submitting));
            session.RecordPaymentTransition(attempt3.AttemptId, nameof(PaymentState.Rejected), "Insufficient funds", bankCode: "9900");

            var rejectedAttemptFromSession = session.PaymentAttempts.First(a => a.AttemptId == attempt3.AttemptId);
            verify(rejectedAttemptFromSession.IsRejected, "Payment outcome is Rejected.");
            verify(rejectedAttemptFromSession.CanRetry, "Rejected payment can be retried.");

            // Retrying creates new attempt linked to the same draft
            var retryAttempt = session.CreatePaymentAttempt(rejectedAttemptFromSession.DraftId, nameof(PaymentState.AwaitingAuthorization));
            verify(retryAttempt.DraftId == rejectedAttemptFromSession.DraftId, "Retry attempt retains original draft ID.");
            verify(retryAttempt.AttemptId != rejectedAttemptFromSession.AttemptId, "Retry attempt receives a fresh immutable attempt ID.");
            verify(retryAttempt.EndToEndId != rejectedAttemptFromSession.EndToEndId, "Retry attempt receives a fresh immutable EndToEndId.");
        }
        finally
        {
            try
            {
                if (Directory.Exists(tempDir))
                {
                    Directory.Delete(tempDir, recursive: true);
                }
            }
            catch { }
        }
    }

    private static void VerifyEverydayPaymentsAndStandingOrders(Action<bool, string> verify)
    {
        var now = DateTimeOffset.UtcNow;
        var today = DateOnly.FromDateTime(DateTime.UtcNow.Date);

        // 1. BeneficiaryTemplateViewModel presentation
        var tmplEntry = new StoreBeneficiaryTemplateEntry(
            Id: 50,
            Label: "Landlord",
            BeneficiaryName: "Hausverwaltung Schmidt",
            BeneficiaryIban: "DE89370400440532013000",
            BeneficiaryBic: "SPKADE21XXX",
            DefaultRemittanceUnstructured: "Miete Wohnung 4B",
            DefaultRemittanceCreditorReference: null,
            DefaultAmount: 950.00m,
            DefaultCurrency: "EUR",
            DefaultExecutionMode: "StandardCreditTransfer",
            IsSoftDeleted: false,
            CreatedAt: now.AddDays(-10),
            UpdatedAt: now.AddDays(-5));

        var tmplVm = new BeneficiaryTemplateViewModel(tmplEntry);
        verify(tmplVm.Id == 50, "Template ID matches.");
        verify(tmplVm.Label == "Landlord", "Template label matches.");
        verify(tmplVm.BeneficiaryName == "Hausverwaltung Schmidt", "Template beneficiary name matches.");
        verify(tmplVm.BeneficiaryIban == "DE89370400440532013000", "Template IBAN matches.");
        verify(tmplVm.BeneficiaryBic == "SPKADE21XXX", "Template BIC matches.");
        verify(tmplVm.DefaultAmount == 950.00m, "Template default amount matches.");
        verify(tmplVm.DefaultRemittanceUnstructured == "Miete Wohnung 4B", "Template default remittance matches.");
        verify(tmplVm.DefaultExecutionMode == "StandardCreditTransfer", "Template default mode matches.");
        verify(tmplVm.DisplayName.Contains("Landlord") && tmplVm.DisplayName.Contains("Hausverwaltung Schmidt"), "Template DisplayName includes label and name.");
        verify(tmplVm.DefaultAmountDisplay.Contains("950,00") || tmplVm.DefaultAmountDisplay.Contains("950.00"), "Template amount display formatted.");

        // 2. StandingOrderViewModel presentation
        var orderEntry = new StandingOrderViewModel(
            orderId: 7001,
            beneficiaryName: "Stadtwerke Strom",
            beneficiaryIban: "DE89370400440532013000",
            amount: 110.00m,
            currency: "EUR",
            frequency: "Monthly",
            executionDay: 15,
            firstExecutionDate: today.AddDays(10),
            lastExecutionDate: null,
            status: "Active");

        verify(orderEntry.OrderId == 7001, "Standing order ID matches.");
        verify(orderEntry.BeneficiaryName == "Stadtwerke Strom", "Standing order beneficiary matches.");
        verify(orderEntry.Amount == 110.00m, "Standing order amount matches exact decimal.");
        verify(orderEntry.FrequencyDisplay == "Monthly (Day 15)", "Standing order frequency display formatted.");
        verify(orderEntry.StatusDisplay == "[Active]", "Standing order active status display matches.");
        verify(orderEntry.FirstExecutionDisplay == today.AddDays(10).ToString("yyyy-MM-dd"), "First execution date formatted.");
        verify(orderEntry.LastExecutionDisplay.Contains("Indefinite"), "Ongoing standing order last execution display indicates Indefinite.");

        var cancelledOrder = new StandingOrderViewModel(
            orderId: 7001,
            beneficiaryName: "Stadtwerke Strom",
            beneficiaryIban: "DE89370400440532013000",
            amount: 110.00m,
            currency: "EUR",
            frequency: "Monthly",
            executionDay: 15,
            firstExecutionDate: today.AddDays(10),
            lastExecutionDate: null,
            status: "Cancelled");
        verify(cancelledOrder.StatusDisplay == "[Cancelled]", "Cancelled standing order status formatted.");

        // 3. DirectDebitReturnViewModel presentation and statutory deadlines
        var debitRecent = new DirectDebitReturnViewModel(
            transactionId: 8001,
            creditorName: "Fitness Studio",
            creditorIban: "DE89370400440532013000",
            creditorId: "DE98ZZZ09999999999",
            mandateId: "MANDATE-FIT-1",
            amount: 49.90m,
            currency: "EUR",
            bookingDate: today.AddDays(-14),
            referenceDate: today);

        verify(debitRecent.TransactionId == 8001, "Direct debit transaction ID matches.");
        verify(debitRecent.DaysSinceBooking == 14, "Days since booking calculated correctly (14 days).");
        verify(debitRecent.IsWithin8Weeks, "14 days is within statutory 8-week (56-day) unconditional return window.");
        verify(debitRecent.IsWithin13Months, "14 days is within statutory 13-month (396-day) unauthorized return window.");

        var debitPast8Weeks = new DirectDebitReturnViewModel(
            transactionId: 8002,
            creditorName: "Online Shop",
            creditorIban: "DE89370400440532013000",
            creditorId: "DE12ZZZ01111111111",
            mandateId: "MANDATE-SHOP-2",
            amount: 199.00m,
            currency: "EUR",
            bookingDate: today.AddDays(-70),
            referenceDate: today);

        verify(debitPast8Weeks.DaysSinceBooking == 70, "Days since booking is 70.");
        verify(!debitPast8Weeks.IsWithin8Weeks, "70 days exceeds statutory 8-week window.");
        verify(debitPast8Weeks.IsWithin13Months, "70 days is within statutory 13-month unauthorized return window.");

        var debitExpired = new DirectDebitReturnViewModel(
            transactionId: 8003,
            creditorName: "Old Subscription",
            creditorIban: "DE89370400440532013000",
            creditorId: null,
            mandateId: null,
            amount: 15.00m,
            currency: "EUR",
            bookingDate: today.AddDays(-420),
            referenceDate: today);

        verify(debitExpired.DaysSinceBooking == 420, "Days since booking is 420.");
        verify(!debitExpired.IsWithin8Weeks, "420 days exceeds 8-week window.");
        verify(!debitExpired.IsWithin13Months, "420 days exceeds 13-month window.");

        // 4. HostSession Profile Store lifecycle for templates, standing orders, direct debit returns, and safe mode
        string tempDir = Path.Combine(Path.GetTempPath(), "BroilerFondSelfTestEverydayPayments", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);
        const string passphrase = "EverydayPayments-Passphrase-2026!";

        try
        {
            using var session = new HostSession();
            session.CreateNewProfile(tempDir, passphrase, "Everyday Payments Profile");

            var store = session.GetStore() ?? throw new InvalidOperationException("Store is null");
            var snap = store.CurrentSnapshot ?? throw new InvalidOperationException("Snapshot is null");

            var inst = new StoreInstitutionEntry(1, "Berliner Volksbank", "https://localhost", "10090000", null, "Confirmed");
            var conn = new StoreConnectionEntry(2, 1, "User", null, "Synchronized", "SYS1", "Active", 2, true, now, now, "3.0");
            var acc = new StoreAccountEntry(100, "Girokonto Privat", "EUR");
            var inc = new StoreIncarnationEntry(150, 100, "Active");
            var binding = new StoreBindingEntry(
                200, 100, 150, 1, 2, "Active", "FinTs 3.0", "1.0", "https://localhost",
                "DE89370400440532013000", "0532013000", "10090000", null, "EUR", []);
            var balance = new StoreBalanceEntry(
                300, 299, 100, 150, 200, "Booked", "10000.00", "EUR", now, now, false, "HISAL");

            List<StoreLedgerRecord> ledger =
            [
                new StoreLedgerRecord(1, "Institution", null, []),
                new StoreLedgerRecord(2, "Connection", null, [(1, "Institution")]),
                new StoreLedgerRecord(100, "Account", null, []),
                new StoreLedgerRecord(150, "AccountIncarnation", null, [(100, "Account")]),
                new StoreLedgerRecord(200, "AccountBinding", null, [(150, "AccountIncarnation"), (2, "Connection")]),
                new StoreLedgerRecord(299, "Observation", null, [(200, "AccountBinding")]),
                new StoreLedgerRecord(300, "BalanceSnapshot", null, [(299, "Observation")]),
            ];

            var setupSnap = snap with
            {
                Profile = snap.Profile with { NextId = 500 },
                Institutions = new StoreInstitutionsDocument(snap.Profile.ProfileId, snap.Profile.BranchId, snap.Profile.Generation + 1, [inst]),
                Connections = new StoreConnectionsDocument(snap.Profile.ProfileId, snap.Profile.BranchId, snap.Profile.Generation + 1, [conn]),
                Accounts = new StoreAccountsDocument(
                    snap.Profile.ProfileId, snap.Profile.BranchId, snap.Profile.Generation + 1,
                    [acc], [inc], [binding], []),
                Balances = new StoreBalancesDocument(
                    snap.Profile.ProfileId, snap.Profile.BranchId, snap.Profile.Generation + 1,
                    [balance]),
                Ledger = new StoreLedgerDocument(
                    snap.Profile.ProfileId, snap.Profile.BranchId, snap.Profile.Generation + 1,
                    ledger),
            };

            store.Save(setupSnap);
            session.ReloadData();

            // 4.1 Save Beneficiary Template to encrypted profile store
            verify(session.BeneficiaryTemplates.Count == 0, "Initially zero templates.");
            var savedTmpl = session.SaveBeneficiaryTemplate(
                label: "Miete",
                beneficiaryName: "Vermieter Meyer",
                beneficiaryIban: "DE89370400440532013000",
                beneficiaryBic: "SPKADE21XXX",
                remittance: "Miete Nov 2026",
                defaultAmount: 950.00m,
                defaultMode: "StandardCreditTransfer");

            verify(session.BeneficiaryTemplates.Count == 1, "Template saved to profile store.");
            verify(savedTmpl.Label == "Miete", "Saved template label matches.");
            verify(savedTmpl.BeneficiaryName == "Vermieter Meyer", "Saved template beneficiary matches.");

            // 4.2 Test PaymentInitiationDialog with template pre-population and non-bypass invariant
            using (var dlg = new PaymentInitiationDialog(session))
            {
                verify(dlg.AccessibleRole == AccessibleRole.Dialog, "PaymentInitiationDialog role is Dialog.");
                var cmbTemplates = dlg.Controls.OfType<ComboBox>().First(c => c.AccessibleName == "Saved Beneficiary Template Selector");
                verify(cmbTemplates.Items.Count == 2, "Template combo has 2 items (Manual Entry + Saved template).");

                // Select template item (index 1)
                cmbTemplates.SelectedIndex = 1;

                var txtName = dlg.Controls.OfType<TextBox>().First(t => t.AccessibleName == "Recipient Name Input");
                var txtIban = dlg.Controls.OfType<TextBox>().First(t => t.AccessibleName == "Recipient IBAN Input");
                var txtAmount = dlg.Controls.OfType<TextBox>().First(t => t.AccessibleName == "Payment Amount EUR Input");
                var txtRemittance = dlg.Controls.OfType<TextBox>().First(t => t.AccessibleName == "Remittance Information Text");
                var btnContinue = dlg.Controls.OfType<Button>().First(b => b.AccessibleName == "Continue to Payee Verification Button");

                verify(txtName.Text == "Vermieter Meyer", "Template populated Recipient Name.");
                verify(txtIban.Text == "DE89370400440532013000", "Template populated Recipient IBAN.");
                verify(txtAmount.Text.Contains("950"), "Template populated Amount.");
                verify(txtRemittance.Text == "Miete Nov 2026", "Template populated Remittance.");
                verify(btnContinue.Enabled, "With all template fields populated and valid, Continue button is enabled.");

                // Non-bypassing invariant: corrupting IBAN fails validation
                txtIban.Text = "DE89370400440532013001"; // invalid checksum
                verify(!btnContinue.Enabled, "Corrupted IBAN disables Continue button (anti-bypass invariant).");
                txtIban.Text = "DE89370400440532013000"; // restore valid IBAN
                verify(btnContinue.Enabled, "Restoring valid IBAN re-enables Continue button.");

                // Instant transfer option & limit enforcement
                var radInstant = dlg.Controls.OfType<RadioButton>().First(r => r.Text.Contains("Instant"));
                var radStandard = dlg.Controls.OfType<RadioButton>().First(r => r.Text.Contains("Standard"));
                var dtpDate = dlg.Controls.OfType<DateTimePicker>().First(d => d.AccessibleName == "Requested Execution Date");

                verify(radInstant.Enabled, "Instant mode radio button is enabled for account with instant capability.");
                radInstant.Checked = true;
                verify(dtpDate.Value.Date == DateTime.Today, "Instant mode forces execution date to today.");
                verify(!dtpDate.Enabled, "Instant mode disables future-date picker.");

                // Exceeding instant limit fails validation
                txtAmount.Text = "150000.00"; // exceeds standard 100,000 EUR instant limit
                verify(!btnContinue.Enabled, "Exceeding instant maximum limit disables Continue button.");
                txtAmount.Text = "500.00"; // within limit
                verify(btnContinue.Enabled, "Amount within instant limit re-enables Continue button.");

                // Scheduled payment date bounds
                radStandard.Checked = true;
                verify(dtpDate.Enabled, "Standard mode re-enables date picker.");

                dtpDate.Value = DateTime.Today.AddDays(14);
                verify(btnContinue.Enabled, "Date within advance limits (14 days <= 365) is valid.");

                dtpDate.Value = DateTime.Today.AddDays(400); // exceeds 365 days max lead days
                verify(!btnContinue.Enabled, "Execution date exceeding maximum lead days (400 > 365) disables Continue button.");
                dtpDate.Value = DateTime.Today; // reset
                verify(btnContinue.Enabled, "Resetting execution date to today re-enables Continue button.");

                // Save to address book checkbox and label
                var chkSave = dlg.Controls.OfType<CheckBox>().First(c => c.AccessibleName == "Save Recipient to Address Book Checkbox");
                var txtLabel = dlg.Controls.OfType<TextBox>().First(t => t.AccessibleName == "Template Label Input");
                chkSave.Checked = true;
                verify(txtLabel.Enabled, "Checking Save as Template enables label input.");
                txtLabel.Text = "";
                verify(!btnContinue.Enabled, "Empty template label when Save is checked disables Continue button.");
                txtLabel.Text = "Baeckerei";
                verify(btnContinue.Enabled, "Entering template label re-enables Continue button.");
            }

            // 4.3 Soft-delete template
            session.SoftDeleteBeneficiaryTemplate(savedTmpl.Id);
            verify(session.BeneficiaryTemplates.Count == 0, "Soft-deleted template is no longer in active templates list.");

            // 4.4 Standing orders lifecycle in session & StandingOrdersDialog
            var newOrder = new StandingOrderViewModel(
                orderId: 9001,
                beneficiaryName: "Vermieter Meyer",
                beneficiaryIban: "DE89370400440532013000",
                amount: 950.00m,
                currency: "EUR",
                frequency: "Monthly",
                executionDay: 1,
                firstExecutionDate: today.AddDays(15),
                lastExecutionDate: null,
                status: "Active");

            session.AddStandingOrder(newOrder);
            verify(session.StandingOrders.Count == 1, "Standing order added to session.");
            verify(session.StandingOrders[0].OrderId == 9001, "Order ID is 9001.");

            using (var dlgOrders = new StandingOrdersDialog(session))
            {
                verify(dlgOrders.AccessibleRole == AccessibleRole.Dialog, "StandingOrdersDialog role is Dialog.");
                verify(!string.IsNullOrWhiteSpace(dlgOrders.AccessibleName), "StandingOrdersDialog has AccessibleName.");

                var lvw = dlgOrders.Controls.OfType<ListView>().First();
                verify(lvw.Items.Count == 1, "Standing orders list contains 1 item.");
                verify(lvw.Items[0].SubItems[1].Text == "Vermieter Meyer", "Order recipient matches.");

                var btnNew = dlgOrders.Controls.OfType<Button>().First(b => b.AccessibleName == "New Standing Order Button");
                var btnCancel = dlgOrders.Controls.OfType<Button>().First(b => b.AccessibleName == "Cancel Selected Standing Order Button");
                verify(btnNew.Enabled, "New Standing Order button is enabled when Safe Mode is inactive.");
                verify(btnCancel.Enabled, "Cancel button is enabled for active order.");
            }

            // 4.5 Direct-Debit Return Dialog
            using (var dlgReturn = new DirectDebitReturnDialog(session, debitRecent))
            {
                verify(dlgReturn.AccessibleRole == AccessibleRole.Dialog, "DirectDebitReturnDialog role is Dialog.");
                verify(!string.IsNullOrWhiteSpace(dlgReturn.AccessibleName), "DirectDebitReturnDialog has AccessibleName.");

                var rad8 = dlgReturn.Controls.OfType<GroupBox>().SelectMany(g => g.Controls.OfType<RadioButton>()).First(r => r.Text.Contains("8-week"));
                var rad13 = dlgReturn.Controls.OfType<GroupBox>().SelectMany(g => g.Controls.OfType<RadioButton>()).First(r => r.Text.Contains("13-month"));
                var chkAck = dlgReturn.Controls.OfType<GroupBox>().SelectMany(g => g.Controls.OfType<CheckBox>()).First(c => c.AccessibleName == "Acknowledge Direct-Debit Return Warning Checkbox");
                var btnSubmit = dlgReturn.Controls.OfType<Button>().First(b => b.AccessibleName == "Submit Direct-Debit Return Button");

                verify(rad8.Enabled && rad8.Checked, "8-week unconditional option is checked for recent debit.");
                verify(rad13.Enabled, "13-month option is also available.");
                verify(!btnSubmit.Enabled, "Submit button disabled until warning acknowledged.");

                chkAck.Checked = true;
                verify(btnSubmit.Enabled, "Submit button enabled after acknowledgment.");
                verify(dlgReturn.SelectedReasonCode == "MS02", "Default reason code for 8-week return is MS02.");

                rad13.Checked = true;
                verify(dlgReturn.SelectedReasonCode == "MD01", "Selecting 13-month reason sets reason code to MD01.");
            }

            // Test debit past 8 weeks: only 13-month option enabled
            using (var dlgReturn70 = new DirectDebitReturnDialog(session, debitPast8Weeks))
            {
                var rad8 = dlgReturn70.Controls.OfType<GroupBox>().SelectMany(g => g.Controls.OfType<RadioButton>()).First(r => r.Text.Contains("8-week"));
                var rad13 = dlgReturn70.Controls.OfType<GroupBox>().SelectMany(g => g.Controls.OfType<RadioButton>()).First(r => r.Text.Contains("13-month"));
                verify(!rad8.Enabled, "8-week option is disabled for 70-day old transaction.");
                verify(rad13.Enabled && rad13.Checked, "13-month option is enabled and checked.");
            }

            // 4.6 Record Direct Debit Return in session
            session.RecordDirectDebitReturn(8001, "MS02", "Unconditional return requested");

            // 4.7 Safe Mode Enforcement across all Everyday Payment actions
            session.IsSafeModeActive = true;
            verify(session.IsSafeModeActive, "Safe Mode toggled to Active.");

            // Standing order add blocked
            bool addOrderBlocked = false;
            var blockedOrder = new StandingOrderViewModel(
                orderId: 9002,
                beneficiaryName: "Vermieter Meyer",
                beneficiaryIban: "DE89370400440532013000",
                amount: 950.00m,
                currency: "EUR",
                frequency: "Monthly",
                executionDay: 1,
                firstExecutionDate: today.AddDays(15),
                lastExecutionDate: null,
                status: "Active");
            try
            {
                session.AddStandingOrder(blockedOrder);
            }
            catch (InvalidOperationException)
            {
                addOrderBlocked = true;
            }
            verify(addOrderBlocked, "AddStandingOrder blocked by Safe Mode.");

            // Standing order cancel blocked
            bool cancelOrderBlocked = false;
            try
            {
                session.CancelStandingOrder(9001);
            }
            catch (InvalidOperationException)
            {
                cancelOrderBlocked = true;
            }
            verify(cancelOrderBlocked, "CancelStandingOrder blocked by Safe Mode.");

            // Direct-debit return blocked
            bool returnDebitBlocked = false;
            try
            {
                session.RecordDirectDebitReturn(8001, "MS02");
            }
            catch (InvalidOperationException)
            {
                returnDebitBlocked = true;
            }
            verify(returnDebitBlocked, "RecordDirectDebitReturn blocked by Safe Mode.");

            // UI dialogs lockout under Safe Mode
            using (var dlgInitSafe = new PaymentInitiationDialog(session))
            {
                var btnContinue = dlgInitSafe.Controls.OfType<Button>().First(b => b.AccessibleName == "Continue to Payee Verification Button");
                verify(!btnContinue.Enabled, "PaymentInitiationDialog Continue button disabled under Safe Mode.");
            }

            using (var dlgOrdersSafe = new StandingOrdersDialog(session))
            {
                var btnNew = dlgOrdersSafe.Controls.OfType<Button>().First(b => b.AccessibleName == "New Standing Order Button");
                var btnCancel = dlgOrdersSafe.Controls.OfType<Button>().First(b => b.AccessibleName == "Cancel Selected Standing Order Button");
                verify(!btnNew.Enabled, "StandingOrdersDialog New button disabled under Safe Mode.");
                verify(!btnCancel.Enabled, "StandingOrdersDialog Cancel button disabled under Safe Mode.");
            }

            using (var dlgReturnSafe = new DirectDebitReturnDialog(session, debitRecent))
            {
                var chkAck = dlgReturnSafe.Controls.OfType<GroupBox>().SelectMany(g => g.Controls.OfType<CheckBox>()).First(c => c.AccessibleName == "Acknowledge Direct-Debit Return Warning Checkbox");
                var btnSubmit = dlgReturnSafe.Controls.OfType<Button>().First(b => b.AccessibleName == "Submit Direct-Debit Return Button");
                chkAck.Checked = true;
                verify(!btnSubmit.Enabled, "DirectDebitReturnDialog Submit button disabled under Safe Mode even when acknowledged.");
            }

            session.IsSafeModeActive = false;
            verify(!session.IsSafeModeActive, "Safe Mode toggled back to Inactive.");

            // Cancel standing order when safe mode inactive succeeds
            session.CancelStandingOrder(9001);
            verify(session.StandingOrders.Count == 0, "Standing order cancelled successfully.");
        }
        finally
        {
            try
            {
                if (Directory.Exists(tempDir))
                {
                    Directory.Delete(tempDir, recursive: true);
                }
            }
            catch { }
        }
    }

    private static void VerifyFinancialInsightsAndBudgetManagement(Action<bool, string> verify)
    {
        var now = DateTimeOffset.UtcNow;
        var today = DateOnly.FromDateTime(DateTime.UtcNow);

        string tempDir = Path.Combine(Path.GetTempPath(), "BroilerFondSelfTestInsights", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);
        const string passphrase = "Insights-Passphrase-2026!";

        try
        {
            using var session = new HostSession();
            session.CreateNewProfile(tempDir, passphrase, "Financial Insights Profile");

            var store = session.GetStore() ?? throw new InvalidOperationException("Store is null");
            var snap = store.CurrentSnapshot ?? throw new InvalidOperationException("Snapshot is null");

            var inst = new StoreInstitutionEntry(1, "Berliner Sparkasse", "https://localhost", "10050000", null, "Confirmed");
            var conn = new StoreConnectionEntry(2, 1, "User", null, "Synchronized", "SYS1", "Active", 2, true, now, now, "3.0");
            var acc1 = new StoreAccountEntry(100, "Girokonto", "EUR");
            var inc1 = new StoreIncarnationEntry(150, 100, "Active");
            var binding1 = new StoreBindingEntry(
                200, 100, 150, 1, 2, "Active", "FinTs 3.0", "1.0", "https://localhost",
                "DE89370400440532013000", "0532013000", "10050000", null, "EUR", []);
            var balance1 = new StoreBalanceEntry(
                300, 299, 100, 150, 200, "Booked", "5000.00", "EUR", now, now, false, "HISAL");

            var acc2 = new StoreAccountEntry(101, "Tagesgeld", "EUR");
            var inc2 = new StoreIncarnationEntry(151, 101, "Active");
            var binding2 = new StoreBindingEntry(
                201, 101, 151, 1, 2, "Active", "FinTs 3.0", "1.0", "https://localhost",
                "DE89370400440532013001", "0532013001", "10050000", null, "EUR", []);
            var balance2 = new StoreBalanceEntry(
                301, 298, 101, 151, 201, "Booked", "15000.00", "EUR", now, now, false, "HISAL");

            var obs1 = new StoreTransactionObservationEntry(
                Id: 601,
                AccountBindingId: 200,
                RetrievedAt: now,
                StatementFormat: "FinTs",
                StatementNumber: "1",
                PageToken: null);

            var tx1 = new StoreTransactionEntry(801, 100, 150, "Booked", "Debit");

            var rev1 = new StoreTransactionRevisionEntry(
                Id: 701,
                TransactionId: 801,
                ObservationId: 601,
                AccountId: 100,
                AccountIncarnationId: 150,
                AccountBindingId: 200,
                Status: "Booked",
                Kind: "Debit",
                Amount: "-100.00",
                Currency: "EUR",
                IsCredit: false,
                BookingDate: today.AddDays(-5),
                ValueDate: today.AddDays(-5),
                BankTimestamp: now,
                RetrievedAt: now,
                CounterpartyName: "EDEKA Supermarkt",
                CounterpartyIban: "DE02100500001234567890",
                CounterpartyBic: "BELADEBEXXX",
                RemittanceText: "Kartenzahlung EDEKA Markt 123",
                EndToEndId: null,
                MandateId: null,
                CreditorId: null,
                CustomerReference: null,
                BankReference: "REF-EDEKA-01",
                PurposeCode: null,
                ReturnReasonCode: null,
                RawBankCode: null);

            var obs2 = new StoreTransactionObservationEntry(
                Id: 602,
                AccountBindingId: 200,
                RetrievedAt: now,
                StatementFormat: "FinTs",
                StatementNumber: "2",
                PageToken: null);

            var tx2 = new StoreTransactionEntry(802, 100, 150, "Booked", "Debit");

            var rev2 = new StoreTransactionRevisionEntry(
                Id: 702,
                TransactionId: 802,
                ObservationId: 602,
                AccountId: 100,
                AccountIncarnationId: 150,
                AccountBindingId: 200,
                Status: "Booked",
                Kind: "Debit",
                Amount: "-50.00",
                Currency: "EUR",
                IsCredit: false,
                BookingDate: today.AddDays(-2),
                ValueDate: today.AddDays(-2),
                BankTimestamp: now,
                RetrievedAt: now,
                CounterpartyName: "Apotheke Am Markt",
                CounterpartyIban: "DE02100500009876543210",
                CounterpartyBic: "BELADEBEXXX",
                RemittanceText: "Rezeptgebuehr und Medikamente",
                EndToEndId: null,
                MandateId: null,
                CreditorId: null,
                CustomerReference: null,
                BankReference: "REF-APO-01",
                PurposeCode: null,
                ReturnReasonCode: null,
                RawBankCode: null);

            List<StoreLedgerRecord> ledger =
            [
                new StoreLedgerRecord(1, "Institution", null, []),
                new StoreLedgerRecord(2, "Connection", null, [(1, "Institution")]),
                new StoreLedgerRecord(100, "Account", null, []),
                new StoreLedgerRecord(150, "AccountIncarnation", null, [(100, "Account")]),
                new StoreLedgerRecord(200, "AccountBinding", null, [(150, "AccountIncarnation"), (2, "Connection")]),
                new StoreLedgerRecord(299, "Observation", null, [(200, "AccountBinding")]),
                new StoreLedgerRecord(300, "BalanceSnapshot", null, [(299, "Observation")]),
                new StoreLedgerRecord(101, "Account", null, []),
                new StoreLedgerRecord(151, "AccountIncarnation", null, [(101, "Account")]),
                new StoreLedgerRecord(201, "AccountBinding", null, [(151, "AccountIncarnation"), (2, "Connection")]),
                new StoreLedgerRecord(298, "Observation", null, [(201, "AccountBinding")]),
                new StoreLedgerRecord(301, "BalanceSnapshot", null, [(298, "Observation")]),
                new StoreLedgerRecord(601, "Observation", null, [(200, "AccountBinding")]),
                new StoreLedgerRecord(801, "Transaction", null, [(100, "Account"), (150, "AccountIncarnation")]),
                new StoreLedgerRecord(701, "TransactionRevision", null, [(801, "Transaction"), (601, "Observation")]),
                new StoreLedgerRecord(602, "Observation", null, [(200, "AccountBinding")]),
                new StoreLedgerRecord(802, "Transaction", null, [(100, "Account"), (150, "AccountIncarnation")]),
                new StoreLedgerRecord(702, "TransactionRevision", null, [(802, "Transaction"), (602, "Observation")]),
            ];

            var txDoc = new StoreTransactionsDocument(
                snap.Profile.ProfileId, snap.Profile.BranchId, snap.Profile.Generation + 1,
                [obs1, obs2], [tx1, tx2], [rev1, rev2], [], [], []);

            var setupSnap = snap with
            {
                Profile = snap.Profile with { NextId = 1000 },
                Institutions = new StoreInstitutionsDocument(snap.Profile.ProfileId, snap.Profile.BranchId, snap.Profile.Generation + 1, [inst]),
                Connections = new StoreConnectionsDocument(snap.Profile.ProfileId, snap.Profile.BranchId, snap.Profile.Generation + 1, [conn]),
                Accounts = new StoreAccountsDocument(
                    snap.Profile.ProfileId, snap.Profile.BranchId, snap.Profile.Generation + 1,
                    [acc1, acc2], [inc1, inc2], [binding1, binding2], []),
                Balances = new StoreBalancesDocument(
                    snap.Profile.ProfileId, snap.Profile.BranchId, snap.Profile.Generation + 1,
                    [balance1, balance2]),
                Transactions = txDoc,
                Ledger = new StoreLedgerDocument(
                    snap.Profile.ProfileId, snap.Profile.BranchId, snap.Profile.Generation + 1,
                    ledger),
            };

            store.Save(setupSnap);
            session.ReloadData();

            verify(session.Accounts.Count == 2, "Profile loaded 2 accounts.");
            verify(session.Transactions.Count == 2, "Profile loaded 2 transactions.");

            // 1. Transaction Splits & Strict Balance Invariant
            var txVm = session.Transactions.First(t => t.TransactionId == 801);
            using (var splitDlg = new TransactionSplitDialog(session, txVm))
            {
                verify(splitDlg.AccessibleRole == AccessibleRole.Dialog, "TransactionSplitDialog role is Dialog.");
                verify(!string.IsNullOrWhiteSpace(splitDlg.AccessibleName), "TransactionSplitDialog has AccessibleName.");

                // Add split 1: -60.00 EUR (40 remaining)
                splitDlg.AddSplitForTesting(-60.00m, "Lebensmittel", "Supermarkt Einkauf");
                var btnSave = splitDlg.Controls.OfType<Button>().First(b => b.Text.Contains("Save"));
                verify(!btnSave.Enabled, "Save button is disabled when split allocation is incomplete.");

                // Add split 2: -40.00 EUR (0 remaining)
                splitDlg.AddSplitForTesting(-40.00m, "Haushalt", "Kuechenrolle");
                verify(btnSave.Enabled, "Save button is enabled when exact balance matches parent transaction.");

                // Add split 3: overallocated
                splitDlg.AddSplitForTesting(-10.00m, "Snacks");
                verify(!btnSave.Enabled, "Save button is disabled when split amounts are overallocated.");
            }

            // Test session split persistence
            var validSplits = new List<TransactionSplitItem>
            {
                new(1, -60.00m, "Lebensmittel", "Supermarkt Einkauf"),
                new(2, -40.00m, "Haushalt", "Kuechenrolle")
            };
            session.SaveTransactionSplits(801, validSplits);
            session.ReloadData();
            var reloadedTx = session.Transactions.First(t => t.TransactionId == 801);
            verify(reloadedTx.Record.IsSplit, "Transaction is marked as split in record.");

            // 2. Categorization Rules & Live Preview
            var edekaRule = CategorizationRule.Create(
                id: 1,
                name: "Supermarkt Rule",
                targetCategory: "Lebensmittel",
                criteria: new RuleCriteria(CounterpartyPattern: "EDEKA"),
                priority: 150);

            session.AddRule(edekaRule);
            verify(session.Rules.Count == 1, "Categorization rule added to session.");

            var preview = session.PreviewRule(edekaRule);
            verify(preview.TotalEvaluated >= 2, "Preview evaluated at least 2 transactions.");
            verify(preview.MatchCount == 1, "Preview matched 1 EDEKA transaction.");
            verify(preview.MatchedTransactions[0].CounterpartyName == "EDEKA Supermarkt", "Matched transaction counterparty matches.");

            using (var rulesDlg = new CategorizationRulesDialog(session))
            {
                verify(rulesDlg.AccessibleRole == AccessibleRole.Dialog, "CategorizationRulesDialog role is Dialog.");
                verify(!string.IsNullOrWhiteSpace(rulesDlg.AccessibleName), "CategorizationRulesDialog has AccessibleName.");

                int matches = rulesDlg.TriggerPreviewForTesting(edekaRule);
                verify(matches == 1, "TriggerPreviewForTesting matched 1 transaction.");
            }

            int applied = session.ApplyRules();
            verify(applied >= 1, "Applied rules created annotation revision records.");

            // 3. Budgets & Spending Rollups
            var budgetMonth = new DateOnly(today.Year, today.Month, 1);
            var budget = new BudgetTarget(
                id: 1,
                category: "Lebensmittel",
                monthlyLimit: 300.00m,
                currency: "EUR",
                effectiveFrom: budgetMonth);

            session.SetBudgetTarget(budget);
            verify(session.BudgetTargets.Count == 1, "Budget target set in session.");

            var progresses = session.GetBudgetProgress(budgetMonth, "EUR");
            verify(progresses.Count > 0, "Budget progress returned category items.");
            var foodProgress = progresses.FirstOrDefault(p => p.Category == "Lebensmittel");
            verify(foodProgress is not null, "Lebensmittel budget progress found.");
            verify(foodProgress!.TargetAmount == 300.00m, "Budget limit is 300.00 EUR.");
            verify(foodProgress.ActualSpent > 0m, "Actual spending recorded for Lebensmittel.");
            verify(!foodProgress.IsOverspent, "Actual spent within 300.00 EUR limit.");

            // 4. Recurring Obligations & Cash-Flow Forecast
            var obligation = new RecurringObligation(
                id: 1,
                accountId: 100,
                counterpartyName: "Hausverwaltung Meyer",
                expectedAmount: 950.00m,
                currency: "EUR",
                isCredit: false,
                cadence: RecurringCadence.Monthly,
                expectedDayOfMonth: 1);

            session.AddRecurringObligation(obligation);
            verify(session.RecurringObligations.Count == 1, "Recurring obligation added to session.");

            var forecast = session.GenerateForecast(100, horizonDays: 60, lowBalanceThreshold: 1000.00m);
            verify(forecast.ForecastItems.Count > 0, "Forecast generated future occurrences.");
            verify(forecast.ForecastItems.All(i => i.IsEstimate), "Non-conflation: All forecast items are explicitly IsEstimate = true.");
            verify(forecast.ForecastItems.All(i => i.DisplayLabel.Contains("[Estimate / Prognose]")), "DisplayLabel contains mandatory estimate label.");
            verify(forecast.CurrentBookedBalance == 5000.00m, "Forecast starting balance matches booked balance.");

            using (var forecastDlg = new CashFlowForecastDialog(session, 100))
            {
                verify(forecastDlg.AccessibleRole == AccessibleRole.Dialog, "CashFlowForecastDialog role is Dialog.");
                verify(!string.IsNullOrWhiteSpace(forecastDlg.AccessibleName), "CashFlowForecastDialog has AccessibleName.");

                forecastDlg.CalculateCurrentForecast();
                verify(forecastDlg.CurrentForecast is not null, "CurrentForecast calculated in dialog.");
                verify(forecastDlg.CurrentForecast!.ForecastItems.Count > 0, "Dialog forecast has timeline items.");
            }

            // 5. Net-Worth History & Multi-Currency Isolation
            var netWorth = session.GetCurrentNetWorth();
            verify(netWorth.Currencies.Contains("EUR"), "Net-worth snapshot includes EUR.");
            var eurNetWorth = netWorth.GetSummary("EUR");
            verify(eurNetWorth is not null, "EUR net-worth summary exists.");
            verify(eurNetWorth!.TotalAssets.Amount == 20000.00m, "Total assets equals 5000 + 15000 = 20000 EUR.");
            verify(eurNetWorth.TotalLiabilities.Amount == 0m, "Total liabilities equals 0 EUR.");
            verify(eurNetWorth.NetWorth.Amount == 20000.00m, "Net worth equals 20000 EUR.");

            var history = session.GetNetWorthHistory(budgetMonth.AddMonths(-1), budgetMonth);
            verify(history.Count >= 1, "Net worth history returned snapshots.");

            // 6. MainForm Presentation & Insights Tab
            using (var mainForm = new MainForm(session))
            {
                verify(mainForm.AccessibleRole == AccessibleRole.Client, "MainForm role is Client.");
                var tabControl = mainForm.Controls.OfType<TabControl>().First();
                var insightsTab = tabControl.TabPages.Cast<TabPage>().FirstOrDefault(t => t.Text.Contains("Insights"));
                verify(insightsTab is not null, "MainForm contains Insights & Budgets tab page.");
            }
        }
        finally
        {
            try
            {
                if (Directory.Exists(tempDir))
                {
                    Directory.Delete(tempDir, recursive: true);
                }
            }
            catch { }
        }
    }

    private static void VerifyWindowsHost10Hardening(Action<bool, string> verify)
    {
        // 1. AboutDialog & Disclosures
        using (var aboutDlg = new AboutDialog())
        {
            verify(aboutDlg.AccessibleRole == AccessibleRole.Dialog, "AboutDialog role is Dialog.");
            verify(!string.IsNullOrWhiteSpace(aboutDlg.AccessibleName), "AboutDialog has AccessibleName.");
            var audit = BfsgAccessibilityAuditor.Audit(aboutDlg);
            verify(audit.Passed, $"AboutDialog passed BFSG accessibility audit: {string.Join("; ", audit.Defects)}");
            verify(audit.TotalAudited > 5, "AboutDialog controls audited.");
        }

        // 2. RestoreBackupDialog & Backup Export/Restore Workflow
        string tempDir = Path.Combine(Path.GetTempPath(), "BroilerFondSelfTestBackup", Guid.NewGuid().ToString("N"));
        string restoreDir = Path.Combine(Path.GetTempPath(), "BroilerFondSelfTestRestored", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);
        const string passphrase = "HostHardening-Passphrase-2026!";

        try
        {
            using var session = new HostSession();
            session.CreateNewProfile(tempDir, passphrase, "Hardening Profile");

            // Add an account so there's committed data
            var store = session.GetStore() ?? throw new InvalidOperationException("Store is null");
            var snap = store.CurrentSnapshot ?? throw new InvalidOperationException("Snapshot is null");
            var now = DateTimeOffset.UtcNow;
            var inst = new StoreInstitutionEntry(10, "Test Bank", "https://localhost", "10050000", null, "Confirmed");
            var conn = new StoreConnectionEntry(20, 10, "user", null, "Synchronized", "sys", "Active", 1, true, now, now, "3.0");
            var acc = new StoreAccountEntry(100, "Checking", "EUR");
            var inc = new StoreIncarnationEntry(150, 100, "Active");
            var binding = new StoreBindingEntry(200, 100, 150, 10, 20, "Active", "FinTs", "1", "https://localhost", "DE00", "00", "100", null, "EUR", []);

            var records = new List<StoreLedgerRecord>
            {
                new(10, "Institution", null, []),
                new(20, "Connection", null, [(10, "Institution")]),
                new(100, "Account", null, []),
                new(150, "AccountIncarnation", null, [(100, "Account")]),
                new(200, "AccountBinding", null, [(150, "AccountIncarnation"), (20, "Connection")])
            };

            var newSnap = snap with
            {
                Profile = snap.Profile with { Generation = 2, NextId = 500 },
                Institutions = snap.Institutions with { Generation = 2, Institutions = [inst] },
                Connections = snap.Connections with { Generation = 2, Connections = [conn] },
                Accounts = snap.Accounts with
                {
                    Generation = 2,
                    Accounts = [acc],
                    Incarnations = [inc],
                    Bindings = [binding]
                },
                Ledger = snap.Ledger with
                {
                    Generation = 2,
                    Records = records
                }
            };
            store.Save(newSnap);
            session.ReloadData();

            // Export backup
            string backupPath = Path.Combine(tempDir, "hardening_backup.bffond");
            session.ExportBackup(backupPath);
            verify(File.Exists(backupPath), "ExportBackup creates destination file.");

            // Verify RestoreBackupDialog
            using (var restoreDlg = new RestoreBackupDialog(session, backupPath))
            {
                verify(restoreDlg.AccessibleRole == AccessibleRole.Dialog, "RestoreBackupDialog role is Dialog.");
                verify(!string.IsNullOrWhiteSpace(restoreDlg.AccessibleName), "RestoreBackupDialog has AccessibleName.");
                var restoreAudit = BfsgAccessibilityAuditor.Audit(restoreDlg);
                verify(restoreAudit.Passed, $"RestoreBackupDialog passed BFSG accessibility audit: {string.Join("; ", restoreAudit.Defects)}");
            }

            // Execute restore
            session.RestoreFromBackup(backupPath, restoreDir, passphrase);
            verify(session.IsUnlocked, "HostSession is unlocked after restore.");
            verify(session.ProfileDirectory == restoreDir, "ProfileDirectory matches restored destination.");
            verify(session.Accounts.Count == 1, "Restored profile contains saved account.");
            verify(session.Accounts[0].AccountName == "Checking", "Account name preserved in restored session.");

            // 3. Safe Mode & MainForm BFSG audit
            using (var mainForm = new MainForm(session))
            {
                verify(mainForm.AccessibleRole == AccessibleRole.Client, "MainForm role is Client.");
                var mainAudit = BfsgAccessibilityAuditor.Audit(mainForm);
                verify(mainAudit.Passed, $"MainForm passed BFSG accessibility audit: {string.Join("; ", mainAudit.Defects)}");

                // Verify Safe Mode toggle
                session.IsSafeModeActive = true;
                verify(session.IsSafeModeActive, "Safe Mode is active.");
                session.IsSafeModeActive = false;
                verify(!session.IsSafeModeActive, "Safe Mode is inactive.");
            }
        }
        finally
        {
            try
            {
                if (Directory.Exists(tempDir)) Directory.Delete(tempDir, recursive: true);
                if (Directory.Exists(restoreDir)) Directory.Delete(restoreDir, recursive: true);
            }
            catch { }
        }
    }
}
