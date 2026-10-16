# ADR 0095: Statement Parameter Schemas and Multi-Format Capability Negotiation

- **Status:** Accepted
- **Date:** 2026-10-16
- **Related:** [ADR 0012](0012-fints-parameter-evidence.md), [ADR 0013](0013-read-capability-evidence.md), [ADR 0094](0094-immutable-transaction-domain-models.md), [docs/roadmap.md](../roadmap.md#m2--transactions-and-account-details), [docs/milestone-2.md](../milestone-2.md)

---

## 1. Context and Problem Statement

In Milestone 2, **Broiler Fond - Finance on Demand** retrieves booked and pending account movements from
FinTS 3.0 institutions. German financial institutions report statement data using two distinct protocol families:
1. **SWIFT MT940 / MT942** flat statement lines, requested via business transaction `HKKAZ` and advertised in
   Bank Parameter Data (BPD) segment `HIKAZS` (versions 5, 6, and 7).
2. **ISO 20022 CAMT XML** messages (`camt.052` for interim/pending reports, `camt.053` for end-of-day booked statements),
   requested via business transaction `HKCAZ` and advertised in BPD segment `HICAZS` (versions 1 and 2).

Institutions vary widely in their advertised capabilities:
- Some support only MT940 (`HIKAZS`), while modern core banking systems prefer or mandate CAMT (`HICAZS`).
- Within `HIKAZS`, version 5 allows only date range queries; version 6 adds all-account and pagination token flags;
  version 7 adds supported format descriptors.
- Within `HICAZS`, versions 1 and 2 define time range, all-account, and pagination token options, accompanied by
  up to 99 supported CAMT schema URNs (e.g. `camt.052.001.02`, `camt.053.001.02`, `camt.053.001.08`).
- User Parameter Data (`HIUPD`) explicitly specifies whether `HKKAZ` or `HKCAZ` is permitted for an individual account,
  along with the required signature authorization level.

Without deterministic, evidence-based capability negotiation:
1. Requesting `HKCAZ` from an MT940-only bank would result in dialogue abort errors (`9010` / `9210`).
2. Hardcoding MT940 would discard richer structured SEPA remittance information, End-to-End IDs, and return reasons
   provided by CAMT.
3. Guessing pagination tokens or all-account support could send malformed segment layouts to strict bank servers.

---

## 2. Decision and Architecture

We implement a BCL-only statement capability negotiation engine under `Broiler.Fond.Kernel.FinTs`:
[`FinTsStatementCapabilities.cs`](file:///d:/Broiler.Fond/src/Broiler.Fond.Kernel/FinTs/FinTsStatementCapabilities.cs).

### 2.1 Statement Protocol Families (`FinTsStatementOperation`)

```csharp
public enum FinTsStatementOperation
{
    Mt940 = 1,
    Camt = 2,
}
```

### 2.2 Untrusted Advertisement Parsing (`FinTsStatementAdvertisement`)

- Encapsulates parsed `HIKAZS` and `HICAZS` segments.
- Extracts `MaximumOrders`, `MinimumSignatures`, and `SecurityClass`.
- Extracts version-specific query capability flags:
  - `TimeRangeAllowed` (`ZeitraumEingabeErl`, boolean).
  - `AllAccountsAllowed` (`AlleKontenErl`, optional boolean; null in `HIKAZS` v5).
  - `PaginationTokenAllowed` (`AufsetzpunktErl`, optional boolean; null in `HIKAZS` v5).
  - `SupportedFormats` (read-only list of unescaped format descriptors/URNs, capped at 99 entries).

### 2.3 Bounded Statement Parameter Set (`FinTsStatementParameterSet`)

- Wraps [`FinTsParameterSet`](file:///d:/Broiler.Fond/src/Broiler.Fond.Kernel/FinTs/FinTsParameters.cs).
- Enforces defensive resource bounds:
  - Maximum 128 statement advertisements per parameter set (`MaximumAdvertisements = 128`).
  - Maximum 99 format descriptors per segment (`MaximumCamtFormats = 99`).
- Schema validation enforces exact element counts per segment version:
  - `HIKAZS` v5: exactly 1 option element.
  - `HIKAZS` v6: exactly 3 option elements.
  - `HIKAZS` v7: 3 to 102 elements (options + format descriptors).
  - `HICAZS` v1 and v2: 3 to 102 elements (options + format URNs).
- Unknown segment versions (e.g. future `HIKAZS:8` or `HICAZS:3`) and uninterpreted segments are safely preserved
  in `UninterpretedSegments` without failing closed or guessing.

### 2.4 Diagnostic Capability Evidence (`FinTsStatementCapabilityEvidence`)

Evaluates a specific account against bank parameters, user parameters, and advertised operation/version.
Produces a bitmask of diagnostic issues (`FinTsStatementEvidenceIssue`):
- `MissingBankParameters`, `Protocol300NotAdvertised`, `MissingUserParameters`, `MissingAccountConnection`.
- `PermissionUnknown`, `PermissionBlocked`, `PermissionAmbiguous` (evaluated via `HIUPD` permission entries).
- `MissingAdvertisement`, `UnsupportedVersion`, `DuplicateAdvertisement`.
- `SignatureConflict` (when account signature level is less than bank advertised minimum).
- `ZeroOrderCapacity` (when bank advertises maximum orders = 0).
- `MissingInternationalIdentity` (CAMT strictly requires an account IBAN).
- `UnsupportedCamtFormat` (when a requested format like `camt.053.001.02` is not advertised, supporting both exact and URN-suffix matches).
- `InstitutionContextMismatch`, `DuplicateAccountIdentity`, `ResponseNeedsReview`.

Evidence matches cleanly (`HasMatchingEvidence == true`) only when `Issues == FinTsStatementEvidenceIssue.None`.

### 2.5 Multi-Format Preference Negotiation (`FinTsStatementPreferenceNegotiator`)

Implements deterministic format preference per account:
1. Evaluates CAMT candidates in descending version order (`v2`, `v1`) against the requested/default format (`camt.053.001.02`).
2. Evaluates MT940 candidates in descending version order (`v7`, `v6`, `v5`).
3. **Selection rule:**
   - If CAMT matches cleanly, it is selected as `SelectedEvidence`. If MT940 also matches, it is recorded as `AlternateEvidence`.
   - If CAMT does not match but MT940 matches cleanly, MT940 is selected as `SelectedEvidence` with no alternate.
   - If neither matches, returns the best candidate with diagnostic issues for transparent error reporting.
   - No guessing or silent unverified fallback is permitted.

---

## 3. Invariants and Verification

- **Zero NuGet dependencies:** Pure .NET 10 BCL implementation adhering strictly to the kernel boundary.
- **Fail-closed:** Malformed parameter segments, signature conflicts, or missing account permissions fail safely.
- **Privacy safety:** Default diagnostics and exception messages never leak private bank identifiers or account numbers.
- **Verified by test suite:** 78 automated verification checks in [`StatementCapabilitiesTests.cs`](file:///d:/Broiler.Fond/tests/Broiler.Fond.Kernel.Tests/StatementCapabilitiesTests.cs) covering all versions, boundary conditions, diagnostic scenarios, and format preference negotiations.
