using Broiler.Fond.Kernel.Domain.Identifiers;

namespace Broiler.Fond.Kernel.Tests;

internal static class IdentityTests
{
    internal static void Run(Action<bool, string> check)
    {
        int checks = 0;
        void Verify(bool condition, string message)
        {
            checks++;
            check(condition, message);
        }

        VerifyAllocation(Verify);
        VerifyStartup(Verify);
        VerifyGraphs(Verify);
        VerifyBoundsAndIsolation(Verify);
        VerifyConcurrentCommit(Verify);
        Console.WriteLine($"Identity allocation and startup validation: {checks} checks completed.");
    }

    private static LocalIdentityLedger Open(IdentitySnapshot snapshot)
    {
        IdentityValidationResult result = LocalIdentityLedger.TryOpen(snapshot, out LocalIdentityLedger? ledger);
        if (!result.IsValid || ledger is null)
        {
            throw new InvalidOperationException("Test setup identity snapshot was invalid.");
        }

        return ledger;
    }

    private static void VerifyAllocation(Action<bool, string> verify)
    {
        LocalIdentityLedger ledger = Open(IdentitySnapshot.Empty());
        IdentitySnapshot before = ledger.Snapshot;
        using (IdentityAllocationBatch batch = ledger.BeginBatch())
        {
            LocalEntityId institution = batch.Allocate(LocalEntityKind.Institution);
            LocalEntityId connection = batch.Allocate(LocalEntityKind.Connection, [new(institution, LocalEntityKind.Institution)]);
            LocalEntityId account = batch.Allocate(LocalEntityKind.Account, [new(connection, LocalEntityKind.Connection)]);
            verify(institution.Value == 1 && connection.Value == 2 && account.Value == 3,
                "Different entity kinds must share one allocation sequence.");
            verify(ReferenceEquals(before, ledger.Snapshot) && before.NextId == 1 && before.Records.Count == 0,
                "Staged IDs and records must not be visible before commit.");
            IdentitySnapshot committed = batch.Commit();
            verify(committed.NextId == 4 && committed.Records.Count == 3 && ReferenceEquals(committed, ledger.Snapshot),
                "Commit must publish records and counter together.");
            verify(before.NextId == 1 && before.Records.Count == 0, "Earlier snapshots must remain immutable.");
            Throws<ObjectDisposedException>(() => batch.Commit(), verify, "A committed batch cannot publish twice.");
            Throws<ObjectDisposedException>(() => batch.Allocate(LocalEntityKind.Account), verify, "A committed batch cannot allocate.");
        }

        using (IdentityAllocationBatch abandoned = ledger.BeginBatch())
        {
            verify(abandoned.Allocate(LocalEntityKind.Account).Value == 4, "Next batch must continue the committed sequence.");
        }

        verify(ledger.Snapshot.NextId == 4 && ledger.Snapshot.Records.Count == 3, "Disposal must abandon provisional allocations.");
        using (IdentityAllocationBatch invalid = ledger.BeginBatch())
        {
            _ = invalid.Allocate(LocalEntityKind.AccountBinding, [new(new(99), LocalEntityKind.Account)]);
            Throws<InvalidIdentitySnapshotException>(() => invalid.Commit(), verify, "Dangling references must prevent publication.");
            verify(ledger.Snapshot.NextId == 4 && ledger.Snapshot.Records.Count == 3, "Invalid commit must leave counter and records unchanged.");
            Throws<ObjectDisposedException>(() => invalid.Allocate(LocalEntityKind.Account), verify, "A failed commit closes the batch.");
        }

        using (IdentityAllocationBatch cancelled = ledger.BeginBatch())
        {
            _ = cancelled.Allocate(LocalEntityKind.Account);
            Throws<OperationCanceledException>(() => cancelled.Commit(new CancellationToken(true)), verify, "Cancelled commit must fail.");
            verify(ledger.Snapshot.NextId == 4, "Cancellation cannot consume a committed ID.");
            Throws<ObjectDisposedException>(() => cancelled.Commit(), verify, "Cancellation closes the batch.");
        }

        using (IdentityAllocationBatch valid = ledger.BeginBatch())
        {
            verify(valid.Allocate(LocalEntityKind.Account).Value == 4, "Unpublished IDs may be reused after abort; committed IDs may not.");
            _ = valid.Commit();
        }

        using (IdentityAllocationBatch empty = ledger.BeginBatch())
        {
            IdentitySnapshot current = ledger.Snapshot;
            verify(ReferenceEquals(current, empty.Commit()), "An empty commit must not create a new snapshot version.");
        }

        LocalIdentityLedger exhausted = Open(new(ulong.MaxValue - 1, []));
        using (IdentityAllocationBatch last = exhausted.BeginBatch())
        {
            verify(last.Allocate(LocalEntityKind.Observation).Value == ulong.MaxValue - 1, "The last representable ID is MaxValue minus one.");
            Throws<IdentityAllocationExhaustedException>(() => last.Allocate(LocalEntityKind.Observation), verify, "Exhaustion cannot wrap or issue MaxValue.");
            verify(last.Commit().NextId == ulong.MaxValue, "The final allocation must remain committable after a refused extra allocation.");
        }

        verify(IdentitySnapshotValidator.Validate(exhausted.Snapshot).IsValid, "An exhausted snapshot remains valid and readable.");
        using IdentityAllocationBatch noMore = exhausted.BeginBatch();
        Throws<IdentityAllocationExhaustedException>(() => noMore.Allocate(LocalEntityKind.Account), verify, "An exhausted ledger cannot restart allocation.");

        LocalIdentityLedger gaps = Open(new(100, [Record(2), Record(80)]));
        using IdentityAllocationBatch gapBatch = gaps.BeginBatch();
        verify(gapBatch.Allocate(LocalEntityKind.Account).Value == 100, "Startup must preserve a persisted high-water counter and gaps.");
    }

    private static void VerifyStartup(Action<bool, string> verify)
    {
        (IdentitySnapshot Snapshot, IdentityIssueCode Code)[] cases =
        [
            (new(0, []), IdentityIssueCode.InvalidNextId),
            (new(2, [Record(0)]), IdentityIssueCode.InvalidEntityId),
            (new(2, [Record(1, (LocalEntityKind)0)]), IdentityIssueCode.UnknownEntityKind),
            (new(2, [Record(1), Record(1, LocalEntityKind.Institution)]), IdentityIssueCode.DuplicateEntityId),
            (new(1, [Record(1)]), IdentityIssueCode.CounterNotAboveAllocatedId),
            (new(ulong.MaxValue, [Record(ulong.MaxValue)]), IdentityIssueCode.CounterNotAboveAllocatedId),
            (new(2, [Record(1, references: [new(new(0), LocalEntityKind.Account)])]), IdentityIssueCode.InvalidReferenceId),
            (new(2, [Record(1, references: [new(new(1), (LocalEntityKind)999)])]), IdentityIssueCode.UnknownReferenceKind),
            (new(2, [Record(1, references: [new(new(20), LocalEntityKind.Account)])]), IdentityIssueCode.MissingReference),
            (new(3, [Record(1), Record(2, references: [new(new(1), LocalEntityKind.Connection)])]), IdentityIssueCode.ReferenceKindMismatch),
            (new(3, [Record(1), Record(2, supersedes: 1)]), IdentityIssueCode.InvalidRevisionKind),
            (new(2, [Record(1, LocalEntityKind.TransactionRevision, supersedes: 99)]), IdentityIssueCode.MissingPredecessor),
            (new(3, [Record(1, LocalEntityKind.AnnotationRevision), Record(2, LocalEntityKind.TransactionRevision, supersedes: 1)]), IdentityIssueCode.RevisionKindMismatch),
        ];
        foreach ((IdentitySnapshot snapshot, IdentityIssueCode code) in cases)
        {
            LocalEntityRecord[] original = snapshot.Records.ToArray();
            IdentityValidationResult result = LocalIdentityLedger.TryOpen(snapshot, out LocalIdentityLedger? ledger);
            verify(!result.IsValid && ledger is null, "Invalid startup data must not obtain a writer.");
            verify(result.Issues.Any(issue => issue.Code == code), $"Startup must report {code}.");
            verify(snapshot.Records.SequenceEqual(original), "Validation must retain all input records, including duplicates.");
        }

        IdentitySnapshot forward = new(3, [Record(1, references: [new(new(2), LocalEntityKind.Account)]), Record(2)]);
        verify(IdentitySnapshotValidator.Validate(forward).IsValid, "Forward references must be resolved against the complete inventory.");
        Throws<OperationCanceledException>(() => IdentitySnapshotValidator.Validate(forward, new CancellationToken(true)), verify,
            "Startup validation must honor cancellation.");
    }

    private static void VerifyGraphs(Action<bool, string> verify)
    {
        IdentitySnapshot chain = new(5,
        [
            Record(4, LocalEntityKind.TransactionRevision, supersedes: 3),
            Record(1, LocalEntityKind.TransactionRevision),
            Record(3, LocalEntityKind.TransactionRevision, supersedes: 2),
            Record(2, LocalEntityKind.TransactionRevision, supersedes: 1),
        ]);
        verify(IdentitySnapshotValidator.Validate(chain).IsValid, "A revision chain must be valid regardless of input order.");
        foreach (IdentitySnapshot cyclic in new IdentitySnapshot[]
        {
            new(2, [Record(1, LocalEntityKind.TransactionRevision, supersedes: 1)]),
            new(4, [Record(3, LocalEntityKind.AnnotationRevision, supersedes: 1), Record(1, LocalEntityKind.AnnotationRevision, supersedes: 2), Record(2, LocalEntityKind.AnnotationRevision, supersedes: 1)]),
        })
        {
            IdentityValidationResult result = LocalIdentityLedger.TryOpen(cyclic, out LocalIdentityLedger? ledger);
            verify(ledger is null && result.Issues.Any(static issue => issue.Code == IdentityIssueCode.RevisionCycle), "Self/multi-record revision cycles must prevent writes.");
        }

        IdentitySnapshot ordinaryCycle = new(3,
        [
            Record(1, references: [new(new(2), LocalEntityKind.Account)]),
            Record(2, references: [new(new(1), LocalEntityKind.Account)]),
        ]);
        verify(IdentitySnapshotValidator.Validate(ordinaryCycle).IsValid, "Ordinary references do not inherit the revision DAG rule.");
        const int count = 100_000;
        IdentitySnapshot deep = new(count + 1,
            Enumerable.Range(1, count).Reverse().Select(index => Record((ulong)index, LocalEntityKind.TransactionRevision,
                supersedes: index == 1 ? null : (ulong)index - 1)));
        verify(IdentitySnapshotValidator.Validate(deep).IsValid, "A 100,000-record reverse-ordered chain must validate without recursion.");
    }

    private static void VerifyBoundsAndIsolation(Action<bool, string> verify)
    {
        List<LocalEntityReference> references = [new(new(1), LocalEntityKind.Account)];
        LocalEntityRecord record = Record(2, references: references);
        references.Clear();
        verify(record.References.Count == 1, "Records must copy caller-owned reference lists.");
        List<LocalEntityRecord> source = [Record(1), record];
        IdentitySnapshot snapshot = new(3, source);
        source.Clear();
        verify(snapshot.Records.Count == 2, "Snapshots must copy caller-owned record lists.");
        Throws<NotSupportedException>(() => ((IList<LocalEntityRecord>)snapshot.Records).Clear(), verify, "Snapshot views must not permit mutation.");
        Throws<NotSupportedException>(() => ((IList<LocalEntityReference>)record.References).Clear(), verify, "Reference views must not permit mutation.");
        Throws<ArgumentException>(() => Record(1, references: Enumerable.Repeat(new LocalEntityReference(new(1), LocalEntityKind.Account), 65)), verify,
            "Reference materialization must be bounded.");
        Throws<ArgumentException>(() => new IdentitySnapshot(2, Enumerable.Repeat(Record(1), IdentitySnapshot.MaximumEntities + 1)), verify,
            "Entity materialization must be bounded.");
        LocalEntityRecord heavilyLinked = Record(1, references: Enumerable.Repeat(new LocalEntityReference(new(1), LocalEntityKind.Account), 64));
        Throws<ArgumentException>(() => new IdentitySnapshot(2, Enumerable.Repeat(heavilyLinked, IdentitySnapshot.MaximumTotalReferences / 64 + 1)), verify,
            "Total reference materialization must be bounded across all records.");
        IdentitySnapshot manyIssues = new(1, Enumerable.Repeat(Record(1), 200));
        IdentityValidationResult report = IdentitySnapshotValidator.Validate(manyIssues);
        verify(!report.IsValid && report.IssueLimitReached && report.Issues.Count == IdentitySnapshotValidator.MaximumIssues,
            "Corruption reports must be bounded and disclose the diagnostic limit.");
        verify(manyIssues.Records.Count == 200, "Limiting diagnostics must not discard source records.");

        LocalIdentityLedger ledger = Open(IdentitySnapshot.Empty());
        using IdentityAllocationBatch batch = ledger.BeginBatch();
        Throws<ArgumentOutOfRangeException>(() => batch.Allocate((LocalEntityKind)999), verify, "Unknown kinds cannot be allocated.");
        Throws<ArgumentException>(() => batch.Allocate(LocalEntityKind.Account,
            Enumerable.Repeat(new LocalEntityReference(new(1), LocalEntityKind.Account), 65)), verify, "Invalid staging must not consume an ID.");
        verify(batch.Allocate(LocalEntityKind.Account).Value == 1, "Rejected allocation input must leave the provisional counter unchanged.");

        LocalIdentityLedger reentrantLedger = Open(IdentitySnapshot.Empty());
        using IdentityAllocationBatch reentrantBatch = reentrantLedger.BeginBatch();
        LocalEntityId outer = reentrantBatch.Allocate(LocalEntityKind.Account, ReentrantReferences());
        verify(outer.Value == 2 && reentrantBatch.Commit().Records.Count == 2,
            "Caller-controlled reference enumeration must not duplicate an ID through reentrancy.");

        IEnumerable<LocalEntityReference> ReentrantReferences()
        {
            LocalEntityId inner = reentrantBatch.Allocate(LocalEntityKind.Connection);
            yield return new(inner, LocalEntityKind.Connection);
        }
    }

    private static void VerifyConcurrentCommit(Action<bool, string> verify)
    {
        LocalIdentityLedger ledger = Open(IdentitySnapshot.Empty());
        using IdentityAllocationBatch first = ledger.BeginBatch();
        using IdentityAllocationBatch second = ledger.BeginBatch();
        _ = first.Allocate(LocalEntityKind.Institution);
        _ = second.Allocate(LocalEntityKind.Connection);
        int winners = 0;
        int stale = 0;
        Parallel.Invoke(() => Commit(first), () => Commit(second));
        verify(winners == 1 && stale == 1, "Two batches from one snapshot must have exactly one commit winner.");
        verify(ledger.Snapshot.NextId == 2 && ledger.Snapshot.Records.Count == 1, "A stale batch cannot overwrite a committed record or counter.");
        using IdentityAllocationBatch next = ledger.BeginBatch();
        verify(next.Allocate(LocalEntityKind.Account).Value == 2, "Retry must allocate from the new committed snapshot.");
        _ = next.Commit();

        void Commit(IdentityAllocationBatch batch)
        {
            try
            {
                _ = batch.Commit();
                Interlocked.Increment(ref winners);
            }
            catch (StaleIdentityBatchException)
            {
                Interlocked.Increment(ref stale);
            }
        }
    }

    private static LocalEntityRecord Record(ulong id, LocalEntityKind kind = LocalEntityKind.Account,
        IEnumerable<LocalEntityReference>? references = null, ulong? supersedes = null) =>
        new(new(id), kind, references, supersedes is ulong value ? new(value) : null);

    private static void Throws<T>(Action action, Action<bool, string> verify, string message) where T : Exception
    {
        try
        {
            action();
            verify(false, message);
        }
        catch (T)
        {
            verify(true, message);
        }
    }
}
