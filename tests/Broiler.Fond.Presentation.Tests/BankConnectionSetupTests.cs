using System.Net;
using System.Security.Authentication;
using Broiler.Fond.Kernel.FinTs.Transport;
using Broiler.Fond.Kernel.Storage;
using Broiler.Fond.Presentation.Dialogs;
using Broiler.Fond.Presentation.Services;
using Broiler.Fond.Presentation.Sessions;
using Broiler.Fond.Presentation.Views;

namespace Broiler.Fond.Presentation.Tests;

internal static class BankConnectionSetupTests
{
    public static async Task RunAsync(Action<bool, string> check)
    {
        const string endpoint = "https://bank.example:8443/fints?channel=desktop";
        using (var dialog = new BroilerInstitutionSetupDialog(new InstitutionSetupRequest("Bank", "37040044", endpoint)))
        {
            dialog.Accept();
            check(!dialog.GetResult().Succeeded && dialog.ErrorLabel.Text.Length > 0, "Bank login must be entered explicitly in setup.");
            dialog.UserIdEdit.Text = "bank-user";
            dialog.CustomerIdEdit.Text = "customer-42";
            dialog.Accept();
            check(dialog.GetResult() is { Succeeded: true, UserId: "bank-user", CustomerId: "customer-42" }, "Setup captures separate bank user/customer IDs.");
        }
        using (var dialog = new BroilerEndpointConfirmationDialog(endpoint))
        {
            check(dialog.HostLabel.Text.Contains(endpoint) && dialog.HostLabel.Text.Contains("Port: 8443"), "Endpoint review displays complete URL, port, and path/query.");
            dialog.Accept();
            check(!dialog.IsConfirmed, "Unacknowledged endpoint review is not confirmation.");
            dialog.VerifyCheckBox.IsChecked = true;
            dialog.Accept();
            check(dialog.IsConfirmed, "Explicit review acknowledgement can confirm the displayed endpoint.");
        }
        using (var dialog = new BroilerEndpointConfirmationDialog(endpoint))
        {
            dialog.VerifyCheckBox.IsChecked = true;
            dialog.Dialog.Accept();
            check(!dialog.IsConfirmed, "Dialog-level Enter/default acceptance cannot bypass endpoint review validation.");
        }
        foreach (string invalid in new[] { "http://bank.example/fints", "https://user:secret@bank.example/fints", "https://bank.example/fints#fragment", "https://bank.example/ bad", "https://bank.example\\fints", "https://" })
            Reject(() => BankConnectionSetup.Validate("Bank", invalid, null, "user", null), "Invalid endpoint is rejected without credential dispatch.");
        Reject(() => BankConnectionSetup.Validate("Bank", endpoint, null, new string('x', 36), null), "Bank user ID length is bounded.");
        Reject(() => BankConnectionSetup.Validate("Bank", endpoint, null, "user", "bad\ncustomer"), "Customer ID controls are rejected.");
        check(!BankProductConfiguration.FromSettings(null, null).IsConfigured &&
            !BankProductConfiguration.FromSettings("9999999999", "1.0").IsConfigured &&
            !BankProductConfiguration.FromSettings("BROILER-FOND", "1.0").IsConfigured, "Missing/default product placeholders are not production configuration.");
        check(!BankProductConfiguration.FromSettings("ID+bad", "1.0").IsConfigured &&
            !BankProductConfiguration.FromSettings("TEST-ID", "123456").IsConfigured, "Invalid FinTS product fields block configuration.");
        var product = BankProductConfiguration.FromSettings("TEST-APPLICATION-ID", "1.0");
        check(product.Registration!.ProductIdentifier == "TEST-APPLICATION-ID" && product.Registration.RegistrationCode == product.Registration.ProductIdentifier,
            "Explicit product ID populates the actual initialization product field.");

        string directory = Path.Combine(Path.GetTempPath(), "BroilerFond_BR02_" + Guid.NewGuid().ToString("N"));
        try
        {
            using var session = new HostSession(product);
            session.CreateNewProfile(directory, "SetupTests123!", "Connection setup");
            var alerts = new List<string>();
            bool trust = false;
            var dialogs = new BroilerDialogService
            {
                HeadlessAlertHandler = (_, message) => { alerts.Add(message); return Task.CompletedTask; },
                HeadlessInstitutionSetupHandler = _ => Task.FromResult(new InstitutionSetupResult(true, "Bank", "37040044", endpoint, false, "user", "customer")),
                HeadlessEndpointConfirmationHandler = url => { check(url == endpoint, "Review receives the exact persisted endpoint."); return Task.FromResult(trust); }
            };
            using var view = new ConnectionsView(session, dialogs);
            check(await view.ConfigureConnectionAsync(), "Add Connection saves explicit login details through the dialog service.");
            ulong id = session.Connections.Single().ConnectionId;
            var context = session.GetConnectionSetupContext(id);
            check(context.Connection.UserId == "user" && context.Connection.CustomerId == "customer" && context.Institution.VerificationState == "Unconfirmed",
                "Declining trust leaves login data saved and endpoint unconfirmed.");
            Reject(() => session.CreateBankTransport(context), "Unconfirmed setup cannot create a credential transport.");
            trust = true;
            check(await view.ConfirmSelectedEndpointAsync(), "Confirm Endpoint persists explicit trust for the selected connection.");
            context = session.GetConnectionSetupContext(id);
            check(BankConnectionSetup.ToDomain(context.Connection, context.Institution).InstitutionConfiguration.IsEndpointConfirmed,
                "Persisted connection maps to explicit confirmed kernel configuration.");
            session.AddManualAccount(id, "Household", "DE89370400440532013000", "EUR");
            context = session.GetConnectionSetupContext(id);
            session.Lock();
            session.Unlock(directory, "SetupTests123!");
            var reopened = session.GetConnectionSetupContext(id);
            check(reopened.Connection.UserId == "user" && reopened.Connection.CustomerId == "customer" && reopened.Institution.VerificationState == "Confirmed",
                "Login and exact endpoint trust survive reopening.");
            Reject(() => session.ConfirmConnectionEndpoint(context, endpoint), "Old-session review tokens cannot confirm a reopened session.");

            context = reopened;
            session.UpdateConnectionSetup(context, new(true, "Bank", "37040044", "https://other.example/fints", false, "user", "customer"));
            var changed = session.GetConnectionSetupContext(id);
            check(changed.Institution.VerificationState == "Quarantined" && !changed.Connection.IsSynchronized && changed.Connection.SystemId is null,
                "Endpoint edit revokes trust and resets bank session evidence.");
            check(session.Accounts.Count == 1 && session.CurrentSnapshot!.Balances.Balances.Count == 0, "Endpoint edit preserves account identity and saved data.");
            Reject(() => session.ConfirmConnectionEndpoint(context, endpoint), "A stale reviewed endpoint cannot be confirmed after editing.");
            session.UpdateConnectionSetup(changed, new(true, "Bank", "37040044", endpoint, false, "user", "customer"));
            Reject(() => session.ConfirmConnectionEndpoint(context, endpoint), "Edit-and-revert cannot revive a stale endpoint review revision.");
            context = session.GetConnectionSetupContext(id);
            Reject(() => session.ConfirmConnectionEndpoint(context, "https://bank.example:8443/different"), "Confirmation compares the full path, not just hostname.");
            session.ConfirmConnectionEndpoint(context, endpoint);

            // A login edit must not reuse authenticated system state, even at the same endpoint.
            session.GetStore()!.MutateAndSave(s => s with
            {
                Connections = s.Connections with
                {
                    Connections = s.Connections.Connections.Select(c => c with
                    {
                        State = "Connected",
                        SystemId = "BANK-REPORTED-SYSTEM",
                        IsSynchronized = true,
                        LastSuccessfulContact = DateTimeOffset.UtcNow,
                        LastAttemptedContact = DateTimeOffset.UtcNow
                    }).ToArray()
                }
            });
            session.ReloadData();
            dialogs.HeadlessInstitutionSetupHandler = request =>
            {
                check(request.InitialUserId == "user" && request.InitialCustomerId == "customer" && request.InitialEndpoint == endpoint,
                    "Edit Connection prepopulates the stored bank identity and endpoint.");
                return Task.FromResult(new InstitutionSetupResult(true, "Bank", "37040044", endpoint, false, "updated-user", "updated-customer"));
            };
            view.ConnectionsList.SelectedItemId = id.ToString();
            check(await view.ConfigureConnectionAsync(edit: true), "Edit Connection updates settings through the UI service seam.");
            var edited = session.GetConnectionSetupContext(id);
            check(edited.Connection.UserId == "updated-user" && edited.Connection.CustomerId == "updated-customer" &&
                edited.Connection.SystemId is null && !edited.Connection.IsSynchronized && edited.Connection.LastSuccessfulContact is null &&
                edited.Institution.VerificationState == "Confirmed", "Login edits preserve unchanged endpoint trust while requiring fresh bank synchronization.");

            // Exercise safe normalization of the earlier desktop's fabricated state.
            session.GetStore()!.MutateAndSave(s => s with
            {
                Institutions = s.Institutions with { Institutions = s.Institutions.Institutions.Select(i => i with { VerificationState = "Verified" }).ToArray() },
                Connections = s.Connections with
                {
                    Connections = s.Connections.Connections.Select(c => c with
                    {
                        UserId = "default-user",
                        State = "Synchronized",
                        SystemStatus = "Active",
                        SystemId = "SYS1",
                        IsSynchronized = true,
                        LastSuccessfulContact = DateTimeOffset.UtcNow,
                        LastAttemptedContact = DateTimeOffset.UtcNow
                    }).ToArray()
                }
            });
            session.ReloadData();
            var normalized = session.GetConnectionSetupContext(id);
            check(normalized.Institution.VerificationState == "Unconfirmed" && normalized.Connection.UserId == "" &&
                !normalized.Connection.IsSynchronized && normalized.Connection.SystemId is null && normalized.Connection.LastSuccessfulContact is null,
                "Known legacy fabricated state is normalized without retaining fake trust or bank contact.");
            check(session.Accounts.Count == 1, "Legacy normalization does not delete manual accounts.");
            ulong generation = normalized.Revision;
            session.ReloadData();
            check(session.Metadata!.Generation == generation, "Legacy normalization is idempotent.");
            session.UpdateConnectionSetup(normalized, new(true, "Bank", "37040044", endpoint, false, "user", "customer"));
            session.ConfirmConnectionEndpoint(session.GetConnectionSetupContext(id), endpoint);
            var current = session.GetConnectionSetupContext(id);
            Reject(() => BankConnectionSetup.ToDomain(current.Connection with { SystemStatus = "garbage" }, current.Institution),
                "Unknown stored state requires review rather than enum-default coercion.");

            await TestTransport(session, current);
            session.IsSafeModeActive = true;
            Reject(() => session.UpdateConnectionSetup(current, new(true, "Bank", "37040044", endpoint, false, "user")), "Safe Mode blocks setup changes.");

            using var noProduct = new HostSession();
            var result = await noProduct.BankRefresh.RefreshAsync(new BalanceRefreshRequest());
            check(result.Issues.Any(i => i.Code == BalanceRefreshOutcome.ProductConfigurationMissing), "Missing deployment product ID is reported before any credentials or requests.");
            check(alerts.Count == 0, "Normal setup and trust workflow finishes without error alerts.");
        }
        finally { if (Directory.Exists(directory)) Directory.Delete(directory, true); }

        async Task TestTransport(HostSession session, ConnectionSetupContext context)
        {
            int sends = 0;
            var handler = new Handler((_, _) => { sends++; return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent([1]) }); });
            using var transport = session.CreateBankTransport(context, handler);
            await transport.SendAsync(new Uri(endpoint), new byte[] { 1 });
            check(sends == 1, "Confirmed transport sends once to the reviewed HTTPS endpoint.");
            await RejectAsync(() => transport.SendAsync(new Uri("https://replacement.example/fints"), new byte[] { 1 }).AsTask(),
                "Transport refuses an unreviewed replacement endpoint.");
            check(sends == 1, "Endpoint rejection occurs before network dispatch.");
            using var redirect = session.CreateBankTransport(context, new Handler((_, _) =>
            {
                sends++;
                var response = new HttpResponseMessage(HttpStatusCode.Found);
                response.Headers.Location = new Uri("https://replacement.example/fints");
                return Task.FromResult(response);
            }));
            await RejectAsync(() => redirect.SendAsync(new Uri(endpoint), new byte[] { 1 }).AsTask(), "Redirect response fails rather than being accepted as bank data.");
            check(sends == 2, "A scripted redirect never triggers a second credential-bearing request.");
            using var tls = session.CreateBankTransport(context, new Handler((_, _) => throw new HttpRequestException("TLS failed", new AuthenticationException())));
            try { await tls.SendAsync(new Uri(endpoint), new byte[] { 1 }); check(false, "TLS failure must reject."); }
            catch (FinTsTransportException ex) { check(ex.Issue == FinTsTransportIssue.TlsFailure, "Certificate/TLS failures remain explicit without bypassing validation."); }
            using var timeout = session.CreateBankTransport(context, new Handler(async (_, ct) =>
            {
                await Task.Delay(Timeout.InfiniteTimeSpan, ct); return new HttpResponseMessage(HttpStatusCode.OK);
            }), TimeSpan.FromMilliseconds(20));
            try { await timeout.SendAsync(new Uri(endpoint), new byte[] { 1 }); check(false, "Timeout must reject."); }
            catch (FinTsTransportException ex) { check(ex.Issue == FinTsTransportIssue.Timeout, "Transport timeout has a distinct result."); }
            using var cancelled = new CancellationTokenSource();
            cancelled.Cancel();
            try { await transport.SendAsync(new Uri(endpoint), new byte[] { 1 }, cancelled.Token); check(false, "Cancellation must reject."); }
            catch (OperationCanceledException) { check(sends == 2, "Pre-cancellation prevents sending credentials."); }
            session.Lock();
            await RejectAsync(() => transport.SendAsync(new Uri(endpoint), new byte[] { 1 }).AsTask(), "Profile lock invalidates a previously created transport.");
            session.Unlock(directory, "SetupTests123!");
        }

        void Reject(Action action, string message)
        {
            try { action(); check(false, message); }
            catch (Exception ex) when (ex is ArgumentException or InvalidOperationException) { check(true, message); }
        }
        async Task RejectAsync(Func<Task> action, string message)
        {
            try { await action(); check(false, message); }
            catch (Exception ex) when (ex is InvalidOperationException or FinTsTransportException) { check(true, message); }
        }
    }

    private sealed class Handler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> send) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) => send(request, cancellationToken);
    }
}
