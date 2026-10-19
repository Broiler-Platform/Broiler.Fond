# ADR 0144: Accounting Exports, DATEV Interoperability, and Enterprise Compliance

- **Status:** Accepted
- **Date:** 2026-10-19
- **Related:** [ADR 0140](0140-professional-roles-and-four-eyes-approval-workflow.md), [ADR 0141](0141-ebics-protocol-architecture-and-order-types.md), [ADR 0142](0142-iso-20022-batch-processing-and-granular-reconciliation.md), [ADR 0143](0143-distributed-electronic-signatures-veu-lifecycle.md), [docs/roadmap.md](../roadmap.md#separate-professional-track), [docs/professional-track.md](../professional-track.md#p-05-accounting-exports-datev-interoperability--enterprise-compliance)

---

## 1. Context and Problem Statement

Corporate financial accounting in Germany requires seamless interoperability with certified tax software and CPA workflows, predominantly governed by the DATEV accounting exchange format (*DATEV-Format Buchungsstapel*, version 700).

Consumer CSV exports are insufficient for enterprise tax and accounting compliance:
1. **DATEV Header Requirements:** DATEV imports strictly require the standardized `EXTF` header specification line declaring format version 700, category 21 (Buchungsstapel), client number, consultant number, fiscal year start, and account length.
2. **Chart of Accounts (Kontenrahmen):** Corporate transactions must be mapped to recognized German standard charts of accounts (SKR03 or SKR04), distinguishing bank transit accounts (e.g. 1200 in SKR03, 1800 in SKR04) from counter-accounts and tax keys (*BU-Schlüssel*).
3. **Number and Date Formatting:** Amounts must be represented without positive/negative signs, using explicit Debit/Credit indicator columns (`S` for *Soll*, `H` for *Haben*), German comma separators, and `ddMM` receipt date syntax.
4. **Enterprise Audit Trail & Manifest:** Statutory retention (GoBD) requires exporting an immutable audit package bundled with cryptographic SHA-256 manifests to verify that exported records have not been tampered with post-export.
5. **Key Recovery Policy:** Enterprise compliance mandates policy-driven separation between operational user signature keys and corporate escrow/recovery credentials.

---

## 2. Decision and Architecture

### 2.1 Standard Charts of Accounts (`ChartOfAccounts`)

- `SKR03`: Process-oriented chart of accounts. Default bank account: `1200`.
- `SKR04`: Balance-sheet-oriented chart of accounts. Default bank account: `1800`.

### 2.2 DATEV Booking Record (`DatevBookingRecord`)

- `Amount`: Exact positive decimal `Money`.
- `DebitCredit`: `Soll` (`S` for outgoing payments) or `Haben` (`H` for incoming payments).
- `Account`: Standard bank account code (e.g. 1200 or 1800).
- `ContraAccount`: Counterparty ledger account (e.g. revenue 8400/4400 or expense accounts).
- `PostingKey`: Optional tax/correction key (*BU-Schlüssel*, e.g. "9" for 19% VAT).
- `ReceiptDate`: `DateOnly` rendered as `ddMM`.
- `ReceiptField1`: Invoice or payment reference (alphanumeric, max 36 characters).
- `PostingText`: Sanitized description (max 60 characters).

### 2.3 DATEV Format Generator (`DatevExportGenerator`)

- Generates the official DATEV header:
  `"EXTF";700;21;"Buchungsstapel";...;[ConsultantNumber];[ClientNumber];[FiscalYearStart];...`
- Outputs semicolons as delimiters, quoting text fields containing special characters.
- Converts amounts to German format with comma (`1234,56`).

### 2.4 Enterprise Audit Bundle (`EnterpriseAuditExporter`)

- Collects corporate audit entries, batch logs, and reconciled bank transactions.
- Computes SHA-256 digests for each exported component.
- Outputs a signed `manifest.json` containing entry hashes, creation timestamp, and export provenance.

### 2.5 Organization Key Recovery Policy (`OrganizationKeyRecoveryPolicy`)

- Provides enterprise governance for credential escrow and emergency access:
  - `RequiresEscrowRecovery`: Declares whether escrow recovery is active.
  - `RecoveryKeyIdentifier`: Identifies the organization recovery key.
  - `EscrowAuthorizedRoles`: Defines roles permitted to authorize key recovery operations (strictly `Administrator`).

---

## 3. Consequences and Verification

### Positive
- Fully compliant DATEV CSV generation directly ingestible by German CPA and tax software (DATEV Unternehmen online / Kanzlei-Rechnungswesen).
- Verifiable GoBD-compliant enterprise audit manifests.
- Zero external libraries or closed third-party format converters.

### Negative
- Advanced counter-account categorization requires corporate chart of accounts configuration.

### Verification Plan
- Automated test suite `AccountingExportTests`:
  1. Chart of accounts configuration (`SKR03`, `SKR04`).
  2. DATEV booking record construction, debit/credit indicator, and receipt date formatting (`ddMM`).
  3. DATEV header line generation (EXTF 700 category 21) and CSV syntax.
  4. German decimal number formatting (`1250,75`).
  5. Audit package manifest calculation and SHA-256 verification.
  6. Key recovery policy invariants.
