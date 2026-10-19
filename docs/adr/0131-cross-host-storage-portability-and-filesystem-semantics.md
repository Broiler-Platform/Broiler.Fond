# ADR 0131: Cross-Host Storage Portability & Filesystem Semantics

- **Status:** Accepted
- **Date:** 2026-10-19
- **Related:** [ADR 0004](0004-profile-envelope-v1.md), [ADR 0005](0005-profile-snapshots-v1.md), [ADR 0087](0087-profile-storage-schemas-preflight-and-calibration.md), [ADR 0125](0125-storage-migration-and-rollback-integrity.md), [ADR 0130](0130-cross-platform-host-seam-and-platform-qualification.md), [docs/milestone-8.md](../milestone-8.md#m8-02-cross-host-portable-profile-archive--filesystem-interoperability)

---

## 1. Context and Problem Statement

A fundamental requirement of Milestone 8 ([Additional Hosts](../roadmap.md#m8--additional-hosts)) is that encrypted profile archives and backups are 100% interoperable across all supported desktop and mobile platforms (Windows, Linux, macOS, Android, iOS):
1. **Passphrase Normalization:** Passphrases entered via platform-specific input methods (e.g., macOS native keyboard input which defaults to Unicode NFD vs Windows/Linux NFC) must derive byte-for-byte identical PBKDF2-SHA256 master keys.
2. **Deterministic XML Lexical Format:** Serialized XML documents must strictly use LF (`\n`) newlines, invariant culture formatting (`1234.56`), and strict UTF-8 without BOM, regardless of host `Environment.NewLine` or default system locale.
3. **Canonical ZIP Entry Naming:** All ZIP archive entries must use forward slash (`/`) path separators and flat entry names (e.g., `profile.xml`, `manifest.xml`), never backslashes (`\`) or OS-specific directory tokens.
4. **Filesystem Concurrency & Promotion Semantics:** Exclusive single-writer locking must function reliably across NTFS (`FileShare.None`) and POSIX filesystems (`flock`/`fcntl`). Candidate generation promotion (`File.Move` with `overwrite: false`) must remain fail-closed, ensuring partial writes never displace validated generations.

---

## 2. Decision and Architecture

### 2.1 Passphrase Normalization and Key Derivation Portability

As defined in ADR 0004 and ADR 0087:
- All passphrases undergo strict Unicode Form C normalization (`passphrase.Normalize(NormalizationForm.FormC)`) prior to UTF-8 byte encoding and PBKDF2 key derivation.
- An archive encrypted on macOS using a decomposed character (e.g., `u` + combining diaeresis `\u0308`) unlocks seamlessly on Windows or Linux using the precomposed character (`\u00FC`), and vice-versa.

### 2.2 Deterministic Archive Serialization Invariants

- `StoreArchiveCodec` enforces `XmlWriterSettings.NewLineChars = "\n"` and `NewLineHandling = NewLineHandling.Entitize`.
- Raw byte inspection verifies that `\r` (CR) bytes are strictly absent from XML document bodies.
- Manifest SHA-256 digests authenticate the exact serialized bytes of every document, ensuring identical cryptographic integrity across all platforms.
- ZIP entry paths are strictly flat and validated against allow-listed document sets (`StoreDocumentNames`), eliminating cross-platform path traversal or separator anomalies.

### 2.3 Cross-Host Filesystem & Concurrency Semantics

- **Directory Locking:** `ProfileStore` acquires an exclusive lock via `FileStream(..., FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None)` on `profile.lock`. In .NET 10, this translates to native file sharing locks on Windows and POSIX advisory locks (`flock`) on Linux/macOS.
- **Candidate Promotion:** New generations are written to a temporary candidate file (`profile.new-{32-hex}.tmp`), durably flushed, and reopened for full cryptographic and schema validation. Only a fully verified candidate is promoted via `File.Move(..., overwrite: false)` to its final generation path (`generation-{20-digit}.bffond`).
- **Fail-Closed Fallback:** In the event of an interrupted write or filesystem crash, unpromoted temporary files are ignored at startup, and the highest authenticated generation is selected.

### 2.4 Automated Portability Verification (`CrossHostStoragePortability`)

Implemented in `Broiler.Fond.Kernel.Storage`:
- `VerifyPassphraseDerivationPortability`: Confirms that composed and decomposed Unicode inputs derive identical 32-byte master key sessions.
- `VerifyXmlLexicalLineEndings`: Audits all XML entries in a live store snapshot, asserting zero CR (`\r`) bytes.
- `VerifyZipEntryPathSeparators`: Asserts all entry names conform strictly to forward-slash flat paths.
- `VerifyCrossHostRoundtrip`: Generates a multi-institution profile on one simulated host configuration and restores it on another, asserting 100% data and identity integrity.

---

## 3. Consequences

### Positive
- Users can seamlessly move profile archives between Windows, Linux, macOS, and mobile devices without migration errors or corrupted manifests.
- Prevents subtle platform-specific bugs caused by default locale differences or line ending conversions.
- Eliminates risk of file lock bypass on POSIX filesystems.

### Negative / Trade-offs
- File movement across separate filesystem mount points requires atomic temporary directory placement on the same filesystem volume.
