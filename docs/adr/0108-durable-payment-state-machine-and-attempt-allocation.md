# ADR 0108: Durable Payment State Machine and Attempt Allocation

## Status
Accepted

## Context
Payment initiation is an irreversible economic operation. In accordance with the product principles defined in [`docs/roadmap.md`](../roadmap.md):
1. **Durable Domain State Machine:** The payment lifecycle is a core domain state machine, not ephemeral UI state. State changes are strictly append-only.
2. **Immutable `PaymentAttempt` Allocated Before Send:** Before the first request byte can cross the network boundary, an immutable `PaymentAttempt` with a store-wide unique ID and a locally generated, unique `EndToEndId` must be allocated and committed durably.
3. **Truthful Terminal and Quarantined Outcomes:**
   - `Submitted` alone is never success; the status remains `Submitting` until an authoritative bank response arrives.
   - `Unknown` is durable and user-visible: when a network connection drops or times out after submission, the application must assume the order may have reached the bank. It must never automatically retry, directing the user to reconciliation instead.
   - Retries require explicit user action and create a brand-new attempt linked to the original draft.
4. **Duplicate Risk Guard:** Repeating an identical transfer (same source account, beneficiary IBAN, exact amount, and execution date) while an attempt is pending, submitting, or in an unknown state requires explicit confirmation.

## Decisions

### 1. Payment States and Valid Transitions
We define `PaymentState` matching the normative roadmap table:
- `Draft` -> `Validated`
- `Validated` -> `PayeeCheckPending`, `PayeeCheckNotApplicable`
- `PayeeCheckPending` -> `PayeeMatch`, `PayeeCloseMatch`, `PayeeNoMatch`, `PayeeCheckUnavailable`, `Rejected`
- `PayeeMatch` -> `AwaitingAuthorization`
- `PayeeCloseMatch` -> `AwaitingAuthorization`, `AuthorizationCancelled`
- `PayeeNoMatch` -> `AwaitingAuthorization`, `AuthorizationCancelled`
- `PayeeCheckNotApplicable` -> `AwaitingAuthorization`
- `PayeeCheckUnavailable` -> `AwaitingAuthorization`, `AuthorizationCancelled`, `Rejected`
- `AwaitingAuthorization` -> `Submitting`, `AuthorizationCancelled`, `Rejected`
- `Submitting` -> `Accepted`, `BankPending`, `Rejected`, `Unknown`
- `Accepted` -> `BankPending`, `Reconciled`
- `BankPending` -> `Accepted`, `Rejected`, `Unknown`, `Reconciled`
- `Unknown` -> `Accepted`, `BankPending`, `Rejected`, `Reconciled` (only upon explicit reconciliation evidence)
- Terminal states: `AuthorizationCancelled`, `Rejected`, `Reconciled`.

The transition engine (`PaymentStateMachine`) validates every transition and rejects invalid paths with `InvalidPaymentTransitionException`.

### 2. Immutable `PaymentAttempt` & `EndToEndId` Generation
- `PaymentAttempt` captures:
  - `Id`: `ulong` allocated from monotonic `LocalIdentityLedger`.
  - `DraftId`: `ulong` identifying the parent draft.
  - `DraftRevisionId`: `ulong` identifying the exact reviewed parameters.
  - `SourceAccountId`, `SourceAccountBindingId`, `SourceAccountIncarnationId`: permanent provenance.
  - `EndToEndId`: unique non-secret payment reference formatted as `BF-{yyyyMMdd}-{OpaqueRandomHex}` (no PII, no account numbers).
  - `State`: current `PaymentState`.
  - `VoPResult`: attached `VerificationOfPayeeResult` if check was performed.
  - `BankReference`, `ResponseCode`, `StatusDescription`.
  - `CreatedAt`, `UpdatedAt`.
- Committed to store *before* network transmission starts.

### 3. Append-Only Transition Audit Trail
- Each state transition creates a `PaymentStateTransition` entry:
  - `Id`, `AttemptId`, `FromState`, `ToState`, `Timestamp`, `Reason`, `BankCode`.
- Tamper-evident and persisted in profile storage without authentication secrets.

### 4. Duplicate Guard (`PaymentDuplicateGuard`)
- Evaluates whether an existing draft/attempt matches the candidate instruction within an active window.
- Warns the user if an identical payment is pending, submitting, or in `Unknown` state.
- Continuation requires explicit user override.

## Consequences

### Positive
- Guarantees zero ambiguous payment submissions and eliminates duplicate transfers caused by automated retries.
- Full compliance with the normative payment state machine in [`docs/roadmap.md`](../roadmap.md).
- Detailed audit trail allows transparent post-mortem analysis of failed, cancelled, or reconciled payments.

### Negative / Trade-offs
- Strict state transitions require explicit reconciliation before resolving an `Unknown` payment.
