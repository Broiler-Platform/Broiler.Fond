# ADR 0142: ISO 20022 Batch Processing and Granular Reconciliation

- **Status:** Accepted
- **Date:** 2026-10-19
- **Related:** [ADR 0140](0140-professional-roles-and-four-eyes-approval-workflow.md), [ADR 0141](0141-ebics-protocol-architecture-and-order-types.md), [docs/roadmap.md](../roadmap.md#separate-professional-track), [docs/professional-track.md](../professional-track.md#p-03-iso-20022-batch-processing-bulk-payments--granular-reconciliation)

---

## 1. Context and Problem Statement

Corporate financial operations require processing bulk payments (such as payroll runs, vendor payables, and customer subscription direct debits) as single consolidated batches rather than individual consumer transfers.

In European banking, batch money movement is governed by ISO 20022 XML standards:
- `pain.001.001.09`: Customer Credit Transfer Initiation.
- `pain.008.001.08`: Customer Direct Debit Initiation.
- `pain.002.001.10`: Customer Payment Status Report.
- `camt.053.001.08`: Bank-to-Customer Statement (collective booking).

Key challenges:
1. **Control Sum Invariance:** Statutory SEPA rulebooks mandate exact transaction counts (`NbOfTxs`) and control sums (`CtrlSum`). Any decimal rounding or truncation results in immediate file rejection by the bank.
2. **Collective Booking Disaggregation:** Banks routinely post batch submissions as a single aggregate debit/credit entry on the bank statement. Financial accounting systems require granular reconciliation to map the single booking back to individual line items.
3. **Partial Failure and Return Tracking:** If 3 out of 1,000 transactions fail (e.g. invalid account, mandate expired, insufficient funds), the bank accepts 997 and rejects 3 via `pain.002` `RJCT`. The domain must track individual line-item statuses without corrupting batch totals.

---

## 2. Decision and Architecture

### 2.1 Batch Domain Model (`BulkPaymentBatch`, `BulkPaymentItem`)

- `BulkPaymentItem`:
  - `EndToEndId`: Unique corporate reference per transaction.
  - `CounterpartyName`: Creditor or Debtor name.
  - `CounterpartyIban`: Standardized SEPA IBAN.
  - `CounterpartyBic`: Optional BIC.
  - `Amount`: Exact decimal `Money`.
  - `RemittanceInformation`: Unstructured payment reference.
  - `Status`: `Pending`, `Settled`, `Rejected`.
  - `RejectionReason`: Optional code (e.g. `AC01`, `AM04`, `MD01`).

- `BulkPaymentBatch`:
  - `BatchId`: Internal unique batch identifier.
  - `MessageId`: EPC-compliant message reference (`MsgId`).
  - `PaymentInfoId`: Payment information identifier (`PmtInfId`).
  - `InitiatorIban`: Originating corporate IBAN.
  - `InitiatorName`: Originating organization name.
  - `ExecutionDate`: Requested execution date (`DateOnly`).
  - `BatchType`: `CreditTransfer` (`pain.001`) or `DirectDebit` (`pain.008`).
  - `Items`: Immutable list of `BulkPaymentItem`.
  - `ControlSum`: Calculated sum of all items in `Money`.
  - `TransactionCount`: Count of items.

### 2.2 Statutory Batch Validation (`BulkBatchValidator`)

- Enforces that `Items.Count > 0`.
- Validates that `ControlSum` matches the exact sum of all item amounts using decimal arithmetic.
- Validates that all items share the same currency as the batch.
- Enforces SEPA IBAN structure and character length constraints.

### 2.3 Pure BCL ISO 20022 XML Codec (`Iso20022BatchXmlCodec`)

- Generates schema-compliant XML for `pain.001.001.09` and `pain.008.001.08`.
- Streams XML generation using `XmlWriter` without in-memory DOM overhead.
- Parses batch files and status reports using secure `XmlReader` (`DtdProcessing.Prohibit`).

### 2.4 Granular Reconciliation Engine (`BatchReconciliationEngine`)

- **Collective Statement Matching:** Links a CAMT statement collective entry (`camt.053`) to a `BulkPaymentBatch` by `MessageId` or `PaymentInfoId`, verifying that the statement booking amount matches the batch control sum.
- **Payment Status Report Ingestion:** Ingests `pain.002` XML reports, parses `<OrgnlTxRef>` and `<TxSts>` tags, updates corresponding `BulkPaymentItem` records to `Rejected` with their specific rejection reason (`StsRsnInf`), and marks remaining items as `Settled`.
- **Reconciliation Summary:** Generates an immutable `BatchReconciliationResult` reporting total submitted, settled, and rejected amounts.

---

## 3. Consequences and Verification

### Positive
- Enables high-performance bulk payment preparation and submission via EBICS.
- Transparent and auditable reconciliation between collective bank statements and line items.
- Full compliance with EPC SEPA rulebooks and zero external dependencies.

### Negative
- High-volume batch generation requires careful memory management when handling tens of thousands of items.

### Verification Plan
- `BulkPaymentBatchTests`:
  1. Batch creation, control sum calculation, and validation with exact decimal arithmetic.
  2. Rejection of batches with mismatched control sums or invalid IBANs.
  3. ISO 20022 `pain.001.001.09` XML generation and roundtrip parsing.
  4. ISO 20022 `pain.008.001.08` direct debit batch generation.
  5. Collective CAMT statement entry matching.
  6. `pain.002` status report ingestion with partial line-item rejection (`RJCT`) handling.
