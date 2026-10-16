# ADR 0096: Statement Wire Parsers for SWIFT MT940 and ISO 20022 CAMT XML

- **Status:** Accepted
- **Date:** 2026-10-16
- **Related:** [ADR 0094](0094-immutable-transaction-domain-models.md), [ADR 0095](0095-statement-parameters-and-capability-negotiation.md), [docs/roadmap.md](../roadmap.md#m2--transactions-and-account-details), [docs/milestone-2.md](../milestone-2.md)

---

## 1. Context and Problem Statement

In Milestone 2, **Broiler Fond - Finance on Demand** introduces transaction retrieval and history inspection.
FinTS 3.0 servers report transactions through either:
1. **SWIFT MT940 / MT942:** Legacy line-based text format containing opening/closing balances (`:60F:`, `:62F:`),
   statement lines (`:61:`), and account owner remittance information (`:86:`).
2. **ISO 20022 CAMT XML:** Structured XML documents (`camt.053` for booked statements, `camt.052` for interim/pending reports).

To satisfy the product principles:
- **Exact money:** Floating-point parsing is forbidden; amounts must parse as exact decimal values with mandatory currency.
- **Security & DoS protection:** Untrusted XML inputs must prohibit DTD processing, disable external resolution, and enforce strict depth and memory limits.
- **Truthful bank facts:** Values such as booking dates, value dates, counterparty details, SEPA remittance subfields,
  and reversal indicators (`RC`/`RD`) must be captured verbatim without guessing or lossy conversions.
- **BCL-only boundary:** Parsers must be implemented exclusively with .NET 10 runtime APIs; no third-party packages may be introduced.

---

## 2. Decision and Architecture

We implement two dedicated statement parsers in `Broiler.Fond.Kernel.Formats`:

### 2.1 SWIFT MT940 / MT942 Parser ([`Mt940Parser.cs`](file:///d:/Broiler.Fond/src/Broiler.Fond.Kernel/Formats/Mt940/Mt940Parser.cs))

Under `Broiler.Fond.Kernel.Formats.Mt940`:
- **Statement Structure:**
  - Mandatory `:20:` Transaction Reference Number and `:25:` Account Identification (IBAN or national account).
  - Mandatory `:28C:` or `:28:` Statement number and optional sequence number.
  - Mandatory `:60F:` or `:60M:` Opening balance (`[D|C][YYMMDD][Currency 3 chars][Amount with , or .]`).
  - Repeated `:61:` Statement lines with value date, optional 4-digit booking date (`MMDD`), debit/credit mark
    (`C`, `D`, or reversal `RC`/`RD`), exact amount, transaction code, and customer/bank references.
  - Multi-line `:86:` Remittance Information: parses German SEPA structured subfields (`?00` to `?99`):
    - `?00`: Booking text (e.g. `GUTSCHRIFT`, `LASTSCHRIFT`)
    - `?20`..`?29`: Remittance purpose text
    - `?30`: Counterparty BIC
    - `?31`: Counterparty IBAN
    - `?32` / `?33`: Counterparty Name
    - `?34`: Mandate ID / return reason
    - `?35`: Creditor ID
  - Optional `:62F:` or `:62M:` Closing balance and `:64:`/`:65:` forward balances.
- **Resource Bounds & Fail-Closed:**
  - Maximum 10 MiB input size.
  - Maximum 1,000 statements per batch.
  - Maximum 50,000 entries per statement.
  - Inputs without valid statement boundaries fail closed with `FormatException`.

### 2.2 ISO 20022 CAMT XML Parser ([`CamtXmlParser.cs`](file:///d:/Broiler.Fond/src/Broiler.Fond.Kernel/Formats/Camt/CamtXmlParser.cs))

Under `Broiler.Fond.Kernel.Formats.Camt`:
- **Secure XML Settings:**
  - `DtdProcessing = DtdProcessing.Prohibit` (defense against XXE and expansion attacks).
  - `XmlResolver = null`.
  - `MaxCharactersInDocument = 20_000_000`.
  - Parsed via `XDocument.Load(reader)` using standard .NET BCL `System.Xml.Linq`.
- **Supported Schemas:**
  - `camt.053` (`BkToCstmrStmt`) across versions (`.001.02`, `.001.08`) for settled/booked transactions.
  - `camt.052` (`BkToCstmrAcctRpt`) across versions for interim/pending movements.
- **Entry Extraction:**
  - Amount, currency, and credit/debit indicator (`CRDT` vs `DBIT`).
  - Status mapping: `BOOK` maps to `TransactionStatus.Booked`; `PDNG` or `INFO` maps to `TransactionStatus.Pending`.
  - Booking and value dates (`BookgDt`, `ValDt`).
  - Related parties (`RltdPties`): for incoming credit (`CRDT`), counterparty is Debtor (`Dbtr`); for outgoing debit (`DBIT`), counterparty is Creditor (`Cdtr`).
  - Related agents (`RltdAgts`): BIC / BICFI.
  - End-to-End reference (`EndToEndId`), Mandate ID (`MndtId`), Creditor ID (`CdtrSchmeId`).
  - Remittance information (`RmtInf/Ustrd` multi-line concatenation).
  - Return debit reason code (`RtrInf/Rsn/Cd`, e.g. `MS02`, `AC04`).
- **Resource Bounds & Fail-Closed:**
  - Maximum 10 MiB input size.
  - Maximum 1,000 statements per document.
  - Maximum 50,000 entries per statement.
  - Documents lacking `<Stmt>` or `<Rpt>` fail closed with `FormatException`.

---

## 3. Invariants and Verification

- **BCL-only:** Both parsers use only standard .NET 10 runtime APIs, complying 100% with `Verify-KernelBoundary.ps1`.
- **Exact money:** Amounts are converted directly from parsed character tokens into [`Money`](file:///d:/Broiler.Fond/src/Broiler.Fond.Kernel/Domain/Money.cs) using `decimal.TryParse` with invariant culture.
- **Verified by test suite:** 61 automated checks in [`StatementWireParserTests.cs`](file:///d:/Broiler.Fond/tests/Broiler.Fond.Kernel.Tests/StatementWireParserTests.cs) verifying MT940 single/multi statements, SEPA structured subfields, reversal markings (`RC`/`RD`), CAMT.053 booked statements, CAMT.052 pending reports, return codes, DTD attack rejection, and size bounds.
