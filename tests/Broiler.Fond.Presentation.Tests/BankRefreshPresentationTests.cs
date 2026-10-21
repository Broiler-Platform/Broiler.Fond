using System.Reflection;
using System.Text.Json;
using Broiler.Fond.Kernel.FinTs;
using Broiler.Fond.Kernel.FinTs.Transport;
using Broiler.Fond.Presentation.Services;
using Broiler.Fond.Presentation.Sessions;
using Broiler.Fond.Presentation.Views;
using Broiler.UI.Standard;

namespace Broiler.Fond.Presentation.Tests;

internal static class BankRefreshPresentationTests
{
    internal static void Run(Action<bool, string> check)
    {
        using var fixture = JsonDocument.Parse(Assembly.GetExecutingAssembly().GetManifestResourceStream("Discovery.bank-responses.json")!);
        var vectors = fixture.RootElement.GetProperty("vectors"); var single = vectors[2];
        var origin = single.GetProperty("initialization").GetProperty("context");
        var request = FinTsUnsignedInitializationRequest.Parse(FinTsMessageFrame.Parse(Bytes(origin, "originRequestBase64")));
        var source = FinTsResponse.Parse(FinTsMessageFrame.Parse(Bytes(origin, "procedureResponseBase64")));
        var selection = new BankProcedureSelection(new(request, FinTsTanParameterSet.Parse(FinTsParameterSet.Parse(source)), FinTsPermittedProcedureSet.Parse(source)),
            new(origin.GetProperty("profileVersion").GetInt32(), origin.GetProperty("securityFunction").GetInt32(), origin.GetProperty("tanSegmentVersion").GetInt32()));
        const string iban = "DE89370400440532013000";
        string directory = Path.Combine(Path.GetTempPath(), "BroilerFond_BR08_" + Guid.NewGuid().ToString("N"));
        TaskCompletionSource? release = null;
        int factories = 0;
        var alerts = new List<(string Title, string Message)>();
        var pins = new List<FinTsSessionCredential>();
        int owner = Environment.CurrentManagedThreadId;
        bool checkThread = false, wrongThread = false;
        try
        {
            using var session = new HostSession(BankProductConfiguration.FromSettings("ASSIGNED-APP-ID", "2.1"), null,
                s => new BankBatchRefreshService(s, transport: (_, _) => Transport()));
            session.CreateNewProfile(directory, "PresentationTests123!", "UI refresh");
            ulong id = session.AddInstitutionAndConnection("Bank", "PUBLIC-BANK", "https://bank.example/fints", "PUBLIC-USER", "PUBLIC-CUSTOMER").ConnectionId;
            session.ConfirmConnectionEndpoint(session.GetConnectionSetupContext(id), "https://bank.example/fints");
            var account = session.AddManualAccount(id, "Everyday", iban, "EUR");
            session.GetStore()!.MutateAndSave(s => s with
            {
                Connections = s.Connections with
                {
                    Connections = s.Connections.Connections.Select(c => c with
                    { IsSynchronized = true, SystemId = "PUBLIC-SYSTEM", State = "Connected", LastSuccessfulContact = DateTimeOffset.UtcNow }).ToArray()
                }
            });
            session.ReloadData();
            using var pin = Pin();
            var discoveryTransport = new FinTsSimulatedTransport();
            discoveryTransport.EnqueueResponse(Wire(single.GetProperty("initialization")));
            discoveryTransport.EnqueueResponse(Bytes(vectors[0].GetProperty("allAccount"), "responseBase64"));
            discoveryTransport.EnqueueResponse(Bytes(vectors[0].GetProperty("closing"), "responseBase64"));
            var discovered = session.DiscoverBankAccountsCoreAsync(session.GetConnectionSetupContext(id), pin, selection.Procedures, selection.Selection,
                () => discoveryTransport).GetAwaiter().GetResult();
            check(discovered.Review is not null && session.ConfirmBankAccountDiscovery(discovered.Review, [0]).Outcome == BankDiscoveryOutcome.Saved,
                "Refresh UI tests start with reviewed bank evidence and a supported balance capability.");
            var dialogs = new BroilerDialogService
            {
                HeadlessAlertHandler = (title, message) => { if (checkThread && Environment.CurrentManagedThreadId != owner) wrongThread = true; alerts.Add((title, message)); return Task.CompletedTask; },
                HeadlessBankCredentialHandler = (_, _) =>
                {
                    if (checkThread && Environment.CurrentManagedThreadId != owner) wrongThread = true;
                    var credential = Pin(); pins.Add(credential); return Task.FromResult<FinTsSessionCredential?>(credential);
                }
            };
            using var shell = new BroilerMainWindow(session, dialogs);
            shell.SyncAllButton.Click();
            check(factories == 0 && alerts.Last().Message.Contains("Unsupported") && alerts.Last().Message.Contains("bank parameters"),
                "The default desktop provider explains missing procedure provenance before collecting a banking PIN or sending a request.");
            session.SelectBankProcedure(session.GetConnectionSetupContext(id), selection);
            shell.SyncAllButton.Click();
            check(factories == 1 && session.Accounts.Single().BookedAmount == 1m && alerts.Last().Title == "Bank Refresh Completed" && alerts.Last().Message.Contains("Updated: 1"),
                "Sync All now invokes the real batch/read/save workflow and reports a durable update.");
            shell.Accounts.SyncButton.Click();
            check(factories == 2 && alerts.Last().Message.Contains("Unchanged: 1") && !alerts.Last().Message.Contains("not yet connected"),
                "Refresh Balances shares the same real workflow and treats unchanged amounts as successful reads.");
            check(shell.Accounts.FreshnessLabel.Text.Contains("Cached balances") && shell.StatusCurrencyTotalsLabel.Text.StartsWith("Cached totals"),
                "Successful refresh retains truthful cached-age and cached-total wording.");

            using var wake = new AutoResetEvent(false);
            var dispatcher = new StandardQueuedUiDispatcher(() => wake.Set());
            session.IsDiagnosticsEnabled = true;
            var stages = new List<string>();
            session.RefreshPresentation.Changed += (_, _) => { if (checkThread && Environment.CurrentManagedThreadId != owner) wrongThread = true; stages.Add(session.RefreshPresentation.ProgressText); };
            session.AccountListChanged += (_, _) => { if (checkThread && Environment.CurrentManagedThreadId != owner) wrongThread = true; };
            release = new(TaskCreationOptions.RunContinuationsAsynchronously);
            checkThread = true;
            var running = BankRefreshFeedback.ShowAsync(session, dialogs, dispatcher);
            check(!shell.SyncAllButton.IsEnabled && !shell.Accounts.SyncButton.IsEnabled && shell.CancelRefreshButton.IsEnabled && shell.Accounts.CancelRefreshButton.IsEnabled,
                "Both refresh actions disable together and expose cancellation while bank work is active.");
            int startedFactories = factories;
            session.RefreshPresentation.RunAsync(dialogs).GetAwaiter().GetResult();
            check(factories == startedFactories, "The shared presentation controller ignores duplicate refresh entry while work is running.");
            Task.Run(() => release.SetResult()).GetAwaiter().GetResult();
            Pump(running);
            check(!wrongThread && stages.Any(s => s.Contains("Retrieving")) && stages.Any(s => s.Contains("Saving")),
                "Network continuations, profile projections, prompt/alert callbacks and progress return through the toolkit UI dispatcher.");
            check(shell.SyncAllButton.IsEnabled && !shell.CancelRefreshButton.IsEnabled && pins.All(p => p.GetSnapshot().State == FinTsCredentialState.Disposed),
                "Completion restores both refresh actions and disposes every banking PIN owner.");
            using (var diagnostic = JsonDocument.Parse(session.RefreshPresentation.GetSupportPreviewJson()))
                check(diagnostic.RootElement.GetProperty("stages").EnumerateArray().Any(s => s.GetProperty("stage").GetString() == "Saving"),
                    "Opt-in support preview records bounded stage codes for the displayed correlation reference.");
            check(!session.RefreshPresentation.GetSupportPreviewJson().Contains(iban) && !session.RefreshPresentation.GetSupportPreviewJson().Contains("PIN-MARKER") &&
                alerts.Last().Message.Contains(session.RefreshPresentation.CorrelationId!), "Support correlation data contains no raw account or credential payload.");
            session.IsDiagnosticsEnabled = false;
            using (var diagnostic = JsonDocument.Parse(session.RefreshPresentation.GetSupportPreviewJson()))
                check(diagnostic.RootElement.GetProperty("stages").GetArrayLength() == 0, "Detailed stage export is disabled when diagnostic opt-in is off.");

            release = new(TaskCreationOptions.RunContinuationsAsynchronously);
            ulong generation = session.Metadata!.Generation;
            var cancelRun = BankRefreshFeedback.ShowAsync(session, dialogs, dispatcher);
            shell.Accounts.CancelRefreshButton.Click();
            Pump(cancelRun);
            check(alerts.Last().Title == "Bank Refresh Cancelled" && session.Metadata.Generation == generation && session.Accounts.Single().BookedAmount == 1m,
                "Cancellation from either view preserves cached balances and retrieval timestamps and reports cancellation.");
            var malicious = new BankBatchResult(new[] { new BankBatchConnectionResult(id, BankBatchConnectionOutcome.Partial, "PIN-MARKER",
                new[] { new BankBatchAccountResult(id, account.AccountId, BankBatchAccountOutcome.Failed, "PIN-MARKER", BankBalanceOutcome.ReceivedNotSaved) }) }, []);
            string formatted = session.RefreshPresentation.FormatResult(malicious);
            check(formatted.Contains("received but not saved") && !formatted.Contains("PIN-MARKER"),
                "Recoverable persistence feedback is explicit and never echoes arbitrary backend exception text.");
            var mixed = new BankBatchResult(new[] { new BankBatchConnectionResult(id, BankBatchConnectionOutcome.Partial, "",
                new[] { new BankBatchAccountResult(id, account.AccountId, BankBatchAccountOutcome.Updated, ""),
                    new BankBatchAccountResult(id, account.AccountId, BankBatchAccountOutcome.Failed, "", BankBalanceOutcome.ReceivedNotSaved) }) }, []);
            string mixedText = session.RefreshPresentation.FormatResult(mixed);
            check(mixedText.Contains("Updated: 1") && mixedText.Contains("Failed: 1") && mixedText.Contains("received but not saved"),
                "Partial completion preserves separate durable success and recoverable failure counts.");

            checkThread = false;
            var oldSetup = session.GetConnectionSetupContext(id);
            session.Lock();
            check(session.RefreshPresentation.LastResult is null && session.BankProcedures.GetBankProcedureAsync(oldSetup).GetAwaiter().GetResult() is null,
                "Profile lock clears account result details and invalidates volatile procedure selections.");
            dialogs.HeadlessProfileUnlockHandler = _ => Task.FromResult(new ProfileUnlockResult(true, "PresentationTests123!", false));
            release = null;
            shell.SyncAllButton.Click();
            check(session.IsUnlocked && alerts.Last().Title == "Bank Refresh Unavailable", "Unlocking through refresh succeeds without self-cancelling; cleared parameters remain explicitly unavailable.");
            session.SelectBankProcedure(session.GetConnectionSetupContext(id), selection);
            var setup = session.GetConnectionSetupContext(id);
            session.UpdateConnectionSetup(setup, new(true, setup.Institution.DisplayName, setup.Institution.BankCode!, setup.Institution.Endpoint, false, "OTHER-USER", "PUBLIC-CUSTOMER"));
            check(session.BankProcedures.GetBankProcedureAsync(setup).GetAwaiter().GetResult() is null, "Changing bank login invalidates selected procedure provenance.");

            void Pump(Task task)
            {
                var timer = System.Diagnostics.Stopwatch.StartNew();
                while (!task.IsCompleted && timer.Elapsed < TimeSpan.FromSeconds(8)) { dispatcher.Drain(); if (!task.IsCompleted) wake.WaitOne(10); }
                dispatcher.Drain();
                if (!task.IsCompleted) throw new TimeoutException("Headless UI workflow did not complete.");
                task.GetAwaiter().GetResult();
            }
        }
        finally { if (Directory.Exists(directory)) Directory.Delete(directory, true); }

        FinTsSimulatedTransport Transport()
        {
            factories++;
            var t = new FinTsSimulatedTransport(); var pending = release;
            if (pending is not null) t.EnqueueHandler(async (_, _, ct) => { await pending.Task.WaitAsync(ct); return new(200, Wire(single.GetProperty("initialization")), null, TimeSpan.Zero); });
            else t.EnqueueResponse(Wire(single.GetProperty("initialization")));
            t.EnqueueResponse(Wire(single.GetProperty("balanceInitial"))); t.EnqueueResponse(Bytes(single.GetProperty("closing"), "responseBase64")); return t;
        }
        byte[] Wire(JsonElement element) => BankDiscoveryTests.ChangeWrappedBody(Bytes(element, "responseBase64"), b => b.Replace("PUBLIC-IBAN", iban, StringComparison.Ordinal));
    }
    private static FinTsSessionCredential Pin() => FinTsSessionCredential.CaptureAndClear("PIN-MARKER"u8.ToArray(), FinTsCredentialKind.Pin, TimeSpan.FromMinutes(2));
    private static byte[] Bytes(JsonElement element, string key) => Convert.FromBase64String(element.GetProperty(key).GetString()!);
}
