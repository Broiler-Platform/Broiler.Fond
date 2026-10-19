# ADR 0138: FinTS 4.1 Account Operations and Native ISO 20022 Integration

- **Status:** Accepted
- **Date:** 2026-10-19
- **Related:** [ADR 0008](0008-account-values-and-exact-money.md), [ADR 0095](0095-statement-capabilities-and-wire-parsers.md), [ADR 0106](0106-payment-domain-draft-revisions-and-sepa-validation.md), [ADR 0109](0109-sepa-credit-transfer-fints-codec-and-sca.md), [ADR 0135](0135-fints-4-1-protocol-architecture-and-version-negotiation.md), [ADR 0136](0136-fints-4-1-xml-envelope-and-syntax-codec.md), [ADR 0137](0137-fints-4-1-security-profile-and-pin-tan-continuation.md), [docs/roadmap.md](../roadmap.md#m9--fints-41-protocol-expansion), [docs/milestone-9.md](../milestone-9.md#m9-04-fints-41-account-operations--native-iso-20022-integration)

---

## 1. Context and Problem Statement

FinTS 4.1 standardizes core banking business transactions on ISO 20022 XML formats and modernized segment versions:
1. **Account Discovery:** `HKSPA` v3 / `HISPA` v3 replaces legacy account descriptors with structured SEPA identifiers (`<iban>`, `<bic>`, `<subAccount>`, `<currency>`, `<accountType>`).
2. **Account Balances:** `HKSAL` v8 / `HISAL` v8 and `HKCAZ` v2 return structured balance entries with ISO timestamping and unambiguous currency attribution.
3. **Account Statements:** ISO 20022 `camt.052` (intraday report) and `camt.053` (statement) XML documents are natively embedded in FinTS 4.1 response envelopes.
4. **SEPA Credit Transfers:** `HKCCS` v2 / `HKIPB` v2 embeds `pain.001` XML documents and supports Verification of Payee (VoP) pre-authorization checks.

The architectural challenge is guaranteeing that whether data originates from FinTS 3.0 or FinTS 4.1, all entities map into the identical canonical domain types (`AccountSourceLocator`, `BalanceSnapshot`, `SourceObservation`, `PaymentAttempt`) with complete provenance retention, no silent loss of attributes, and zero floating-point arithmetic.

---

## 2. Decision and Architecture

### 2.1 FinTS 4.1 Account Operations Codec (`FinTs41AccountOperations`)

We implement dedicated operation request builders and response parsers:
- `FinTs41AccountDiscovery`:
  - Builds `HKSPA` v3 request segment.
  - Parses `HISPA` v3 response segments into canonical `FinTs41DiscoveredAccount` records (`Iban`, `Bic`, `AccountNumber`, `Currency`, `HolderName`, `AccountType`).
  - Generates lossless `AccountSourceLocator` with connector version `"FinTS-4.1"`.
- `FinTs41BalanceQuery`:
  - Builds `HKSAL` v8 request for a targeted IBAN.
  - Parses `HISAL` v8 response segments into exact decimal `Money` balances (`Booked`, `Available`, `CreditLine`).
  - Enforces clock anomaly and stale balance warnings.
- `FinTs41CamtStatementParser`:
  - Extracts embedded `camt.052`/`camt.053` transaction blocks from `HICAZ` XML segments.
  - Maps entries to immutable `SourceObservation` entities with original binding references, UETR, end-to-end IDs, and booking/value dates.
- `FinTs41SepaTransferBuilder`:
  - Constructs `HKCCS` v2 order segments wrapping ISO 20022 `pain.001.001.09` payment documents.
  - Integrates Verification of Payee (`VoP`) indicator and validation attributes.

### 2.2 Canonical Invariants

1. **Exact Money Arithmetic:** All balances and transaction amounts are parsed using invariant decimal parsing (`decimal.Parse(..., CultureInfo.InvariantCulture)`). Floating-point conversions are forbidden.
2. **Provenance Retention:** Every balance snapshot and transaction observation records `ConnectorVersion = "FinTS-4.1"` and preserves bank-declared timestamps and reference tokens.
3. **Zero Conflation:** Multiple accounts sharing an IBAN with distinct sub-account numbers remain strictly isolated.

---

## 3. Consequences and Verification

### Positive
- Fully native ISO 20022 processing in FinTS 4.1 dialogues.
- Seamless mapping into Broiler Fond's canonical domain models without data loss or schema bifurcation.
- BCL-only implementation using `System.Xml`.

### Negative
- Embedded XML requires nested subtree parsing while maintaining memory limits.

### Verification Plan
- `FinTs41AccountOperationsTests`:
  1. Build `HKSPA` v3 and parse `HISPA` v3 response into canonical `AccountSourceLocator`.
  2. Build `HKSAL` v8 and parse `HISAL` v8 balance snapshot across booked, available, and credit line values.
  3. Parse embedded CAMT statement records into `SourceObservation` with exact decimal amounts and dates.
  4. Build `HKCCS` v2 SEPA transfer segment with pain.001 document and VoP indicator.
