# ADR 0046: PIN/TAN closing attempt lifecycle

- **Status:** Accepted (local synthetic lifecycle only)
- **Date:** 2026-09-09
- **Related:** [Synchronization attempt](0043-pin-tan-synchronization-attempt-lifecycle.md),
  [closing candidate](0044-pin-tan-dialogue-end-context-encoding.md),
  [closing response semantics](0045-pin-tan-closing-response-binding.md)

## Ownership and phase boundary

`FinTsPinTanDialogueEndAttempt` retains one immutable closing candidate with an
absolute deadline. Start records metadata only; the candidate factory has already
required matching synchronization and closing context. The attempt takes no PIN
owner or encoded output and proves no encoding, transmission or receipt.

The caller can construct the candidate from the exact evidence returned by the
synchronization attempt. Closing retains that provenance through the candidate,
without resurrecting or mutating the completed synchronization phase. The phases
have separate explicit lifetimes. There is no combined deadline, background task
or automatic dispatch between them, and no cross-instance replay registry.

## Response handling

Start moves Ready to AwaitingResponse. A second start cannot replace the candidate
or restart its deadline. Responses before start yield WrongState. The attempt
recomputes ADR 0045 reference binding and semantics internally from the supplied
response; callers cannot inject precomputed success.

Foreign dialogue/message scope and absent or unknown references yield
ContextMismatch with no evidence or handled count. The exact original candidate
remains pending and its deadline is unchanged. Only scalar binding issues are
retained. A later matching response clears transient mismatch diagnostics.

A scoped response returns caller-owned evidence exactly once. Qualified closure
ends as ReinitializationRequired; qualified abort ends as Aborted and must not
trigger another closing request. All scoped unresolved outcomes end as NeedsReview,
including mapped signature/wrapper success, foreign envelope identity/profile,
missing envelope, unexpected data and ambiguous termination. Raw termination flags
cannot override the qualified outcome. ResponsesHandled becomes one for closure,
abort or review and does not imply successful closure or authentication.

All operations are serialized with one lock. Terminal state is immutable. Repeated,
reparsed or concurrent submissions cannot publish another evidence object. Closure,
abort, review, cancellation, stop, timeout, invalid clock and disposal all release
the pending candidate and its retained synchronization/recovery metadata. Terminal
evidence is not stored in the attempt; the caller's returned evidence survives
cleanup and remains its responsibility.

## Time, cancellation and diagnostics

The caller selects a positive lifetime up to fifteen minutes. Start begins an
absolute deadline; equality is expired. Snapshots, foreign responses and another
start cannot renew it. Expiry is observed on API calls without a background timer.

Clock observations before binding, after binding and after semantic comparison
withhold evidence on expiry or clock failure. Cancellation is checked at the same
boundaries and within comparison, throws after releasing active metadata, and
does not replace an already terminal state. The same failure handling applies to
both closure and abort replies. Clock regression, negative elapsed time or custom
clock failures yield ClockInvalid with no forwarded exception details. Reentrant
abandonment at a clock callback cannot revive an attempt or publish evidence.

Stop/Dispose support abandonment after parse, encoding or transport failure;
none of those operations is owned by the attempt. Snapshots expose only state,
candidate/handled counts, remaining duration and fixed semantic/binding flags.
Terminal remaining duration is zero; no identity or response object is exposed.

## Verification and next work

Twenty independent Python traces and 260 checks cover both profiles, escaped and
recovery contexts, one-time closure/abort/review handoff, foreign scope retention,
nonrenewal, deadline equality, cancellation, stop/disposal and clock regression.
Additional checks exercise late expiry/cancellation/clock faults around both closure
and abort handling, constructor failures, reentrant abandonment, scalar snapshots,
exact metadata retention/release, concurrent one-time handoff and the real
synchronization-to-closing evidence path. All values are synthetic.

Next is assembled initialization TAN-procedure and permission response integration,
preserving exact response provenance and unresolved observations without procedure
activation. Broader session coordination, secure input, authenticated transport,
live acceptance and recovery application remain pending. Reported closure still
requires fresh initialization; the host remains inert.
