# ADR 0026: Initialization response binding and parameter-scope evidence

- **Status:** Accepted (pure synthetic comparison only)
- **Date:** 2026-09-07
- **Related:** [Initialization schemas](0025-unsigned-dialogue-initialization.md),
  [parameter evidence](0012-fints-parameter-evidence.md),
  [mechanical correlation](0011-fints-responses-and-dialogue-correlation.md)

## Sources and scope

Reviewed Formals, 2017-10-06, B.7, C.3.1.3–C.3.2, C.5.1 and E.2–E.3
from the [official FinTS specification](https://www.fints.org/de/spezifikation).
The PDF hash is recorded in ADR 0025. These sections define initialization
response framing, optional parameter transmission, distinct user/customer
identities, guest UPD and dialogue-scoped UPD version zero.

`FinTsInitializationEvidence.Evaluate` compares one existing restricted unsigned
initialization request with one `FinTsParameterSet`. The parameter set owns its
source response, preventing the API from pairing separately supplied response
and parameter objects. The exact request, response and parameter objects remain
available on both matching and review results.

This comparator authenticates nothing, creates no session, consumes no response,
merges no cache and activates no capability or account. A matching observation
is called `ExecutionReported`; all unresolved issue sets produce `NeedsReview`.
The existing mechanical dialogue correlator is unchanged.

## Dialogue, references and status

The bank response must have message number 1 and an outer reference to the
request's message number. The reference dialogue must equal the newly reported
bank dialogue identifier, rather than the outgoing placeholder `0`. The assigned
identifier cannot be `0`, `unbekannt` or padded with ASCII spaces. Its raw value
remains exposed even on review outcomes, with no session activation.

HIRMS references may target HKIDN or HKVVB. Parameter segments must reference
HKVVB, including supported HIBPA/HIUPA/HIUPD. Missing, foreign or incorrectly
targeted references require review. Explicitly parsed PIN/TAN envelopes also
require review in this restricted initialization comparator; no security-profile
support is inferred from envelope parsing.

The local status vocabulary accepts HIRMG 0010/0020 and scoped HIRMS 0020.
Either message-level 0020 or execution reports for both identification and
preparation are required. Receipt alone does not establish matching evidence.
Errors, conflicting classes, indeterminate status, unknown codes, duplicate
codes within a reply segment, element references and nonempty reply parameters
require review. Omitted trailing reply parameters remain omissions.

One HKVVB-scoped 3050 can be preserved as a parameter-update observation when
at least one parameter header is returned. It does not itself provide execution
evidence. Free-form text never selects BPD versus UPD, and generic reply decoding
continues to classify 3050 as uninterpreted. Unscoped, repeated or unsupported
3050 shapes require review.

## Parameter identity and absence

Returned BPD must match the request's country and institution exactly, advertise
FinTS 300 and include any explicitly selected nonstandard language. Language 0
does not choose a returned language or negotiate a character subset.

Identified initialization requires an explicit expected user identifier supplied
by the caller. HIUPA's user identifier is compared to that value, not to HKIDN's
customer identifier. Missing caller context is a review issue. Each HIUPD entry's
customer identifier must match HKIDN; account-bound entries must also match its
country and institution. Non-account-bound entries remain non-account-bound.
No source row is removed, deduplicated or assigned a local identity.

For anonymous requests, guest HIUPA user identifiers must consist only of nines.
The parser already bounds and requires that identifier. HIUPD customer identifiers
must use the prescribed anonymous customer marker. An optional explicit user
expectation is also checked exactly. This validates a source namespace only and
does not permit account discovery or identified account linkage.

Returned BPD is required for a matching result in this local subset. Identified
requests also require HIUPA; anonymous BPD-only replies can match, but account
entries without HIUPA require review. The protocol allows parameter omission:
these missing-data issues mean this comparator lacks the required evidence,
not that every omitted-parameter response is protocol-invalid. In particular,
nonzero sent versions cannot cause implicit cache reuse.

`BankVersionChanged` and `UserVersionChanged` compare returned versions with
the request and remain null when absent. A version change, including a lower
value, is not a freshness or downgrade verdict. HIUPA version zero retains its
existing dialogue-scoped marker and never becomes durable reusable state.

Uninterpreted parameter segments, including advertisements outside the base
parameter parser, remain available and require review. A match therefore covers
only this supported subset; it makes no parameter completeness, account
inventory, extension interpretation or negotiated-capability claim.

## Bounds and verification

Existing frame, reply and parameter limits apply, including 512 UPD account
entries. Cancellation is checked during response, reply and account traversal.
Default evidence diagnostics contain no identifiers; explicit source properties
remain untrusted private data unsuitable for logging.

Fourteen independent Python fixtures cover identified and anonymous matching,
zero-version UPD, omitted cached parameters, message/segment scope failures,
bank/user/customer/account-institution mismatches, scoped updates, unknown
advertisements, receipt-only outcomes and errors with parameter data. The suite
adds 120 checks for these cases, invalid dialogue assignments, explicit user
context, status ambiguity, wrapped responses, all 512 entries, source identity,
cancellation and repeated pure evaluation without replay consumption.

Next is a bounded local initialization attempt with one-time response handling,
timeout, cancellation and terminal context release. Authenticated initialization,
cache reuse, synchronization, transport, durable replay and domain ingestion
remain pending. The host stays inert.
