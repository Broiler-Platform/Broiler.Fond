using Broiler.Fond.Kernel.Domain.Identifiers;

namespace Broiler.Fond.Kernel.Storage;

/// <summary>
/// Categories of divergent lineage fork conflicts between snapshots.
/// </summary>
public enum ForkConflictKind
{
    /// <summary>Both snapshots claim the exact same ProfileId, BranchId, and Generation, but have divergent contents or timestamps.</summary>
    DivergentBranchGeneration = 1,

    /// <summary>Identical local entity IDs within the same profile lineage describe divergent entity kinds or conflicting structures.</summary>
    ConflictingEntityIdentity = 2,

    /// <summary>Multiple transaction revisions claim the exact same predecessor revision on the same transaction.</summary>
    ConflictingRevisionPredecessor = 3,
}

/// <summary>
/// Detailed record of a detected lineage fork anomaly.
/// </summary>
public sealed record ForkConflict(
    ForkConflictKind Kind,
    string EntityType,
    ulong? EntityId,
    string Message);

/// <summary>
/// Result of analyzing two snapshots for lineage fork divergence.
/// </summary>
public sealed class LineageForkReport
{
    public LineageForkReport(IReadOnlyList<ForkConflict> conflicts)
    {
        Conflicts = conflicts ?? [];
    }

    public IReadOnlyList<ForkConflict> Conflicts { get; }
    public bool HasConflicts => Conflicts.Count > 0;
}

/// <summary>
/// Detects divergent lineage forks, conflicting entity revisions, and branch generation anomalies between store snapshots.
/// </summary>
public static class LineageForkDetector
{
    /// <summary>
    /// Evaluates target and imported snapshots for fork conflicts.
    /// </summary>
    public static LineageForkReport Detect(StoreSnapshot targetSnapshot, StoreSnapshot importedSnapshot)
    {
        ArgumentNullException.ThrowIfNull(targetSnapshot);
        ArgumentNullException.ThrowIfNull(importedSnapshot);

        List<ForkConflict> conflicts = [];

        // 1. Same branch & same generation divergence check
        if (targetSnapshot.Profile.ProfileId == importedSnapshot.Profile.ProfileId &&
            targetSnapshot.Profile.BranchId == importedSnapshot.Profile.BranchId)
        {
            if (targetSnapshot.Profile.Generation == importedSnapshot.Profile.Generation)
            {
                if (targetSnapshot.Profile.SavedAt != importedSnapshot.Profile.SavedAt ||
                    targetSnapshot.Profile.NextId != importedSnapshot.Profile.NextId ||
                    targetSnapshot.Ledger.Records.Count != importedSnapshot.Ledger.Records.Count)
                {
                    conflicts.Add(new ForkConflict(
                        ForkConflictKind.DivergentBranchGeneration,
                        "Profile",
                        null,
                        $"Divergent contents detected on the same branch {targetSnapshot.Profile.BranchId} at generation {targetSnapshot.Profile.Generation}."));
                }
            }
        }

        // 2. Conflicting entity identity check within the same profile
        if (targetSnapshot.Profile.ProfileId == importedSnapshot.Profile.ProfileId)
        {
            Dictionary<ulong, StoreLedgerRecord> targetLedger = targetSnapshot.Ledger.Records.ToDictionary(r => r.Id);
            foreach (StoreLedgerRecord importedRec in importedSnapshot.Ledger.Records)
            {
                if (targetLedger.TryGetValue(importedRec.Id, out StoreLedgerRecord? targetRec))
                {
                    if (targetRec.Kind != importedRec.Kind ||
                        targetRec.SupersedesId != importedRec.SupersedesId ||
                        !targetRec.References.SequenceEqual(importedRec.References))
                    {
                        conflicts.Add(new ForkConflict(
                            ForkConflictKind.ConflictingEntityIdentity,
                            importedRec.Kind,
                            importedRec.Id,
                            $"Entity ID {importedRec.Id} has conflicting kind, supersedes, or references between target and imported snapshots."));
                    }
                }
            }
        }

        // 3. Conflicting transaction revision predecessor check
        if (targetSnapshot.Profile.ProfileId == importedSnapshot.Profile.ProfileId)
        {
            var targetSupersedes = targetSnapshot.Ledger.Records
                .Where(r => r.SupersedesId.HasValue)
                .GroupBy(r => (r.Kind, r.SupersedesId!.Value))
                .ToDictionary(g => g.Key, g => g.ToList());

            foreach (var importedRec in importedSnapshot.Ledger.Records)
            {
                if (importedRec.SupersedesId.HasValue &&
                    targetSupersedes.TryGetValue((importedRec.Kind, importedRec.SupersedesId.Value), out var matchingTargetRecs))
                {
                    if (matchingTargetRecs.Any(r => r.Id != importedRec.Id))
                    {
                        conflicts.Add(new ForkConflict(
                            ForkConflictKind.ConflictingRevisionPredecessor,
                            importedRec.Kind,
                            importedRec.Id,
                            $"Multiple revisions of kind '{importedRec.Kind}' claim the same predecessor revision {importedRec.SupersedesId.Value}."));
                    }
                }
            }
        }

        return new LineageForkReport(conflicts);
    }
}
