# ADR 0088: Production Profile Store Lifecycle, Seekable Frame Authentication, and Purge Recovery

- **Status:** Accepted
- **Date:** 2026-10-16
- **Extends:** [ADR 0004](0004-profile-envelope-v1.md), [ADR 0005](0005-profile-snapshots-v1.md), [ADR 0006](0006-local-identity-allocation.md), [ADR 0087](0087-profile-storage-schemas-preflight-and-calibration.md)
- **Human security review:** Pending human storage/security review before enabling host UI persistence

## Context

[ADR 0004](0004-profile-envelope-v1.md), [ADR 0005](0005-profile-snapshots-v1.md), and [ADR 0087](0087-profile-storage-schemas-preflight-and-calibration.md) specified the 96-byte framed AES-256-GCM envelope (`BFONDPRF`), the `store-v1.xsd` and `purge-intent-v1.xsd` XML schemas, bounded hostile-input ZIP preflight validation, and supported-floor PBKDF2-HMAC-SHA256 calibration. Completing Milestone 1 item **M1-02 (Profile lifecycle)** requires promoting these storage components into `Broiler.Fond.Kernel.Storage` and implementing the production `IProfileStore` / `ProfileStore` lifecycle:

1. **Single-Writer Directory Lock**: Preventing concurrent writers in the same profile directory while permitting explicit read-only inspection.
2. **Seekable Authenticating Frame Stream**: Authenticating the 96-byte envelope header, final empty record, last data frame, and every accessed 64 KiB AES-256-GCM frame before returning any plaintext bytes to `ZipArchive` or XML readers.
3. **Immutable Generation Commits & Bounded Retention**: Writing temporary candidates with `FileMode.CreateNew`, flushing, reopening and verifying before and after atomic `File.Move(..., overwrite: false)` promotion, bounding write amplification (`<= 1.10`), and retaining only the current (`G`) and immediately preceding (`G-1`) validated generation in the same lineage.
4. **Fail-Closed Startup Inspection & Branched Recovery**: Refusing to silently fall back to stale generations when the newest committed generation fails authentication/validation or when lineage/fork/generation-filename/identity-ledger conflicts exist; instead entering a read-only `RecoveryRequired` state with explicit branched `RecoverFromPriorGeneration` (`new BranchId`, `ParentBranchId`, `RecoveryProvenance`, and `maxSeenGeneration + 1`).
5. **Privacy Purge, Passphrase Rotation, Whole-Profile Erasure & Fault-Injected Recovery**: Executing destructive purges and key rotations under an encrypted `purge.intent.bffond` record and `purge.pending` (`BFONDPURGE1\n`) marker, with deterministic `ResumePendingPurge` recovery across all crash boundaries.
6. **Portable Backup, Restore Into Empty Profile & ID-Remapping Import/Merge**: Exporting verified backups, restoring into empty directories with preserved lineage and entity IDs, and merging into non-empty profiles by deterministically remapping imported entity IDs into the target's `NextId` space and rewriting all cross-references.

## Decision

### 1. Kernel Storage Promotion (`Broiler.Fond.Kernel.Storage`)

The embedded XSD resources (`ProfileEnvelope.bootstrap-v1.xsd`, `ProfileEnvelope.store-v1.xsd`, `ProfileEnvelope.purge-intent-v1.xsd`) and the following production codecs and validators live in `src/Broiler.Fond.Kernel/Storage/`:
- `ProfileEnvelopeCodec` and `ProfileMasterKeySession`: Enforces `AesGcm.IsSupported` as a fail-closed host gate, normalizes passphrases (`NormalizationForm.FormC`), derives 32-byte PBKDF2-HMAC-SHA256 master keys and HKDF-SHA256 content keys, generates fresh 32-byte `snapshotSalt` and 4-byte `noncePrefix` values via `RandomNumberGenerator.Fill` on every encryption attempt, and caches session master keys bound to `(profileSalt, kdfId=1, passphraseRuleId=1, iterations)` with deterministic zeroization on `Dispose` or rotation.
- `SeekableAuthenticatedProfileStream`: Wraps a seekable encrypted envelope `Stream`. During initialization it validates physical bounds (`125 .. 1,074,217,085` bytes), validates the 96-byte header, authenticates the final empty record at `stream.Length - 29` (`flag == 1`, `length == 0`, valid 16-byte GCM tag), and authenticates the last data frame (`finalOrdinal - 1`) to establish the exact authenticated payload `Length` before any caller or `ZipArchive` reads from the stream. Random-access `Read` calls map `Position` to frame ordinal `n = Position / 65536`, authenticate frame `n` (`ordinal == n`, `flag == 0`, expected length, GCM tag over `header || descriptor`), and maintain a bounded 4-frame LRU cache whose plaintext buffers are zeroed via `CryptographicOperations.ZeroMemory` on eviction and disposal.
- `ZipPreflight`, `SupportedFloorKdfCalibration`, and `StoreArchiveCodec`: Validate bounded ZIP structure before `ZipArchive` instantiation, validate all seven `store-v1.xsd` XML documents plus `manifest.xml` and byte-exact round-trip equality, and verify `LocalIdentityLedger` (`IdentitySnapshotValidator`) plus cross-document semantic reference integrity (`ValidateIdentityAndSemanticInvariants`).

### 2. Directory Layout, Exclusive Locking, and Atomic Generation Commits (`ProfileStore`)

- **Managed filenames**:
  - `profile.lock`: Opened with `FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None` alongside an in-process normalized-path guard (`ActiveWriteLocks`). Competing read-write `CreateNew` or `Open` calls fail immediately with `ProfileLockConflictException`. Read-only opens (`openReadOnly: true`) do not acquire the write lock and disallow all mutating operations.
  - `generation-{20 decimal digits}.bffond`: Committed generation files (`generation >= 1`, exact 20-digit zero-padded decimal formatting).
  - `profile.new-{32 lowercase hex digits}.tmp`: Unpromoted temporary candidate files.
  - `purge.pending` (`"BFONDPURGE1\n"`) and `purge.intent.bffond`: Durable purge and passphrase-rotation markers.
- **Save commit protocol (`Save`)**:
  1. Validates `StoreSnapshot` against `StoreArchiveCodec.ValidateIdentityAndSemanticInvariants` and checks that the generation counter has not reached `ulong.MaxValue`.
  2. Serializes the canonical `store-v1` ZIP archive and generates a fresh 96-byte header (`snapshotSalt` and `noncePrefix` from CSPRNG; `profileSalt` preserved from the active lineage session).
  3. Writes `profile.new-{32 hex}.tmp` using `FileMode.CreateNew`, flushes to disk (`Flush(flushToDisk: true)`), closes the handle, and reopens the candidate to verify all frames (`VerifyAllFrames()`), ZIP preflight, XML schemas, manifest digests, and identity ledger invariants.
  4. Promotes the candidate via `File.Move(tempPath, promotedPath, overwrite: false)` and reopens the promoted file at its final path for full verification before updating in-memory state (`CurrentGeneration`, `CurrentSnapshot`, `IdentityLedger`).
  5. Applies bounded retention: keeps only the newly promoted generation (`G`) and the immediately preceding validated generation (`G - 1`) in the same `(ProfileId, BranchId)` lineage, recording any non-fatal file deletion errors in `ProfileSaveMetrics.CleanupWarnings` and `Diagnostics`.

### 3. Fail-Closed Startup Selection and Branched Prior-Generation Recovery

- On `ProfileStore.Open`:
  - Unpromoted `profile.new-*.tmp` files are never selected as current and never automatically deleted during normal open; they are reported via `ProfileDiagnosticCode.UnpromotedTemporaryCandidateIgnored`.
  - If `purge.pending` exists, `Open` immediately enters `ProfileStoreOpenState.RecoveryRequired` (`IsReadOnly = true`, `CurrentSnapshot = null`) with `ProfileDiagnosticCode.IncompletePurgeDetected`, preventing any silent fallback to superseded generations.
  - All `generation-*.bffond` files are evaluated in descending generation order. If no generation authenticates with the supplied passphrase, `Open` throws `ProfileAuthenticationException`.
  - Failed committed generation files are never automatically deleted as a repair. If the highest-numbered generation fails authentication, ZIP preflight, XML/manifest validation, filename-to-manifest generation matching (`FilenameManifestGenerationMismatch`), or identity ledger validation (`IdentityLedgerValidationFailed`), or if valid generations exhibit `LineageProfileIdMismatch` or `ForkedBranchConflict`, `Open` enters `ProfileStoreOpenState.RecoveryRequired` (`IsReadOnly = true`) and populates `Diagnostics` and `RecoveryCandidates`.
- **Explicit branched recovery (`RecoverFromPriorGeneration`)**:
  - Requires a writable store instance in `RecoveryRequired` state (with no active `purge.pending`).
  - Branches from the user-selected `ProfileRecoveryCandidate` by allocating a fresh `BranchId`, setting `ParentBranchId` to the candidate's `BranchId`, recording `RecoveryProvenance`, and committing `maxSeenGeneration + 1` so generation numbers remain strictly monotonic across the directory.

### 4. Privacy Purge, Passphrase Rotation, Whole-Profile Erasure, and Crash Recovery

- **Privacy purge (`ExecutePrivacyPurge`)**:
  - Builds a SHA-256 inventory (`TargetInventory`) of all existing `generation-*.bffond` and `profile.new-*.tmp` files, writes encrypted `purge.intent.bffond` (`OperationType = "PrivacyPurge"`, `ReplacementGeneration = G + 1`) and `purge.pending`, commits and verifies replacement generation `G + 1`, deletes all files in `TargetInventory` (retaining zero prior recovery copies), verifies by directory re-enumeration that only `G + 1` remains, and removes `purge.intent.bffond` and `purge.pending` last.
- **Passphrase rotation (`RotatePassphrase`)**:
  - Verifies the current passphrase, captures the validated `TargetInventory` before key rotation, generates a fresh 32-byte `profileSalt` and derives the new master-key session, writes `purge.intent.bffond` and `purge.pending`, commits replacement generation `G + 1` under the new passphrase and salts, reopens `G + 1` with the new passphrase to verify unlockability, zeroes the old master key session, deletes all old-passphrase files using the pre-verified `TargetInventory` (never labeling an old file purged merely because it fails the new passphrase), verifies by re-enumeration that only `G + 1` remains, and removes the purge markers last.
- **Whole-profile erasure (`EraseWholeProfile`)**:
  - Verifies passphrase and lineage ownership of all committed generations in the directory, records `purge.intent.bffond` (`OperationType = "WholeProfileErasure"`) and `purge.pending`, deletes all managed generation and `.tmp` files, verifies by re-enumeration that none remain, removes the purge markers last, zeroes session keys, and transitions to `ProfileStoreOpenState.Erased`.
- **Interrupted purge recovery (`ResumePendingPurge`)**:
  - Deterministically recovers from crashes at `PurgeAfterIntentWritten`, `PurgeAfterReplacementCommitted`, `PurgeDuringSupersededDeletion`, and `PurgeBeforePendingMarkerRemoval`. If a crash occurred before replacement generation promotion, uncommitted `.tmp` files and markers are cleaned up and the intact prior generation is reloaded; if replacement promotion completed, all remaining `TargetInventory` files are verified and deleted, re-enumeration confirms completion, and the replacement generation becomes active.

### 5. Portable Backup, Empty Restore, and ID-Remapping Import/Merge

- `ExportBackup`: Copies the active generation file to the destination path and reopens/verifies every frame and document of the written backup.
- `RestoreIntoEmptyProfile`: Authenticates and validates a backup file before copying it into an empty profile directory and opening it without regenerating `ProfileId`, `BranchId`, `Generation`, or entity IDs.
- `ImportAndMerge`: Authenticates and validates an external backup and merges it into a non-empty active profile via `StoreArchiveCodec.MergeSnapshotsWithIdRemapping`, deterministically remapping every imported entity ID (`Institution`, `Connection`, `Account`, `AccountIncarnation`, `AccountBinding`, `Observation`, `BalanceSnapshot`, and `Ledger` records) into the target profile's `NextId` allocation space and rewriting all cross-references before committing generation `G + 1`.

## Consequences & Verification

- `ProfileStoreLifecycleTests` adds 127 executable checks verifying single-writer locking, read-only open, multi-generation retention, write-amplification bounds (`<= 1.10`), seekable random-access frame authentication and tamper rejection, save fault injection, corrupt-latest-generation recovery branching, lineage/manifest conflict detection, privacy purge, passphrase rotation, whole-profile erasure, interrupted purge resume across all fault points, backup/restore, ID-remapping import/merge, and multi-threaded contention.
- `Broiler.Fond.Host.Windows` remains deliberately inert until Milestone 1 UI and human format/security reviews complete.
