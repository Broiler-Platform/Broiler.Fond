# ADR 0125: Storage Migration Guarantees, Downgrade Protection, and Rollback Integrity

- **Status:** Accepted
- **Date:** 2026-10-19
- **Related:** [ADR 0004](0004-profile-envelope-v1.md), [ADR 0005](0005-profile-snapshots-v1.md), [ADR 0087](0087-profile-storage-schemas-preflight-and-calibration.md), [ADR 0104](0104-backup-migration-and-fork-detection-hardening.md), [ADR 0110](0110-payment-storage-extension-and-audit-history.md), [docs/roadmap.md](../roadmap.md#portable-local-store-format), [docs/milestone-7.md](../milestone-7.md#m7-02-comprehensive-storage-migration-guarantees-downgrade-protection--rollback-integrity)

---

## 1. Context and Problem Statement

The Broiler Fond profile store is an authenticated-encrypted container (AES-256-GCM, PBKDF2-SHA256, ZIP framing) holding deterministic UTF-8 XML documents. As the product expanded across Milestones 1 through 6, new modular XML schemas were introduced:
- `bootstrap-v1.xsd` & `store-v1.xsd` (M1: profile, institutions, connections, accounts, balances, ledger)
- `transactions-v1.xsd` (M2: transaction projections, observations, revisions, annotations, splits)
- `payments-v1.xsd` (M4/M5: payment drafts, attempts, state transitions)
- `templates-v1.xsd` (M5: beneficiary address book)
- `purge-intent-v1.xsd` (M1: cryptographic deletion transaction)

For Windows 1.0, storage integrity must be guaranteed against:
1. **Schema forward migration:** Profiles created in earlier releases (M1, M2, M3, M4, M5) must open seamlessly in 1.0 without data loss or re-authentication requirements. Missing optional documents (`transactions`, `payments`, `templates`) must populate transparent empty defaults without failing schema validation.
2. **Downgrade attacks and premature forward-format exposure:** If an older client binary attempts to open an archive written by a future format version or carrying unknown required XML elements, it must fail closed immediately rather than stripping unrecognized documents or corrupting the lineage.
3. **Rollback attacks and file replacement:** An external attacker or file sync service replacing current generation $G$ with an older valid generation $G - k$ could induce silent data loss. The host must track known highest lineage generations and reject unexpected regressions.
4. **Backup and restore cryptographic roundtrip:** User backups must verify 100% digest matching, entity integrity, and reference validity before allowing restoration into live storage.
5. **Strict BCL-only kernel boundary:** All migration, downgrade verification, and hashing operations must use only .NET 10 shared framework APIs.

---

## 2. Decision and Architecture

### 2.1 Storage Migration Engine (`StoreMigrationEngine`)

The kernel implements `StoreMigrationEngine` to validate and safely migrate snapshots across schema versions:
- **Default Document Population:** If an archive lacks `transactions.xml`, `payments.xml`, or `templates.xml` (e.g. created during M1), the migration engine populates valid default empty documents matching the current profile ID, branch ID, and generation.
- **Reference Integrity Audit:** Verifies that all references across documents (`AccountBinding -> Connection`, `Balance -> Observation`, `TransactionRevision -> Observation`, `Split -> Transaction`, `PaymentStateTransition -> PaymentAttempt`) resolve to existing IDs in the ledger.
- **Entity ID Monotonicity Guard:** Asserts that `Profile.NextId > Max(Id)` across every entity in all documents.

### 2.2 Strict Downgrade Guard (`StoreDowngradeGuard`)

Before opening or decrypting an archive:
- The envelope header format version and snapshot format version are evaluated. Format version `1` is normative for Windows 1.0.
- If an envelope declares format version $> 1$, or a manifest specifies an unrecognized schema namespace with `isRequired = true`, the loader throws `StoreDowngradeException`.
- The exception includes actionable details: the detected container version, maximum supported version, and a user message directing the user to upgrade the application.

### 2.3 Rollback Detection (`LineageRollbackGuard`)

To guard against unexpected disk rollback:
- `ProfileStore` and `HostSession` maintain volatile session lineage tracking (`HighestObservedGeneration`).
- If an on-disk profile candidate presents a generation $G_{candidate} < G_{known}$ for the same `ProfileId` and `BranchId`, it is flagged as a potential rollback conflict (`RollbackDetectedException`), refusing silent overwrites.

### 2.4 Cryptographic Backup Verification

Backup exports are verified byte-for-byte before completion:
1. Create candidate backup file.
2. Read candidate back using seekable authenticating reader.
3. Verify every AES-GCM frame tag.
4. Unpack manifest and compute SHA-256 hash of each contained document.
5. Compare against manifest SHA-256 digests.
6. Only return success if 100% of frame tags and document digests match.

---

## 3. Consequences

### Positive
- Guarantees backward compatibility for profiles created since M1.
- Prevents silent data corruption or loss from older software versions opening newer profiles.
- Detects unauthorized rollback or conflicting fork states.
- Zero external dependencies.

### Negative / Trade-offs
- Format version 1 is locked; future breaking schema changes will require formal version increments and dual-generation upgrade paths.
