# Milestone 8 — Additional Hosts

- **Brand:** Broiler
- **Product:** Fond - Finance on Demand
- **Technical name:** `Broiler.Fond`

**Status:** Complete (Milestones 0 through 8 complete).

**Last updated:** 2026-10-19

The [roadmap](roadmap.md#m8--additional-hosts) defines the product specification.
This backlog breaks Milestone 8 into reviewable increments; completion of one increment does not
imply authorization for production distribution.

---

## 1. Outcome

Broiler Fond qualifies cross-platform execution on Linux and macOS desktop hosts, establishes mobile lifecycle
and suspension safeguards, guarantees cross-host portable archive interoperability with zero-fork kernel reuse,
proves Native AOT and full trimming compatibility, and verifies multi-platform CI execution across Windows,
Linux, and macOS without compromising the BCL-only kernel boundary or zero-telemetry invariant.

---

## 2. Milestone Principles and Invariants

- **Shared, Unforked Kernel:** All banking, FinTS 3.0 protocol execution, storage, identity, reconciliation,
  and encryption logic remain in `Broiler.Fond.Kernel`; host code owns only presentation, lifecycle, input, and packaging.
- **Cross-Host Storage Interoperability:** A profile created or backed up on one qualified host opens transparently on
  every other qualified host with the same passphrase, exact local IDs, and zero data loss.
- **Trimming and Native AOT Compliance:** The kernel must be fully trimmable and compatible with Native AOT without
  hidden dynamic code paths, reflection warnings, or JIT dependency.
- **Mobile Lifecycle & Suspension Hygiene:** Mobile execution respects OS pause/suspend events during SCA flows, clears
  volatile in-memory credentials immediately upon suspension, and enforces transient clipboard hygiene.
- **Zero Telemetry on All Hosts:** No host implementation may add telemetry, analytics, crash reporting, background
  relay, or remote synchronization.

---

## 3. Work Breakdown Structure (M8-01 through M8-05)

### M8-01: Cross-Platform Host Seam & Platform Qualification Framework

- [x] Implement host qualification framework and cross-platform seam contracts ([ADR 0130](adr/0130-cross-platform-host-seam-and-platform-qualification.md)):
  - Platform qualification engine: assert `AesGcm.IsSupported`, UTF-8 FormC normalization, and invariant culture formatting.
  - Cross-platform host seam: abstract host notifications, credential prompt lifecycle, and safe mode boundaries.
- [x] Add automated kernel test coverage for platform qualification (`HostPlatformQualificationTests`).

### M8-02: Cross-Host Portable Profile Archive & Filesystem Interoperability

- [x] Implement cross-host storage portability and POSIX filesystem verification ([ADR 0131](adr/0131-cross-host-storage-portability-and-filesystem-semantics.md)):
  - Cross-host archive portability: verify profile archives exchange cleanly across Windows, Linux, and macOS without path separator or newline corruption.
  - POSIX directory locking and atomic rename fallback semantics.
- [x] Add automated test suite verifying cross-host archive roundtrips and filesystem edge cases (`CrossHostStoragePortabilityTests`).

### M8-03: Trimming, Linker Analysis & Native AOT Compatibility Verification

- [x] Implement trimming and Native AOT verification ([ADR 0132](adr/0132-trimming-and-native-aot-compatibility.md)):
  - Kernel trimmability verification (`<IsTrimmable>true</IsTrimmable>`).
  - Native AOT static analysis: ensure XML serialization, cryptographic primitives, and domain models are free of unsupported dynamic reflection.
- [x] Add automated kernel test suite verifying AOT compatibility invariants (`TrimmingAndAotCompatibilityTests`).

### M8-04: Mobile & Background Lifecycle Hardening & Device Threat Model

- [x] Implement mobile and background lifecycle safeguards ([ADR 0133](adr/0133-mobile-lifecycle-suspension-and-device-security.md)):
  - Lifecycle coordinator: handle app suspension, background timeouts during decoupled SCA, and immediate memory zeroization.
  - Transient clipboard hygiene and secure memory wiping on pause/backgrounding.
- [x] Add automated test suite verifying lifecycle state transitions and credential wiping on suspension (`MobileLifecycleHardeningTests`).

### M8-05: Multi-Platform CI Pipeline & Cross-Host Verification

- [x] Implement multi-platform packaging specifications and build automation ([ADR 0134](adr/0134-cross-host-distribution-pipeline-and-multi-platform-ci.md)):
  - Build and packaging specs for Linux (`linux-x64`, `linux-arm64`) and macOS (`osx-x64`, `osx-arm64`).
  - Cross-host verification suite in CI automation (`Invoke-CI.ps1`).
- [x] Verify full multi-platform suite and execute end-to-end CI verification.

---

## 4. Exit Criteria

- Kernel executes cleanly on Linux, macOS, and Windows with zero platform-specific forks.
- Profile archives roundtrip seamlessly across all supported host architectures.
- Native AOT and trimming static analysis pass with zero warnings or dynamic code regressions.
- Mobile lifecycle coordinator zeroizes credentials and recovers state safely without data loss.
- Full CI passes across all targets.
