# ADR 0093: User-Reviewed Ambiguous-Rediscovery Account Relinking UI

- **Status:** Accepted
- **Date:** 2026-10-16
- **Extends:** [ADR 0006](0006-local-identity-allocation.md), [ADR 0007](0007-account-source-rediscovery.md), [ADR 0008](0008-account-values-and-exact-money.md), [ADR 0088](0088-production-profile-store-lifecycle.md), [ADR 0090](0090-ambiguous-rediscovery-account-relinking.md), [ADR 0092](0092-windows-host-ui-and-support-workflows.md)

## Context

Milestone 1 items **M1-03 (Identity & Relinking)** and **M1-06 (Windows Setup UI)** require providing a user-reviewed account relinking workflow:

1. **Quarantined and Ambiguous Discovery Candidates (ADR 0007 / ADR 0090)**:
   - When bank account locators change, are ambiguous, or arrive from a newly configured connection, the FinTS read ingestion pipeline does not silently conflate them with an existing logical account.
   - Instead, rediscovery isolates them into candidate bindings with state `Quarantined` or `Ambiguous` attached to a temporary candidate account shell.
2. **User-Reviewed Relinking Requirements**:
   - The user must be able to inspect candidate bindings and deliberate on relinking them to an existing logical account.
   - **Provenance preservation**: All historical balance snapshots and observations permanently retain their original `AccountBindingId` and `AccountIncarnationId`. Relinking changes bindings, never historical balance records.
   - **Candidate account pruning and privacy purge**: When relinking an unreferenced temporary candidate account shell with zero other bindings and no balance entries, the orphaned account must be pruned and superseded generations containing its temporary ID must be deleted via a verified privacy purge (`ExecutePrivacyPurge`), requiring profile passphrase entry.
   - **Host accessibility**: The relinking workflow must be accessible via keyboard and screen reader, providing clear visual and non-color-only state indicators, live deterministic outcome previews, and descriptive error messages.

## Decision

### 1. Accessible Relinking Dialog (`AccountRelinkingDialog`)

- Implemented `Broiler.Fond.Host.Windows.Dialogs.AccountRelinkingDialog` using Windows Forms and standard Windows Desktop SDK components:
  - **Accessibility**: Configured with `AccessibleRole = AccessibleRole.Dialog`, descriptive `AccessibleName`, `AccessibleDescription`, `AcceptButton = _btnRelink`, `CancelButton = _btnCancel`, and high-DPI scaling.
  - **Source binding selection (`_cboBindings`)**: Dropdown displaying all profile bindings with ID, state badge (`[Quarantined]`, `[Active]`, etc.), locator (IBAN or domestic account number/BLZ), and currently bound account name. Quarantined or ambiguous bindings are pre-selected by default.
  - **Target account selection (`_cboTargetAccounts`)**: Dropdown displaying candidate logical accounts with display name and currency. Pre-selects an account other than the binding's current account.
  - **Prune candidate account checkbox (`_chkPruneOrphan`)**: Checked by default, giving the user explicit control over whether unreferenced candidate account shells are pruned via privacy purge.
  - **Passphrase entry (`_txtPassphrase`)**: Password-masked text box capturing the profile passphrase required for encrypted generation commit and privacy purge.
  - **Live deterministic plan preview (`_txtPreview`)**: Evaluates `AccountRelinkingPlanner.PlanRelink` in real time whenever selection or inputs change:
    - If valid: displays source binding transition (`Quarantined ➔ Active`), target account ID/name, target incarnation ID, candidate pruning outcome, and whether a privacy purge or standard save will be executed. Enables the relink button when passphrase is entered.
    - If invalid: displays the exact domain validation error (e.g., currency incompatibility, duplicate active binding, or identical active binding) and disables the relink button.
  - **Execution**: On confirmation, calls `IProfileStore.RelinkAccountBinding(bindingId, targetAccountId, passphrase, pruneOrphanedAccount)`. On error, presents an accessible error message box and retains dialog state so the user can adjust inputs.

### 2. Main Window Integration (`MainForm`)

- **Accounts Tab**:
  - Added dedicated `_btnRelinkBinding` ("&Relink Binding...") inside `_grpAccountDetail`.
  - Dynamically enabled when an account with a binding is selected in an unlocked profile session.
  - Highlighted status hint in `_lblDetailFreshness`: appends `" (Click 'Relink Binding...' to resolve)"` when an account with a quarantined or ambiguous binding is selected.
- **Menu Bar**:
  - Added `&Relink Account Binding...` (shortcut `Ctrl+B`) under the `&Accounts` menu.
- **Session Refresh**:
  - Invokes `_session.ReloadData()` upon successful relinking, automatically updating account lists, multi-currency totals, and binding state indicators. Displays confirmation disclosing whether candidate account pruning occurred.

### 3. Automated Self-Test Verification (`HostSelfTest.cs`)

- Implemented `VerifyAccountRelinkingWorkflow` in `HostSelfTest`:
  - Validates `AccountRelinkingDialog` accessibility properties, initial selection, and control configuration.
  - Validates deterministic planning via `AccountRelinkingPlanner.PlanRelink`.
  - Executes live candidate relinking against an active test `ProfileStore`:
    - Relinks quarantined candidate binding 201 from temporary account 101 to primary account 100.
    - Verifies candidate account 101 is pruned from `accounts.xml` and `ledger.xml`.
    - Verifies binding 201 transitions to `State = "Active"` under account 100.
    - Verifies balance 300 permanently retains original binding ID 200.
    - Verifies privacy purge leaves exactly 1 generation file on disk.
    - Verifies `session.ReloadData()` reflects 1 consolidated account.

## Consequences & Verification

- Fulfills the remaining user-reviewed account relinking UI requirement for **M1-03** and completes interactive setup and management for **M1-06**.
- Verified in full CI:
  - Host smoke test and `HostSelfTest` execute warning-free and headless-compatible.
  - All 87 kernel test suites pass with zero warnings or errors.
  - Strict BCL-only boundary in `Broiler.Fond.Kernel` and `Broiler.Fond.Host.Windows` is preserved.
