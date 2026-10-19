# ADR 0121: Recurring Obligations Detection, Cash-Flow Projections and Labeled Forecasts

- **Status:** Accepted
- **Date:** 2026-10-19
- **Related:** [ADR 0118](0118-deterministic-categorization-and-rules-engine.md), [ADR 0120](0120-budgets-and-spending-targets.md), [docs/roadmap.md](../roadmap.md#personal-finance-and-reporting), [docs/milestone-6.md](../milestone-6.md#m6-04-recurring-obligation-detection-cash-flow-projections--labeled-forecasts)

---

## 1. Context and Problem Statement

A critical capability in personal finance is looking ahead: knowing when fixed obligations (rent, insurance, gym, subscriptions, utilities) and income (salary, pensions) are scheduled to occur, and forecasting running cash flow over 30, 60, or 90 days.
However, forecasting in banking applications often creates serious user hazards:
1. **Conflating estimates with bank facts:** Forecasted future transactions are frequently rendered in the same table and styling as settled bank facts, misleading users into believing funds have already cleared or are legally guaranteed.
2. **Autonomous or brittle pattern matching:** Machine-learning detectors may infer false cadences or silently create unconfirmed standing predictions.
3. **Black-box low-balance alerts:** Users receive low-balance warnings without clear visibility into which specific future debits triggered the projected shortfall.
4. **Strict BCL-only kernel boundary:** All cadence detection, statistical interval clustering, cash-flow projections, and alert evaluation must be implemented purely using .NET 10 runtime APIs.

To uphold Broiler Fond's principles:
- **Truthful states & non-conflation:** Forecast items are strictly typed as `ForecastItem` with `IsEstimate = true` and display label prefix `"[Estimate / Prognose]"`. Forecasts never modify or mix with `TransactionRevision` or `SourceObservation`.
- **User confirmation workflow:** Automatically detected obligations are marked as unconfirmed candidates (`IsUserConfirmed = false`) until explicitly reviewed and accepted by the user.
- **On-device privacy:** All recurring detection, projection math, and threshold evaluation run entirely on the local device with zero telemetry.
- **Exact money & zero floating-point math:** Running balances, projected balances, and shortfalls operate on exact decimal arithmetic.

---

## 2. Decision and Architecture

### 2.1 Cadence and Recurring Obligation Models (`RecurringObligation`)

Supported cadences:
- `Weekly` (~7 days)
- `Monthly` (~28–31 days)
- `BiMonthly` (~58–62 days)
- `Quarterly` (~89–93 days)
- `SemiAnnually` (~180–185 days)
- `Annually` (~365–366 days)

`RecurringObligation`:
- `Id`: unsigned 64-bit local entity ID.
- `AccountId`: target account local ID.
- `CounterpartyName`: counterparty or normalized merchant name.
- `CounterpartyIban`: optional counterparty IBAN.
- `ExpectedAmount`: expected amount (positive magnitude).
- `Currency`: mandatory ISO currency code.
- `IsCredit`: boolean (true = recurring income like salary; false = recurring expense).
- `Cadence`: `RecurringCadence`.
- `ExpectedDayOfMonth`: expected execution day (1–31).
- `Category`: optional category name.
- `IsUserConfirmed`: boolean (false = candidate detected by algorithm; true = approved by user).
- `IsActive`: boolean (default true).
- `LastObservedDate`: date of the most recent matching historical booking.
- `NextExpectedDate`: projected date for the next occurrence.

### 2.2 Recurring Cadence Detector (`RecurringCadenceDetector`)

Analyzes historical booked transactions:
1. Groups transactions by account, currency, direction, and counterparty (matching counterparty IBAN or normalized merchant).
2. Requires a minimum cluster size ($\ge 3$ transactions for monthly/weekly; $\ge 2$ for quarterly/semi-annual/annual).
3. Computes inter-transaction interval in days ($\Delta t$). If variance falls within strict tolerance bands:
   - Monthly: $27 \le \Delta t \le 34$ days.
   - Bi-Monthly: $56 \le \Delta t \le 65$ days.
   - Quarterly: $85 \le \Delta t \le 96$ days.
   - Semi-Annually: $175 \le \Delta t \le 190$ days.
   - Annually: $355 \le \Delta t \le 375$ days.
4. Checks amount stability (variance $\le 10\%$, or exact matching).
5. Returns candidate `RecurringObligation` records with `IsUserConfirmed = false`.

### 2.3 Forecast Engine (`CashFlowForecastEngine`)

Projects cash flow over a configurable horizon (e.g. 30, 60, 90 days):
1. Starts from the latest bank-booked account balance snapshot (`CurrentBookedBalance`).
2. Advances day-by-day from `StartDate` to `EndDate`:
   - Evaluates active, confirmed (or user-selected) obligations whose cadence lands on the date.
   - Generates a typed `ForecastItem` with `IsEstimate = true` and `DisplayLabel = "[Estimate / Prognose] <Counterparty>"`.
   - Computes exact decimal running `ProjectedBalance`.
3. Low-Balance Alert Evaluation:
   - Compares daily `ProjectedBalance` against a user-configured safety threshold (e.g. €200.00).
   - If projected balance dips below threshold, emits a `LowBalanceAlert` recording the date, projected balance, shortfall, and triggering obligation.

---

## 3. Consequences

### Positive
- Clear, unambiguous separation between bank-cleared transactions and prospective estimates.
- Early warning against overdraft fees or failed SEPA direct debits.
- Explainable projections where every future dip traces to a specific anticipated debit.
- Local, private execution without sending financial history to remote servers.

### Negative / Limitations
- Projections are based on historical cadence and standing orders; unpredicted irregular expenses (e.g. emergency car repairs) cannot be foreseen.
