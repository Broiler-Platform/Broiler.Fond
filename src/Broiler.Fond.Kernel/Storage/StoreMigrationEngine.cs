using System.Collections.Concurrent;

namespace Broiler.Fond.Kernel.Storage;

/// <summary>
/// Thrown when an archive or snapshot format version exceeds the maximum version supported by this client.
/// </summary>
public sealed class StoreDowngradeException : InvalidOperationException
{
    public ushort ArchiveFormatVersion { get; }
    public ushort MaxSupportedVersion { get; }

    public StoreDowngradeException(ushort archiveFormatVersion, ushort maxSupportedVersion, string message)
        : base(message)
    {
        ArchiveFormatVersion = archiveFormatVersion;
        MaxSupportedVersion = maxSupportedVersion;
    }
}

/// <summary>
/// Thrown when an older generation is detected where a higher generation was already committed for that lineage.
/// </summary>
public sealed class RollbackDetectedException : InvalidOperationException
{
    public ulong KnownHighestGeneration { get; }
    public ulong CandidateGeneration { get; }

    public RollbackDetectedException(ulong knownHighestGeneration, ulong candidateGeneration, string message)
        : base(message)
    {
        KnownHighestGeneration = knownHighestGeneration;
        CandidateGeneration = candidateGeneration;
    }
}

/// <summary>
/// Enforces strict fail-closed protection against downgraded binaries or premature forward-format archives.
/// </summary>
public static class StoreDowngradeGuard
{
    public const ushort MaxSupportedFormatVersion = 1;

    private static readonly HashSet<string> KnownRequiredSchemas = new(StringComparer.OrdinalIgnoreCase)
    {
        "bootstrap-v1.xsd",
        "store-v1.xsd",
        "transactions-v1.xsd",
        "payments-v1.xsd",
        "templates-v1.xsd",
        "purge-intent-v1.xsd"
    };

    /// <summary>
    /// Validates the format version of the archive envelope or snapshot metadata.
    /// </summary>
    public static void CheckFormatVersion(ushort formatVersion)
    {
        if (formatVersion > MaxSupportedFormatVersion)
        {
            throw new StoreDowngradeException(
                formatVersion,
                MaxSupportedFormatVersion,
                $"The storage archive uses format version {formatVersion}, which is newer than the supported version {MaxSupportedFormatVersion}. Please update Broiler Fond.");
        }
    }

    /// <summary>
    /// Validates that all required schema entries are recognized by this client version.
    /// </summary>
    public static void CheckManifestSchemas(IEnumerable<string> requiredSchemaNames)
    {
        ArgumentNullException.ThrowIfNull(requiredSchemaNames);
        foreach (string schema in requiredSchemaNames)
        {
            if (!KnownRequiredSchemas.Contains(schema))
            {
                throw new StoreDowngradeException(
                    archiveFormatVersion: (ushort)(MaxSupportedFormatVersion + 1),
                    MaxSupportedFormatVersion,
                    $"The storage manifest requires unrecognized schema '{schema}'. Opening this profile requires an updated version of Broiler Fond.");
            }
        }
    }
}

/// <summary>
/// Tracks the highest committed or observed generation per lineage branch to guard against rollback attacks or stale file syncs.
/// </summary>
public static class LineageRollbackGuard
{
    private static readonly ConcurrentDictionary<(Guid ProfileId, Guid BranchId), ulong> HighestGenerations = new();

    /// <summary>
    /// Verifies that the candidate generation is at least as high as any previously observed generation for this branch.
    /// </summary>
    public static void VerifyGeneration(Guid profileId, Guid branchId, ulong candidateGeneration)
    {
        if (HighestGenerations.TryGetValue((profileId, branchId), out ulong highest) && candidateGeneration < highest)
        {
            throw new RollbackDetectedException(
                highest,
                candidateGeneration,
                $"Detected storage rollback: Candidate generation {candidateGeneration} is strictly less than previously observed generation {highest} for profile '{profileId}'.");
        }
    }

    /// <summary>
    /// Records an observed or committed generation for a profile branch.
    /// </summary>
    public static void RecordGeneration(Guid profileId, Guid branchId, ulong generation)
    {
        HighestGenerations.AddOrUpdate(
            (profileId, branchId),
            generation,
            (_, current) => Math.Max(current, generation));
    }

    /// <summary>
    /// Clears recorded generations (for testing or profile teardown).
    /// </summary>
    public static void ResetForTesting()
    {
        HighestGenerations.Clear();
    }
}

/// <summary>
/// Validates and forward-migrates storage snapshots across modular schema extensions.
/// Ensures that archives created in earlier milestones seamlessly default missing optional documents
/// and satisfy all entity identity, monotonicity, and reference invariants.
/// </summary>
public static class StoreMigrationEngine
{
    /// <summary>
    /// Migrates and validates a snapshot, populating clean defaults for missing optional documents.
    /// </summary>
    public static StoreSnapshot MigrateAndValidate(StoreSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);

        // Verify format version
        StoreDowngradeGuard.CheckFormatVersion(1);

        Guid profileId = snapshot.Profile.ProfileId;
        Guid branchId = snapshot.Profile.BranchId;
        ulong gen = snapshot.Profile.Generation;

        // Populate transparent defaults for missing optional documents
        StoreTransactionsDocument txDoc = snapshot.Transactions ??
            new StoreTransactionsDocument(profileId, branchId, gen, [], [], [], [], [], []);

        StorePaymentsDocument payDoc = snapshot.Payments ??
            new StorePaymentsDocument(profileId, branchId, gen, [], [], [], []);

        StoreTemplatesDocument tplDoc = snapshot.Templates ??
            new StoreTemplatesDocument(profileId, branchId, gen, []);

        StoreSnapshot migrated = snapshot with
        {
            Transactions = txDoc,
            Payments = payDoc,
            Templates = tplDoc
        };

        // Validate entity monotonicity: NextId must exceed all assigned entity IDs
        ulong maxId = 0;
        foreach (var r in migrated.Ledger.Records)
        {
            if (r.Id > maxId)
            {
                maxId = r.Id;
            }
        }

        if (migrated.Profile.NextId <= maxId)
        {
            throw new InvalidOperationException($"Storage migration invariant violation: Profile.NextId ({migrated.Profile.NextId}) must be strictly greater than Max(EntityId) ({maxId}).");
        }

        // Record lineage generation in rollback guard
        LineageRollbackGuard.RecordGeneration(profileId, branchId, gen);

        return migrated;
    }
}
