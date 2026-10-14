# ADR 0059: Balance-continuation PIN/TAN signature context

- **Status:** Accepted (local synthetic context only)
- **Date:** 2026-09-10
- **Related:** [First-read signature context](0049-first-read-pin-tan-signature-context.md),
  [balance-continuation context](0058-assembled-balance-continuation-context.md)

## Scope and comparison

`FinTsPinTanReadContinuationSignatureContext.Evaluate` binds a detached HNSHK-4
header, explicit procedure selection and explicit control reference to a proposed
second balance page. It uses the existing PIN/TAN header/context rules recorded
in ADRs 0032, 0033 and 0049, whose reviewed sources are available through the
[official specification](https://www.fints.org/de/spezifikation). No new protocol
version or security procedure is introduced.

The complete ADR 0058 continuation must qualify first: prior partial evidence,
exact token, unchanged operation/version/account/options and dialogue, sequential
client/bank counters and inherited capability requirements. Any failure is
preserved in that exact nested context and adds ContinuationNeedsReview here.
Matching inner signature fields cannot bypass an unresolved continuation.

Header country, institution and user must equal the original first-read signature
identity. The system identifier must also match and cannot be 0 or `unbekannt`.
The explicit selection's profile, security function and TAN segment version must
equal the original selection, and the header must match the selected profile and
function. Comparison uses selection values, not object identity; it never replaces
the inherited returned procedure evidence with a newly selected or reparsed source.

The header must occupy segment 2 with supplier role 1 and security party 1. Its
control reference must equal the caller's explicit nonzero reference, validated
with the existing strict fourteen-character text rules. A mismatched reference
or header role remains a distinct review issue. This comparison creates no HNSHA
link and introduces no control-reference uniqueness or replay rule.

Schema-valid timestamps, security reference numbers, key numbers/versions and
algorithm fillers remain exact observations. The context neither increments a
security reference nor interprets a timestamp as freshness or a filler as an
algorithm selection. Reusing a prior control reference can match this pure context;
that establishes no submission or replay safety.

## Qualified results and ownership

The result retains the exact continuation, header and selection objects and the
explicit control string. Request is the original proposed continuation request;
no segment is inserted, renumbered or rewritten. Any issue withholds MatchingOrigin,
MatchingAdvertisement, MatchingProcedure and MatchingRequirements and returns
Unknown for OperationEvidence. Raw nested inputs remain inspectable and private.

Only a complete match exposes the exact original returned TAN advertisement,
two-step procedure where applicable, HIPINS requirements and account/capability
context. One-step matching invents no two-step procedure. Unknown PIN bounds
remain nullable; the reported operation N flag grants no TAN/SCA waiver. This
context validates no credential bytes or completeness requirements.

Comparison consumes no partial result, token or request and reopens no completed
attempt. It accepts neither credential owners nor secret output, encodes no header
or message and performs no transport, scheduling, authentication or SCA completion.
Existing first-read signature and writer APIs retain their original restrictions.
Independent continuation encoding and credential cleanup remain separate work.

Work is bounded by existing parser/context limits. Cancellation is checked before
comparison and before publication; malformed controls and null inputs use fixed
diagnostics without source values. Repeated/concurrent comparisons retain immutable
provenance and are not a replay guard. No clock or timestamp allocation is performed.

## Verification and next work

Sixty-nine independent Python fixtures and 531 checks cover every existing
continuation-context variant, both profiles, procedure versions, identity/system
changes, selection/header disagreements, explicit controls and header roles.
Positive cases preserve escaped/maximal controls, missing/date-only timestamps,
zero/maximal security references, key/filler observations, nullable PIN bounds
and reported N flags. Any review issue withholds every qualified object.

Tests retain exact source identity and unchanged request/response bytes, integrate
the actual one-time partial handoff, and verify that the completed first-read owner
remains terminal. Additional checks cover equivalent selection objects, invalid
controls, null inputs, cancellation, cultures and concurrent evaluation. All input
values are public synthetic data.

Next is continuation PIN-only trailer encoding with actual owned-PIN requirement
validation and bounded secret cleanup. Whole-message/envelope assembly, later-page
responses and ownership, TAN/challenge integration, authenticated transport and
live qualification remain pending. The host stays inert.
