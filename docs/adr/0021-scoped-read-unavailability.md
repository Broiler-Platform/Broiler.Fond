# ADR 0021: Scoped read-unavailability reports

- **Status:** Accepted (untrusted read observations only)
- **Date:** 2026-09-07
- **Related:** [Read context](0019-read-request-response-context.md),
  [read attempt](0020-in-memory-read-refresh-attempt.md)

## Source and meaning

Reviewed **FinTS Rückmeldungscodes**, 2026-02-03, B.3, from the
[official specification](https://www.fints.org/de/spezifikation). PDF SHA-256:
`f0ab2a40c93921a31b715c6006e683ebd9ca7a4d3a842120b8c75d2d535c7b2d`.
Code 3010 is an order-level warning meaning unavailable. Its examples include
temporarily unavailable information and no new entries; it does not uniquely
mean an empty account. Code 9210 remains an error with several possible causes.

Messages, 2022-04-15, C.10.1.3 (printed page 380), lists 3010 as an example
for no account connections returned by discovery. The source hash is in
[ADR 0018](0018-account-discovery-and-balance-schemas.md). The implementation
uses the broader typed meaning `UnavailableReported`, retaining exact source
reports and text without interpreting their prose as closure, deletion or zero.

## Context rules

This extends the existing selected-account HKSPA-1 and HKSAL-6/7/8 comparison.
All request, capability, dialogue, reference, version, account and continuation
checks continue to apply. A clean unavailable observation requires exactly one
request-scoped HIRMS 3010 with no element reference or nonempty parameters.
Trailing empty parameter positions remain allowed.

The limited scoped comparator understands 3010; the generic reply parser still
retains it as `Uninterpreted`. Other protocol/parameter/SCA consumers therefore
do not silently gain a new status permission or waive their review rules.
`FinTsReadDataSet.Source.HasUninterpretedCodes` may remain true even when the
scoped read comparator returns `UnavailableReported`.

With a valid scoped 3010, absent business reports are allowed. Discovery may
instead include one supported empty HISPA report. Those shapes remain distinct
in `Response.Discovery` and `Response.Balances`; no empty report is fabricated.
The request still supplies the selected-account scope when no response account
tuple is present. This does not authenticate that scope.

Returned balance/account data, including an explicit zero balance, conflicts
with an unavailable report and adds `AvailabilityConflict`. A simultaneous
3040 partial report also conflicts. Duplicate unavailable replies, element-level
3010, nonempty parameters, pending/error/unknown statuses, unknown report
versions and duplicate reports remain review outcomes. Wrong message/segment
scope cannot supply availability evidence for the selected account.

An otherwise valid 0020 acknowledgement alongside 3010 is compatible: the
request can have been processed while its requested information is unavailable.
The typed outcome remains `UnavailableReported`. A missing or empty report
without scoped 3010 still produces `MissingReport`. A normal 0020 with a
reported zero balance remains `ExecutionReported` with an exact zero amount.

No wording in the free-form bank text changes these typed rules. In particular,
a 9210 whose text mentions a depot without a balance remains an error, and
3010 text mentioning closure establishes no closed-account state.

## Attempt lifecycle and cached values

The existing read attempt now has terminal `UnavailableReported` state and
transition values. A matching unavailable response increments `PagesAccepted`
once, clears retained request/parameter/page/dialogue references and cannot
restart, continue or be consumed again. The count denotes accepted response
observations, including a response with no account values.

This terminal report can also follow a valid partial page. It then describes
the requested continuation page, not the entire account or every earlier page.
Continuation provenance is still required. The earlier input data is not
rewritten or reclassified; caller-owned evidence remains intact. No cached
balance, account identity or source binding is deleted or replaced.

Unavailable observations can terminate at the local page limit without being
treated as another partial page. Cancellation, timeout, one-time consumption
and terminal reference-release rules remain unchanged. Concurrent responses
produce one unavailable transition and subsequent terminal rejections.

## Verification and next increment

Eight independent Python fixtures cover empty and absent discovery reports,
unavailable balance retrieval, conflicting zero data, missing reports without
3010, ordinary errors and partial/unavailable conflicts. The executable suite
adds 85 checks covering explicit versions, source scope, incompatible statuses,
raw-text invariance, preserved zero/missing distinctions, continuation provenance,
terminal reference release, concurrency and timeout. Existing fixtures and
prior comparison/lifecycle checks remain unchanged.

Next: bounded all-account discovery evidence with returned-account matching and
explicit ambiguity handling. Authenticated sessions, account reconciliation,
durable replay protection, aggregation and domain ingestion remain pending.
No bank connection or cached-value update is enabled.
