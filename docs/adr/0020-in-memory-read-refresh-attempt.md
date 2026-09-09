# ADR 0020: Bounded in-memory read-refresh attempt

- **Status:** Accepted (synthetic local lifecycle only)
- **Date:** 2026-09-07
- **Extended by:** [Scoped read unavailability](0021-scoped-read-unavailability.md)
- **Related:** [Read context](0019-read-request-response-context.md),
  [read schemas](0018-account-discovery-and-balance-schemas.md),
  [SCA lifecycle](0017-in-memory-sca-continuation.md)

## Scope and ownership

`FinTsReadRefreshAttempt` owns one local, selected-account read attempt. It
records caller-created unsigned requests and consumes parsed response
observations. It does not send, authenticate, schedule, aggregate pages, update
the domain or persist anything. No host or connector implementation is added.

The request/account/capability and continuation rules from ADR 0019 are shared
internal comparison helpers, used by both the pure comparator and this attempt.
No synthetic response is invented to validate an outgoing request.

`Start` validates the initial request and pins its exact capability and optional
national-permission advertisement. Initial continuation, all-account requests
or unresolved context terminate in `NeedsReview` without recording a request.
Only a successful start begins the deadline and records the first request.
Another start cannot replace an active request or restart a terminal attempt.

## Transitions

All transitions, snapshots and continuation-evidence observations use one lock.

- `Ready` becomes `AwaitingResponse` after one valid start.
- `AcceptResponse` recomputes context evidence using the internally pending
  request, pinned parameters and last accepted partial page. It does not accept
  caller-computed comparison results that could omit prior-page context.
- Unrelated message/dialogue/segment references return `ContextMismatch`,
  preserving the pending request. A same-dialogue response at or below the last
  accepted bank counter returns `ReplayRejected`. Neither consumes a page.
- A response bound to the pending request but needing schema-context/status
  review terminates in `NeedsReview`, preserving only scalar issue flags.
- A matching partial page increments the accepted count and enters
  `WaitingToContinue`. `ContinuationEvidence` exposes that exact untrusted
  page only while waiting. It grants no permission to send another request.
- `RecordContinuation` validates an explicit next request against the pinned
  capability and previous page. Old counters are rejected as replay; other
  mismatches leave the attempt waiting without consuming a request slot.
  One valid continuation enters `AwaitingResponse` and hides the public
  continuation-evidence property while retaining the previous page internally
  for response comparison.
- A matching execution report increments the accepted count and terminates in
  `ExecutionReported`. This is an untrusted reported outcome, not authenticated
  completion, account freshness or a committed balance update.

`Cancel` and `Stop` are terminal from Ready or either active state. Other terminal
states are `TimedOut`, `ClockInvalid`, `PageLimitReached` and `CounterExhausted`.
Terminal results are immutable: later cancellation, stop, start, request or
response calls cannot replace them. Calls after termination return `Terminal`.
Wire parsing occurs before `AcceptResponse`; a future caller must use `Stop`
to abandon the attempt after parse failure, disconnect or transport failure.

## Budgets, clock and replay

The caller supplies a positive lifetime up to 15 minutes and a page budget of
1–128, default 16. These are local resource policies, not bank validity rules.
Time comes only from an injected `TimeProvider` monotonic timestamp. The model
starts its absolute deadline when it records the initial request; accepting
pages, rejecting candidates or recording continuation never extends it.

Every observation/transition checks expiry. Validation is also followed by a
clock check before recording continuation or accepting a response. Equality
with the deadline expires the attempt, including time spent validating.
Clock regression terminates in `ClockInvalid`. No background timer is created;
an idle attempt notices expiry on its next access.

A partial response at the last permitted page terminates in `PageLimitReached`.
An execution report at that same boundary can terminate normally. Likewise,
message counter 9999 can receive a final execution report, while a partial report
there terminates in `CounterExhausted` instead of wrapping either counter.
The context comparator continues to enforce independent client/bank counters.

Replay rejection uses consumed counters within this one attempt and dialogue.
Reparsing identical bytes cannot bypass it. This does not establish durable
replay evidence, survive process restarts or deduplicate across attempt
instances. It makes no assumption that continuation tokens are unique.

## Retention and diagnostics

At most the pinned capability/advertisement, one pending request and one last
partial-page comparison are retained. Each new accepted partial replaces the
old one; the comparator itself retains no previous-page chain.

Every terminal path clears the attempt's request, capability, advertisement,
page and dialogue references. Scalar state, request/page counts and last issue
flags remain in snapshots. The snapshot contains no account, amount, raw status
text or token; default object diagnostics contain no source data.

Reference release does not erase immutable parser buffers or caller-owned
objects. Callers may still own the input response or a previously observed
continuation page. Raw evidence remains unsuitable for logging or credentials.

## Verification and next increment

Eight independent Python traces cover one-page execution, partial continuation
and replay, absolute timeout, cancellation in flight, page exhaustion, an
unrelated reference followed by a matching response, a bound account mismatch
and stop before start. The executable suite adds 137 checks, including fake
clock boundaries, expiry during validation, terminal reference release,
128-page/counter limits and concurrent starts, responses, continuations and
cancellation. It uses no sleeps, credentials or bank transport.

Next: explicit bank-reported no-data outcomes for selected-account reads,
preserving the difference between an empty report, missing data and an error.
All-account discovery, broader account-type handling, authenticated SCA/session
integration, durable replay evidence, aggregation and domain ingestion remain
pending. No cached value is created or replaced by this local attempt.
