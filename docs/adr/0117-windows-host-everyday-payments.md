# ADR 0117: Windows Host Everyday Payments, Standing Orders, Direct-Debit Returns and Address Book

- **Status:** Accepted
- **Date:** 2026-10-19
- **Related:** [ADR 0092](0092-windows-host-ui-and-support-workflows.md), [ADR 0111](0111-windows-host-payment-initiation-and-safe-mode.md), [ADR 0112](0112-own-account-and-instant-sepa-transfers.md), [ADR 0113](0113-scheduled-sepa-credit-transfers.md), [ADR 0114](0114-standing-orders-management.md), [ADR 0115](0115-direct-debit-return-and-mandate-revocation.md), [ADR 0116](0116-beneficiary-templates-and-address-book.md), [docs/roadmap.md](../roadmap.md#m5--everyday-payments), [docs/milestone-5.md](../milestone-5.md)

---

## 1. Context and Problem Statement

Milestone 5 delivers everyday consumer payments in **Broiler Fond - Finance on Demand**:
- M5-01: Own-account transfers and instant SEPA credit transfers (SCT Inst).
- M5-02: Scheduled SEPA credit transfers and bank order lifecycle.
- M5-03: Standing orders management (list, setup, modify, delete).
- M5-04: Direct-debit return writes and mandate revocation.
- M5-05: Beneficiary templates & address book in profile store (`templates.xml`).

Milestone item **M5-06** completes the user-facing integration into the Windows host application (`Broiler.Fond.Host.Windows`):
1. **Instant SEPA Credit Transfer Option:** Enable instant credit transfers with transparent fee notices, timeout feedback, bank limit verification, and strict anti-downgrade enforcement (fail-closed if instant clearing is unavailable).
2. **Scheduled Payment Date Selector:** Allow future-dated execution dates validated against bank-advertised advance notice windows (`MinimumAdvanceDays`, `MaximumAdvanceDays`).
3. **Beneficiary Templates & Address Book:** Pre-fill transfer parameters from encrypted templates without bypassing SEPA validation, VoP, review, or bank SCA; prompt to save new payees into the address book.
4. **Standing Orders Management:** Provide an accessible view to inspect active standing orders, configure recurrence frequencies and execution days, and initiate setup, edit, or cancellation requests.
5. **Direct-Debit Return Writes:** Provide a review action on eligible direct-debit transactions to request statutory 8-week unconditional or 13-month unauthorized returns, displaying reason codes, deadline calculations, and bank processing warnings.
6. **Accessibility, Safe Mode & Headless Self-Tests:** Strict safe mode lockout (`IsSafeModeActive`) on all money-movement actions, complete keyboard/screen-reader accessibility, and headless automated self-tests in `HostSelfTest`.

---

## 2. Decision and Architecture

### 2.1 Host Session Extensions (`HostSession`)

`HostSession` is extended to support Milestone 5 capabilities:
- **Beneficiary Templates:**
  - Exposes `IReadOnlyList<BeneficiaryTemplateViewModel> BeneficiaryTemplates`.
  - Exposes `SaveBeneficiaryTemplate(...)` and `SoftDeleteBeneficiaryTemplate(...)` mutating the encrypted profile archive.
- **Capability Lookups:**
  - `GetInstantPaymentCapability(ulong accountId)`: inspects account BPD capabilities for `HKIPB` / `HKSIP`.
  - `GetScheduledPaymentCapability(ulong accountId)`: inspects account BPD capabilities for `HKCME`.
  - `GetStandingOrderCapability(ulong accountId)`: inspects account BPD capabilities for `HKCDE` / `HKCDB`.
  - `GetDirectDebitReturnCapability(ulong accountId)`: inspects account BPD capabilities for `HKDRW` / `HKDMB`.
- **Standing Orders & Direct-Debit Returns:**
  - Exposes `IReadOnlyList<StandingOrderViewModel> StandingOrders`.
  - Exposes `RecordDirectDebitReturn(...)` and `SubmitStandingOrderAction(...)`.

### 2.2 Payment Initiation Dialog Enhancements (`PaymentInitiationDialog`)

The `PaymentInitiationDialog` is enhanced with:
1. **Template Selector (`_cmbTemplates`):**
   - Populated from `session.BeneficiaryTemplates`.
   - Selecting a template populates Recipient Name, IBAN, BIC, Remittance, Amount, and preferred Execution Mode.
   - Non-bypassing invariant: populating fields does *not* bypass subsequent validation, VoP, review, or SCA.
2. **Save to Address Book Checkbox (`_chkSaveAsTemplate`):**
   - Checked by default when a new recipient is entered; allows the user to label and save the payee to their encrypted profile.
3. **Execution Mode Radio Group (`_radStandardTransfer`, `_radInstantTransfer`):**
   - Standard SEPA vs Instant SEPA (SCT Inst).
   - If Instant is selected:
     - Disables if the debit account does not advertise instant capability.
     - Displays bank fee notice (e.g. `Fee: 0.00 EUR (Included)`) and execution timeframe (`<= 10 seconds`).
     - Validates that amount does not exceed bank-advertised instant limit.
     - Strict anti-downgrade rule: will never fall back to standard credit transfer without explicit user confirmation.
4. **Scheduled Execution Date Picker (`_dtpExecutionDate`):**
   - When execution date > today, validates against the account's scheduled payment capability:
     - Rejects dates exceeding `MaximumAdvanceDays` or before `MinimumAdvanceDays`.
     - Instant mode forces execution date to today.

### 2.3 Standing Orders Management Dialog (`StandingOrdersDialog`)

An accessible dialog dedicated to managing standing orders:
- **Standing Orders List:**
  - Displays Order ID, Beneficiary Name & IBAN, Amount, Recurrence Frequency (Monthly, Bi-Monthly, Quarterly, Semi-Annually, Annually), Execution Day of Month, First Execution Date, Last Execution Date, and Status badge.
- **Actions:**
  - `&New Standing Order...`: opens creation dialog with recurrence rules.
  - `&Edit Standing Order...`: modifies amount, execution day, or cadence.
  - `&Cancel Standing Order...`: initiates cancellation with warning confirmation.
- **Safe Mode Enforcement:** All action buttons are disabled when `session.IsSafeModeActive` is true.

### 2.4 Direct-Debit Return Dialog (`DirectDebitReturnDialog`)

When a user selects a direct-debit transaction in the Transactions tab:
- If the transaction is a direct debit and within statutory deadlines:
  - An action button `&Return Direct Debit...` (or `Lastschrift widersprechen...`) is enabled.
- The `DirectDebitReturnDialog` displays:
  - Original Creditor Name, IBAN, Creditor ID, Mandate ID, Amount, and Booking Date.
  - Return Reason Selection:
    - `MS02` / `MS03`: Unconditional return (within statutory 8-week / 56-day window).
    - `MD01` / `MD06`: Unauthorized debit return (within 13-month / 396-day window).
  - Calculated statutory deadline and days remaining.
  - Explicit warning notice:
    > "Requesting a direct-debit return instructs your bank to reverse this posting. This request cannot be undone. Your bank will verify the mandate status. Do not use this function if you authorized the payment and have no contractual basis for reversal."
  - Bank submission requires confirmation; disabled if Safe Mode is active.

### 2.5 Headless Self-Tests (`HostSelfTest`)

`HostSelfTest` is extended to verify all M5-06 everyday payment features:
1. Safe mode blocking on instant payments, scheduled payments, standing orders, and direct debit returns.
2. Beneficiary template picker pre-filling and non-bypassing verification.
3. Instant payment capability checks, limit verification, and anti-downgrade guarantees.
4. Scheduled payment lead-time validation.
5. Standing order model creation, frequency calculation, and dialog lifecycle.
6. Direct-debit return deadline enforcement and statutory warning presentation.
7. Keyboard navigation, accessible roles, and color-independent badges.

---

## 3. Consequences

- Everyday payments in the Windows host provide full parity with commercial banking software while preserving Broiler Fond's strict privacy and security invariants:
  - Ephemeral session credentials (PINs and TANs never saved).
  - Exact decimal EUR money arithmetic.
  - Transparent Verification of Payee (VoP) per EU 2024/886.
  - Complete zero-telemetry local operation.
