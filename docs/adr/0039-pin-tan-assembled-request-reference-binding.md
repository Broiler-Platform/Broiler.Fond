# ADR 0039: Assembled PIN/TAN request context and response-reference binding

- **Status:** Accepted (local structural comparison only)
- **Date:** 2026-09-09
- **Related:** [Initialization scope](0026-initialization-response-scope.md),
  [synchronization scope](0029-synchronization-response-context.md),
  [signature context](0033-pin-tan-signature-request-context.md),
  [request envelopes](0038-pin-tan-request-envelope-assembly.md)

## Source and scope

The [official FinTS specification](https://www.fints.org/de/spezifikation),
Formals 2017-10-06 B.7.3 (printed page 25), requires HIRMS references to identify
the corresponding customer segment. Its B.5.2 message header and the PIN/TAN
initialization example cited in ADR 0038 provide the outer and inner numbering
context. The Formals download hash is recorded in ADR 0037.

Inserting HNSHK changes the business segment numbers used by existing unsigned
comparators. A reference to 2 now identifies HNSHK; HKIDN is 3, HKVVB is 4,
and synchronization's HKSYN is 5. Applying unsigned comparison directly would
misattribute replies or reject correctly numbered parameter/report data.

This increment adds immutable candidate metadata and pure message/profile/
reference comparison. It does not interpret execution status, validate returned
parameter identities, authenticate envelope identity or accept an active session.
The public boolean is deliberately named `HasMatchingReferences`.

## Candidate metadata

`FinTsPinTanRequestBinding.ForEnvelopeCandidate` requires matching signature
evidence from ADR 0033. It retains that exact evidence and exposes a read-only
map of code, number, version and role for the restricted envelope writer.
The list includes HNHBK 1, HNVSK 998, HNVSD 999, HNSHK 2, HKIDN 3, HKVVB 4,
optional HKSYN 5, HNSHA 5/6 and HNHBS 6/7. It preserves the explicit user
expectation and profile; message number is 1. Callers cannot edit the map.

Construction accepts neither PIN/TAN owners nor assembled output bytes and
creates no secret-bearing wire snapshot. Metadata can be built before encoding
and remains useful after credential disposal and output erasure. It describes
a candidate, not proof that encoding succeeded or that a bank received it.
It must remain tied to the same signature evidence if used with the writer.

## Response-reference comparison

`FinTsPinTanResponseBinding.Evaluate` accepts that metadata and an existing
parsed response. It retains both exact instances, the raw reported dialogue and
one source-preserving observation for each non-HIRMG body segment. Each
observation links the original response segment to the immutable request target;
absent or unknown references retain a null target. No source is rewritten,
renumbered or converted into a synthetic response.

The comparison requires an explicitly parsed PIN/TAN response envelope with
the selected profile version. The response message number must be 1, and its
outer reference must name that message and the bank-assigned dialogue. Dialogue
0, `unbekannt` and padded identifiers need review. The outgoing placeholder 0
is not substituted for the assigned dialogue.

HIRMS can reference any segment actually present in the candidate, including
signature and reserved envelope segments. An error referencing HNSHK 2 stays
associated with the signature header; it is never counted as identification
execution. A correctly scoped abort may have matching references and still be
an error. Original reply codes, parameters and status properties remain intact.

Known parameter data (HIBPA-3, HIUPA-4, HIUPD-6, HIPINS-1 and HITANS-6/7)
must reference HKVVB 4. HISYN-4 must reference HKSYN 5 and requires a
synchronization candidate. Wrong roles, absent references and unknown numbers
produce distinct issues. Unknown codes or future versions retain their source
and target observations but carry `UninterpretedData`; there is no fallback.
These version checks classify the narrow reference vocabulary and do not
replace business-schema parsing or validate payload fields.

## Limits and unresolved interpretation

Matching references establish only this structural relation. Bank/user/customer
identity, envelope system/key identity, status conflicts, missing/duplicate
parameters, procedure permissions, freshness and synchronization report shapes
still require separate semantic comparison. A test deliberately supplies foreign
user data with correct references to ensure this boundary remains explicit.
Existing unsigned comparators keep their original behavior and must not be
applied to these signed references.

The candidate has eight or nine segment descriptors. Response work is bounded
by the existing parser/envelope budgets; each body iteration observes
cancellation. Null inputs and unresolved signature evidence fail with fixed
diagnostics. Collections are immutable. Comparison consumes no response, makes
no clock or transport calls and provides no replay protection; repeated or
concurrent evaluation is intentionally pure. A later attempt model must own
submission and one-time response handoff.

No locally supplied credential bytes are captured by this API. Referenced
signature/procedure/response observations still contain untrusted identity and
bank data and are not suitable for unrestricted logging. No capability, cache,
recovery value, authentication state or live banking path is activated.

## Verification and next work

Twenty-three independent Python fixtures cover both profiles, initialization and
synchronization, old/foreign/missing references, signature/envelope errors,
profile mismatch, missing wrappers, dialogue/counter mismatch, unknown/future
data, unexpected synchronization reports and correctly scoped errors or foreign
parameter identities. The suite adds 361 checks for exact issues and target roles,
source retention, immutable metadata, cancellation, fixed diagnostics and pure
concurrent evaluation. Integration checks compare the metadata with actual
synthetic envelope output and repeat binding after clearing output and disposing
credentials. Existing unsigned and envelope suites continue to run unchanged.

Next is assembled initialization response semantics: bank/user/customer parameter
scope and status interpretation using the bound segment roles. Synchronization
semantics, attempt ownership, operation/challenge checks, secure input,
authenticated transport and controlled-bank acceptance remain pending.
