using Broiler.Fond.Kernel.Storage;
using Broiler.Fond.Presentation.Services;
using Broiler.Fond.Presentation.Sessions;
using Broiler.Fond.Presentation.Views;

namespace Broiler.Fond.Presentation.Tests;

internal static class BankRefreshFeedbackTests
{
    public static void Run(Action<bool, string> check)
    {
        string directory = Path.Combine(Path.GetTempPath(), "BroilerFond_Refresh_" + Guid.NewGuid().ToString("N"));
        try
        {
            using var session = new HostSession();
            session.CreateNewProfile(directory, "RefreshTests123!", "Refresh tests");
            var alerts = new List<(string Title, string Message)>();
            var dialogs = new BroilerDialogService
            {
                HeadlessAlertHandler = (title, message) => { alerts.Add((title, message)); return Task.CompletedTask; }
            };
            using var shell = new BroilerMainWindow(session, dialogs);
            check(!shell.Accounts.FreshnessLabel.Text.Contains("Real-Time"), "Empty profile does not claim live balance freshness.");
            check(shell.StatusCurrencyTotalsLabel.Text == "Balances: Unavailable", "Missing bank data is not presented as a zero balance.");
            shell.Accounts.SyncButton.Click();
            check(alerts.Last().Message.Contains("No bank request was sent") && alerts.Last().Message.Contains("Add Connection"),
                "Refresh Balances reports no bank request and explains missing connection.");
            var connection = session.AddInstitutionAndConnection("Example", "37040044", "https://bank.example/fints");
            var savedConnection = session.CurrentSnapshot!.Connections.Connections.Single();
            check(!savedConnection.IsSynchronized && savedConnection.SystemId is null && savedConnection.LastSuccessfulContact is null && savedConnection.LastAttemptedContact is null,
                "Manual connection setup does not fabricate synchronization or contact timestamps.");
            check(!connection.IsEndpointConfirmed && connection.StateDisplay.Contains("Not Synchronized"), "New connection truthfully requires confirmation and synchronization.");
            var account = session.AddManualAccount(connection.ConnectionId, "Household", "DE89370400440532013000", "EUR");
            ulong generation = session.CurrentSnapshot!.Profile.Generation;
            shell.SyncAllButton.Click();
            check(alerts.Last().Title == "Bank Refresh Unavailable" && alerts.Last().Message.Contains("product ID") && alerts.Last().Message.Contains("manually entered"),
                "Sync All reports concrete setup blockers and manual account limitations without claiming the batch workflow is disconnected.");
            check(session.CurrentSnapshot!.Profile.Generation == generation && session.CurrentSnapshot.Balances.Balances.Count == 0,
                "Unavailable refresh preserves profile generation and does not create balances.");

            // A real recorded zero must remain distinguishable from an absent balance.
            StoreSnapshot snapshot = session.CurrentSnapshot;
            var binding = snapshot.Accounts.Bindings.Single();
            ulong observationId = snapshot.Profile.NextId;
            ulong balanceId = observationId + 1;
            DateTimeOffset retrieved = new(2026, 10, 20, 12, 0, 0, TimeSpan.Zero);
            session.GetStore()!.Save(snapshot with
            {
                Profile = snapshot.Profile with { NextId = balanceId + 1 },
                Balances = snapshot.Balances with
                {
                    Balances = [new StoreBalanceEntry(balanceId, observationId, account.AccountId,
                    binding.IncarnationId, binding.Id, "Booked", "0.00", "EUR", retrieved, retrieved, true, "Test recorded balance")]
                },
                Ledger = snapshot.Ledger with
                {
                    Records = [.. snapshot.Ledger.Records,
                    new StoreLedgerRecord(observationId, "Observation", null, [(binding.Id, "AccountBinding")]),
                    new StoreLedgerRecord(balanceId, "BalanceSnapshot", null, [(binding.Id, "AccountBinding"), (observationId, "Observation")])]
                }
            });
            session.ReloadData();
            check(shell.Accounts.FreshnessLabel.Text.Contains("Cached balances") && shell.Accounts.FreshnessLabel.Text.Contains("2026-10-20 12:00"),
                "Freshness describes the recorded retrieval timestamp instead of claiming real-time data.");
            check(shell.StatusCurrencyTotalsLabel.Text.Contains("EUR 0") && shell.StatusCurrencyTotalsLabel.Text.StartsWith("Cached totals"),
                "A recorded zero balance remains visible as a cached currency total.");
            shell.SyncAllButton.Click();
            check(session.Accounts.Single().RetrievedAtUtc == retrieved, "Refresh warning does not make cached data appear newly retrieved.");
            session.IsSafeModeActive = true;
            shell.Accounts.SyncButton.Click();
            check(alerts.Last().Message.Contains("Safe Mode"), "Refresh explains the Safe Mode blocker.");
            session.Lock();
            dialogs.HeadlessProfileUnlockHandler = _ => Task.FromException<ProfileUnlockResult>(new IOException("Profile is inaccessible."));
            shell.SyncAllButton.Click();
            check(alerts.Last().Title == "Bank Refresh Failed" && alerts.Last().Message.Contains("profile could not be opened"),
                "Profile access failure is reported to the user instead of escaping the click handler.");
            dialogs.HeadlessProfileUnlockHandler = _ => Task.FromResult(new ProfileUnlockResult(false, null, true));
            int alertCount = alerts.Count;
            shell.Accounts.SyncButton.Click();
            check(alerts.Count == alertCount, "Cancelling unlock does not show a misleading bank error.");
        }
        finally
        {
            if (Directory.Exists(directory))
                Directory.Delete(directory, recursive: true);
        }
    }
}
