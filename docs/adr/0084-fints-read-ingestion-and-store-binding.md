# ADR 0084: FinTS Read Data Ingestion, Account Rediscovery, and Store Binding Application

- **Status:** Accepted (pure fail-closed read data ingestion, rediscovery planning, atomic identity batch allocation, balance snapshot persistence, and connection contact recording)
- **Date:** 2026-10-15
- **Related:** [Kernel boundary](0001-kernel-boundary.md),
  [lossless locators and rediscovery](0003-lossless-locators-and-rediscovery.md),
  [exact money and account values](0004-exact-money-and-account-values.md),
  [account discovery and balance schemas](0018-account-discovery-and-balance-schemas.md),
  [assembled read session attempt](0072-assembled-read-session-attempt.md),
  [synchronization recovery application](0082-synchronization-recovery-application.md),
  [FinTS transport and capability activation](0083-fints-transport-and-capability-activation.md)

## Context and architectural requirement

Milestone 1 (specifically M1-05 FinTS read-only slice and M1-03 Identity) requires connecting verified read-session
evidence—specifically account discovery (`HISPA`), balance reports (`HISAL`), and user parameter accounts (`HIUPD`)—to
the local identity ledger and durable domain entities without exposing credentials, silently overwriting conflicting
accounts, or fabricating financial balances.

Prior to this increment:
- Pure read-session execution returned verified in-memory session results (`FinTsPinTanReadSessionResult`), but no
  mechanism existed to translate raw wire account identifiers into domain `AccountSourceLocator` instances.
- Account rediscovery planning (`AccountRediscoveryPlanner`) existed in isolation without an ingestion engine to drive
  atomic batch allocation on the `LocalIdentityLedger`.
- Parsed balance reports (`FinTsBalanceReport`) with booked, available, and credit-line components were not bound to
  local account incarnations or stored as immutable `BalanceSnapshot` entities.
- Connection timestamps (`LastSuccessfulContact`, `LastAttemptedContact`) were not updated upon completed read sessions.

This increment delivers `FinTsAccountSourceLocatorFactory` and `FinTsReadIngestionApplication` to bridge read-session
evidence into canonical local identity and balance records with pure fail-closed invariants on the standard `net10.0`
BCL.

## Design and implementation

### 1. Lossless Locator Construction (`FinTsAccountSourceLocatorFactory`)

`FinTsAccountSourceLocatorFactory` constructs canonical `AccountSourceLocator` domain records from wire structures while
enforcing lossless preservation of raw bank evidence:
- **`FinTsReadAccount` mapping (from `HISPA` discovery or `HISAL` balance reports):**
  - Connector: `"fints"`.
  - Domestic elements: Preserves raw account number with leading zeros and letter casing; preserves subaccount and
    domestic bank code.
  - IBAN: Preserves raw IBAN string including any whitespace or casing reported by the bank.
  - Currency: Preserves explicit ISO-4217 currency.
  - Bank identifiers: Extracts BIC (`"bic"`) and numeric country code (`"country"`) into `BankAccountIdentifier`s.
- **`FinTsAccountParameters` mapping (from `HIUPD` user parameter data):**
  - Domestic elements: Maps parsed domestic connection elements (`ktv`/`kti`) if available.
  - IBAN: Maps UPD IBAN.
  - Currency: Maps UPD currency.
  - Bank identifiers: Maps customer ID (`"customer_id"`) if reported.

### 2. Read Ingestion Application (`FinTsReadIngestionApplication`)

`FinTsReadIngestionApplication.Apply(...)` executes an immutable, fail-closed pipeline:
1. **Operational Connection Checks:**
   - Rejects disabled connections (`RejectedDisabledConnection`).
   - Rejects unconfigured or unsynchronized connections (`RejectedUnsynchronizedConnection`).
   - Records attempted contact timestamp on rejected connections.
2. **Terminal Session Outcome Verification:**
   - Requires `ExecutionReported` outcome and `Terminal` stage from `FinTsPinTanReadSessionResult`.
   - Rejects sessions terminating in bank aborts (`9800`) or requiring manual review (`RejectedSessionOutcome`).
3. **Evidence Presence Verification:**
   - Requires balance reports, account discovery, or parameter accounts (`RejectedMissingEvidence`).
4. **Institution Code Consistency:**
   - Verifies that reported bank codes match the configured institution bank code (`RejectedInstitutionMismatch`).
5. **Account Discovery Consolidation:**
   - Gathers accounts across `AllAccountEvidence` (`HISPA`), `AggregatedBalances` (`HISAL`), and UPD accounts (`HIUPD`),
     enriching accounts with currency advertised in UPD parameters when omitted in discovery segments.
6. **Atomic Identity Allocation Batch:**
   - Opens an `IdentityAllocationBatch` on the `LocalIdentityLedger`.
   - Executes `AccountRediscoveryPlanner.Plan` against caller-provided `KnownAccountBinding`s.
   - For `ReuseBinding`: Preserves existing binding without duplicating entities.
   - For `NewAccountCandidate`: Atomically allocates `LocalEntityKind.Account`, `AccountIncarnation`, and
     `AccountBinding` in `AccountBindingState.Active`.
   - For `QuarantinedCandidate`: Atomically allocates `Account`, `AccountIncarnation`, and `AccountBinding` in
     `AccountBindingState.Quarantined` (e.g., when an existing locator undergoes an unconfirmed currency mutation or
     locator conflict, preventing silent corruption).
7. **Balance Snapshot Ingestion:**
   - For active account bindings with valid balance reports, allocates `LocalEntityKind.Observation` and
     `LocalEntityKind.BalanceSnapshot`.
   - Maps `BalanceKind.Booked`, `BalanceKind.Available`, and `BalanceKind.CreditLine`.
   - Validates reported currencies against `CurrencyCatalog.IsRecognized`.
   - Parses bank-reported timestamps (`DateTimeOffset`) into immutable `BalanceSnapshot` domain entities.
8. **Atomic Commit & Concurrency Protection:**
   - Commits the allocation batch via `batch.Commit(cancellationToken)`.
   - If a concurrent write invalidated the snapshot (`StaleIdentityBatchException`), the batch is rolled back and
     `RejectedLedgerConflict` is returned, allowing safe retry with a fresh ledger snapshot.
9. **Connection State Update:**
   - On success, updates the connection with `connection.WithSuccessfulContact(retrievedAt)` and returns the newly
     committed snapshot and bound accounts in `FinTsReadIngestionResult`.

## Verification

80 executable checks in `FinTsReadIngestionTests.cs` verify:
- Accurate mapping and raw character preservation in `FinTsAccountSourceLocatorFactory` across `FinTsReadAccount` and
  `FinTsAccountParameters`.
- Fail-closed rejections for disabled, unsynchronized, mismatched, and empty read sessions.
- Fail-closed rejections for aborted or review-requiring read sessions.
- Atomic entity allocation (`Account`, `AccountIncarnation`, `AccountBinding`) for newly discovered accounts.
- Stable reuse of existing active bindings for rediscovering existing accounts.
- Automatic quarantining of conflicting accounts (e.g., currency changes).
- Ingestion of booked, available, and credit line balance snapshots with bank timestamps and recognized currencies.
- Concurrency rollback and `RejectedLedgerConflict` return when batches collide with stale snapshots.
- Multi-threaded execution across 16 concurrent threads ensuring atomic ledger snapshots with zero data loss or
  corruption.
