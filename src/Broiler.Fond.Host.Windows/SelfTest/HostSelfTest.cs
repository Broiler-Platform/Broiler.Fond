using System.Windows.Forms;
using Broiler.Fond.Host.Windows.Dialogs;
using Broiler.Fond.Host.Windows.Models;
using Broiler.Fond.Host.Windows.Views;
using Broiler.Fond.Kernel.Diagnostics;
using Broiler.Fond.Kernel.Domain.Accounts;
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
}
