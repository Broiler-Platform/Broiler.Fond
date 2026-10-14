# ADR 0057: Bounded assembled first-read attempt

- **Status:** Accepted (local synthetic lifecycle only)
- **Date:** 2026-09-10
- **Related:** [Assembled initialization attempt](0042-pin-tan-initialization-attempt-lifecycle.md),
  [assembled first-read references](0055-first-read-response-reference-binding.md),
  [assembled read semantics](0056-assembled-first-read-response-semantics.md)

## Candidate ownership and deadline

`FinTsPinTanReadAttempt` owns one immutable first-read candidate and one absolute
monotonic deadline. Start records metadata only; it accepts no credentials or
encoded bytes and proves neither encoding nor submission. A second Start cannot
replace the candidate or restart the deadline. The caller supplies a positive
lifetime bounded to fifteen minutes and an optional TimeProvider.

The attempt begins Ready with no active deadline. A recorded candidate enters
AwaitingResponse. ExecutionReported, PartialReported, UnavailableReported,
NeedsReview, Cancelled, TimedOut, Stopped and ClockInvalid are terminal. Every
terminal transition releases the pending candidate reference. Terminal calls
cannot restart the attempt, repeat evidence handoff or change an earlier outcome.

Time is observed on API calls, not by a background timer. Equality with the
deadline expires the attempt. Clock regression, negative elapsed time or clock
exceptions terminate conservatively. Invalid clock construction uses fixed
diagnostics without forwarding exception details. Cancellation, Stop and Dispose
release pending metadata; they do not dispose caller-owned objects or credentials.

## Response handling

AcceptResponse takes an already parsed FinTsReadDataSet. It recomputes reference
binding against the pinned candidate and that data set's exact Source, then
recomputes ADR 0056 semantics. Callers cannot inject an unrelated precomputed
binding or semantic success. Parsing remains outside the attempt; callers abandon
with Stop after a parsing or transport failure. No retry or sending is scheduled.

Message mismatches, missing/unknown references and wrong operation/segment roles
return ContextMismatch without consuming the candidate or handing out evidence.
The original deadline remains active. Only scalar mismatch flags are retained,
and a later scoped response replaces those transient flags with its diagnostics.

Scoped responses are handled once, including missing/wrong envelope profiles,
foreign envelope identity, invalid account/version/currency evidence, unknown
data and unsupported statuses. Semantic review consumes that response and
terminates NeedsReview. Security/framing success replies remain review evidence;
they cannot substitute for read execution.

A matching response terminates with its reported execution, partial or unavailable
outcome. All three return the exact semantic evidence once. PartialReported ends
this first-read attempt and releases pending metadata; the raw token belongs only
to the returned caller-owned evidence. No continuation request, page counter,
polling schedule or automatic retry is created. An unavailable report retains its
absent/empty source shape and does not become a zero balance.

The attempt records ResponsesHandled only when it publishes evidence. A lock
serializes public operations, so concurrent submissions produce at most one
handoff. Reparsed copies after completion receive no evidence. This is replay
protection within one attempt only, not durable or cross-attempt replay tracking.

## Publication and cleanup

Availability, clock and cancellation checks surround binding, semantic evaluation
and final publication. Late timeout, clock failure, cancellation or reentrant
abandonment withholds evidence, leaves ResponsesHandled at zero and releases the
pending candidate. Reentrant abandonment during Start cannot resurrect state.

Snapshots contain only state, candidate/response counts, remaining time and scalar
binding/read issue flags. They expose no account, response, candidate, token or
credential object. The attempt never stores the returned binding, response or
semantic evidence; the caller controls the lifetime of handed-out private data.
Successful handoff authenticates nothing and proves no TAN/SCA completion,
freshness, capability activation or account ingestion. The host stays inert.

## Verification and next work

Twenty-seven independent Python traces and 544 checks cover read versions and
profiles, escaped dialogue, all four handoff categories, mismatches followed by
valid data, fixed-deadline behavior, second starts, cancellation, disposal and
regression. All fifty-nine existing semantic fixtures also run through the actual
attempt to verify source-preserving handoff or pending mismatch behavior.

Failure injection covers each of the three active processing boundaries for
execution, partial, unavailable and review responses: expiry, token cancellation,
clock exception/regression and reentrant Stop, Dispose and Cancel. Additional
checks cover both Start clock boundaries, cancellation before work, invalid
lifetimes/clocks, null inputs, transient diagnostic reset, immutable terminal
outcomes, concurrent submissions and scalar-only snapshots. Tests inspect the
actual pending field to verify cleanup on every terminal path.

Next is assembled balance-continuation context from qualified partial-read
evidence, preserving original request/account/options and sequential counters.
Continuation encoding, TAN/challenge integration, broader session coordination,
secure input, authenticated transport and live qualification remain pending.
