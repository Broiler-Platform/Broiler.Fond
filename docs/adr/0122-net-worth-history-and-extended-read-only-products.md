# ADR 0122: Multi-Currency Net-Worth History and Extended Read-Only Products

- **Status:** Accepted
- **Date:** 2026-10-19
- **Related:** [ADR 0118](0118-deterministic-categorization-and-rules-engine.md), [ADR 0120](0120-budgets-and-spending-targets.md), [ADR 0121](0121-recurring-obligations-and-cash-flow-forecasts.md), [docs/roadmap.md](../roadmap.md#accounts-and-values), [docs/milestone-6.md](../milestone-6.md#m6-05-multi-currency-net-worth-history--extended-read-only-products)

---

## 1. Context and Problem Statement

A comprehensive view of personal financial health requires tracking net worth over time across diverse accounts: current accounts (Girokonten), savings (Tagesgeld, Festgeld, Sparbuch), credit cards, loans/mortgages, and manual cash accounts (wallets, safes).
However, net-worth tracking in consumer software often introduces grave accuracy and security issues:
1. **Silent cross-currency conflation:** Applications frequently aggregate multiple currencies into a single fictitious total using arbitrary, unverified exchange rates. This directly violates Broiler Fond's core principle: *"Aggregate only values in the same currency... floating-point money is forbidden."*
2. **Conflating read-only / offline accounts with payment accounts:** Attempting payment initiation or bank synchronization against savings products, credit cards, or manual cash accounts.
3. **Muddling assets and liabilities:** Treating available overdraft credit lines as positive wealth or masking debt liabilities.
4. **Lack of historical explainability:** Net-worth graphs that cannot demonstrate the constituent account balances and source snapshots that produced each data point.
5. **Strict BCL-only kernel boundary:** All aggregations, classifications, and historical reconstruction must be implemented strictly using .NET 10 shared framework APIs.

---

## 2. Decision and Architecture

### 2.1 Account Product Classification (`AccountProductCategory`)

Supported account product categories:
- `Current`: Payment / checking account (bank-synced, capable of money movement when advertised). Positive balance contributes to Assets; negative balance (overdraft) contributes to Liabilities.
- `Savings`: Read-only savings account (Tagesgeld, Festgeld, Sparbuch). Strictly Asset; cannot initiate payments.
- `CreditCard`: Revolving credit card account. Negative balance contributes to Liabilities (debt); positive credit balance contributes to Assets. Cannot initiate standard SEPA credit transfers.
- `Loan`: Installment loan, consumer credit, or mortgage. Strictly Liability.
- `ManualCash`: User-managed offline cash tracking (physical wallet, petty cash). Strictly Asset; visibly offline, never synced to FinTS, zero payment capability.
- `Depot`: Read-only securities / investment custody shell. Asset.
- `Other`: Unclassified account.

### 2.2 Extended Read-Only Product Presentation (`ExtendedAccountProduct`)

`ExtendedAccountProduct` encapsulates an account's extended product attributes:
- `AccountId`: Unsigned 64-bit local entity identifier.
- `DisplayName`: Account name / alias.
- `Currency`: Mandatory ISO currency code.
- `ProductCategory`: `AccountProductCategory`.
- `IsBankSynced`: True for bank-connected accounts; false for `ManualCash`.
- `IsReadOnly`: True for savings, loans, credit cards, depots, and offline accounts.
- `CanInitiatePayments`: Invariant guard: True only if `ProductCategory == Current` and bank capabilities explicitly advertise transfer operations.
- `BookedBalance`: Current booked balance (`Money?`).
- `AvailableBalance`: Optional available balance (`Money?`).
- `CreditLine`: Optional credit line / overdraft limit (`Money?`), distinct from booked balance and never added to assets.

### 2.3 Assets vs. Liabilities and Multi-Currency Isolation

For any account with balance $B$:
- If $B \ge 0$ and account is classified as asset-bearing (or positive current/card balance): $B$ is counted under **Total Assets**.
- If $B < 0$ (or loan/debt account): $|B|$ is counted under **Total Liabilities**.
- Net worth for that currency: $\text{NetWorth} = \text{TotalAssets} - \text{TotalLiabilities}$.

`CurrencyNetWorthSummary`:
- `Currency`: ISO currency code.
- `TotalAssets`: Exact decimal `Money`.
- `TotalLiabilities`: Exact decimal `Money` (positive magnitude).
- `NetWorth`: Exact decimal `Money` ($\text{TotalAssets} - \text{TotalLiabilities}$).
- `Accounts`: Read-only collection of constituent `NetWorthAccountBalance` items.
- `CategoryBreakdown`: Exact subtotal per `AccountProductCategory`.

`NetWorthSnapshot`:
- `SnapshotDate`: Point in time of evaluation.
- `CurrencySummaries`: Dictionary of `CurrencyNetWorthSummary` keyed by ISO currency code.
- Invariant: Zero cross-currency addition. Users inspecting multiple currencies see distinct, uncompromised currency cards/sections.

### 2.4 Historical Net-Worth Calculator (`NetWorthHistoryCalculator`)

Reconstructs historical net worth:
1. Pure functional calculation with no external dependencies.
2. Accepts accounts, historical `BalanceSnapshot` records, and optional booked transaction records.
3. For a given point in time $T$:
   - Resolves the effective balance of each account at $T$ by taking the most recent snapshot at or prior to $T$ (optionally adjusted by transactions between snapshot and $T$).
   - Categorizes each balance as Asset or Liability.
   - Computes isolated currency summaries and aggregates product category breakdowns.
4. Generates historical timelines (`CalculateHistory`) across requested evaluation points (e.g. month-end snapshots or daily intervals).

---

## 3. Consequences

### Positive
- Strict compliance with the no-floating-point and no-silent-currency-conversion product invariants.
- Clear separation between active payment accounts and read-only / offline accounts.
- Honest depiction of debt obligations (loans, credit card balances, overdrafts) versus true asset equity.
- Every historical net-worth point is fully explainable and auditable down to individual account snapshots.

### Negative / Limitations
- Net worth cannot be expressed as a single global number when the user owns multiple currencies, unless the user explicitly inputs their own exchange rates for a provisional view.
