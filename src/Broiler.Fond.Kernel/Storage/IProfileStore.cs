using Broiler.Fond.Kernel.Domain.Accounts;
using Broiler.Fond.Kernel.Domain.Identifiers;

namespace Broiler.Fond.Kernel.Storage;

/// <summary>
/// Thrown when a profile directory is already locked by an active read-write store instance.
/// </summary>
public sealed class ProfileLockConflictException : IOException
{
    public ProfileLockConflictException(string directoryPath)
        : base($"Profile directory '{directoryPath}' is already locked by another read-write ProfileStore instance.")
    {
        DirectoryPath = directoryPath;
    }

    public ProfileLockConflictException(string directoryPath, Exception innerException)
        : base($"Profile directory '{directoryPath}' is already locked by another read-write ProfileStore instance.", innerException)
    {
        DirectoryPath = directoryPath;
    }

    public string DirectoryPath { get; }
}

/// <summary>
/// Operational state of an opened <see cref="IProfileStore"/>.
/// </summary>
public enum ProfileStoreOpenState
{
    Ready = 1,
    RecoveryRequired = 2,
    Erased = 3,
}

/// <summary>
/// Diagnostic categories emitted during profile directory inspection, startup validation, or retention cleanup.
/// </summary>
public enum ProfileDiagnosticCode
{
    CorruptLatestGeneration = 1,
    FilenameManifestGenerationMismatch,
    LineageProfileIdMismatch,
    ForkedBranchConflict,
    IdentityLedgerValidationFailed,
    IncompletePurgeDetected,
    UnpromotedTemporaryCandidateIgnored,
    RetentionCleanupWarning,
}

/// <summary>
/// Structured diagnostic record describing a storage anomaly without echoing sensitive user payloads.
/// </summary>
public sealed record ProfileDiagnostic(
    ProfileDiagnosticCode Code,
    string FileName,
    ulong? Generation,
    string Message);

/// <summary>
/// Verified prior generation available for explicit local recovery or branched roll-forward repair.
/// </summary>
public sealed record ProfileRecoveryCandidate(
    string FileName,
    ulong Generation,
    Guid ProfileId,
    Guid BranchId,
    DateTimeOffset SavedAt,
    StoreSnapshot Snapshot);

/// <summary>
/// Metrics recorded for a committed profile generation save, including write-amplification verification.
/// </summary>
public sealed record ProfileSaveMetrics(
    ulong Generation,
    long PlaintextXmlBytes,
    long CompressedZipBytes,
    long EncryptedEnvelopeBytes,
    double WriteAmplificationRatio,
    long ElapsedMilliseconds,
    IReadOnlyList<string> RetainedGenerationFiles,
    IReadOnlyList<string> CleanupWarnings);

/// <summary>
/// Deterministic fault-injection points across generation save, retention cleanup, privacy purge,
/// passphrase rotation, and whole-profile erasure boundaries.
/// </summary>
public enum ProfileStoreFaultPoint
{
    None = 0,
    DuringTemporaryGenerationWrite,
    BeforeGenerationPromotion,
    BeforeRetentionCleanup,
    PurgeAfterIntentWritten,
    PurgeAfterReplacementCommitted,
    PurgeDuringSupersededDeletion,
    PurgeBeforePendingMarkerRemoval,
}

/// <summary>
/// Production portable-profile lifecycle seam managing single-writer directory locking,
/// generation commits, bounded seekable authentication, explicit recovery, and privacy purge flows.
/// </summary>
public interface IProfileStore : IDisposable
{
    /// <summary>Gets the container format version (<c>1</c>).</summary>
    int ContainerVersion { get; }

    /// <summary>Gets the canonical profile directory path.</summary>
    string DirectoryPath { get; }

    /// <summary>Gets the current open state of the profile store.</summary>
    ProfileStoreOpenState State { get; }

    /// <summary>Gets a value indicating whether the store is currently read-only.</summary>
    bool IsReadOnly { get; }

    /// <summary>Gets the active profile lineage identifier.</summary>
    Guid ProfileId { get; }

    /// <summary>Gets the active branch identifier.</summary>
    Guid BranchId { get; }

    /// <summary>Gets the active committed generation number, or 0 if in unrecovered state without an active head.</summary>
    ulong CurrentGeneration { get; }

    /// <summary>Gets the active validated store snapshot when <see cref="State"/> is <see cref="ProfileStoreOpenState.Ready"/>.</summary>
    StoreSnapshot? CurrentSnapshot { get; }

    /// <summary>Gets the active single-writer identity ledger when the store is writable and <see cref="ProfileStoreOpenState.Ready"/>.</summary>
    LocalIdentityLedger? IdentityLedger { get; }

    /// <summary>Gets the active volatile master key session when <see cref="State"/> is <see cref="ProfileStoreOpenState.Ready"/>.</summary>
    ProfileMasterKeySession? MasterKeySession { get; }

    /// <summary>Gets structured diagnostics recorded during open or cleanup.</summary>
    IReadOnlyList<ProfileDiagnostic> Diagnostics { get; }

    /// <summary>Gets validated recovery candidates available when <see cref="State"/> is <see cref="ProfileStoreOpenState.RecoveryRequired"/>.</summary>
    IReadOnlyList<ProfileRecoveryCandidate> RecoveryCandidates { get; }

    /// <summary>Gets the metrics from the most recent generation commit in this session.</summary>
    ProfileSaveMetrics? LastSaveMetrics { get; }

    /// <summary>
    /// Commits a new generation atomically, verifies the promoted file, and retains only the current
    /// and immediately preceding validated generation in the same lineage.
    /// </summary>
    ProfileSaveMetrics Save(StoreSnapshot snapshot, DateTimeOffset? savedAt = null);

    /// <summary>
    /// Opens a seekable read-only stream over the active generation file that authenticates each 64 KiB
    /// AES-256-GCM frame before returning plaintext bytes.
    /// </summary>
    SeekableAuthenticatedProfileStream OpenCurrentGenerationAuthenticatedStream();

    /// <summary>
    /// Explicitly recovers from a validated prior generation by creating a new branch lineage
    /// with <c>parentBranchId</c> and <c>recoveryProvenance</c> at <c>maxSeenGeneration + 1</c>.
    /// </summary>
    ProfileSaveMetrics RecoverFromPriorGeneration(
        ulong targetGeneration,
        string passphrase,
        DateTimeOffset? savedAt = null,
        string? recoveryNote = null);

    /// <summary>
    /// Executes an atomic privacy purge that commits a replacement generation and deletes all
    /// superseded generations and temporary candidates before clearing <c>purge.pending</c>.
    /// </summary>
    ProfileSaveMetrics ExecutePrivacyPurge(
        StoreSnapshot purgedSnapshot,
        string passphrase,
        DateTimeOffset? savedAt = null,
        string? note = null);

    /// <summary>
    /// Rotates the profile passphrase with a fresh 32-byte <c>profileSalt</c>, fresh <c>snapshotSalt</c>,
    /// and full purge of all old-passphrase generations.
    /// </summary>
    ProfileSaveMetrics RotatePassphrase(
        string currentPassphrase,
        string newPassphrase,
        int? newKdfIterations = null,
        DateTimeOffset? savedAt = null,
        string? note = null);

    /// <summary>
    /// Erases all profile generations and temporary candidates from the managed directory under
    /// a durable purge marker, removing the marker only after re-enumeration confirms erasure.
    /// </summary>
    void EraseWholeProfile(
        string passphrase,
        DateTimeOffset? createdAt = null,
        string? note = null);

    /// <summary>
    /// Resumes and completes an interrupted privacy purge, passphrase rotation, or whole-profile erasure.
    /// </summary>
    void ResumePendingPurge(string passphrase, string? alternatePassphrase = null);

    /// <summary>
    /// Exports a verified backup copy of the active generation file to an external path.
    /// </summary>
    void ExportBackup(string destinationFilePath);

    /// <summary>
    /// Imports an external backup into the active non-empty profile by deterministically remapping
    /// all imported entity IDs and rewriting cross-references before committing the next generation.
    /// </summary>
    ProfileSaveMetrics ImportAndMerge(
        string backupFilePath,
        string backupPassphrase,
        DateTimeOffset? savedAt = null);

    /// <summary>
    /// Removes a bank connection according to the selected <see cref="ConnectionRemovalDataDisposition"/>
    /// and executes a verified privacy purge of all superseded generations and temporary candidates.
    /// </summary>
    ConnectionRemovalResult RemoveConnection(
        ulong connectionId,
        ConnectionRemovalDataDisposition disposition,
        string passphrase,
        bool removeOrphanedInstitution = true,
        DateTimeOffset? savedAt = null,
        string? note = null);

    /// <summary>
    /// Relinks an account binding to a different target account identity, updating binding references
    /// and optionally executing a privacy purge to prune an orphaned temporary account.
    /// </summary>
    AccountRelinkingResult RelinkAccountBinding(
        ulong bindingId,
        ulong targetAccountId,
        string passphrase,
        bool pruneOrphanedAccount = true,
        DateTimeOffset? savedAt = null,
        string? note = null);
}
