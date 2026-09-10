# ADR 0042: Assembled PIN/TAN initialization attempt lifecycle

- **Status:** Accepted (local synthetic lifecycle only)
- **Date:** 2026-09-09
- **Related:** [Unsigned initialization attempt](0027-in-memory-initialization-attempt.md),
  [assembled reference binding](0039-pin-tan-assembled-request-reference-binding.md),
  [initialization semantics](0040-pin-tan-initialization-response-semantics.md)

## Scope and ownership

`FinTsPinTanInitializationAttempt` owns one pending assembled initialization
candidate and an absolute local deadline. It records metadata, not transmission:
`Start` neither invokes the encoder nor proves that a bank received a request.
No credentials or encoded output buffers enter this API. The caller remains
responsible for their separate disposal and clearing.

The attempt retains the exact immutable candidate, including its pinned user and
signature/procedure evidence. It recomputes reference binding from the incoming
parameter set's own source and evaluates semantics internally. Callers cannot
inject a precomputed success result or replace the candidate after starting.
All operations are serialized through one lock.

## States and response handling

The existing initialization state/transition vocabulary is reused. A valid start
moves Ready to AwaitingResponse and records one candidate. A synchronization
candidate instead terminates as NeedsReview without being retained or counted.
Responses before start yield WrongState; restarts while awaiting a response cannot
replace the pending candidate or reset its deadline.

Missing, foreign or wrong-role segment references and outer message-reference
mismatches return ContextMismatch without evidence and leave the original candidate
pending. Only scalar binding issues are retained. No response is counted and no
retry or send operation is scheduled. Later matching scope clears transient issues.

A scoped response is evaluated with ADR 0040's semantics. Matching observations
end as ExecutionReported; semantic, envelope/profile, invalid-assigned-dialogue or
uninterpreted-data issues end as NeedsReview. Either scoped outcome returns one
caller-owned evidence object and increments ResponsesHandled to one. This counter
includes review handoffs and does not imply execution or authentication.

Terminal state is immutable. Repeated, reparsed or concurrent submissions cannot
produce another evidence handoff. Completion, review, cancellation, stop, timeout,
clock invalidation and disposal all release the pending candidate reference. The
attempt stores no terminal evidence; a handed-out result remains owned by its
caller and survives cleanup. These guarantees apply to one attempt instance, not
to a bank session or a cross-instance replay registry.

## Deadline, cancellation and clock failures

The caller supplies a positive lifetime no greater than fifteen minutes. The
deadline begins at Start and is never renewed by snapshots, another start or
foreign responses. Equality with the deadline is expired. No background timer is
created; idle expiry is observed on the next API call.

Clock observations occur before response work, after binding and after semantic
comparison, before handoff. Expiry at any boundary withholds evidence, leaves
ResponsesHandled at zero and releases pending context. Clock regression, negative
elapsed time or clock exceptions terminate as ClockInvalid. Clock constructor
validation and runtime failures expose fixed diagnostics without forwarding custom
clock exception content. Reentrant stop/cancel during a clock callback cannot
resurrect a terminal attempt or publish evidence afterwards.

Start and AcceptResponse accept cancellation tokens. Observed cancellation throws
after releasing ready/active context as Cancelled; cancellation during comparison
also withholds evidence. Explicit Cancel/Stop and Dispose provide local abandonment
without transport work. An already-observed timeout or other terminal state is not
overwritten by later cancellation, stop or disposal. Cancellation after a successful
handoff cannot revoke caller-owned evidence.

Callers should Stop/Dispose after parse failure, disconnect or abandoned encoding/
transport work. The attempt takes already parsed parameter evidence; it owns no
parser, socket, credential buffer, background task or bank dialogue.

## Diagnostics and verification

Snapshots contain only state, candidate/handled counts, remaining duration and
fixed top-level/binding/parameter issue flags. They expose no candidate, response,
user, customer, system, dialogue or credential values. Terminal remaining time is
zero. This scalar view neither exports logs nor authorizes use of report data.

Thirteen independent Python traces and 156 checks cover both profiles, one-time
execution/review handoff, foreign scope retention, cancellation, stop, disposal,
deadline equality, nonrenewal, clock regression and wrong candidate kind. Additional
checks exercise late expiry/cancellation/clock faults around every processing
boundary, reentrant abandonment, constructor failures, exact retained references,
scalar diagnostics and concurrent one-time handoff. All values are synthetic.

Next is a bounded assembled PIN/TAN synchronization attempt with an explicit
mandatory closing/reinitialization handoff. Closing integration, procedure/challenge
checks, secure input, authenticated transport and live acceptance remain pending.
The host stays inert and no execution report activates a session.
