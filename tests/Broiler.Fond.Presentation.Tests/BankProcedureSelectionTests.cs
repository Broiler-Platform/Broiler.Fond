using System.Reflection;
using System.Text;
using System.Text.Json;
using Broiler.Fond.Kernel.FinTs;
using Broiler.Fond.Kernel.FinTs.Transport;
using Broiler.Fond.Presentation.Dialogs;
using Broiler.Fond.Presentation.Services;
using Broiler.Fond.Presentation.Sessions;
using Broiler.Fond.Presentation.Views;
using Broiler.Fond.TestSupport;

namespace Broiler.Fond.Presentation.Tests;

internal static class BankProcedureSelectionTests
{
    internal static async Task RunAsync(Action<bool, string> check)
    {
        using var fixture = JsonDocument.Parse(Assembly.GetExecutingAssembly().GetManifestResourceStream("Synchronization.bank-responses.json")!);
        var vector = fixture.RootElement.GetProperty("vectors")[0];
        byte[] identified = Bytes(vector.GetProperty("syncCandidate"), "procedureResponseBase64");
        string tan = Encoding.Latin1.GetString(identified).Split('\'').Single(s => s.StartsWith("HITANS:", StringComparison.Ordinal));
        tan = "HITANS:6:7:3" + tan[tan.IndexOf('+')..] + "'";
        string directory = Path.Combine(Path.GetTempPath(), "BroilerFond_Procedure_" + Guid.NewGuid().ToString("N"));
        int downloads = 0, syncs = 0, pins = 0, choices = 0;
        string mode = "cancel";
        var alerts = new List<string>();
        var credentials = new List<FinTsSessionCredential>();
        try
        {
            using var session = new HostSession(BankProductConfiguration.FromSettings("ASSIGNED-APP-ID", "2.1"), null, null, _ => SyncTransport(), _ => ParameterTransport());
            session.CreateNewProfile(directory, "ProcedureTests123!", "Procedures");
            ulong id = session.AddInstitutionAndConnection("Bank", "PUBLIC-BANK", "https://bank.example/fints", "PUBLIC-USER", "PUBLIC-CUSTOMER").ConnectionId;
            session.ConfirmConnectionEndpoint(session.GetConnectionSetupContext(id), "https://bank.example/fints");
            var dialogs = new BroilerDialogService
            {
                HeadlessAlertHandler = (_, text) => { alerts.Add(text); return Task.CompletedTask; },
                HeadlessBankProcedureChoiceHandler = (request, _) =>
                {
                    choices++;
                    check(request.Options.Any(o => o.SecurityFunction == 900) && request.Options.Any(o => o.SecurityFunction == 999),
                        "Procedure chooser receives actual supported advertisements, including explicit one-step availability.");
                    return Task.FromResult<string?>(mode == "cancel" ? null : mode == "invalid" ? "invented" : "7:900");
                },
                HeadlessBankCredentialHandler = (_, _) =>
                {
                    pins++;
                    var credential = FinTsSessionCredential.CaptureAndClear("PIN-MARKER"u8.ToArray(), FinTsCredentialKind.Pin, TimeSpan.FromMinutes(1));
                    credentials.Add(credential); return Task.FromResult<FinTsSessionCredential?>(credential);
                }
            };
            using var view = new ConnectionsView(session, dialogs);
            view.ConnectionsList.SelectedItemId = id.ToString();
            ulong generation = session.Metadata!.Generation;
            await view.ConnectSelectedAsync();
            check(choices == 1 && downloads == 1 && pins == 0 && session.Metadata.Generation == generation,
                "Cancelled explicit procedure selection stops before identified download, PIN entry or profile writes.");
            mode = "invalid";
            await view.ConnectSelectedAsync();
            check(downloads == 1 && pins == 0 && await session.BankProcedures.GetBankProcedureAsync(session.GetConnectionSetupContext(id)) is null,
                "An invented selection ID cannot trigger a login-specific exchange or populate the provider.");
            mode = "wrong-user";
            await view.ConnectSelectedAsync();
            check(downloads == 2 && pins == 0 && !session.GetConnectionSetupContext(id).Connection.IsSynchronized &&
                await session.BankProcedures.GetBankProcedureAsync(session.GetConnectionSetupContext(id)) is null,
                "A mismatched identified user response is rejected before PIN entry and cannot promote anonymous permissions.");
            mode = "success";
            await view.ConnectSelectedAsync();
            var current = session.GetConnectionSetupContext(id);
            check(current.Connection.IsSynchronized && downloads == 3 && syncs == 1 && pins == 1,
                "Desktop Connect can download public parameters, select, qualify the login-specific procedure and synchronize a new connection.");
            check(await session.BankProcedures.GetBankProcedureAsync(current) is not null && credentials.All(c => c.GetSnapshot().State == FinTsCredentialState.Disposed),
                "Qualified procedure provenance survives expected synchronization changes while owned PIN buffers are disposed.");
            int before = choices;
            await view.ConnectSelectedAsync();
            check(choices == before && pins == 1 && alerts.Last().Contains("does not verify a new sign-in"),
                "A current qualified synchronized connection does not re-prompt or claim a fresh authentication.");
            session.Lock(); session.Unlock(directory, "ProcedureTests123!");
            view.ConnectionsList.SelectedItemId = id.ToString();
            check(await session.BankProcedures.GetBankProcedureAsync(session.GetConnectionSetupContext(id)) is null,
                "Lock/reopen invalidates qualified login-specific procedure state.");
            await view.ConnectSelectedAsync();
            check(choices == before + 1 && downloads == 5 && pins == 1 && await session.BankProcedures.GetBankProcedureAsync(session.GetConnectionSetupContext(id)) is not null,
                "Connect requalifies an already synchronized connection after lock instead of getting stuck without a provider.");
            session.Lock(); session.Unlock(directory, "ProcedureTests123!");
            view.ConnectionsList.SelectedItemId = id.ToString();
            mode = "changed-method";
            await view.ConnectSelectedAsync();
            check(await session.BankProcedures.GetBankProcedureAsync(session.GetConnectionSetupContext(id)) is null &&
                session.BankParameters.GetCurrent(session.GetConnectionSetupContext(id)) is null && pins == 1,
                "A changed method definition invalidates the public observation and requires a fresh explicit choice before PIN entry.");
            mode = "success";
            await view.ConnectSelectedAsync();
            current = session.GetConnectionSetupContext(id);
            session.UpdateConnectionSetup(current, new(true, "Bank", "PUBLIC-BANK", current.Institution.Endpoint, false, "OTHER-USER", "PUBLIC-CUSTOMER"));
            check(await session.BankProcedures.GetBankProcedureAsync(session.GetConnectionSetupContext(id)) is null && alerts.All(a => !a.Contains("PIN-MARKER")),
                "Login edits clear qualified procedure state and feedback excludes banking secrets.");
            var selecting = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            dialogs.HeadlessBankProcedureChoiceHandler = async (_, ct) =>
            { selecting.TrySetResult(); await Task.Delay(Timeout.InfiniteTimeSpan, ct); return null; };
            var pending = view.ConnectSelectedAsync();
            await selecting.Task.WaitAsync(TimeSpan.FromSeconds(5));
            current = session.GetConnectionSetupContext(id);
            session.UpdateConnectionSetup(current, new(true, "Bank", "PUBLIC-BANK", current.Institution.Endpoint, false, "THIRD-USER", "PUBLIC-CUSTOMER"));
            await pending.WaitAsync(TimeSpan.FromSeconds(5));
            check(alerts.Last().Contains("cancelled") && pins == 1, "Connection edits cancel an open procedure chooser before any PIN or identified qualification request.");
        }
        finally { if (Directory.Exists(directory)) Directory.Delete(directory, true); }

        var request = new BankProcedureChoiceRequest("Bank", "https://bank.example/fints", [new("7:900", "Bank method", true, "Details", 2, 900, 7),
            new("7:901", "Device method", false, "Bank-reported media inventory is required.", 2, 901, 7)]);
        using (var dialog = new BroilerBankProcedureDialog(request))
        {
            dialog.ChooseButton.Click();
            check(dialog.ProcedureComboBox.SelectedItem is null && dialog.GetSelection() is null && dialog.ErrorLabel.Text.Contains("explicitly"),
                "Procedure dialog has no automatically selected default and cannot accept without an explicit choice.");
            dialog.ProcedureComboBox.SelectedIndex = 1; dialog.ChooseButton.Click();
            check(dialog.GetSelection() is null && dialog.ErrorLabel.Text.Contains("inventory"), "Unsupported media requirements cannot be accepted as a procedure choice.");
            dialog.ProcedureComboBox.SelectedIndex = 0; dialog.ChooseButton.Click();
            check(dialog.GetSelection() == "7:900", "The chooser returns the explicitly accepted advertised option ID.");
        }
        using (var mediaTransport = new FinTsSimulatedTransport())
        {
            mediaTransport.EnqueueResponse(BankParameterTestWire.Initialization(extra: tan.Replace(":00:0:N:0", ":00:2:N:0", StringComparison.Ordinal)));
            mediaTransport.EnqueueResponse(BankParameterTestWire.Closing());
            var media = await FinTsBankParameterBootstrap.ExecuteAsync(mediaTransport, new Uri("https://bank.example/fints"), "280", "PUBLIC-BANK", "ASSIGNED-APP-ID", "2.1");
            check(media.Observation is not null && !BankProcedureChoices.Create(media.Observation).Single(o => o.SecurityFunction == 900).CanSelect,
                "Actual advertised medium-name requirements are surfaced as unsupported instead of inventing a TAN device.");
        }
        using (var decoupledTransport = new FinTsSimulatedTransport())
        {
            decoupledTransport.EnqueueResponse(BankParameterTestWire.Initialization(extra: tan.Replace(":App:1.0:Public method:6:1:", ":DecoupledPush:1.0:Public method:::", StringComparison.Ordinal)));
            decoupledTransport.EnqueueResponse(BankParameterTestWire.Closing());
            var decoupled = await FinTsBankParameterBootstrap.ExecuteAsync(decoupledTransport, new Uri("https://bank.example/fints"), "280", "PUBLIC-BANK", "ASSIGNED-APP-ID", "2.1");
            check(decoupled.Observation is not null && !BankProcedureChoices.Create(decoupled.Observation).Single(o => o.SecurityFunction == 900).CanSelect,
                "Decoupled approval procedures are surfaced as unsupported in the text-TAN build.");
        }

        string tanDirectory = Path.Combine(Path.GetTempPath(), "BroilerFond_SignedTan_" + Guid.NewGuid().ToString("N"));
        byte[] template = Bytes(vector.GetProperty("syncResponse"), "wireBase64");
        byte[] challengeInitial = SignedTanTestWire.Initial(template, identified);
        string tanScenario = "cancel-tan";
        int tanPrompts = 0, pinPrompts = 0;
        bool awaitingObserved = false;
        FinTsSimulatedTransport? lastTanTransport = null;
        var ownedPins = new List<FinTsSessionCredential>();
        var ownedTans = new List<FinTsSessionCredential>();
        var tanAlerts = new List<string>();
        try
        {
            using var session = new HostSession(BankProductConfiguration.FromSettings("ASSIGNED-APP-ID", "2.1"), null, null,
                _ => TanSyncTransport(), _ => ParameterTransport());
            session.CreateNewProfile(tanDirectory, "SignedTanTests123!", "SignedTan");
            ulong id = session.AddInstitutionAndConnection("Bank", "PUBLIC-BANK", "https://bank.example/fints", "PUBLIC-USER", "PUBLIC-CUSTOMER").ConnectionId;
            session.ConfirmConnectionEndpoint(session.GetConnectionSetupContext(id), "https://bank.example/fints");
            mode = "success";
            session.OnlineSetup.Changed += (_, _) => awaitingObserved |= session.OnlineSetup.ProgressText.Contains("Awaiting bank approval");
            var dialogs = new BroilerDialogService
            {
                HeadlessAlertHandler = (_, text) => { tanAlerts.Add(text); return Task.CompletedTask; },
                HeadlessBankProcedureChoiceHandler = (_, _) => Task.FromResult<string?>("7:900"),
                HeadlessBankCredentialHandler = (req, _) =>
                {
                    if (!req.IsTan)
                    {
                        pinPrompts++;
                        var pin = FinTsSessionCredential.CaptureAndClear("PIN-SECRET"u8.ToArray(), FinTsCredentialKind.Pin, TimeSpan.FromMinutes(1));
                        ownedPins.Add(pin);
                        return Task.FromResult<FinTsSessionCredential?>(pin);
                    }
                    tanPrompts++;
                    check(req.PromptMessage.Contains("Enter the TAN shown for bank sign-in") && req.TanProcedureName == "Public method (900)",
                        "TAN prompt surfaces the sanitized bank challenge text and qualified procedure name.");
                    if (tanScenario == "cancel-tan") return Task.FromResult<FinTsSessionCredential?>(null);
                    var tanCred = FinTsSessionCredential.CaptureAndClear("123456"u8.ToArray(), FinTsCredentialKind.Tan, TimeSpan.FromMinutes(1));
                    ownedTans.Add(tanCred);
                    if (tanScenario == "lock-during-tan") session.Lock();
                    return Task.FromResult<FinTsSessionCredential?>(tanCred);
                }
            };
            using var view = new ConnectionsView(session, dialogs);
            view.ConnectionsList.SelectedItemId = id.ToString();
            ulong generation = session.Metadata!.Generation;

            await view.ConnectSelectedAsync();
            check(awaitingObserved && pinPrompts == 1 && tanPrompts == 1 && lastTanTransport?.TransmittedCount == 1 &&
                session.Metadata.Generation == generation && !session.GetConnectionSetupContext(id).Connection.IsSynchronized &&
                ownedPins.All(p => p.GetSnapshot().State == FinTsCredentialState.Disposed),
                "Cancelling the TAN prompt stops before message-2 TAN dispatch and leaves durable state unchanged.");

            tanScenario = "reject-tan";
            await view.ConnectSelectedAsync();
            check(pinPrompts == 2 && tanPrompts == 2 && lastTanTransport?.TransmittedCount == 2 &&
                session.Metadata.Generation == generation && !session.GetConnectionSetupContext(id).Connection.IsSynchronized &&
                tanAlerts.Last().Contains("credentials were not retried") &&
                ownedPins.All(p => p.GetSnapshot().State == FinTsCredentialState.Disposed) &&
                ownedTans.All(t => t.GetSnapshot().State == FinTsCredentialState.Consumed),
                "Rejected signed TAN confirmation stops without message-3 close, credential retry, or durable mutation.");

            tanScenario = "confirm-tan";
            await view.ConnectSelectedAsync();
            var synchronized = session.GetConnectionSetupContext(id).Connection;
            check(pinPrompts == 3 && tanPrompts == 3 && lastTanTransport?.TransmittedCount == 3 &&
                synchronized is { IsSynchronized: true, State: "Connected", SystemId: "PUBLIC-ASSIGNED" } &&
                ownedPins.All(p => p.GetSnapshot().State == FinTsCredentialState.Disposed) &&
                ownedTans.All(t => t.GetSnapshot().State == FinTsCredentialState.Consumed) &&
                tanAlerts.All(a => !a.Contains("PIN-SECRET") && !a.Contains("123456")),
                "Signed process-2 text-TAN approval completes message-2 confirmation, message-3 close, and durable synchronization.");
            session.Lock(); session.Unlock(tanDirectory, "SignedTanTests123!");
            check(session.GetConnectionSetupContext(id).Connection is { IsSynchronized: true, SystemId: "PUBLIC-ASSIGNED" },
                "Synchronized system state from signed TAN approval survives profile reopen.");

            ulong secondId = session.AddInstitutionAndConnection("Second Bank", "PUBLIC-BANK", "https://bank.example/fints", "PUBLIC-USER", "PUBLIC-CUSTOMER").ConnectionId;
            session.ConfirmConnectionEndpoint(session.GetConnectionSetupContext(secondId), "https://bank.example/fints");
            view.ConnectionsList.SelectedItemId = secondId.ToString();
            tanScenario = "lock-during-tan";
            await view.ConnectSelectedAsync();
            session.Unlock(tanDirectory, "SignedTanTests123!");
            check(lastTanTransport?.TransmittedCount == 1 && !session.GetConnectionSetupContext(secondId).Connection.IsSynchronized &&
                ownedPins.All(p => p.GetSnapshot().State == FinTsCredentialState.Disposed) &&
                ownedTans.Last().GetSnapshot().State == FinTsCredentialState.Disposed,
                "Profile lock racing a returned TAN disposes both PIN and TAN without dispatching message 2.");
        }
        finally { if (Directory.Exists(tanDirectory)) Directory.Delete(tanDirectory, true); }

        FinTsSimulatedTransport TanSyncTransport()
        {
            var transport = new FinTsSimulatedTransport();
            lastTanTransport = transport;
            transport.EnqueueResponse(challengeInitial);
            transport.EnqueueResponse(tanScenario == "reject-tan" ? SignedTanTestWire.Confirm(template, "WRONG-ORDER") : SignedTanTestWire.Confirm(template));
            transport.EnqueueResponse(SignedTanTestWire.Close(template));
            return transport;
        }

        FinTsSimulatedTransport ParameterTransport()
        {
            downloads++; bool anonymous = false;
            var transport = new FinTsSimulatedTransport();
            transport.EnqueueHandler((_, wire, _) =>
            {
                anonymous = FinTsUnsignedInitializationRequest.Parse(FinTsMessageFrame.Parse(wire.Span)).Identification.IsAnonymous;
                byte[] response = anonymous ? BankParameterTestWire.Initialization(extra: tan) : mode == "wrong-user" ?
                    BankParameterTestWire.Edit(identified, s => s.Replace("PUBLIC-USER", "WRONG-USER", StringComparison.Ordinal)) : mode == "changed-method" ?
                    BankParameterTestWire.Edit(identified, s => s.Replace("Public method", "Changed method", StringComparison.Ordinal)) : identified;
                return ValueTask.FromResult(new FinTsTransportResponse(200, response, null, TimeSpan.Zero));
            });
            transport.EnqueueHandler((_, _, _) => ValueTask.FromResult(new FinTsTransportResponse(200,
                BankParameterTestWire.Closing(anonymous ? "BOOTSTRAP" : "SYNTHETIC"), null, TimeSpan.Zero)));
            return transport;
        }
        FinTsSimulatedTransport SyncTransport()
        {
            syncs++; var transport = new FinTsSimulatedTransport();
            transport.EnqueueResponse(Bytes(vector.GetProperty("syncResponse"), "wireBase64"));
            transport.EnqueueResponse(Bytes(vector.GetProperty("closingResponse"), "wireBase64")); return transport;
        }
    }
    private static byte[] Bytes(JsonElement element, string name) => Convert.FromBase64String(element.GetProperty(name).GetString()!);
}
