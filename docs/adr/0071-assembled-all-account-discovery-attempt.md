# ADR 0071: Bounded assembled all-account discovery attempt

- **Status:** Accepted (local synthetic lifecycle only)
- **Date:** 2026-10-14
- **Related:** [All-account response reference binding and semantics](0070-all-account-response-semantics.md),
  [All-account request envelopes](0069-all-account-request-envelope.md),
  [Assembled first-read attempt](0057-assembled-first-read-attempt.md),
  [All-account discovery attempt](0023-all-account-discovery-attempt.md)

## Candidate ownership and deadline

`FinTsPinTanAllAccountDiscoveryAttempt` coordinates the lifecycle of one immutable all-account
discovery candidate (`FinTsPinTanAllAccountRequestBinding`) under an absolute monotonic deadline.
`Start` records candidate metadata only; it handles no secret bytes, performs no network transmission,
and proves neither request submission nor authentication. A second `Start` call cannot replace the
candidate or restart the deadline. The caller supplies a lifetime bounded to fifteen minutes and an
optional `TimeProvider`.

The coordinator begins in `Ready` with no active deadline. Recording a candidate enters `AwaitingResponse`.
`ExecutionReported`, `UnavailableReported`, `NeedsReview`, `Cancelled`, `TimedOut`, `Stopped`, and `ClockInvalid`
are terminal states. Every terminal transition releases the internal candidate reference (`_pending = null`).
Calls on a terminal coordinator cannot restart the attempt, repeat evidence handoff, or mutate previous outcomes.

Time is evaluated on API calls rather than through background timers or polling threads. Reaching or
exceeding the deadline triggers `TimedOut`. Clock regression, negative elapsed time, or clock exceptions
fail closed to `ClockInvalid`. Cancellation tokens, `Stop()`, and `Dispose()` transition to terminal states
and release pending candidate metadata without modifying caller-owned objects.

## Response handling

`AcceptResponse` takes a parsed `FinTsReadDataSet`. It recomputes reference binding against the pinned
candidate and the dataset's exact source response, then computes `FinTsPinTanAllAccountEvidence` semantics.
Callers cannot bypass reference correlation or inject precomputed evidence. Parsing remains external;
callers abandon the attempt with `Stop()` after transport or framing failures. No automatic retry or
sending is scheduled.

Foreign scope issues (`MessageMismatch`, `MissingSegmentReference`, `UnknownSegmentReference`, or
`SegmentRoleMismatch`) return `ContextMismatch` without consuming the candidate or advancing the handled
count. The attempt remains active in `AwaitingResponse` under its original deadline, preserving transient
diagnostics until a subsequent scoped response arrives.

Scoped responses are handled exactly once:
- Clean execution terminates in `ExecutionReported` with a one-time handoff of `FinTsPinTanAllAccountEvidence`.
- Clean unavailable reports (3010) terminate in `UnavailableReported` with one-time evidence handoff.
  FinTS all-account discovery does not support continuation tokens; no pagination or multi-page loop exists.
- Semantic issues (duplicate returned accounts, identity conflicts, institution mismatches, unmatched UPD
  accounts, or envelope identity mismatches) terminate in `NeedsReview` with `RejectedForReview`.

A lock serializes public methods, ensuring concurrent response submissions produce at most one handoff.
Submissions after terminal transition return `Terminal` without evidence.

## Publication and cleanup

Availability, clock, and cancellation checks surround binding, semantic evaluation, and final publication.
Late timeout, clock invalidation, cancellation, or reentrant abandonment withhold evidence, leave
`ResponsesHandled` at zero, and release the pending candidate.

`FinTsPinTanAllAccountDiscoverySnapshot` exposes strictly scalar diagnostics: state, candidate count,
handled count, remaining time, and issue flags (`FinTsPinTanAllAccountIssue`, `FinTsPinTanResponseBindingIssue`,
and `FinTsReadContextIssue`). It contains no candidate, response, account, or secret references. The coordinator
retains no terminal evidence.

Successful handoff neither authorizes account ingestion nor activates live session state. The host remains inert.

## Verification

`FinTsPinTanAllAccountDiscoveryAttemptTests` exercises twenty-eight independent Python-generated traces
and 553 checks in `pin-tan-all-account-attempt-v1.json`. Verification covers:
- One-time execution and terminal transitions across profiles 1 and 2, TAN versions 6 and 7
- Clean discovery variations (single, multiple UPD accounts, escaped dialogue)
- Clean unavailable outcomes (absent, empty, and with execution replies)
- Scoped review outcomes (missing envelope, wrong profile, foreign envelope user, read error,
  unmatched UPD accounts, unknown returned accounts, duplicate identities, institution mismatches,
  identity conflicts, and duplicate discovery reports)
- Foreign scope handling (`ContextMismatch`) preserving active candidate and original deadline across
  dialogue mismatches, balance data, and signature header references
- Fixed deadline expiration, second start rejection (`WrongState`), cancellation, stop, dispose, and clock regression
- Full execution of all fifty-two independent response vectors through the attempt lifecycle
- Fault injection across all three active processing boundaries (expiry, token cancellation, clock regression,
  stop, dispose, cancel)
- Pre-cancellation and start clock failure boundaries
- Lifetime and clock constructor bounds validation
- Multithreaded concurrent submission producing exactly one evidence handoff
- Reflection assertions confirming scalar-only snapshot diagnostics and absence of sensitive fields
- Culture invariance (`de-DE`, `tr-TR`, `en-US`, `ar-SA`).
