# ADR 0123: Windows Host Financial Insights, Budget Dashboard, Rule Management and Transaction Splits

- **Status:** Accepted
- **Date:** 2026-10-19
- **Related:** [ADR 0118](0118-deterministic-categorization-and-rules-engine.md), [ADR 0119](0119-transaction-splits-and-tagging.md), [ADR 0120](0120-budgets-and-spending-targets.md), [ADR 0121](0121-recurring-obligations-and-cash-flow-forecasts.md), [ADR 0122](0122-net-worth-history-and-extended-read-only-products.md), [docs/roadmap.md](../roadmap.md#personal-finance-and-reporting), [docs/milestone-6.md](../milestone-6.md#m6-06-windows-host-financial-insights-budget-dashboard--rules-management)

---

## 1. Context and Problem Statement

Milestone 6 introduces deterministic personal finance management (categorization rules, transaction splits, monthly budgets, recurring obligations, cash-flow forecasts, and net-worth history).
To bring these capabilities to the user in `Broiler.Fond.Host.Windows`, the host application must provide:
1. **Insights & Budgets Tab in `MainForm`:** A dedicated view showing monthly category spending progress, budget targets, overspend warnings, and isolated multi-currency net-worth summaries.
2. **Rule Editor with Live Preview:** A dialog allowing users to create, test, and preview categorization rules against loaded profile transactions before committing changes.
3. **Cash-Flow Forecast View:** Visual presentation of recurring income/expenses over 30, 60, or 90 days, explicitly labeled with `[Estimate / Prognose]` and highlighting low-balance safety threshold breaches.
4. **Transaction Split Dialog:** Interactive allocation of transaction amounts into sub-items with real-time feedback on remaining unallocated amounts and strict balance enforcement ($\sum \text{SplitAmount} = \text{TransactionAmount}$).
5. **Accessibility and Safety:** WCAG 2.1 AA compliant keyboard navigation, high contrast support, screen-reader accessible names, and safe mode enforcement.

---

## 2. Decision and Architecture

### 2.1 Insights & Budgets Tab (`MainForm`)
A new tab (`_tabInsights`) is added to the main tab control:
- **Month Selector:** Allows switching the active calendar month for budget rollups.
- **Budget Progress List:** Displays category names, target budgets, actual debit spending, remaining balance, and status indicators (e.g., `[✓ Under Budget]`, `[⚠ Overspent]`).
- **Net-Worth Summary:** Displays isolated assets, liabilities, and net worth per currency.
- **Action Buttons:** "Manage Rules...", "Cash-Flow Forecast...", and "Split Transaction...".

### 2.2 Rule Editor and Live Preview (`CategorizationRulesDialog`)
- Lists current user and system categorization rules with priority order.
- Supports adding and editing rules with match criteria: Counterparty, IBAN, remittance text, amount range, and target category.
- **Live Preview:** User can click "Preview / Test Rule" to evaluate against the loaded profile transactions in `HostSession.TransactionEngine`. The dialog displays matched transaction count and sample prospective changes before applying.
- Applying rules writes `AnnotationRevision` entries; bank facts remain completely untouched.

### 2.3 Transaction Split Dialog (`TransactionSplitDialog`)
- Displays parent transaction details: Date, Counterparty, Total Amount, and Currency.
- DataGridView / List allowing line item entry: Split Amount, Category, and Note.
- Real-time footer showing:
  - Total Allocated Amount.
  - Remaining Balance.
  - Save button is enabled **only** when Remaining Balance is exactly `0.00` in the parent currency.

### 2.4 Cash-Flow Forecast Dialog (`CashFlowForecastDialog`)
- Allows configuring forecast horizon (30, 60, 90 days) and safety alert threshold (e.g., €200.00).
- Displays chronological timeline table with dates, counterparty, amount, direction, running projected balance, and mandatory `[Estimate / Prognose]` indicator.
- Highlights low-balance alert dates where projected balance dips below the safety threshold.

### 2.5 HostSession Integration
`HostSession` is extended with:
- Categories & Rules: rule storage, evaluation, preview, and application.
- Budgets: monthly budget target persistence and `BudgetRollupEngine` calculation.
- Recurring Obligations & Forecasting: obligation tracking and `CashFlowForecastEngine` execution.
- Net-Worth: `NetWorthHistoryCalculator` queries.
- Splits: saving split line items via `AnnotationRevision`.

---

## 3. Consequences

### Positive
- Fully integrates Milestone 6 kernel capabilities into an accessible, responsive Windows desktop experience.
- Upholds strict invariants: previewable deterministic rules, zero floating-point arithmetic, strict split balance invariance, and clear non-conflation of forecast estimates with bank facts.
- Fully verified via headless self-tests (`HostSelfTest`).

### Negative / Limitations
- Forecasting relies on regular historical recurring cadences; irregular discretionary expenses cannot be predicted.
