using System.Reflection;
using System.Text;
using System.Text.Json;
using Broiler.Fond.Kernel.FinTs;
using Broiler.Fond.Kernel.FinTs.Transport;
using Broiler.Fond.Presentation.Services;
using Broiler.Fond.Presentation.Sessions;

namespace Broiler.Fond.Presentation.Tests;

internal static class BankSynchronizationTests
{
    public static async Task RunAsync(Action<bool, string> check)
    {
        using var source = JsonDocument.Parse(Assembly.GetExecutingAssembly().GetManifestResourceStream("Synchronization.bank-responses.json")!);
        var vector = source.RootElement.GetProperty("vectors")[0];
        var input = vector.GetProperty("syncCandidate");
        var origin = FinTsUnsignedInitializationRequest.Parse(FinTsMessageFrame.Parse(Bytes(input, "originRequestBase64")));
        var response = FinTsResponse.Parse(FinTsMessageFrame.Parse(Bytes(input, "procedureResponseBase64")));
        var procedures = new FinTsPinTanProcedureContext(origin, FinTsTanParameterSet.Parse(FinTsParameterSet.Parse(response)), FinTsPermittedProcedureSet.Parse(response));
        var selection = new FinTsPinTanSignatureSelection(2, 900, 7);
        string directory = Path.Combine(Path.GetTempPath(), "BroilerFond_BR03_" + Guid.NewGuid().ToString("N"));
        try
        {
            using var session = new HostSession(BankProductConfiguration.FromSettings("ASSIGNED-APP-ID", "2.1"));
            session.CreateNewProfile(directory, "SyncTests123!", "Synchronization");
            ulong id = session.AddInstitutionAndConnection("Bank", "PUBLIC-BANK", "https://bank.example/fints", "PUBLIC-USER", "PUBLIC-CUSTOMER").ConnectionId;
            session.ConfirmConnectionEndpoint(session.GetConnectionSetupContext(id), "https://bank.example/fints");
            using var pin = FinTsSessionCredential.CaptureAndClear("PUBLIC-PIN"u8.ToArray(), FinTsCredentialKind.Pin, TimeSpan.FromMinutes(2));
            var context = session.GetConnectionSetupContext(id);
            var transport = Transport();
            var synchronized = await session.SynchronizeBankConnectionCoreAsync(context, pin, procedures, selection, () => transport);
            check(synchronized.IsSuccess && synchronized.Outcome == BankSynchronizationOutcome.Synchronized && transport.RecordedTransmissions.Count == 2,
                "Desktop synchronization uses production candidates, validates the bank reply and closes the dialogue.");
            check(transport.RecordedTransmissions.Any(t => Encoding.Latin1.GetString(t.Payload).Contains("ASSIGNED-APP-ID+2.1")),
                "Desktop request bytes contain deployment product registration rather than the kernel placeholder.");
            var saved = session.GetConnectionSetupContext(id);
            check(saved.Connection is { SystemId: "PUBLIC-ASSIGNED", IsSynchronized: true } && saved.Connection.LastSuccessfulContact.HasValue &&
                session.CurrentSnapshot!.Balances.Balances.Count == 0, "Synchronization persists bank-reported system state without fabricating financial data.");
            bool disposed = false;
            try { await transport.SendAsync(new Uri("https://bank.example/fints"), new byte[] { 1 }); }
            catch (ObjectDisposedException) { disposed = true; }
            check(disposed, "Desktop owns and disposes its per-operation transport.");
            session.Lock(); session.Unlock(directory, "SyncTests123!");
            check(session.GetConnectionSetupContext(id).Connection.SystemId == "PUBLIC-ASSIGNED", "Bank-reported synchronization survives profile reopening.");
            int factories = 0;
            var reused = await session.SynchronizeBankConnectionCoreAsync(session.GetConnectionSetupContext(id), pin, procedures, selection,
                () => { factories++; return Transport(); });
            check(reused.Outcome == BankSynchronizationOutcome.AlreadySynchronized && factories == 0, "Compatible synchronized context is reused without another network dialogue.");

            Reset();
            context = session.GetConnectionSetupContext(id);
            ulong generation = context.Revision;
            var closeFails = new FinTsSimulatedTransport();
            closeFails.EnqueueResponse(Bytes(vector.GetProperty("syncResponse"), "wireBase64"));
            closeFails.EnqueueFailure(new FinTsTransportException(FinTsTransportIssue.Timeout, "Close timeout", new Uri("https://bank.example/fints")));
            var failed = await session.SynchronizeBankConnectionCoreAsync(context, pin, procedures, selection, () => closeFails);
            check(!failed.IsSuccess && session.Metadata!.Generation == generation && session.GetConnectionSetupContext(id).Connection.SystemId is null,
                "An unconfirmed close cannot publish synchronized state or a new generation.");
            var invalid = await session.SynchronizeBankConnectionCoreAsync(context, pin, procedures, new FinTsPinTanSignatureSelection(2, 901, 7),
                () => { factories++; return Transport(); });
            check(invalid.Outcome == BankSynchronizationOutcome.UnsupportedCandidate && factories == 0,
                "Missing permitted procedure rejects before transport creation or PIN encoding.");

            var begun = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var slow = new FinTsSimulatedTransport();
            slow.EnqueueHandler(async (_, _, ct) =>
            {
                begun.TrySetResult(); await Task.Delay(Timeout.InfiniteTimeSpan, ct);
                throw new InvalidOperationException("unreachable");
            });
            var active = session.SynchronizeBankConnectionCoreAsync(context, pin, procedures, selection, () => slow);
            await begun.Task.WaitAsync(TimeSpan.FromSeconds(5));
            var duplicate = await session.SynchronizeBankConnectionCoreAsync(context, pin, procedures, selection, () => Transport());
            check(duplicate.Outcome == BankSynchronizationOutcome.AlreadyRunning, "Synchronization shares the per-connection operation reservation.");
            session.Lock(); session.Unlock(directory, "SyncTests123!");
            var stale = await active.WaitAsync(TimeSpan.FromSeconds(5));
            check(stale.Outcome == BankSynchronizationOutcome.ContextChanged && session.GetConnectionSetupContext(id).Connection.SystemId is null,
                "Lock/reopen invalidates an in-flight synchronization result and cannot publish stale state.");
            using var cancelled = new CancellationTokenSource();
            cancelled.Cancel();
            var cancelledResult = await session.SynchronizeBankConnectionCoreAsync(session.GetConnectionSetupContext(id), pin, procedures, selection,
                () => { factories++; return Transport(); }, cancelled.Token);
            check(cancelledResult.Outcome == BankSynchronizationOutcome.Cancelled && factories == 0, "Pre-cancelled synchronization does not dispatch.");

            FinTsSessionCredential? acquired = null;
            int prompts = 0;
            var dialogs = new BroilerDialogService
            {
                HeadlessBankCredentialHandler = (request, _) =>
            {
                prompts++;
                check(!request.IsTan && request.PromptMessage.Contains("not the profile passphrase"), "Bank login explicitly requests a separate banking PIN.");
                acquired = FinTsSessionCredential.CaptureAndClear("secret-marker"u8.ToArray(), FinTsCredentialKind.Pin, TimeSpan.FromMinutes(1));
                return Task.FromResult<FinTsSessionCredential?>(acquired);
            }
            };
            var interactive = await session.AuthenticateAndSynchronizeBankConnectionCoreAsync(session.GetConnectionSetupContext(id), dialogs,
                procedures, selection, () => Transport());
            check(interactive.IsSuccess && prompts == 1 && acquired!.GetSnapshot().State == FinTsCredentialState.Disposed,
                "Interactive synchronization owns and disposes the banking PIN after one authenticated exchange.");
            check(!interactive.Explanation.Contains("secret-marker") && !System.Text.Json.JsonSerializer.Serialize(session.CurrentSnapshot).Contains("secret-marker"),
                "Bank PIN marker is absent from the persisted snapshot and user-facing result.");
            var noPrompt = await session.AuthenticateAndSynchronizeBankConnectionCoreAsync(session.GetConnectionSetupContext(id), dialogs,
                procedures, selection, () => Transport());
            check(noPrompt.Outcome == BankSynchronizationOutcome.AlreadySynchronized && prompts == 1, "Reusable synchronized state does not prompt for a PIN.");
            Reset();
            dialogs.HeadlessBankCredentialHandler = (_, _) => Task.FromResult<FinTsSessionCredential?>(null);
            int dispatch = 0;
            var declined = await session.AuthenticateAndSynchronizeBankConnectionCoreAsync(session.GetConnectionSetupContext(id), dialogs,
                procedures, selection, () => { dispatch++; return Transport(); });
            check(declined.Outcome == BankSynchronizationOutcome.Cancelled && dispatch == 0, "Declining bank sign-in never creates a transport.");
            await session.BankPromptGate.WaitAsync();
            using (var queuedCancellation = new CancellationTokenSource())
            {
                int queuedPrompts = 0;
                dialogs.HeadlessBankCredentialHandler = (_, _) => { queuedPrompts++; return Task.FromResult<FinTsSessionCredential?>(null); };
                var queued = session.AuthenticateAndSynchronizeBankConnectionCoreAsync(session.GetConnectionSetupContext(id), dialogs,
                    procedures, selection, () => { dispatch++; return Transport(); }, queuedCancellation.Token);
                check(!queued.IsCompleted && queuedPrompts == 0, "Shared bank prompt gate prevents overlapping credential prompts.");
                queuedCancellation.Cancel();
                var queuedResult = await queued.WaitAsync(TimeSpan.FromSeconds(5));
                check(queuedResult.Outcome == BankSynchronizationOutcome.Cancelled && queuedPrompts == 0, "A queued bank prompt can be cancelled before collecting a credential.");
            }
            session.BankPromptGate.Release();
            using (var lateCancellation = new CancellationTokenSource())
            {
                var lateOwner = FinTsSessionCredential.CaptureAndClear("secret-marker"u8.ToArray(), FinTsCredentialKind.Pin, TimeSpan.FromMinutes(1));
                dialogs.HeadlessBankCredentialHandler = (_, _) =>
                {
                    lateCancellation.Cancel();
                    return Task.FromResult<FinTsSessionCredential?>(lateOwner);
                };
                bool wasCancelled = false;
                try { await dialogs.AcquireBankCredentialAsync(new("PIN", "Bank PIN", "Bank"), lateCancellation.Token); }
                catch (OperationCanceledException) { wasCancelled = true; }
                check(wasCancelled && lateOwner.GetSnapshot().State == FinTsCredentialState.Disposed, "A credential returned concurrently with cancellation is disposed instead of transferred.");
            }
            dialogs.HeadlessBankCredentialHandler = (_, _) => throw new Exception("secret-marker");
            var promptFailure = await session.AuthenticateAndSynchronizeBankConnectionCoreAsync(session.GetConnectionSetupContext(id), dialogs,
                procedures, selection, () => { dispatch++; return Transport(); });
            check(promptFailure.Outcome == BankSynchronizationOutcome.Failed && !promptFailure.Explanation.Contains("secret-marker") && dispatch == 0,
                "Arbitrary prompt failures cannot leak secret exception text or start a bank request.");
            dialogs.HeadlessBankCredentialHandler = async (_, ct) =>
            {
                session.Lock();
                await Task.Delay(Timeout.InfiniteTimeSpan, ct);
                return null;
            };
            var lockedPrompt = await session.AuthenticateAndSynchronizeBankConnectionCoreAsync(session.GetConnectionSetupContext(id), dialogs,
                procedures, selection, () => { dispatch++; return Transport(); });
            check(lockedPrompt.Outcome == BankSynchronizationOutcome.Cancelled && dispatch == 0, "Profile lock cancels an outstanding bank PIN prompt before dispatch.");

            void Reset()
            {
                session.GetStore()!.MutateAndSave(s => s with
                {
                    Connections = s.Connections with
                    {
                        Connections = s.Connections.Connections.Select(c => c with
                        {
                            State = "Unconfigured",
                            SystemId = null,
                            IsSynchronized = false,
                            LastSuccessfulContact = null,
                            LastAttemptedContact = null
                        }).ToArray()
                    }
                });
                session.ReloadData();
            }
        }
        finally { if (Directory.Exists(directory)) Directory.Delete(directory, true); }

        FinTsSimulatedTransport Transport()
        {
            var transport = new FinTsSimulatedTransport();
            transport.EnqueueResponse(Bytes(vector.GetProperty("syncResponse"), "wireBase64"));
            transport.EnqueueResponse(Bytes(vector.GetProperty("closingResponse"), "wireBase64"));
            return transport;
        }
    }

    private static byte[] Bytes(JsonElement element, string property) => Convert.FromBase64String(element.GetProperty(property).GetString()!);
}
