# ADR 0105: Windows Host Multibank Management, Freshness & Support Export

## Status
Accepted

## Context
Milestone 3 ("Reliable multibank read-only beta") expands Broiler Fond from single-institution read operations to robust multibank management across independent financial institutions.

In accordance with core product principles:
1. **Strict UI Separation:** All presentation and WinForms lifecycle code resides in `Broiler.Fond.Host.Windows`. The core banking kernel (`Broiler.Fond.Kernel`) remains strictly platform-independent and BCL-only, without UI or native dependencies.
2. **Truthful Connection & Freshness States:** Connection states must be explicitly communicated without ambiguity (`Connected`, `ReauthenticationRequired`, `TemporarilyUnavailable`, `ChallengeRequired`, `Authenticating`, `Configured`, `Disabled`). Accounts must display truthful freshness indicators (`Fresh`, `Recent`, `Stale`) and exact balance timestamps without conflating missing data with zero.
3. **Explicit Connection Management Controls:** Users must be able to add new institutions, reauthenticate connections requiring fresh credentials, edit institution endpoints (with mandatory phishing/hostname confirmation), trigger per-account/per-connection refreshes, and safely disconnect/purge connection data.
4. **Zero Telemetry Support Export with Byte-Bound Preview:** When requesting diagnostics, users must see a full in-memory redacted preview and SHA-256 content digest before writing to disk, ensuring zero secret leakage and zero remote transmission.

## Decisions

### 1. Multibank Dashboard & Freshness Presentation
- `ConnectionViewModel`:
  - Formats connection state with accessible text badges (`[✓ Connected]`, `[⚠ Reauthentication Required]`, `[⏳ Temporarily Unavailable]`, `[🔐 Challenge Required]`, `[🔄 Authenticating]`, `[⚪ Configured - Not Synchronized]`, `[✕ Disabled]`).
  - Exposes endpoint host, IDN host, and confirmation status.
  - Identifies whether reauthentication or endpoint reconfirmation is required.
- `AccountViewModel`:
  - Evaluates balance retrieval timestamps against current time: `< 1h` (Fresh), `< 24h` (Recent), `> 24h` (Stale).
  - Preserves strict multi-currency isolation; balances in different currencies are never summed.
- `MainForm`:
  - Connections tab (`_tabConnections`) presents a complete multibank dashboard with all configured connections, endpoints, and operational statuses.
  - Accounts tab (`_tabAccounts`) displays individual accounts, freshness indicators, and dedicated per-account balance refresh controls (`_btnRefreshAccount`).

### 2. Connection Lifecycle Management Workflows
- **Add Connection:** Invokes `InstitutionSetupDialog` and verifies HTTPS endpoint before appending institution and connection entries to the profile store.
- **Reauthenticate Connection:** Invokes `CredentialPromptDialog` to capture ephemeral PIN, transitions connection state from `ReauthenticationRequired` to `Connected`, and saves the updated profile generation.
- **Edit Endpoint:** Validates new absolute HTTPS URI and mandates `EndpointConfirmationDialog` to confirm the exact target hostname before persisting to `StoreInstitutionsDocument`.
- **Remove Connection:** Invokes `ConnectionRemovalDialog` to let the user select between retaining cached balances or executing a verified privacy purge of account data via `IProfileStore.RemoveConnection`.

### 3. Redacted Diagnostic Support Export Preview
- `SupportExportDialog` renders the diagnostic JSON payload in a read-only scrollable view alongside its SHA-256 digest.
- Prominently displays the zero-telemetry guarantee notice.
- Saving to disk requires an explicit user-selected file path; no automated telemetry or background reporting channel exists.

### 4. Headless Verification (`HostSelfTest`)
- Verified in headless automated self-tests covering:
  - Multibank connection state formatting and freshness calculations.
  - Connection management workflows (add, reauthenticate, edit endpoint, remove).
  - Diagnostic support preview and SHA-256 digest validation.
  - Accessibility roles, names, and keyboard navigation across all host dialogs.

## Consequences

### Positive
- Users have full transparency and control over all bank connections and their operational states.
- Reauthentication and endpoint changes follow strict security boundaries without silent modifications.
- Diagnostic export guarantees zero secret persistence and zero remote upload.
- Fully accessible keyboard and screen-reader compliant interface.

### Negative / Trade-offs
- Endpoint modifications require explicit confirmation dialogs to protect users against phishing and misdirection.
