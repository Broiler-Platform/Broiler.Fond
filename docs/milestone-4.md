# Milestone 4 — Safe SEPA money movement

- **Brand:** Broiler
- **Product:** Fond - Finance on Demand
- **Technical name:** `Broiler.Fond`

**Status:** Complete (Milestone 0, 1, 2, 3, and 4 complete).

**Last updated:** 2026-10-19

The [roadmap](roadmap.md#m4--safe-sepa-money-movement) defines the product specification.
This backlog breaks Milestone 4 into reviewable increments; completion of one increment does not
imply authorization for production distribution.

---

## 1. Outcome

A controlled transactional alpha can initiate a single standard SEPA credit transfer without lying
about its status, losing track of submissions, or risking an automatic duplicate. The product incorporates
pre-authorization Verification of Payee (VoP) per Regulation (EU) 2024/886, bank-advertised Strong Customer
Authentication (SCA), durable immutable payment attempts with fail-closed unknown-outcome handling,
and transparent reconciliation against bank statements and orders.

---

## 2. Milestone Principles and Invariants

- **Write boundary safety:** Money movement is enabled only for explicitly verified institutions and
  accounts. Local read-only safe mode can immediately disable all money movement actions.
- **Strict BCL-only kernel boundary:** `Broiler.Fond.Kernel` has zero NuGet packages or native dependencies;
  all SEPA validation, XML serialization, and FinTS codecs rely solely on .NET 10 shared framework APIs.
- **Exact money only:** Payment amounts require exact decimal representation and mandatory EUR currency (`Money`).
  Floating-point arithmetic is strictly forbidden.
- **Ephemeral session credentials & zero persistence:** PINs and TANs are held only in bounded in-memory
  buffers during an active, foreground transaction. Credentials are never written to disk, never persisted
  in archives or logs, and never reused across sessions.
- **Verification of Payee (VoP) pre-authorization:** In compliance with Regulation (EU) 2024/886, a payee
  check precedes authorization. VoP results (`Match`, `CloseMatch`, `NoMatch`, `Unavailable`, `NotApplicable`)
  and bank warnings are presented transparently to the user before authorization.
- **Immutable PaymentAttempt & durable commit before send:** An immutable `PaymentAttempt` with unique
  local entity ID and fresh `EndToEndId` is committed durably to the profile store *before* the first
  network request byte is transmitted.
- **Durable `Unknown` outcome & zero automatic retries:** If a connection fails or times out after the
  request may have crossed the bank boundary, the state becomes durably `Unknown`. It is never automatically
  retried. Any retry requires explicit user intervention and creates a brand-new attempt linked to the draft.
- **Tamper-evident non-secret audit log:** State transitions are logged in an append-only audit trail
  in the encrypted profile store without storing authentication secrets.

---

## 3. Work Breakdown Structure (M4-01 through M4-06)

### M4-01: Payment Domain Model, Draft Revisions & Exact Validation

- [x] Design and implement payment domain model and SEPA validation ([ADR 0106](adr/0106-payment-domain-draft-revisions-and-sepa-validation.md)):
  - Canonical entities: `PaymentDraft`, `PaymentDraftRevision`, `PaymentBeneficiary`, `PaymentRemittance`, `SepaPaymentDraftValidator`.
  - IBAN checksum validation: strict MOD 97-10 check (ISO 7064 / ISO 13616) and country code verification (`SepaIbanValidator`).
  - Money and remittance validation: exact decimal `Money` in EUR, positive amount (> 0.00 EUR), non-empty beneficiary name, remittance information (unstructured up to 140 chars or structured ISO 11649 creditor reference).
  - Draft revision tracking: append-only immutable revisions (`Draft` -> `Validated`), preserving edit provenance.
- [x] Add unit verification suite covering domain invariants, IBAN validation, edge-case amounts, and revision immutability (`PaymentDraftValidationTests`, 70 checks).

### M4-02: Verification of Payee (VoP) Domain & FinTS Integration

- [x] Implement Verification of Payee (VoP) workflow ([ADR 0107](adr/0107-verification-of-payee-vop-workflow.md)):
  - VoP domain states: `PayeeCheckPending`, `PayeeMatch`, `PayeeCloseMatch`, `PayeeNoMatch`, `PayeeCheckNotApplicable`, `PayeeCheckUnavailable` (`VerificationOfPayeeStatus`, `VerificationOfPayeeResult`).
  - FinTS 3.0 VoP business transactions (`HKVOP` / `HIVOP`) and capability activation (`FinTsVopRequestWriter`, `FinTsVopResponseParser`, `FinTsVopAdvertisement`).
  - Bank warning capture: retain close match suggestions (e.g. corrected name) and mismatch warnings without silently suppressing them.
  - Fail-safe workflow: warnings are prominently displayed; continuation requires explicit user acknowledgment.
- [x] Verify VoP state transitions, wire serialization, response parsing, and warning retention (`VerificationOfPayeeTests`, 60 checks).

### M4-03: Durable Payment State Machine & Immutable PaymentAttempt Allocation

- [x] Implement durable payment state machine and attempt allocation ([ADR 0108](adr/0108-durable-payment-state-machine-and-attempt-allocation.md)):
  - Canonical state machine: `Draft`, `Validated`, VoP states, `AwaitingAuthorization`, `Submitting`, `Accepted`, `BankPending`, `Rejected`, `Unknown`, `Reconciled`, `AuthorizationCancelled`.
  - Pre-send durable commit: `PaymentAttempt` with immutable ID and fresh `EndToEndId` committed before network transmission.
  - Replay and duplicate guard: detect identical active or pending drafts and require explicit user override.
  - Durable `Unknown` handling: terminal/quarantined outcome requiring reconciliation evidence; zero automatic retry.
- [x] Verify state transitions, pre-send commit guarantees, duplicate detection, and invalid transition rejection (`PaymentStateMachineTests`, 140 checks).

### M4-04: Single SEPA Credit Transfer FinTS Wire Codec & SCA Authorization Continuation

- [x] Implement SEPA Credit Transfer FinTS 3.0 wire codecs and SCA continuation ([ADR 0109](adr/0109-sepa-credit-transfer-fints-codec-and-sca.md)):
  - FinTS 3.0 Single SEPA Credit Transfer segments (`HKCCS` / `HICCS` with ISO 20022 `pain.001` or pain syntax).
  - SCA challenge integration: bind `PaymentAttempt` to `HKTAN` challenge with exact payment details (payee, IBAN, amount) displayed in challenge dialog.
  - Submission receipt generation: attempt ID, bank reference, timestamps, and return codes.
  - Failure injection: network loss before send, after send, during SCA, and before bank confirmation.
- [x] Verify wire codecs, SCA challenge continuation, receipt generation, and failure injection resilience (`SepaCreditTransferTests`, 75 checks).

### M4-05: Payment Storage Extension & Audit History in Profile Store

- [x] Extend profile store schema with `payments-v1.xsd` in profile ZIP archive ([ADR 0110](adr/0110-payment-storage-extension-and-audit-history.md)):
  - New XML document `payments.xml` storing payment drafts, draft revisions, payment attempts, and state transition audit logs.
  - Maintain backward compatibility: older profiles cleanly migrate without data loss.
  - Atomic generation promotion and crash-resilient privacy purge (`ExecutePrivacyPurge`).
  - Cross-snapshot ID remapping and fork conflict rejection for payment entities.
- [x] Verify storage serialization round-trips, hostile schema injection, tamper detection, and audit trail integrity (`PaymentStorageTests`, 68 checks).

### M4-06: Windows Host Payment Initiation, Review Screen, Reconciliation & Safe Mode

- [x] Implement payment presentation and controls in `Broiler.Fond.Host.Windows` ([ADR 0111](adr/0111-windows-host-payment-initiation-and-safe-mode.md)):
  - `PaymentInitiationDialog`: draft input with real-time IBAN validation, exact money input, and remittance fields.
  - `PayeeVerificationView`: display VoP outcome and bank mismatch warnings before proceeding.
  - `PaymentReviewDialog`: exact review of source account, payee name, IBAN, amount, remittance, and VoP confirmation before triggering bank authorization.
  - User-visible read-only safe mode toggle (`IsSafeModeActive`) disabling all money movement immediately.
  - Payment reconciliation view: match submitted attempts with booked/pending transactions or bank orders.
- [x] Verify accessibility, keyboard navigation, and headless self-tests (`HostSelfTest`).

---

## 4. Exit Criteria

- Controlled transfers succeed for at least one explicitly verified payment row; unverified institutions cannot expose the action.
- The user sees exact amount, currency, recipient name, IBAN, execution date, remittance information, VoP outcome, and bank warnings before authorization.
- Failure before submission is safely retryable. Failure after possible submission creates durable `Unknown` and is never automatically retried.
- Repeating an identical draft after an earlier attempt requires explicit user override and presents the earlier attempt.
- Reused or conflicting references never overwrite or coalesce a payment attempt.
- `Accepted` appears only when supported by an authoritative bank response; `Submitted` alone is not success.
- Every state transition is auditable without persisting PINs, TANs, or reusable challenge data.
- Failure injection passes at every network and SCA boundary.
- Independent penetration test has zero unresolved critical or high-severity findings, and the two-human release gate passes.
