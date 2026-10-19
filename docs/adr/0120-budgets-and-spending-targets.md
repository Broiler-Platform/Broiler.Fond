# ADR 0120: Monthly Budgets, Spending Targets, and Category Rollups

- **Status:** Accepted
- **Date:** 2026-10-19
- **Related:** [ADR 0118](0118-deterministic-categorization-and-rules-engine.md), [ADR 0119](0119-transaction-splits-and-tagging.md), [docs/roadmap.md](../roadmap.md#personal-finance-and-reporting), [docs/milestone-6.md](../milestone-6.md#m6-03-monthly-budgets-spending-targets--category-rollups)

---

## 1. Context and Problem Statement

Personal financial planning requires users to set monthly spending targets per category (e.g. €450 for Groceries, €100 for Dining Out, €80 for Public Transport) and track their real-time progress as booked transactions and transaction splits accumulate.
Commercial personal finance applications frequently exhibit critical flaws:
1. **Black-box rollups:** It is unclear which exact transactions or split items caused an overspend.
2. **Floating-point rounding errors:** Rounding inaccuracies accumulate when aggregating split transactions and percentages.
3. **Cross-currency conflation:** Multicurrency accounts or foreign travel expenditures are erroneously summed into one total without clear currency isolation.
4. **Inflexible intervals:** Missing support for multi-month budgets, effective date ranges, and optional unspent balance roll-overs.

To satisfy Broiler Fond's core principles:
- **Explainable rollups:** Every category total must explicitly enumerate all contributing transactions, split items, and excluded transfers.
- **Exact decimal money:** Budget limits, actual spending, and remaining amounts must operate purely on exact decimal arithmetic (`Money`), forbidding floating-point math.
- **Split-aware attribution:** If a transaction is split, expenditures are attributed per split item; if not split, the whole transaction amount is attributed to the primary category.
- **Strict currency isolation:** Rollups group strictly by ISO currency (EUR, CHF, etc.); amounts in different currencies are never combined.
- **Strict BCL-only kernel boundary:** All rollup calculators and summary projections must use only .NET 10 runtime APIs.

---

## 2. Decision and Architecture

### 2.1 Budget Target Domain Model (`BudgetTarget`)

A budget target represents a user-configured monthly spending allowance for a category:
- `Id`: unsigned 64-bit local entity ID.
- `Category`: category name (matching category registry).
- `MonthlyLimit`: exact decimal spending limit.
- `Currency`: mandatory 3-letter ISO currency code (default: `"EUR"`).
- `EffectiveFrom`: calendar month when the budget begins (`DateOnly` representing first day of month, e.g. `2026-10-01`).
- `EffectiveTo`: optional calendar month when the budget expires (`DateOnly?`).
- `AllowRollover`: boolean indicating whether unspent positive balances carry over to subsequent months.
- `IsActive`: boolean indicating whether the target is actively evaluated.

### 2.2 Category Rollup and Progress Projection (`CategoryBudgetProgress`)

For a given category and calendar month, the rollup projection computes:
- `Category`: target category name.
- `Currency`: ISO currency.
- `TargetAmount`: configured monthly limit (plus any rolled-over allowance).
- `BaseLimit`: base monthly limit without rollover.
- `RolloverAmount`: amount rolled over from prior month (if enabled).
- `ActualSpent`: positive decimal sum of debit transactions and split items attributed to this category in the month.
- `RemainingBudget`: exact decimal remainder (`TargetAmount - ActualSpent`).
- `PercentageConsumed`: percentage of budget consumed ($0.0\%$ to $100.0\%+$).
- `IsOverspent`: boolean (`ActualSpent > TargetAmount`).
- `OverspentAmount`: positive decimal excess, or $0.00$.
- `ContributingTransactionCount`: number of transactions/splits included.
- `ContributingTransactions`: itemized records providing full explainability.

### 2.3 Split-Aware Rollup Algorithm (`BudgetRollupEngine`)

When calculating monthly progress:
1. Filter transactions to the target month:
   $$\text{BookingDate} \ge \text{FirstDayOfMonth} \land \text{BookingDate} \le \text{LastDayOfMonth}$$
2. Verify transaction currency matches target currency.
3. Exclude transactions marked with `IsExcludedFromBudget = true` (e.g. own-account transfers, internal rebookings).
4. For each transaction:
   - If `transaction.IsSplit` is true and `transaction.Splits` contains items:
     - For each split item matching the category: add `Math.Abs(split.Amount)` to `ActualSpent`.
   - Else if `transaction.Category` matches the target category and `!transaction.IsCredit`:
     - Add `Math.Abs(transaction.Amount)` to `ActualSpent`.
5. Roll-over calculation:
   If `AllowRollover` is enabled, evaluate month $M-1$. If $\text{Remaining}_{M-1} > 0$, add to month $M$'s effective target.

---

## 3. Consequences

### Positive
- Fully explainable and auditable budget dashboards where every cent traces to visible source transactions.
- Exact decimal precision preserves financial accuracy across high-volume transaction sets.
- Granular support for split receipts ensures supermarket runs do not distort non-grocery budgets.
- Complete currency isolation prevents invalid aggregation of multi-currency assets.

### Negative / Limitations
- Cross-currency spending does not automatically convert to a base currency without explicit user exchange rates.
