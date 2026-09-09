# ADR 0027: In-memory initialization attempt

- **Status:** Accepted (local synthetic lifecycle only)
- **Date:** 2026-09-07
- **Related:** [Initialization scope](0026-initialization-response-scope.md),
  [discovery lifecycle](0023-in-memory-all-account-discovery-attempt.md)

## Decision and initial context

`FinTsInitializationAttempt` owns one pending restricted unsigned initialization
request and the exact caller-supplied expected user identifier. It uses the
initial-context and response-reference checks shared with the pure comparator
in ADR 0026. That comparator's supported protocol and parameter scope is unchanged.

A successful start records one request and begins its fixed deadline. A missing
expected user identifier on an identified request terminates for review before
retaining context or incrementing the request counter. Malformed caller strings
throw the existing fixed format error without changing a ready attempt; corrected
input may then start it. Anonymous starts retain their existing optional user
expectation policy. A second start cannot replace an active context, and terminal
attempts cannot restart.

The attempt creates no authenticated session, active dialogue, capability, account
or parameter cache. It sends no message and performs no synchronization or SCA.

## Response handling and ownership

Callers parse candidate responses into `FinTsParameterSet` before submitting
them. The parameter set owns its source response. Shared mechanical preflight
checks run before parameter comparison. A missing/foreign outer reference, bank
message mismatch or incorrect segment reference returns `ContextMismatch` and
leaves the original request pending, without handing out evidence.

A candidate with matching references is compared internally against the pinned
request and user expectation. Matching evidence terminates as `ExecutionReported`
and increments the accepted-response counter once. Bound review outcomes also
terminate, returning the complete comparison once without incrementing that
counter. Invalid assigned dialogue, identity conflicts, missing parameters,
unsupported status and bank errors therefore cannot activate a session or be
replaced by a later clean candidate. A valid candidate after an unrelated one
replaces transient scope diagnostics.

`FinTsInitializationAttemptResult.Evidence` belongs to the caller. Every terminal
path clears the attempt's pending request and expected-user references; no result,
parameter set or reported dialogue is retained in terminal state. Previously
returned evidence and caller-owned inputs remain available to their owners.
Reference release is not memory erasure.

Repeated, reparsed or concurrent responses cannot obtain a second handoff from
the same instance. This is per-instance consumption only. Initial requests reuse
message number 1, so local matching cannot distinguish a replay across fresh
attempts or establish transport provenance. Request routing, authentication and
durable replay evidence remain separate future requirements.

## Deadline, cancellation and diagnostics

The caller supplies a positive lifetime capped at 15 minutes. A monotonic clock
starts at successful recording and the deadline never extends. Equality with
the deadline expires the attempt. The clock is observed at API entry and after
preflight and comparison, so work crossing the deadline cannot hand out evidence.
Clock regression terminates as `ClockInvalid`.

Timeout is observed on API calls; no background timer is created. Callers must
call the lifecycle API or stop an abandoned attempt to release context. `Cancel`
and `Stop` terminate ready or pending attempts. Stop covers local abandonment
after parse failure, disconnect or transport failure, without automatic retry.
Existing terminal outcomes are immutable.

All transitions use one lock. A response and cancellation race has one terminal
winner. Cancellation waits for an already-running bounded comparison to leave
the lock; it does not interrupt that comparison. Existing parser/comparator
resource limits remain in force.

Snapshots expose only state, counters, remaining time and fixed issue flags.
They contain no customer, user, system, parameter or assigned-dialogue data.
Default result diagnostics likewise exclude source identifiers. Explicit evidence
properties remain untrusted private data unsuitable for logging.

## Verification and next work

Eight independent Python traces define event times, transitions, terminal states
and evidence presence for identified/anonymous execution, foreign references,
missing user context, missing parameters, cancellation, timeout and stop. The
executable suite adds 125 checks, including exact context pinning, one-time
handoff, terminal cleanup, invalid caller input, bound review outcomes, deadline
expiry during work, clock regression and concurrent start/response/cancel calls.
The pure initialization comparator's existing 120 checks remain unchanged.

Next is credential-free synchronization request/response schemas under M1-05.
Authenticated initialization, security codecs, reviewed cache reuse, transport,
durable replay and domain ingestion remain pending; the host stays inert.
