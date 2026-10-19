# ADR 0097: Statement Pagination, Range Queries, and Overlap Sync

- **Status:** Accepted
- **Date:** 2026-10-16
- **Related:** [ADR 0094](0094-immutable-transaction-domain-models.md), [ADR 0095](0095-statement-parameters-and-capability-negotiation.md), [ADR 0096](0096-statement-wire-parsers-mt940-and-camt.md), [docs/roadmap.md](../roadmap.md#m2--transactions-and-account-details), [docs/milestone-2.md](../milestone-2.md)

---

## 1. Context and Problem Statement

In Milestone 2, **Broiler Fond - Finance on Demand** implements statement synchronization across German FinTS 3.0 institutions.
Financial institutions report transaction history over bounded time slices through multi-page responses (`HKKAZ` for SWIFT MT940 and `HKCAZ` for ISO 20022 CAMT XML).

To satisfy the core product principles:
- **No silent conflation / Multiplicity preservation:** Two indistinguishable rows in one statement (e.g., two identical €10 cash withdrawals on the same date at the same terminal) are distinct real-world economic events. A naive content-hash or reference set would conflate them into a single transaction.
- **Overlap sync idempotency:** Refreshing statements with overlapping historical date windows (e.g., last 30–90 days) must produce **zero duplicate transactions** and zero balance discrepancies across repeated syncs.
- **Resilient continuation & bounded execution:** Multi-page dialogues must observe strict page budgets ($\le 128$ pages) and monotonic deadlines to prevent unbounded memory consumption or server hangs, preserving continuation tokens if interrupted.
- **Truthful reconciliation:** Pending authorizations must reconcile with booked movements without destructive overwriting or double counting.
- **BCL-only boundary:** All request encoding, coordination, and correlation must use pure .NET 10 runtime APIs with zero third-party packages.

---

## 2. Decision and Architecture

We introduce four immutable components in `Broiler.Fond.Kernel`:

### 2.1 Statement Request Encoding ([`FinTsStatementRequestWriter.cs`](file:///d:/Broiler.Fond/src/Broiler.Fond.Kernel/FinTs/FinTsStatementRequestWriter.cs))

Encodes unsigned statement range query segments wrapped in FinTS envelopes:
- **`HKKAZ` (SWIFT MT940):** Supports versions 5, 6, and 7.
  - Formats national-only or international (IBAN/BIC) account identifiers.
  - Encodes date ranges (`yyyyMMdd`), maximum entry counts, and continuation tokens ($\le 35$ characters).
  - Version 7 encodes optional format codes (e.g. `MT940`).
- **`HKCAZ` (ISO 20022 CAMT):** Supports versions 1 and 2.
  - Encodes target CAMT format URN (e.g. `camt.053.001.02` or `camt.052.001.02`).
  - Supports account identifiers, date ranges, and continuation tokens.
- **Fail-closed validation:** Rejects inverted date ranges (`fromDate > toDate`), zero/negative maximum entries, empty tokens, and unsupported versions. Supports `CancellationToken`.

### 2.2 Synchronization Checkpoint ([`StatementSyncCheckpoint.cs`](file:///d:/Broiler.Fond/src/Broiler.Fond.Kernel/Domain/Transactions/Sync/StatementSyncCheckpoint.cs))

Immutable checkpoint tracking synchronization progress for an account binding:
- Stores `AccountId`, `AccountIncarnationId`, `AccountBindingId`.
- Tracks `WindowStart` and `WindowEnd` (date range).
- Records `LastContinuationToken`, `IsComplete`, and cumulative `ObservedTransactionCount`.
- Provides immutable transition methods: `WithContinuation(...)` and `WithCompletion(...)`.

### 2.3 Overlap Synchronization & Correlation Planner ([`StatementOverlapSyncPlanner.cs`](file:///d:/Broiler.Fond/src/Broiler.Fond.Kernel/Domain/Transactions/Sync/StatementOverlapSyncPlanner.cs))

Deterministic correlation planner responsible for:
1. **Query Window Calculation:**
   - Initial sync: queries back `initialWindowDays` (default 90 days) to the current date.
   - Interrupted sync: preserves and resumes the exact incomplete window (`WindowStart` to `WindowEnd`).
   - Incremental sync: overlaps previous `WindowEnd` by `overlapDays` (default 30 days) up to the current date.
2. **Multiplicity Preservation & One-to-One Counting:**
   - Constructs a canonical composite key: `(BookingDate, ValueDate, Amount, Currency, IsCredit, CounterpartyIban, CounterpartyName, EndToEndId, RemittanceText)`.
   - Populates a multiplicity frequency map of existing cached transactions.
   - For incoming statement entries, matches against available multiplicity counts:
     - If count > 0: decrements remaining count and increments matched count (no new transaction created).
     - If count == 0: creates a new immutable `TransactionRevision` and `TransactionObservation` using unique store allocator IDs.
   - Multiple identical transactions within the statement remain distinct entries with unique IDs, while repeated syncs of the same entries yield zero duplicate transactions.
3. **Non-Destructive Pending-to-Booked Reconciliation:**
   - Detects when a newly booked transaction corresponds to an existing pending transaction.
   - Matches by unique `EndToEndId` or heuristic criteria (within $\pm 7$ days, exact amount, matching counterparty).
   - Generates an immutable [`PendingToBookedLink`](file:///d:/Broiler.Fond/src/Broiler.Fond.Kernel/Domain/Transactions/PendingToBookedLink.cs) without modifying or deleting the original pending observation.

### 2.4 Multi-Page Continuation Coordinator ([`StatementContinuationCoordinator.cs`](file:///d:/Broiler.Fond/src/Broiler.Fond.Kernel/Domain/Transactions/Sync/StatementContinuationCoordinator.cs))

Coordinates multi-page statement downloads:
- Enforces a maximum page limit of 128 pages (`MaximumPages = 128`).
- Monitors monotonic deadlines (`DeadlineExpired`).
- Preserves consumed tokens history.
- Handles interrupted dialogues (`MarkInterrupted`), keeping the current continuation token intact for subsequent resumption.

---

## 3. Invariants and Guarantees

1. **Exact decimal money:** All amounts use [`Money`](file:///d:/Broiler.Fond/src/Broiler.Fond.Kernel/Domain/Money.cs); floating-point types are strictly forbidden.
2. **Idempotence across overlapping syncs:** Ten repeated overlapping syncs of identical statement data produce exactly zero new transactions and zero balance discrepancy.
3. **Multiplicity invariant:** $N$ identical transactions in a single statement produce $N$ distinct local entities.
4. **Memory bounds:** Pagination is strictly capped at 128 pages per dialogue.

---

## 4. Verification

Implemented in [`tests/Broiler.Fond.Kernel.Tests/StatementSyncTests.cs`](file:///d:/Broiler.Fond/tests/Broiler.Fond.Kernel.Tests/StatementSyncTests.cs) (90 executable checks):
- `FinTsStatementRequestWriter`: Wire format generation and validation for HKKAZ v5/v6/v7 and HKCAZ v1/v2 across all argument variations and cancellation.
- `StatementSyncCheckpoint`: Invariant enforcement, token progression, and completion transitions.
- `StatementOverlapSyncPlanner`: Initial, resume, and incremental query window calculations.
- Multiplicity preservation: Two identical entries in the same statement produce distinct `TransactionId` instances.
- Ten repeated overlapping sync passes: Each repeat yields `NewTransactions.Count == 0` and matches all existing entries.
- Pending-to-booked reconciliation: Exact EndToEndId linking and 7-day heuristic linking verified.
- `StatementContinuationCoordinator`: Progression, 128-page limit enforcement, monotonic deadline expiration, failure states, and interruption preservation.
