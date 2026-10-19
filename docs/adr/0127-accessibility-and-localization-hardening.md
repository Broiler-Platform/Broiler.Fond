# ADR 0127: Accessibility (BFSG/BFSGV) and German/English Localization Hardening

- **Status:** Accepted
- **Date:** 2026-10-19
- **Related:** [ADR 0092](0092-windows-host-ui-and-support-workflows.md), [ADR 0111](0111-windows-host-payment-initiation-and-safe-mode.md), [ADR 0117](0117-windows-host-everyday-payments.md), [ADR 0123](0123-windows-host-financial-insights-and-budgets.md), [docs/roadmap.md](../roadmap.md#accessible-operation), [docs/milestone-7.md](../milestone-7.md#m7-04-accessibility-bfsgsgv-and-full-germanenglish-localization-verification)

---

## 1. Context and Problem Statement

Under the German Barrierefreiheitsstärkungsgesetz (BFSG) and its implementing ordinance (BFSGV § 17), consumer banking services, authentication mechanisms, and electronic payment software distributed in Germany must meet statutory accessibility requirements:
1. **Perceivable, operable, understandable, and robust interfaces:** All UI affordances must be fully navigable and controllable using keyboard-only interaction, screen readers (Narrator / NVDA), text scaling, and high-contrast display modes.
2. **Dual-indicator requirement:** Color must never be the sole visual means of conveying status, success, warning, or error states. State badges must feature explicit textual prefix symbols (`[✓ ...]` for verified/active, `[⚠ ...]` for warning/stale/quarantined, `[✕ ...]` for rejected/failed).
3. **Consumer language mandate:** Consumer information, legal disclosures, and operational guidance must be available in clear, unambiguous German (`de-DE`), alongside English (`en-US`), without confusing technical jargon.
4. **Exact money & date invariant:** Monetary amounts must preserve exact decimal precision with mandatory ISO currency codes, avoiding cultural rounding errors or floating-point truncation.

---

## 2. Decision and Architecture

### 2.1 BFSG Compliance Audit Engine (`BfsgAccessibilityAuditor`)

The host testing framework contains `BfsgAccessibilityAuditor` to programmatically inspect any Windows Forms container (`Form`, `Dialog`, `TabPage`, `Panel`):
- **Accessible Name & Role Coverage:** Asserts that 100% of interactive controls (`Button`, `TextBox`, `ComboBox`, `ListView`, `CheckBox`) and top-level dialogs have non-empty `AccessibleName`, descriptive `AccessibleDescription`, and an appropriate `AccessibleRole`.
- **Keyboard Operability:** Verifies that focus traversal (`TabStop = true` and ordered `TabIndex`) covers all editable fields, and that `CancelButton` and `AcceptButton` are wired on all modal dialogs.
- **Dual Visual Indicators:** Enforces textual tagging for stateful labels:
  - Active / Synchronized / Success: `[✓ Active]`, `[✓ Synchronized]`, `[✓ Verified]`
  - Warning / Stale / Quarantined: `[⚠ Stale]`, `[⚠ Quarantined]`, `[⚠ Action Required]`
  - Rejection / Error: `[✕ Rejected]`, `[✕ Blocked]`

### 2.2 Bilingual Localization Catalog (`LocalizationCatalog`)

All host UI strings are maintained in `LocalizationCatalog`:
- Supported locales: `de-DE` (German, default for German homebanking) and `en-US` (English).
- Covers all major product areas:
  - Navigation & Menus: Accounts, Transactions, Insights & Budgets, Payments.
  - Action Dialogs: Profile Unlock, PIN Prompt, SCA Challenge, CSV Export, Payment Initiation, Review, Standing Orders, Rules, Forecast, Splits.
  - Disclosures: Pure user-operated trust boundary statement, BFSG compliance declaration, FinTS product registration notice.
- Currency display formatting helper respects cultural conventions (`de-DE`: `1.234,56 EUR`, `en-US`: `1,234.56 EUR`) while maintaining exact decimal `Money` underlying representations.

---

## 3. Consequences

### Positive
- Fully fulfills BFSG and BFSGV regulatory mandates for consumer banking software in Germany.
- Guarantees seamless keyboard and screen-reader usability across all dialogs.
- Clean bilingual support for German and English homebanking users.

### Negative / Trade-offs
- UI layout must allow sufficient text padding to accommodate German string expansion (which typically exceeds English text length by 20–30%).
