# ADR 0109: Single SEPA Credit Transfer FinTS Wire Codec and SCA Authorization Continuation

## Status
Accepted

## Context
Milestone 4 requires implementing the wire codecs, ISO 20022 `pain.001` XML generation, Strong Customer Authentication (SCA) continuation, submission receipt generation, and network failure injection resilience for Single SEPA Credit Transfers (`HKCCS` / `HICCS`).

Key requirements:
1. **Strict BCL-Only Boundary:** Zero third-party XML or ISO 20022 packages. The `pain.001.001.03` generation and FinTS segment encoding must use standard .NET 10 runtime APIs (`System.Xml.XmlWriter`, `System.Text.StringBuilder`, etc.).
2. **Deterministic `pain.001.001.03` XML Document Generation:**
   - Standard SEPA credit transfer schema `urn:iso:std:iso:20022:tech:xsd:pain.001.001.03`.
   - Uses `EndToEndId` from the pre-committed `PaymentAttempt` (e.g. `BF-{yyyyMMdd}-{OpaqueRandomHex}`).
   - Exact decimal currency amount in EUR with fixed 2-place fractional format (`123.45`).
   - UTF-8 without byte-order mark (BOM), deterministic element order, and strictly valid SEPA character set.
3. **FinTS 3.0 `HKCCS` Wire Codec:**
   - Segment format: `HKCCS:segNum:segVer+KtvInt+SEPADescriptor+SEPAPainMessage'`.
   - Versions 1 and 2 supported with proper escaping and binary length prefix (`@length@<Document...`).
   - Response parsing for `HICCS` (order confirmation reference) and correlated `HIRMS` return codes (`0020`, `0010`, `0030`, `3905`, `9010`, etc.).
4. **SCA Authorization Continuation:**
   - Two-step authorization: when bank returns `3905` or `3920` requiring SCA, the process binds to the existing `PaymentAttempt`, preserving its immutable ID and `EndToEndId`.
   - Interactive and decoupled challenge support: challenge text and matrix codes display dynamic linking details (amount, recipient, IBAN).
   - Ephemeral TANs held only in bounded in-memory buffers; zeroized immediately upon transmission.
5. **Submission Receipt (`PaymentSubmissionReceipt`):**
   - Non-secret immutable receipt recording `AttemptId`, `EndToEndId`, `BankReference`, `ResponseCode`, `ResponseText`, timestamps, and outcome state.
6. **Failure Injection Resilience:**
   - Failure before send: safely aborts or remains in `AwaitingAuthorization` without crossing wire boundary.
   - Failure during transmission or awaiting response: transitions durably to `PaymentState.Unknown`. Resubmission is strictly prohibited without explicit reconciliation evidence.

## Decisions

### 1. ISO 20022 `pain.001.001.03` Document Writer (`SepaPainDocumentWriter`)
We implement `SepaPainDocumentWriter` in `src/Broiler.Fond.Kernel/FinTs/SepaPainDocumentWriter.cs`:
- Generates compliant XML for `CstmrCdtTrfInitn` with `GrpHdr`, `PmtInf`, `Dbtr`, `DbtrAcct`, `Cdtr`, `CdtrAcct`, `Amt`, `RmtInf`.
- Uses `System.Xml.XmlWriter` with UTF-8 encoding and omit XML declaration when embedded in FinTS segment or full XML document.
- Strictly validates debtor and creditor IBANs, amount currency (EUR), non-empty names, and remittance length (<= 140 chars).

### 2. FinTS 3.0 `HKCCS` Request Writer & `HICCS` Response Parser
- `FinTsSepaTransferRequestWriter`:
  - Encodes the `HKCCS` segment with international account connection (`KtvInt`), SEPA descriptor (`urn:iso:std:iso:20022:tech:xsd:pain.001.001.03`), and `@length@` prefixed pain XML payload.
  - Encodes complete signed or challenge-continuation frames.
- `FinTsSepaTransferResponseParser`:
  - Parses `HICCS` segment returning bank-assigned order reference.
  - Correlates with `HIRMS` / `HIRMG` return codes to classify outcome (`Accepted`, `BankPending`, `AwaitingAuthorization` for SCA, or `Rejected`).

### 3. Submission Receipt (`PaymentSubmissionReceipt`)
- Canonical domain value object capturing the outcome of a bank submission attempt.
- Contains no secrets, PINs, or TANs.

### 4. Wire Execution & Failure Injection Simulator (`SepaTransferExecutionCoordinator`)
- Implements the transactional flow:
  1. Pre-send commit verification: asserts `PaymentAttempt` is committed and in valid state (`AwaitingAuthorization` or `Submitting`).
  2. Wire transmission via `IFinTsTransport` with injected failure points (before send, during send, after send timeout).
  3. Response evaluation: updates `PaymentAttempt` to `Accepted`, `BankPending`, `Rejected`, or `Unknown` (on transport fault).
  4. Generates `PaymentSubmissionReceipt` and audit transition record.

## Consequences

### Positive
- Fully standards-compliant SEPA credit transfer initiation and receipt generation using pure .NET 10 shared framework APIs.
- Provable resistance against duplicate submissions via durable `Unknown` outcome handling under network faults.
- Ephemeral credentials are never exposed or persisted.

### Negative / Trade-offs
- Writing raw ISO 20022 XML with `XmlWriter` requires rigorous verification of element ordering and schema constraints.
