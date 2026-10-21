using System.Reflection;
using System.Text;
using System.Text.Json;
using Broiler.Fond.Kernel.FinTs;
using Broiler.Fond.Kernel.FinTs.Transport;
using Broiler.Fond.Presentation.Services;
using Broiler.Fond.Presentation.Sessions;
using Broiler.Fond.Presentation.Views;
using Broiler.Fond.TestSupport;

namespace Broiler.Fond.Presentation.Tests;

internal static class BankFirstBalanceTests
{
    internal static async Task RunAsync(Action<bool, string> check)
    {
        using var syncFixture = JsonDocument.Parse(Assembly.GetExecutingAssembly().GetManifestResourceStream("Synchronization.bank-responses.json")!);
        using var readFixture = JsonDocument.Parse(Assembly.GetExecutingAssembly().GetManifestResourceStream("Discovery.bank-responses.json")!);
        var sync = syncFixture.RootElement.GetProperty("vectors")[0];
        var discovery = readFixture.RootElement.GetProperty("vectors")[0];
        var read = readFixture.RootElement.GetProperty("vectors")[2];
        byte[] identified = Bytes(sync.GetProperty("syncCandidate"), "procedureResponseBase64");
        string tan = Encoding.Latin1.GetString(identified).Split('\'').Single(s => s.StartsWith("HITANS:", StringComparison.Ordinal));
        tan = "HITANS:6:7:3" + tan[tan.IndexOf('+')..] + "'";
        string directory = Path.Combine(Path.GetTempPath(), "BroilerFond_FirstBalance_" + Guid.NewGuid().ToString("N"));
        var alerts = new List<string>();
        var credentials = new List<FinTsSessionCredential>();
        var transports = new List<FinTsSimulatedTransport>();
        string mode = "success";
        int downloads = 0, choices = 0;
        bool cancelChoice = false, failSave = false;
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        try
        {
            using var session = new HostSession(BankProductConfiguration.FromSettings("ASSIGNED-APP-ID", "2.1"), null,
                s => new BankBatchRefreshService(s, transport: (_, _) => Balance(), beforeSave: (_, _) => { if (failSave) throw new IOException("SECRET-BACKEND"); }),
                _ => Synchronize(), _ => Parameters(), discoveryTransport: _ => Discover());
            session.CreateNewProfile(directory, "FirstBalanceTests123!", "First Balance");
            ulong id = session.AddInstitutionAndConnection("Bank", "PUBLIC-BANK", "https://bank.example/fints", "PUBLIC-USER", "PUBLIC-CUSTOMER").ConnectionId;
            session.ConfirmConnectionEndpoint(session.GetConnectionSetupContext(id), "https://bank.example/fints");
            session.AddInstitutionAndConnection("Unselected Bank", "OTHER-BANK", "https://other.example/fints", "OTHER-USER", "OTHER-CUSTOMER");
            var dialogs = new BroilerDialogService
            {
                HeadlessAlertHandler = (_, text) => { alerts.Add(text); return Task.CompletedTask; },
                HeadlessBankProcedureChoiceHandler = (_, _) => { choices++; return Task.FromResult<string?>(cancelChoice ? null : "7:900"); },
                HeadlessBankCredentialHandler = (_, _) =>
                {
                    var pin = FinTsSessionCredential.CaptureAndClear("PIN-MARKER"u8.ToArray(), FinTsCredentialKind.Pin, TimeSpan.FromMinutes(2));
                    credentials.Add(pin); return Task.FromResult<FinTsSessionCredential?>(pin);
                },
                HeadlessBankDiscoveryReviewHandler = (review, _) => Task.FromResult<IReadOnlyCollection<int>?>(review.Accounts.Where(a => a.CanConfirm).Select(a => a.Index).ToArray())
            };
            using var view = new ConnectionsView(session, dialogs);
            await view.RefreshSelectedAsync();
            check(alerts.Last().Contains("Select a bank") && credentials.Count == 0, "Selected-bank refresh requires an explicit selection.");
            view.ConnectionsList.SelectedItemId = id.ToString();
            await view.RefreshSelectedAsync();
            check(alerts.Last().Contains("use Connect") && credentials.Count == 0, "First refresh explains unsynchronized setup before PIN entry.");
            await session.RefreshPresentation.RunAsync(dialogs, new(ConnectionIds: [id]));
            check(session.RefreshPresentation.LastResult?.Connections.Single().BlockReason == BankBatchBlockReason.ConnectionSetupRequired &&
                alerts.Last().Contains("Connect to confirm and synchronize") && !alerts.Last().Contains("Add account details"),
                "Accountless batch feedback identifies synchronization as the immediate blocker instead of asking for manual accounts.");
            await view.ConnectSelectedAsync();
            check(session.GetConnectionSetupContext(id).Connection.IsSynchronized && downloads == 2 && choices == 1,
                "First-balance test qualifies and synchronizes through desktop actions without procedure injection or direct store mutation.");
            view.ConnectionsList.SelectedItemId = id.ToString();
            await view.RefreshSelectedAsync();
            check(alerts.Last().Contains("Discover Accounts") && transports.Count == 0, "Synchronized setup without reviewed accounts directs users to discovery before balance requests.");
            await view.DiscoverSelectedAsync();
            view.ConnectionsList.SelectedItemId = id.ToString();
            await view.RefreshSelectedAsync();
            var first = session.CurrentSnapshot!.Balances.Balances.ToArray();
            check(session.RefreshPresentation.LastResult?.Updated == 1 && session.Accounts.Single().BookedAmount == 1m &&
                session.Accounts.Single().RetrievedAtUtc.HasValue && first.Length > 0,
                "Connect, discovery, explicit review and selected-bank refresh produce a first durable balance and retrieval time: " + alerts.Last());
            check(session.RefreshPresentation.LastResult?.Connections is { Count: 1 } && session.RefreshPresentation.LastResult.Connections.Single().ConnectionId == id,
                "Connections refresh scopes the batch to the selected bank, leaving other configured banks untouched.");
            check(transports.Count == 1 && transports[0].RecordedTransmissions.Count == 3 && transports[0].RecordedTransmissions.Any(t => Encoding.Latin1.GetString(t.Payload).Contains("HKSAL:")),
                "The selected setup refresh invokes the actual closed production balance pipeline.");
            session.Lock(); session.Unlock(directory, "FirstBalanceTests123!");
            check(session.CurrentSnapshot!.Balances.Balances.SequenceEqual(first), "The first bank balance and its retrieval timestamp survive profile reopen.");
            view.ConnectionsList.SelectedItemId = id.ToString();
            await session.RefreshPresentation.RunAsync(dialogs, new(ConnectionIds: [id]));
            check(session.RefreshPresentation.LastResult?.Connections.Single().BlockReason == BankBatchBlockReason.ProcedureRequired &&
                alerts.Last().Contains("Refresh Balances to choose and qualify"),
                "Existing batch entry points explain how to requalify an expired procedure through Connections.");
            cancelChoice = true;
            int beforeCancel = credentials.Count;
            await view.RefreshSelectedAsync();
            check(credentials.Count == beforeCancel && transports.Count == 1 && alerts.Last().Contains("cancelled"), "Cancelling post-unlock procedure qualification sends no balance request or banking PIN.");
            cancelChoice = false;
            await view.RefreshSelectedAsync();
            check(session.RefreshPresentation.LastResult?.Unchanged == 1 && choices == 3 && downloads == 4 && transports.Count == 2,
                "Refresh after normal lock/unlock requalifies through the real chooser and accepts unchanged bank amounts as a successful read.");
            var saved = session.CurrentSnapshot!.Balances.Balances.ToArray();
            mode = "bad-close";
            view.ConnectionsList.SelectedItemId = id.ToString();
            await view.RefreshSelectedAsync();
            check(session.RefreshPresentation.LastResult?.Failed == 1 && session.CurrentSnapshot!.Balances.Balances.SequenceEqual(saved),
                "A first-balance read without validated closure cannot replace saved amounts or retrieval timestamps.");
            mode = "success";
            failSave = true;
            view.ConnectionsList.SelectedItemId = id.ToString();
            await view.RefreshSelectedAsync();
            check(session.RefreshPresentation.LastResult?.Accounts.Single().Detail == BankBalanceOutcome.ReceivedNotSaved &&
                alerts.Last().Contains("received but not saved") && !alerts.Last().Contains("SECRET-BACKEND") && session.CurrentSnapshot!.Balances.Balances.SequenceEqual(saved),
                "First-balance desktop feedback distinguishes received-but-unsaved data and preserves saved values and retrieval times.");
            failSave = false;
            mode = "reject";
            view.ConnectionsList.SelectedItemId = id.ToString();
            await view.RefreshSelectedAsync();
            check(session.RefreshPresentation.LastResult?.Accounts.Single().Detail == BankBalanceOutcome.RequiresAuthentication &&
                alerts.Last().Contains("Rejected credentials were not retried") && transports.Last().RecordedTransmissions.Count == 1 && session.CurrentSnapshot!.Balances.Balances.SequenceEqual(saved),
                "Rejected bank authentication is reported distinctly without credential retries or balance changes.");
            mode = "slow";
            view.ConnectionsList.SelectedItemId = id.ToString();
            var running = view.RefreshSelectedAsync();
            await started.Task.WaitAsync(TimeSpan.FromSeconds(5));
            check(!view.ConnectButton.IsEnabled && !view.DiscoverAccountsButton.IsEnabled && !view.RefreshBalancesButton.IsEnabled && view.CancelRefreshButton.IsEnabled,
                "Connections shares refresh progress, disables overlapping setup and exposes refresh cancellation.");
            view.CancelRefreshButton.Click();
            await running.WaitAsync(TimeSpan.FromSeconds(5));
            check(session.RefreshPresentation.LastResult?.Cancelled == 1 && session.CurrentSnapshot!.Balances.Balances.SequenceEqual(saved) && view.RefreshBalancesButton.IsEnabled,
                "Selected-bank refresh cancellation restores actions and retains previous saved balances.");
            check(credentials.All(p => p.GetSnapshot().State == FinTsCredentialState.Disposed), "Every PIN used by the first-balance desktop flow is disposed.");
        }
        finally { if (Directory.Exists(directory)) Directory.Delete(directory, true); }

        FinTsSimulatedTransport Synchronize()
        {
            var t = new FinTsSimulatedTransport(); t.EnqueueResponse(Bytes(sync.GetProperty("syncResponse"), "wireBase64"));
            t.EnqueueResponse(Bytes(sync.GetProperty("closingResponse"), "wireBase64")); return t;
        }
        FinTsSimulatedTransport Parameters()
        {
            downloads++; bool anonymous = false;
            var t = new FinTsSimulatedTransport();
            t.EnqueueHandler((_, wire, _) =>
            {
                anonymous = FinTsUnsignedInitializationRequest.Parse(FinTsMessageFrame.Parse(wire.Span)).Identification.IsAnonymous;
                return ValueTask.FromResult(new FinTsTransportResponse(200, anonymous ? BankParameterTestWire.Initialization(extra: tan) : identified, null, TimeSpan.Zero));
            });
            t.EnqueueHandler((_, _, _) => ValueTask.FromResult(new FinTsTransportResponse(200, BankParameterTestWire.Closing(anonymous ? "BOOTSTRAP" : "SYNTHETIC"), null, TimeSpan.Zero)));
            return t;
        }
        FinTsSimulatedTransport Discover()
        {
            var t = new FinTsSimulatedTransport();
            t.EnqueueResponse(SystemId(BankDiscoveryTests.ChangeWrappedBody(Bytes(discovery.GetProperty("initialization"), "responseBase64"), b => b
                .Replace("++++HKSPA:1'", "++++HKSAL:1+HKSPA:1'", StringComparison.Ordinal)
                .Replace("HISPAS:10:1:4", "HISALS:10:7:4+1+1+0'HISPAS:11:1:4", StringComparison.Ordinal))));
            t.EnqueueResponse(SystemId(Bytes(discovery.GetProperty("allAccount"), "responseBase64")));
            t.EnqueueResponse(SystemId(Bytes(discovery.GetProperty("closing"), "responseBase64"))); return t;
        }
        FinTsSimulatedTransport Balance()
        {
            var t = new FinTsSimulatedTransport(); transports.Add(t);
            byte[] initialization = Wire(read.GetProperty("initialization"));
            if (mode == "slow") t.EnqueueHandler(async (_, _, ct) => { started.TrySetResult(); await Task.Delay(Timeout.InfiniteTimeSpan, ct); throw new InvalidOperationException(); });
            else t.EnqueueResponse(mode == "reject" ? BankDiscoveryTests.ChangeWrappedBody(initialization, b => b.Replace("HIRMG:2:2+0010", "HIRMG:2:2+9942", StringComparison.Ordinal)) : initialization);
            t.EnqueueResponse(Wire(read.GetProperty("balanceInitial")));
            t.EnqueueResponse(mode == "bad-close" ? "invalid-closing-frame"u8.ToArray() : Wire(read.GetProperty("closing"))); return t;
        }
        byte[] Wire(JsonElement e) => SystemId(BankDiscoveryTests.ChangeWrappedBody(Bytes(e, "responseBase64"), b => b.Replace("PUBLIC-IBAN", "DE89370400440532013000", StringComparison.Ordinal)));
        byte[] SystemId(byte[] wire) => BankParameterTestWire.Edit(wire, s => s.Replace("1::PUBLIC-SYSTEM+", "1::PUBLIC-ASSIGNED+", StringComparison.Ordinal));
    }
    private static byte[] Bytes(JsonElement element, string key) => Convert.FromBase64String(element.GetProperty(key).GetString()!);
}
