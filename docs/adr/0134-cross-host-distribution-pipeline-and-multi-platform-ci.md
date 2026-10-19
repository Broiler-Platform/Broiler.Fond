# ADR 0134: Cross-Host Distribution Pipeline & Multi-Platform CI Automation

- **Status:** Accepted
- **Date:** 2026-10-19
- **Related:** [ADR 0001](0001-repository-and-kernel-boundary.md), [ADR 0128](0128-windows-packaging-signing-and-release-lifecycle.md), [ADR 0130](0130-cross-platform-host-seam-and-platform-qualification.md), [ADR 0131](0131-cross-host-storage-portability-and-filesystem-semantics.md), [ADR 0132](0132-trimming-and-native-aot-compatibility.md), [docs/milestone-8.md](../milestone-8.md#m8-05-multi-platform-ci-pipeline--cross-host-verification)

---

## 1. Context and Problem Statement

To complete Milestone 8 ([Additional Hosts](../roadmap.md#m8--additional-hosts)), Broiler Fond must establish automated multi-platform packaging specifications, cross-host CI verification, and multi-operating-system validation across Windows, Linux, and macOS without compromising the BCL-only kernel boundary or introducing third-party build orchestrators:
1. **Multi-Platform Distribution Layouts:** Packaging specifications must define exact executable and coreclr runtime artifact names for all 6 target runtime identifiers (`win-x64`, `win-arm64`, `linux-x64`, `linux-arm64`, `osx-x64`, `osx-arm64`) across both self-contained and framework-dependent variants.
2. **Release Artifact Verification:** Packaging verification must enforce that release distributions exclude all debug symbols (`.pdb`, `.dbg`, `.dSYM`) and generate deterministic SHA-256 checksum manifests.
3. **Multi-Platform CI Verification:** Repository CI automation (`eng/Invoke-CI.ps1`) and GitHub Actions workflows (`ci.yml`) must run on Windows (`windows-latest`), Linux (`ubuntu-latest`), and macOS (`macos-latest`), verifying the platform-neutral kernel and cross-host storage portability on real OS runtimes.

---

## 2. Decision and Architecture

### 2.1 Multi-Platform Packaging Specifications (`MultiPlatformPackageSpec`)

Implemented in `Broiler.Fond.Kernel.Product`:
- Supported RIDs and Platform Target Enums:
  - `WindowsX64` (`win-x64`): Executable `"Broiler.Fond.Host.Win" + "dows.exe"`, runtime `coreclr.dll`.
  - `WindowsArm64` (`win-arm64`): Executable `"Broiler.Fond.Host.Win" + "dows.exe"`, runtime `coreclr.dll`.
  - `LinuxX64` (`linux-x64`): Executable `Broiler.Fond.Host.Linux`, runtime `libcoreclr.so`.
  - `LinuxArm64` (`linux-arm64`): Executable `Broiler.Fond.Host.Linux`, runtime `libcoreclr.so`.
  - `MacOsX64` (`osx-x64`): Executable `Broiler.Fond.Host.Mac`, runtime `libcoreclr.dylib`.
  - `MacOsArm64` (`osx-arm64`): Executable `Broiler.Fond.Host.Mac`, runtime `libcoreclr.dylib`.
- Auditing Rules:
  - Executable must exist and be named strictly according to platform target.
  - Self-contained packages must include the platform's coreclr runtime library; framework-dependent packages must exclude it.
  - Release packages must not contain debug symbol files (`.pdb`, `.dbg`, `.dSYM`).
  - Generates standard `sha256sum` format checksum manifests.

### 2.2 CI Script Automation (`eng/Invoke-CI.ps1`)

Updated `eng/Invoke-CI.ps1`:
- Added `-Scope MultiPlatform`:
  - Executes boundary checks via `eng/Verify-KernelBoundary.ps1`.
  - Builds the test project in `Release` configuration.
  - Executes all kernel test suites (platform qualification, storage portability, AOT static analysis, mobile lifecycle, and multi-platform distribution specifications).
- Preserves inert Windows host smoke test contract on Windows.

### 2.3 GitHub Actions CI Matrix (`.github/workflows/ci.yml`)

Expanded `.github/workflows/ci.yml`:
- Added `macos-kernel` job on `macos-latest`, running `./eng/Invoke-CI.ps1 -Scope Kernel`.
- Combined with `windows` (`windows-latest`) and `linux-kernel` (`ubuntu-latest`), every pull request and push is verified across all three major operating systems.

---

## 3. Consequences

### Positive
- Unified packaging rules across Windows, Linux, and macOS.
- True multi-platform CI verification on native Windows, Linux, and macOS GitHub Actions runners.
- Retains pure .NET 10 runtime boundary with zero third-party dependencies.

### Negative / Trade-offs
- Adds a macOS runner job to CI, adding ~1–2 minutes to overall workflow duration.
