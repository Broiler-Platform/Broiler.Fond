# ADR 0111: Windows Host Payment Initiation, Review Screen, Reconciliation and Safe Mode

- **Status:** Accepted
- **Date:** 2026-10-19
- **Related:** [ADR 0092](0092-windows-host-ui-and-support-workflows.md), [ADR 0106](0106-payment-domain-draft-revisions-and-sepa-validation.md), [ADR 0107](0107-verification-of-payee-vop-workflow.md), [ADR 0108](0108-durable-payment-state-machine-and-attempt-allocation.md), [ADR 0109](0109-sepa-credit-transfer-fints-codec-and-sca.md), [ADR 0110](0110-payment-storage-extension-and-audit-history.md), [docs/roadmap.md](../roadmap.md#m4--safe-sepa-money-movement), [docs/milestone-4.md](../milestone-4.md)

---

## 1. Context and Problem Statement

Milestone 4 delivers safe SEPA money movement in **Broiler Fond - Finance on Demand**. M4-01 through M4-05 implemented the complete kernel architecture: domain models, exact decimal EUR validation, Verification of Payee (VoP) per Regulation (EU) 2024/886, durable state machine with pre-send commit and duplicate guards, FinTS wire codecs with SCA continuation, and tamper-evident storage in `payments.xml`.

Milestone item **M4-06** completes the vertical slice by integrating these kernel capabilities into the Windows host application (`Broiler.Fond.Host.Windows`):
1. **Safe Mode Enforcement:** A user-visible, instant kill-switch toggle (`IsSafeModeActive`) that locks out all money movement and restricts the client to read-only operation.
2. **Payment Initiation & Real-time Validation:** A structured dialog (`PaymentInitiationDialog`) providing real-time IBAN MOD 97-10 checksum validation, exact decimal money input in EUR, structured/unstructured remittance fields, source account capability checks, and duplicate draft detection.
3. **Verification of Payee Presentation:** Transparent display of VoP verification outcomes (`Match`, `CloseMatch`, `NoMatch`, `Unavailable`, `NotApplicable`) and bank mismatch warnings before authorization, requiring explicit user acknowledgment.
4. **Unambiguous Review Screen:** A final review dialog (`PaymentReviewDialog`) showing exact debit account, recipient name and IBAN, amount, currency, execution type/date, remittance information, VoP confirmation, and bank warnings before triggering bank authorization.
5. **Payment Reconciliation & History View:** A dedicated Payments dashboard in `MainForm` listing payment attempts, their audit trails, and reconciliation status against booked/pending transactions, with durable fail-closed handling for `Unknown` outcomes.
6. **Accessibility & Zero Credential Persistence:** Full keyboard navigation, screen-reader semantics (`AccessibleName`, `AccessibleRole`), high-contrast and non-color-only status badges, and strict ephemeral credential handling (PINs and TANs never touch persistent storage or logs).

---

## 2. Decision and Architecture

### 2.1 Read-Only Safe Mode (`HostSession.IsSafeModeActive`)

- A global property `IsSafeModeActive` on `HostSession` (defaulting to `false`, toggled via menu and toolbar).
- When `IsSafeModeActive` is true:
  - The UI presents a prominent visual indicator: `[SAFE MODE - READ ONLY]`.
  - All payment initiation buttons, menus, and shortcuts (`&New Transfer...`, `&Retry Attempt...`) are immediately disabled.
  - No network write requests can be issued from the host.

### 2.2 Payment View Models

We introduce view models in `Broiler.Fond.Host.Windows.Models`:
1. **`PaymentDraftViewModel`:**
   Wraps `StorePaymentDraftEntry` and its current `StorePaymentDraftRevisionEntry`:
   - `DraftId`, `SourceAccountId`, `SourceAccountName`, `BeneficiaryName`, `BeneficiaryIban`, `BeneficiaryBicDisplay`.
   - `Amount`, `Currency`, `AmountDisplay`.
   - `RemittanceDisplay` (unstructured or creditor reference).
   - `ExecutionModeDisplay`, `ExecutionDateDisplay`.
   - `CreatedAtDisplay`, `UpdatedAtDisplay`.
2. **`PaymentAttemptViewModel`:**
   Wraps `StorePaymentAttemptEntry`:
   - `AttemptId`, `DraftId`, `EndToEndId`.
   - `State`, `StateDisplay` with non-color indicators (e.g. `[✓ Accepted]`, `[⏳ BankPending]`, `[⚠ Unknown]`, `[✗ Rejected]`).
   - `VoPStatusDisplay` (`[Match]`, `[CloseMatch]`, `[NoMatch]`, `[Unavailable]`, `[NotApplicable]`).
   - `BankReference`, `ResponseCode`, `StatusDescription`.
   - `IsUnknown`, `IsTerminal`, `CanRetry`, `CanReconcile`.
   - `CreatedAtDisplay`, `UpdatedAtDisplay`.
3. **`PaymentStateTransitionViewModel`:**
   Wraps `StorePaymentStateTransitionEntry` for the audit history log:
   - `Id`, `AttemptId`, `FromState`, `ToState`, `TimestampDisplay`, `Reason`, `BankCode`.

### 2.3 Payment Initiation Dialog (`PaymentInitiationDialog`)

- **Source Account Selection:** Dropdown populated exclusively with supported payment accounts (`IsSupportedPaymentAccount`) that have active bindings.
- **Beneficiary Details:**
  - Recipient Name: validated non-empty.
  - Recipient IBAN: real-time MOD 97-10 check using `SepaIbanValidator`. Shows instant feedback `[✓ Valid SEPA IBAN]` or `[✗ Invalid IBAN Checksum]`.
  - Recipient BIC: optional, validated format if provided.
- **Amount & Currency:**
  - Mandatory EUR currency.
  - Exact decimal input parsing (using invariant and user culture) with strict scale <= 2 and positive value (> 0.00 EUR).
- **Remittance Information:**
  - Radio toggle between Unstructured (up to 140 chars) and ISO 11649 Creditor Reference (RF format).
- **Duplicate Guard Check:**
  - Queries active and pending drafts in `HostSession`; warns user if an identical draft (same source, payee IBAN, and amount within 24h) exists, requiring explicit confirmation to proceed.

### 2.4 Verification of Payee & Review Dialogs

- **`PayeeVerificationView` (or integrated VoP step):**
  - Displays the result of bank VoP check:
    - `PayeeMatch`: Payee name matches account records.
    - `PayeeCloseMatch`: Close match detected, displays bank-suggested corrected name and reason.
    - `PayeeNoMatch`: Warning displayed; user must explicitly acknowledge the mismatch before continuing.
    - `PayeeCheckUnavailable` / `PayeeCheckNotApplicable`: Status and required statutory warning retained.
- **`PaymentReviewDialog`:**
  - Read-only review summarizing all payment parameters: Source Account, Payee Name, Payee IBAN, Payee BIC, Amount (e.g. `450.50 EUR`), Remittance, Execution Mode/Date, VoP status and any bank warnings.
  - Requires user to click `&Authorize Payment...` (Enter) or `Cancel` (Escape).

### 2.5 Payments Tab in `MainForm`

- A new tab `Payments (&5)` in `MainForm`:
  - **Toolbar Action:** `&New Payment...` (disabled if Safe Mode is active or profile is locked).
  - **Attempts List (`ListView`):** Displays Date, End-to-End ID, Payee, Amount, State badge, and VoP result.
  - **Attempt Detail GroupBox:** Shows full attempt metadata, VoP details, bank reference, return code, and the append-only transition audit history.
  - **Reconciliation Actions:**
    - `&Reconcile with Transactions...`: matches accepted/bank-pending attempts against booked/pending statement records matching `EndToEndId` or amount/date/creditor.
    - `&Retry Transfer...`: for failed attempts, pre-fills a fresh draft from the original attempt parameters without mutating historical records.

### 2.6 Accessibility, Safe Mode & Headless Self-Test (`HostSelfTest`)

- All dialogs set `AccessibleRole = AccessibleRole.Dialog`, meaningful `AccessibleName`, `AcceptButton` (Enter), and `CancelButton` (Escape).
- Status representations use alphanumeric tags (e.g. `[✓]`, `[⏳]`, `[⚠]`, `[✗]`) alongside colors.
- `HostSelfTest` executes headless verification:
  1. Safe mode toggle state, toolbar disablement, and execution blocking.
  2. Real-time IBAN validation in `PaymentInitiationDialog`.
  3. Exact decimal money input parsing and formatting.
  4. VoP result presentation and warning acknowledgment in `PaymentReviewDialog`.
  5. Payment reconciliation matching against statement transactions.
  6. Zero credential retention across the execution lifecycle.

---

## 3. Consequences

### Positive
- Users have full transparency and control before any payment order crosses the bank network boundary.
- Regulation (EU) 2024/886 VoP outcomes and mismatch warnings are clearly visible.
- Safe Mode provides an immediate, user-controlled safeguard against accidental transactions.
- Zero credential persistence preserves privacy and compliance.
- Durable audit log and reconciliation views provide tamper-evident record keeping.

### Negative / Trade-offs
- Windows Forms requires manual control styling and DPI layout management.
- Headless testing requires simulating dialog parameter validation and event dispatching.
