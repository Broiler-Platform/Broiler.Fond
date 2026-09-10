# ADR 0043: Assembled PIN/TAN synchronization attempt lifecycle

- **Status:** Accepted (local synthetic lifecycle only)
- **Date:** 2026-09-09
- **Related:** [Synchronization semantics](0041-pin-tan-synchronization-response-semantics.md),
  [initialization lifecycle](0042-pin-tan-initialization-attempt-lifecycle.md),
  [unsigned synchronization/closing](0031-in-memory-synchronization-and-closing.md)

## Ownership and scope

`FinTsPinTanSynchronizationAttempt` retains one immutable assembled candidate and
its optional exact recovery context. Start records metadata only. It takes no
credentials or encoded buffers and proves no encoding, transmission or execution.
Initialization candidates, missing recovery context for message recovery, and
recovery context on system assignment terminate for review before being retained
or counted. Another Start cannot replace either reference or renew the deadline.

The attempt recomputes response binding from the synchronization data set's exact
source and runs ADR 0041 semantics internally. Callers cannot inject a precomputed
success. One lock serializes state changes and response handoff.

## Response and terminal states

A valid start moves Ready to AwaitingResponse. Missing, unknown or wrong-role
segment references and outer message mismatches yield ContextMismatch, retaining
the original candidate and recovery context without counting a response or
extending time. Only scalar issue flags survive the mismatch.

A scoped matching response terminates as CloseAndReinitializeRequired. This is a
mandatory next-step handoff, never confirmation of closure or a usable session.
A scoped unresolved response, including bank abort, terminates as NeedsReview.
Both scoped outcomes return caller-owned evidence exactly once and increment
ResponsesHandled to one; this counter does not imply successful synchronization.
Reported system identifiers and message counters are never applied.

Every terminal path clears both pending references. The attempt stores no terminal
evidence. Repeated, reparsed or concurrent submissions cannot produce another
handoff; cancellation, stop and disposal cannot change an existing terminal state.
These guarantees concern one instance, not a cross-instance replay registry.

## Deadline and failures

A positive lifetime up to fifteen minutes starts at candidate recording. The
deadline is absolute, expires at equality and is observed on API calls without a
background timer. It covers synchronization response handling only; it does not
continue timing the caller's subsequent closing work.

Clock observations before binding, after binding and after semantic comparison
withhold evidence on expiry, cancellation or clock failure. Regression and invalid
elapsed time terminate as ClockInvalid. Custom clock exceptions are not forwarded.
Reentrant abandonment at a clock boundary cannot resurrect a terminal attempt.
Observed cancellation throws after releasing ready/active metadata. Stop/Dispose
also support abandonment after parsing, encoding or transport failure; none of
those operations is owned by this attempt.

Snapshots contain scalar state, candidate/handled counts, remaining duration and
top-level, binding and synchronization issue flags. Terminal remaining time is
zero; snapshots contain no identity, response or recovery references.

## Verification and next increment

Twenty-one independent Python traces and 228 checks cover both profiles, mandatory
one-time handoff, scoped review and bank abort, foreign scope retention, bounded
and maximum message recovery, invalid recovery inputs, prior-dialogue reuse,
duplicate reports, deadline equality/nonrenewal, cancellation, disposal and clock
regression. Additional checks cover exact reference ownership, recovery replacement
rejection, late failures at every processing boundary, reentrant abandonment,
scalar snapshots and concurrent one-time handoff. All data are synthetic.

Next is credential-aware PIN/TAN dialogue-end request context and encoding, needed
before an assembled closing lifecycle can consume this handoff. The existing
unsigned closing attempt does not encode or validate assembled signed closing.
Procedure/challenge integration, secure input, authenticated transport and live
acceptance remain pending. The host stays inert.
