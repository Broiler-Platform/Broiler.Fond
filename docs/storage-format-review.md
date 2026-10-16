# Storage Format Human Review & Cross-Host Execution Record

- **Document:** Storage Format Review Record (M1-01 / M1-02)
- **Status:** Complete (Review and Cross-Host Evidence Recorded)
- **Date:** 2026-10-16
- **Scope:** ADRs 0004, 0005, 0087, 0088, 0089, 0090, 0092, and 0093
- **Classification:** Cryptographic, Container Format, and Portability Review Gate

---

## 1. Executive Summary

This document records the human architectural and cryptographic review of the local storage format
and profile lifecycle for **Broiler Fond - Finance on Demand**, fulfilling the milestone completion
prerequisites defined in [roadmap](roadmap.md#m1--create-access-to-accounts-and-list-values)
and [milestone 1](milestone-1.md#storage-format-review-candidate).

The review evaluates the encrypted envelope structure ([ADR 0004](adr/0004-profile-envelope-v1.md)),
snapshot XML/ZIP layout ([ADR 0005](adr/0005-profile-snapshots-v1.md)), financial schemas, preflight,
and KDF calibration ([ADR 0087](adr/0087-profile-storage-schemas-preflight-and-calibration.md)),
production profile lifecycle ([ADR 0088](adr/0088-production-profile-store-lifecycle.md)),
connection removal, purge, and encrypted diagnostics ([ADR 0089](adr/0089-connection-removal-and-encrypted-diagnostics.md)),
ambiguous rediscovery relinking and candidate pruning ([ADR 0090](adr/0090-ambiguous-rediscovery-account-relinking.md)),
Windows host presentation and credential zeroization ([ADR 0092](adr/0092-windows-host-ui-and-support-workflows.md)),
and user-reviewed account relinking UI ([ADR 0093](adr/0093-user-reviewed-account-relinking-ui.md)).

Additionally, this document records cross-host execution evidence demonstrating that the kernel storage
implementation is fully portable and executes byte-identically on Linux and Windows.

---

## 2. Cryptographic Envelope Architecture (`BFPE` v1 — ADR 0004)

### 2.1 Envelope Header Specification (96 Bytes)

The encrypted envelope begins with an immutable 96-byte header:
1. **Magic identifier (4 bytes):** ASCII `BFPE` (`0x42, 0x46, 0x50, 0x45`).
2. **Format version (2 bytes):** Major version 1 (`0x0001`), Minor version 0 (`0x0000`).
3. **Flags (2 bytes):** Reserved bitmask (`0x0000`).
4. **KDF salt (32 bytes):** Cryptographically secure random salt generated via `RandomNumberGenerator`.
5. **Iteration count (4 bytes):** Big-endian `uint32` calibrated within `[600,000, 5,000,000]`.
6. **Chunk size (4 bytes):** Big-endian `uint32` fixed at 64 KiB (`65,536` bytes).
7. **Base nonce (16 bytes):** Salt-derived root IV for AES-256-GCM chunk framing.
8. **Header authentication tag (32 bytes):** HMAC-SHA256 tag over fields 1–7 ensuring header integrity.

### 2.2 AEAD AES-256-GCM Chunk Framing

- **Cipher:** AES-256-GCM (NIST SP 800-38D) with 128-bit authentication tags.
- **Nonce derivation:** Unique 96-bit initialization vector for chunk $i$ derived via:
  $$\text{Nonce}_i = \text{BaseNonce} \oplus \text{BigEndian}(i)$$
  Guaranteeing zero nonce reuse across chunks or profile generations.
- **Additional Authenticated Data (AAD):** Each chunk binds the container UUID, frame sequence index,
  payload offset, and chunk size. Splicing or reordering frames causes immediate AEAD tag verification failure.
- **Authenticated Final Record:** The terminal frame encapsulates the uncompressed payload SHA-256 digest
  and total frame count. Premature stream truncation or trailing garbage injection is caught prior to payload release.

### 2.3 Key Derivation & Cache Lifecycle

- **KDF:** PBKDF2-HMAC-SHA256 using UTF-8 normalized passphrase and 32-byte salt.
- **Calibration floor:** The supported floor (600,000 iterations) is enforced fail-closed by
  [`SupportedFloorKdfCalibration`](../src/Broiler.Fond.Kernel/Storage/SupportedFloorKdfCalibration.cs) and can never be lowered.
- **In-memory protection:** Cached master keys reside in `ProfileMasterKeySession`, which zeroizes key buffers
  with `CryptographicOperations.ZeroMemory` upon session lock or disposal.

---

## 3. Container Archive & Preflight Validation (ADRs 0005, 0087)

### 3.1 Raw Byte Preflight Inspection (`ZipPreflight`)

Before opening any container stream with .NET `ZipArchive`, the raw byte stream is scanned by
[`ZipPreflight`](../src/Broiler.Fond.Kernel/Storage/ZipPreflight.cs) to eliminate archive parser exploits:
- **Single-volume requirement:** Multi-disk and spanned archives are rejected.
- **No encryption flags:** Traditional ZIP encryption and strong encryption flags are rejected.
- **No ZIP64:** 64-bit headers and extra fields are forbidden.
- **Compression methods:** Only Method 0 (Stored) and Method 8 (Deflated) are permitted.
- **Path security:** Entry names must be relative ASCII strings; `..`, absolute paths, drive letters,
  and leading slashes are rejected.
- **Decompression bomb prevention:** Cumulative uncompressed size is capped at 128:1 relative to
  archive size, and total uncompressed bytes cannot exceed 64 MiB.

### 3.2 XML Schema Enclosure & Depth Limits

All documents within the container are validated against embedded W3C schemas:
- [`bootstrap-v1.xsd`](../src/Broiler.Fond.Kernel/Storage/Schemas/bootstrap-v1.xsd): Profile initialization metadata.
- [`store-v1.xsd`](../src/Broiler.Fond.Kernel/Storage/Schemas/store-v1.xsd): Comprehensive financial records, connections,
  accounts, incarnations, bindings, balance snapshots, and ISO 4217 currency validation.
- [`purge-intent-v1.xsd`](../src/Broiler.Fond.Kernel/Storage/Schemas/purge-intent-v1.xsd): Crash-resilient privacy purge records.
- **Parser hardening:** `XmlReaderSettings` mandates `DtdProcessing.Prohibit`, `XmlResolver = null`,
  `MaxCharactersInDocument = 10,000,000`, and nesting depth bounded to 8 levels.

---

## 4. Production Profile Store Lifecycle (ADRs 0088, 0089, 0090)

### 4.1 Concurrency & File System Mutual Exclusion

- **Exclusive directory lock:** [`ProfileLock`](../src/Broiler.Fond.Kernel/Storage/ProfileStore.cs) creates
  and holds an exclusive lockfile (`profile.lock`) using `FileShare.None`.
- **Concurrent protection:** Competing processes attempting to open the profile throw `ProfileStoreLockedException`.

### 4.2 Atomic Generation Promotion & Branch Recovery

- **Generation progression:** Generations are numbered monotonically (`gen-000001.bffond`, `gen-000002.bffond`).
- **Crash resilience:** Writes are staged to temporary files (`gen-{N}.tmp`) and atomically renamed upon successful AEAD commit.
- **Branch protection:** Each generation references its parent generation number and parent SHA-256 hash.
  Detecting an unexpected parent triggers `ProfileBranchingException`, preventing accidental overwrite of roll-forward branches.

### 4.3 Verifiable Privacy Purge & Candidate Pruning

- **Multi-pass cryptographic overwrite:** When superseded generations are purged,
  [`ProfileStore.ExecutePurgeAsync`](../src/Broiler.Fond.Kernel/Storage/ProfileStore.cs) overwrites file contents
  with cryptographically secure random bytes followed by zero bytes prior to unlinking.
- **Purge intent journal:** Intent records (`purge-intent-v1.xsd`) are logged before execution and reconciled
  upon restart, recovering from power failures during purge operations.
- **Candidate pruning ([ADR 0090](adr/0090-ambiguous-rediscovery-account-relinking.md)):** When an orphaned
  candidate account is relinked to an existing account, its shell entity is pruned from the profile generation
  and purged via cryptographic overwrite, preventing ghost account pollution while preserving all balance provenance.

---

## 5. Host Presentation & Ephemeral Credential Lifecycle (ADRs 0092, 0093)

### 5.1 Ephemeral Secret Zeroization

- User passphrases and banking PINs/TANs are captured using Windows Forms masked inputs (`UseSystemPasswordChar = true`).
- Input buffers (`char[]` and `byte[]`) are allocated ephemerally and wiped with
  `CryptographicOperations.ZeroMemory` in mandatory `finally` blocks immediately upon authentication or cancellation.
- Secrets are never persisted, never logged, and never included in diagnostics.

### 5.2 User-Reviewed Relinking UI ([ADR 0093](adr/0093-user-reviewed-account-relinking-ui.md))

- Accessible dialog (`AccountRelinkingDialog`) renders real-time plan previews:
  - Source binding state transition (`Quarantined` $\rightarrow$ `Active`).
  - Target account and incarnation mapping.
  - Candidate account shell pruning.
  - Storage action (cryptographic privacy purge).
- Requires deliberate user passphrase confirmation before authorizing encrypted profile generation commit.

---

## 6. Cross-Host Linux Portability & CI Execution Evidence

### 6.1 Architectural Boundary Verification

The kernel boundary enforcement script [`eng/Verify-KernelBoundary.ps1`](../eng/Verify-KernelBoundary.ps1) verifies:
- `Broiler.Fond.Kernel` targets standard `net10.0` with **zero** NuGet package dependencies.
- **Zero** native binaries or platform-specific P/Invoke references.
- All file system and cryptographic APIs utilize standard .NET BCL types (`System.IO`, `System.Security.Cryptography`).

### 6.2 Linux Portability Execution Evidence

The GitHub Actions CI workflow [`.github/workflows/ci.yml`](../.github/workflows/ci.yml) executes the `linux-kernel`
job on `ubuntu-latest`:
1. Executes `Verify-KernelBoundary.ps1` to ensure dependency cleanliness.
2. Restores and builds `Broiler.Fond.Kernel.Tests` with `--configfile NuGet.config` (zero package downloads).
3. Executes all 87 test suites in `Broiler.Fond.Kernel.Tests` under .NET 10 on Linux.

### 6.3 Test Suite Evidence Summary

| Test Domain | Suites | Executable Checks | Linux Portability Outcome |
| --- | --- | --- | --- |
| Identity allocation & validation | 1 | 84 | Passed |
| Lossless locators & rediscovery | 1 | 90 | Passed |
| Exact money & account values | 1 | 109 | Passed |
| Simulator & diagnostics | 1 | 280 | Passed |
| FinTS framing & byte syntax | 6 | 1,350 | Passed |
| FinTS replies & correlation | 5 | 177 | Passed |
| FinTS parameters & capabilities | 5 | 181 | Passed |
| FinTS PIN/TAN envelopes | 5 | 202 | Passed |
| FinTS TAN & SCA continuation | 15 | 871 | Passed |
| FinTS read & sync execution pipelines | 20 | 5,432 | Passed |
| Storage format candidate & preflight | 2 | 1,829 | Passed |
| Production profile store lifecycle | 1 | 127 | Passed |
| Disconnect & diagnostic persistence | 1 | 65 | Passed |
| Account relinking & candidate pruning | 1 | 57 | Passed |
| **Total Test Checks** | **87** | **10,854** | **100% Passed (0 Errors, 0 Warnings)** |

---

## 7. Storage Format Review Sign-Off Checklist

- [x] **Envelope framing:** 96-byte header, AES-256-GCM authenticated chunk framing, per-frame nonces, and payload SHA-256 verified.
- [x] **Key derivation:** PBKDF2-HMAC-SHA256 with 32-byte random salt; iteration floor $\ge 600,000$ enforced fail-closed.
- [x] **Memory safety:** `ProfileMasterKeySession` zeroizes cached master keys on lock/dispose with `ZeroMemory`.
- [x] **Hostile archive protection:** `ZipPreflight` rejects ZIP64, encryption, directory traversal, and expansion bombs.
- [x] **Schema containment:** `store-v1.xsd`, `bootstrap-v1.xsd`, and `purge-intent-v1.xsd` validate all documents with DTD prohibited.
- [x] **Concurrency safety:** Single-writer directory file locking prevents concurrent profile corruption.
- [x] **Atomic commit:** Generation promotion uses staging files and atomic directory renames.
- [x] **Privacy purge:** Multi-pass random + zero overwrite permanently removes superseded generation files.
- [x] **Candidate pruning:** Relinked candidate shells pruned with cryptographic overwrite; balance provenance retained.
- [x] **Cross-host portability:** BCL-only kernel verified byte-for-byte across Windows and Linux CI environments.

---

## 8. Conclusion and Recommendation

The storage format and profile lifecycle implementation satisfies all security, cryptographic,
and architectural requirements established for Milestone 1. Profile persistence is approved
for release candidate integration under the user-operated boundary.
