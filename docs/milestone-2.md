# Milestone 2 — Transactions and account details

- **Brand:** Broiler
- **Product:** Fond - Finance on Demand
- **Technical name:** `Broiler.Fond`

**Status:** In progress (Milestone 1 foundation complete; Milestone 2 initiated).

**Last updated:** 2026-10-16

The [roadmap](roadmap.md#m2--transactions-and-account-details) defines the product specification.
This backlog breaks Milestone 2 into reviewable increments; completion of one increment does not
imply completion of the MVP.

---

## 1. Outcome

A read-only MVP explains what changed in each account over time. The client retrieves, parses,
authenticates, and persists bank-reported transaction history (booked and pending) in the formats
advertised by each bank, presenting a truthful, searchable, and exportable ledger without
modifying bank-reported facts.

---

## 2. Milestone Principles and Invariants

- **Exact money:** Transaction amounts use exact decimal representation paired with an explicit ISO 4217
  currency identifier (`Money`). Floating-point money arithmetic is strictly forbidden.
- **Immutable bank facts:** Bank observations are strictly append-only (`TransactionRevision`).
  Counterparty names, IBANs, booking dates, value dates, and bank references reflect verbatim
  bank evidence and cannot be overwritten.
- **Separation of bank facts and user annotations:** User-supplied tags, categories, and notes
  reside in an independent, append-only `AnnotationRevision` stream, ensuring user edits never
  mutate or conceal bank records.
- **No silent deduplication or conflation:** Two identical rows in one bank statement remain
  two distinct observations. Ambiguous, one-to-many, reference-reuse, correction, reversal,
  and rebooking cases remain separate and auditable.
- **Pending-to-booked candidate linking:** Unique bank references or structured remittance
  evidence can link a pending pre-authorization to a booked transaction without double counting;
  unlinked pending items expire or resolve explicitly.
- **Overlap sync idempotency:** Repeated overlapping range queries of historical data produce
  zero duplicated transactions and zero balance discrepancies.
- **Local privacy and memory safety:** Banking PINs and TANs are never persisted in transaction
  stores or logs. Transaction search executes entirely on-device with zero telemetry or network calls.

---

## 3. Work Breakdown Structure (M2-01 through M2-06)

### M2-01: Immutable Transaction Domain Models

- [x] Define canonical transaction domain entities ([ADR 0094](adr/0094-immutable-transaction-domain-models.md)):
  - `TransactionId`, `TransactionRevisionId`, and `AnnotationRevisionId` using the shared unsigned 64-bit allocator.
  - `TransactionStatus` (`Booked`, `Pending`, `Cancelled`) and `TransactionKind` (`Transfer`, `Debit`, `Credit`, `ReturnDebit`, `Fee`, `Interest`, `CardPayment`, `Other`).
  - `TransactionRevision` capturing bank-reported facts: amount (`Money`), booking date, value date, counterparty name/IBAN/BIC,
    end-to-end reference, mandate ID, creditor ID, remittance text, proprietary bank transaction codes, and return debit reasons.
  - `AnnotationRevision` capturing user tags, categories, notes, and custom review markers.
  - `TransactionObservation` preserving source request provenance, bank statement sequence, and page tokens.
  - `PendingToBookedLink` maintaining candidate correlation evidence without destroying original observations.
- [x] Add unit verification suites validating precision boundaries, immutability, revision history, and link transitions
  (`TransactionDomainTests`, 56 checks).

### M2-02: FinTS Statement Parameter Schemas & Capability Negotiation

- [x] Parse `HIKAZS` versions 5–7 (SWIFT MT940 statement capabilities) and `HICAZS` versions 1–2 (ISO 20022 CAMT capabilities) ([ADR 0095](adr/0095-statement-parameters-and-capability-negotiation.md)).
- [x] Implement capability comparison against user parameter data (`HIUPA`) and bank parameter data (`HIBPA`) ([ADR 0095](adr/0095-statement-parameters-and-capability-negotiation.md)).
- [x] Determine supported formats and version preferences per account without hardcoded fallback assumptions (`FinTsStatementPreferenceNegotiator`).
- [x] Verify capability matching, diagnostic issues, and preference negotiation (`StatementCapabilitiesTests`, 78 checks).

### M2-03: Statement Wire Parsers (MT940 and CAMT)

- [x] **SWIFT MT940 / MT942 parser ([ADR 0096](adr/0096-statement-wire-parsers-mt940-and-camt.md)):**
  - Parse Field 20 (Transaction Reference Number), Field 25 (Account Identification), Field 28C (Statement Number).
  - Parse Field 60F/60M (Opening Balance), Field 62F/62M (Closing Balance).
  - Parse Field 61 (Statement Line): value date, booking date, debit/credit mark (`C`, `D`, `RC`, `RD`), currency, exact `Money` amount, transaction type code, customer/bank references.
  - Parse Field 86 (Information to Account Owner): German SEPA structured subfields (`?00` to `?99`: BIC, IBAN, Name, purpose, mandate, creditor ID) and unstructured lines.
  - Enforce bounds (10 MiB max input, 1,000 statements, 50,000 entries) and fail-closed malformed line rejection (`Mt940Parser`).
- [x] **ISO 20022 CAMT XML parser (`camt.052` / `camt.053`) ([ADR 0096](adr/0096-statement-wire-parsers-mt940-and-camt.md)):**
  - Implement secure XML reader (`DtdProcessing.Prohibit`, `XmlResolver = null`, 20M char bound).
  - Parse `BkToCstmrStmt` (camt.053) for booked statements and `BkToCstmrAcctRpt` (camt.052) for interim/pending movements.
  - Extract statement entries (`Ntry`), transaction details (`TxDtls`), exact decimal `Money` amount/currency, credit/debit indicator (`CRDT`, `DBIT`),
    booking/value dates (`BookgDt`, `ValDt`), counterparty party/account (`RltdPties`, `RltdAgts`), remittance information (`RmtInf`), and return debit reason codes (`RtrInf/Rsn/Cd`) (`CamtXmlParser`).
- [x] Add automated test suites verifying parsing, reversals, structured SEPA subfields, security constraints, and boundary conditions (`StatementWireParserTests`, 61 checks).

### M2-04: Multi-Page Pagination, Range Queries, and Overlap Sync

- [ ] Construct bounded statement range requests (`HKKAZ` / `HKCAZ`) with optional start date, end date, and continuation tokens.
- [ ] Implement multi-page continuation coordinator with strict page limits and monotonic timeouts.
- [ ] Implement overlap sync algorithm: query recent window (e.g. last 30–90 days), correlate against cached observations,
  detect gaps, and update latest sync checkpoint.
- [ ] Verify ten repeated overlapping syncs produce zero duplicate transactions and zero balance discrepancies.

### M2-05: Transaction Ledger Storage Extension

- [ ] Extend profile container schema (`store-v2.xsd` or dedicated `transactions-v1.xsd` within profile ZIP container).
- [ ] Maintain atomic generation promotion and crash-resilient privacy purge.
- [ ] Ensure backward compatibility: older profiles cleanly migrate without data loss.
- [ ] Verify serialization round-trips, hostile input rejection, and tamper detection.

### M2-06: Windows Host UI Transaction Views & CSV Export

- [ ] Implement accessible Transaction List and Detail view in `Broiler.Fond.Host.Windows`:
  - Keyboard navigation, high-DPI rendering, and accessible screen-reader labels.
  - Filtering by account, date range, transaction status (booked vs pending), amount range, and text search.
  - Detail pane showing complete bank references, counterparty info, and unedited remittance information.
- [ ] Implement local high-performance search (< 300 ms p95 with 100,000 cached transactions).
- [ ] Implement privacy-conscious CSV export with user preview, file destination selection, and zero telemetry.

---

## 4. Exit Criteria

- The application retrieves the maximum supported history range and clearly states when an institution limits it.
- Ten repeated overlapping syncs of every conformance fixture yield no extra visible events or balance impacts.
- Interrupted multi-page sync resumes without hiding a gap or publishing a partial run as complete.
- Unique evidence links pending to booked items without double counting; ambiguous cases remain separate for audit.
- Exact source amount, currency, booking date, and value date survive round-trip persistence and export.
- Warm local search completes within 300 ms at p95 with 100,000 cached transactions.
- Zero credentials (PINs/TANs) exist in archives, local logs, or exported files.
- Two-human release review approves the candidate artifact.
