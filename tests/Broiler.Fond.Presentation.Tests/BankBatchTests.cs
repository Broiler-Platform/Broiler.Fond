using System.Collections.Concurrent;
using System.Reflection;
using System.Text.Json;
using Broiler.Fond.Kernel.Domain.Identifiers;
using Broiler.Fond.Kernel.FinTs;
using Broiler.Fond.Kernel.FinTs.Transport;
using Broiler.Fond.Kernel.Storage;
using Broiler.Fond.Presentation.Services;
using Broiler.Fond.Presentation.Sessions;

namespace Broiler.Fond.Presentation.Tests;

internal static class BankBatchTests
{
    internal static async Task RunAsync(Action<bool, string> check)
    {
        using var fixture = JsonDocument.Parse(Assembly.GetExecutingAssembly().GetManifestResourceStream("Discovery.bank-responses.json")!);
        var vectors = fixture.RootElement.GetProperty("vectors"); var single = vectors[2];
        var origin = single.GetProperty("initialization").GetProperty("context");
        var request = FinTsUnsignedInitializationRequest.Parse(FinTsMessageFrame.Parse(Bytes(origin, "originRequestBase64")));
        var response = FinTsResponse.Parse(FinTsMessageFrame.Parse(Bytes(origin, "procedureResponseBase64")));
        var provider = new ProcedureProvider(new(new(request, FinTsTanParameterSet.Parse(FinTsParameterSet.Parse(response)), FinTsPermittedProcedureSet.Parse(response)),
            new(origin.GetProperty("profileVersion").GetInt32(), origin.GetProperty("securityFunction").GetInt32(), origin.GetProperty("tanSegmentVersion").GetInt32())));
        const string firstIban = "DE89370400440532013000";
        string directory = Path.Combine(Path.GetTempPath(), "BroilerFond_BR07_" + Guid.NewGuid().ToString("N"));
        try
        {
            using var session = new HostSession(BankProductConfiguration.FromSettings("ASSIGNED-APP-ID", "2.1"));
            session.CreateNewProfile(directory, "BatchTests123!", "Batch");
            ulong bankA = session.AddInstitutionAndConnection("Bank A", "PUBLIC-BANK", "https://bank.example/fints", "PUBLIC-USER", "PUBLIC-CUSTOMER").ConnectionId;
            session.ConfirmConnectionEndpoint(session.GetConnectionSetupContext(bankA), "https://bank.example/fints");
            var manual = session.AddManualAccount(bankA, "First account", firstIban, "EUR");
            Synchronize(bankA);
            using var initialPin = Pin();
            var dt = new FinTsSimulatedTransport();
            dt.EnqueueResponse(Change(Bytes(single.GetProperty("initialization"), "responseBase64"), firstIban, "PUBLIC-001", "EUR"));
            dt.EnqueueResponse(Bytes(vectors[0].GetProperty("allAccount"), "responseBase64"));
            dt.EnqueueResponse(Bytes(vectors[0].GetProperty("closing"), "responseBase64"));
            var discovery = await session.DiscoverBankAccountsCoreAsync(session.GetConnectionSetupContext(bankA), initialPin, provider.Value.Procedures, provider.Value.Selection, () => dt);
            check(discovery.Review is not null && session.ConfirmBankAccountDiscovery(discovery.Review, [0]).Outcome == BankDiscoveryOutcome.Saved,
                "Batch tests start from BR-05's bank-qualified source evidence.");
            if (discovery.Review is null) return;
            var source = session.CurrentSnapshot!.Accounts.Bindings.Single(b => b.Connector == "fints");
            ulong second = Seed(bankA, "Second account", "DE12500105170648489890", "PUBLIC-002", "EUR");
            ulong disabled = Seed(bankA, "Closed account", "DE44500105175407324931", "PUBLIC-004", "EUR", active: false);
            ulong bankB = session.AddInstitutionAndConnection("Bank B", "PUBLIC-BANK", "https://second.example/fints", "PUBLIC-USER", "PUBLIC-CUSTOMER").ConnectionId;
            session.ConfirmConnectionEndpoint(session.GetConnectionSetupContext(bankB), "https://second.example/fints");
            Synchronize(bankB);
            ulong third = Seed(bankB, "USD account", "FR1420041010050500013M02606", "PUBLIC-003", "USD");
            var pins = new ConcurrentBag<FinTsSessionCredential>();
            var prompts = new ConcurrentDictionary<string, int>();
            var dialogs = new BroilerDialogService
            {
                HeadlessBankCredentialHandler = (r, _) =>
            {
                prompts.AddOrUpdate(r.ConnectionName, 1, (_, n) => n + 1);
                var pin = Pin(); pins.Add(pin); return Task.FromResult<FinTsSessionCredential?>(pin);
            }
            };
            var calls = new ConcurrentBag<(ulong Connection, ulong? Account)>();
            bool failBankB = true;
            using var batch = new BankBatchRefreshService(session, transport: (connection, account) =>
            {
                calls.Add((connection, account));
                if (connection == bankB && failBankB)
                {
                    var timeout = new FinTsSimulatedTransport();
                    timeout.EnqueueFailure(new FinTsTransportException(FinTsTransportIssue.Timeout, "PIN-MARKER", new Uri("https://second.example/fints")));
                    return timeout;
                }
                return Balance(account!.Value);
            });
            var mixed = await batch.RefreshAsync(new(), dialogs, provider);
            check(mixed.Updated == 2 && mixed.Failed == 1 && mixed.Unavailable == 1 && mixed.SavedObservations == 6,
                "Mixed-bank refresh counts only two durable saves, one failed bank, and one unavailable closed account.");
            check(prompts.GetValueOrDefault("Bank A") == 1 && prompts.GetValueOrDefault("Bank B") == 1 && pins.All(p => p.GetSnapshot().State == FinTsCredentialState.Disposed),
                "Multiple accounts share one disposable PIN per connection, disposed after each connection batch.");
            check(calls.Count(c => c.Connection == bankA && c.Account.HasValue) == 2 && calls.All(c => c.Account != disabled),
                "Unavailable/closed accounts never dispatch bank balance requests.");
            check(mixed.CurrencyTotals.Single(t => t.Currency == "EUR").RefreshedBookedTotal == 2m &&
                mixed.CurrencyTotals.Single(t => t.Currency == "USD").RefreshedBookedTotal is null,
                "Currency totals include only newly saved bank evidence and never treat failed-bank balances as zero or convert currencies.");
            check(!JsonSerializer.Serialize(mixed).Contains("PIN-MARKER"), "Batch reports never expose raw bank/credential exception markers.");
            int beforeRetryB = calls.Count(c => c.Connection == bankB);
            var repeated = await batch.RefreshAsync(new(), dialogs, provider);
            check(repeated.Unchanged == 2 && repeated.Unavailable == 2 && repeated.Failed == 0 && calls.Count(c => c.Connection == bankB) == beforeRetryB,
                "A repeated refresh reports unchanged durable successes while retry backoff blocks the failed bank before another sign-in.");
            check(session.CurrentSnapshot!.Accounts.Accounts.Count == 4 && session.CurrentSnapshot.Balances.Balances.Count == 12,
                "Each account commits independently; repeated batch reads allocate observations without duplicating identities.");

            failBankB = false;
            var usd = await batch.RefreshAsync(new([bankB]), dialogs, provider);
            check(usd.Unavailable == 1 && usd.SavedObservations == 0, "Backoff is shared across successive service calls and cannot be bypassed by a selection change.");
            session.GetBatchBackoff().Reset(new(bankB));
            var allGood = await batch.RefreshAsync(new(), dialogs, provider);
            check(allGood.Unchanged == 2 && allGood.Updated == 1 && allGood.CurrencyTotals.Count == 2 &&
                allGood.CurrencyTotals.Single(t => t.Currency == "EUR").RefreshedBookedTotal == 2m &&
                allGood.CurrencyTotals.Single(t => t.Currency == "USD").RefreshedBookedTotal == 1m,
                "Successful bank groups retain separate EUR and USD totals; one previous failure does not prevent later durable success.");

            // Authentication failure on the first account stops later accounts of that connection without re-prompting.
            int rejectedCalls = 0;
            using var rejected = new BankBatchRefreshService(session, transport: (_, _) =>
            {
                rejectedCalls++;
                var t = new FinTsSimulatedTransport();
                t.EnqueueResponse(Change(Bytes(single.GetProperty("initialization"), "responseBase64"), firstIban, "PUBLIC-001", "EUR",
                    body => body.Replace("HIRMG:2:2+0010", "HIRMG:2:2+9942", StringComparison.Ordinal)));
                return t;
            });
            int beforePrompts = prompts.GetValueOrDefault("Bank A");
            var authentication = await rejected.RefreshAsync(new([bankA]), dialogs, provider);
            check(authentication.Failed == 2 && rejectedCalls == 1 && prompts.GetValueOrDefault("Bank A") == beforePrompts + 1 &&
                authentication.Accounts.Where(a => a.Outcome == BankBatchAccountOutcome.Failed).All(a => a.Detail == BankBalanceOutcome.RequiresAuthentication),
                "Rejected credentials stop the connection after one request and one PIN prompt; later accounts do not retry that PIN.");

            using var cancelled = new CancellationTokenSource(); cancelled.Cancel();
            int beforeCancelled = calls.Count;
            var cancellation = await batch.RefreshAsync(new(), dialogs, provider, cancellationToken: cancelled.Token);
            check(cancellation.Cancelled == 4 && calls.Count == beforeCancelled, "A pre-cancelled Sync All reports every selected account without collecting PINs or dispatching requests.");
            var subset = await batch.RefreshAsync(new([bankA], [second, second, 999999]), dialogs, provider);
            check(subset.Unchanged == 1 && subset.Unavailable == 1 && subset.Accounts.Count() == 2,
                "Explicit account selections are deduplicated and missing/unassigned selections remain visible.");

            // A failure to save the second account does not undo the first account's successful commit.
            using var saveFailure = new BankBatchRefreshService(session, transport: (_, account) => Balance(account!.Value),
                beforeSave: (account, _) => { if (account == second) throw new IOException("PIN-MARKER"); });
            var partial = await saveFailure.RefreshAsync(new([bankA]), dialogs, provider);
            check(partial.Unchanged == 1 && partial.Failed == 1 && partial.Unavailable == 1 && partial.SavedObservations == 3 &&
                partial.Connections.Single().Outcome == BankBatchConnectionOutcome.Partial,
                "An account save failure preserves earlier durable successes and yields an explicit partial connection result.");

            // Optional discovery is preparation, followed by real account reads using the same scoped PIN.
            int discoveryCalls = 0, balanceCalls = 0;
            dialogs.HeadlessBankDiscoveryReviewHandler = (_, _) => Task.FromResult<IReadOnlyCollection<int>?>([]);
            using var discoveryBatch = new BankBatchRefreshService(session, transport: (_, account) =>
            {
                if (account.HasValue) { balanceCalls++; return Balance(account.Value); }
                discoveryCalls++;
                var binding = session.CurrentSnapshot!.Accounts.Bindings.Single(b => b.ConnectionId == bankB && b.Connector == "fints");
                var t = new FinTsSimulatedTransport();
                t.EnqueueResponse(Change(Bytes(single.GetProperty("initialization"), "responseBase64"), binding.Iban!, binding.DomesticAccountNumber!, binding.Currency!));
                t.EnqueueResponse(BankDiscoveryTests.ChangeWrappedBody(Bytes(vectors[0].GetProperty("allAccount"), "responseBase64"), b =>
                    b.Replace(firstIban, binding.Iban!, StringComparison.Ordinal).Replace("PUBLIC-001", binding.DomesticAccountNumber!, StringComparison.Ordinal)));
                t.EnqueueResponse(Bytes(vectors[0].GetProperty("closing"), "responseBase64")); return t;
            });
            int promptsBeforeDiscovery = prompts.GetValueOrDefault("Bank B");
            var discoveredBatch = await discoveryBatch.RefreshAsync(new([bankB], DiscoverAccounts: true), dialogs, provider, dialogs);
            check(discoveredBatch.Unchanged == 1 && discoveredBatch.SavedObservations == 3 && discoveryCalls == 1 && balanceCalls == 1 &&
                prompts.GetValueOrDefault("Bank B") == promptsBeforeDiscovery + 1,
                "Discovery/review is followed by a real durable balance read and reuses one connection PIN; discovery success alone is not balance success.");
            dialogs.HeadlessBankDiscoveryReviewHandler = (_, _) => Task.FromResult<IReadOnlyCollection<int>?>(null);
            var discoveryDeclined = await discoveryBatch.RefreshAsync(new([bankB], DiscoverAccounts: true), dialogs, provider, dialogs);
            check(discoveryDeclined.Cancelled == 1 && discoveryDeclined.SavedObservations == 0 && balanceCalls == 1,
                "Cancelling discovery review stops before balances and cannot be counted as a successful refresh.");

            // A second login on A's institution must not occupy a worker while waiting for that institution.
            ulong bankC = 0;
            session.GetStore()!.MutateAndSave(s =>
            {
                var original = s.Connections.Connections.Single(c => c.Id == bankA);
                bankC = s.Profile.NextId;
                return s with
                {
                    Profile = s.Profile with { NextId = bankC + 1 },
                    Connections = s.Connections with { Connections = [.. s.Connections.Connections, original with { Id = bankC }] },
                    Ledger = s.Ledger with { Records = [.. s.Ledger.Records, new StoreLedgerRecord(bankC, "Connection", null, [(original.InstitutionId, "Institution")])] }
                };
            });
            session.ReloadData();
            ulong fourth = Seed(bankC, "Another login account", "NL91ABNA0417164300", "PUBLIC-005", "EUR");
            var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var twoBanks = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            int active = 0, maximum = 0;
            var startedConnections = new ConcurrentBag<ulong>();
            using var parallel = new BankBatchRefreshService(session, maximumConcurrency: 3, transport: (connection, account) =>
            {
                var b = session.CurrentSnapshot!.Accounts.Bindings.Single(b => b.AccountId == account && b.Connector == "fints");
                var t = new FinTsSimulatedTransport();
                t.EnqueueHandler(async (_, _, ct) =>
                {
                    startedConnections.Add(connection);
                    int current = Interlocked.Increment(ref active);
                    int prior;
                    do { prior = maximum; if (prior >= current) break; } while (Interlocked.CompareExchange(ref maximum, current, prior) != prior);
                    if (current == 2) twoBanks.TrySetResult();
                    try { await release.Task.WaitAsync(ct); }
                    finally { Interlocked.Decrement(ref active); }
                    return new FinTsTransportResponse(200, Change(Bytes(single.GetProperty("initialization"), "responseBase64"), b.Iban!, b.DomesticAccountNumber!, b.Currency!), null, TimeSpan.Zero);
                });
                t.EnqueueResponse(Change(Bytes(single.GetProperty("balanceInitial"), "responseBase64"), b.Iban!, b.DomesticAccountNumber!, b.Currency!));
                t.EnqueueResponse(Bytes(single.GetProperty("closing"), "responseBase64")); return t;
            });
            var concurrent = parallel.RefreshAsync(new([bankA, bankC, bankB]), dialogs, provider);
            await twoBanks.Task.WaitAsync(TimeSpan.FromSeconds(5));
            check(startedConnections.Contains(bankA) && startedConnections.Contains(bankB) && !startedConnections.Contains(bankC) && maximum == 2,
                "Two institutions progress concurrently within the shared network limit; another login at the same institution waits without starving its peer bank.");
            var duplicateBatch = await parallel.RefreshAsync(new([bankA]), dialogs, provider);
            check(duplicateBatch.Connections.Single().Outcome == BankBatchConnectionOutcome.AlreadyRunning && duplicateBatch.SavedObservations == 0,
                "A batch reservation prevents duplicate Sync All calls for a connection throughout its account sequence.");
            var singleDuringBatch = await session.RefreshBankAccountBalanceCoreAsync(session.GetBankBalanceContext(manual.AccountId), initialPin,
                provider.Value.Procedures, provider.Value.Selection, () => Balance(manual.AccountId));
            check(singleDuringBatch.Outcome == BankBalanceOutcome.AlreadyRunning, "Single-account refresh cannot bypass an active batch connection lease.");
            release.SetResult();
            var concurrentResult = await concurrent.WaitAsync(TimeSpan.FromSeconds(10));
            check(concurrentResult.Unchanged == 3 && concurrentResult.Updated == 1 && maximum <= 2 && startedConnections.Contains(bankC),
                "Queued same-institution accounts proceed after release and all four eligible accounts commit without exceeding global network concurrency.");

            var pinStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var returnPin = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var batchSigningIn = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var standaloneDialogs = new BroilerDialogService
            {
                HeadlessBankCredentialHandler = async (_, ct) =>
            {
                pinStarted.TrySetResult(); await returnPin.Task.WaitAsync(ct); return Pin();
            }
            };
            var standalone = session.AuthenticateAndRefreshBankAccountBalanceCoreAsync(session.GetBankBalanceContext(fourth), standaloneDialogs,
                provider.Value.Procedures, provider.Value.Selection, () => Balance(fourth));
            await pinStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));
            var waitingBatch = batch.RefreshAsync(new([bankA], [manual.AccountId]), dialogs, provider,
                progress: new InlineProgress(p => { if (p.Stage == BankBatchStage.SigningIn) batchSigningIn.TrySetResult(); }));
            await batchSigningIn.Task.WaitAsync(TimeSpan.FromSeconds(5));
            returnPin.SetResult();
            await Task.WhenAll(standalone, waitingBatch).WaitAsync(TimeSpan.FromSeconds(10));
            check(standalone.Result.IsSuccess && waitingBatch.Result.Unchanged == 1,
                "A standalone PIN prompt releases its interaction gate before waiting on a batch's institution gate, preventing circular waits.");

            session.GetStore()!.MutateAndSave(s => s with
            {
                Connections = s.Connections with
                {
                    Connections = s.Connections.Connections.Select(c =>
                c.Id == bankC ? c with { State = "Disabled" } : c).ToArray()
                }
            });
            session.ReloadData();
            int beforeDisabled = calls.Count;
            var disabledConnection = await batch.RefreshAsync(new([bankC]), dialogs, provider);
            check(disabledConnection.Unavailable == 1 && disabledConnection.SavedObservations == 0 && calls.Count == beforeDisabled,
                "A disabled connection remains visible and cannot dispatch a bank request.");
            provider.Available = false;
            int beforeMissingProcedure = prompts.Values.Sum();
            var noProcedure = await batch.RefreshAsync(new([bankA]), dialogs, provider);
            check(noProcedure.Unsupported == 2 && prompts.Values.Sum() == beforeMissingProcedure,
                "Missing bank procedure provenance blocks eligible accounts before any banking PIN prompt.");
            provider.Available = true;

            var blockedStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            using var disposable = new BankBatchRefreshService(session, transport: (_, _) =>
            {
                var t = new FinTsSimulatedTransport();
                t.EnqueueHandler(async (_, _, ct) => { blockedStarted.TrySetResult(); await Task.Delay(Timeout.InfiniteTimeSpan, ct); throw new InvalidOperationException(); });
                return t;
            });
            var disposableRun = disposable.RefreshAsync(new([bankA]), dialogs, provider);
            await blockedStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));
            disposable.Dispose();
            var disposedResult = await disposableRun.WaitAsync(TimeSpan.FromSeconds(5));
            check(disposedResult.Cancelled == 2 && disposedResult.Unavailable == 1 && disposedResult.SavedObservations == 0,
                "Disposing a batch service cancels active and pending account work without leaving background credential owners.");

            EventHandler failedObserver = (_, _) => throw new InvalidOperationException("PIN-MARKER");
            session.StateChanged += failedObserver;
            BankBatchResult notified;
            try { notified = await batch.RefreshAsync(new([bankA]), dialogs, provider); }
            finally { session.StateChanged -= failedObserver; }
            check(notified.Unchanged == 2 && notified.SavedObservations == 6 && !JsonSerializer.Serialize(notified).Contains("PIN-MARKER"),
                "A failing view observer cannot hide successful balance commits or expose its exception through batch results.");
            var overflow = new BankBatchResult(new[] { new BankBatchConnectionResult(bankA, BankBatchConnectionOutcome.Completed, "Test totals",
                new[] { new BankBatchAccountResult(bankA, 1, BankBatchAccountOutcome.Updated, "Saved", Currency: "EUR", Booked: decimal.MaxValue),
                    new BankBatchAccountResult(bankA, 2, BankBatchAccountOutcome.Updated, "Saved", Currency: "EUR", Booked: 1m) }) }, Array.Empty<BankBatchAccountResult>());
            check(overflow.CurrencyTotals.Single() is { Overflowed: true, RefreshedBookedTotal: null },
                "A currency-total overflow is unavailable instead of rounding, wrapping, or hiding other account results.");

            bool lockedAfterCommit = false;
            EventHandler lockOnCommit = (_, _) => { if (!lockedAfterCommit) { lockedAfterCommit = true; session.Lock(); } };
            session.StateChanged += lockOnCommit;
            BankBatchResult lockedBatch;
            try { lockedBatch = await batch.RefreshAsync(new([bankA]), dialogs, provider); }
            finally { session.StateChanged -= lockOnCommit; }
            check(lockedBatch.Unchanged == 1 && lockedBatch.Cancelled == 1 && lockedBatch.Unavailable == 1 && lockedBatch.SavedObservations == 3,
                "Lock immediately after the first commit preserves that durable success in the report and cancels only remaining eligible accounts.");
            session.Unlock(directory, "BatchTests123!");
            check(session.Accounts.Single(a => a.AccountId == manual.AccountId).BookedAmount == 1m && pins.All(p => p.GetSnapshot().State == FinTsCredentialState.Disposed),
                "Completed observations survive lock/reopen, and every reused connection credential is disposed.");

            void Synchronize(ulong id)
            {
                session.GetStore()!.MutateAndSave(s => s with
                {
                    Connections = s.Connections with
                    {
                        Connections = s.Connections.Connections.Select(c => c.Id == id ? c with
                        { IsSynchronized = true, SystemId = "PUBLIC-SYSTEM", State = "Connected", LastSuccessfulContact = DateTimeOffset.UtcNow } : c).ToArray()
                    }
                });
                session.ReloadData();
            }
            ulong Seed(ulong connection, string name, string iban, string number, string currency, bool active = true)
            {
                ulong account = 0;
                session.GetStore()!.MutateAndSave(s =>
                {
                    var c = s.Connections.Connections.Single(c => c.Id == connection);
                    var i = s.Institutions.Institutions.Single(i => i.Id == c.InstitutionId);
                    using var allocation = BankDiscoveryPlanner.DetachedLedger(s).BeginBatch();
                    account = allocation.Allocate(LocalEntityKind.Account).Value;
                    var incarnation = allocation.Allocate(LocalEntityKind.AccountIncarnation, [new(new(account), LocalEntityKind.Account)]).Value;
                    var binding = allocation.Allocate(LocalEntityKind.AccountBinding, [new(new(incarnation), LocalEntityKind.AccountIncarnation), new(new(connection), LocalEntityKind.Connection)]).Value;
                    var ledger = allocation.Commit();
                    return s with
                    {
                        Profile = s.Profile with { NextId = ledger.NextId },
                        Accounts = s.Accounts with
                        {
                            Accounts = [.. s.Accounts.Accounts, new(account, name, currency)],
                            Incarnations = [.. s.Accounts.Incarnations, new(incarnation, account, active ? "LiveDialogue" : "ClosedReported")],
                            Bindings = [.. s.Accounts.Bindings, source with { Id = binding, AccountId = account, IncarnationId = incarnation, InstitutionId = i.Id,
                                ConnectionId = connection, Endpoint = i.Endpoint, Iban = iban, DomesticAccountNumber = number, Currency = currency, State = active ? "Active" : "Closed" }],
                            Capabilities = active ? [.. s.Accounts.Capabilities, new(connection, account, "HKSAL", 7, "Active", null)] : s.Accounts.Capabilities
                        },
                        Ledger = s.Ledger with
                        {
                            Records = ledger.Records.Select(r => new StoreLedgerRecord(r.Id.Value, r.Kind.ToString(), r.SupersedesId?.Value,
                            r.References.Select(x => (x.TargetId.Value, x.ExpectedKind.ToString())).ToArray())).ToArray()
                        }
                    };
                });
                session.ReloadData(); return account;
            }
            FinTsSimulatedTransport Balance(ulong account)
            {
                var binding = session.CurrentSnapshot!.Accounts.Bindings.Single(b => b.AccountId == account && b.Connector == "fints");
                var t = new FinTsSimulatedTransport();
                t.EnqueueResponse(Change(Bytes(single.GetProperty("initialization"), "responseBase64"), binding.Iban!, binding.DomesticAccountNumber!, binding.Currency!));
                t.EnqueueResponse(Change(Bytes(single.GetProperty("balanceInitial"), "responseBase64"), binding.Iban!, binding.DomesticAccountNumber!, binding.Currency!));
                t.EnqueueResponse(Bytes(single.GetProperty("closing"), "responseBase64"));
                return t;
            }
        }
        finally { if (Directory.Exists(directory)) Directory.Delete(directory, true); }
    }
    private static FinTsSessionCredential Pin() => FinTsSessionCredential.CaptureAndClear("PIN-MARKER"u8.ToArray(), FinTsCredentialKind.Pin, TimeSpan.FromMinutes(2));
    private static byte[] Bytes(JsonElement element, string key) => Convert.FromBase64String(element.GetProperty(key).GetString()!);
    private static byte[] Change(byte[] wire, string iban, string number, string currency, Func<string, string>? change = null) =>
        BankDiscoveryTests.ChangeWrappedBody(wire, body => (change ?? (b => b))(body.Replace("PUBLIC-IBAN", iban, StringComparison.Ordinal)
            .Replace("PUBLIC-001", number, StringComparison.Ordinal).Replace("+EUR+", $"+{currency}+", StringComparison.Ordinal).Replace(":EUR:", $":{currency}:", StringComparison.Ordinal)));
    private sealed class ProcedureProvider(BankProcedureSelection value) : IBankProcedureProvider
    {
        internal bool Available { get; set; } = true;
        internal BankProcedureSelection Value => value;
        public Task<BankProcedureSelection?> GetBankProcedureAsync(ConnectionSetupContext context, CancellationToken cancellationToken = default) => Task.FromResult<BankProcedureSelection?>(Available ? value : null);
    }
    private sealed class InlineProgress(Action<BankBatchProgress> report) : IProgress<BankBatchProgress>
    {
        public void Report(BankBatchProgress value) => report(value);
    }
}
