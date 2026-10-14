# ADR 0055: Assembled first-read response reference binding

- **Status:** Accepted (local structural comparison only)
- **Date:** 2026-09-10
- **Related:** [Assembled reference binding](0039-pin-tan-assembled-request-reference-binding.md),
  [unsigned read context](0019-read-request-response-context.md),
  [first-read envelopes](0054-first-read-pin-only-request-envelope.md)

## Scope and source

The [official FinTS specification](https://www.fints.org/de/spezifikation),
Formals 2017-10-06 B.7.3 and B.5.2, supplies the segment-reference and message
context rules already recorded in ADR 0039. The supported read-data vocabulary
is unchanged from ADR 0018: HISPA-1 and HISAL-6/7/8. No new status interpretation
or protocol version is introduced.

The assembled read candidate places HNSHK at 2 and HKSPA/HKSAL at 3. The existing
unsigned comparator expects the read at 2, so applying it directly would mistake
signature replies for read replies or reject correctly numbered read data.

`FinTsPinTanReadResponseBinding.Evaluate` compares one immutable first-read
candidate with one parsed response. It retains those exact instances and returns
`HasMatchingReferences` only when its message, profile and reference issues are
empty. This establishes a structural relation, not execution or account evidence.

## Comparison and source retention

An explicitly parsed PIN/TAN response envelope is required, with the candidate's
profile version. The bank message number must equal the candidate's expected
bank number; the outer dialogue and referenced dialogue must both equal its
assigned dialogue. The referenced client message must equal the candidate's
message number. The qualified first-read context fixes both counters at 2.
No initial-dialogue assignment or zero placeholder substitution is performed.

Each body segment except HIRMG produces an immutable reference observation.
The observation retains its original segment and the exact target descriptor
from the candidate map. Missing and unknown references leave the target null and
produce distinct flags. Comparison never rewrites or renumbers a response.

HIRMS may target any segment actually present in the candidate, including HNHBK,
HNVSK, HNVSD, HNSHK, HNSHA and HNHBS. A reference to 2 stays attached to the
signature header regardless of its reply code. A scoped error, pending reply,
unavailable report or message abort can have matching references while preserving
its original status observations. None becomes read execution at this layer.

HISPA/HISAL data must name the candidate's read segment 3 and correspond to its
operation code. A balance report on a discovery candidate, or a report naming a
signature/framing segment, produces SegmentRoleMismatch. The narrow vocabulary
recognizes HISPA-1 and HISAL-6/7/8; other codes and future versions carry
UninterpretedData without fallback. These are reference-vocabulary checks, not
business-schema parsing. Exact request/response version equality remains for the
semantic comparator, so a known HISAL-8 response to HKSAL-7 retains matching
references and its version discrepancy for later review.

All common issue flags and source-preserving link types are reused unchanged.
Existing initialization, synchronization, closing and unsigned read comparators
retain their behavior.

## Ownership and limits

Evaluation accepts no credentials, assembled secret bytes, clock or transport.
Candidate creation and reference matching can precede encoding; even a candidate
with missing PIN bounds can have matching response references. The API therefore
proves neither encoding, submission, credential completeness nor authentication.

The candidate has seven immutable targets and existing parser/envelope budgets
bound response work. Evaluation checks cancellation before work, per body segment
and before publication. Null arguments fail without source values in diagnostics.
Repeated and concurrent evaluations are pure; reparsed bytes form independent
source objects. There is no response consumption, replay protection or lifecycle
state. Retained account, envelope and response data remain private and untrusted.

## Verification and next work

Forty-six independent Python fixtures and 481 checks cover supported read/profile
variants, exact and escaped dialogues, counter/reference mismatches, absent
wrappers, wrong profiles, old unsigned references, all seven target roles,
missing/unknown references, wrong operations, unknown/future data and combined
issues. Tests preserve errors, duplicate data, foreign account/envelope identities,
known version differences and malformed payloads for later semantic validation.

Integration tests compare every target with actual synthetic envelope output for
eight qualified contexts, then repeat binding after PIN disposal and complete
output erasure. Source identity and unchanged bytes, immutable collections,
cancellation, fixed null diagnostics, culture independence and concurrent
comparison are verified. Parsing credential-bearing output is test-only and uses
public synthetic values.

Next is assembled first-read response semantics: exact-source read schemas,
account/version/currency checks, envelope identity and scoped status interpretation
against these actual segment roles. Missing/duplicate data, unavailable and partial
reports must remain explicit. TAN/challenge integration, attempt ownership,
continuation assembly, session coordination, secure input, authenticated transport
and live qualification remain pending. The host stays inert.
