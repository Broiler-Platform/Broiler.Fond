# Human release review

> **Current status: PENDING — no distributable candidate has been approved.**

This file defines the human approval record for every externally distributed
prerelease and release of **Broiler Fond - Finance on Demand**. It does not
approve the repository, a branch, or any artifact by itself.

Current Milestone 1 implementation ([docs/milestone-1.md](docs/milestone-1.md), [ADRs 0003–0093](docs/adr/README.md)) covers:

- **Domain, identity, rediscovery, relinking, and exact money (`ADRs 0003, 0006–0008, 0082, 0090, 0093`):** Manual HTTPS institution endpoint validation with confirmation and quarantine (`FinTsEndpoint`, `ManualInstitutionEndpointConfiguration`), immutable `BankConnection` operational state transitions, shared 64-bit `LocalIdentityLedger` with atomic batch commits and startup validation, lossless `AccountSourceLocator` and `AccountRediscoveryPlanner`, deterministic ambiguous-rediscovery account relinking and provenance-preserving candidate pruning (`AccountRelinkingPlanner`, `IProfileStore.RelinkAccountBinding`), user-reviewed relinking dialog (`AccountRelinkingDialog`), and exact ISO 4217 `Money`, `BalanceSnapshot`, and `AccountValueProjection` models.
- **Encrypted profile storage, lifecycle, disconnect, and diagnostic persistence (`ADRs 0004, 0005, 0087–0089`):** Authenticated-encrypted `BFPE` v1 envelope (`ProfileEnvelopeCodec`), ZIP/XML snapshot protocol (`bootstrap-v1.xsd`), full M1 financial store schemas (`store-v1.xsd`, `StoreArchiveCodec`), snapshot ancestry lineage, encrypted purge-intent schemas (`purge-intent-v1.xsd`), bounded hostile-input ZIP preflight validation (`ZipPreflight`), supported-floor PBKDF2-HMAC-SHA256 calibration (`SupportedFloorKdfCalibration`, `600,000..5,000,000` iterations), seekable authenticating frame stream (`SeekableAuthenticatedProfileStream`), production `IProfileStore` / `ProfileStore` lifecycle (single-writer directory lock, atomic generation promotion, branched prior-generation recovery, privacy purge, passphrase rotation, whole-profile erasure, backup/restore, and ID-remapping import/merge), connection removal with cached-data purge (`ConnectionRemovalPlanner`, `IProfileStore.RemoveConnection`), and opt-in encrypted local diagnostic persistence (`EncryptedLocalDiagnosticStore`, `ExportReviewedSupportPreview`).
- **FinTS 3.0 protocol, session coordinators, execution pipelines, and SCA continuation (`ADRs 0009–0086, 0091`):** Bounded FinTS 3.0 byte syntax and outer framing, typed `HIRMG`/`HIRMS` dialogue correlation, `HIBPA`/`HIUPA`/`HIUPD`/`HIPINS` parameter and `HITANS`/`HITAN` v6–v7 challenge schemas, PIN/TAN security headers/trailers/envelopes (`HNSHK`, `HNSHA`, `HNVSK`, `HNVSD`), zeroized `SessionCredentialBuffer`, single- and multi-page read/discovery/initialization/synchronization/closing attempts, multi-stage session coordinators (`FinTsPinTanReadSessionAttempt`, `FinTsPinTanSynchronizationSessionAttempt`) with hardened clock/provenance/ownership/accounting invariants, synchronization recovery (`FinTsSynchronizationRecoveryApplication`), capability activation (`FinTsCapabilityActivation`), bounded HTTPS transport (`FinTsHttpsTransport`), read store ingestion (`FinTsReadIngestionApplication`), end-to-end execution pipelines (`FinTsReadExecutionPipeline`, `FinTsSynchronizationExecutionPipeline`), FinTS product registration (`FinTsProductRegistration`, `KernelProduct.FinTsRegistration`), and interactive/decoupled SCA challenge continuation (`FinTsScaExecutionCoordinator`).
- **Windows host presentation, truthful values, and support workflows (`ADRs 0092, 0093`):** Accessible Windows UI shell (`Broiler.Fond.Host.Windows`, `MainForm`, `AccountViewModel`, `ConnectionViewModel`, `HostSession`), dual-mode startup with automated headless self-test (`HostSelfTest`) meeting smoke test invariants, independent HTTPS endpoint confirmation dialog with phishing risk disclosures (`EndpointConfirmationDialog`), profile lock/unlock dialog (`ProfileUnlockDialog`), session PIN/TAN prompts with volatile buffer zeroization (`CredentialPromptDialog`, `ScaChallengeDialog`), user-reviewed candidate account relinking dialog (`AccountRelinkingDialog`) with deterministic plan preview and privacy purge execution, truthful balance presentation with freshness indicators (< 1h, < 24h, Stale, Offline) and strict multi-currency isolation, connection disconnect and irreversible privacy purge dialog (`ConnectionRemovalDialog`), and SHA-256 preview digest bound redacted support export dialog (`SupportExportDialog`) with zero background telemetry.

The human storage format review and cross-host Linux execution evidence ([docs/storage-format-review.md](docs/storage-format-review.md)) are complete and recorded across ADRs 0004, 0005, 0087, 0088, 0089, 0090, 0092, and 0093.
Controlled live bank acceptance ([M1-09](docs/compatibility/README.md)), registered production product code, written legal classification memo, and two-human release signatures ([M1-10](docs/roadmap.md)) remain pending before an external release is distributed. It is not yet an approved banking-capable prerelease.

## Milestone 1 release gate breakdown

1. **Storage Format & Cryptographic Review (M1-01 / M1-02):** `APPROVED` (Recorded in [docs/storage-format-review.md](docs/storage-format-review.md)).
2. **Cross-Host Linux CI Verification:** `APPROVED` (100% test pass on Linux in `.github/workflows/ci.yml`).
3. **Live Bank Acceptance Gate (M1-09):** `PENDING` (Controlled live execution on test institution per [docs/compatibility/README.md](docs/compatibility/README.md)).
4. **Legal Classification Memo (M1-10):** `PENDING` (Written legal review confirming purely user-operated client model).
5. **Two-Human Release Signatures (M1-10):** `PENDING` (Independent sign-off by release owner and security reviewer below).

## Required people

Each candidate requires two named people:

- a **release owner**, accountable for scope, build provenance, and release
  readiness; and
- a **security reviewer**, accountable for an independent review of the exact
  candidate and its evidence.

One person must not fill both roles for the same candidate. Payment-capable
candidates additionally require the independent penetration-test evidence
specified by the roadmap and release checklist.

## Candidate identity

Approval is bound to immutable candidate evidence. Copy this block into the
release record and replace every placeholder.

| Field | Value |
| --- | --- |
| Product | Broiler Fond - Finance on Demand |
| Candidate version | `UNSET` |
| Release channel | `UNSET` (`prerelease` or `release`) |
| Source commit | `UNSET` |
| Build workflow/run | `UNSET` |
| Artifact names | `UNSET` |
| Artifact SHA-256 digests | `UNSET` |
| Review checklist revision | `UNSET` |
| Review opened (UTC) | `UNSET` |

Any source, dependency, configuration, packaging, signing, or artifact change
after review invalidates approval and requires a new candidate record.

## Review procedure

1. Create a candidate record containing the immutable identity above.
2. Complete [the prerelease checklist](docs/release/prerelease-checklist.md)
   against that exact candidate.
3. Record each finding with an owner, severity, disposition, and evidence.
4. Block distribution while any required item is incomplete or any
   release-blocking finding remains open.
5. Have the release owner and security reviewer sign independently.
6. Retain the signed record and evidence with the release metadata.

Allowed record states are `PENDING`, `BLOCKED`, `APPROVED`, and `SUPERSEDED`.
Only an `APPROVED` record for the exact artifacts permits distribution.

## Approval record

The following is deliberately unsigned.

| Role | Name | Decision | Date (UTC) | Evidence/signature |
| --- | --- | --- | --- | --- |
| Release owner | `UNSET` | `PENDING` | `UNSET` | `UNSET` |
| Security reviewer | `UNSET` | `PENDING` | `UNSET` | `UNSET` |

**Candidate decision:** `PENDING`
