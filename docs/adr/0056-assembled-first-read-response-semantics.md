# ADR 0056: Assembled first-read response semantics

- **Status:** Accepted (local synthetic comparison only)
- **Date:** 2026-09-10
- **Related:** [Read context](0019-read-request-response-context.md),
  [scoped unavailability](0021-scoped-read-unavailability.md),
  [read capability integration](0050-initialization-read-capability-integration.md),
  [assembled read references](0055-first-read-response-reference-binding.md)

## Source and scope

This increment applies the read-data, account and status rules from ADRs 0018,
0019 and 0021 to the actual assembled read references established in ADR 0055.
Their reviewed Messages, Formals and reply-code sources remain available through
the [official specification](https://www.fints.org/de/spezifikation). No new
protocol version, reply-code meaning or authentication claim is introduced.

`FinTsPinTanReadEvidence.Evaluate` takes an assembled first-read response binding
and a typed `FinTsReadDataSet`. It requires the data set's Source to be the exact
response object retained by that binding. A separately parsed response containing
identical bytes is a different source and is rejected before interpreting its
data or continuation token. A second typed view of the same response is allowed.

The result retains the exact binding and raw data set. Outer issues distinguish
binding review, read-source mismatch, envelope identity mismatch and read review;
ReadIssues preserve the detailed existing read-context flags. Any outer issue
forces NeedsReview and withholds MatchingReadData. Raw inputs remain available
for inspection, including a mismatched source; they are not qualified evidence.

## Identity and data comparison

The response envelope's system identifier and key country, institution and user
must match the bound signature header. Profile, dialogue, message counters and
segment references remain the binding's responsibility. Both qualified PIN
profiles are supported; the legacy unsigned comparator keeps its existing
profile policy. No returned identity activates or authenticates a session.

Shared internal helpers preserve the existing account and status rules. The
unsigned path still supplies its original read segment number and performs its
own message/reference/profile and continuation checks. The assembled path uses
actual read segment 3 and the already qualified first-read capability context.
Neither source segments nor response bytes are rewritten to simulate unsigned
references.

Exactly one matching report/account is required except for a valid scoped
unavailable observation. Discovery checks national identity against the request
and selected UPD account, including known IBAN consistency. Balance reports must
match every logical national/international request component and UPD identity.
Exact response/request version equality is now enforced after reference binding.
Unknown or wrong-operation data, duplicate or missing reports/accounts, currency
conflicts and unhandled due dates remain explicit review issues.

Optional booked, pending and available values retain separate typed observations
and source timestamps. Unsigned wire amounts and debit/credit indicators remain
distinct; the existing SignedValue projection preserves debit polarity. Missing
amounts are not synthesized, and a reported zero remains a real zero observation.
No values are ingested, combined, cached or declared fresh.

## Status and continuation observations

Only HIRMS for assembled read segment 3 supplies read status evidence. Any
additional scoped reply on a signature, wrapper or framing role requires review,
even if it reports execution. Message-level 0010/0020 alone cannot establish a
read outcome. Errors, pending, unknown/conflicting or duplicate status reports,
element references and unsupported parameters retain the existing review rules.

Exactly one clean scoped 3010 permits absent balance/discovery data or an explicit
empty discovery report. It yields UnavailableReported without inventing data or
interpreting bank prose as account closure. Generic reply parsing still calls
3010 uninterpreted. Actual balance/account data, including zero, or a simultaneous
partial report conflicts with unavailability. A compatible 0020 acknowledgement
does not override the unavailable outcome.

A balance 3040 requires a nonempty opaque token with only empty trailing parameter
positions. A compatible 0020 plus 3040 yields PartialReported. Discovery
continuation remains unsupported. A valid same-source token is retained even when
other issues require review, but foreign typed data cannot contribute a token.
This first-read comparator constructs no continuation request, schedules no work
and grants no permission to continue.

## Ownership and verification

Comparison takes no credential owner, secret output, clock or transport. It does
not prove request encoding, submission, TAN/SCA completion or authentication.
Collections and original source objects remain immutable; evaluation is repeatable
and concurrent, with no response consumption or replay protection. Existing parser
limits bound work, and cancellation is checked before work, within report loops
and before result publication. Retained identity and response data remain private.

Fifty-nine independent Python fixtures and 495 checks cover all supported read
versions and both profiles, exact national/international identities, separate
optional values, zero/unavailable/partial distinctions, escaped tokens, source
provenance, each envelope identity component, version/currency conflicts, missing
and duplicate data, due dates, wrong references and scoped status failures.
Tests retain raw sources while withholding qualified data on every review result.
Additional checks cover byte-identical foreign response trees, a second typed view
of the same source, cancellation, null diagnostics, cultures and concurrency.
The unchanged unsigned read, continuation and unavailability suites also run.

Next is a bounded assembled first-read attempt owning one candidate and deadline,
consuming a scoped response once and releasing pending metadata on terminal paths.
TAN/challenge integration, continuation assembly, broader session coordination,
secure input, authenticated transport and live qualification remain pending.
The host stays inert.
