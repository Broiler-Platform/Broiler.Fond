# ADR 0094: Immutable transaction domain models and revision streams

- **Status:** Accepted
- **Date:** 2026-10-16
- **Related:** [ADR 0006](0006-local-identity-allocation.md), [ADR 0008](0008-account-values-and-exact-money.md), [docs/roadmap.md](../roadmap.md#m2--transactions-and-account-details), [docs/milestone-2.md](../milestone-2.md)

---

## 1. Context and Problem Statement

In Milestone 2, **Broiler Fond - Finance on Demand** introduces transaction retrieval and history inspection.
Financial institutions report booked and pending account movements across diverse protocols and wire formats
(including SWIFT MT940/MT942 and ISO 20022 CAMT.052/CAMT.053 XML). Over time, transactions may undergo bank-side
reversals, corrections, returned direct debits, or transitions from pending authorizations to settled bookings.
Simultaneously, users need the ability to annotate transactions with personal categories, notes, and tags.

If bank-reported facts and user annotations were stored in a mutable, conflated record:
1. Re-syncing from a bank could overwrite or erase user notes and categories.
2. User edits could tamper with or obscure the verbatim evidence supplied by the bank.
3. Corrections, chargebacks, and returned payments would lack durable audit provenance.
4. Linking pending authorizations to booked transactions would risk double counting or losing authorization history.

---

## 2. Decision and Architecture

We define an immutable domain model under `Broiler.Fond.Kernel.Domain.Transactions` that strictly isolates
verbatim bank facts from user annotations and represents all state changes as append-only revision streams:

### 2.1 Shared Local Identity Allocation

- `TransactionId`, `TransactionRevisionId`, and `AnnotationRevisionId` use the store-wide unsigned 64-bit namespace
  managed by [`LocalIdentityLedger`](../src/Broiler.Fond.Kernel/Domain/Identifiers/LocalIdentityLedger.cs).
- They correspond to the reserved entity kinds `LocalEntityKind.Transaction`, `LocalEntityKind.TransactionRevision`,
  and `LocalEntityKind.AnnotationRevision`.
- Zero is invalid; distinct identities are enforced fail-closed at construction.

### 2.2 Verbatim Bank Facts (`TransactionRevision`)

- [`TransactionRevision`](../src/Broiler.Fond.Kernel/Domain/Transactions/TransactionRevision.cs) captures immutable
  bank observations:
  - Account and binding provenance (`AccountId`, `AccountIncarnationId`, `AccountBindingId`, `ObservationId`).
  - Status (`TransactionStatus`: `Booked`, `Pending`, `Cancelled`).
  - Classification (`TransactionKind`: `Transfer`, `Debit`, `Credit`, `ReturnDebit`, `Fee`, `Interest`, `CardPayment`, `Other`).
  - Exact money (`Money`) with ISO 4217 currency; magnitude is non-negative, with `IsCredit` denoting cash flow direction.
  - Dates (`DateOnly BookingDate`, `DateOnly ValueDate`, `DateTimeOffset BankTimestamp`, `DateTimeOffset RetrievedAt`).
  - Counterparty details ([`Counterparty`](../src/Broiler.Fond.Kernel/Domain/Transactions/Counterparty.cs): Name, IBAN, BIC, account number, bank code).
  - Remittance information ([`RemittanceInformation`](../src/Broiler.Fond.Kernel/Domain/Transactions/RemittanceInformation.cs): End-to-end ID, Mandate ID, Creditor ID, customer reference, bank reference, purpose code, unstructured remittance text).
  - Return debit reason codes (e.g., SEPA return reason codes `MS02`, `MS03`, `AC04`, `AM04`) and raw bank transaction codes.
- When a bank modifies or corrects a transaction, a new `TransactionRevision` is appended with `SupersedesId` referencing
  the prior revision, forming an immutable audit trail validated by `IdentitySnapshotValidator`.

### 2.3 Independent User Annotations (`AnnotationRevision`)

- [`AnnotationRevision`](../src/Broiler.Fond.Kernel/Domain/Transactions/AnnotationRevision.cs) stores user-supplied data:
  - Free-form note (bounded to 4,096 characters).
  - Category (bounded to 128 characters).
  - Normalized, deduplicated, sorted tags (at most 32 tags, each bounded to 64 characters).
  - Budget exclusion flag (`IsExcludedFromBudget`).
- User edits create a new `AnnotationRevision` superseding the previous one without touching bank records.

### 2.4 Pending-to-Booked Candidate Linking (`PendingToBookedLink`)

- [`PendingToBookedLink`](../src/Broiler.Fond.Kernel/Domain/Transactions/PendingToBookedLink.cs) establishes a deterministic
  link between a pending authorization and its settled counterpart.
- Records the match criterion (e.g. `EndToEndId`, `CardAuthReference`, `ExactAmountAndCounterparty`) and link timestamp.
- Neither transaction observation is destroyed or merged in-place, eliminating double counting while maintaining
  unbroken lifecycle provenance.

### 2.5 Wire Observation Provenance (`TransactionObservation`)

- [`TransactionObservation`](../src/Broiler.Fond.Kernel/Domain/Transactions/TransactionObservation.cs) captures the wire
  context of statement retrieval:
  - Statement format (e.g. `CAMT.053`, `CAMT.052`, `MT940`, `MT942`).
  - Statement number and pagination token.
  - SHA-256 payload checksum for duplicate and replay detection.

---

## 3. Consequences and Invariants

- **Exact money:** Floating-point arithmetic is completely prohibited; all amounts use [`Money`](../src/Broiler.Fond.Kernel/Domain/Money.cs).
- **Audit integrity:** Bank facts can never be overwritten by user edits or client heuristics.
- **Portability:** Pure BCL implementation with zero external dependencies, targeting `net10.0`.
- **Concurrency & cycle freedom:** Revisions form strictly acyclic predecessor graphs verified by `IdentitySnapshotValidator`.
