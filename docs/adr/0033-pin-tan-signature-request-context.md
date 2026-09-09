# ADR 0033: PIN/TAN signature-header request context

- **Status:** Accepted (pure synthetic comparison only)
- **Date:** 2026-09-08
- **Related:** [Signature-header schemas](0032-pin-tan-signature-header-schemas.md),
  [initialization scope](0026-initialization-response-scope.md),
  [permitted procedures](0016-permitted-procedures-and-challenge-context.md)

## Decision and source basis

`FinTsPinTanSignatureEvidence` compares a detached HNSHK-4 header with one
explicit unsigned initialization or synchronization request expectation and an
explicitly sourced procedure context. All objects remain caller-owned evidence.
No signed frame is constructed or authenticated, and no matching result grants
permission to send or apply a recovered identifier.

The rules use the [official FinTS specification](https://www.fints.org/de/spezifikation),
PIN/TAN 2020-07-10 B.8.2 and B.9 (printed pages 77–78), and HBCI 2024-06-11
B.5.1. Document hashes are recorded in ADR 0032. PIN/TAN binds the chosen
two-step function to bank-advertised and user-reported procedures; the header
contains the user identifier and system identifier. Its customer identifier is
not a substitute for the user identifier.

## Explicit request expectation

`FinTsPinTanSignatureSelection` validates a caller-selected profile/function
pair (PIN:1/999 or PIN:2/900–997) and an explicit HITANS version 6 or 7. It does
not select a version automatically or imply permission.

`FinTsPinTanSignatureRequestContext` retains either the exact unsigned
initialization or synchronization request, the selection, an explicit expected
user ID and expected control reference. Malformed caller text or selection
fails with fixed diagnostics. Country, institution and system ID are derived
from that request's HKIDN, rather than supplied as unrelated expectations.

Matching header evidence requires:

- Exact country/institution, expected user, system ID, control reference and
  profile/function equality, with no trimming or case normalization.
- An identified request with required system status. Anonymous requests and
  PIN/TAN signature-reference synchronization require review.
- An assigned-looking system identifier; `0` is allowed only for an explicit
  system-ID synchronization request. `unbekannt` always requires review.
- The local single-header layout: candidate segment number 2, supplier role 1
  and security party 1. Other schema-valid positions/roles require review.

The candidate header is detached from the unsigned frame. Number 2 describes
the intended position after a future frame transformation, not proof of inclusion
in the original unsigned frame. No existing segments are renumbered. Read, close,
multi-signature, signature-trailer and authenticated envelope integration remain
outside this comparison.

Signature reference numbers, timestamps, key fillers and algorithm fillers remain
observations. They are not used as cryptographic, freshness or replay evidence.
Control-reference equality is only a caller expectation; no HNSHA link is proved.
System-ID equality does not prove bank assignment, and message-number recovery
still needs the separate recovery-context checks in ADR 0029.

## Procedure origin and scoped status

`FinTsPinTanProcedureContext` retains an unsigned initialization that produced
the observations, its parsed TAN parameter set and a parsed 3920 permission set.
Its constructor establishes no provenance. Comparison requires the parameter
and permission sets to share the exact same parsed response instance; even
separately reparsed identical bytes are not silently joined.

The origin request must be identified and match the target request's country,
institution and customer ID. Initialization binding then checks response counters,
assigned dialogue and references, returned BPD/UPD bank/user identity, advertised
protocol/language and any returned account identities. Missing bank or user
parameters require review; no implicit cached-data reuse is added.

ADR 0026's generic unknown-parameter and status results are deliberately refined
locally for HITANS and 3920. Every other initialization issue is retained as a
procedure-scope issue. The generic initialization comparator is unchanged.
Any parameter segment left uninterpreted by the TAN parser, including future
HITANS versions, still requires review.

The local status vocabulary permits HIRMG 0010/0020, HIRMS 0020 and HIRMS 3920
referencing the origin's preparation segment. Execution must cover the message
or both identification and preparation. Duplicate codes, element references,
nonempty parameters outside 3920, errors, conflicting/indeterminate status,
other codes and explicitly parsed PIN/TAN response envelopes require review.
An aborted origin can retain useful 3920 observations but cannot yield a match.

These are consistency checks on supplied source objects. They do not authenticate
the bank, user or prior request, establish freshness, prove parameter completeness
beyond the required fields, or activate a cache across dialogues. The target
request and origin may be different initialization attempts; the caller remains
responsible for their actual provenance and lifecycle.

## Procedure requirements and unresolved evidence

Exactly one advertisement of the explicitly selected HITANS version is required.
Duplicate advertisement versions, duplicate functions in the selected
advertisement and unknown parameter observations require review. The local
single-header subset also requires a positive maximum order count and a minimum
signature count no greater than one.

For PIN:2, exactly one advertised procedure must have the selected function.
For PIN:1, the selected advertisement must report one-step allowed; no two-step
procedure object is invented. Both profiles require exactly one unambiguous
3920 report listing the selected function, scoped to preparation. Missing,
duplicate or unlisted reports remain distinct issues.

Only an issue-free comparison exposes `MatchingAdvertisement` and, for PIN:2,
`MatchingProcedure`. The full input contexts remain available on every result;
ambiguity never selects a winner. Procedure-specific HKTAN process, media,
challenge, operation permission and SCA checks are separate work.

The initial 999 procedure-discovery exception described by the specification
does not bypass these checks. With no explicit procedure context this comparator
returns MissingProcedureContext; it initiates no probe or automatic fallback.

## Bounds, verification and next work

Existing bounded parameter and permission parsers apply. Comparison observes
cancellation during initialization and reply traversal and before return. It is
pure and repeatable, with no response consumption or replay state. Default
diagnostics exclude identifiers; retained sources remain private and untrusted.

Eighteen independent Python fixtures cover both profiles, system-ID synchronization,
missing procedure context, identity/system/control/selection mismatches, missing
or duplicate permission reports, one-step restrictions, duplicate/future
advertisements and foreign or aborted origins. The executable suite adds 139
checks, including exact ownership, source-instance separation, missing BPD/UPD,
role and version restrictions, advertisement limits, malformed caller input,
cancellation and culture independence.

Next is typed credential-free PIN/TAN signature-header encoding. Credentials,
HNSHA, complete request security, authenticated sessions, transport and durable
integration remain pending. The host stays inert.
