# ADR 0031: In-memory synchronization and closing attempt

- **Status:** Accepted (local synthetic lifecycle only)
- **Date:** 2026-09-08
- **Related:** [Synchronization context](0029-synchronization-response-context.md),
  [dialogue-end evidence](0030-dialogue-end-schemas-and-encoding.md),
  [initialization attempt](0027-in-memory-initialization-attempt.md)

## Decision and state transitions

`FinTsSynchronizationAttempt` owns one restricted unsigned synchronization
request followed by one explicitly supplied unsigned close request. It computes
evidence internally using the comparisons in ADRs 0029 and 0030. It does not
accept a caller's precomputed success result, activate a session or apply any
recovered identifier or counter.

| Current state | Input and required evidence | Result |
| --- | --- | --- |
| Ready | Start with valid request, profile hypothesis and required recovery context | AwaitingSynchronization |
| AwaitingSynchronization | Matching synchronization dataset | AwaitingCloseRequest; synchronization evidence handed to caller once |
| AwaitingCloseRequest | Explicit close for assigned dialogue with client/bank counters 2/2 | AwaitingCloseResponse |
| AwaitingCloseResponse | Matching closure report | Terminal ReinitializationRequired; closing evidence handed to caller once |
| AwaitingCloseResponse | Matching explicit abort | Terminal Aborted; abort evidence handed to caller once |
| Either response wait | Foreign message or segment reference | ContextMismatch; original request remains pending |
| Either response wait | Bound unresolved evidence | Terminal NeedsReview; review evidence handed to caller once |
| Any active stage | Deadline or clock regression | Terminal TimedOut or ClockInvalid |
| Ready or any active stage | Explicit local cancellation or stop | Terminal Cancelled or Stopped |

Request/profile requirements share the comparator's internal validation. Missing
prior context, prohibited combinations and unresolved profile hypotheses fail
before recording or retaining a request. Profile hypotheses still provide no
authentication evidence. Unknown profile values require review.

Synchronization is the first client and bank message in this restricted flow.
The next close must use counter 2 independently on each side and the exact
reported dialogue. No intervening operation or SCA continuation is supported.
A wrong close context leaves AwaitingCloseRequest intact for correction. The
attempt records requests but never encodes, signs or sends them itself.

Only a matching normal close reaches ReinitializationRequired. That terminal
state records a prerequisite for future work, not a business-ready dialogue or
permission to apply recovered data. Aborted and NeedsReview are separate terminal
states. An abort observed during synchronization requires review under ADR 0029
and cannot advance to closing; an abort during closing cannot cause another
close. Fresh initialization and explicit recovery application remain outside
this attempt.

## Ownership and response consumption

Start pins the exact request, profile and optional prior-dialogue context. A
matching synchronization response releases those references immediately, handing
the comparison to the caller and retaining only the reported dialogue ID.
Recording the close replaces that ID with the exact close-request object.
All terminal paths clear every mutable reference to request, recovery context
and dialogue data. Returned evidence remains caller-owned and untrusted.

Message/reference mismatches return no evidence and consume no response. Bound
review results are returned once and terminate their stage. `ResponsesAccepted`
counts matching observations: one for synchronization and one for either matching
closure or matching abort. Review evidence does not increment it.
`RequestsRecorded` is at most two. Transient scope issues clear on a subsequent
matching candidate.

All operations use one lock. Concurrent starts, close recording and response
delivery cannot replace pinned context or return the same stage's evidence twice.
Terminal states are immutable; restart and repeated response calls return no
evidence, including when identical bytes have been reparsed into new objects.
Calls for another active stage return WrongState.

This is per-instance consumption only. A new instance can reuse initial message
numbers; these comparisons provide no cross-attempt or durable replay protection,
transport provenance or proof of bank receipt. A caller retaining synchronization
evidence after cancellation or timeout still has only a raw observation.

## Lifetime and diagnostics

A caller-selected positive lifetime, capped at 15 minutes, begins at successful
Start using `TimeProvider` timestamps. It covers both response waits and the
interval awaiting an explicit close request. Progress, foreign candidates and
repeated calls never restart it. Deadline equality expires the attempt.
Clock regression terminates with ClockInvalid. Both response comparisons check
the clock again before any evidence handoff.

No timer, background work, wall-clock dependency or network operation is added.
Timeout is observed on the next API call. Cancel and Stop abandon local work;
they do not prove remote termination, send HKEND, retry or start initialization.
Stop covers parse failure, disconnect and transport failure once reported by a
future caller.

Snapshots contain only state, counts, remaining duration and issue flags. They
exclude identifiers, source objects and recovered values. Default diagnostics do
not expose private context; explicitly returned evidence must not be logged.

## Verification and next work

Twelve independent Python traces cover all synchronization modes, foreign replies
at both stages, bound review, explicit abort, cancellation between stages, the
shared deadline, missing recovery context and stop before start. The executable
suite adds 249 checks for exact ownership, stage isolation, per-instance replay,
counter/dialogue binding, reference cleanup, timeout during comparison, clock
regression, concurrent calls and cancellation/close races.

Next is credential-free PIN/TAN signature-header schema work under M1-05.
Full request security, session-only credential ownership, authenticated sessions,
fresh initialization integration, explicit recovery application, transport and
durable integration remain pending. The host stays inert.
