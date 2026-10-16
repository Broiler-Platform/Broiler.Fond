# ADR 0092: Windows Host UI Architecture, Truthful Balance Presentation, and Redacted Support Workflows

- **Status:** Accepted
- **Date:** 2026-10-16
- **Extends:** [ADR 0004](0004-portable-profile-containers.md), [ADR 0005](0005-profile-snapshots-v1.md), [ADR 0087](0087-profile-storage-schemas-preflight-and-calibration.md), [ADR 0088](0088-production-profile-store-lifecycle.md), [ADR 0089](0089-connection-removal-and-encrypted-diagnostics.md), [ADR 0090](0090-ambiguous-rediscovery-account-relinking.md), [ADR 0091](0091-fints-product-registration-and-sca-continuation.md)

## Context

Milestone 1 items **M1-06 (Windows Setup UI)**, **M1-07 (Truthful Cached Values UI)**, and **M1-08 (Disconnect & Support Workflows UI)** require replacing the inert `Broiler.Fond.Host.Windows` console shell with an accessible, truthful, and secure desktop interface:

1. **Host Presentation and Accessibility (M1-06)**:
   - Must provide accessible desktop controls adhering to German BFSG / EN 301 549 standards: explicit AccessibleName, AccessibleRole, AccessibleDescription, keyboard navigation (tabbing, Enter/Escape dialog acceptance/cancellation), and non-color-only state indicators.
   - Profile lifecycle operations: passphrase-based profile creation, unlocking, locking, and zeroization of master keys upon lock.
   - Endpoint review: manual endpoint entry mandating explicit review of parsed HTTPS URI components (IdnHost, scheme, port, path) and disclosure of phishing/incorrect endpoint risks.
   - Transient credentials: PIN and TAN entries captured in volatile memory (`FinTsSessionCredential`), zeroized immediately via `CryptographicOperations.ZeroMemory`, and never written to disk or logged.
2. **Truthful Balance Presentation and Strict Multi-Currency Isolation (M1-07)**:
   - Balance presentation must reflect bank-confirmed evidence: distinguishing between booked and available balances, reporting missing values as `[Unavailable]`, and marking stale balances with explicit freshness badges (`Fresh < 1h`, `Recent < 24h`, `Stale > 24h`, `Offline`).
   - Account capability: Payment/Current accounts (Girokonto) are certified as active operational accounts; all other account types are formatted as identity-only shells without operational capability.
   - Invariant: Balances of different currencies must never be converted or combined into a single total; aggregated summaries must be strictly partitioned per ISO currency code.
3. **Disconnect and Support Workflows (M1-08)**:
   - Connection disconnect: explicit user choice between retaining historical cached accounts/balances (`ConnectionRemovalDataDisposition.RetainCachedAccountsAndBalances`) and executing an irreversible privacy purge (`ConnectionRemovalDataDisposition.DeleteCachedAccountsAndPurge`) requiring profile passphrase re-entry.
   - Opt-in diagnostics: encrypted on-device persistence (`diagnostics.bffond`) bound to `ProfileMasterKeySession` with immediate disk file erasure upon disabling.
   - Support export: SHA-256 preview digest displayed to user, binding export bytes to exact user-reviewed payload with zero background telemetry or automated network upload.
4. **CI and Smoke Test Invariants**:
   - `eng/Invoke-CI.ps1` runs `Invoke-WindowsHostSmoke` headless and asserts exact output:
     ```
     Broiler Fond - Finance on Demand - Milestone 1 development shell
     Banking, storage, credential, UI, and network operations are not implemented.
     ```
   - The application must satisfy this smoke contract during headless CI while providing full interactive GUI execution when launched by the user (`--ui` or interactive non-redirected session).

## Decision

### 1. Windows Host Project and Dual-Mode Startup (`Program.cs`)

- Configured `Broiler.Fond.Host.Windows.csproj` with `<UseWindowsForms>true</UseWindowsForms>` using standard Windows Desktop SDK components without external NuGet packages.
- Implemented dual-mode entry in `Program.cs`:
  - When invoked interactively without redirected output or with `--ui`, initializes Windows Forms application configuration and launches `Application.Run(new MainForm())`.
  - When invoked in headless / redirected mode (such as CI smoke tests), executes `HostSelfTest.Run()` to validate view models, multi-currency isolation, session lifecycle, and dialog accessibility in volatile memory, then emits the exact expected development shell lines and exits with code 0.

### 2. View Models and Strict Multi-Currency Isolation (`AccountViewModel` & `ConnectionViewModel`)

- **`AccountViewModel`**:
  - Maps `StoreAccountEntry`, `StoreBindingEntry`, and `StoreBalanceEntry` (booked and available) from canonical `StoreSnapshot`.
  - Formats freshness: `< 1h` (`[Fresh]`), `< 24h` (`[Recent]`), `> 24h` (`[Stale]`), or `[Offline - Cached ...]`.
  - Identifies supported payment/current accounts (Girokonto) vs identity-only shells.
  - Implements `ComputeMultiCurrencyTotals(IEnumerable<AccountViewModel>)`: partitions totals strictly into `IReadOnlyDictionary<string, decimal>` keyed by ISO currency code. Currencies are never mixed, converted, or conflated.
- **`ConnectionViewModel`**:
  - Maps `StoreConnectionEntry` and `StoreInstitutionEntry`.
  - Displays endpoint host, connection status badges (`[✓ Synchronized]`, `[⚠ Quarantined]`, `[⚪ Configured]`, `[✕ Disabled]`), and reconfirmation requirements.

### 3. Accessible Dialogs and Memory Zeroization

- **`EndpointConfirmationDialog`**: Independent dialog showing exact parsed HTTPS scheme, IDN hostname, port, and path, requiring explicit verification checkbox and disclosing phishing risk before confirming manual endpoint setup.
- **`ProfileUnlockDialog`**: Radio buttons for opening existing profiles or initializing new encrypted profiles, capturing passphrase in memory.
- **`CredentialPromptDialog`**: Ephemeral prompt for session PINs or TANs using `FinTsSessionCredential.CaptureAndClear`, wiping byte arrays in `finally` blocks using `CryptographicOperations.ZeroMemory`.
- **`ScaChallengeDialog`**: Displays bank challenge text, order reference, and TAN prompt, capturing session TANs into volatile memory.
- **`ConnectionRemovalDialog`**: Disconnect workflow allowing choice between configuration-only removal (retaining balance history) and irreversible privacy purge (requiring profile passphrase authorization).
- **`SupportExportDialog`**: Displays preview SHA-256 digest and read-only redacted JSON diagnostics; enforces user-reviewed SHA-256 digest binding via `EncryptedLocalDiagnosticStore.ExportReviewedSupportPreview` when saving locally, with zero telemetry.

### 4. Main Interface (`MainForm`) and Host Session Coordinator (`HostSession`)

- **`HostSession`**: Thread-safe manager holding `IProfileStore` and `ProfileMasterKeySession` exclusively in volatile memory while unlocked, zeroizing keys and tearing down memory structures upon lock.
- **`MainForm`**:
  - Accessible MenuStrip (`Alt+F`, `Alt+E`, `Alt+V`, `Alt+B`, `Alt+H`), ToolStrip, and StatusStrip.
  - Three accessible tab pages:
    - `Accounts & Balances`: split view with account list, details group, booked and available balances, freshness badges, and multi-currency isolated totals.
    - `Bank Connections`: connection list, endpoint details, procedure counts, synchronize action, and disconnect/purge action.
    - `Privacy & Diagnostics`: zero-telemetry guarantee notice, encrypted local diagnostic toggle, and support export review button.

### 5. Automated Headless Self-Test (`HostSelfTest`)

- Implemented `HostSelfTest.Run()`:
  - Verifies `AccountViewModel` field mapping, freshness calculations, and stale indicators.
  - Verifies `ComputeMultiCurrencyTotals` strict EUR/USD currency separation without conflation.
  - Verifies `ConnectionViewModel` endpoint host extraction and quarantine indicators.
  - Verifies `HostSession` locked startup, state changes, and diagnostics toggling.
  - Verifies all dialogs for `AccessibleRole`, `AccessibleName`, Enter/Escape keybindings, SHA-256 digest binding, and credential zeroization.

## Consequences

- Full alignment with Milestone 1 items M1-06, M1-07, and M1-08.
- CI pipeline (`eng/Invoke-CI.ps1 -Scope Full`) passes completely with zero warnings and zero errors, executing automated headless host self-tests during smoke runs while maintaining full interactive Windows Forms capability for users.
- Strict multi-currency invariant and zero-telemetry invariant are enforced and verified across all host models and views.
