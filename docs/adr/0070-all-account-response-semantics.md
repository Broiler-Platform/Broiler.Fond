# ADR 0070: All-account response reference binding and semantics

- **Status:** Accepted (local structural comparison only)
- **Date:** 2026-10-14
- **Related:** [All-account request envelopes](0069-all-account-request-envelope.md),
  [Assembled all-account capability context](0068-assembled-all-account-capability-context.md),
  [Assembled first-read response reference binding](0055-first-read-response-reference-binding.md),
  [Assembled first-read response semantics](0056-assembled-first-read-response-semantics.md),
  [All-account discovery evidence](0022-all-account-discovery-evidence.md)

## Scope and reference binding

This architectural decision record defines the reference binding and semantic evaluation of
bank responses to assembled all-account discovery requests (`HKSPA:3:1++J'`) under PIN/TAN.

`FinTsPinTanAllAccountResponseBinding.Evaluate` compares one immutable
`FinTsPinTanAllAccountRequestBinding` candidate against one parsed `FinTsResponse`. It establishes
structural correlation between the candidate message definition and the response framing:
- An outer PIN/TAN response envelope is required, matching the candidate's profile version (1 or 2);
  absence or mismatch flags `MissingEnvelope` or `ProfileMismatch`.
- The bank message number must equal expected bank number (2), outer dialogue ID and referenced
  dialogue ID must both match the candidate's assigned dialogue, and the referenced client message
  number must equal the candidate's message number (2); discrepancies flag `MessageMismatch`.
- Each non-`HIRMG` body segment produces an immutable `FinTsPinTanResponseSegmentBinding` mapping
  the segment to the candidate segment map (`HNHBK:1:3`, `HNVSK:998:3`, `HNVSD:999:1`, `HNSHK:2:4`,
  `HKSPA:3:1`, `HNSHA:4:2`, `HNHBS:5:1`). Missing or unknown segment references flag
  `MissingSegmentReference` or `UnknownSegmentReference`.
- Body segments other than `HIRMS` are checked against supported read data: only `HISPA:1` is accepted
  for all-account discovery. Balance data (`HISAL`), uninterpreted data, or segments referencing
  non-read positions flag `UninterpretedData` or `SegmentRoleMismatch`.

`HasMatchingReferences` requires all issue flags to be clean (`FinTsPinTanResponseBindingIssue.None`).

## Response semantics and account matching

`FinTsPinTanAllAccountEvidence.Evaluate` compares the evaluated binding against caller-provided
`FinTsReadDataSet` evidence. It enforces strict source object provenance, envelope identity
verification, status scoping, and user account matching:

1. **Read-source provenance:** The `FinTsReadDataSet.Source` must be the exact `FinTsResponse`
   instance retained by the binding (`ReferenceEquals`). Byte-identical reparsed instances are
   rejected with `ReadSourceMismatch` before any further interpretation.
2. **Envelope identity comparison:** The response encryption envelope's security header must match
   the signature header's expected system ID, country code, institution ID, and user ID. Any
   discrepancy flags `EnvelopeIdentityMismatch`.
3. **Status scoping and reply validation:** Read status is evaluated against read segment 3 via
   shared `FinTsReadContextEvidence.StatusIssues`. Only segment-3 replies supply read execution
   or unavailability evidence (3010). Replies targeting other segments or framing roles trigger
   `StatusNeedsReview`. Unexpected balance reports, uninterpreted segments, duplicate discovery
   reports, or version mismatches flag `UnexpectedReport`, `DuplicateReport`, or `VersionMismatch`.
4. **Account matching:** Account matching reuses the core logic of
   `FinTsAllAccountDiscoveryEvidence.MatchAccounts`. Returned accounts are matched against bound
   parameters and advertisements. Duplicate returned identities, institution mismatches, or
   identity conflicts flag `AccountNeedsReview`. If any UPD accounts remain unmatched when discovery
   was not unavailable (3010), `UnmatchedUserAccounts` is flagged.

Any issue sets `Outcome` to `FinTsReadOutcomeObservation.NeedsReview` and suppresses `MatchingReadData`
(returning `null`). Caller-supplied raw evidence remains accessible on `ReadData` for inspection
without conferring execution validity.

## Ownership and limits

The comparators are pure and deterministic:
- They accept no credentials, access no clocks or networks, and perform no transport.
- They consume no response instances and provide no session activation, account persistence, or
  replay protection.
- All diagnostics are strictly scalar flags and counts; no IBANs, credentials, or raw message bytes
  are exposed through diagnostics.
- Cancellation tokens are checked before work, during segment and account loops, and before result
  publication.

## Verification

`FinTsPinTanAllAccountResponseTests` exercises fifty-two independent Python-generated fixtures
and 843 checks in `pin-tan-all-account-response-v1.json`. Verification covers:
- Reference binding and semantics across profiles 1 and 2, TAN versions 6 and 7
- Clean single-account, multiple-account, and unavailable (3010) discoveries
- Empty UPD accounts, unlisted operations, and `SingleAccountRequestAllowed = N`
- Dialogue, message number, and profile mismatches
- Missing and unknown segment references, segment role mismatches (`HISAL` on discovery candidate)
- Read-source provenance enforcement rejecting byte-identical reparsed response instances
- Envelope identity mismatches across system ID, country code, institution ID, and user ID
- Security execution segregation (3920 on signature header vs read segment 3)
- Account semantics: unmatched UPD accounts, duplicate returned identities, institution mismatches,
  identity conflicts, and version mismatches
- Argument validation, cancellation handling, parallel execution, and culture invariance
  (`de-DE`, `tr-TR`, `en-US`, `ar-SA`)
- Reflection checks confirming absence of sensitive fields or credential leaks.
