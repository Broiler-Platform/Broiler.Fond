using Broiler.Fond.Kernel.Domain.Connections.Orchestration;
using Broiler.Fond.Kernel.Domain.Identifiers;
using Broiler.Fond.Kernel.Storage;
using Broiler.Fond.Presentation.Services;
using Broiler.Fond.Presentation.Sessions;

namespace Broiler.Fond.Presentation.Tests;

internal static class BankRefreshServiceTests
{
    public static async Task RunAsync(Action<bool, string> check)
    {
        using var fixture = new Fixture();
        HostSession session = fixture.Session;
        ulong id = fixture.ConnectionId;
        var request = new BalanceRefreshRequest([id]);
        var operation = new Operation((_, _, _) => Task.CompletedTask);
        using var service = new BankRefreshService(session, operation);

        BalanceRefreshResult blocked = await service.RefreshAsync(request);
        CheckIssue(blocked, BalanceRefreshOutcome.MissingLogin);
        CheckIssue(blocked, BalanceRefreshOutcome.EndpointUnconfirmed);
        CheckIssue(blocked, BalanceRefreshOutcome.ManualAccount);
        CheckIssue(blocked, BalanceRefreshOutcome.UnsupportedOperation);
        check(blocked.Connections.Single().Accounts.Single().AccountId == fixture.AccountId, "Blocked account remains visible in the result.");
        check(operation.Calls == 0, "Ineligible accounts never invoke online operation.");
        fixture.MakeEligible();
        check(BankRefreshService.ProfileIssues(session.CaptureRefreshState() with { ReadOnly = true }, false)
            .Any(i => i.Code == BalanceRefreshOutcome.ProfileReadOnly), "Read-only profile has a distinct preflight blocker.");
        var unavailable = await session.BankRefresh.RefreshAsync(request);
        check(unavailable.Connections.Single().Outcome == BalanceRefreshOutcome.Unavailable && unavailable.CommittedBalances == 0,
            "Default service explicitly reports unavailable, without fabricated success counts.");

        session.IsSafeModeActive = true;
        CheckIssue(await service.RefreshAsync(request), BalanceRefreshOutcome.SafeMode);
        session.IsSafeModeActive = false;
        CheckIssue(await service.RefreshAsync(new BalanceRefreshRequest([999])), BalanceRefreshOutcome.MissingConnection);
        check(operation.Calls == 0, "Safe Mode and nonexistent connections are rejected before execution.");
        var backoff = new ConnectionBackoffTracker();
        backoff.RecordTransientFailure(new ConnectionId(id), "maintenance", DateTimeOffset.UtcNow, TimeSpan.FromHours(1));
        using (var delayed = new BankRefreshService(session, operation, backoff))
            CheckIssue(await delayed.RefreshAsync(request), BalanceRefreshOutcome.Backoff);
        check(operation.Calls == 0, "Backoff prevents operation dispatch.");

        using (var cancelled = new CancellationTokenSource())
        {
            cancelled.Cancel();
            CheckIssue(await service.RefreshAsync(request, cancellationToken: cancelled.Token), BalanceRefreshOutcome.Cancelled);
        }
        check(operation.Calls == 0, "Pre-cancelled requests do not execute.");

        using (var mixed = new Fixture())
        {
            mixed.MakeEligible();
            ulong manual = mixed.Session.AddManualAccount(mixed.ConnectionId, "Unverified", "DE12500105170648489890", "EUR").AccountId;
            BalanceRefreshContext? captured = null;
            using var mixedService = new BankRefreshService(mixed.Session, new Operation((context, _, _) =>
            {
                captured = context; return Task.CompletedTask;
            }));
            var result = await mixedService.RefreshAsync(new BalanceRefreshRequest());
            check(captured!.EligibleAccountIds.SequenceEqual([mixed.AccountId]) &&
                result.Connections.Single().Accounts.Any(a => a.AccountId == manual && a.Issues.Count > 0),
                "Mixed eligibility dispatches only verified supported account IDs and preserves blocked account reasons.");
        }

        var started = Signal();
        var release = Signal();
        var waiting = new Operation(async (_, _, ct) => { started.TrySetResult(); await release.Task.WaitAsync(ct); });
        using (var deduplicated = new BankRefreshService(session, waiting))
        {
            var stages = new ProgressRecorder();
            Task<BalanceRefreshResult> first = deduplicated.RefreshAsync(new BalanceRefreshRequest([id, id]), stages);
            await started.Task.WaitAsync(TimeSpan.FromSeconds(5));
            var duplicate = await deduplicated.RefreshAsync(request);
            check(duplicate.Connections.Single().Outcome == BalanceRefreshOutcome.AlreadyRunning && waiting.Calls == 1,
                "Repeated IDs and duplicate clicks cannot create concurrent runs for one connection.");
            release.TrySetResult();
            var completed = await first;
            check(completed.Connections.Single().Outcome == BalanceRefreshOutcome.Unavailable && completed.CommittedBalances == 0,
                "Completing a test operation cannot claim durable success in BR-01.");
            check(stages.Stages.SequenceEqual([BalanceRefreshStage.Preflight, BalanceRefreshStage.Waiting,
                BalanceRefreshStage.Executing, BalanceRefreshStage.Completed]), "Progress follows preflight, waiting, execution, and terminal stages.");
        }

        await Invalidate("profile lock/reopen", () => { session.Lock(); session.Unlock(fixture.Directory, Fixture.Passphrase); });
        await Invalidate("endpoint edit", () => fixture.Mutate(s => s with
        {
            Institutions = s.Institutions with { Institutions = s.Institutions.Institutions.Select(i => i with { Endpoint = "https://changed.example/fints" }).ToArray() }
        }));
        fixture.MakeEligible();
        await Invalidate("Safe Mode", () => session.IsSafeModeActive = true);
        session.IsSafeModeActive = false;
        await Invalidate("bank login edit", () => fixture.Mutate(s => s with
        {
            Connections = s.Connections with { Connections = s.Connections.Connections.Select(c => c with { UserId = "changed-user" }).ToArray() }
        }));

        using (var cancellation = new CancellationTokenSource())
        {
            var begun = Signal();
            using var cancellable = new BankRefreshService(session, new Operation(async (_, _, ct) =>
            {
                begun.TrySetResult();
                await Task.Delay(Timeout.InfiniteTimeSpan, ct);
            }));
            var run = cancellable.RefreshAsync(request, cancellationToken: cancellation.Token);
            await begun.Task.WaitAsync(TimeSpan.FromSeconds(5));
            cancellation.Cancel();
            check((await run.WaitAsync(TimeSpan.FromSeconds(5))).Connections.Single().Outcome == BalanceRefreshOutcome.Cancelled,
                "User cancellation produces a terminal cancelled outcome.");
        }

        // A backend that returns after a context change cannot make that stale run valid.
        var lateStarted = Signal();
        var lateRelease = Signal();
        using (var late = new BankRefreshService(session, new Operation(async (_, _, _) =>
        {
            lateStarted.TrySetResult(); await lateRelease.Task;
        })))
        {
            var run = late.RefreshAsync(request);
            await lateStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));
            session.Lock();
            session.Unlock(fixture.Directory, Fixture.Passphrase);
            lateRelease.TrySetResult();
            check((await run).Connections.Single().Outcome == BalanceRefreshOutcome.ContextChanged,
                "Late results are discarded even after reopening the same profile and branch.");
        }

        using (var failing = new BankRefreshService(session, new Operation((_, _, _) => throw new IOException("PIN-SECRET bank payload"))))
        {
            var result = await failing.RefreshAsync(request);
            check(result.Connections.Single().Outcome == BalanceRefreshOutcome.Failed &&
                !string.Join(" ", result.Connections.Single().Issues).Contains("PIN-SECRET"),
                "Unexpected operation failures have a safe terminal result without raw exception text.");
        }

        await TestConcurrency();
        await TestQueuedCancellation();
        await Invalidate("profile switch", () => session.CreateNewProfile(Path.Combine(fixture.Directory, "other-profile"), Fixture.Passphrase, "Other"));
        session.Unlock(fixture.Directory, Fixture.Passphrase);
        await Invalidate("connection removal", () => session.RemoveConnection(id));
        using (var disposalFixture = new Fixture())
        {
            disposalFixture.MakeEligible();
            var begun = Signal();
            using var disposing = new BankRefreshService(disposalFixture.Session, new Operation(async (_, _, ct) =>
            {
                begun.TrySetResult(); await Task.Delay(Timeout.InfiniteTimeSpan, ct);
            }));
            var run = disposing.RefreshAsync(new BalanceRefreshRequest());
            await begun.Task.WaitAsync(TimeSpan.FromSeconds(5));
            disposalFixture.Session.Dispose();
            check((await run.WaitAsync(TimeSpan.FromSeconds(5))).Connections.Single().Outcome == BalanceRefreshOutcome.ContextChanged,
                "Session disposal cancels active work.");
        }
        service.Dispose();
        CheckIssue(await service.RefreshAsync(request), BalanceRefreshOutcome.Cancelled);

        async Task Invalidate(string reason, Action change)
        {
            var begun = Signal();
            using var invalidated = new BankRefreshService(session, new Operation(async (_, _, ct) =>
            {
                begun.TrySetResult(); await Task.Delay(Timeout.InfiniteTimeSpan, ct);
            }));
            var run = invalidated.RefreshAsync(request);
            await begun.Task.WaitAsync(TimeSpan.FromSeconds(5));
            change();
            var result = await run.WaitAsync(TimeSpan.FromSeconds(5));
            check(result.Connections.Single().Outcome == BalanceRefreshOutcome.ContextChanged && result.CommittedBalances == 0,
                $"{reason} cancels active work and prevents stale results.");
        }

        async Task TestConcurrency()
        {
            using var other = new Fixture();
            ulong second = other.Session.AddInstitutionAndConnection("Second bank", "37040044", "https://bank.example/fints", "user2").ConnectionId;
            other.Session.AddManualAccount(second, "Second account", "DE89370400440532013000", "EUR");
            ulong third = other.Session.AddInstitutionAndConnection("Third bank", "37040044", "https://bank.example/fints", "user3").ConnectionId;
            other.Session.AddManualAccount(third, "Third account", "DE89370400440532013000", "EUR");
            other.MakeEligible();
            var twoStarted = Signal();
            var releaseAll = Signal();
            var promptStarted = Signal();
            var releasePrompt = Signal();
            int calls = 0, running = 0, maximumRunning = 0, prompts = 0, maximumPrompts = 0;
            var work = new Operation(async (_, interaction, ct) =>
            {
                int current = Interlocked.Increment(ref running);
                maximumRunning = Math.Max(maximumRunning, current);
                if (Interlocked.Increment(ref calls) == 2) twoStarted.TrySetResult();
                await interaction.RunAsync(async token =>
                {
                    int count = Interlocked.Increment(ref prompts);
                    maximumPrompts = Math.Max(maximumPrompts, count);
                    promptStarted.TrySetResult();
                    await releasePrompt.Task.WaitAsync(token);
                    Interlocked.Decrement(ref prompts);
                    return true;
                }, ct);
                await releaseAll.Task.WaitAsync(ct);
                Interlocked.Decrement(ref running);
            });
            using var limited = new BankRefreshService(other.Session, work, maximumConcurrency: 2);
            var run = limited.RefreshAsync(new BalanceRefreshRequest());
            await twoStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));
            await promptStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));
            check(calls == 2 && maximumRunning == 2 && maximumPrompts == 1,
                "Connection concurrency is bounded and credential/SCA interactions are serialized.");
            releasePrompt.TrySetResult();
            releaseAll.TrySetResult();
            var result = await run.WaitAsync(TimeSpan.FromSeconds(5));
            check(calls == 3 && result.Connections.Count == 3 && maximumRunning <= 2 && maximumPrompts == 1,
                "Queued connections eventually execute without exceeding connection or prompt limits.");
        }

        async Task TestQueuedCancellation()
        {
            using var other = new Fixture();
            ulong second = other.Session.AddInstitutionAndConnection("Second", "37040044", "https://bank.example/fints", "user").ConnectionId;
            other.Session.AddManualAccount(second, "Second", "DE89370400440532013000", "EUR");
            other.MakeEligible();
            var begun = Signal();
            var finish = Signal();
            var work = new Operation(async (_, _, ct) => { begun.TrySetResult(); await finish.Task.WaitAsync(ct); });
            using var limited = new BankRefreshService(other.Session, work, maximumConcurrency: 1);
            var first = limited.RefreshAsync(new BalanceRefreshRequest([other.ConnectionId]));
            await begun.Task.WaitAsync(TimeSpan.FromSeconds(5));
            var queued = limited.RefreshAsync(new BalanceRefreshRequest([second]));
            other.Session.RemoveConnection(second);
            check((await queued.WaitAsync(TimeSpan.FromSeconds(5))).Connections.Single().Outcome == BalanceRefreshOutcome.ContextChanged && work.Calls == 1,
                "Removing a queued connection cancels it before online operation dispatch.");
            finish.TrySetResult();
            await first;
        }

        void CheckIssue(BalanceRefreshResult result, BalanceRefreshOutcome code) => check(
            result.Issues.Concat(result.Connections.SelectMany(c => c.Issues)).Concat(result.Connections.SelectMany(c => c.Accounts).SelectMany(a => a.Issues))
                .Any(i => i.Code == code && i.Explanation.Length > 0 && i.RecoveryAction.Length > 0),
            $"Refresh exposes actionable {code} outcome.");
    }

    private static TaskCompletionSource Signal() => new(TaskCreationOptions.RunContinuationsAsynchronously);

    private sealed class ProgressRecorder : IProgress<BalanceRefreshProgress>
    {
        public List<BalanceRefreshStage> Stages { get; } = [];
        public void Report(BalanceRefreshProgress value) => Stages.Add(value.Stage);
    }

    private sealed class Operation(Func<BalanceRefreshContext, BalanceRefreshInteraction, CancellationToken, Task> execute) : IBalanceRefreshOperation
    {
        public int Calls;
        public Task ExecuteAsync(BalanceRefreshContext context, BalanceRefreshInteraction interaction, CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref Calls);
            return execute(context, interaction, cancellationToken);
        }
    }

    private sealed class Fixture : IDisposable
    {
        public const string Passphrase = "RefreshCoordinator123!";
        public string Directory { get; } = Path.Combine(Path.GetTempPath(), "BroilerFond_BR01_" + Guid.NewGuid().ToString("N"));
        public HostSession Session { get; } = new(BankProductConfiguration.FromSettings("TEST-APPLICATION-ID", "1.0"));
        public ulong ConnectionId { get; private set; }
        public ulong AccountId { get; private set; }
        public Fixture()
        {
            Session.CreateNewProfile(Directory, Passphrase, "Refresh coordinator tests");
            RestoreConnection();
        }
        public void RestoreConnection()
        {
            ConnectionId = Session.AddInstitutionAndConnection("Bank", "37040044", "https://bank.example/fints").ConnectionId;
            AccountId = Session.AddManualAccount(ConnectionId, "Account", "DE89370400440532013000", "EUR").AccountId;
        }
        public void MakeEligible() => Mutate(s => s with
        {
            Institutions = s.Institutions with { Institutions = s.Institutions.Institutions.Select(i => i with { VerificationState = "Confirmed" }).ToArray() },
            Connections = s.Connections with { Connections = s.Connections.Connections.Select(c => c with { UserId = "bank-user" }).ToArray() },
            Accounts = s.Accounts with
            {
                Bindings = s.Accounts.Bindings.Select(b => b with { State = "Active", Connector = "FinTS" }).ToArray(),
                Capabilities = s.Accounts.Bindings.Select(b => new StoreCapabilityEntry(b.ConnectionId, b.AccountId, "HKSAL", 7, "Active", null)).ToArray()
            }
        });
        public void Mutate(Func<StoreSnapshot, StoreSnapshot> mutate)
        {
            Session.GetStore()!.MutateAndSave(mutate);
            Session.ReloadData();
        }
        public void Dispose()
        {
            Session.Dispose();
            if (System.IO.Directory.Exists(Directory)) System.IO.Directory.Delete(Directory, true);
        }
    }
}
