# ADR 0089: Connection Removal, Cached-Data Purge, and Encrypted Local Diagnostic Persistence

- **Status:** Accepted
- **Date:** 2026-10-16
- **Extends:** [ADR 0004](0004-profile-envelope-v1.md), [ADR 0005](0005-profile-snapshots-v1.md), [ADR 0009](0009-read-only-simulator-and-redacted-diagnostics.md), [ADR 0088](0088-production-profile-store-lifecycle.md)
- **Human security review:** Pending human storage/security review before enabling host UI persistence

## Context

Milestone 1 item **M1-08 (Disconnect, purge, diagnostics, and support preview)** requires deterministic kernel contracts for two closely related privacy and support boundaries:

1. **Bank Connection Removal and Cached-Data Disposition**:
   - Users removing a bank connection must be able to choose between:
     - **Retain cached accounts and balances** (`ConnectionRemovalDataDisposition.RetainCachedAccountsAndBalances`): Removes the connection's operational state (`connections.xml`) and active capabilities (`accounts.xml`), transitions its account bindings to `Inactive` so cached accounts and balances remain viewable offline, and purges superseded profile generations that still contain the removed connection's state.
     - **Delete cached accounts and purge** (`ConnectionRemovalDataDisposition.DeleteCachedAccountsAndPurge`): Removes the connection, capabilities, bindings, observations, balances, and any incarnations, accounts, and institution exclusively owned by the removed connection (while preserving shared accounts, incarnations, or institutions still referenced by another connection in the profile), removes purged entity IDs from `ledger.xml` without rolling back `Profile.NextId`, and purges all superseded profile generations from disk.
2. **Opt-In Encrypted Local Diagnostic Persistence and Preview-Bound Support Export**:
   - [ADR 0009](0009-read-only-simulator-and-redacted-diagnostics.md) established the in-memory `LocalDiagnosticBuffer` and redacted `DiagnosticSupportPreview`.
   - M1-08 requires an opt-in on-device encrypted diagnostic store (`EncryptedLocalDiagnosticStore`) that persists only predefined `LocalDiagnosticCode` and `LocalDiagnosticOutcome` enum values (plus UTC timestamps) inside a v1 AES-256-GCM envelope (`diagnostics.bffond`) bound to the active `ProfileMasterKeySession`, deletes the persisted file immediately when diagnostics are disabled or purged, and provides a local-file-only support preview export gate (`ExportReviewedSupportPreview`) that verifies the caller-confirmed SHA-256 digest of the exact reviewed `DiagnosticSupportPreview` JSON bytes.

## Decision

### 1. Deterministic Connection Removal Planner (`ConnectionRemovalPlanner` & `IProfileStore.RemoveConnection`)

- `ConnectionRemovalDataDisposition` defines two explicit dispositions:
  1. `RetainCachedAccountsAndBalances`:
     - Removes the target `StoreConnectionEntry` from `connections.xml` and all `StoreCapabilityEntry` records for `connectionId` from `accounts.xml`.
     - Transitions every `StoreBindingEntry` with `ConnectionId == connectionId` to `State = "Inactive"`.
     - Preserves all `StoreAccountEntry`, `StoreIncarnationEntry`, `StoreBalanceEntry`, `StoreInstitutionEntry`, and `StoreLedgerRecord` entries, as well as `Profile.NextId`.
     - Validates the resulting `StoreSnapshot` against `StoreArchiveCodec.ValidateIdentityAndSemanticInvariants`.
  2. `DeleteCachedAccountsAndPurge`:
     - Removes the target `StoreConnectionEntry` and its `StoreCapabilityEntry` records.
     - Removes all `StoreBindingEntry` records with `ConnectionId == connectionId` and all `StoreBalanceEntry` records referencing those bindings (along with their exclusive `ObservationId` values).
     - Removes `StoreIncarnationEntry` and `StoreAccountEntry` records that were associated with `connectionId` and are no longer referenced by any remaining binding, capability, incarnation, or balance in the profile—preserving shared accounts and incarnations still bound to other connections.
     - When `removeOrphanedInstitution` is `true` (default), removes the connection's `StoreInstitutionEntry` if no remaining connection or binding references it.
     - Removes purged entity IDs from `ledger.xml` (and filters references to purged IDs) while preserving `Profile.NextId` so deleted local entity IDs are never reused.
     - Validates the resulting `StoreSnapshot` against `StoreArchiveCodec.ValidateIdentityAndSemanticInvariants`.
- `IProfileStore.RemoveConnection` plans the removal over `CurrentSnapshot` via `ConnectionRemovalPlanner.PlanRemoval` and commits the resulting snapshot via `ExecutePrivacyPurge` (`ADR 0088`), ensuring that all superseded `generation-*.bffond` and `profile.new-*.tmp` files are durably deleted and verified by directory re-enumeration before returning `ConnectionRemovalResult` (`ExternalCopyDisclosureRequired = true`).

### 2. Opt-In Encrypted Local Diagnostic Store (`EncryptedLocalDiagnosticStore`)

- `ILocalDiagnosticSink` is promoted to a public kernel contract implemented by both `LocalDiagnosticBuffer` (in-memory only) and `EncryptedLocalDiagnosticStore` (in-memory ring buffer + opt-in encrypted disk persistence).
- **Default-off & immediate purge on disable**:
  - `EncryptedLocalDiagnosticStore` defaults to `enabled: false`. While disabled, `Record` is a no-op (after validating enum bounds) and `SaveToDisk` ensures no `diagnostics.bffond` or `diagnostics.new-*.tmp` files remain in the directory.
  - Calling `SetEnabled(false)` or `PurgeFromDisk()` immediately clears in-memory events and deletes `diagnostics.bffond` and any `diagnostics.new-*.tmp` candidate files.
- **Encrypted envelope persistence (`SaveToDisk` / `TryLoadFromDisk`)**:
  - `SaveToDisk(ProfileMasterKeySession session)` serializes `{ "schemaVersion": 1, "droppedEvents": <ulong>, "events": [{ "code": "<enum>", "outcome": "<enum>", "timestamp": "<UTC ISO-8601>" }] }`, generates a fresh 96-byte `BFONDPRF` v1 header bound to `session.ProfileSalt` and `session.Iterations` with fresh CSPRNG `snapshotSalt` and `noncePrefix`, writes and flushes `diagnostics.new-{32 hex}.tmp`, verifies all frames via `SeekableAuthenticatedProfileStream.OpenWithSession`, and atomically promotes to `diagnostics.bffond`.
  - `TryLoadFromDisk(ProfileMasterKeySession session)` authenticates all AES-256-GCM frames via `SeekableAuthenticatedProfileStream.OpenWithSession`, enforces a 256 KiB payload ceiling (`MaxDiagnosticPayloadBytes`), and strictly validates the JSON schema: only `schemaVersion == 1`, `droppedEvents`, and event objects containing the exact three properties `code`, `outcome`, and `timestamp` with defined `LocalDiagnosticCode` and `LocalDiagnosticOutcome` enum names are accepted. Any unexpected JSON property (such as free-text messages) or unknown enum value fails closed with `InvalidDataException`.
- **Preview-bound local support export (`ExportReviewedSupportPreview`)**:
  - `DiagnosticSupportPreview` computes and exposes `PreviewSha256` (the 64-character lowercase SHA-256 hex digest of the exact UTF-8 `Json` string).
  - `EncryptedLocalDiagnosticStore.ExportReviewedSupportPreview(DiagnosticSupportPreview preview, string confirmedPreviewSha256, string destinationFilePath)` recomputes the UTF-8 SHA-256 of `preview.Json`, verifies that it matches both `preview.PreviewSha256` and the caller-supplied `confirmedPreviewSha256`, and writes the exact UTF-8 bytes to `destinationFilePath` with zero network upload methods.

## Consequences & Verification

- `DisconnectAndDiagnosticPersistenceTests` adds 65 executable checks covering `RetainCachedAccountsAndBalances` and `DeleteCachedAccountsAndPurge` (both single-connection and multi-connection shared-account/shared-institution cases) with on-disk `ExecutePrivacyPurge` verification, plus `EncryptedLocalDiagnosticStore` opt-in lifecycle, raw ciphertext inspection, wrong-key and tampered-ciphertext rejection, forbidden JSON property rejection, `SetEnabled(false)` / `PurgeFromDisk()` erasure, and `ExportReviewedSupportPreview` SHA-256 confirmation enforcement.
