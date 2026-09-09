# ADR 0023: In-memory all-account discovery attempt

- **Status:** Accepted (synthetic local lifecycle only)
- **Date:** 2026-09-07
- **Related:** [All-account evidence](0022-all-account-discovery-evidence.md),
  [selected read lifecycle](0020-in-memory-read-refresh-attempt.md)

## Decision and scope

`FinTsAllAccountDiscoveryAttempt` owns one pending unsigned HKSPA-1 all-account
request and the exact caller-selected parameter set. Start shares the initial
validation used by ADR 0022. Unsupported requests or unsuitable parameters
terminate for review before recording or retaining a request. An active attempt
cannot replace its context, and a terminal attempt cannot restart.

This is a local lifecycle policy, using the protocol scope documented in ADR
0022. It sends no request and supplies no authentication, domain reconciliation
or durable replay protection. HKSPA-1 partial results remain unsupported; there
is no continuation API or automatic retry.

## Response consumption and ownership

Every response is checked against the pinned message and segment references
before account candidate expansion. A foreign response returns `ContextMismatch`
without consuming the request or returning candidate evidence. A later valid
response can still complete and replaces transient response diagnostics.

For a bound response the attempt recomputes the comparison internally. Clean
execution and scoped unavailability each consume the response once. A bound
review result also terminates, returning its unknown, ambiguous, conflicting
and unmatched account evidence for caller inspection. Review results do not
increment the clean-response counter. A candidate-budget failure terminates
with the fixed `LimitExceeded` diagnostic and returns no partial comparison.

`FinTsAllDiscoveryAttemptResult.Evidence` is handed to the caller once, including
on review outcomes. Every terminal path clears the attempt's request and
parameter references. Previously returned evidence and caller-owned inputs
remain available to their owners; reference release is not memory erasure.
No terminal result is stored by the attempt. Repeated or concurrent responses
cannot obtain a second handoff from the same instance.

An unavailable result preserves unmatched known accounts. None of these outcomes
authorizes activation, relinking, removal, credential use or ingestion.

## Lifetime and synchronization

The caller supplies a positive lifetime capped at 15 minutes. The deadline
starts when a valid request is recorded, uses monotonic elapsed time and never
extends on unrelated responses. Clock regression terminates as `ClockInvalid`.
Expiry at the deadline prevents handoff, including when preflight or candidate
matching crosses the deadline. Timeout is observed on API calls; no background
timer is created.

All state transitions use one lock. Cancellation and stop release pending
context; stop also supports caller abandonment after parsing, disconnect or
transport failure. A concurrent response and cancellation have one winning
terminal outcome. Cancellation waits for an already-running bounded comparison
to leave the lock; it does not interrupt that comparison.

Snapshots contain only state, counters, remaining time and fixed issue/error
codes. Default result diagnostics exclude account identifiers. Explicit source
and comparison objects remain untrusted banking data unsuitable for logging.

## Verification and next work

Eight independent Python traces define transitions, event times, terminal states
and evidence presence for execution, unavailability, ambiguity, cancellation,
timeout, foreign references, stop and unsupported partial results. The executable
suite adds 108 checks for those traces, pinned source identity, one-time handoff,
reference cleanup, deadline boundaries, resource failures and concurrent calls.

Next is credential-free read-request encoding with exact wire vectors under
M1-05. Authenticated sessions, durable replay, domain reconciliation and live
transport remain pending; the host stays inert.
