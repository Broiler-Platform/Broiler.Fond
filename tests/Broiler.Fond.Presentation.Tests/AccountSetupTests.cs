using Broiler.Fond.Kernel.Storage;
using Broiler.Fond.Presentation.Dialogs;
using Broiler.Fond.Presentation.Services;
using Broiler.Fond.Presentation.Sessions;
using Broiler.Fond.Presentation.Views;
using Broiler.UI.Dialog;

namespace Broiler.Fond.Presentation.Tests;

internal static class AccountSetupTests
{
    public static async Task RunAsync(Action<bool, string> check)
    {
        const string iban = "DE89370400440532013000";
        var request = new AccountSetupRequest([new AccountConnectionOption(2, "Example Bank")]);
        using (var dialog = new BroilerAccountSetupDialog(request))
        {
            dialog.NameEdit.Text = "Household";
            dialog.IbanEdit.Text = "invalid";
            dialog.Accept();
            check(dialog.Dialog.CompletedResult.Kind == UiDialogResultKind.None && dialog.ErrorLabel.Text.Length > 0, "Invalid IBAN keeps account form open with validation feedback.");
            dialog.IbanEdit.Text = "de89 3704 0044 0532 0130 00";
            dialog.CurrencyEdit.Text = "BAD";
            dialog.Accept();
            check(dialog.Dialog.CompletedResult.Kind == UiDialogResultKind.None, "Unknown currency cannot be saved from the account form.");
            dialog.CurrencyEdit.Text = "eur";
            dialog.Accept();
            check(dialog.GetResult() is { Succeeded: true, ConnectionId: 2, Currency: "EUR", Iban: iban }, "Account form returns normalized accepted details.");
        }
        using (var dialog = new BroilerAccountSetupDialog(request))
        {
            dialog.CancelButton.Click();
            check(dialog.GetResult().Cancelled && !dialog.GetResult().Succeeded, "Cancel does not return account details for saving.");
        }

        string directory = Path.Combine(Path.GetTempPath(), "BroilerFond_AccountSetup_" + Guid.NewGuid().ToString("N"));
        try
        {
            using var session = new HostSession();
            session.CreateNewProfile(directory, "AccountSetupTest123!", "Account tests");
            var alerts = new List<string>();
            var dialogs = new BroilerDialogService
            {
                HeadlessAlertHandler = (_, message) => { alerts.Add(message); return Task.CompletedTask; },
                HeadlessAccountSetupHandler = _ => Task.FromResult(new AccountSetupResult(true, 2, "Household", iban, "EUR", false)),
                HeadlessProfileUnlockHandler = _ => Task.FromResult(new ProfileUnlockResult(false, null, true))
            };
            using var view = new AccountsView(session, dialogs);
            check(view.Toolbar.Children.Contains(view.AddAccountButton) && view.AddAccountButton.Text == "Add Account", "Accounts toolbar exposes Add Account.");
            check(!await view.AddAccountAsync() && alerts.Last().Contains("Add Connection"), "Missing connection explains how to create one without writing an account.");
            var connection = session.AddInstitutionAndConnection("Example Bank", "37040044", "https://bank.example/fints");
            dialogs.HeadlessAccountSetupHandler = r =>
            {
                check(r.Connections.Single().ConnectionId == connection.ConnectionId, "Account form receives the stored bank connection.");
                return Task.FromResult(new AccountSetupResult(true, connection.ConnectionId, " Household ", "de89 3704 0044 0532 0130 00", "eur", false));
            };
            int previousAlertCount = alerts.Count;
            view.AddAccountButton.Click();
            check(session.Accounts.Count == 1 && alerts.Count == previousAlertCount, "Clicking Add Account saves a manual account without errors.");
            var account = session.Accounts.Single();
            check(view.AccountsList.Items.Count == 1 && view.AccountsList.SelectedItemId == account.AccountId.ToString(), "New account immediately appears selected in the accounts list.");
            check(account.AccountName == "Household" && account.Currency == "EUR" && account.BookedAmount is null && account.AvailableAmount is null,
                "Manual account retains normalized details without fabricating balances.");
            check(account.BindingStateDisplay.Contains("Not Bank Verified"), "Manual account is identified as not bank verified.");
            StoreSnapshot snapshot = session.CurrentSnapshot!;
            StoreArchiveCodec.ValidateIdentityAndSemanticInvariants(snapshot);
            check(snapshot.Accounts.Bindings.Single().State == "Quarantined" && snapshot.Accounts.Capabilities.Count == 0,
                "Manual setup does not grant bank capabilities or an active bank binding.");

            ulong generation = snapshot.Profile.Generation;
            check(!await view.AddAccountAsync() && session.Accounts.Count == 1 && session.CurrentSnapshot!.Profile.Generation == generation,
                "Duplicate normalized IBAN cannot create another account or generation.");
            Reject(() => session.AddManualAccount(connection.ConnectionId, "Other", "bad", "EUR"), "Invalid IBAN is rejected by the session too.");
            Reject(() => session.AddManualAccount(connection.ConnectionId, "Other", iban, "XYZ"), "Unknown currency is rejected by the session too.");
            Reject(() => session.AddManualAccount(999, "Other", iban, "EUR"), "Missing connection is rejected by the session too.");
            Reject(() => session.AddManualAccount(connection.ConnectionId, " ", iban, "EUR"), "Empty account name is rejected.");
            check(session.CurrentSnapshot!.Profile.Generation == generation, "Validation failures do not modify the stored profile.");

            dialogs.HeadlessAccountSetupHandler = _ => Task.FromResult(new AccountSetupResult(false, 0, "", "", "", true));
            check(!await view.AddAccountAsync() && session.CurrentSnapshot!.Profile.Generation == generation, "Cancelling account setup leaves the profile unchanged.");
            session.IsSafeModeActive = true;
            check(!await view.AddAccountAsync(), "Add Account respects Safe Mode.");
            Reject(() => session.AddManualAccount(connection.ConnectionId, "Other", iban, "EUR"), "Direct account creation respects Safe Mode.");
            session.IsSafeModeActive = false;

            session.Lock();
            check(!await view.AddAccountAsync(), "Cancelling profile unlock does not open account setup.");
            Reject(() => session.AddManualAccount(connection.ConnectionId, "Other", iban, "EUR"), "Locked profile cannot be modified directly.");
            session.Unlock(directory, "AccountSetupTest123!");
            check(session.Accounts.Single().AccountId == account.AccountId && session.Accounts.Single().LocatorDisplay.Contains(iban),
                "Manual account and IBAN survive profile lock and reopening.");

            void Reject(Action action, string message)
            {
                bool rejected = false;
                try { action(); }
                catch (Exception ex) when (ex is ArgumentException or InvalidOperationException) { rejected = true; }
                check(rejected, message);
            }
        }
        finally
        {
            if (Directory.Exists(directory))
                Directory.Delete(directory, recursive: true);
        }
    }
}
