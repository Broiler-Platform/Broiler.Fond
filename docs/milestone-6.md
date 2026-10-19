# Milestone 6 — Insights and planning

- **Brand:** Broiler
- **Product:** Fond - Finance on Demand
- **Technical name:** `Broiler.Fond`

**Status:** Complete (Milestones 0, 1, 2, 3, 4, 5, and 6 complete; Milestone 7 in progress).

**Last updated:** 2026-10-19

The [roadmap](roadmap.md#m6--insights-and-planning) defines the product specification.
This backlog breaks Milestone 6 into reviewable increments; completion of one increment does not
imply authorization for production distribution.

---

## 1. Outcome

The product moves from bank transport to useful, explainable personal financial management:
user-defined and deterministic rule-based categories, transaction splits and tags, merchant normalization,
monthly budgets with progress rollups, recurring-item detection, upcoming obligations and low-balance alerts,
clearly labeled cash-flow forecasts, and net-worth history grouped strictly by currency without silent conversion.

---

## 2. Milestone Principles and Invariants

- **Explainable reports:** Every report, budget rollup, or category total can reveal exactly which
  underlying source transactions, revisions, and active rules produced the value. No black-box calculation.
- **Deterministic and reversible categorization:** Rules evaluate in explicit deterministic order. Rule changes
  are previewable before bulk application; applied categories append reversible `AnnotationRevision` records,
  never modifying immutable bank facts (`TransactionRevision` or `SourceObservation`).
- **Strict BCL-only kernel boundary:** `Broiler.Fond.Kernel` maintains zero NuGet packages or native dependencies;
  all rule engines, projections, statistics, and aggregations use only .NET 10 shared framework APIs.
- **Exact decimal money & zero cross-currency conflation:** All budget targets, spendings, and net-worth
  summaries operate on exact decimal `Money` and remain isolated per ISO currency. No currency is ever
  converted or summed into another without an explicit user-provided rate source and timestamp.
- **Truthful forecasts & non-conflation:** Forecast, pending, booked, and manual values remain visually
  and semantically distinct. Projections are always labeled as estimates (`[Estimate / Prognose]`) and
  never imply a bank-guaranteed future balance.
- **Local on-device evaluation:** All rules, alerts, projections, and budget tracking run exclusively on
  the user's device with zero telemetry or remote analytics.

---

## 3. Work Breakdown Structure (M6-01 through M6-06)

### M6-01: Deterministic Categorization Engine, Reversible Rules & Merchant Normalization

- [x] Implement category domain model, deterministic rule engine, and merchant normalization ([ADR 0118](adr/0118-deterministic-categorization-and-rules-engine.md)):
  - Category model: unique category names, system defaults, user-defined categories, and optional hierarchical parent.
  - Deterministic rule matcher: criteria on Counterparty, IBAN, remittance pattern (exact, prefix, wildcard, regex), amount ranges, and transaction kind.
  - Rule evaluation priority: highest-priority match wins; preview mode returns matched transactions and prospective changes before application.
  - Merchant cleaner / normalizer: strips card terminal noise, location tags, and reference IDs to produce canonical counterparty names.
  - Invariant: Applying rules appends `AnnotationRevision` records with explicit `RuleId` provenance; source observations remain untouched.
- [x] Add comprehensive unit verification suite for rule matching, previewing, priority ordering, and merchant normalization (`CategorizationEngineTests`).

### M6-02: Transaction Splits & Multi-Tagging in Profile Store

- [x] Implement transaction splits and tagging models ([ADR 0119](adr/0119-transaction-splits-and-tagging.md)):
  - Split transaction domain model: subdivides a transaction's amount into multiple split items with distinct categories and notes.
  - Strict balance invariant: the sum of split amounts must exactly equal the parent transaction amount in the same currency.
  - Storage extension: serialize and deserialize split items and user tags within `transactions.xml` or dedicated `rules.xml`.
- [x] Verify split validation, rounding preservation, XML serialization, and profile store roundtrip (`TransactionSplitTests`).

### M6-03: Monthly Budgets, Spending Targets & Category Rollups

- [x] Implement budget management and spending rollups ([ADR 0120](adr/0120-budgets-and-spending-targets.md)):
  - Budget model: monthly target amount per category, effective start/end month, roll-over preferences.
  - Rollup queries: calculate actual debits per category for a given calendar month, compared against target.
  - Exact decimal computation: remaining budget, percentage consumed, and overspend warnings.
- [x] Verify budget calculation, multi-month interval transitions, and category rollups (`BudgetCalculationTests`).

### M6-04: Recurring Obligation Detection, Cash-Flow Projections & Labeled Forecasts

- [x] Implement recurring payment detection and cash-flow forecasting ([ADR 0121](adr/0121-recurring-obligations-and-cash-flow-forecasts.md)):
  - Recurring cadence detector: analyzes historical booked debit intervals (monthly, bi-monthly, quarterly, semi-annually, annually) and counterparties.
  - User confirmation workflow: detected obligations are marked as candidate until confirmed by user.
  - Cash-flow forecast engine: projects expected recurring debits and credits over a configurable forecast horizon (30, 60, 90 days).
  - Low-balance alert: flags dates where projected balance dips below user-configured safety thresholds.
  - Non-conflation guard: all forecast entries are typed as `ForecastItem` with `IsEstimate = true`.
- [x] Verify recurring cadence detection, projection calculation, low-balance alert thresholds, and non-conflation invariants (`CashFlowForecastTests`).

### M6-05: Multi-Currency Net-Worth History & Extended Read-Only Products

- [x] Implement net-worth historical snapshots and extended account product views ([ADR 0122](adr/0122-net-worth-history-and-extended-read-only-products.md)):
  - Historical net-worth calculator: aggregates running balances across all user accounts grouped strictly by currency.
  - Assets vs liabilities breakdown: current accounts, savings, credit card, and loan accounts.
  - Extended read-only product presentation: read-only savings balances, credit line limits, and manual cash accounts without conflating payment-account semantics.
- [x] Verify net-worth grouping, multi-currency isolation, and historical snapshot queries (`NetWorthHistoryTests`).

### M6-06: Windows Host Financial Insights, Budget Dashboard & Rules Management

- [x] Implement financial insights UI in `Broiler.Fond.Host.Windows` ([ADR 0123](adr/0123-windows-host-financial-insights-and-budgets.md)):
  - Insights & Budgets tab in `MainForm` with category spending breakdown and budget progress indicators.
  - Rule editor dialog with live rule test / preview against current profile transactions.
  - Cash-flow forecast view with timeline graph/table and low-balance warnings.
  - Transaction split dialog: interactive allocation of amounts with real-time remaining balance display.
- [x] Verify accessibility, keyboard navigation, and headless self-tests (`HostSelfTest`).

---

## 4. Exit Criteria

- Every report can reveal exactly which source transactions and rules produced a value.
- Category rules are deterministic, reversible, and previewable before bulk application.
- Forecast, pending, booked, and manually entered values remain visually and semantically distinct.
- Alert evaluation runs entirely on-device and does not imply a guaranteed future balance.
- Extended account types have separate accuracy fixtures and do not reuse payment-account semantics where they differ.
