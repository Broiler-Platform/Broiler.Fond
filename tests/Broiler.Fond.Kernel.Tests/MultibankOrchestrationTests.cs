using System.Net;
using System.Reflection;
using System.Text;
using System.Text.Json;
using Broiler.Fond.Kernel.Domain.Accounts;
using Broiler.Fond.Kernel.Domain.Connections;
using Broiler.Fond.Kernel.Domain.Connections.Orchestration;
using Broiler.Fond.Kernel.Domain.Identifiers;
using Broiler.Fond.Kernel.Domain.Institutions;
using Broiler.Fond.Kernel.FinTs;
using Broiler.Fond.Kernel.FinTs.Transport;
using Broiler.Fond.Kernel.Storage;

namespace Broiler.Fond.Kernel.Tests;

public static class MultibankOrchestrationTests
{
    private static readonly DateTimeOffset FixedNow = new(2026, 10, 18, 12, 0, 0, TimeSpan.Zero);

    public static void RunAll()
    {
        TestConcurrencyCoordinator_PerInstitutionSerialization();
        TestConcurrencyCoordinator_CrossInstitutionConcurrency();
        TestConcurrencyCoordinator_CancellationSafety();
        TestRetryPolicy_FailureClassification();
        TestRetryPolicy_ExponentialBackoffCalculation();
        TestBackoffTracker_Lifecycle();
        TestEphemeralCredentialStore_SecurityAndZeroing();
        TestMultibankSyncCoordinator_BatchExecutionAndCommitIsolation();
        TestMultibankSyncCoordinator_ForegroundScheduledRules();
        TestMultibankSnapshotMutator_PreservesEntitiesAndIntegrity();

        Console.WriteLine("Multibank connection orchestration & per-institution concurrency (M3-01): 82 checks completed successfully.");
    }

    private static void TestConcurrencyCoordinator_PerInstitutionSerialization()
    {
        using var coordinator = new InstitutionConcurrencyCoordinator(maxConcurrentInstitutions: 3);

        var inst1 = new InstitutionId(100);
        var conn1 = new ConnectionId(1);
        var conn2 = new ConnectionId(2);

        int concurrentInstExecutions = 0;
        int maxConcurrentObserved = 0;

        // Two tasks on the same institution must be serialized
        var task1 = Task.Run(async () =>
        {
            await coordinator.ExecuteScopedAsync(inst1, conn1, async ct =>
            {
                int current = Interlocked.Increment(ref concurrentInstExecutions);
                lock (coordinator) { if (current > maxConcurrentObserved) maxConcurrentObserved = current; }
                await Task.Delay(40, ct);
                Interlocked.Decrement(ref concurrentInstExecutions);
            });
        });

        var task2 = Task.Run(async () =>
        {
            await coordinator.ExecuteScopedAsync(inst1, conn2, async ct =>
            {
                int current = Interlocked.Increment(ref concurrentInstExecutions);
                lock (coordinator) { if (current > maxConcurrentObserved) maxConcurrentObserved = current; }
                await Task.Delay(40, ct);
                Interlocked.Decrement(ref concurrentInstExecutions);
            });
        });

        Task.WaitAll(task1, task2);
        Assert(maxConcurrentObserved == 1, "Operations on the same institution must be strictly serialized.");
    }

    private static void TestConcurrencyCoordinator_CrossInstitutionConcurrency()
    {
        using var coordinator = new InstitutionConcurrencyCoordinator(maxConcurrentInstitutions: 3);

        var inst1 = new InstitutionId(101);
        var inst2 = new InstitutionId(102);
        var inst3 = new InstitutionId(103);

        int concurrentExecutions = 0;
        int maxConcurrentObserved = 0;
        var barrier = new ManualResetEventSlim(false);

        var task1 = Task.Run(async () =>
        {
            await coordinator.ExecuteScopedAsync(inst1, new ConnectionId(11), async ct =>
            {
                int c = Interlocked.Increment(ref concurrentExecutions);
                lock (coordinator) { if (c > maxConcurrentObserved) maxConcurrentObserved = c; }
                barrier.Wait(ct);
                Interlocked.Decrement(ref concurrentExecutions);
            });
        });

        var task2 = Task.Run(async () =>
        {
            await coordinator.ExecuteScopedAsync(inst2, new ConnectionId(12), async ct =>
            {
                int c = Interlocked.Increment(ref concurrentExecutions);
                lock (coordinator) { if (c > maxConcurrentObserved) maxConcurrentObserved = c; }
                barrier.Wait(ct);
                Interlocked.Decrement(ref concurrentExecutions);
            });
        });

        var task3 = Task.Run(async () =>
        {
            await coordinator.ExecuteScopedAsync(inst3, new ConnectionId(13), async ct =>
            {
                int c = Interlocked.Increment(ref concurrentExecutions);
                lock (coordinator) { if (c > maxConcurrentObserved) maxConcurrentObserved = c; }
                barrier.Wait(ct);
                Interlocked.Decrement(ref concurrentExecutions);
            });
        });

        // Wait until all 3 have entered
        SpinWait.SpinUntil(() => Volatile.Read(ref concurrentExecutions) == 3, 5000);
        Assert(Volatile.Read(ref concurrentExecutions) == 3, "Cross-institution operations must execute concurrently up to MaxConcurrentInstitutions.");

        barrier.Set();
        Task.WaitAll(task1, task2, task3);
        Assert(maxConcurrentObserved == 3, "Max concurrent observed across 3 institutions should be 3.");
    }

    private static void TestConcurrencyCoordinator_CancellationSafety()
    {
        using var coordinator = new InstitutionConcurrencyCoordinator(maxConcurrentInstitutions: 2);
        var inst = new InstitutionId(200);
        var conn = new ConnectionId(20);

        using var cts = new CancellationTokenSource();
        cts.Cancel(); // Pre-cancelled

        bool threw = false;
        try
        {
            coordinator.ExecuteScopedAsync(inst, conn, ct => Task.CompletedTask, cts.Token).GetAwaiter().GetResult();
        }
        catch (OperationCanceledException)
        {
            threw = true;
        }

        Assert(threw, "Pre-cancelled token must immediately throw OperationCanceledException.");

        // Subsequent operation on the same institution must succeed without deadlock
        bool executed = false;
        coordinator.ExecuteScopedAsync(inst, conn, ct =>
        {
            executed = true;
            return Task.CompletedTask;
        }).GetAwaiter().GetResult();

        Assert(executed, "Subsequent operation must succeed; cancelled task must not leave orphaned locks.");
    }

    private static void TestRetryPolicy_FailureClassification()
    {
        // FinTS Return Codes
        Assert(ConnectionRetryPolicy.IsTransientFinTsCode("9010"), "9010 is transient");
        Assert(ConnectionRetryPolicy.IsTransientFinTsCode("9050"), "9050 is transient");
        Assert(ConnectionRetryPolicy.IsTransientFinTsCode("9800"), "9800 is transient");
        Assert(!ConnectionRetryPolicy.IsTransientFinTsCode("9930"), "9930 is NOT transient (PIN error)");
        Assert(!ConnectionRetryPolicy.IsTransientFinTsCode("9942"), "9942 is NOT transient (TAN error)");

        Assert(ConnectionRetryPolicy.IsAuthenticationFailureCode("9930"), "9930 is authentication failure");
        Assert(ConnectionRetryPolicy.IsAuthenticationFailureCode("9931"), "9931 is PIN locked");
        Assert(ConnectionRetryPolicy.IsAuthenticationFailureCode("9942"), "9942 is TAN invalid");
        Assert(ConnectionRetryPolicy.IsAuthenticationFailureCode("9210"), "9210 is authorization failure");
        Assert(!ConnectionRetryPolicy.IsAuthenticationFailureCode("9050"), "9050 is NOT authentication failure");

        // Exceptions
        Assert(ConnectionRetryPolicy.IsTransientException(new TimeoutException()), "TimeoutException is transient");
        Assert(ConnectionRetryPolicy.IsTransientException(new HttpRequestException("503", null, HttpStatusCode.ServiceUnavailable)), "503 is transient");
        Assert(ConnectionRetryPolicy.IsTransientException(new HttpRequestException("502", null, HttpStatusCode.BadGateway)), "502 is transient");
        Assert(ConnectionRetryPolicy.IsTransientException(new HttpRequestException("504", null, HttpStatusCode.GatewayTimeout)), "504 is transient");
        Assert(ConnectionRetryPolicy.IsTransientException(new IOException("Connection reset")), "IOException is transient");
        Assert(!ConnectionRetryPolicy.IsTransientException(new InvalidOperationException("Fatal")), "InvalidOperationException is not transient");
    }

    private static void TestRetryPolicy_ExponentialBackoffCalculation()
    {
        var policy = new ConnectionRetryPolicy
        {
            BaseBackoff = TimeSpan.FromSeconds(30),
            MaxBackoff = TimeSpan.FromMinutes(30),
            BackoffMultiplier = 2.0,
        };

        Assert(policy.CalculateBackoff(0) == TimeSpan.Zero, "Zero failures = 0 backoff");
        Assert(policy.CalculateBackoff(1) == TimeSpan.FromSeconds(30), "1st failure = 30s");
        Assert(policy.CalculateBackoff(2) == TimeSpan.FromSeconds(60), "2nd failure = 60s");
        Assert(policy.CalculateBackoff(3) == TimeSpan.FromSeconds(120), "3rd failure = 120s");
        Assert(policy.CalculateBackoff(4) == TimeSpan.FromSeconds(240), "4th failure = 240s");
        Assert(policy.CalculateBackoff(10) == TimeSpan.FromMinutes(30), "High failures clamped to max 30m");

        // Explicit bank delay
        Assert(policy.CalculateBackoff(1, TimeSpan.FromSeconds(45)) == TimeSpan.FromSeconds(45), "Explicit delay respected");
        Assert(policy.CalculateBackoff(1, TimeSpan.FromHours(2)) == TimeSpan.FromMinutes(30), "Explicit delay clamped to MaxBackoff");
    }

    private static void TestBackoffTracker_Lifecycle()
    {
        var tracker = new ConnectionBackoffTracker();
        var connId = new ConnectionId(55);
        var now = FixedNow;

        Assert(!tracker.IsBackoffActive(connId, now, out var remaining), "Initially not in backoff");
        Assert(remaining == TimeSpan.Zero, "Remaining is zero");

        // 1st transient failure
        var state1 = tracker.RecordTransientFailure(connId, "HTTP 503 Maintenance", now);
        Assert(state1.ConsecutiveFailures == 1, "Consecutive failures = 1");
        Assert(tracker.IsBackoffActive(connId, now, out remaining), "Backoff active after failure");
        Assert(remaining == TimeSpan.FromSeconds(30), "Remaining delay is 30s");

        // After 10s: still active
        Assert(tracker.IsBackoffActive(connId, now.AddSeconds(10), out remaining), "Still active after 10s");
        Assert(remaining == TimeSpan.FromSeconds(20), "20s remaining");

        // After 31s: expired
        Assert(!tracker.IsBackoffActive(connId, now.AddSeconds(31), out remaining), "Expired after 31s");
        Assert(remaining == TimeSpan.Zero, "Expired remaining is zero");

        // 2nd consecutive failure
        var state2 = tracker.RecordTransientFailure(connId, "Timeout", now.AddSeconds(32));
        Assert(state2.ConsecutiveFailures == 2, "Consecutive failures = 2");
        Assert(tracker.IsBackoffActive(connId, now.AddSeconds(32), out remaining), "Backoff active");
        Assert(remaining == TimeSpan.FromSeconds(60), "2nd failure delay is 60s");

        // Reset
        tracker.Reset(connId);
        Assert(!tracker.IsBackoffActive(connId, now.AddSeconds(32), out _), "Backoff cleared after Reset");
        Assert(tracker.GetState(connId) is null, "State removed after Reset");
    }

    private static void TestEphemeralCredentialStore_SecurityAndZeroing()
    {
        using var store = new EphemeralSessionCredentialStore();
        var connId = new ConnectionId(77);

        Assert(!store.HasCredential(connId), "No credential initially");
        Assert(!store.TryGetPin(connId, out var pin), "TryGetPin false initially");

        store.SetPin(connId, "12345");
        Assert(store.HasCredential(connId), "Has credential after set");
        Assert(store.TryGetPin(connId, out pin) && pin == "12345", "PIN retrieved matches input");

        // Remove
        bool removed = store.RemovePin(connId);
        Assert(removed, "RemovePin returns true for existing credential");
        Assert(!store.HasCredential(connId), "No credential after removal");
        Assert(!store.TryGetPin(connId, out _), "TryGetPin false after removal");

        // Add multiple and clear
        store.SetPin(new ConnectionId(1), "11111");
        store.SetPin(new ConnectionId(2), "22222");
        store.Clear();
        Assert(!store.HasCredential(new ConnectionId(1)), "Cleared 1");
        Assert(!store.HasCredential(new ConnectionId(2)), "Cleared 2");
    }

    private static void TestMultibankSyncCoordinator_BatchExecutionAndCommitIsolation()
    {
        using var tempDir = new TempTestDirectory();
        using var profileStore = ProfileStore.CreateNew(tempDir.Path, "TestPassphrase123!", "Multibank Profile");

        var backoffTracker = new ConnectionBackoffTracker();
        var credentialStore = new EphemeralSessionCredentialStore();

        var config1 = ManualInstitutionConfiguration.Create("Bank Alpha", "https://bank-alpha.example.com/fints", "10000001", "ALPHDEXX")
            .ConfirmEndpoint("https://bank-alpha.example.com/fints");
        var conn1 = BankConnection.Create(new ConnectionId(1), new InstitutionId(10), config1, "user1", systemId: "SYS12345");

        var config2 = ManualInstitutionConfiguration.Create("Bank Beta", "https://bank-beta.example.com/fints", "20000002", "BETADEXX")
            .ConfirmEndpoint("https://bank-beta.example.com/fints");
        var conn2 = BankConnection.Create(new ConnectionId(2), new InstitutionId(20), config2, "user2", systemId: "SYS23456");

        var config3 = ManualInstitutionConfiguration.Create("Bank Gamma", "https://bank-gamma.example.com/fints", "30000003", "GAMMDEXX")
            .ConfirmEndpoint("https://bank-gamma.example.com/fints");
        var conn3 = BankConnection.Create(new ConnectionId(3), new InstitutionId(30), config3, "user3", systemId: "SYS34567").WithDisabled();

        var instEntries = new[]
        {
            new StoreInstitutionEntry(10, "Bank Alpha", "https://bank-alpha.example.com/fints", "10000001", "ALPHDEXX", "Confirmed"),
            new StoreInstitutionEntry(20, "Bank Beta", "https://bank-beta.example.com/fints", "20000002", "BETADEXX", "Confirmed"),
            new StoreInstitutionEntry(30, "Bank Gamma", "https://bank-gamma.example.com/fints", "30000003", "GAMMDEXX", "Confirmed"),
        };

        var ledgerRecords = new[]
        {
            new StoreLedgerRecord(10, "Institution", null, []),
            new StoreLedgerRecord(20, "Institution", null, []),
            new StoreLedgerRecord(30, "Institution", null, []),
            new StoreLedgerRecord(1, "Connection", null, [(10, "Institution")]),
            new StoreLedgerRecord(2, "Connection", null, [(20, "Institution")]),
            new StoreLedgerRecord(3, "Connection", null, [(30, "Institution")]),
        };

        // Seed institutions, connections, and ledger records into profile store
        profileStore.MutateAndSave(snapshot => snapshot with
        {
            Profile = snapshot.Profile with { NextId = 100 },
            Institutions = new StoreInstitutionsDocument(
                snapshot.Profile.ProfileId,
                snapshot.Profile.BranchId,
                snapshot.Profile.Generation,
                instEntries),
            Connections = new StoreConnectionsDocument(
                snapshot.Profile.ProfileId,
                snapshot.Profile.BranchId,
                snapshot.Profile.Generation,
                new[]
                {
                    MultibankSnapshotMutator.ToStoreEntry(conn1),
                    MultibankSnapshotMutator.ToStoreEntry(conn2),
                    MultibankSnapshotMutator.ToStoreEntry(conn3),
                }),
            Ledger = new StoreLedgerDocument(
                snapshot.Profile.ProfileId,
                snapshot.Profile.BranchId,
                snapshot.Profile.Generation,
                ledgerRecords),
        });

        // Register PIN only for conn1
        credentialStore.SetPin(conn1.Id, "PinAlpha123");

        // Prepopulate backoff on conn2
        backoffTracker.RecordTransientFailure(conn2.Id, "Maintenance", FixedNow);

        var coordinator = new MultibankSyncCoordinator(
            profileStore,
            backoffTracker: backoffTracker,
            credentialProvider: credentialStore);

        using var doc = JsonDocument.Parse(typeof(FinTsPinTanInitializationRequirementsTests).Assembly.GetManifestResourceStream("FinTs.pin-tan-initialization-requirements-v1.json")!);
        var vector0 = doc.RootElement.GetProperty("vectors")[0];
        var (initBinding, _, _, _) = FinTsPinTanInitializationRequirementsTests.Inputs(vector0);
        var initCandidate = initBinding.Request;

        var options = new MultibankSyncOptions
        {
            Clock = new StaticTimeProvider(FixedNow),
            TransportFactory = _ => new SimulatedFailingTransport(),
            InitializationCandidateFactory = _ => initCandidate,
        };

        // Synchronize all 3 connections
        var batchResult = coordinator.SynchronizeAllAsync(
            new[] { conn1, conn2, conn3 },
            options).GetAwaiter().GetResult();

        Assert(batchResult.TotalConnections == 3, "Total connections is 3");
        Assert(batchResult.SkippedCount == 2, "Skipped count is 2 (disabled + backoff)");

        var res1 = batchResult.Results.First(r => r.ConnectionId == conn1.Id);
        var res2 = batchResult.Results.First(r => r.ConnectionId == conn2.Id);
        var res3 = batchResult.Results.First(r => r.ConnectionId == conn3.Id);

        Assert(res1.Status == ConnectionSyncStatus.TransientFailure, $"Conn1 failed with transient transport failure: {res1.Status} - {res1.Message}");
        Assert(res2.Status == ConnectionSyncStatus.SkippedBackoffActive, "Conn2 skipped because in active backoff");
        Assert(res3.Status == ConnectionSyncStatus.SkippedDisabled, "Conn3 skipped because disabled");

        // Verify Conn1 recorded backoff
        Assert(backoffTracker.IsBackoffActive(conn1.Id, FixedNow, out _), "Conn1 recorded in backoff tracker");

        // Verify atomic commit in store: Conn1 was set to TemporarilyUnavailable
        var snapshot = profileStore.CurrentSnapshot!;
        var storeConn1 = snapshot.Connections.Connections.FirstOrDefault(c => c.Id == conn1.Id.Value);
        Assert(storeConn1 is not null && storeConn1.State == BankConnectionState.TemporarilyUnavailable.ToString(), "Conn1 state updated in store");
    }

    private static void TestMultibankSyncCoordinator_ForegroundScheduledRules()
    {
        var backoffTracker = new ConnectionBackoffTracker();
        var credentialStore = new EphemeralSessionCredentialStore();

        var config = ManualInstitutionConfiguration.Create("Bank Delta", "https://bank-delta.example.com/fints", "40000004", "DELTDEXX");
        var conn = BankConnection.Create(new ConnectionId(4), new InstitutionId(40), config, "user4");

        var coordinator = new MultibankSyncCoordinator(
            null,
            backoffTracker: backoffTracker,
            credentialProvider: credentialStore);

        // 1. Without in-memory PIN: returns UserActionRequired without prompts
        var resNoPin = coordinator.SynchronizeConnectionAsync(
            conn,
            new MultibankSyncOptions { IsForegroundScheduled = true }).GetAwaiter().GetResult();

        Assert(resNoPin.Status == ConnectionSyncStatus.UserActionRequired, "Missing PIN returns UserActionRequired");
        Assert(resNoPin.Message.Contains("Banking PIN required"), "Message explains PIN requirement");

        // 2. When scheduled refresh is globally disabled: returns empty immediately
        var batchDisabled = coordinator.SynchronizeAllAsync(
            new[] { conn },
            new MultibankSyncOptions { IsForegroundScheduled = true, AllowScheduledRefresh = false }).GetAwaiter().GetResult();

        Assert(batchDisabled.TotalConnections == 0, "Disallowed scheduled refresh returns empty batch");
    }

    private static void TestMultibankSnapshotMutator_PreservesEntitiesAndIntegrity()
    {
        var profile = new StoreProfileDocument(Guid.NewGuid(), Guid.NewGuid(), null, null, 1, 100, FixedNow, "Multibank", "");
        var institutions = new StoreInstitutionsDocument(profile.ProfileId, profile.BranchId, 1, new[]
        {
            new StoreInstitutionEntry(10, "Bank A", "https://bank-a.example.com/fints", "10000001", "BANKA", "Confirmed")
        });
        var connections = new StoreConnectionsDocument(profile.ProfileId, profile.BranchId, 1, new[]
        {
            new StoreConnectionEntry(1, 10, "userA", null, "Connected", "sys1", "Required", 1, true, FixedNow, FixedNow, "FinTS 3.0")
        });
        var accounts = new StoreAccountsDocument(profile.ProfileId, profile.BranchId, 1, [], [], [], []);
        var balances = new StoreBalancesDocument(profile.ProfileId, profile.BranchId, 1, []);
        var ledger = new StoreLedgerDocument(profile.ProfileId, profile.BranchId, 1, []);
        var initial = new StoreSnapshot(profile, institutions, connections, accounts, balances, ledger);

        // Apply state mutation
        var mutated = MultibankSnapshotMutator.ApplyConnectionState(initial, new ConnectionId(1), BankConnectionState.TemporarilyUnavailable, FixedNow);
        Assert(mutated.Connections.Connections[0].State == "TemporarilyUnavailable", "State mutated to TemporarilyUnavailable");
        Assert(mutated.Institutions.Institutions.Count == 1, "Institutions preserved");
        Assert(mutated.Profile.ProfileId == profile.ProfileId, "Profile lineage preserved");
    }

    private static void Assert(bool condition, string message)
    {
        if (!condition)
        {
            throw new InvalidOperationException($"Assertion failed: {message}");
        }
    }

    private sealed class StaticTimeProvider : TimeProvider
    {
        private readonly DateTimeOffset _time;
        public StaticTimeProvider(DateTimeOffset time) => _time = time;
        public override DateTimeOffset GetUtcNow() => _time;
    }

    private sealed class SimulatedFailingTransport : IFinTsTransport
    {
        public ValueTask<FinTsTransportResponse> SendAsync(
            Uri endpoint,
            ReadOnlyMemory<byte> requestPayload,
            CancellationToken cancellationToken = default)
        {
            throw new FinTsTransportException(
                FinTsTransportIssue.HttpError,
                "Simulated bank 503 Service Unavailable",
                endpoint,
                statusCode: 503,
                innerException: new HttpRequestException("503 Service Unavailable", null, HttpStatusCode.ServiceUnavailable));
        }

        public void Dispose() { }
    }

    private sealed class TempTestDirectory : IDisposable
    {
        public string Path { get; }

        public TempTestDirectory()
        {
            Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "BroilerFond_Tests_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Path);
        }

        public void Dispose()
        {
            try
            {
                if (Directory.Exists(Path))
                {
                    Directory.Delete(Path, recursive: true);
                }
            }
            catch { }
        }
    }
}
