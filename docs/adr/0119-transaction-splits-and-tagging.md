# ADR 0119: Transaction Splits and Multi-Tagging in Profile Store

- **Status:** Accepted
- **Date:** 2026-10-19
- **Related:** [ADR 0094](0094-immutable-transaction-domain-models.md), [ADR 0098](0098-transaction-ledger-storage-extension.md), [ADR 0118](0118-deterministic-categorization-and-rules-engine.md), [docs/roadmap.md](../roadmap.md#personal-finance-and-reporting), [docs/milestone-6.md](../milestone-6.md#m6-02-transaction-splits--multi-tagging-in-profile-store)

---

## 1. Context and Problem Statement

Transactions in consumer banking often encompass expenditures across multiple distinct economic categories.
For example:
- A €120.00 debit at a hypermarket may include €75.00 for groceries, €30.00 for household supplies, and €15.00 for clothing.
- A €1,500.00 credit may combine salary with an expense reimbursement.
- A card purchase may include a €20.00 cash-back withdrawal alongside goods purchased.

In Broiler Fond, the following foundational invariants must be strictly maintained:
1. **Bank fact immutability:** Bank records (`SourceObservation`, `TransactionRevision`) are immutable representations of what the institution booked. A transaction must never be divided into synthetic bank transactions or deleted.
2. **Strict balance invariant:** The sum of split item amounts must exactly equal the parent transaction amount in the same currency:
   $$\sum_{i=1}^{n} \text{SplitAmount}_i = \text{TransactionAmount}$$
   Zero rounding error, decimal floating approximations, or unaccounted remainders are tolerated.
3. **Reversibility and user ownership:** Splits and tags belong exclusively to the append-only user annotation layer (`AnnotationRevision`). Synchronizing new bank statements or refreshing transactions cannot alter or destroy user splits.
4. **Multi-tagging:** Users must be able to attach multiple arbitrary tags (e.g. `#Vacation2026`, `#TaxDeductible`, `#ProjectX`) at both transaction and split-item granularity.
5. **Storage compatibility:** The portable encrypted profile store (`transactions.xml` schema `transactions-v1.xsd`) must serialize split items deterministically while maintaining backward-compatibility for archives created in earlier milestones.
6. **Strict BCL-only kernel boundary:** Implementation must use only .NET 10 runtime APIs without third-party libraries.

---

## 2. Decision and Architecture

### 2.1 Split Item Domain Model (`TransactionSplitItem`)

A split item represents one categorized partition of a parent transaction:
- `Index`: 0-based integer ordinal indicating position within the split set.
- `Amount`: exact `decimal` amount. The sign must match the parent transaction direction unless an explicit cash-back/offset split is represented, with the net sum strictly matching the parent amount.
- `Category`: non-empty category name (max 64 chars, e.g. "Groceries").
- `Note`: optional user note / memo (max 1024 chars).
- `Tags`: read-only collection of user tags (up to 16 tags per split, max 64 chars each).

### 2.2 Split Invariants and Validation (`TransactionSplitValidator`)

Split validation enforces:
1. **Cardinality:** When splits are defined, there must be at least two split items ($\ge 2$). A single split item is redundant with whole-transaction categorization.
2. **Exact Decimal Balance:** The sum of split amounts must exactly equal `parent.Amount` using exact decimal arithmetic:
   ```csharp
   decimal sum = items.Sum(s => s.Amount);
   if (sum != parentAmount)
   {
       throw new InvalidOperationException($"Sum of split amounts ({sum}) does not equal transaction amount ({parentAmount}).");
   }
   ```
3. **Non-Zero Amounts:** Every split item must have a non-zero amount.
4. **Tags Hygiene:** Tags are trimmed, deduplicated case-insensitively, and bounded to maximum length and count limits.

### 2.3 Integration into `AnnotationRevision`

`AnnotationRevision` is extended with:
- `Splits`: optional read-only collection `IReadOnlyList<TransactionSplitItem>?`.
- `IsSplit`: convenience property returning `Splits != null && Splits.Count > 1`.
- When a transaction is split, `AnnotationRevision.Category` is set to `"[Split]"` or left null to indicate that classification is governed by individual split items.

### 2.4 Profile Storage Extension (`transactions-v1.xsd` & `StoreArchiveCodec`)

The XML schema `transactions-v1.xsd` is extended within the `annotation` element:

```xml
<xs:element name="annotation" minOccurs="0" maxOccurs="unbounded">
  <xs:complexType>
    <xs:sequence>
      <xs:element name="revisionTimestamp" type="xs:string" />
      <xs:element name="userNote" type="xs:string" minOccurs="0" />
      <xs:element name="category" type="xs:string" minOccurs="0" />
      <xs:element name="tag" type="xs:string" minOccurs="0" maxOccurs="64" />
      <xs:element name="split" minOccurs="0" maxOccurs="unbounded">
        <xs:complexType>
          <xs:sequence>
            <xs:element name="amount" type="xs:string" />
            <xs:element name="category" type="xs:string" />
            <xs:element name="note" type="xs:string" minOccurs="0" />
            <xs:element name="tag" type="xs:string" minOccurs="0" maxOccurs="32" />
          </xs:sequence>
          <xs:attribute name="index" type="xs:int" use="required" />
        </xs:complexType>
      </xs:element>
    </xs:sequence>
    <xs:attribute name="id" type="f:positiveId" use="required" />
    <xs:attribute name="transactionId" type="f:positiveId" use="required" />
  </xs:complexType>
</xs:element>
```

In `StoreArchiveCodec`:
- `StoreSplitItemEntry(int Index, decimal Amount, string Category, string? Note, IReadOnlyList<string> Tags)` is added to `StoreAnnotationRevisionEntry`.
- Serialized deterministically ordered by `index` ascending.
- Amounts are formatted with invariant decimal representation (`0.00`).
- Because `<xs:element name="split" minOccurs="0" maxOccurs="unbounded">` is optional, earlier archives load without schema violation.

---

## 3. Consequences

### Positive
- Users can partition multi-category shopping receipts or complex transfers accurately.
- Strict decimal equality prevents balance drifts in financial reports or budgets.
- Bank transaction facts remain 100% untouched and tamper-evident.
- Seamless backward compatibility with existing profile archives.

### Negative / Limitations
- A split item is an annotation construct and does not produce individual bank-level IDs. It cannot be individually reconciled with a bank reference.
