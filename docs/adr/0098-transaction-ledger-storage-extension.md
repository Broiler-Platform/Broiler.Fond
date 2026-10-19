# ADR 0098: Transaction Ledger Storage Extension

- **Status:** Accepted
- **Date:** 2026-10-18
- **Related:** [ADR 0004](0004-profile-envelope-v1.md), [ADR 0005](0005-profile-snapshots-v1.md), [ADR 0087](0087-profile-storage-schemas-preflight-and-calibration.md), [ADR 0094](0094-immutable-transaction-domain-models.md), [ADR 0097](0097-statement-pagination-range-queries-and-overlap-sync.md), [docs/roadmap.md](../roadmap.md#m2--transactions-and-account-details), [docs/milestone-2.md](../milestone-2.md)

---

## 1. Context and Problem Statement

In Milestone 1, the portable encrypted profile archive (`.bffond`) stored account structures, bank connections, and balance snapshots across six canonical XML documents (`profile.xml`, `institutions.xml`, `connections.xml`, `accounts.xml`, `balances.xml`, and `ledger.xml`) plus `manifest.xml`.

In Milestone 2, **Broiler Fond - Finance on Demand** introduces transaction synchronization. The storage layer must persist:
- Immutable bank statement observations (`TransactionObservationEntry`)
- Economic transaction entities (`TransactionEntry`)
- Bank-reported transaction revisions (`TransactionRevisionEntry`)
- User-owned annotations, tags, categories, and notes (`AnnotationRevisionEntry`)
- Non-destructive pending-to-booked links (`PendingToBookedLinkEntry`)
- Statement sync checkpoints (`StatementSyncCheckpointEntry`)

To satisfy the core product principles:
1. **Separation of bank facts and user annotations:** Bank facts and user annotations remain strictly partitioned into distinct entities and revision chains.
2. **Backward compatibility & zero data loss:** Older Milestone 1 profile archives containing 7 entries (6 non-manifest documents) must load seamlessly without error or data loss, initializing an empty transaction ledger that upgrades automatically to the 8-document format upon next save.
3. **Deterministic lexical contract & byte-exact round-trip:** Serialized documents use UTF-8 without BOM, LF line endings, invariant culture formatting, and canonical attribute ordering; every read asserts byte-exact equality against re-serialization.
4. **Local identity integrity:** All transaction entity identifiers are allocated from the store-wide monotonic 64-bit allocator and verified against `ledger.xml` with acyclic revision graphs.
5. **Hostile input rejection & tamper detection:** Schema violations, inverted checkpoint dates, lineage mismatch, missing ledger entities, or altered entry digests fail closed.
6. **BCL-only boundary:** Implementation relies exclusively on .NET 10 runtime APIs (`System.Xml`, `System.IO.Compression`, `System.Security.Cryptography`) with zero third-party dependencies.

---

## 2. Decision and Architecture

### 2.1 Schema Definition ([`transactions-v1.xsd`](file:///d:/Broiler.Fond/src/Broiler.Fond.Kernel/Storage/Schemas/transactions-v1.xsd))

We embed `transactions-v1.xsd` into `Broiler.Fond.Kernel` as an `EmbeddedResource` under logical name `ProfileEnvelope.transactions-v1.xsd` within namespace `urn:broiler:fond:profile:1`:
- **`observation`:** Captures `id`, `accountBindingId`, `statementFormat`, optional `statementNumber`, optional `pageToken`, and `retrievedAt`.
- **`transaction`:** Captures `id`, `accountId`, `accountIncarnationId`, `status`, and `kind`.
- **`revision`:** Captures `id`, `transactionId`, `observationId`, `accountId`, `accountIncarnationId`, `accountBindingId`, `status`, `kind`, `amount`, `currency`, `isCredit`, `bookingDate`, optional `valueDate`, optional `bankTimestamp`, `retrievedAt`, optional structured `counterparty` (`name`, `iban`, `bic`), optional structured `remittance` (`unstructuredText`, `endToEndId`, `mandateId`, `creditorId`, `customerReference`, `bankReference`, `purposeCode`), optional `returnReasonCode`, and optional `rawBankCode`.
- **`annotation`:** Captures `id`, `transactionId`, `revisionTimestamp`, optional `userNote`, optional `category`, and $\le 64$ `tag` elements.
- **`link`:** Captures `pendingTransactionId`, `bookedTransactionId`, `linkedAt`, and `matchCriterion`.
- **`checkpoint`:** Captures `accountId`, `accountIncarnationId`, `accountBindingId`, `windowStart`, `windowEnd`, `lastSyncTimestamp`, optional `lastContinuationToken`, `isComplete`, and `observedTransactionCount`.
- **Root Attributes:** `version="1"`, `profileId` (UUID), `branchId` (UUID), and `generation` (positive integer).

### 2.2 Storage Archive Codec Integration ([`StoreArchiveCodec.cs`](file:///d:/Broiler.Fond/src/Broiler.Fond.Kernel/Storage/StoreArchiveCodec.cs))

1. **DTOs and Snapshot Container:**
   - Introduced `StoreTransactionObservationEntry`, `StoreTransactionEntry`, `StoreTransactionRevisionEntry`, `StoreAnnotationRevisionEntry`, `StorePendingToBookedLinkEntry`, `StoreStatementSyncCheckpointEntry`, and `StoreTransactionsDocument`.
   - Extended `StoreSnapshot` with optional `StoreTransactionsDocument? Transactions = null` and a backward-compatible `TransactionsOrDefault` property.
2. **Canonical Entry Ordering & Document Names:**
   - Defined `LegacyStoreDocumentNames` (7 entries: 6 documents + `manifest.xml`).
   - Defined `StoreDocumentNames` (8 entries: 7 documents including `transactions.xml` + `manifest.xml`).
3. **Serialization & Deserialization:**
   - `SerializeTransactions`: Deterministically writes `<transactions>` using `XmlWriter` with UTF-8, LF line endings, and canonical element order.
   - `ParseAndValidateTransactions`: Validates input against embedded `transactions-v1.xsd`, enforces `DtdProcessing.Prohibit`, checks date invariants (`windowStart <= windowEnd`), and verifies lineage.
   - `ExtractAndValidateSnapshotFromZipStream`: Extracts `transactions.xml` if present, validates it, and asserts byte-exact re-serialization match (`txBytes.SequenceEqual(SerializeTransactions(doc))`). If absent (legacy v1 archive), it supplies an empty `StoreTransactionsDocument`.
   - `ValidateManifest`: Dynamically adapts to expect 6 document entries for legacy archives or 7 document entries for modern archives, asserting entry ordering, byte counts, and SHA-256 digests.
4. **Identity & Semantic Validation:**
   - `ValidateIdentityAndSemanticInvariants`: Asserts that every transaction, observation, revision, and annotation ID exists in `ledger.xml` with the correct `LocalEntityKind` (`Observation`, `Transaction`, `TransactionRevision`, `AnnotationRevision`).
   - Validates that cross-document references (`accountId`, `accountIncarnationId`, `accountBindingId`, `transactionId`, `observationId`) resolve to valid entities.
5. **Cross-Snapshot ID Remapping & Merge:**
   - `MergeSnapshotsWithIdRemapping`: Remaps all transaction entity IDs and cross-references into the target profile's `NextId` space, updating checkpoints and links without ID collisions.

---

## 3. Invariants and Guarantees

1. **Backward Compatibility:** Milestone 1 profiles load without modification or data loss; new commits automatically write the 8-document format.
2. **Byte-Exact Canonical Round-Trip:** Every deserialized document is guaranteed to re-serialize to the exact input bytes.
3. **Zero Floating-Point Money:** All amounts are parsed and formatted as exact decimal strings with explicit ISO currencies.
4. **Crash Resilience & Atomic Promotion:** New profile generations write to temporary candidates and are verified before renaming; privacy purge purges superseded generations and quarantine markers.
5. **Tamper Evidence:** Tampered document bytes or modified manifest entries fail closed via SHA-256 digest validation.

---

## 4. Verification

Implemented in [`tests/Broiler.Fond.Kernel.Tests/Storage/TransactionLedgerStorageTests.cs`](file:///d:/Broiler.Fond/tests/Broiler.Fond.Kernel.Tests/Storage/TransactionLedgerStorageTests.cs) (124 executable checks):
- `VerifyTransactionsSerializationAndDeserializationRoundTrip`: Round-trip serialization and byte-exact verification across all fields, structured counterparty and remittance data, return reason codes, and checkpoints.
- `VerifyFullStoreSnapshotWithTransactionsRoundTrip`: Creation of 8-document store ZIP, entry order validation, and extraction.
- `VerifyLegacyStoreBackwardCompatibilityAndMigration`: Successful parsing of 7-document legacy v1 archive, initialization of default empty transactions, and auto-upgrade to 8 documents on save.
- `VerifyHostileInputAndSchemaViolations`: Rejection of inverted checkpoint date ranges, lineage mismatches (profileId, generation), schema violations (unknown element, missing attribute), and non-canonical whitespace.
- `VerifyIdentityAndSemanticInvariants`: Detection and rejection of transaction entities missing from `ledger.xml`, entity kind mismatches, and duplicate entity IDs.
- `VerifyManifestTamperRejection`: Rejection of tampered `transactions.xml` SHA-256 digests in manifest and extra unlisted ZIP entries.
- `VerifySnapshotMergeWithTransactionsRemapping`: Verification that snapshot merge remaps all transaction entity IDs and cross-references into target ID space ($\ge 500$).
- `VerifyProductionProfileStoreLifecycleWithTransactions`: Multi-generation persistence across `ProfileStore.CreateNew`, `Save`, `Open`, and `ExecutePrivacyPurge`.
