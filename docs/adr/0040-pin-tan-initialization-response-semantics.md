# ADR 0040: Assembled PIN/TAN initialization response semantics

- **Status:** Accepted (local synthetic comparison only)
- **Date:** 2026-09-09
- **Related:** [Unsigned initialization scope](0026-initialization-response-scope.md),
  [request envelopes](0038-pin-tan-request-envelope-assembly.md),
  [assembled reference binding](0039-pin-tan-assembled-request-reference-binding.md)

## Source and scope

The initialization status and bank/user/customer rules reviewed in ADR 0026
continue to apply: Formals 2017-10-06 B.7, C.3.1.3–C.3.2, C.5.1 and E.2–E.3
from the [official FinTS specification](https://www.fints.org/de/spezifikation).
PIN/TAN envelope metadata follows the sources in ADRs 0014 and 0038. This
increment applies those comparisons to the actual signed segment roles, without
rewriting a response or relaxing the unsigned comparator's wrapper restrictions.

`FinTsPinTanInitializationEvidence.Evaluate` accepts an assembled response binding
and an existing parameter set. It returns the exact supplied binding and parameters,
top-level issues and detailed parameter issues. `ExecutionReported` remains an
untrusted report; it does not authenticate a bank, activate a session or authorize
an operation. Every unresolved issue yields `NeedsReview`.

## Provenance and envelope checks

The candidate must be initialization, not synchronization. The parameter set's
source must be the exact bound response instance. Even byte-identical parameters
parsed from another response instance are rejected. Kind/source failures retain
the supplied objects for inspection but skip semantic and version comparison;
version observations are null and detailed parameter issues have not been evaluated.
Parsing another parameter set from the same response instance is permitted.

All reference-binding issues remain visible through the original binding and
produce `BindingNeedsReview`. The existing binder checks the envelope profile,
assigned dialogue, counters and actual outgoing segment roles.

For the restricted identified initialization, the response envelope's reported
system, country, institution and user must exactly match the candidate's bound
identities. Differences produce `EnvelopeIdentityMismatch`. This is a conservative
local identity comparison, not proof of authenticity or general bank compatibility.
Schema-valid role, timestamp and filler fields remain untrusted observations;
timestamp freshness and cryptographic identity verification are not inferred.

## Status interpretation by bound role

The local vocabulary accepts HIRMG 0010/0020 and HIRMS 0020. Matching evidence
requires message-level 0020 or both identification and preparation execution
reports. HNSHK success is a signature observation and cannot substitute for
HKIDN execution; the comparison uses the immutable bound role rather than old
unsigned segment numbers. Receipt or a pending code alone cannot establish
execution. Binding a reply to a framing/security segment does not turn an error
into success.

Response errors, conflicting classes and indeterminate processing require review.
Unknown codes, duplicate codes within a reply segment, element references and
nonempty reply parameters also require review. Empty optional trailing parameters
remain omissions. Bank text is never used to determine meaning.

HKVVB-scoped 3050 is supported when BPD or UPD is actually returned, while still
requiring separate execution evidence. Its text does not choose which parameter
set changed. Generic reply decoding keeps 3050 uninterpreted. Procedure reports
such as 3920 and SCA/challenge continuation remain outside this vocabulary and
explicitly require later integration rather than being silently accepted.

## Parameter scope and version observations

Signed and unsigned initialization share one internal parameter-comparison helper.
The extraction preserves the unsigned public API and behavior. Returned BPD must
match country/institution, advertise protocol 300 and include the explicitly
selected nonstandard language. UPD user identity must match the pinned expected
user, which remains distinct from the customer identifier. Every account customer
must match HKIDN; account-bound entries must also match its country/institution.

Missing bank/user headers remain explicit issues even if the request named cached
versions. Unknown parameter segments are retained with `UninterpretedParameters`.
TAN/PIN parameter families require their own later integration; reference binding
alone does not establish their semantic validity. Malformed or duplicate BPD/UPD
headers continue to fail in the existing parameter parser before comparison.

Bank/user version-change properties are nullable observations: omitted headers
stay unknown, equality stays unchanged and any difference is reported as a change.
No monotonic freshness or cache-reuse rule is inferred. UPD version zero remains
dialogue-scoped. Version observations on review outcomes do not authorize use of
foreign or otherwise unresolved data.

## Ownership, verification and next work

This API takes no credentials or assembled request buffers, retains original
source observations, and performs no cache updates, account ingestion, response
consumption, transport or clock work. Existing parser bounds apply; reply/account
loops observe cancellation. Repeated and concurrent comparison is intentionally
pure and provides no replay barrier. No live banking path is enabled.

Thirty independent Python fixtures and 199 checks cover both profiles, scoped and
message execution, foreign identities, missing parameters, old references, 3050,
UPD zero, pending/error/duplicate statuses, nonempty reply metadata, unsupported
procedure reports, protocol/language mismatch and envelope identity/profile checks.
Additional checks reject source mixing and synchronization candidates, preserve
provenance, verify null/cancellation behavior and exercise concurrent evaluation.
The complete unsigned comparison suite continues to pass after helper extraction.

Next is assembled PIN/TAN synchronization response semantics, preserving the
close-and-reinitialize requirement. Procedure/challenge integration, attempt
ownership, secure input, authenticated transport and live acceptance remain pending.
