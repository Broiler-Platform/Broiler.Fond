# ADR 0104: Backup Migration, Fork Detection, and ID Remapping Hardening

## Status
Accepted

## Context
In Broiler Fond, the local profile store is the single source of truth for user financial data. Users may export encrypted portable backups, restore backups onto new devices, or import/merge backups across profiles or machines.

Because Broiler Fond operates strictly without a central relay or remote database, several critical invariants defined in the product roadmap must be strictly enforced during backup migration and snapshot merges:
1. **Zero Credential Persistence & Mandatory Reauthentication:**
   Banking credentials (PINs and TANs) are ephemeral and held only in memory during active foreground sessions. Backups and archives never contain credentials. Consequently, any connection imported from an external backup or restored onto a device must never remain in an active session state (`Connected`, `Authenticating`, `ChallengeRequired`). It must transition immediately to `ReauthenticationRequired` so that the user is explicitly prompted for credentials before any bank dialogue occurs.
2. **Lineage Fork Detection:**
   When an imported snapshot shares the same `ProfileId` or `BranchId` as the target profile but possesses divergent generations, timestamps, or entity contents, a lineage fork has occurred (e.g. the user restored a backup on two separate laptops and used both independently). The engine must detect this divergence rather than silently overwriting records.
3. **Collision-Free ID Remapping:**
   Equal entity IDs across forked branches or distinct profiles cannot be assumed to represent the same entity. The merge engine must remap every imported entity to an all-new ID allocated strictly from `targetSnapshot.Profile.NextId`, rewrite all intra-document cross-references, and guarantee that no records are dropped or merged without explicit user relinking.
4. **Acyclic Revision Graphs & Ledger Integrity:**
   The merged transaction revision and annotation revision graphs must be verified to be strictly acyclic, with every revision pointing to a valid predecessor and every reference pointing to an existing entity of the expected kind.

## Decisions

### 1. Lineage Fork Detection (`LineageForkDetector`)
We implement `LineageForkDetector` in `Broiler.Fond.Kernel.Storage`:
- Evaluates `targetSnapshot` and `importedSnapshot` before merge:
  - `DivergentBranchGeneration`: Identifies if both snapshots share `ProfileId` and `BranchId` but have divergent generation contents or timestamps.
  - `ConflictingEntityIdentity`: Identifies if identical entity IDs in the same profile have divergent entity kinds or conflicting attributes.
  - `ConflictingRevisionPredecessor`: Identifies if multiple revisions on the same transaction claim the same predecessor revision, indicating a fork in transaction revision history.
- Returns a structured `LineageForkReport` containing all detected fork anomalies.

### 2. Mandatory Reauthentication for Imported and Restored Connections
- In `StoreArchiveCodec.MergeSnapshotsWithIdRemapping`:
  - Every connection imported from `importedSnapshot` transitions its state to `ReauthenticationRequired` if it was in `Connected`, `Authenticating`, `ChallengeRequired`, or `TemporarilyUnavailable`.
  - Inactive states (`Disabled`, `Unconfigured`) are preserved as-is.
- In `ProfileStore.RestoreIntoEmptyProfile`:
  - A restored profile establishes a new writable branch lineage (`RecoveryProvenance = "restored-from-backup"`) and transitions all active connections to `ReauthenticationRequired`.

### 3. Acyclic Revision & Reference Integrity Verification
- After remapping IDs and constructing the merged snapshot, `MergeSnapshotsWithIdRemapping` immediately invokes `ValidateIdentityAndSemanticInvariants(merged)`.
- Validates that:
  - `merged.Profile.NextId > max(id)`.
  - Every entity in `accounts.xml`, `balances.xml`, and `transactions.xml` exists in `ledger.xml`.
  - `IdentitySnapshotValidator.Validate` confirms that the merged entity graph has zero missing references, zero reference kind mismatches, and zero revision cycles.

## Consequences

### Positive
- Prevents silent data corruption or loss when merging backups from divergent devices.
- Guarantees that imported profiles never falsely appear "Connected" without user-entered credentials.
- Retains complete historical provenance for all entities across multi-branch forks.
- Enforces strict BCL-only mathematical correctness and acyclic revision history.

### Negative / Trade-offs
- Users who merge backups from forked branches will see duplicate accounts or transactions remapped with new IDs and will need to review or relink them explicitly.
