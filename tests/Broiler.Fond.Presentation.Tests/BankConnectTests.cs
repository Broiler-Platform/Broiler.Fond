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

internal static class BankConnectTests
{
    internal static async Task RunAsync(Action<bool, string> check)
    {
        using var fixture = JsonDocument.Parse(Assembly.GetExecutingAssembly().GetManifestResourceStream("Synchronization.bank-responses.json")!);
        var vector = fixture.RootElement.GetProperty("vectors")[0];
        var input = vector.GetProperty("syncCandidate");
        var origin = FinTsUnsignedInitializationRequest.Parse(FinTsMessageFrame.Parse(Bytes(input, "originRequestBase64")));
        var response = FinTsResponse.Parse(FinTsMessageFrame.Parse(Bytes(input, "procedureResponseBase64")));
        var selection = new BankProcedureSelection(new(origin, FinTsTanParameterSet.Parse(FinTsParameterSet.Parse(response)),
            FinTsPermittedProcedureSet.Parse(response)), new(2, 900, 7));
        string directory = Path.Combine(Path.GetTempPath(), "BroilerFond_Connect_" + Guid.NewGuid().ToString("N"));
        int dispatch = 0, prompts = 0;
        var discoveryTransports = new List<FinTsSimulatedTransport>();
        bool acceptReview = false, slowDiscovery = false;
        var discoveryStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        bool slow = true, failClose = false;
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var pins = new List<FinTsSessionCredential>();
        var alerts = new List<string>();
        try
        {
            using var session = new HostSession(BankProductConfiguration.FromSettings("ASSIGNED-APP-ID", "2.1"), null, null, _ => Transport(), _ => new FinTsSimulatedTransport(), discoveryTransport: _ => DiscoveryTransport());
            session.CreateNewProfile(directory, "ConnectTests123!", "Connect");
            ulong id = session.AddInstitutionAndConnection("Bank", "PUBLIC-BANK", "https://bank.example/fints", "PUBLIC-USER", "PUBLIC-CUSTOMER").ConnectionId;
            var dialogs = new BroilerDialogService
            {
                HeadlessAlertHandler = (_, text) => { alerts.Add(text); return Task.CompletedTask; },
                HeadlessBankDiscoveryReviewHandler = (review, _) =>
                {
                    check(pins.All(p => p.GetSnapshot().State == FinTsCredentialState.Disposed), "Desktop discovery disposes the PIN before account review.");
                    return Task.FromResult<IReadOnlyCollection<int>?>(acceptReview ? review.Accounts.Where(a => a.CanConfirm).Select(a => a.Index).ToArray() : null);
                },
                HeadlessBankCredentialHandler = (_, _) =>
                {
                    prompts++;
                    var pin = FinTsSessionCredential.CaptureAndClear("secret-pin"u8.ToArray(), FinTsCredentialKind.Pin, TimeSpan.FromMinutes(1));
                    pins.Add(pin); return Task.FromResult<FinTsSessionCredential?>(pin);
                }
            };
            using var view = new ConnectionsView(session, dialogs);
            await view.DiscoverSelectedAsync();
            check(alerts.Last().Contains("Select a bank") && prompts == 0, "Desktop discovery requires a selected connection.");
            await view.ConnectSelectedAsync();
            check(alerts.Last().Contains("Select a bank") && prompts == 0, "Connect requires an explicit selected connection before PIN entry.");
            view.ConnectionsList.SelectedItemId = id.ToString();
            await view.ConnectSelectedAsync();
            check(alerts.Last().Contains("Confirm the bank endpoint") && dispatch == 0 && prompts == 0,
                "New connection setup requires endpoint confirmation before sign-in.");
            await view.DiscoverSelectedAsync();
            check(alerts.Last().Contains("Use Connect") && prompts == 0 && discoveryTransports.Count == 0, "Discovery blocks an unsynchronized connection before PIN or transport.");
            session.ConfirmConnectionEndpoint(session.GetConnectionSetupContext(id), "https://bank.example/fints");
            view.ConnectionsList.SelectedItemId = id.ToString();
            await view.ConnectSelectedAsync();
            check(alerts.Last().Contains("parameter exchange failed") && prompts == 0 && dispatch == 0,
                "Connect reports a failed anonymous parameter download without requesting PIN or dispatching synchronization.");
            session.SelectBankProcedure(session.GetConnectionSetupContext(id), selection);
            ulong generation = session.Metadata!.Generation;
            var active = view.ConnectSelectedAsync();
            await started.Task.WaitAsync(TimeSpan.FromSeconds(5));
            check(!view.ConnectButton.IsEnabled && view.CancelConnectButton.IsEnabled, "Connect exposes busy state and cancellation during synchronization.");
            var duplicate = await session.OnlineSetup.ConnectAsync(id, dialogs);
            check(duplicate.Outcome == BankConnectOutcome.Busy && dispatch == 1, "Session-owned setup rejects duplicate work.");
            view.CancelConnectButton.Click();
            await active.WaitAsync(TimeSpan.FromSeconds(5));
            check(session.Metadata.Generation == generation && !session.GetConnectionSetupContext(id).Connection.IsSynchronized && pins.All(p => p.GetSnapshot().State == FinTsCredentialState.Disposed),
                "Cancelled first connection keeps unconfigured durable state and disposes PIN ownership.");
            started = new(TaskCreationOptions.RunContinuationsAsynchronously);
            active = view.ConnectSelectedAsync();
            await started.Task.WaitAsync(TimeSpan.FromSeconds(5));
            session.Lock(); session.Unlock(directory, "ConnectTests123!");
            await active.WaitAsync(TimeSpan.FromSeconds(5));
            check(!session.GetConnectionSetupContext(id).Connection.IsSynchronized && pins.All(p => p.GetSnapshot().State == FinTsCredentialState.Disposed),
                "Profile lock cancels first connection and prevents stale synchronization into the reopened profile.");
            session.SelectBankProcedure(session.GetConnectionSetupContext(id), selection);
            view.ConnectionsList.SelectedItemId = id.ToString();
            slow = false; failClose = true;
            await view.ConnectSelectedAsync();
            check(!session.GetConnectionSetupContext(id).Connection.IsSynchronized && session.Metadata.Generation == generation && !alerts.Last().Contains("secret-pin"),
                "A failed closing exchange cannot mark first connection successful or expose backend secret text.");
            failClose = false;
            await view.ConnectSelectedAsync();
            var connected = session.GetConnectionSetupContext(id).Connection;
            check(connected.IsSynchronized && connected.State == "Connected" && !string.IsNullOrEmpty(connected.SystemId) && session.Accounts.Count == 0,
                "The desktop Connect action synchronizes a new connection without test-only store changes or fabricated accounts.");
            check(alerts.Last().Contains("No balances were retrieved") && alerts.Last().Contains("Discover and review") && view.ConnectButton.IsEnabled,
                "Connect reports durable synchronization and the next discovery step without claiming balance success.");
            int before = prompts;
            await view.ConnectSelectedAsync();
            check(prompts == before && alerts.Last().Contains("does not verify a new sign-in"), "Repeated Connect reports cached synchronization rather than inventing a new authentication success.");
            ulong beforeReview = session.Metadata!.Generation;
            await view.DiscoverSelectedAsync();
            check(alerts.Last().Contains("cancelled") && session.Metadata.Generation == beforeReview && session.Accounts.Count == 0,
                "Cancelling desktop discovery review leaves a new connection without fabricated accounts.");
            acceptReview = true;
            view.ConnectionsList.SelectedItemId = id.ToString();
            await view.DiscoverSelectedAsync();
            check(alerts.Last().Contains("No balances were downloaded") && session.Accounts.Count == 1 && session.CurrentSnapshot!.Accounts.Capabilities.Any(c => c.OperationCode == "HKSAL"),
                "Desktop discovery creates an explicitly reviewed bank account and qualified balance capability without a manual account prerequisite: " + alerts.Last());
            check(session.CurrentSnapshot!.Balances.Balances.Count == 0 && discoveryTransports.All(t => t.RecordedTransmissions.Count == 3 &&
                !t.RecordedTransmissions.Any(x => Encoding.Latin1.GetString(x.Payload).Contains("HKSAL:"))),
                "Desktop discovery performs initialization, HKSPA and close without sending a balance request.");
            slowDiscovery = true;
            view.ConnectionsList.SelectedItemId = id.ToString();
            ulong beforeCancel = session.Metadata.Generation;
            var discoveryRunning = view.DiscoverSelectedAsync();
            await discoveryStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));
            check(!view.ConnectButton.IsEnabled && !view.DiscoverAccountsButton.IsEnabled && view.CancelConnectButton.IsEnabled,
                "Desktop discovery shares setup progress and cancellation and disables duplicate setup actions.");
            int beforeBusy = prompts;
            var busy = await session.OnlineSetup.ConnectAsync(id, dialogs);
            check(busy.Outcome == BankConnectOutcome.Busy && prompts == beforeBusy, "Connect cannot overlap desktop discovery or request another PIN.");
            session.OnlineSetup.Cancel();
            await discoveryRunning.WaitAsync(TimeSpan.FromSeconds(5));
            check(alerts.Last().Contains("cancelled") && session.Metadata.Generation == beforeCancel && view.DiscoverAccountsButton.IsEnabled,
                "Cancel Setup cancels desktop discovery without saving and restores action availability.");
            slowDiscovery = false;
            session.Lock(); session.Unlock(directory, "ConnectTests123!");
            check(session.GetConnectionSetupContext(id).Connection.IsSynchronized && pins.All(p => p.GetSnapshot().State == FinTsCredentialState.Disposed),
                "First connection synchronized state survives reopening and all owned credentials are disposed.");
            check(session.Accounts.Count == 1 && session.CurrentSnapshot!.Accounts.Capabilities.Any(c => c.OperationCode == "HKSAL"),
                "Reviewed desktop-discovered identity and balance capability survive profile reopen.");
            view.ConnectionsList.SelectedItemId = id.ToString();
            int beforeMissingSelection = prompts;
            await view.DiscoverSelectedAsync();
            check(prompts == beforeMissingSelection && alerts.Last().Contains("Use Connect"),
                "Discovery after lock explains procedure requalification before asking for a PIN.");
        }
        finally { if (Directory.Exists(directory)) Directory.Delete(directory, true); }

        FinTsSimulatedTransport DiscoveryTransport()
        {
            using var readFixture = JsonDocument.Parse(Assembly.GetExecutingAssembly().GetManifestResourceStream("Discovery.bank-responses.json")!);
            var read = readFixture.RootElement.GetProperty("vectors")[0];
            var transport = new FinTsSimulatedTransport();
            if (slowDiscovery)
            {
                transport.EnqueueHandler(async (_, _, ct) => { discoveryStarted.TrySetResult(); await Task.Delay(Timeout.InfiniteTimeSpan, ct); throw new InvalidOperationException(); });
                discoveryTransports.Add(transport); return transport;
            }
            byte[] initialization = BankDiscoveryTests.ChangeWrappedBody(Bytes(read.GetProperty("initialization"), "responseBase64"), body =>
                body.Replace("++++HKSPA:1'", "++++HKSAL:1+HKSPA:1'", StringComparison.Ordinal)
                    .Replace("HISPAS:10:1:4", "HISALS:10:7:4+1+1+0'HISPAS:11:1:4", StringComparison.Ordinal));
            transport.EnqueueResponse(SystemId(initialization));
            transport.EnqueueResponse(SystemId(Bytes(read.GetProperty("allAccount"), "responseBase64")));
            transport.EnqueueResponse(SystemId(Bytes(read.GetProperty("closing"), "responseBase64")));
            discoveryTransports.Add(transport);
            return transport;
        }
        byte[] SystemId(byte[] wire) => BankParameterTestWire.Edit(wire, raw => raw.Replace("1::PUBLIC-SYSTEM+", "1::PUBLIC-ASSIGNED+", StringComparison.Ordinal));

        FinTsSimulatedTransport Transport()
        {
            dispatch++;
            var transport = new FinTsSimulatedTransport();
            if (slow) transport.EnqueueHandler(async (_, _, ct) => { started.TrySetResult(); await Task.Delay(Timeout.InfiniteTimeSpan, ct); throw new InvalidOperationException("unreachable"); });
            else
            {
                transport.EnqueueResponse(Bytes(vector.GetProperty("syncResponse"), "wireBase64"));
                if (failClose) transport.EnqueueFailure(new FinTsTransportException(FinTsTransportIssue.Timeout, "secret-pin", new Uri("https://bank.example/fints")));
                else transport.EnqueueResponse(Bytes(vector.GetProperty("closingResponse"), "wireBase64"));
            }
            return transport;
        }
    }
    private static byte[] Bytes(JsonElement element, string name) => Convert.FromBase64String(element.GetProperty(name).GetString()!);
}
