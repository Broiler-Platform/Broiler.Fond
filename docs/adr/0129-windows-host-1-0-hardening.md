# ADR 0129: Windows Host 1.0 Hardening, Shell Integration, Legal Disclosures & Headless Verification

- **Status:** Accepted
- **Date:** 2026-10-19
- **Related:** [ADR 0003](0003-operating-model-and-trust-boundary.md), [ADR 0092](0092-windows-host-architecture-and-headless-verification.md), [ADR 0127](0127-accessibility-and-localization-hardening.md), [ADR 0128](0128-windows-packaging-signing-and-release-lifecycle.md), [docs/milestone-7.md](../milestone-7.md#m7-06-windows-host-10-hardening-shell-integration--headless-verification)

---

## 1. Context and Problem Statement

For the Windows 1.0 milestone completion (Milestone 7), the host presentation layer (`Broiler.Fond.Host.Windows`) requires complete release-ready hardening, user disclosures, and automated compliance auditing:
1. **Legal, Trust Boundary & Registration Disclosures:** The application must present authoritative disclosures explaining the purely user-operated operating model (ADR 0003), zero telemetry policy, volatile credential hygiene, DK FinTS 3.0 accredited product registration code (ADR 0091), and BFSG/BFSGV accessibility conformance statements.
2. **Interactive Profile Backup & Restore:** Users must be able to export encrypted backups and restore them into designated directories with cryptographic preflight validation.
3. **Safe Mode & Shell Indicators:** Safe Mode status must provide prominent dual non-color visual indicators on the main window status bar (`[🛡️ SAFE MODE: ACTIVE (READ-ONLY)]` / `[✓ Safe Mode: Inactive]`).
4. **Headless BFSG Accessibility Audit:** The automated `HostSelfTest` suite must audit all host forms and dialogs using `BfsgAccessibilityAuditor` to prove 100% accessible control names and dialog roles.

---

## 2. Decision and Architecture

### 2.1 About & Legal Disclosures Dialog (`AboutDialog`)

A multi-tab accessible dialog (`AboutDialog.cs`) was implemented in `Broiler.Fond.Host.Windows.Dialogs`:
- **Overview & Trust Boundary:** Reaffirms the ADR 0003 trust boundary—no intermediary servers, no cloud aggregators, no analytics, and volatile session-only credential storage zeroized via `CryptographicOperations.ZeroMemory`.
- **FinTS Registration:** Discloses the registered DK product identifier (`BROILER-FOND`), product version (`1.0`), and registration code (`9999999999`) from `KernelProduct.FinTsRegistration`.
- **Accessibility Declaration:** Documents BFSG and BFSGV § 17 conformance with European Standard EN 301 549 and WCAG 2.1 Level AA.
- **Legal & License:** Confirms the BCL-only pure .NET 10 runtime boundary and provides license terms.

### 2.2 Profile Backup & Restore UI Workflows

1. **Backup Export:** `HostSession.ExportBackup(destinationPath)` copies and verifies the active generation archive via `ProfileStore.ExportBackup`.
2. **Restore Dialog (`RestoreBackupDialog`):**
   - Prompts for source backup file (`.bffond`), destination profile directory, and profile passphrase.
   - Executes cryptographic preflight: verifies the 96-byte header, derives master key session, authenticates all AES-256-GCM frames, and validates snapshot XML schema invariants.
   - Atomically copies the validated generation file to destination (`ProfileStore.FormatGenerationFileName(generation)`) and unlocks the profile in `HostSession`.

### 2.3 Shell Integration, Menu Additions & Safe Mode Polish

- **File Menu (`&File`):**
  - Added `&Export Encrypted Backup...` (`Ctrl+Shift+E`).
  - Added `&Restore from Encrypted Backup...` (`Ctrl+Shift+R`).
- **Help Menu (`&Help`):**
  - Added `&About Broiler Fond...` (`F1`).
  - Added `&Accessibility (BFSG) Statement...`.
  - Added `&Legal & Licenses...`.
- **Safe Mode Status Bar:**
  - StatusStrip indicator updated with dual cues: `[🛡️ SAFE MODE: ACTIVE (READ-ONLY)]` and `[✓ Safe Mode: Inactive]`, accompanied by descriptive `AccessibleName` and `AccessibleDescription`.

### 2.4 Headless BFSG Accessibility Verification

`HostSelfTest.VerifyWindowsHost10Hardening` integrates `BfsgAccessibilityAuditor.Audit`:
- Audits `AboutDialog`, `RestoreBackupDialog`, and `MainForm`.
- Verifies zero defects across all controls (accessible names, roles, and dual visual indicators).
- Executes backup export and restore roundtrip, verifying full state reload.

---

## 3. Consequences

### Positive
- Fully accessible, auditable, and self-contained Windows 1.0 application shell.
- Truthful, transparent legal and regulatory disclosures available in-app.
- Reliable encrypted profile backup and restore workflows preventing data loss.
- 100% automated BFSG/BFSGV accessibility verification in CI.

### Negative / Trade-offs
- Windows Forms requires explicit accessibility property assignment on every newly created control.
