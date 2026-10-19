# ADR 0128: Windows 1.0 Packaging Manifest, Artifact Verification, and Release Lifecycle

- **Status:** Accepted
- **Date:** 2026-10-19
- **Related:** [ADR 0001](0001-repository-and-build-baseline.md), [ADR 0087](0087-profile-storage-schemas-preflight-and-calibration.md), [docs/release/prerelease-checklist.md](../release/prerelease-checklist.md), [HUMAN_REVIEW.md](../../HUMAN_REVIEW.md), [docs/roadmap.md](../roadmap.md#initial-delivery), [docs/milestone-7.md](../milestone-7.md#m7-05-windows-packaging-authenticode-signing-release-pipeline--update-lifecycle)

---

## 1. Context and Problem Statement

Broiler Fond is delivered as an installed, purely user-operated Windows application targeting 64-bit Windows (`win-x64`). 
For a 1.0 release declaration, packaging, code signing, and distribution must adhere to strict security and reproducibility controls:
1. **Deterministic packaging variants:** Two standard distribution formats must be generated:
   - `Self-contained`: Bundles the full .NET 10 runtime (`coreclr.dll`, shared libraries) so users can run Broiler Fond out-of-the-box without prerequisites.
   - `Framework-dependent`: Minimal lightweight package targeting systems with an existing .NET 10 desktop runtime.
2. **Cryptographic integrity and SBOM:** Every release asset must have an immutable SHA-256 checksum recorded in a signed manifest (`SHA256SUMS`). A Software Bill of Materials (SBOM) must prove zero third-party runtime package dependencies in the kernel.
3. **Debug and symbol hygiene:** Production packages must strip `.pdb` files to minimize footprint and prevent internal build-path disclosure.
4. **Authenticode code signing and timestamping:** Release binaries must be signable with Authenticode certificates using trusted timestamping authorities.
5. **Two-human release gate:** Every distributed candidate requires independent signatures from the named release owner and an independent human security reviewer in `HUMAN_REVIEW.md`.

---

## 2. Decision and Architecture

### 2.1 Release Artifact Verifier (`ReleaseArtifactVerifier`)

The build and verification pipeline includes `ReleaseArtifactVerifier`:
- **Directory Layout Audit:**
  - Asserts that `Broiler.Fond.Host.Windows.exe` exists in root of each package.
  - Verifies `coreclr.dll` presence in self-contained and strictly absent from framework-dependent packages.
  - Verifies zero `.pdb` symbol files remain in release distributions.
  - Asserts zero telemetry libraries or forbidden native dependencies.
- **SHA-256 Checksum Manifest Generation:**
  - Computes SHA-256 digests for all published archives and executables, formatting them in standard `sha256sum` format: `<hex_digest>  <filename>`.
- **Runtime Version Stamping:**
  - Verifies that assembly version metadata matches `Directory.Build.props` (`$(BroilerFondVersion)`).

### 2.2 Release Gate Checklist Execution

Every candidate release executes the checklist in `docs/release/prerelease-checklist.md`:
- Candidate identity and commit hash recorded.
- Automated CI verification passed (`eng/Invoke-CI.ps1 -Scope Full`).
- Kernel boundary check verified zero external NuGet packages (`eng/Verify-KernelBoundary.ps1`).
- Sign-offs recorded in `HUMAN_REVIEW.md`.

---

## 3. Consequences

### Positive
- Predictable, automated, and tamper-evident packaging for Windows 1.0.
- Clean separation between self-contained and framework-dependent distributions.
- Verifiable SHA-256 digests protecting users against distribution tampering.

### Negative / Trade-offs
- Self-contained packages are larger (~80 MiB) due to bundling the .NET 10 desktop runtime.
