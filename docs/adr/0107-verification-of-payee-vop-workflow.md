# ADR 0107: Verification of Payee (VoP) Domain & FinTS Integration

## Status
Accepted

## Context
Regulation (EU) 2024/886 (Instant Payments Regulation and Verification of Payee mandate) requires payment service providers offering credit transfers in euro to provide a pre-authorization service that checks whether the payee's name matches the name of the payment account holder identified by the IBAN.

As defined in the product specification ([`docs/roadmap.md`](../roadmap.md)):
1. **Pre-Authorization Requirement:** For covered EUR credit transfers, Broiler Fond requests the bank-provided Verification of Payee (VoP) check *before* requesting user authorization or Strong Customer Authentication (SCA).
2. **Consumer Protection & No Opt-Out:** In accordance with Article 5c(6) of Regulation (EU) 2024/886, consumer users cannot opt out of Verification of Payee.
3. **Truthful & Actionable States:** The domain must explicitly represent all bank-supplied outcomes:
   - `PayeeMatch`
   - `PayeeCloseMatch` (including bank-suggested corrected names)
   - `PayeeNoMatch` (mismatch warning)
   - `PayeeCheckUnavailable` (service unavailable, retaining mandatory warnings)
   - `PayeeCheckNotApplicable` (e.g. out-of-scope or unadvertised)
4. **Non-Blocking Warnings with Explicit Acknowledgment:** Under Regulation (EU) 2024/886, a mismatch warning does not automatically prevent payment authorization. However, it must be prominently displayed to the user, and the user must explicitly acknowledge the warning before proceeding to authorization review.
5. **Strict BCL-only Kernel Boundary:** Wire segment serialization (`HKVOP`) and response parsing (`HIVOP` / `HIVOPS`) must be implemented strictly using .NET 10 shared framework APIs.

## Decisions

### 1. VoP Domain Types
- **`VerificationOfPayeeStatus`**:
  - `Pending`: check in progress or awaiting bank response.
  - `Match`: payee name and account holder match.
  - `CloseMatch`: close match; payee bank supplied a suggested/corrected name.
  - `NoMatch`: mismatch; payee name does not match the account holder for this IBAN.
  - `Unavailable`: bank or directory unable to verify; statutory warning retained.
  - `NotApplicable`: operation outside VoP scope or institution does not support VoP.
- **`VerificationOfPayeeResult`**:
  - Immutable record capturing:
    - `Status`: `VerificationOfPayeeStatus`
    - `SuppliedName`: name submitted by user
    - `CorrectedName`: suggested name returned by bank (for close match)
    - `WarningMessage`: bank-reported or standardized warning description
    - `RawBankCode`: raw FinTS or ISO return code (e.g., `MATCH`, `NMTC`, `NO_MATCH`)
    - `CheckedAt`: UTC timestamp
    - `CanProceed`: boolean indicating whether the flow can continue to authorization review (requires user acknowledgment for warnings).

### 2. FinTS 3.0 Wire Codecs (`HKVOP` / `HIVOP`)
- **`FinTsVopRequestWriter`**:
  - Encodes unsigned `HKVOP` (Empfängerüberprüfung) segment:
    - Segment Header: `HKVOP:2:1` (tag, ordinal, version 1)
    - Source Account DE: `[IBAN]:[BIC]`
    - Beneficiary DE: `[Name]:[IBAN]:[BIC]`
    - Amount DE: `[Amount]:EUR`
  - Validates and frames according to FinTS 3.0 byte syntax.
- **`FinTsVopResponseParser`**:
  - Decodes `HIVOP` response segments and extracts result indicators:
    - Indicator `1` / `"MATCH"` -> `VerificationOfPayeeStatus.Match`
    - Indicator `2` / `"CLOSE_MATCH"` -> `VerificationOfPayeeStatus.CloseMatch` with extracted corrected name
    - Indicator `3` / `"NO_MATCH"` -> `VerificationOfPayeeStatus.NoMatch` with mismatch warning
    - Indicator `4` / `"UNAVAILABLE"` -> `VerificationOfPayeeStatus.Unavailable` with warning
  - Correlates bank message codes (e.g., HIRMS 3070, 3950) into structured warnings.

### 3. BPD Parameter & Capability Activation (`HIVOPS`)
- **`FinTsVopAdvertisement`**:
  - Parses untrusted `HIVOPS` parameter segment from BPD.
  - Identifies supported `HKVOP` versions (e.g. version 1).
  - Determines whether an institution advertises live VoP capabilities.
  - When unadvertised, payments transition from `Validated` to `PayeeCheckNotApplicable` with explicit reason recorded.

### 4. Fail-Safe Acknowledgment Workflow
- If status is `Match`: advances directly to `AwaitingAuthorization`.
- If status is `CloseMatch`: user is presented with the suggested name and can choose between keeping their entered name, adopting the suggested name (updating the draft revision), or cancelling.
- If status is `NoMatch` or `Unavailable`: displays high-contrast non-color warning text. Advancing requires the user to check an explicit confirmation box acknowledging the discrepancy/unavailability.

## Consequences

### Positive
- Full regulatory compliance with Regulation (EU) 2024/886.
- Prevents fraud and misdirected transfers by giving users immediate feedback on recipient account validity.
- Preserves user autonomy: warnings inform the user but do not silently abort legitimate transfers.
- Preserves clean BCL-only kernel boundary without external API or cloud lookups.

### Negative / Trade-offs
- Adds an additional round-trip before authorization when banks advertise VoP, slightly increasing submission latency.
