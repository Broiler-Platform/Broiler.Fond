# ADR 0118: Deterministic Categorization Engine, Reversible Rules and Merchant Normalization

- **Status:** Accepted
- **Date:** 2026-10-19
- **Related:** [ADR 0098](0098-transaction-ledger-storage-extension.md), [ADR 0099](0099-windows-host-transaction-views-and-csv-export.md), [docs/roadmap.md](../roadmap.md#m6--insights-and-planning), [docs/milestone-6.md](../milestone-6.md)

---

## 1. Context and Problem Statement

Milestone 6 expands **Broiler Fond - Finance on Demand** from bank transport and everyday payments into explainable personal financial management.
A cornerstone of personal finance is categorizing transaction activity (e.g. Salary, Rent, Groceries, Insurance, Utilities).
In many consumer banking tools, categorization is implemented as a brittle heuristic or an opaque machine-learning model that silently overwrites data, makes unexplainable predictions, or provides no preview before bulk-tagging hundreds of records.

To uphold Broiler Fond's product principles:
1. **Explainable reports:** Every assigned category must trace directly to a user-defined or verified default rule with explicit criteria, or an explicit manual annotation.
2. **Deterministic evaluation:** Rules must evaluate in a predictable, priority-ordered manner where the same rule set applied to the same transactions always yields identical results.
3. **Reversibility & Bank Fact Immutability:** Applying rules modifies only append-only `AnnotationRevision` records. Bank facts (`SourceObservation`, `TransactionRevision`) and amounts are strictly immutable and cannot be altered by categorization.
4. **Preview before bulk execution:** Users must be able to preview prospective matches and conflicts against their existing transaction corpus before applying rule changes.
5. **Merchant normalization:** Bank remittance texts for card transactions or direct debits frequently contain noisy prefixes and suffixes (e.g. `PAYPAL *SPOTIFY 123456`, `SUMUP *CAFE BERLIN`, `EDEKA FILIALE 4918`). A deterministic merchant cleaner must extract clean merchant identities for counterparty display and matching.
6. **Strict BCL-only kernel boundary:** All rule evaluation, regular expressions, pattern matching, and string normalization must rely exclusively on .NET 10 runtime APIs.

---

## 2. Decision and Architecture

### 2.1 Category Domain Model (`Category`)

Categories are represented by a canonical model:
- `CategoryId`: immutable unsigned 64-bit local entity ID.
- `Name`: trimmed string (max 64 chars, e.g. "Groceries", "Housing & Rent", "Salary & Income").
- `ParentCategoryId`: optional parent category for hierarchical grouping (e.g. "Groceries" under "Living & Food").
- `IsSystemDefault`: boolean indicating whether the category is seeded by the system or created by the user.
- `ColorHex`: optional 7-character hex code for visual badge rendering (e.g. `#2E7D32`).

A standard set of well-known categories is provided by default:
- **Income:** Salary, Freelance / Other Income, Investment Return.
- **Fixed Expenses:** Rent & Mortgage, Utilities (Electricity, Gas, Water), Telecommunications & Internet, Insurance, Taxes & Charges.
- **Living & Daily:** Groceries, Household & Supplies, Clothing, Health & Pharmacy.
- **Mobility:** Public Transport, Fuel & Car, Travel & Holiday.
- **Leisure & Lifestyle:** Dining & Takeaway, Subscriptions & Streaming, Entertainment, Sport & Hobbies.
- **Financial & Transfers:** Own-Account Transfer, Investment & Savings, Fees & Interest.
- **Uncategorized:** Fallback category for unclassified transactions.

### 2.2 Deterministic Rule Model (`CategorizationRule`)

A categorization rule evaluates whether an incoming or existing transaction matches criteria:
- `RuleId`: immutable unsigned 64-bit local entity ID.
- `RuleName`: friendly name (e.g. "Match Edeka as Groceries").
- `TargetCategory`: target category name.
- `TargetTags`: optional collection of tags to apply (e.g. `["Food", "Essential"]`).
- `Priority`: integer priority (higher integer = evaluated earlier; default = 100).
- `Criteria`: composite matching criteria:
  - `CounterpartyPattern`: text pattern matching counterparty name or normalized merchant.
  - `IbanPattern`: exact or prefix matching counterparty IBAN.
  - `RemittancePattern`: regex or wildcard/contains pattern matching unstructured remittance text.
  - `PatternKind`: `Contains`, `Exact`, `StartsWith`, `Regex`.
  - `Direction`: `All`, `CreditOnly`, `DebitOnly`.
  - `MinAmount`: optional minimum amount limit.
  - `MaxAmount`: optional maximum amount limit.
  - `AccountScopeId`: optional constraint to a specific source account ID.

### 2.3 Rule Evaluation and Matching Order

The `CategorizationEngine` evaluates rules deterministically:
1. Active rules are ordered by `Priority` descending. If priorities are equal, rules are ordered by `RuleId` ascending.
2. For each transaction:
   - Rules are evaluated sequentially.
   - The first rule whose criteria all match wins (`FirstMatchWins`).
   - If no rule matches, the transaction retains its existing category or remains `Uncategorized`.
3. If an existing transaction already has a user-annotated manual category and the user selects "Preserve manual categories", the manual category is not overridden.

### 2.4 Preview Engine (`CategorizationPreview`)

Before modifying store state, the engine offers a `PreviewRules(rules, transactions, options)` method returning:
- Total transactions evaluated.
- Number of transactions that would receive a newly assigned category.
- Number of transactions whose category would change (with previous and prospective category).
- Detailed per-transaction match diagnostics indicating the specific rule ID and matched criteria.

### 2.5 Merchant Normalizer (`MerchantNormalizer`)

To support clean display and accurate matching:
- Strips payment gateway prefixes: `PAYPAL *`, `SUMUP *`, `KLARNA *`, `ADYEN *`, `STRIPE *`, `IZETTLE *`.
- Strips terminal locations, store branch markers, and terminal numbers (e.g. `FIL. 1234`, `MARKET 0815`, trailing city/postal markers).
- Strips punctuation and trailing reference digits.
- Returns normalized canonical merchant name (e.g. `PAYPAL *SPOTIFY P12345` -> `Spotify`; `EDEKA SAGT DANKE FIL 491` -> `Edeka`).

---

## 3. Consequences

### Positive
- Transparent, explainable, and reproducible categorization across all transactions.
- Zero risk of silent or accidental bank fact alteration; all annotations are isolated in `AnnotationRevision`.
- Instant preview allows users to test new regex or pattern rules before applying them to hundreds of historical records.
- Canonical merchant normalization provides clean, consistent payee displays across multibank statements.

### Negative / Limitations
- Complex natural language or fuzzy clustering is not supported; all matching is explicit and rule-based.
- Regex rules with catastrophic backtracking could stall execution; the engine enforces strict regex timeouts (`TimeSpan.FromMilliseconds(200)`).
