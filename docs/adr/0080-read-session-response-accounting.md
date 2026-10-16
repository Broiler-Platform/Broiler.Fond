# ADR 0080: Read-session response accounting across review and terminal outcomes

- **Status:** Accepted (consistent response accounting across review and terminal outcomes)
- **Date:** 2026-10-15
- **Related:** [Read sessions](0072-assembled-read-session-attempt.md),
  [synchronization sessions](0073-assembled-synchronization-session-attempt.md),
  [terminal ownership](0075-session-terminal-evidence-ownership.md),
  [transient diagnostics and outcomes](0078-read-session-diagnostic-lifecycle.md),
  [initialization handoffs](0079-read-session-initialization-handoffs.md)

## Uniform response accounting across evaluated outcomes

The FinTS 3.0 PIN/TAN read session attempt coordinator (`FinTsPinTanReadSessionAttempt`)
tracks progress via `ResponsesHandled` alongside sequential client and expected bank message
numbers (`lastClientNumber + 1`, `lastBankNumber + 1`). Prior to this increment,
in-scope bank responses that evaluated to semantic review (`RejectedForReview` / `NeedsReview`)
or terminal abort on close (`AbortReported` / `Aborted`) did not increment `ResponsesHandled`
or advance `_lastBankNumber` across several stage entry points. This created an inconsistency
with `FinTsPinTanSynchronizationSessionAttempt` (which already increments `_responsesHandled`
on review and abort) and left `ResponsesHandled` undercounting the actual number of in-scope
responses evaluated by the attempt coordinator.

`FinTsPinTanReadSessionAttempt` now advances `ResponsesHandled` and updates sequential
message counters for every evaluated in-scope response across all dialogue stages:

| Stage and transition | Semantic outcome | Handled count | Counter update |
| --- | --- | --- | --- |
| Initialization: `DialogueOpen` | Accepted | 0 -> 1 | client: 1, bank: 1 |
| Initialization: `RejectedForReview` | Needs review (terminal) | 0 -> 1 | client: 1, bank: 1 |
| All-Account: `ExecutionReported` | Accepted | 1 -> 2 | client: 2, bank: 2 |
| All-Account: `RejectedForReview` | Needs review (terminal) | 1 -> 2 | client: 2, bank: 2 |
| Balance first-read: `ExecutionReported` / `PartialAccepted` | Accepted | 1 -> 2 | client: 2, bank: 2 |
| Balance first-read: `RejectedForReview` | Needs review (terminal) | 1 -> 2 | client: 2, bank: 2 |
| Balance continuation: `ExecutionReported` / `PartialAccepted` | Accepted | $n \to n+1$ | client: $n+1$, bank: $n+1$ |
| Balance continuation: `RejectedForReview` | Needs review (terminal) | $n \to n+1$ | client: $n+1$, bank: $n+1$ |
| Closing: `Closed` | Clean close (terminal) | $n \to n+1$ | client: $n+1$, bank: $n+1$ |
| Closing: `AbortReported` | Bank abort (terminal) | $n \to n+1$ | client: $n+1$, bank: $n+1$ |
| Closing: `RejectedForReview` | Needs review (terminal) | $n \to n+1$ | client: $n+1$, bank: $n+1$ |

In all cases, evaluating an in-scope response consumes the active candidate, marks
the bank response as handled, and updates sequential counters so subsequent replays
are rejected under monotonic message counter rules.

## Invariance boundaries and monotonicity

The response accounting contract defines strict invariance rules where `ResponsesHandled`
and sequential counters MUST NOT advance:

1. **Foreign responses (`ContextMismatch`):** Responses with mismatched dialogue IDs,
   incompatible envelope framing, unknown segment references, or segment role mismatches
   are not evaluated as matching the pending candidate. `ResponsesHandled`, message counters,
   and active candidate metadata remain unchanged, allowing a valid response to be retried.
2. **Replayed responses (`ReplayRejected`):** Incoming bank responses with message numbers
   less than or equal to `_lastBankNumber` are rejected as duplicate replays and do not
   increment `ResponsesHandled`.
3. **Wrong-state calls (`WrongState`) and terminal replays (`Terminal`):** Calling an
   entry point out of stage order or after a terminal transition preserves `ResponsesHandled`
   and message counters unchanged.
4. **Pre-publication interruptions:** If cancellation is requested or the monotonic
   deadline expires before response evaluation publishes its outcome, the attempt fails
   closed without incrementing `ResponsesHandled`.

Across all stages, transitions, and snapshot observations, the progress monotonicity
invariant `ResponsesHandled <= RequestsRecorded` is strictly maintained. Concurrency
guarantees ensure that 16 parallel threads racing to submit a review or closing response
yield exactly one successful transition with an increment of 1, while all competing
threads receive `Terminal` without inflating `ResponsesHandled`.

## Verification and fixture alignment

Synthetic session traces in `pin-tan-read-session-attempt-v1.json` and the generator
`eng/Generate-FinTsVectors.py` are aligned to reflect handled count increments on
review outcomes:
- Trace 8 (`session-initialization-review`): `handled` increments from 0 to 1.
- Trace 9 (`session-all-account-review`): `handled` increments from 1 to 2.
- Trace 10 (`session-balance-read-review`): `handled` increments from 1 to 2.
- Trace 11 (`session-closing-review`): `handled` increments from 2 to 3.

All 64 synthetic FinTS fixtures reproduce byte-for-byte from `Generate-FinTsVectors.py`.
138 additional executable checks in `FinTsPinTanReadSessionAccountingTests.cs` verify
initialization accounting across all four overloads, operational review, continuation
review, clean/abort/review closing, foreign and replay invariance, 16-thread concurrent
submissions, and monotonicity across all 28 session attempt traces.

No runtime package dependencies are added; platform-neutral BCL net10.0 only.
Session activation, credential persistence, and live transport remain disabled.
