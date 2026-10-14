# ADR 0068: Assembled all-account capability context

- **Status:** Accepted (local synthetic evidence only)
- **Date:** 2026-10-14
- **Related:** [Bounded all-account discovery evidence](0022-all-account-discovery-evidence.md),
  [Initialization read-capability integration](0050-initialization-read-capability-integration.md),
  [First-read PIN/TAN signature context](0049-first-read-pin-tan-signature-context.md),
  [First-read credential requirement comparison](0051-first-read-credential-requirement-comparison.md)

## Context and scope

ADR 0050 (`FinTsPinTanReadCapabilityContext`) evaluated single-account read capabilities under PIN/TAN,
strictly requiring exactly one selected target account (`Accounts.Count == 1`). Under FinTS 3.0,
all-account SEPA account discovery (`HKSPA:3:1++J'`) omits selected account specifications (`AllAccounts = true`,
`Accounts.Count == 0`) so the bank returns all eligible customer accounts.

`FinTsPinTanAllAccountCapabilityContext` establishes the capability, permission, and parameter
evaluator for all-account discovery requests within an authenticated PIN/TAN session. It compares one
`FinTsPinTanReadSignatureContext`, its bound `FinTsReadParameterSet`, and user/bank parameter evidence,
producing structured diagnostic flags (`FinTsPinTanAllAccountCapabilityIssue`) without sending messages,
updating stored account inventories, allocating identities, or validating credentials.

## Evaluation invariants and rules

The comparator verifies all-account readiness against the following rules:

1. **Signature Context and Request Role:**
   The attached `FinTsPinTanReadSignatureContext` must have clean matching evidence. The embedded request
   must specify `Code == "HKSPA"`, `Version == 1`, `AllAccounts == true`, `Accounts.Count == 0`, and
   `ContinuationToken == null`. Any deviation flags `RequestNeedsReview`. Unresolved signatures flag
   `SignatureNeedsReview`.

2. **Integrated Read Parameters:**
   The initialization evidence must retain an explicitly integrated `FinTsReadParameterSet` sharing the
   exact source tree of the initialization parameters. An absent parameter set flags `MissingReadParameters`
   and halts further parameter inspection.

3. **Bank and User Parameter Integrity:**
   Bank parameters (`HIBPA:3`) must be present and advertise FinTS 300 (`ProtocolVersions.Contains(300)`).
   User parameters (`HIUPA:4`) must be present. The source response must have no errors, no conflicting reply
   classes, and match the dialogue identifier and message counter sequence. Account connections in UPD must
   match the institution identity in bank parameters. Violations flag `ParametersNeedReview`.

4. **HISPAS-1 Advertisement:**
   The parameter set must contain exactly one `HISPAS` version 1 advertisement with nonzero order capacity
   (`MaximumOrders > 0`). An absent advertisement flags `MissingAdvertisement`. Multiple advertisements or
   zero order capacity flag `AdvertisementNeedsReview`. In the local single-header PIN/TAN flow, the customer
   signature requirement must be exactly one (`MinimumSignatures == 1`); any other minimum flags
   `SignatureRequirementsNeedReview`. As in ADR 0022, `SingleAccountRequestAllowed` may be `J` or `N`; an `N`
   restricts selected-account requests but does not bar all-account discovery.

5. **User Policy and Permissions:**
   If all accounts in UPD report HKSPA as unlisted blocked under the user's `FinTsUnlistedOperationPolicy`,
   or if UPD contains zero accounts and the user's unlisted policy is `Blocked` (`0`), the comparator flags
   `OperationBlocked`. Ambiguous permissions flag `ParametersNeedReview`.

6. **Limits:**
   If any account in UPD specifies an account limit or an operation limit on `HKSPA`, the evaluator flags
   `LimitsNeedReview` to ensure limit policies are reviewed before request assembly.

7. **Qualification:**
   Only a result with `Issues == FinTsPinTanAllAccountCapabilityIssue.None` exposes `MatchingAdvertisement`
   and the qualified reported HIPINS TAN requirement (`OperationEvidence`). Unresolved results return
   `null` for `MatchingAdvertisement` and `FinTsPinTanOperationEvidence.Unknown`.

## Credential comparison integration

`FinTsPinTanReadCredentialComparison.Compare` provides an overload accepting `FinTsPinTanAllAccountCapabilityContext`.
It shares core credential inspection logic (`CompareCore`), verifying caller-owned PIN and optional TAN
byte spans against the inherited HIPINS length bounds and active two-step procedure specifications without
retaining, logging, or copying credential bytes.

## Verification

`FinTsPinTanAllAccountCapabilityContextTests` exercises twenty-seven independent Python-generated vectors
and 195 checks in `pin-tan-all-account-capability-context-v1.json`. Verification covers:
- Valid all-account discovery under one-step and two-step TAN profiles
- Permitted all-account queries when single-account requests are not advertised (`SingleAccountRequestAllowed = N`)
- Empty UPD account lists with permitted unlisted operations
- Signature, control reference, dialogue, and counter mismatches
- Missing, duplicate, zero-order, and version-2 `HISPAS` advertisements
- Unlisted blocked operation policies (empty and populated account sets)
- Account and permission limits requiring review
- Selected-account and foreign-operation (`HKSAL`) request rejections
- Credential comparison integration with valid, short, long, and invalid PIN bytes
- Provenance preservation, argument validation, and cancellation handling
- Reflection checks confirming absence of credential leaks or sensitive fields.
