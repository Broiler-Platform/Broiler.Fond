# ADR 0090: Ambiguous-Rediscovery Account Relinking and Provenance-Preserving Pruning

- **Status:** Accepted
- **Date:** 2026-10-16
- **Extends:** [ADR 0004](0004-profile-envelope-v1.md), [ADR 0005](0005-profile-snapshots-v1.md), [ADR 0006](0006-local-identity-allocation.md), [ADR 0007](0007-account-source-rediscovery.md), [ADR 0088](0088-production-profile-store-lifecycle.md)
- **Human security review:** Pending human storage/security review before host UI integration

## Context

Milestone 1 items **M1-03 (Identity & Relinking)** and **M1-05 (FinTS Slice)** define collision-free local identity allocation and exact account rediscovery:

1. **Quarantined Candidates and Separate Allocation**:
   - [ADR 0007](0007-account-source-rediscovery.md) and [ADR 0084](0084-fints-read-ingestion-and-store-binding.md) establish that when account identifiers conflict, change, or arrive from a new connection or endpoint, rediscovery cannot silently conflate them with an existing local account.
   - The ingestion pipeline allocates a separate temporary account identity (`QuarantinedCandidate`) with an associated incarnation and binding (`AccountBindingState.Quarantined`), ensuring that ambiguous candidates remain visible for user review without corrupting existing account totals.
2. **User-Reviewed Account Relinking**:
   - After inspecting the quarantined candidate, a user may confirm that it represents an existing logical account in their profile (e.g. an updated IBAN, a new connection to the same bank relationship, or an unlinked locator).
   - The product roadmap ([docs/roadmap.md lines 607–627](../roadmap.md#collision-free-local-identity-and-source-ambiguity)) mandates that:
     - Relinking changes only bindings: local account identity, observations, annotations, history, and payment attempts remain stable.
     - Every balance snapshot, source observation, and payment attempt permanently retains the `AccountBindingId` and `AccountIncarnationId` that were active at its origin. Relinking never rewrites historical balance provenance.
     - When pruning an auto-generated temporary candidate account that has no other bindings or historical observations, superseded profile generations that still reference the pruned temporary ID must be deleted via a verified privacy purge (`ExecutePrivacyPurge`), while preserving monotonic identity allocation (`Profile.NextId` never moves backwards).

## Decision

### 1. Deterministic Relinking Planner (`AccountRelinkingPlanner.PlanRelink`)

- `AccountRelinkingPlanner.PlanRelink(StoreSnapshot snapshot, ulong bindingId, ulong targetAccountId, bool pruneOrphanedAccount = true, DateTimeOffset? savedAt = null)` performs pure, deterministic validation and transformation over a store snapshot:
  1. **Preflight Invariant Checks**:
     - Validates the input snapshot using `StoreArchiveCodec.ValidateIdentityAndSemanticInvariants`.
     - Validates that `bindingId` and `targetAccountId` exist in the snapshot.
     - Rejects if `bindingId` is already linked to `targetAccountId` with `State == "Active"` (`InvalidOperationException`).
  2. **Currency Compatibility**:
     - Validates that the binding's currency and target account currency are compatible (ignoring whitespace and case); incompatible currencies fail closed with `InvalidOperationException`.
  3. **Target Incarnation Selection**:
     - Identifies incarnations for `targetAccountId`; if none exist, fails closed.
     - Selects the active non-closed incarnation (or the latest incarnation if all are closed).
  4. **Duplicate Active Binding Conflict Guard**:
     - Enforces that no other active binding on the same connection already links to `targetAccountId` with a matching locator (matching IBAN or domestic account number and bank code).
  5. **Binding & Capability Update**:
     - Updates the binding to `AccountId = targetAccountId`, `IncarnationId = targetIncarnation.Id`, and `State = "Active"`.
     - Migrates capabilities under `(targetBinding.ConnectionId, previousAccountId)` to `(targetBinding.ConnectionId, targetAccountId)`, deduplicating if the target account already possesses the capability.
  6. **Permanent Provenance Preservation**:
     - Balance snapshots in `balances.xml` are not rewritten. Every historical balance snapshot permanently retains the `AccountBindingId`, `AccountIncarnationId`, and `AccountId` that were active at its origin.
  7. **Orphaned Candidate Account Pruning**:
     - If `pruneOrphanedAccount` is `true`, the previous account is evaluated for pruning:
       - If the previous account has any other bindings or any historical balance entries in `balances.xml`, it is an established account with durable history and is retained (`PrunedAccountId = null`).
       - If the previous account has no remaining bindings and no balance entries (it was an unconfirmed temporary candidate shell), `previousAccountId` and all of its associated incarnations and capabilities are removed from `accounts.xml`.
       - Pruned entity IDs are excluded from `ledger.xml`, while the relinked binding's ledger references are updated to `[(targetIncarnation.Id, "AccountIncarnation"), (targetBinding.ConnectionId, "Connection")]`.
  8. **Strict Monotonicity & Validation**:
     - `Profile.NextId` is strictly preserved and never moves backwards; deleted IDs are never reused.
     - The updated snapshot is validated against `StoreArchiveCodec.ValidateIdentityAndSemanticInvariants` before returning `AccountRelinkingResult`.

### 2. Profile Store Integration (`IProfileStore.RelinkAccountBinding`)

- `IProfileStore` exposes `RelinkAccountBinding(ulong bindingId, ulong targetAccountId, string passphrase, bool pruneOrphanedAccount = true, DateTimeOffset? savedAt = null, string? note = null)`.
- `ProfileStore.RelinkAccountBinding` executes under the exclusive single-writer lock (`_syncRoot`), validating `EnsureWritableReady()`:
  - Calls `AccountRelinkingPlanner.PlanRelink`.
  - **Pruned candidate path**: If `planned.PrunedAccountId.HasValue`, executes `ExecutePrivacyPurge(planned.UpdatedSnapshot, passphrase, timestamp, purgeNote)` ([ADR 0088](0088-production-profile-store-lifecycle.md)). This commits the new generation, writes an authenticated `purge.intent.bffond` and `purge.pending` marker, durably deletes all superseded `generation-*.bffond` files containing the pruned temporary ID, verifies the deletion by directory re-enumeration, and leaves exactly one generation file on disk.
  - **Non-pruned path**: If `!planned.PrunedAccountId.HasValue` (`pruneOrphanedAccount = false`, or previous account has other bindings/balances), commits via standard atomic `Save(planned.UpdatedSnapshot, timestamp)`, retaining both the new generation and the immediately preceding validated generation.
  - Reopening the store confirms clean startup in `ProfileStoreOpenState.Ready` with initialized `IdentityLedger`.

## Consequences & Verification

- `AccountRelinkingTests` adds 57 executable checks covering:
  - Relinking a quarantined binding to an existing active account with automatic pruning of the temporary candidate shell via `ExecutePrivacyPurge` (verifying binding update to Active, capability migration, ledger reference rewrites, removal of pruned account/incarnation from disk, directory retention with 1 generation file, and clean store reopening).
  - Relinking with `pruneOrphanedAccount = false` using standard `Save` (retaining both generations and keeping previous account intact).
  - Relinking when the previous account has another binding (preventing pruning).
  - Relinking when the previous account has historical balances (verifying permanent provenance retention on `StoreBalanceEntry` and preventing pruning).
  - Fail-closed rejections: non-existent binding ID, non-existent target account ID, already active on target account, incompatible currencies (e.g. EUR vs USD), duplicate active binding conflict on target account, target account with no incarnations, read-only store invocation, and wrong passphrase during privacy purge.
