# Milestone 5 — Everyday payments

- **Brand:** Broiler
- **Product:** Fond - Finance on Demand
- **Technical name:** `Broiler.Fond`

**Status:** Complete (Milestones 0, 1, 2, 3, 4, and 5 complete).

**Last updated:** 2026-10-19

The [roadmap](roadmap.md#m5--everyday-payments) defines the product specification.
This backlog breaks Milestone 5 into reviewable increments; completion of one increment does not
imply authorization for production distribution.

---

## 1. Outcome

Common consumer money-management actions work where each bank advertises them:
transfers between the user's own accounts, instant SEPA credit transfers with explicit selection
and no silent downgrade, scheduled transfers, standing-order lifecycle (list, create, edit, delete),
direct-debit return writes where explicitly advertised by the bank, protected beneficiary templates,
and bank order status reconciliation.

---

## 2. Milestone Principles and Invariants

- **Capability-gated execution:** Everyday payment actions (instant, scheduled, standing orders, direct debit returns)
  are enabled strictly when advertised by bank parameter data (BPD) and account parameter data (UPD). No heuristic
  or bank-name matching enables a write boundary.
- **No silent downgrades:** Instant SEPA payments cannot be silently converted to standard SEPA transfers, or vice versa.
  If an instant transfer is rejected or unavailable, the user must explicitly decide how to proceed.
- **Strict BCL-only kernel boundary:** `Broiler.Fond.Kernel` has zero NuGet packages or native dependencies;
  all payment schemas, wire codecs, XML parsing, and domain validations rely solely on .NET 10 shared framework APIs.
- **Exact decimal money:** Exact decimal `Money` representation in EUR; zero floating-point arithmetic.
- **Ephemeral session credentials & zero persistence:** PINs and TANs are held only in bounded in-memory
  buffers during active transactions; credentials never touch disk, profile archives, or diagnostic logs.
- **Pre-send durable commit:** Every order or write attempt allocates an immutable entity ID and unique
  `EndToEndId` committed durably to the profile store *before* network transmission.
- **Durable `Unknown` outcome & zero automatic retries:** A write that may have reached a bank is resolved
  via reconciliation evidence, never automatically resent.
- **Protected templates as financial data:** Beneficiary templates are encrypted within the profile archive;
  using a template never bypasses current SEPA validation, Verification of Payee (VoP), review, or bank SCA.

---

## 3. Work Breakdown Structure (M5-01 through M5-06)

### M5-01: Own-Account Transfers & Instant SEPA Credit Transfers

- [x] Implement own-account transfers and instant SEPA credit transfers ([ADR 0112](adr/0112-own-account-and-instant-sepa-transfers.md)):
  - Own-account transfer path: source and destination account validation within same user profile; skip external clearing delays where internal bank transfer is supported.
  - SEPA Instant credit transfers: BPD capability check (`HKIPB` / `HKSIP` or pain instant indicator), maximum amount limit verification, execution timeframe guarantees, and explicit fee/warning disclosures.
  - Strict anti-downgrade guard: fail-closed if instant clearing is unavailable; forbid automatic fallback to standard SEPA.
- [x] Add unit verification suite covering own-account validation, instant limits, pain XML instant schema attributes, and downgrade rejection (`InstantPaymentTests`).

### M5-02: Scheduled SEPA Credit Transfers & Bank Order Lifecycle

- [x] Implement scheduled SEPA credit transfers ([ADR 0113](adr/0113-scheduled-sepa-credit-transfers.md)):
  - Scheduled payment domain model: future execution date bounds, bank minimum/maximum advance lead times (`HKCME` / `HKCIE`).
  - Wire codecs: FinTS 3.0 scheduled transfer request and cancellation segments (`HKCME` / `HKCDB`).
  - Order state tracking: pending execution, bank-confirmed cancellation, and status reconciliation.
- [x] Verify scheduled order codecs, date boundary validation, cancellation flows, and durable state updates (`ScheduledPaymentTests`).

### M5-03: Standing Orders Management (List, Create, Modify, Delete)

- [x] Implement standing orders management ([ADR 0114](adr/0114-standing-orders-management.md)):
  - Domain model: recurrence frequency (Monthly, Two-Monthly, Quarterly, Semi-Annually, Annually), execution day of month, start/end dates.
  - FinTS 3.0 standing order wire segments: query list (`HKCDB`), setup (`HKCDE`), edit/modify, and delete/cancel.
  - Synchronization: reconcile local standing order projections against bank-authoritative standing order lists.
- [x] Verify standing order wire codecs, cadence calculations, modification flows, and deletion confirmation (`StandingOrderTests`).

### M5-04: Direct-Debit Return Writes & Mandate Revocation

- [x] Implement direct-debit return writes and mandate revocation ([ADR 0115](adr/0115-direct-debit-return-and-mandate-revocation.md)):
  - Direct-debit return model: statutory 8-week unconditional return and 13-month unauthorized debit return; reason codes (`MS02`, `MS03`, `MD01`, `MD06`).
  - FinTS wire codec: return request submission (`HKDRW` / `HKDMB`) gated by BPD capability advertisement.
  - Truthful state handling: return request remains pending/unknown until authoritative statement evidence confirms reversal.
  - Mandate revocation notice: client-recorded revocation sent to bank where supported.
- [x] Verify direct-debit return codecs, statutory deadline enforcement, BPD capability checks, and audit trails (`DirectDebitReturnTests`).

### M5-05: Beneficiary Templates & Address Book in Profile Store

- [x] Extend profile store with protected beneficiary templates ([ADR 0116](adr/0116-beneficiary-templates-and-address-book.md)):
  - Schema extension: `templates-v1.xsd` in profile ZIP archive for encrypted local template storage.
  - Invariant: applying a template populates payment draft fields, but strictly preserves all validation, VoP, review, and SCA steps.
  - Lifecycle: add, edit, rename, soft-delete, and backward-compatible profile schema migration.
- [x] Verify template storage serialization, schema validation, tamper detection, and workflow bypass guards (`BeneficiaryTemplateStorageTests`).

### M5-06: Windows Host Everyday Payments, Standing Orders & Instant Review

- [x] Implement host UI controls in `Broiler.Fond.Host.Windows` ([ADR 0117](adr/0117-windows-host-everyday-payments.md)):
  - Instant SEPA transfer option with fee notice and timeout feedback.
  - Scheduled payment date selector with advance notice validation.
  - Standing Orders management view: list, create, edit, and cancel standing orders.
  - Direct-Debit return button on eligible debit transactions with warning review.
  - Beneficiary template picker and template save prompt.
- [x] Verify accessibility, keyboard navigation, and headless self-tests (`HostSelfTest`).

---

## 4. Exit Criteria

- At least three institutions across two banking groups have one or more explicitly verified payment-operation rows; each row names its exact SCA path.
- Unsupported operations are absent or clearly disabled with an explanation; no bank-name heuristic enables a write.
- An instant payment cannot be silently downgraded to a standard transfer or vice versa.
- Creating, editing, or deleting a scheduled order shows its bank-confirmed final state or an explicit unknown/pending state.
- A direct-debit return or revocation is never inferred from a local state change; it has an authoritative bank-confirmed result or remains visibly pending/unknown and is never automatically retried.
- Templates never bypass current validation, VoP, review, or SCA.
- The payment conformance suite covers every supported operation, response category, challenge type, and ambiguous network boundary.
