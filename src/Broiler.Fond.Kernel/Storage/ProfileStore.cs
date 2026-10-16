using System.Collections.Concurrent;
using System.Diagnostics;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using Broiler.Fond.Kernel.Domain.Accounts;
using Broiler.Fond.Kernel.Domain.Identifiers;

namespace Broiler.Fond.Kernel.Storage;

/// <summary>
/// Production directory-managed encrypted profile store implementing ADR 0004, ADR 0005, and ADR 0087.
/// </summary>
public sealed partial class ProfileStore : IProfileStore
{
    public const string LockFileName = "profile.lock";
    public const string PurgePendingFileName = "purge.pending";
    public const string PurgeIntentFileName = "purge.intent.bffond";
    public const double MaxAllowedWriteAmplificationRatio = 1.10;

    private static readonly byte[] PurgePendingMagicBytes = "BFONDPURGE1\n"u8.ToArray();
    private static readonly ConcurrentDictionary<string, byte> ActiveWriteLocks = new(StringComparer.OrdinalIgnoreCase);

    private readonly object _syncRoot = new();
    private readonly string _directoryPath;
    private readonly string _normalizedLockKey;
    private readonly FileStream? _lockStream;
    private readonly bool _openedReadOnly;
    private readonly Action<ProfileStoreFaultPoint>? _faultHook;
    private readonly List<ProfileDiagnostic> _diagnostics = [];
    private readonly List<ProfileRecoveryCandidate> _recoveryCandidates = [];

    private ProfileMasterKeySession? _masterKeySession;
    private ProfileStoreOpenState _state;
    private Guid _profileId;
    private Guid _branchId;
    private ulong _currentGeneration;
    private ulong _maxSeenGeneration;
    private StoreSnapshot? _currentSnapshot;
    private LocalIdentityLedger? _identityLedger;
    private ProfileSaveMetrics? _lastSaveMetrics;
    private bool _disposed;

    [GeneratedRegex(@"^generation-(?<gen>[0-9]{20})\.bffond$", RegexOptions.CultureInvariant)]
    private static partial Regex GenerationFileRegex();

    [GeneratedRegex(@"^profile\.new-[0-9a-f]{32}\.tmp$", RegexOptions.CultureInvariant)]
    private static partial Regex TempCandidateFileRegex();

    private ProfileStore(
        string directoryPath,
        string normalizedLockKey,
        FileStream? lockStream,
        bool openedReadOnly,
        Action<ProfileStoreFaultPoint>? faultHook)
    {
        _directoryPath = directoryPath;
        _normalizedLockKey = normalizedLockKey;
        _lockStream = lockStream;
        _openedReadOnly = openedReadOnly;
        _faultHook = faultHook;
    }

    public int ContainerVersion => 1;

    public string DirectoryPath
    {
        get
        {
            lock (_syncRoot)
            {
                EnsureNotDisposed();
                return _directoryPath;
            }
        }
    }

    public ProfileStoreOpenState State
    {
        get
        {
            lock (_syncRoot)
            {
                EnsureNotDisposed();
                return _state;
            }
        }
    }

    public bool IsReadOnly
    {
        get
        {
            lock (_syncRoot)
            {
                EnsureNotDisposed();
                return _openedReadOnly || _state != ProfileStoreOpenState.Ready;
            }
        }
    }

    public Guid ProfileId
    {
        get
        {
            lock (_syncRoot)
            {
                EnsureNotDisposed();
                return _profileId;
            }
        }
    }

    public Guid BranchId
    {
        get
        {
            lock (_syncRoot)
            {
                EnsureNotDisposed();
                return _branchId;
            }
        }
    }

    public ulong CurrentGeneration
    {
        get
        {
            lock (_syncRoot)
            {
                EnsureNotDisposed();
                return _currentGeneration;
            }
        }
    }

    public StoreSnapshot? CurrentSnapshot
    {
        get
        {
            lock (_syncRoot)
            {
                EnsureNotDisposed();
                return _currentSnapshot;
            }
        }
    }

    public LocalIdentityLedger? IdentityLedger
    {
        get
        {
            lock (_syncRoot)
            {
                EnsureNotDisposed();
                return IsReadOnly ? null : _identityLedger;
            }
        }
    }

    public ProfileMasterKeySession? MasterKeySession
    {
        get
        {
            lock (_syncRoot)
            {
                EnsureNotDisposed();
                return _state == ProfileStoreOpenState.Ready ? _masterKeySession : null;
            }
        }
    }

    public IReadOnlyList<ProfileDiagnostic> Diagnostics
    {
        get
        {
            lock (_syncRoot)
            {
                EnsureNotDisposed();
                return _diagnostics.ToList();
            }
        }
    }

    public IReadOnlyList<ProfileRecoveryCandidate> RecoveryCandidates
    {
        get
        {
            lock (_syncRoot)
            {
                EnsureNotDisposed();
                return _recoveryCandidates.ToList();
            }
        }
    }

    public ProfileSaveMetrics? LastSaveMetrics
    {
        get
        {
            lock (_syncRoot)
            {
                EnsureNotDisposed();
                return _lastSaveMetrics;
            }
        }
    }

    public static string FormatGenerationFileName(ulong generation)
    {
        if (generation == 0)
        {
            throw new ArgumentOutOfRangeException(nameof(generation), "Generation must be >= 1.");
        }

        return string.Create(CultureInfo.InvariantCulture, $"generation-{generation:D20}.bffond");
    }

    public static bool TryParseGenerationFileName(string fileName, out ulong generation)
    {
        generation = 0;
        if (string.IsNullOrEmpty(fileName))
        {
            return false;
        }

        Match m = GenerationFileRegex().Match(fileName);
        if (!m.Success)
        {
            return false;
        }

        string digits = m.Groups["gen"].Value;
        return ulong.TryParse(digits, NumberStyles.None, CultureInfo.InvariantCulture, out generation)
            && generation >= 1
            && string.Equals(generation.ToString("D20", CultureInfo.InvariantCulture), digits, StringComparison.Ordinal);
    }

    public static bool IsTemporaryCandidateFileName(string fileName) =>
        !string.IsNullOrEmpty(fileName) && TempCandidateFileRegex().IsMatch(fileName);

    /// <summary>
    /// Creates a new encrypted profile lineage at generation 1 in an empty or newly created directory.
    /// </summary>
    public static ProfileStore CreateNew(
        string directoryPath,
        string passphrase,
        string label,
        string note = "",
        Guid? profileId = null,
        Guid? branchId = null,
        int kdfIterations = ProfileEnvelopeCodec.DefaultIterations,
        DateTimeOffset? savedAt = null,
        Action<ProfileStoreFaultPoint>? faultHook = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(directoryPath);
        ArgumentNullException.ThrowIfNull(passphrase);
        ArgumentNullException.ThrowIfNull(label);
        ArgumentNullException.ThrowIfNull(note);
        ProfileEnvelopeCodec.EnsureCryptographicHostSupport();

        string fullDir = Path.GetFullPath(directoryPath);
        Directory.CreateDirectory(fullDir);

        (string lockKey, FileStream lockStream) = AcquireWriteLock(fullDir);
        ProfileStore store = new(fullDir, lockKey, lockStream, openedReadOnly: false, faultHook);
        try
        {
            if (File.Exists(Path.Combine(fullDir, PurgePendingFileName)) ||
                File.Exists(Path.Combine(fullDir, PurgeIntentFileName)))
            {
                throw new InvalidOperationException("Cannot create a new profile in a directory with a pending purge marker.");
            }

            foreach (string path in Directory.EnumerateFiles(fullDir))
            {
                string name = Path.GetFileName(path);
                if (!string.Equals(name, LockFileName, StringComparison.OrdinalIgnoreCase))
                {
                    throw new InvalidOperationException($"Cannot create a new profile in non-empty directory containing '{name}'.");
                }
            }

            byte[] profileSalt = new byte[32];
            RandomNumberGenerator.Fill(profileSalt);
            try
            {
                store._masterKeySession = ProfileEnvelopeCodec.CreateMasterKeySession(profileSalt, kdfIterations, passphrase);
            }
            finally
            {
                CryptographicOperations.ZeroMemory(profileSalt);
            }

            Guid pid = profileId ?? Guid.NewGuid();
            Guid bid = branchId ?? Guid.NewGuid();
            DateTimeOffset timestamp = savedAt ?? DateTimeOffset.UtcNow;

            StoreSnapshot initial = StoreArchiveCodec.CreateEmptySnapshot(pid, bid, 1UL, timestamp, label, note);
            store._profileId = pid;
            store._branchId = bid;
            store._state = ProfileStoreOpenState.Ready;
            store._lastSaveMetrics = store.CommitGenerationCore(initial, 1UL, timestamp, retainPreviousGeneration: false);
            return store;
        }
        catch
        {
            store.Dispose();
            throw;
        }
    }

    /// <summary>
    /// Opens an existing profile directory, acquiring the single-writer lock unless <paramref name="openReadOnly"/> is true.
    /// Never silently falls back to stale data when the newest generation fails validation or a purge is pending.
    /// </summary>
    public static ProfileStore Open(
        string directoryPath,
        string passphrase,
        bool openReadOnly = false,
        Action<ProfileStoreFaultPoint>? faultHook = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(directoryPath);
        ArgumentNullException.ThrowIfNull(passphrase);
        ProfileEnvelopeCodec.EnsureCryptographicHostSupport();

        string fullDir = Path.GetFullPath(directoryPath);
        if (!Directory.Exists(fullDir))
        {
            throw new DirectoryNotFoundException($"Profile directory '{fullDir}' does not exist.");
        }

        string lockKey = NormalizeDirectoryLockKey(fullDir);
        FileStream? lockStream = null;
        if (!openReadOnly)
        {
            (lockKey, lockStream) = AcquireWriteLock(fullDir);
        }

        ProfileStore store = new(fullDir, lockKey, lockStream, openReadOnly, faultHook);
        try
        {
            store.InspectAndLoadDirectory(passphrase);
            return store;
        }
        catch
        {
            store.Dispose();
            throw;
        }
    }

    /// <summary>
    /// Restores a verified backup file into an empty directory without regenerating <c>ProfileId</c> or entity IDs.
    /// </summary>
    public static ProfileStore RestoreIntoEmptyProfile(
        string directoryPath,
        string backupFilePath,
        string passphrase,
        bool openReadOnly = false)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(directoryPath);
        ArgumentException.ThrowIfNullOrWhiteSpace(backupFilePath);
        ArgumentNullException.ThrowIfNull(passphrase);
        ProfileEnvelopeCodec.EnsureCryptographicHostSupport();

        if (!File.Exists(backupFilePath))
        {
            throw new FileNotFoundException("Backup file not found.", backupFilePath);
        }

        string fullDir = Path.GetFullPath(directoryPath);
        Directory.CreateDirectory(fullDir);

        foreach (string existing in Directory.EnumerateFiles(fullDir))
        {
            string name = Path.GetFileName(existing);
            if (!string.Equals(name, LockFileName, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException($"RestoreIntoEmptyProfile requires an empty target directory; found '{name}'.");
            }
        }

        // Authenticate and validate the backup before copying it into the target directory.
        StoreSnapshot backupSnapshot;
        using (FileStream fs = new(backupFilePath, FileMode.Open, FileAccess.Read, FileShare.Read))
        using (SeekableAuthenticatedProfileStream auth = SeekableAuthenticatedProfileStream.Open(fs, passphrase, leaveOpen: true))
        {
            auth.VerifyAllFrames();
            backupSnapshot = StoreArchiveCodec.ValidateAndParseStoreZipFromStream(auth);
            StoreArchiveCodec.ValidateIdentityAndSemanticInvariants(backupSnapshot);
        }

        string targetGenFile = Path.Combine(fullDir, FormatGenerationFileName(backupSnapshot.Profile.Generation));
        File.Copy(backupFilePath, targetGenFile, overwrite: false);

        return Open(fullDir, passphrase, openReadOnly);
    }

    public ProfileSaveMetrics Save(StoreSnapshot snapshot, DateTimeOffset? savedAt = null)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        lock (_syncRoot)
        {
            EnsureWritableReady();
            if (_currentGeneration == ulong.MaxValue || _maxSeenGeneration == ulong.MaxValue)
            {
                throw new OverflowException("Generation counter has reached UInt64.MaxValue; profile store fails closed.");
            }

            ulong nextGen = Math.Max(_currentGeneration, _maxSeenGeneration) + 1UL;
            DateTimeOffset timestamp = savedAt ?? DateTimeOffset.UtcNow;
            StoreSnapshot updated = StoreArchiveCodec.WithLineage(
                snapshot,
                _profileId,
                _branchId,
                nextGen,
                timestamp,
                snapshot.Profile.ParentBranchId,
                snapshot.Profile.RecoveryProvenance);

            _lastSaveMetrics = CommitGenerationCore(updated, nextGen, timestamp, retainPreviousGeneration: true);
            return _lastSaveMetrics;
        }
    }

    public SeekableAuthenticatedProfileStream OpenCurrentGenerationAuthenticatedStream()
    {
        lock (_syncRoot)
        {
            EnsureNotDisposed();
            if (_state != ProfileStoreOpenState.Ready || _currentGeneration == 0 || _masterKeySession is null)
            {
                throw new InvalidOperationException("Cannot open current generation stream when the profile store is not in Ready state.");
            }

            string path = Path.Combine(_directoryPath, FormatGenerationFileName(_currentGeneration));
            FileStream fs = new(path, FileMode.Open, FileAccess.Read, FileShare.Read);
            try
            {
                return SeekableAuthenticatedProfileStream.OpenWithSession(fs, _masterKeySession, leaveOpen: false);
            }
            catch
            {
                fs.Dispose();
                throw;
            }
        }
    }

    public ProfileSaveMetrics RecoverFromPriorGeneration(
        ulong targetGeneration,
        string passphrase,
        DateTimeOffset? savedAt = null,
        string? recoveryNote = null)
    {
        ArgumentNullException.ThrowIfNull(passphrase);
        lock (_syncRoot)
        {
            EnsureNotDisposed();
            if (_openedReadOnly)
            {
                throw new InvalidOperationException("Cannot perform recovery on a read-only ProfileStore instance.");
            }

            if (_state != ProfileStoreOpenState.RecoveryRequired)
            {
                throw new InvalidOperationException("ProfileStore is not in RecoveryRequired state.");
            }

            if (File.Exists(Path.Combine(_directoryPath, PurgePendingFileName)))
            {
                throw new InvalidOperationException("Cannot recover from a prior generation while purge.pending is active; call ResumePendingPurge instead.");
            }

            ProfileRecoveryCandidate candidate = _recoveryCandidates.FirstOrDefault(c => c.Generation == targetGeneration)
                ?? throw new InvalidOperationException($"Generation {targetGeneration} is not a validated recovery candidate.");

            if (_maxSeenGeneration == ulong.MaxValue)
            {
                throw new OverflowException("Generation counter has reached UInt64.MaxValue; cannot allocate recovery generation.");
            }

            string candidatePath = Path.Combine(_directoryPath, candidate.FileName);
            byte[] header = new byte[ProfileEnvelopeCodec.HeaderSize];
            using (FileStream fs = new(candidatePath, FileMode.Open, FileAccess.Read, FileShare.Read))
            {
                fs.ReadExactly(header);
            }

            _masterKeySession?.Dispose();
            _masterKeySession = ProfileEnvelopeCodec.CreateMasterKeySessionFromHeader(header, passphrase);

            ulong recoveryGen = _maxSeenGeneration + 1UL;
            Guid newBranchId = Guid.NewGuid();
            DateTimeOffset timestamp = savedAt ?? DateTimeOffset.UtcNow;
            string provenance = string.Create(CultureInfo.InvariantCulture, $"recovered-from-gen-{candidate.Generation}-branch-{candidate.BranchId:D}");

            StoreSnapshot branchedSnapshot = StoreArchiveCodec.WithLineage(
                candidate.Snapshot,
                candidate.ProfileId,
                newBranchId,
                recoveryGen,
                timestamp,
                parentBranchId: candidate.BranchId,
                recoveryProvenance: provenance);

            if (recoveryNote is not null)
            {
                branchedSnapshot = branchedSnapshot with
                {
                    Profile = branchedSnapshot.Profile with { Note = recoveryNote },
                };
            }

            _profileId = candidate.ProfileId;
            _branchId = newBranchId;
            _state = ProfileStoreOpenState.Ready;
            _recoveryCandidates.Clear();

            _lastSaveMetrics = CommitGenerationCore(branchedSnapshot, recoveryGen, timestamp, retainPreviousGeneration: false);
            return _lastSaveMetrics;
        }
    }

    public ProfileSaveMetrics ExecutePrivacyPurge(
        StoreSnapshot purgedSnapshot,
        string passphrase,
        DateTimeOffset? savedAt = null,
        string? note = null)
    {
        ArgumentNullException.ThrowIfNull(purgedSnapshot);
        ArgumentNullException.ThrowIfNull(passphrase);

        lock (_syncRoot)
        {
            EnsureWritableReady();
            if (_masterKeySession is null)
            {
                throw new InvalidOperationException("No active master key session.");
            }

            // Verify caller passphrase matches the active profile lineage before starting a destructive purge.
            VerifyPassphraseAgainstSession(passphrase, _masterKeySession);

            if (_maxSeenGeneration == ulong.MaxValue)
            {
                throw new OverflowException("Generation counter exhausted.");
            }

            ulong replacementGen = Math.Max(_currentGeneration, _maxSeenGeneration) + 1UL;
            DateTimeOffset timestamp = savedAt ?? DateTimeOffset.UtcNow;
            StoreSnapshot updated = StoreArchiveCodec.WithLineage(
                purgedSnapshot,
                _profileId,
                _branchId,
                replacementGen,
                timestamp,
                purgedSnapshot.Profile.ParentBranchId,
                purgedSnapshot.Profile.RecoveryProvenance);

            StoreArchiveCodec.ValidateIdentityAndSemanticInvariants(updated);

            List<(string Name, string Sha256)> targetInventory = BuildManagedTargetInventory();
            StorePurgeIntent pendingIntent = new(
                _profileId,
                _branchId,
                _currentGeneration,
                "PrivacyPurge",
                "Pending",
                timestamp,
                replacementGen,
                targetInventory,
                note ?? "Privacy purge of superseded generations.");

            WriteEncryptedPurgeIntent(pendingIntent, _masterKeySession);
            WritePurgePendingMarker();
            TriggerFault(ProfileStoreFaultPoint.PurgeAfterIntentWritten);

            ProfileSaveMetrics metrics = CommitGenerationCore(updated, replacementGen, timestamp, retainPreviousGeneration: false, skipRetentionCleanup: true);

            StorePurgeIntent committedIntent = pendingIntent with { Status = "Committed" };
            WriteEncryptedPurgeIntent(committedIntent, _masterKeySession);
            TriggerFault(ProfileStoreFaultPoint.PurgeAfterReplacementCommitted);

            DeletePurgeInventoryAndVerify(targetInventory, replacementGen);
            TriggerFault(ProfileStoreFaultPoint.PurgeBeforePendingMarkerRemoval);

            RemovePurgeMarkers();
            _lastSaveMetrics = metrics with
            {
                RetainedGenerationFiles = [FormatGenerationFileName(replacementGen)],
            };
            return _lastSaveMetrics;
        }
    }

    public ProfileSaveMetrics RotatePassphrase(
        string currentPassphrase,
        string newPassphrase,
        int? newKdfIterations = null,
        DateTimeOffset? savedAt = null,
        string? note = null)
    {
        ArgumentNullException.ThrowIfNull(currentPassphrase);
        ArgumentNullException.ThrowIfNull(newPassphrase);

        lock (_syncRoot)
        {
            EnsureWritableReady();
            if (_masterKeySession is null || _currentSnapshot is null)
            {
                throw new InvalidOperationException("No active snapshot or master key session.");
            }

            VerifyPassphraseAgainstSession(currentPassphrase, _masterKeySession);
            _ = ProfileEnvelopeCodec.NormalizePassphrase(newPassphrase);

            int targetIterations = newKdfIterations ?? _masterKeySession.Iterations;
            if (targetIterations is < ProfileEnvelopeCodec.MinimumIterations or > ProfileEnvelopeCodec.MaximumIterations)
            {
                throw new ArgumentOutOfRangeException(nameof(newKdfIterations));
            }

            if (_maxSeenGeneration == ulong.MaxValue)
            {
                throw new OverflowException("Generation counter exhausted.");
            }

            // Preserve the validated old-file inventory BEFORE rotating keys.
            List<(string Name, string Sha256)> targetInventory = BuildManagedTargetInventory();
            ulong replacementGen = Math.Max(_currentGeneration, _maxSeenGeneration) + 1UL;
            DateTimeOffset timestamp = savedAt ?? DateTimeOffset.UtcNow;

            byte[] newProfileSalt = new byte[32];
            RandomNumberGenerator.Fill(newProfileSalt);
            ProfileMasterKeySession newSession;
            try
            {
                newSession = ProfileEnvelopeCodec.CreateMasterKeySession(newProfileSalt, targetIterations, newPassphrase);
            }
            finally
            {
                CryptographicOperations.ZeroMemory(newProfileSalt);
            }

            try
            {
                StorePurgeIntent pendingIntent = new(
                    _profileId,
                    _branchId,
                    _currentGeneration,
                    "PassphraseRotation",
                    "Pending",
                    timestamp,
                    replacementGen,
                    targetInventory,
                    note ?? "Passphrase and salt rotation.");

                WriteEncryptedPurgeIntent(pendingIntent, newSession);
                WritePurgePendingMarker();
                TriggerFault(ProfileStoreFaultPoint.PurgeAfterIntentWritten);

                ProfileMasterKeySession previousSession = _masterKeySession;
                _masterKeySession = newSession;
                newSession = null!;

                StoreSnapshot rotatedSnapshot = StoreArchiveCodec.WithLineage(
                    _currentSnapshot,
                    _profileId,
                    _branchId,
                    replacementGen,
                    timestamp,
                    _currentSnapshot.Profile.ParentBranchId,
                    _currentSnapshot.Profile.RecoveryProvenance);

                ProfileSaveMetrics metrics = CommitGenerationCore(rotatedSnapshot, replacementGen, timestamp, retainPreviousGeneration: false, skipRetentionCleanup: true);

                // Explicitly verify that the promoted replacement generation reopens with the new passphrase.
                string promotedPath = Path.Combine(_directoryPath, FormatGenerationFileName(replacementGen));
                using (FileStream verifyFs = new(promotedPath, FileMode.Open, FileAccess.Read, FileShare.Read))
                using (SeekableAuthenticatedProfileStream verifyAuth = SeekableAuthenticatedProfileStream.Open(verifyFs, newPassphrase, leaveOpen: true))
                {
                    verifyAuth.VerifyAllFrames();
                }

                // Clear old master key material now that the replacement generation is durably committed.
                previousSession.Dispose();

                StorePurgeIntent committedIntent = pendingIntent with { Status = "Committed" };
                WriteEncryptedPurgeIntent(committedIntent, _masterKeySession!);
                TriggerFault(ProfileStoreFaultPoint.PurgeAfterReplacementCommitted);

                DeletePurgeInventoryAndVerify(targetInventory, replacementGen);
                TriggerFault(ProfileStoreFaultPoint.PurgeBeforePendingMarkerRemoval);

                RemovePurgeMarkers();
                _lastSaveMetrics = metrics with
                {
                    RetainedGenerationFiles = [FormatGenerationFileName(replacementGen)],
                };
                return _lastSaveMetrics;
            }
            finally
            {
                newSession?.Dispose();
            }
        }
    }

    public void EraseWholeProfile(
        string passphrase,
        DateTimeOffset? createdAt = null,
        string? note = null)
    {
        ArgumentNullException.ThrowIfNull(passphrase);

        lock (_syncRoot)
        {
            EnsureNotDisposed();
            if (_openedReadOnly)
            {
                throw new InvalidOperationException("Cannot erase a profile opened in read-only mode.");
            }

            if (_masterKeySession is null)
            {
                throw new InvalidOperationException("No authenticated profile session available for erasure.");
            }

            VerifyPassphraseAgainstSession(passphrase, _masterKeySession);

            // Verify ownership of every generation-*.bffond file in the directory before erasing.
            foreach (string path in Directory.EnumerateFiles(_directoryPath))
            {
                string fileName = Path.GetFileName(path);
                if (TryParseGenerationFileName(fileName, out _))
                {
                    using FileStream fs = new(path, FileMode.Open, FileAccess.Read, FileShare.Read);
                    using SeekableAuthenticatedProfileStream auth = SeekableAuthenticatedProfileStream.OpenWithSession(fs, _masterKeySession, leaveOpen: true);
                    StoreSnapshot snap = StoreArchiveCodec.ValidateAndParseStoreZipFromStream(auth);
                    if (snap.Profile.ProfileId != _profileId)
                    {
                        throw new InvalidDataException($"Cannot erase directory: file '{fileName}' belongs to a different profileId ({snap.Profile.ProfileId}).");
                    }
                }
            }

            List<(string Name, string Sha256)> targetInventory = BuildManagedTargetInventory();
            DateTimeOffset timestamp = createdAt ?? DateTimeOffset.UtcNow;
            StorePurgeIntent eraseIntent = new(
                _profileId,
                _branchId,
                Math.Max(1UL, _currentGeneration),
                "WholeProfileErasure",
                "Committed",
                timestamp,
                ReplacementGeneration: null,
                targetInventory,
                note ?? "Whole-profile erasure.");

            WriteEncryptedPurgeIntent(eraseIntent, _masterKeySession);
            WritePurgePendingMarker();
            TriggerFault(ProfileStoreFaultPoint.PurgeAfterIntentWritten);

            DeletePurgeInventoryAndVerify(targetInventory, replacementGeneration: null);
            TriggerFault(ProfileStoreFaultPoint.PurgeBeforePendingMarkerRemoval);

            RemovePurgeMarkers();
            _masterKeySession.Dispose();
            _masterKeySession = null;
            _currentSnapshot = null;
            _identityLedger = null;
            _currentGeneration = 0;
            _state = ProfileStoreOpenState.Erased;
        }
    }

    public void ResumePendingPurge(string passphrase, string? alternatePassphrase = null)
    {
        ArgumentNullException.ThrowIfNull(passphrase);

        lock (_syncRoot)
        {
            EnsureNotDisposed();
            if (_openedReadOnly)
            {
                throw new InvalidOperationException("Cannot resume a pending purge in read-only mode.");
            }

            string pendingPath = Path.Combine(_directoryPath, PurgePendingFileName);
            string intentPath = Path.Combine(_directoryPath, PurgeIntentFileName);
            if (!File.Exists(pendingPath))
            {
                throw new InvalidOperationException("No purge.pending marker exists in this profile directory.");
            }

            ValidatePurgePendingMarkerFile(pendingPath);
            if (!File.Exists(intentPath))
            {
                throw new InvalidDataException("purge.pending exists without a companion purge.intent.bffond record.");
            }

            (StorePurgeIntent intent, ProfileMasterKeySession intentSession) = ReadAndAuthenticatePurgeIntent(intentPath, passphrase, alternatePassphrase);
            try
            {
                if (string.Equals(intent.OperationType, "WholeProfileErasure", StringComparison.Ordinal))
                {
                    DeletePurgeInventoryAndVerify(intent.TargetInventory, replacementGeneration: null);
                    RemovePurgeMarkers();
                    _masterKeySession?.Dispose();
                    _masterKeySession = null;
                    _currentSnapshot = null;
                    _identityLedger = null;
                    _currentGeneration = 0;
                    _diagnostics.Clear();
                    _recoveryCandidates.Clear();
                    _state = ProfileStoreOpenState.Erased;
                    return;
                }

                if (!intent.ReplacementGeneration.HasValue)
                {
                    throw new InvalidDataException($"Purge intent '{intent.OperationType}' is missing replacementGeneration.");
                }

                ulong repGen = intent.ReplacementGeneration.Value;
                string repFileName = FormatGenerationFileName(repGen);
                string repPath = Path.Combine(_directoryPath, repFileName);

                if (File.Exists(repPath))
                {
                    // Replacement generation was already promoted. Verify it, then finish deleting all superseded targets.
                    using (FileStream fs = new(repPath, FileMode.Open, FileAccess.Read, FileShare.Read))
                    using (SeekableAuthenticatedProfileStream auth = SeekableAuthenticatedProfileStream.OpenWithSession(fs, intentSession, leaveOpen: true))
                    {
                        auth.VerifyAllFrames();
                        StoreSnapshot repSnap = StoreArchiveCodec.ValidateAndParseStoreZipFromStream(auth);
                        StoreArchiveCodec.ValidateIdentityAndSemanticInvariants(repSnap);
                        if (repSnap.Profile.ProfileId != intent.ProfileId || repSnap.Profile.Generation != repGen)
                        {
                            throw new InvalidDataException("Replacement generation lineage does not match purge.intent.bffond.");
                        }
                    }

                    DeletePurgeInventoryAndVerify(intent.TargetInventory, repGen);
                    RemovePurgeMarkers();

                    _diagnostics.Clear();
                    _recoveryCandidates.Clear();
                    string activePassphrase = CanUnlockFileWithPassphrase(repPath, passphrase)
                        ? passphrase
                        : (alternatePassphrase ?? passphrase);
                    InspectAndLoadDirectory(activePassphrase);
                }
                else
                {
                    // Crash occurred before replacement generation was promoted (e.g. PurgeAfterIntentWritten).
                    // Clean up any unpromoted .tmp files, remove the uncommitted pending markers, and reload intact prior state.
                    DeleteUnpromotedTempFiles();
                    RemovePurgeMarkers();

                    _diagnostics.Clear();
                    _recoveryCandidates.Clear();
                    string activePassphrase = FindPassphraseForExistingGenerations(passphrase, alternatePassphrase);
                    InspectAndLoadDirectory(activePassphrase);
                }
            }
            finally
            {
                intentSession.Dispose();
            }
        }
    }

    public void ExportBackup(string destinationFilePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(destinationFilePath);

        lock (_syncRoot)
        {
            EnsureNotDisposed();
            if (_state != ProfileStoreOpenState.Ready || _currentGeneration == 0 || _masterKeySession is null)
            {
                throw new InvalidOperationException("Cannot export a backup when the profile store is not in Ready state.");
            }

            string sourcePath = Path.Combine(_directoryPath, FormatGenerationFileName(_currentGeneration));
            string fullDest = Path.GetFullPath(destinationFilePath);
            string? destDir = Path.GetDirectoryName(fullDest);
            if (!string.IsNullOrEmpty(destDir))
            {
                Directory.CreateDirectory(destDir);
            }

            File.Copy(sourcePath, fullDest, overwrite: true);

            // Verify the exported backup file by reopening and authenticating every frame.
            using FileStream fs = new(fullDest, FileMode.Open, FileAccess.Read, FileShare.Read);
            using SeekableAuthenticatedProfileStream auth = SeekableAuthenticatedProfileStream.OpenWithSession(fs, _masterKeySession, leaveOpen: true);
            auth.VerifyAllFrames();
            _ = StoreArchiveCodec.ValidateAndParseStoreZipFromStream(auth);
        }
    }

    public ProfileSaveMetrics ImportAndMerge(
        string backupFilePath,
        string backupPassphrase,
        DateTimeOffset? savedAt = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(backupFilePath);
        ArgumentNullException.ThrowIfNull(backupPassphrase);

        lock (_syncRoot)
        {
            EnsureWritableReady();
            if (_currentSnapshot is null)
            {
                throw new InvalidOperationException("Current snapshot is not loaded.");
            }

            if (!File.Exists(backupFilePath))
            {
                throw new FileNotFoundException("Backup file not found.", backupFilePath);
            }

            StoreSnapshot importedSnapshot;
            using (FileStream fs = new(backupFilePath, FileMode.Open, FileAccess.Read, FileShare.Read))
            using (SeekableAuthenticatedProfileStream auth = SeekableAuthenticatedProfileStream.Open(fs, backupPassphrase, leaveOpen: true))
            {
                auth.VerifyAllFrames();
                importedSnapshot = StoreArchiveCodec.ValidateAndParseStoreZipFromStream(auth);
                StoreArchiveCodec.ValidateIdentityAndSemanticInvariants(importedSnapshot);
            }

            if (_currentGeneration == ulong.MaxValue || _maxSeenGeneration == ulong.MaxValue)
            {
                throw new OverflowException("Generation counter exhausted.");
            }

            ulong nextGen = Math.Max(_currentGeneration, _maxSeenGeneration) + 1UL;
            DateTimeOffset timestamp = savedAt ?? DateTimeOffset.UtcNow;

            StoreSnapshot merged = StoreArchiveCodec.MergeSnapshotsWithIdRemapping(
                _currentSnapshot,
                importedSnapshot,
                _profileId,
                _branchId,
                nextGen,
                timestamp);

            _lastSaveMetrics = CommitGenerationCore(merged, nextGen, timestamp, retainPreviousGeneration: true);
            return _lastSaveMetrics;
        }
    }

    public ConnectionRemovalResult RemoveConnection(
        ulong connectionId,
        ConnectionRemovalDataDisposition disposition,
        string passphrase,
        bool removeOrphanedInstitution = true,
        DateTimeOffset? savedAt = null,
        string? note = null)
    {
        ArgumentNullException.ThrowIfNull(passphrase);

        lock (_syncRoot)
        {
            EnsureWritableReady();
            if (_currentSnapshot is null)
            {
                throw new InvalidOperationException("Current snapshot is not loaded.");
            }

            DateTimeOffset timestamp = savedAt ?? DateTimeOffset.UtcNow;
            ConnectionRemovalResult planned = ConnectionRemovalPlanner.PlanRemoval(
                _currentSnapshot,
                connectionId,
                disposition,
                removeOrphanedInstitution,
                timestamp);

            string purgeNote = note ?? $"Connection {connectionId} removed ({disposition}).";
            ProfileSaveMetrics metrics = ExecutePrivacyPurge(planned.UpdatedSnapshot, passphrase, timestamp, purgeNote);
            return planned with
            {
                UpdatedSnapshot = _currentSnapshot!,
                SaveMetrics = metrics,
            };
        }
    }

    public AccountRelinkingResult RelinkAccountBinding(
        ulong bindingId,
        ulong targetAccountId,
        string passphrase,
        bool pruneOrphanedAccount = true,
        DateTimeOffset? savedAt = null,
        string? note = null)
    {
        ArgumentNullException.ThrowIfNull(passphrase);

        lock (_syncRoot)
        {
            EnsureWritableReady();
            if (_currentSnapshot is null)
            {
                throw new InvalidOperationException("Current snapshot is not loaded.");
            }

            DateTimeOffset timestamp = savedAt ?? DateTimeOffset.UtcNow;
            AccountRelinkingResult planned = AccountRelinkingPlanner.PlanRelink(
                _currentSnapshot,
                bindingId,
                targetAccountId,
                pruneOrphanedAccount,
                timestamp);

            ProfileSaveMetrics metrics;
            if (planned.PrunedAccountId.HasValue)
            {
                string purgeNote = note ?? $"Relinked binding {bindingId} to account {targetAccountId} (pruned temporary account {planned.PrunedAccountId.Value}).";
                metrics = ExecutePrivacyPurge(planned.UpdatedSnapshot, passphrase, timestamp, purgeNote);
            }
            else
            {
                metrics = Save(planned.UpdatedSnapshot, timestamp);
            }

            return planned with
            {
                UpdatedSnapshot = _currentSnapshot!,
                SaveMetrics = metrics,
            };
        }
    }

    // ==========================================
    // Internal Lifecycle Implementation
    // ==========================================

    private void InspectAndLoadDirectory(string passphrase)
    {
        _diagnostics.Clear();
        _recoveryCandidates.Clear();

        // 1. Check for unpromoted temporary candidates (never selected as current, never auto-deleted on normal open).
        foreach (string filePath in Directory.EnumerateFiles(_directoryPath))
        {
            string fileName = Path.GetFileName(filePath);
            if (IsTemporaryCandidateFileName(fileName))
            {
                _diagnostics.Add(new ProfileDiagnostic(
                    ProfileDiagnosticCode.UnpromotedTemporaryCandidateIgnored,
                    fileName,
                    null,
                    $"Unpromoted temporary candidate '{fileName}' was ignored during startup selection."));
            }
        }

        // 2. Check for incomplete purge marker first: forbids silent fallback to older generations.
        string pendingPath = Path.Combine(_directoryPath, PurgePendingFileName);
        if (File.Exists(pendingPath))
        {
            _diagnostics.Add(new ProfileDiagnostic(
                ProfileDiagnosticCode.IncompletePurgeDetected,
                PurgePendingFileName,
                null,
                "An incomplete purge or passphrase rotation marker (purge.pending) is present; old generations cannot be selected as current."));
            _state = ProfileStoreOpenState.RecoveryRequired;
            _currentGeneration = 0;
            _currentSnapshot = null;
            _identityLedger = null;
            return;
        }

        // 3. Enumerate all generation-{20 digits}.bffond files sorted descending by filename generation.
        List<(string FileName, string FullPath, ulong FileGeneration)> generationFiles = [];
        foreach (string filePath in Directory.EnumerateFiles(_directoryPath))
        {
            string fileName = Path.GetFileName(filePath);
            if (TryParseGenerationFileName(fileName, out ulong gen))
            {
                generationFiles.Add((fileName, filePath, gen));
            }
        }

        if (generationFiles.Count == 0)
        {
            throw new FileNotFoundException($"No committed profile generation files found in '{_directoryPath}'.");
        }

        generationFiles.Sort((a, b) => b.FileGeneration.CompareTo(a.FileGeneration));
        _maxSeenGeneration = generationFiles[0].FileGeneration;

        List<EvaluatedGeneration> validGenerations = [];
        bool anyAuthenticated = false;
        Exception? lastAuthFailure = null;

        foreach ((string fileName, string fullPath, ulong fileGen) in generationFiles)
        {
            try
            {
                using FileStream fs = new(fullPath, FileMode.Open, FileAccess.Read, FileShare.Read);
                using SeekableAuthenticatedProfileStream auth = SeekableAuthenticatedProfileStream.Open(fs, passphrase, leaveOpen: true);
                auth.VerifyAllFrames();
                anyAuthenticated = true;

                byte[] headerCopy = auth.Header.ToArray();
                StoreSnapshot snapshot = StoreArchiveCodec.ValidateAndParseStoreZipFromStream(auth);

                if (snapshot.Profile.Generation != fileGen)
                {
                    _diagnostics.Add(new ProfileDiagnostic(
                        ProfileDiagnosticCode.FilenameManifestGenerationMismatch,
                        fileName,
                        fileGen,
                        $"Filename generation {fileGen} does not match manifest/profile generation {snapshot.Profile.Generation}."));
                    continue;
                }

                try
                {
                    StoreArchiveCodec.ValidateIdentityAndSemanticInvariants(snapshot);
                }
                catch (Exception idEx)
                {
                    _diagnostics.Add(new ProfileDiagnostic(
                        ProfileDiagnosticCode.IdentityLedgerValidationFailed,
                        fileName,
                        fileGen,
                        idEx.Message));
                    continue;
                }

                validGenerations.Add(new EvaluatedGeneration(fileName, fileGen, headerCopy, snapshot));
            }
            catch (ProfileAuthenticationException authEx)
            {
                lastAuthFailure = authEx;
                _diagnostics.Add(new ProfileDiagnostic(
                    ProfileDiagnosticCode.CorruptLatestGeneration,
                    fileName,
                    fileGen,
                    $"Generation {fileGen} failed cryptographic authentication: {authEx.Message}"));
            }
            catch (Exception ex)
            {
                _diagnostics.Add(new ProfileDiagnostic(
                    ProfileDiagnosticCode.CorruptLatestGeneration,
                    fileName,
                    fileGen,
                    $"Generation {fileGen} failed structural or schema validation: {ex.Message}"));
            }
        }

        if (validGenerations.Count == 0)
        {
            if (!anyAuthenticated && lastAuthFailure is not null)
            {
                throw new ProfileAuthenticationException("Failed to authenticate any profile generation with the provided passphrase.", lastAuthFailure);
            }

            throw new InvalidDataException($"No valid profile generation could be loaded from '{_directoryPath}'.");
        }

        // 4. Check lineage consistency across valid generations.
        EvaluatedGeneration newestValid = validGenerations[0];
        ulong highestFileGen = generationFiles[0].FileGeneration;
        bool hasLineageConflict = false;

        for (int i = 1; i < validGenerations.Count; i++)
        {
            EvaluatedGeneration older = validGenerations[i];
            if (older.Snapshot.Profile.ProfileId != newestValid.Snapshot.Profile.ProfileId)
            {
                hasLineageConflict = true;
                _diagnostics.Add(new ProfileDiagnostic(
                    ProfileDiagnosticCode.LineageProfileIdMismatch,
                    older.FileName,
                    older.Generation,
                    $"Generation {older.Generation} has ProfileId {older.Snapshot.Profile.ProfileId}, conflicting with {newestValid.Snapshot.Profile.ProfileId}."));
            }
            else if (older.Snapshot.Profile.BranchId != newestValid.Snapshot.Profile.BranchId &&
                     newestValid.Snapshot.Profile.ParentBranchId != older.Snapshot.Profile.BranchId)
            {
                hasLineageConflict = true;
                _diagnostics.Add(new ProfileDiagnostic(
                    ProfileDiagnosticCode.ForkedBranchConflict,
                    older.FileName,
                    older.Generation,
                    $"Generation {older.Generation} has unlinked BranchId {older.Snapshot.Profile.BranchId}, conflicting with {newestValid.Snapshot.Profile.BranchId}."));
            }
        }

        // Check if any non-temporary diagnostic (corrupt newest generation, filename mismatch, identity failure, lineage/fork conflict) occurred.
        bool newestFileIsValid = newestValid.Generation == highestFileGen;
        bool hasBlockingAnomaly = !newestFileIsValid || hasLineageConflict ||
            _diagnostics.Any(d => d.Code is ProfileDiagnosticCode.FilenameManifestGenerationMismatch
                or ProfileDiagnosticCode.IdentityLedgerValidationFailed
                or ProfileDiagnosticCode.LineageProfileIdMismatch
                or ProfileDiagnosticCode.ForkedBranchConflict);

        if (hasBlockingAnomaly)
        {
            _state = ProfileStoreOpenState.RecoveryRequired;
            _profileId = newestValid.Snapshot.Profile.ProfileId;
            _branchId = newestValid.Snapshot.Profile.BranchId;
            _currentGeneration = 0;
            _currentSnapshot = null;
            _identityLedger = null;

            foreach (EvaluatedGeneration candidate in validGenerations)
            {
                _recoveryCandidates.Add(new ProfileRecoveryCandidate(
                    candidate.FileName,
                    candidate.Generation,
                    candidate.Snapshot.Profile.ProfileId,
                    candidate.Snapshot.Profile.BranchId,
                    candidate.Snapshot.Profile.SavedAt,
                    candidate.Snapshot));
            }

            return;
        }

        // 5. All checks passed on the newest generation: enter Ready state.
        _masterKeySession?.Dispose();
        _masterKeySession = ProfileEnvelopeCodec.CreateMasterKeySessionFromHeader(newestValid.Header, passphrase);
        _profileId = newestValid.Snapshot.Profile.ProfileId;
        _branchId = newestValid.Snapshot.Profile.BranchId;
        _currentGeneration = newestValid.Generation;
        _currentSnapshot = newestValid.Snapshot;
        _state = ProfileStoreOpenState.Ready;

        IdentitySnapshot idSnap = StoreArchiveCodec.ToIdentitySnapshot(newestValid.Snapshot);
        IdentityValidationResult idVal = LocalIdentityLedger.TryOpen(idSnap, out LocalIdentityLedger? ledger);
        if (!idVal.IsValid || ledger is null)
        {
            throw new InvalidDataException("Failed to open LocalIdentityLedger for validated snapshot.");
        }

        _identityLedger = ledger;
    }

    private sealed record EvaluatedGeneration(
        string FileName,
        ulong Generation,
        byte[] Header,
        StoreSnapshot Snapshot);

    private ProfileSaveMetrics CommitGenerationCore(
        StoreSnapshot snapshot,
        ulong generation,
        DateTimeOffset savedAt,
        bool retainPreviousGeneration,
        bool skipRetentionCleanup = false)
    {
        if (_masterKeySession is null)
        {
            throw new InvalidOperationException("No active master key session.");
        }

        long startTimestamp = Stopwatch.GetTimestamp();

        // 1. Validate snapshot identity & semantic invariants before writing anything.
        StoreSnapshot normalized = StoreArchiveCodec.WithLineage(
            snapshot,
            _profileId,
            _branchId,
            generation,
            savedAt,
            snapshot.Profile.ParentBranchId,
            snapshot.Profile.RecoveryProvenance);

        StoreArchiveCodec.ValidateIdentityAndSemanticInvariants(normalized);

        // 2. Serialize XML documents and build the deterministic store ZIP.
        long plaintextXmlBytes =
            StoreArchiveCodec.SerializeProfile(normalized.Profile).Length +
            StoreArchiveCodec.SerializeInstitutions(normalized.Institutions).Length +
            StoreArchiveCodec.SerializeConnections(normalized.Connections).Length +
            StoreArchiveCodec.SerializeAccounts(normalized.Accounts).Length +
            StoreArchiveCodec.SerializeBalances(normalized.Balances).Length +
            StoreArchiveCodec.SerializeLedger(normalized.Ledger).Length;

        byte[] zipBytes = StoreArchiveCodec.CreateStoreZip(normalized);

        // 3. Create a fresh 96-byte envelope header with fresh snapshotSalt and noncePrefix.
        byte[] header = ProfileEnvelopeCodec.CreateHeader(_masterKeySession.ProfileSalt, _masterKeySession.Iterations);
        byte[] contentKey = _masterKeySession.DeriveContentKey(header);

        Span<byte> randomCandidateId = stackalloc byte[16];
        RandomNumberGenerator.Fill(randomCandidateId);
        string tempFileName = $"profile.new-{Convert.ToHexStringLower(randomCandidateId)}.tmp";
        string tempFilePath = Path.Combine(_directoryPath, tempFileName);
        string promotedFileName = FormatGenerationFileName(generation);
        string promotedFilePath = Path.Combine(_directoryPath, promotedFileName);

        long encryptedBytes;
        try
        {
            // 4. Write temporary candidate with FileMode.CreateNew and flush to disk.
            using (FileStream tempStream = new(tempFilePath, FileMode.CreateNew, FileAccess.ReadWrite, FileShare.None))
            {
                ProfileEnvelopeCodec.EncryptToStream(tempStream, zipBytes, header, contentKey);
                TriggerFault(ProfileStoreFaultPoint.DuringTemporaryGenerationWrite);
                tempStream.Flush(flushToDisk: true);
                encryptedBytes = tempStream.Length;
            }

            // 5. Reopen and verify the temporary candidate before promotion.
            VerifyGenerationFileOnDisk(tempFilePath, _masterKeySession, _profileId, _branchId, generation);

            TriggerFault(ProfileStoreFaultPoint.BeforeGenerationPromotion);

            // 6. Atomically promote the verified candidate to generation-{20 digits}.bffond.
            File.Move(tempFilePath, promotedFilePath, overwrite: false);

            // 7. Reopen and verify the promoted generation file at its final path.
            StoreSnapshot verifiedSnapshot = VerifyGenerationFileOnDisk(promotedFilePath, _masterKeySession, _profileId, _branchId, generation);

            _currentGeneration = generation;
            _maxSeenGeneration = Math.Max(_maxSeenGeneration, generation);
            _currentSnapshot = verifiedSnapshot;

            IdentitySnapshot idSnap = StoreArchiveCodec.ToIdentitySnapshot(verifiedSnapshot);
            IdentityValidationResult idVal = LocalIdentityLedger.TryOpen(idSnap, out LocalIdentityLedger? ledger);
            if (!idVal.IsValid || ledger is null)
            {
                throw new InvalidDataException("Promoted generation failed LocalIdentityLedger initialization.");
            }

            _identityLedger = ledger;
        }
        finally
        {
            CryptographicOperations.ZeroMemory(zipBytes);
            CryptographicOperations.ZeroMemory(contentKey);
            CryptographicOperations.ZeroMemory(header);
        }

        TriggerFault(ProfileStoreFaultPoint.BeforeRetentionCleanup);

        // 8. Retention policy: keep current (G) and immediately preceding validated generation (G-1) in the same lineage.
        List<string> cleanupWarnings = [];
        List<string> retainedFiles = [promotedFileName];

        if (!skipRetentionCleanup)
        {
            ApplyRetentionPolicy(generation, retainPreviousGeneration, retainedFiles, cleanupWarnings);
        }

        long elapsedTicks = Stopwatch.GetTimestamp() - startTimestamp;
        long elapsedMs = (long)((double)elapsedTicks * 1000.0 / Stopwatch.Frequency);
        double writeAmplification = (double)encryptedBytes / Math.Max(1L, zipBytes.Length);

        return new ProfileSaveMetrics(
            generation,
            plaintextXmlBytes,
            zipBytes.Length,
            encryptedBytes,
            writeAmplification,
            elapsedMs,
            retainedFiles,
            cleanupWarnings);
    }

    private static StoreSnapshot VerifyGenerationFileOnDisk(
        string filePath,
        ProfileMasterKeySession session,
        Guid expectedProfileId,
        Guid expectedBranchId,
        ulong expectedGeneration)
    {
        using FileStream fs = new(filePath, FileMode.Open, FileAccess.Read, FileShare.Read);
        using SeekableAuthenticatedProfileStream auth = SeekableAuthenticatedProfileStream.OpenWithSession(fs, session, leaveOpen: true);
        auth.VerifyAllFrames();
        StoreSnapshot snapshot = StoreArchiveCodec.ValidateAndParseStoreZipFromStream(auth);
        StoreArchiveCodec.ValidateIdentityAndSemanticInvariants(snapshot);

        if (snapshot.Profile.ProfileId != expectedProfileId ||
            snapshot.Profile.BranchId != expectedBranchId ||
            snapshot.Profile.Generation != expectedGeneration)
        {
            throw new InvalidDataException("Verified generation file lineage metadata does not match expected commit parameters.");
        }

        return snapshot;
    }

    private void ApplyRetentionPolicy(
        ulong currentGen,
        bool retainPreviousGeneration,
        List<string> retainedFiles,
        List<string> cleanupWarnings)
    {
        if (_masterKeySession is null)
        {
            return;
        }

        List<(string FileName, string FullPath, ulong Generation)> priorGenerations = [];
        foreach (string path in Directory.EnumerateFiles(_directoryPath))
        {
            string name = Path.GetFileName(path);
            if (TryParseGenerationFileName(name, out ulong gen) && gen < currentGen)
            {
                priorGenerations.Add((name, path, gen));
            }
        }

        priorGenerations.Sort((a, b) => b.Generation.CompareTo(a.Generation));
        bool keptPrevious = false;

        foreach ((string name, string path, ulong gen) in priorGenerations)
        {
            // Verify that the file belongs to this profile and branch lineage before touching it.
            bool isSameLineageValid = false;
            try
            {
                using FileStream fs = new(path, FileMode.Open, FileAccess.Read, FileShare.Read);
                using SeekableAuthenticatedProfileStream auth = SeekableAuthenticatedProfileStream.OpenWithSession(fs, _masterKeySession, leaveOpen: true);
                StoreSnapshot snap = StoreArchiveCodec.ValidateAndParseStoreZipFromStream(auth);
                isSameLineageValid = snap.Profile.ProfileId == _profileId && snap.Profile.BranchId == _branchId && snap.Profile.Generation == gen;
            }
            catch
            {
                // Failed files are never automatically deleted during normal retention cleanup.
                continue;
            }

            if (!isSameLineageValid)
            {
                continue;
            }

            if (retainPreviousGeneration && !keptPrevious)
            {
                keptPrevious = true;
                retainedFiles.Add(name);
                continue;
            }

            try
            {
                File.Delete(path);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                string warning = $"Non-fatal retention cleanup warning for '{name}': {ex.Message}";
                cleanupWarnings.Add(warning);
                _diagnostics.Add(new ProfileDiagnostic(ProfileDiagnosticCode.RetentionCleanupWarning, name, gen, warning));
            }
        }

        retainedFiles.Sort(StringComparer.Ordinal);
    }

    private List<(string Name, string Sha256)> BuildManagedTargetInventory()
    {
        List<(string Name, string Sha256)> inventory = [];
        foreach (string path in Directory.EnumerateFiles(_directoryPath))
        {
            string name = Path.GetFileName(path);
            if (TryParseGenerationFileName(name, out _) || IsTemporaryCandidateFileName(name))
            {
                byte[] bytes = File.ReadAllBytes(path);
                string sha = Convert.ToHexStringLower(SHA256.HashData(bytes));
                inventory.Add((name, sha));
            }
        }

        inventory.Sort((a, b) => string.CompareOrdinal(a.Name, b.Name));
        if (inventory.Count == 0)
        {
            throw new InvalidOperationException("Cannot build purge inventory: no managed generation files exist.");
        }

        return inventory;
    }

    private void WriteEncryptedPurgeIntent(StorePurgeIntent intent, ProfileMasterKeySession session)
    {
        byte[] xmlBytes = StoreArchiveCodec.SerializePurgeIntent(intent);
        byte[] header = ProfileEnvelopeCodec.CreateHeader(session.ProfileSalt, session.Iterations);
        byte[] contentKey = session.DeriveContentKey(header);
        try
        {
            byte[] encrypted = ProfileEnvelopeCodec.Encrypt(xmlBytes, header, contentKey);
            string intentPath = Path.Combine(_directoryPath, PurgeIntentFileName);
            File.WriteAllBytes(intentPath, encrypted);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(xmlBytes);
            CryptographicOperations.ZeroMemory(contentKey);
            CryptographicOperations.ZeroMemory(header);
        }
    }

    private void WritePurgePendingMarker()
    {
        string pendingPath = Path.Combine(_directoryPath, PurgePendingFileName);
        using FileStream fs = new(pendingPath, FileMode.Create, FileAccess.Write, FileShare.None);
        fs.Write(PurgePendingMagicBytes);
        fs.Flush(flushToDisk: true);
    }

    private static void ValidatePurgePendingMarkerFile(string pendingPath)
    {
        byte[] bytes = File.ReadAllBytes(pendingPath);
        if (!bytes.AsSpan().SequenceEqual(PurgePendingMagicBytes))
        {
            throw new InvalidDataException("Invalid purge.pending marker header bytes.");
        }
    }

    private static (StorePurgeIntent Intent, ProfileMasterKeySession Session) ReadAndAuthenticatePurgeIntent(
        string intentPath,
        string passphrase,
        string? alternatePassphrase)
    {
        byte[] envelope = File.ReadAllBytes(intentPath);
        ProfileEnvelopeCodec.ValidateEnvelopeSize(envelope.Length);
        ReadOnlySpan<byte> header = envelope.AsSpan(0, ProfileEnvelopeCodec.HeaderSize);
        ProfileEnvelopeCodec.ValidateHeader(header);

        string[] candidates = alternatePassphrase is not null
            ? [passphrase, alternatePassphrase]
            : [passphrase];

        Exception? lastError = null;
        foreach (string candidate in candidates)
        {
            try
            {
                ProfileMasterKeySession session = ProfileEnvelopeCodec.CreateMasterKeySessionFromHeader(header, candidate);
                byte[] contentKey = session.DeriveContentKey(header);
                try
                {
                    byte[] xmlBytes = ProfileEnvelopeCodec.DecryptWithKey(envelope, contentKey);
                    StorePurgeIntent intent = StoreArchiveCodec.ValidateAndParsePurgeIntent(xmlBytes);
                    return (intent, session);
                }
                catch
                {
                    session.Dispose();
                    throw;
                }
                finally
                {
                    CryptographicOperations.ZeroMemory(contentKey);
                }
            }
            catch (Exception ex)
            {
                lastError = ex;
            }
        }

        throw new ProfileAuthenticationException("Unable to authenticate purge.intent.bffond with the supplied passphrase(s).",
            lastError ?? new InvalidDataException("Purge intent authentication failed."));
    }

    private void DeletePurgeInventoryAndVerify(
        IReadOnlyList<(string Name, string Sha256)> targetInventory,
        ulong? replacementGeneration)
    {
        string? replacementFileName = replacementGeneration.HasValue
            ? FormatGenerationFileName(replacementGeneration.Value)
            : null;

        bool firstDeleted = false;
        foreach ((string name, string expectedSha) in targetInventory)
        {
            if (!TryParseGenerationFileName(name, out _) && !IsTemporaryCandidateFileName(name))
            {
                throw new InvalidDataException($"Purge inventory target '{name}' violates fixed managed filename grammar.");
            }

            if (replacementFileName is not null && string.Equals(name, replacementFileName, StringComparison.Ordinal))
            {
                continue;
            }

            string fullPath = Path.Combine(_directoryPath, name);
            if (!File.Exists(fullPath))
            {
                continue;
            }

            byte[] currentBytes = File.ReadAllBytes(fullPath);
            string actualSha = Convert.ToHexStringLower(SHA256.HashData(currentBytes));
            if (!string.Equals(actualSha, expectedSha, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidDataException($"Target file '{name}' SHA-256 digest does not match authenticated purge inventory; refusing unverified deletion.");
            }

            File.Delete(fullPath);
            if (!firstDeleted)
            {
                firstDeleted = true;
                TriggerFault(ProfileStoreFaultPoint.PurgeDuringSupersededDeletion);
            }
        }

        // Also remove any unpromoted temporary candidates matching the fixed grammar.
        DeleteUnpromotedTempFiles();

        // Re-enumerate directory to prove no superseded generation or temporary candidate remains.
        foreach (string path in Directory.EnumerateFiles(_directoryPath))
        {
            string name = Path.GetFileName(path);
            if (IsTemporaryCandidateFileName(name))
            {
                throw new IOException($"Post-purge verification failed: temporary file '{name}' still exists.");
            }

            if (TryParseGenerationFileName(name, out _))
            {
                if (replacementFileName is null || !string.Equals(name, replacementFileName, StringComparison.Ordinal))
                {
                    throw new IOException($"Post-purge verification failed: superseded generation '{name}' still exists.");
                }
            }
        }
    }

    private void DeleteUnpromotedTempFiles()
    {
        foreach (string path in Directory.EnumerateFiles(_directoryPath))
        {
            string name = Path.GetFileName(path);
            if (IsTemporaryCandidateFileName(name))
            {
                File.Delete(path);
            }
        }
    }

    private void RemovePurgeMarkers()
    {
        string intentPath = Path.Combine(_directoryPath, PurgeIntentFileName);
        string pendingPath = Path.Combine(_directoryPath, PurgePendingFileName);

        if (File.Exists(intentPath))
        {
            File.Delete(intentPath);
        }

        if (File.Exists(pendingPath))
        {
            File.Delete(pendingPath);
        }
    }

    private void VerifyPassphraseAgainstSession(string passphrase, ProfileMasterKeySession session)
    {
        if (_currentGeneration == 0)
        {
            throw new InvalidOperationException("No active generation to verify passphrase against.");
        }

        string currentPath = Path.Combine(_directoryPath, FormatGenerationFileName(_currentGeneration));
        using FileStream fs = new(currentPath, FileMode.Open, FileAccess.Read, FileShare.Read);
        using SeekableAuthenticatedProfileStream auth = SeekableAuthenticatedProfileStream.Open(fs, passphrase, leaveOpen: true);
        if (!session.MatchesHeader(auth.Header))
        {
            throw new ProfileAuthenticationException("Supplied passphrase header does not match active profile session.");
        }
    }

    private static bool CanUnlockFileWithPassphrase(string filePath, string passphrase)
    {
        try
        {
            using FileStream fs = new(filePath, FileMode.Open, FileAccess.Read, FileShare.Read);
            using SeekableAuthenticatedProfileStream auth = SeekableAuthenticatedProfileStream.Open(fs, passphrase, leaveOpen: true);
            return true;
        }
        catch
        {
            return false;
        }
    }

    private string FindPassphraseForExistingGenerations(string passphrase, string? alternatePassphrase)
    {
        foreach (string path in Directory.EnumerateFiles(_directoryPath))
        {
            string name = Path.GetFileName(path);
            if (TryParseGenerationFileName(name, out _))
            {
                if (CanUnlockFileWithPassphrase(path, passphrase))
                {
                    return passphrase;
                }

                if (alternatePassphrase is not null && CanUnlockFileWithPassphrase(path, alternatePassphrase))
                {
                    return alternatePassphrase;
                }
            }
        }

        return passphrase;
    }

    private void TriggerFault(ProfileStoreFaultPoint point) => _faultHook?.Invoke(point);

    private static string NormalizeDirectoryLockKey(string fullDirectoryPath) =>
        Path.TrimEndingDirectorySeparator(Path.GetFullPath(fullDirectoryPath));

    private static (string LockKey, FileStream LockStream) AcquireWriteLock(string fullDirectoryPath)
    {
        string lockKey = NormalizeDirectoryLockKey(fullDirectoryPath);
        if (!ActiveWriteLocks.TryAdd(lockKey, 0))
        {
            throw new ProfileLockConflictException(fullDirectoryPath);
        }

        string lockFilePath = Path.Combine(fullDirectoryPath, LockFileName);
        try
        {
            FileStream lockStream = new(lockFilePath, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
            return (lockKey, lockStream);
        }
        catch (IOException ex)
        {
            ActiveWriteLocks.TryRemove(lockKey, out _);
            throw new ProfileLockConflictException(fullDirectoryPath, ex);
        }
        catch
        {
            ActiveWriteLocks.TryRemove(lockKey, out _);
            throw;
        }
    }

    private void EnsureWritableReady()
    {
        EnsureNotDisposed();
        if (_openedReadOnly)
        {
            throw new InvalidOperationException("ProfileStore is opened in read-only mode.");
        }

        if (_state != ProfileStoreOpenState.Ready)
        {
            throw new InvalidOperationException($"ProfileStore is in '{_state}' state and cannot accept normal writes.");
        }
    }

    private void EnsureNotDisposed() => ObjectDisposedException.ThrowIf(_disposed, this);

    public void Dispose()
    {
        lock (_syncRoot)
        {
            if (!_disposed)
            {
                _masterKeySession?.Dispose();
                _masterKeySession = null;
                if (_lockStream is not null)
                {
                    _lockStream.Dispose();
                    ActiveWriteLocks.TryRemove(_normalizedLockKey, out _);
                }

                _disposed = true;
            }
        }
    }
}
