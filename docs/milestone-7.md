# Milestone 7 — Windows 1.0

- **Brand:** Broiler
- **Product:** Fond - Finance on Demand
- **Technical name:** `Broiler.Fond`

**Status:** Complete (Milestones 0, 1, 2, 3, 4, 5, 6, and 7 complete).

**Last updated:** 2026-10-19

The [roadmap](roadmap.md#m7--windows-10) defines the product specification.
This backlog breaks Milestone 7 into reviewable increments; completion of one increment does not
imply authorization for production distribution.

---

## 1. Outcome

Broiler Fond achieves a production-ready Windows 1.0 state with a frozen and hardened FinTS 3.0 capability matrix,
verified cross-version storage migration guarantees, strict downgrade protection, robust zero-telemetry and memory
credential hygiene controls, BFSG/BFSGV-tested accessibility across all interactive screens, full German/English
localization, repeatable Windows packaging and Authenticode signing, and comprehensive two-human release gate validation.

---

## 2. Milestone Principles and Invariants

- **Frozen FinTS 3.0 Baseline:** The protocol capability set promised for Windows 1.0 is strictly frozen to verified
  FinTS 3.0 PIN/TAN operations; newer protocol expansions (FinTS 4.1) remain explicitly deferred to post-1.0.
- **Strict BCL-only Kernel Boundary:** `Broiler.Fond.Kernel` maintains zero NuGet packages or native dependencies;
  all release packaging, migration logic, and conformance suites respect the pure .NET 10 shared framework boundary.
- **Backward and Forward Storage Integrity:** Profile stores maintain schema forward/backward compatibility
  and migration safety; downgrade attempts and conflicting fork branches fail closed with explicit user recovery options.
- **Zero Telemetry & Strict Memory Hygiene:** Zero background reporting, analytics, or automatic network calls to any
  non-bank host; bank credentials (PINs/TANs) and derived cryptographic key buffers are strictly zeroed upon lock.
- **BFSG & BFSGV Accessibility Mandate:** Every user workflow is operable via keyboard, high contrast, text scaling,
  and screen readers, with dual visual indicators (`[✓ ...]` / `[⚠ ...]`) and accessible names on all controls.
- **Two-Human Release Gate:** No candidate is distributed without an independent release owner signature and a separate
  human security reviewer sign-off recorded in `HUMAN_REVIEW.md`.

---

## 3. Work Breakdown Structure (M7-01 through M7-06)

### M7-01: Freeze and Harden FinTS 3.0 Capability Matrix & Conformance Verification

- [x] Implement and verify FinTS 3.0 conformance suite and error hardening ([ADR 0124](adr/0124-fints-3-freeze-and-conformance-hardening.md)):
  - Conformance test harness executing domain contract suites across all verified FinTS 3.0 business transactions (dialogue, BPD/UPD, balances, statements, VoP, transfers, standing orders, direct debit returns).
  - Malformed segment, unexpected encoding, truncated envelope, and failure injection test matrix.
  - Connector compatibility profile lock: establish permanent freeze of supported FinTS 3.0 segment versions and ensure unsupported operations fail closed.
- [x] Add automated kernel test coverage for FinTS 3.0 conformance matrix and boundary failure injection (`FinTs30ConformanceHardeningTests`).

### M7-02: Comprehensive Storage Migration Guarantees, Downgrade Protection & Rollback Integrity

- [x] Implement storage migration guarantees, downgrade protection, and rollback verification ([ADR 0125](adr/0125-storage-migration-and-rollback-integrity.md)):
  - Multi-version migration validator: seamless upgrade across schemas (`store-v1.xsd`, `transactions-v1.xsd`, `payments-v1.xsd`, `templates-v1.xsd`, `purge-intent-v1.xsd`).
  - Strict downgrade guard: detect attempt to load store created by higher generation or incompatible schema, failing closed with actionable warning.
  - Lineage rollback and fork recovery: detect superseded generations and prevent silent history truncation.
  - Profile backup and restore verification: cryptographic roundtrip test for full profile archives.
- [x] Add automated kernel test suite verifying migration matrix, downgrade refusal, fork recovery, and backup roundtrip (`StorageMigrationAndRollbackTests`).

### M7-03: Security Controls, Vulnerability Incident Process & In-Memory Credential Hygiene

- [x] Implement security controls and memory hygiene verification ([ADR 0126](adr/0126-security-controls-and-incident-response.md)):
  - Zero-telemetry static analysis: binary inspection verifying zero network sockets, telemetry endpoints, or diagnostic crash reporters outside user-specified bank endpoints.
  - Ephemeral credential hygiene: verify `CryptographicOperations.ZeroMemory` wipes PIN, TAN, PBKDF2/HKDF master/content keys upon session lock or operation disposal.
  - Security incident process and disclosure guide in `SECURITY.md`: vulnerability reporting channel, PGP key, response SLA, and advisory format.
- [x] Add automated test suite for memory wiping, diagnostic secret redaction, and zero-telemetry binary validation (`SecurityHygieneTests`).

### M7-04: Accessibility (BFSG/BFSGV) & Full German/English Localization Verification

- [x] Implement accessibility compliance and localization hardening ([ADR 0127](adr/0127-accessibility-and-localization-hardening.md)):
  - BFSG / BFSGV audit: verify screen reader announcements, tab order, focus traversal, text scaling, and high-contrast support on all Windows Forms dialogs.
  - Dual visual cues: ensure no status, error, or warning relies solely on color (`[✓ ...]` and `[⚠ ...]`).
  - Bilingual string resources: comprehensive German (`de-DE`) and English (`en-US`) localization with culture-invariant formatting for ISO currencies and dates.
- [x] Add automated headless accessibility and localization verification tests (`AccessibilityAndLocalizationTests`).

### M7-05: Windows Packaging, Authenticode Signing, Release Pipeline & Update Lifecycle

- [x] Implement Windows 1.0 packaging manifest and release lifecycle ([ADR 0128](adr/0128-windows-packaging-signing-and-release-lifecycle.md)):
  - Packaging pipeline: self-contained and framework-dependent `win-x64` zip/installer distribution specs with SHA-256 manifest and SBOM.
  - Authenticode code-signing configuration and reproducible build stamping.
  - Pre-release and 1.0 release checklist verification against `docs/release/prerelease-checklist.md` and `HUMAN_REVIEW.md`.
- [x] Verify packaging pipeline, artifact checksum generation, and version resolution in build automation (`PackagingAndReleaseLifecycleTests`).

### M7-06: Windows Host 1.0 Hardening, Shell Integration & Headless Verification

- [x] Implement Windows Host 1.0 polish and shell integration ([ADR 0129](adr/0129-windows-host-1-0-hardening.md)):
  - About & Legal Disclosures dialog: display pure user-operated trust boundary, BFSG compliance declaration, FinTS product registration code, and open-source licenses (`AboutDialog`).
  - Profile backup and restore UI workflows: interactive backup export and passphrase-verified restore dialogs (`HostSession.ExportBackup`, `RestoreBackupDialog`).
  - Safe Mode status bar indicator and release-ready UI polish.
- [x] Verify full host smoke and headless self-tests in `HostSelfTest` and execute end-to-end CI verification (`Invoke-CI.ps1 -Scope Full`).

---

## 4. Exit Criteria

- FinTS 3.0 capability matrix is frozen, hardened, and verified with zero unresolved failure branches.
- Storage migrations across all schema versions pass without data loss, and downgrade attempts fail closed.
- Binary inspections confirm zero telemetry, zero analytics, and strict in-memory credential zeroing.
- BFSG / BFSGV accessibility verification passes with 100% accessible control names and dual indicators.
- Packaging manifests produce deterministic `win-x64` release artifacts with checksums and SBOM.
- Two-human release approval gates are prepared and signed in `HUMAN_REVIEW.md`.
