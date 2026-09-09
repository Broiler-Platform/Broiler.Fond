using System.Collections.ObjectModel;

namespace Broiler.Fond.Kernel.Domain.Identifiers;

public enum IdentityIssueCode
{
    InvalidNextId,
    InvalidEntityId,
    UnknownEntityKind,
    DuplicateEntityId,
    CounterNotAboveAllocatedId,
    InvalidReferenceId,
    UnknownReferenceKind,
    MissingReference,
    ReferenceKindMismatch,
    InvalidRevisionKind,
    MissingPredecessor,
    RevisionKindMismatch,
    RevisionCycle,
}

/// <summary>Structured identity-only diagnostics; no bank metadata or record payload is echoed.</summary>
public sealed record IdentityValidationIssue(IdentityIssueCode Code, LocalEntityId? EntityId);

public sealed class IdentityValidationResult
{
    internal IdentityValidationResult(List<IdentityValidationIssue> issues, bool issueLimitReached)
    {
        Issues = issues.AsReadOnly();
        IssueLimitReached = issueLimitReached;
    }

    public ReadOnlyCollection<IdentityValidationIssue> Issues { get; }
    public bool IssueLimitReached { get; }
    public bool IsValid => Issues.Count == 0;
}

/// <summary>Checks identity invariants without modifying or dropping any source record.</summary>
public static class IdentitySnapshotValidator
{
    public const int MaximumIssues = 100;

    public static IdentityValidationResult Validate(IdentitySnapshot snapshot, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        cancellationToken.ThrowIfCancellationRequested();
        List<IdentityValidationIssue> issues = [];
        Dictionary<LocalEntityId, LocalEntityRecord> records = new(snapshot.Records.Count);
        void Add(IdentityIssueCode code, LocalEntityId? id)
        {
            if (issues.Count < MaximumIssues)
            {
                issues.Add(new(code, id));
            }
        }

        IdentityValidationResult Result() => new(issues, issues.Count == MaximumIssues);
        if (snapshot.NextId == 0)
        {
            Add(IdentityIssueCode.InvalidNextId, null);
        }

        foreach (LocalEntityRecord record in snapshot.Records)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (record.Id.Value == 0)
            {
                Add(IdentityIssueCode.InvalidEntityId, record.Id);
            }

            if (!Enum.IsDefined(record.Kind))
            {
                Add(IdentityIssueCode.UnknownEntityKind, record.Id);
            }

            if (!records.TryAdd(record.Id, record))
            {
                Add(IdentityIssueCode.DuplicateEntityId, record.Id);
            }

            if (record.Id.Value >= snapshot.NextId)
            {
                Add(IdentityIssueCode.CounterNotAboveAllocatedId, record.Id);
            }

            if (issues.Count == MaximumIssues)
            {
                return Result();
            }
        }

        // The dictionary is only a diagnostic index. Duplicate source records
        // remain in the original snapshot and will prevent a writable ledger.
        foreach (LocalEntityRecord record in snapshot.Records)
        {
            cancellationToken.ThrowIfCancellationRequested();
            foreach (LocalEntityReference reference in record.References)
            {
                if (reference.TargetId.Value == 0)
                {
                    Add(IdentityIssueCode.InvalidReferenceId, record.Id);
                }

                if (!Enum.IsDefined(reference.ExpectedKind))
                {
                    Add(IdentityIssueCode.UnknownReferenceKind, record.Id);
                }

                if (!records.TryGetValue(reference.TargetId, out LocalEntityRecord? target))
                {
                    Add(IdentityIssueCode.MissingReference, record.Id);
                }
                else if (target.Kind != reference.ExpectedKind)
                {
                    Add(IdentityIssueCode.ReferenceKindMismatch, record.Id);
                }
            }

            if (record.SupersedesId is LocalEntityId predecessorId)
            {
                if (!IsRevision(record.Kind))
                {
                    Add(IdentityIssueCode.InvalidRevisionKind, record.Id);
                }

                if (!records.TryGetValue(predecessorId, out LocalEntityRecord? predecessor))
                {
                    Add(IdentityIssueCode.MissingPredecessor, record.Id);
                }
                else if (predecessor.Kind != record.Kind)
                {
                    Add(IdentityIssueCode.RevisionKindMismatch, record.Id);
                }
            }

            if (issues.Count == MaximumIssues)
            {
                return Result();
            }
        }

        // Iterative three-color walk: every revision has at most one predecessor.
        // It runs in linear time and cannot overflow the stack on a long history.
        Dictionary<LocalEntityId, byte> colors = [];
        List<LocalEntityId> path = [];
        foreach (LocalEntityRecord start in snapshot.Records)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!IsRevision(start.Kind) || colors.ContainsKey(start.Id))
            {
                continue;
            }

            path.Clear();
            LocalEntityRecord current = start;
            while (true)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (colors.TryGetValue(current.Id, out byte color))
                {
                    if (color == 1)
                    {
                        Add(IdentityIssueCode.RevisionCycle, current.Id);
                    }

                    break;
                }

                colors[current.Id] = 1;
                path.Add(current.Id);
                if (current.SupersedesId is not LocalEntityId previous ||
                    !records.TryGetValue(previous, out LocalEntityRecord? predecessor) ||
                    predecessor.Kind != current.Kind)
                {
                    break;
                }

                current = predecessor;
            }

            foreach (LocalEntityId id in path)
            {
                colors[id] = 2;
            }

            if (issues.Count == MaximumIssues)
            {
                return Result();
            }
        }

        return Result();
    }

    private static bool IsRevision(LocalEntityKind kind) =>
        kind is LocalEntityKind.TransactionRevision or LocalEntityKind.AnnotationRevision;
}
