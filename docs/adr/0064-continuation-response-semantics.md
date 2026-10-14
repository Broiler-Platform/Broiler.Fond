# ADR 0064: Assembled continuation-response semantics

- **Status:** Accepted (local synthetic comparison only)
- **Date:** 2026-10-14
- **Related:** [First-read semantics](0056-assembled-first-read-response-semantics.md),
  [continuation context](0058-assembled-balance-continuation-context.md),
  [continuation reference binding](0063-continuation-response-reference-binding.md)

## Exact second-page provenance

`FinTsPinTanReadContinuationEvidence.Evaluate` takes a continuation-response
binding and typed `FinTsReadDataSet`. The typed data must name the exact response
instance in the binding. A byte-identical reparse or the predecessor's typed
data cannot contribute qualified data or a token. A second typed view of the
same response is allowed. The result retains the exact binding, candidate,
response and raw data set, including mismatched data for inspection.

The existing outer read-issue flags distinguish binding review, source mismatch,
envelope identity mismatch and semantic review. Detailed read-context flags are
preserved. Any issue forces NeedsReview and withholds MatchingReadData. PageNumber
comes from the qualified continuation context and remains 2 even on review.

This applies the existing read-data and scoped-status rules from ADR 0056 to the
actual continuation candidate. No new protocol version or reply-code meaning is
introduced. Shared production helpers, first-read APIs and enum values remain
unchanged.

## Identity, account and outcome checks

The response envelope's system identifier and key country, institution and user
must match the continuation signature header. Profile, exact dialogue, counters
and actual segment roles remain the binding's responsibility. Both qualified
PIN profiles are supported without authenticating returned identities.

Shared status and data checks use assembled read segment 3 and the exact original
capability evidence inherited by the qualified continuation. Balance versions
must equal the request version. All logical national/international account
components must match the proposed request and selected UPD identity. Missing,
duplicate, wrong-operation or unknown data, currency conflicts and unhandled due
dates remain review issues.

Only scoped read reports supply read status. Additional signature, wrapper or
framing replies require review even when they report execution. Pending, errors,
unknown/conflicting/duplicate statuses and unsupported parameters retain existing
review behavior. One clean scoped 3010 may qualify absent data as unavailable;
actual balance data, including zero, conflicts with that observation. Generic
reply parsing still leaves 3010 uninterpreted.

Booked, pending and available values retain their distinct typed observations,
signs and timestamps. A real zero stays zero, missing optional values stay absent,
and unavailable data is not invented. Second-page observations do not sum,
overwrite, reconcile or ingest the first page's values.

## Partial tokens and ownership limits

A scoped balance 3040 requires a nonempty opaque token and only empty trailing
parameter positions. Compatible execution plus partial status yields
PartialReported. Valid same-source tokens remain raw observations even when other
issues require review; foreign typed data contributes no token. Token bytes retain
case, whitespace and escaped delimiters. Existing response parsing rejects
control bytes and parameters longer than 35 bytes before semantic comparison.

An exact repetition of the request token remains an observation, not proof of
progress. This comparison schedules no further page and proves no replay safety
or permission to continue. Pagination limits, response consumption and broader
session coordination belong to later lifecycle work.

Evaluation accepts no credentials, clock, transport or encoded secret bytes.
It does not prove encoding, submission, SCA completion, authentication or freshness.
Parser budgets bound input; existing per-report cancellation checks and the
final publication check remain active. Repeated and concurrent evaluations are
pure. Retained identity and account evidence remains private and untrusted.

## Verification and next work

Seventy-four independent Python fixtures and 676 checks cover exact source,
message-3 scope, both profiles, HKSAL versions 6/7/8, every national identity
component, IBAN/BIC, currencies, envelope identity, optional values and
zero/unavailable/partial distinctions. Missing and duplicate reports, wrong
references, status failures, repeated/escaped/maximum/invalid tokens and parser
rejections remain explicit.

Tests verify original request/predecessor/response bytes and object identity,
rejection of predecessor or byte-identical foreign typed data, same-source typed
reparsing, separate page amounts, review withholding, null diagnostics,
cancellation, cultures and concurrency. Full Windows CI passed with zero warnings
or errors. All 55 FinTS fixture files reproduced byte-for-byte, and existing
fixture contents were preserved.

Next is a bounded assembled continuation attempt with a fixed deadline, one-time
response handoff and terminal metadata cleanup. Later-page/session coordination,
TAN/challenge integration, authenticated transport and live qualification remain
pending. The host remains inert.
