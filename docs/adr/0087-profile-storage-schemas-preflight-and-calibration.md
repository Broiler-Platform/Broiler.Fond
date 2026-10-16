# ADR 0087: Profile Storage Schemas, Bounded ZIP Preflight, and KDF Calibration

- **Status:** Accepted
- **Date:** 2026-10-15
- **Extends:** [ADR 0004](0004-profile-envelope-v1.md), [ADR 0005](0005-profile-snapshots-v1.md)
- **Human security review:** Completed candidate specification; live disk persistence remains gated on M1-02 lifecycle implementation

## Context

[ADR 0004](0004-profile-envelope-v1.md) and [ADR 0005](0005-profile-snapshots-v1.md) established the 96-byte framed AES-256-GCM envelope candidate and a minimal `bootstrap-v1` interoperability document set. However, production persistence requires complete schemas covering the mature Milestone 1 domain graph:
1. **Financial Document Set (`store-v1`)**: Extending beyond bootstrap to capture institutions, connection operational states, account hierarchies, lossless locators, balance snapshots, and identity ledger records.
2. **Branch Ancestry & Recovery Provenance**: Durably recording parent branch lineage and recovery provenance metadata to enable safe roll-forward branching and restoration from superseded generations.
3. **Purge-Intent & Passphrase Rotation Schema (`purge-intent-v1`)**: Providing an authenticated intent record binding replacement generation, operation types, and exact target file inventories before initiating privacy purges or passphrase rotations.
4. **Production Bounded ZIP Preflight**: Enforcing hostile-input memory, expansion, and structural validation across raw bytes before constructing `System.IO.Compression.ZipArchive` objects.
5. **Supported-Floor KDF Calibration Protocol**: Defining the host qualification runner, measurement metrics, and selection algorithm to calibrate PBKDF2 iterations within the 600,000–5,000,000 format bounds.

## Decision

### 1. Store-v1 Document Set Specification

The archive payload consists of exactly seven entries in both local-record and central-directory order:
1. `profile.xml`
2. `institutions.xml`
3. `connections.xml`
4. `accounts.xml`
5. `balances.xml`
6. `ledger.xml`
7. `manifest.xml`

All documents use XML namespace `urn:broiler:fond:profile:1` and schema version `1`, adhering to strict lexical rules: UTF-8 with no BOM, declaration `<?xml version="1.0" encoding="utf-8"?>`, no extraneous whitespace, LF newlines, double quotes, and invariant-culture scalar formatting.

#### Document Definitions

- **`profile.xml`**:
  - Root attributes: `xmlns`, `version="1"`, `profileId` (canonical UUID `D`), `branchId` (canonical UUID `D`), `parentBranchId` (optional UUID), `recoveryProvenance` (optional string, <= 256 scalars), `generation` (positive uint64), `nextId` (positive uint64), `savedAt` (UTC ISO 8601).
  - Child elements: `<label>` (<= 256 scalars), `<note>` (<= 1,024 scalars).
- **`institutions.xml`**:
  - Encapsulates manually configured institutions (`<institution id="...">`).
  - Elements: `<displayName>`, `<endpoint>` (canonical absolute HTTPS URL), optional `<bankCode>`, optional `<bic>`, `<verificationState>` (`Unconfirmed`, `Confirmed`, `Quarantined`).
- **`connections.xml`**:
  - Encapsulates bank connections (`<connection id="..." institutionId="...">`).
  - Elements: `<userId>`, optional `<customerId>`, `<state>` (`BankConnectionState`), optional `<systemId>`, `<systemStatus>` (`FinTsCustomerSystemStatus`), `<nextClientMessageNumber>` (1..9999), `<isSynchronized>` (boolean), optional `<lastSuccessfulContact>`, optional `<lastAttemptedContact>`, `<connectorVersion>`.
  - Credentials (PIN/TAN) are strictly excluded.
- **`accounts.xml`**:
  - Encapsulates multi-tier account models:
    - `<account id="...">`: `<displayName>`, `<currency>`.
    - `<incarnation id="..." accountId="...">`: `<lifetimeEvidence>` (`Unspecified`, `Active`, `Closed`, `Reopened`).
    - `<binding id="..." accountId="..." incarnationId="..." institutionId="..." connectionId="...">`: `<state>` (`Active`, `Inactive`, `Quarantined`), `<locator>` with `<iban>`, `<domesticAccountNumber>`, `<domesticBankCode>`, `<subaccount>`, `<currency>`, and zero or more multiset `<bankIdentifier>` entries.
    - `<capability connectionId="..." accountId="..." operationCode="..." operationVersion="..." status="...">`: optional `<advertisedLimit>`.
- **`balances.xml`**:
  - Encapsulates immutable balance snapshots (`<balance id="..." observationId="..." accountId="..." incarnationId="..." bindingId="...">`).
  - Elements: `<kind>` (`Booked`, `Available`, `CreditLine`, `Other`, `Unknown`), optional `<amount>` (exact decimal), optional `<currency>`, optional `<bankTimestamp>`, `<retrievedAt>`, `<isStale>`, optional `<sourceLabel>`.
- **`ledger.xml`**:
  - Encapsulates identity ledger inventory records (`<record id="..." kind="...">`).
  - Elements: optional `<supersedes id="..." />`, zero or more `<reference targetId="..." expectedKind="..." />`.
- **`manifest.xml`**:
  - Lists documents 1 through 6 in canonical order.
  - Attributes per entry: `name`, `schema="1"`, `bytes` (exact uncompressed byte count), `sha256` (64 lowercase hex digits).

### 2. Purge-Intent and Passphrase Rotation Schema

Defined in `purge-intent-v1.xsd` under namespace `urn:broiler:fond:purge:1`:
- Root `<purgeIntent>` attributes: `xmlns`, `version="1"`, `profileId`, `branchId`, `generation`, `operationType` (`PrivacyPurge`, `PassphraseRotation`, `WholeProfileErasure`), `status` (`Pending`, `Committed`, `Completed`, `Aborted`), `createdAt`.
- Child elements:
  - Optional `<replacementGeneration>` (positive uint64; omitted on whole profile erasure).
  - `<targetInventory>` containing one or more `<targetFile name="..." sha256="..." />`.
  - Optional `<note>`.

### 3. Production Bounded ZIP Preflight

Implemented in `ZipPreflight`:
- Directly validates the byte stream before any constructor call to `ZipArchive`.
- Enforces strict constraints:
  - End of Central Directory (EOCD) must end at the exact last byte of payload with zero archive comment.
  - Multi-disk archives, extra fields, entry comments, ZIP64 extensions, and encryption (flag bit 0) are unconditionally forbidden.
  - Permitted compression methods: 0 (Stored) and 8 (Deflate) only.
  - Entry names must be UTF-8/ASCII without directory separators (`/` or `\`), traversal (`..`), or duplicates.
  - Expansion ratio ceiling: maximum 128:1 against `max(1, compressedSize)`.
  - Sequential contiguity: local file headers, compressed data, and optional data descriptors must occupy the archive space contiguously without gaps, prefix, suffix, or hidden bytes before the central directory.

### 4. Supported-Floor KDF Calibration Protocol

Implemented in `SupportedFloorKdfCalibration`:
- Profiles host machine context: OS description, architecture, runtime version, processor count.
- Executes warmup iterations followed by statistical multi-sample timing (default 30 iterations).
- Computes min, max, mean, median (50th percentile), and p95 (95th percentile).
- Validates that iterations fall strictly within envelope format bounds (600,000–5,000,000 iterations).
- Recommends the optimal creation iteration count targeting 500–1,000 ms median and <= 2,000 ms p95 derivation latency.
- Fail-closed security rule: the candidate floor (600,000 iterations) is never lowered automatically on low-power hardware.

## Consequences & Invariants

1. **Schema Exclusivity**: Any unknown document entry, missing attribute, or malformed XML element fails closed with `InvalidDataException`.
2. **Credential Safety**: Transient PIN and TAN credentials never touch the store schema, manifest, or purge intents.
3. **Decompression Safety**: The structural ZIP preflight halts decompression bombs, path traversals, and corrupted archives before stream allocation.
4. **Milestone Progress**: Completes the storage format review candidate milestone (M1-01), paving the way for durable profile lifecycle operations under M1-02.
