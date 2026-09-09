# ADR 0017: Bounded in-memory SCA continuation

- **Status:** Accepted (synthetic local state and evidence only)
- **Date:** 2026-09-06
- **Related:** [Challenge context](0016-permitted-procedures-and-challenge-context.md),
  [TAN schemas](0015-tan-procedure-and-challenge-schemas.md),
  [dialogue correlation](0011-fints-responses-and-dialogue-correlation.md)

## Source and scope

Reviewed the official [FinTS specification](https://www.fints.org/de/spezifikation),
**Security – Sicherheitsverfahren PIN/TAN**, 2020-07-10, B.4.2.2 and the
status-query count, wait, manual-confirmation and automatic-query parameter
definitions. Source hash and related schema definitions are in ADR 0014/0015.

`FinTsScaContinuation` owns one in-memory attempt built from previously
compared synthetic evidence. It records local intent and response observations;
it never sends a request, schedules a timer, polls a bank, accepts a TAN,
authenticates a session or authorizes an operation. No host integration is added.

## States and terminal outcomes

The model starts in `Ready`. Clean initial process-1/4 evidence establishes
exactly one current challenge. Subsequent starts cannot replace it.
Unresolved initial evidence terminates in `NeedsReview`; a status response
cannot start a new attempt.

- TAN-input procedures enter `AwaitingUserContinuation`. One explicit
  `RecordUserContinuation` call terminates in `UserContinuationRecorded`.
  This records a local handoff intent, not a submitted or validated credential.
- The exact `Decoupled` method enters `WaitingToQuery`, then
  `AwaitingQueryResponse` after a matching query is explicitly recorded.
- `DecoupledPush` enters `AwaitingExternalNotification`. Push delivery and
  notification verification are unsupported; no query or user handoff is
  inferred from this state. Cancellation, stop and timeout remain available.
- An initial exemption observation terminates in `ExemptionReported`.
- A scoped process-S execution observation terminates in `ExecutionReported`.

Other terminal states are `Cancelled`, `Stopped`, `TimedOut`, `NeedsReview`,
`QueryLimitReached`, `CounterExhausted` and `ClockInvalid`. Terminal states
cannot restart or be replaced by later cancellation, response or stop calls.
All transitions and snapshots are synchronized through one per-instance lock.

Terminal transitions release the model's initial evidence, pending request
and current challenge references. They do not erase buffers owned elsewhere
by callers or the immutable parsers. Scalar counts/state remain available.
Default model/snapshot diagnostics contain no raw bank fields.

## Local clock and query policy

The caller supplies a positive lifetime of at most 15 minutes. This is a local
resource policy, not a bank validity rule. The model uses an injected
`TimeProvider` monotonic timestamp, defaulting to the system provider; it never
uses wall-clock time. The deadline starts with the accepted initial challenge
and is never extended by requests or responses. Equality with the deadline
expires the attempt. Clock regression terminates in `ClockInvalid`.

Clock/deadline checks occur on every public state observation or transition.
No background timer is created, so an idle model records expiry on its next
access. Parsed bank expiry still requires the trusted timezone/clock policy
that is intentionally absent from the preceding evidence comparator.

The caller's local query ceiling is 0–128 (default 32). The effective ceiling
is the smaller of that value and the bank's reported maximum. Explicit zero
terminates polling immediately. A recorded query consumes one slot; a rejected
or premature candidate consumes none. A pending response at the final slot
terminates rather than retrying indefinitely.

The first query waits at least the advertised initial delay. Later queries
wait from receipt of the previous accepted pending response. Local policy
conservatively applies those minimum waits to manual as well as automatic
triggers. This is intentionally stricter than allowing a manual confirmation
to bypass an automated wait interval.

Automatic recording requires both explicit local opt-in and a bank J flag.
Manual recording requires either a bank N flag for automated queries (manual
mode) or a bank J flag for manual confirmation alongside automation. Missing
permission flags do not imply permission. These checks control recording in
this local model only; no successful return value grants permission to send.

## Binding and replay handling

`RecordStatusQuery` accepts one caller-created synthetic process-S request.
The request must contain only the framing and HKTAN segment, preserve the
selected version, raw dialogue ID and initial bank order reference, and
preserve the optional selected medium. A populated operation code must equal
the initial operation. Client and expected bank message counters must be the
next exact values, with no gaps or wraparound.

There can be only one outstanding query. `AcceptStatusResponse` requires the
exact recorded request-context instance and the same parameter, procedure and
permission instances that established the attempt. Reparsed lookalike requests
or substituted procedure/permission evidence are rejected without consuming
the pending request. A correctly bound response with unresolved context issues
terminates in `NeedsReview`, with no automatic retry.

Accepted pending responses update the current challenge and advance counters
once. Previously consumed message counters reject request/response replay,
including concurrent callers. Counter 9999 never wraps. This is per-instance,
in-memory replay handling only; a new model, another process or a restarted
application has no shared durable replay history.

The pure comparator gains the narrow `ExecutionReported` observation for a
matching process-S HITAN with exactly one whole-request HIRMS 0020 and no
pending/exemption status. Receipt-only, element-level or conflicting status
continues to require review. This is a report of execution under a restricted
comparison; it does not prove authentication, successful SCA or business
completion. Other completion forms and full operation-response interpretation
remain unsupported. The generic reply-code parser is unchanged.

## Verification and next work

Eight independently specified public traces and 123 checks cover a single
handoff, pending-to-execution flow, replay, absolute timeout, cancellation
while awaiting a response, query exhaustion, push waiting, exemption reports
and rejected bound responses. Additional tests exercise frozen context,
trigger permissions, malformed local limits, monotonic-clock regression,
message-counter exhaustion, exact boundaries and concurrent callers.
The fake clock throws if wall-clock time is accessed. Tests never sleep or
contact a bank. See the [fixture inventory](../../tests/Broiler.Fond.Kernel.Tests/Fixtures/FinTs/README.md).

Next are credential-free account-discovery/balance request and response
schemas (HKSPA/HISPA and HKSAL/HISAL), with synthetic fixtures before
authenticated domain ingestion. Full HKTAN options, credential submission,
trusted expiry, push delivery, authenticated sessions, persistent replay
evidence, transport and payments remain unimplemented.
