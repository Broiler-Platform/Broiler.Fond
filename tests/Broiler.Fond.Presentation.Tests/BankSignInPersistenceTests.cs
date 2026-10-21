using System.Reflection;
using System.Text.Json;
using Broiler.Fond.Kernel.FinTs;
using Broiler.Fond.Kernel.FinTs.Transport;
using Broiler.Fond.Presentation.Services;
using Broiler.Fond.Presentation.Sessions;
using Broiler.Fond.Presentation.Views;
using Broiler.Fond.TestSupport;

namespace Broiler.Fond.Presentation.Tests;

internal static class BankSignInPersistenceTests
{
    internal static async Task RunAsync(Action<bool, string> check)
    {
        using var fixture = JsonDocument.Parse(Assembly.GetExecutingAssembly().GetManifestResourceStream("Synchronization.bank-responses.json")!);
        var vector = fixture.RootElement.GetProperty("vectors")[0];
        var input = vector.GetProperty("syncCandidate");
        var origin = FinTsUnsignedInitializationRequest.Parse(FinTsMessageFrame.Parse(Bytes(input, "originRequestBase64")));
        var response = FinTsResponse.Parse(FinTsMessageFrame.Parse(Bytes(input, "procedureResponseBase64")));
        var procedures = new BankProcedureSelection(new(origin, FinTsTanParameterSet.Parse(FinTsParameterSet.Parse(response)), FinTsPermittedProcedureSet.Parse(response)), new(2, 900, 7));
        string root = Path.Combine(Path.GetTempPath(), "BroilerFond_SignIn_" + Guid.NewGuid().ToString("N"));
        try
        {
            foreach (string scenario in new[] { "save-fails", "close-fails", "rejected", "success", "projection-fails" })
            {
                string directory = Path.Combine(root, scenario);
                var owners = new List<FinTsSessionCredential>();
                var alerts = new List<string>();
                int transports = 0, prompts = 0;
                bool saving = false;
                using var session = new HostSession(BankProductConfiguration.FromSettings("ASSIGNED-APP-ID", "2.1"), null, null,
                    _ => Transport(), synchronizationBeforeSave: scenario == "save-fails" ? _ => throw new IOException("SECRET-MARKER") : null);
                session.CreateNewProfile(directory, "SignInTests123!", "Sign-in");
                ulong id = session.AddInstitutionAndConnection("Bank", "PUBLIC-BANK", "https://bank.example/fints", "PUBLIC-USER", "PUBLIC-CUSTOMER").ConnectionId;
                session.ConfirmConnectionEndpoint(session.GetConnectionSetupContext(id), "https://bank.example/fints");
                session.SelectBankProcedure(session.GetConnectionSetupContext(id), procedures);
                var original = session.GetConnectionSetupContext(id).Connection;
                ulong generation = session.Metadata!.Generation;
                var dialogs = new BroilerDialogService
                {
                    HeadlessAlertHandler = (_, message) => { alerts.Add(message); return Task.CompletedTask; },
                    HeadlessBankCredentialHandler = (_, _) =>
                    {
                        prompts++;
                        var owner = FinTsSessionCredential.CaptureAndClear("SECRET-MARKER"u8.ToArray(), FinTsCredentialKind.Pin, TimeSpan.FromMinutes(1));
                        owners.Add(owner); return Task.FromResult<FinTsSessionCredential?>(owner);
                    }
                };
                using var view = new ConnectionsView(session, dialogs);
                view.ConnectionsList.SelectedItemId = id.ToString();
                session.OnlineSetup.Changed += (_, _) => saving |= session.OnlineSetup.ProgressText.Contains("Saving synchronized");
                // Observer failure after persistence must never rewrite a durable success into a failed save.
                EventHandler observer = (_, _) =>
                { if (session.IsUnlocked && session.GetConnectionSetupContext(id).Connection.IsSynchronized) throw new InvalidOperationException("SECRET-MARKER"); };
                if (scenario == "success") session.StateChanged += observer;
                EventHandler<AccountListChangedEventArgs> projectionObserver = (_, _) =>
                { if (session.IsUnlocked && session.GetConnectionSetupContext(id).Connection.IsSynchronized) throw new OperationCanceledException("SECRET-MARKER"); };
                if (scenario == "projection-fails") session.AccountListChanged += projectionObserver;
                await view.ConnectSelectedAsync();
                session.StateChanged -= observer;
                session.AccountListChanged -= projectionObserver;
                bool success = scenario is "success" or "projection-fails";
                check(prompts == 1 && transports == 1 && owners.All(o => o.GetSnapshot().State == FinTsCredentialState.Disposed),
                    $"{scenario}: sign-in uses one owned PIN and one exchange without credential retry or retained owners.");
                check(alerts.All(a => !a.Contains("SECRET-MARKER")), $"{scenario}: no banking credential or exception marker enters feedback.");
                if (!success)
                {
                    check(session.Metadata.Generation == generation && session.GetConnectionSetupContext(id).Connection == original,
                        $"{scenario}: rejection, uncertain close or failed save preserves the previous durable connection state.");
                    if (scenario == "save-fails") check(saving && alerts.Last().Contains("could not be saved") && !alerts.Last().Contains("synchronized and saved"),
                        "Closed bank synchronization followed by a save failure has explicit storage recovery feedback and no success claim.");
                }
                else check(saving && alerts.Last().Contains("synchronized and saved") && session.GetConnectionSetupContext(id).Connection.SystemId is not null,
                    "A closed saved exchange reports success even if a post-save state observer fails.");
                session.Lock(); session.Unlock(directory, "SignInTests123!");
                var reopened = session.GetConnectionSetupContext(id).Connection;
                check(success ? reopened.IsSynchronized && reopened.State == "Connected" && reopened.SystemId is not null : reopened == original,
                    $"{scenario}: reopen agrees with durable sign-in outcome and never publishes an unsaved system ID.");

                FinTsSimulatedTransport Transport()
                {
                    transports++; var transport = new FinTsSimulatedTransport();
                    if (scenario == "rejected") transport.EnqueueResponse(BankParameterTestWire.Frame("SYNTHETIC", 1, "HIRMG:2:2+9800::PUBLIC-REJECT'", 3));
                    else transport.EnqueueResponse(Bytes(vector.GetProperty("syncResponse"), "wireBase64"));
                    if (scenario == "close-fails") transport.EnqueueFailure(new FinTsTransportException(FinTsTransportIssue.Timeout, "SECRET-MARKER", new Uri("https://bank.example/fints")));
                    else transport.EnqueueResponse(Bytes(vector.GetProperty("closingResponse"), "wireBase64"));
                    return transport;
                }
            }
            // Reserve ownership before entering a PIN prompt, including the time queued behind other prompts.
            using var admission = new HostSession(BankProductConfiguration.FromSettings("ASSIGNED-APP-ID", "2.1"));
            admission.CreateNewProfile(Path.Combine(root, "admission"), "SignInTests123!", "Admission");
            ulong connection = admission.AddInstitutionAndConnection("Bank", "PUBLIC-BANK", "https://bank.example/fints", "PUBLIC-USER", "PUBLIC-CUSTOMER").ConnectionId;
            admission.ConfirmConnectionEndpoint(admission.GetConnectionSetupContext(connection), "https://bank.example/fints");
            var context = admission.GetConnectionSetupContext(connection);
            int pinPrompts = 0;
            var prompt = new BroilerDialogService { HeadlessBankCredentialHandler = (_, _) => { pinPrompts++; return Task.FromResult<FinTsSessionCredential?>(null); } };
            await admission.BankPromptGate.WaitAsync();
            using var cancel = new CancellationTokenSource();
            var queued = admission.AuthenticateAndSynchronizeBankConnectionCoreAsync(context, prompt, procedures.Procedures, procedures.Selection, null, cancel.Token);
            var duplicate = await admission.AuthenticateAndSynchronizeBankConnectionCoreAsync(context, prompt, procedures.Procedures, procedures.Selection, null);
            check(duplicate.Outcome == BankSynchronizationOutcome.AlreadyRunning && pinPrompts == 0,
                "A competing sign-in is rejected before asking for a banking PIN, even while the owner is queued for its prompt.");
            cancel.Cancel(); await queued.WaitAsync(TimeSpan.FromSeconds(5)); admission.BankPromptGate.Release();
            var retry = await admission.AuthenticateAndSynchronizeBankConnectionCoreAsync(context, prompt, procedures.Procedures, procedures.Selection, null);
            check(retry.Outcome == BankSynchronizationOutcome.Cancelled && pinPrompts == 1, "Cancelled queued sign-in releases its connection lease for a fresh user attempt.");
        }
        finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
    }
    private static byte[] Bytes(JsonElement element, string name) => Convert.FromBase64String(element.GetProperty(name).GetString()!);
}
