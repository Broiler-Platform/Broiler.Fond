# ADR 0110: Payment Storage Extension and Audit History in Profile Store

- **Status:** Accepted
- **Date:** 2026-10-19
- **Related:** [ADR 0004](0004-profile-envelope-v1.md), [ADR 0005](0005-profile-snapshots-v1.md), [ADR 0087](0087-profile-storage-schemas-preflight-and-calibration.md), [ADR 0098](0098-transaction-ledger-storage-extension.md), [ADR 0106](0106-payment-domain-draft-revisions-and-sepa-validation.md), [ADR 0108](0108-durable-payment-state-machine-and-attempt-allocation.md), [docs/roadmap.md](../roadmap.md#m4--safe-sepa-money-movement), [docs/milestone-4.md](../milestone-4.md)

---

## 1. Context and Problem Statement

In Milestone 1 and Milestone 2, the portable encrypted profile archive (`.bffond`) stored account structures, bank connections, balance snapshots, and transaction ledgers across canonical XML documents (`profile.xml`, `institutions.xml`, `connections.xml`, `accounts.xml`, `balances.xml`, `ledger.xml`, and `transactions.xml`) plus `manifest.xml`.

In Milestone 4, **Broiler Fond - Finance on Demand** implements safe SEPA money movement. Payment lifecycle management requires durable, tamper-evident persistence for:
1. **Payment drafts (`PaymentDraft`):** Source account binding, creation/update timestamps, and active revision pointers.
2. **Payment draft revisions (`PaymentDraftRevision`):** Append-only immutable parameter sets (beneficiary name/IBAN/BIC, exact decimal amount, remittance, execution mode and date).
3. **Payment attempts (`PaymentAttempt`):** Pre-send committed entities with store-wide unique IDs, fresh locally generated `EndToEndId` references, current payment states, bank order references, return codes, and attached Verification of Payee (VoP) outcomes.
4. **Payment state transitions (`PaymentStateTransition`):** Append-only audit log tracking every state machine transition with reason and bank status code.

To uphold the core principles of the project:
- **No ephemeral state for money movement:** Payment states, attempts, and audit logs are durable domain records.
- **Zero credential persistence:** PINs, TANs, and session tokens must *never* be persisted to disk, archive, or audit logs.
- **Backward compatibility & zero data loss:** Older Milestone 1 (7-document) and Milestone 2 (8-document) archives must load transparently without error or data loss, initializing an empty payment document that upgrades automatically to the 9-document archive upon next save.
- **Deterministic lexical contract & byte-exact round-trip:** Serialized documents use UTF-8 without BOM, LF line endings, invariant culture formatting, and canonical element ordering; deserialization asserts byte-exact equality against re-serialization.
- **Local identity integrity:** All payment drafts, revisions, attempts, and transitions are allocated from the store-wide monotonic 64-bit allocator and tracked in `ledger.xml`.
- **BCL-only boundary:** Relies exclusively on .NET 10 runtime APIs (`System.Xml`, `System.IO.Compression`, `System.Security.Cryptography`) with zero external packages.

---

## 2. Decision and Architecture

### 2.1 Schema Definition (`payments-v1.xsd`)

We embed `payments-v1.xsd` into `Broiler.Fond.Kernel` as an `EmbeddedResource` under logical name `ProfileEnvelope.payments-v1.xsd` within namespace `urn:broiler:fond:profile:1`:
- **`draft`:** Captures `id`, `sourceAccountId`, `sourceAccountBindingId`, `sourceAccountIncarnationId`, `currentRevisionId`, `createdAt`, and `updatedAt`.
- **`revision`:** Captures `id`, `draftId`, optional `predecessorRevisionId`, `revisionNumber`, `timestamp`, `sourceAccountId`, `sourceAccountBindingId`, `sourceAccountIncarnationId`, structured `beneficiary` (`name`, `iban`, optional `bic`), exact decimal `amount`, `currency` (`EUR`), optional structured `remittance` (`unstructured`, `creditorReference`), `executionMode`, and `executionDate`.
- **`attempt`:** Captures `id`, `draftId`, `draftRevisionId`, `sourceAccountId`, `sourceAccountBindingId`, `sourceAccountIncarnationId`, `endToEndId`, `state`, optional `vopResult` (`status`, `checkedAt`, optional `matchedName`, optional `reasonCode`, optional `warningMessage`, `userOverride`), optional `bankReference`, optional `responseCode`, optional `statusDescription`, `createdAt`, and `updatedAt`.
- **`transition`:** Captures `id`, `attemptId`, `fromState`, `toState`, `timestamp`, `reason`, and optional `bankCode`.
- **Root Attributes:** `version="1"`, `profileId` (UUID), `branchId` (UUID), and `generation` (positive integer).

### 2.2 Domain Identifiers Extension (`LocalEntityKind`)

We extend `LocalEntityKind` in `Domain/Identifiers/IdentitySnapshot.cs` with the four payment entity kinds:
- `PaymentDraft`
- `PaymentDraftRevision` (supersedes predecessor revision, checked for revision cycles in `IdentitySnapshotValidator`)
- `PaymentAttempt` (references `PaymentDraft` and `PaymentDraftRevision`)
- `PaymentStateTransition` (references `PaymentAttempt`)

### 2.3 Storage Archive Codec Integration (`StoreArchiveCodec.cs`)

1. **DTOs and Snapshot Container:**
   - Define `StorePaymentDraftEntry`, `StorePaymentDraftRevisionEntry`, `StorePaymentVoPResultEntry`, `StorePaymentAttemptEntry`, `StorePaymentStateTransitionEntry`, and `StorePaymentsDocument`.
   - Extend `StoreSnapshot` with optional `StorePaymentsDocument? Payments = null` and a backward-compatible `PaymentsOrDefault` property.
2. **Canonical Entry Ordering & Document Names:**
   - Retain `LegacyStoreDocumentNames` (7 entries: 6 documents + `manifest.xml`).
   - Define `StoreDocumentNamesV2` (8 entries: 7 documents including `transactions.xml` + `manifest.xml`).
   - Update `StoreDocumentNames` to 9 entries:
     1. `profile.xml`
     2. `institutions.xml`
     3. `connections.xml`
     4. `accounts.xml`
     5. `balances.xml`
     6. `ledger.xml`
     7. `transactions.xml`
     8. `payments.xml`
     9. `manifest.xml`
3. **Serialization & Deserialization:**
   - `SerializePayments`: Deterministically writes `<payments>` using `XmlWriter` with UTF-8, LF line endings, invariant culture, and canonical element ordering.
   - `ParseAndValidatePayments`: Validates input against embedded `payments-v1.xsd`, enforces `DtdProcessing.Prohibit`, depth bounds ($\le 8$), and verifies lineage (`ProfileId`, `BranchId`, `Generation`).
   - `ExtractAndValidateSnapshotFromZipStream`: Extracts `payments.xml` if present, validates it, and asserts byte-exact re-serialization match (`pmBytes.SequenceEqual(SerializePayments(doc))`). If absent, it supplies an empty `StorePaymentsDocument`.
   - `ValidateManifest`: Adapts dynamically to validate 6 document entries (M1 legacy), 7 document entries (M2), or 8 document entries (M4 modern), asserting document ordering, exact byte counts, and SHA-256 digests.
4. **Identity & Semantic Invariants:**
   - `ValidateIdentityAndSemanticInvariants`: Asserts that every draft, revision, attempt, and transition ID exists in `ledger.xml` with the correct `LocalEntityKind`.
   - Asserts that foreign references (`draftId`, `draftRevisionId`, `attemptId`, `sourceAccountId`, `sourceAccountBindingId`, `sourceAccountIncarnationId`) resolve to declared entities.
5. **Cross-Snapshot ID Remapping & Merge:**
   - `MergeSnapshotsWithIdRemapping`: Remaps all payment entity IDs, predecessor pointers, and cross-references into the target profile's monotonic `NextId` space.

---

## 3. Invariants and Guarantees

1. **Backward Compatibility:** M1 and M2 profiles load seamlessly without modification; new commits automatically write the 9-document archive format.
2. **Byte-Exact Canonical Round-Trip:** Every deserialized payment document is guaranteed to re-serialize to the exact input bytes.
3. **Zero Credential Exposure:** PINs and TANs are absent from domain models and schema definitions.
4. **Crash Resilience & Atomic Promotion:** New generations are written to candidate files and validated before renaming; privacy purge purges superseded generations and quarantine markers.
5. **Tamper Evidence:** Tampered document bytes or modified manifest entries fail closed via SHA-256 digest validation.
6. **Fork Conflict Detection:** Forked payment attempts or lineages are detected and isolated.

---

## 4. Verification

Implemented in `tests/Broiler.Fond.Kernel.Tests/Payments/PaymentStorageTests.cs`:
- Round-trip serialization and byte-exact verification across all payment draft, revision, attempt, and transition fields.
- Full 9-document store ZIP creation, entry order verification, and extraction.
- Backward compatibility: migration from 7-document (M1) and 8-document (M2) archives to 9-document (M4) format on save.
- Hostile schema injection, illegal element order, negative amounts, invalid dates, and non-canonical whitespace rejection.
- Identity and semantic invariants: missing ledger entries, entity kind mismatches, duplicate IDs, and invalid references.
- Manifest tamper detection: altered payment document digests and unlisted entries.
- Cross-snapshot ID remapping and merge with payment entities.
- Multi-generation persistence lifecycle with `ProfileStore.CreateNew`, `Save`, `Open`, and `ExecutePrivacyPurge`.
