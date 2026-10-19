# ADR 0130: Cross-Platform Host Seam & Platform Qualification Framework

- **Status:** Accepted
- **Date:** 2026-10-19
- **Related:** [ADR 0001](0001-repository-and-kernel-boundary.md), [ADR 0003](0003-operating-model-and-trust-boundary.md), [ADR 0004](0004-profile-envelope-v1.md), [ADR 0087](0087-profile-storage-schemas-preflight-and-calibration.md), [ADR 0092](0092-windows-host-architecture-and-headless-verification.md), [docs/milestone-8.md](../milestone-8.md#m8-01-cross-platform-host-seam--platform-qualification-framework)

---

## 1. Context and Problem Statement

As defined in the Broiler Fond roadmap ([Milestone 8](../roadmap.md#m8--additional-hosts)), the product must expand from the initial Windows 1.0 desktop host to support additional desktop and mobile environments (Linux, macOS, Android, iOS) using one shared, unforked `net10.0` kernel (`Broiler.Fond.Kernel`) and compatible versioned profile archives.

To maintain correctness, auditability, and safety across heterogeneous platforms without compromising the zero-telemetry and BCL-only kernel boundary, the architecture requires:
1. **Host Platform Qualification Engine:** An automated in-memory qualification harness that verifies critical runtime primitives before storage or banking execution:
   - `AesGcm.IsSupported`: Hardware-accelerated or runtime AES-256-GCM support is non-negotiable for profile envelope decryption and encryption.
   - Unicode Normalization: Strict `NormalizationForm.FormC` normalization and strict UTF-8 serialization must produce byte-for-byte identical passphrase derivations and XML bytes regardless of underlying host OS defaults.
   - Invariant Culture Globalization: Number formatting (decimal dot separator), ISO 8601 timestamp formatting, date serialization, and case folding must be immune to ambient OS cultural settings (such as `tr-TR` dotted/dotless I or `de-DE` comma separators).
2. **Cross-Platform Host Seam:** A clean abstraction layer decoupling presentation, user notifications, secure credential prompts, clipboard hygiene, and Safe Mode state from any specific windowing or OS framework (WinForms, GTK, AppKit, Cocoa, Android SDK, iOS UIKit).

---

## 2. Decision and Architecture

### 2.1 Host Platform Qualification Engine (`HostPlatformQualification`)

Implemented in `Broiler.Fond.Kernel.Hosting`:
- **`VerifyAesGcmSupport()`:** Asserts `AesGcm.IsSupported`. Runs an in-memory encryption and decryption round-trip with a 256-bit key, 96-bit nonce, 16-byte authentication tag, and authenticated associated data (AAD). Fails closed if the algorithm is unavailable or authentication fails.
- **`VerifyUnicodeNormalization()`:** Tests multiple decomposed vs composed Unicode strings (e.g. `München`, `Zürich`, `Café`, complex combining diacritics), asserting that normalization to `NormalizationForm.FormC` and strict UTF-8 encoding without BOM yields exact canonical byte sequences. Verifies rejection of invalid surrogate sequences.
- **`VerifyInvariantCultureFormatting()`:** Tests that money amounts (`1234.56`), ISO 8601 timestamps (`2026-10-19T12:00:00Z`), and date values (`2026-10-19`) render with exact invariant dot and hyphen tokens across varied active thread cultures, verifying case conversion invariance (`StringComparer.OrdinalIgnoreCase`).
- **`Qualify()` & `EnsureQualified()`:** Produces a structured `HostPlatformQualificationResult` recording all checks. If any check fails, `EnsureQualified()` throws a typed `HostPlatformQualificationException` and halts initialization.

### 2.2 Cross-Platform Host Seam Contracts

Implemented in `Broiler.Fond.Kernel.Hosting`:
- **`IHostPlatformSeam`:**
  - Identifies the host environment: `HostName` (e.g., `"Windows.Desktop"`, `"Linux.Desktop"`, `"MacOS.Desktop"`), `HostPlatformType` (`Windows`, `Linux`, `MacOS`, `Android`, `IOS`, `Other`).
  - Manages Safe Mode state: `IsSafeModeActive`, `EnterSafeMode(string reason)`, `ExitSafeMode()`, and event `SafeModeChanged`.
  - Exposes sub-interfaces: `IHostNotificationChannel`, `IHostCredentialPrompt`, `IHostClipboardService`.
- **`IHostNotificationChannel`:** Host-neutral notification dispatch:
  - `Notify(HostNotificationLevel level, string title, string message)`.
  - Levels: `Info`, `Warning`, `Error`, `SecurityAlert`.
- **`IHostCredentialPrompt`:** Host-neutral credential acquisition:
  - Coordinates secure PIN and passphrase entry into volatile `FinTsSessionCredential` instances.
  - Enforces prompt cancellation and timeout deadlines.
- **`IHostClipboardService`:** Secure clipboard hygiene:
  - `SetTransientText(string text, TimeSpan timeout)`: Copies text with an automatic timed wipe.
  - `Clear()`: Immediately zeroes or clears the system clipboard.

---

## 3. Consequences

### Positive
- Guarantees runtime cryptographic and formatting invariants before any profile archive is touched.
- Provides a unified contract for presentational hosts, enabling future Linux/macOS desktop and mobile hosts without altering the kernel.
- Preserves the BCL-only constraint: no native libraries, platform annotations, or OS-specific `#if` compiler directives in `Broiler.Fond.Kernel`.

### Negative / Trade-offs
- Qualification runs at application startup, adding an initial CPU preflight check (~1–3 ms).
- Abstract host seam requires each host implementation to provide concrete adapters.
