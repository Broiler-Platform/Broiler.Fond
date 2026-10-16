# ADR 0078: Read-session diagnostic lifecycle

- **Status:** Accepted (local lifecycle correction)
- **Date:** 2026-10-15
- **Related:** [Read sessions](0072-assembled-read-session-attempt.md),
  [evidence provenance](0076-read-session-evidence-provenance.md),
  [operation scope](0077-read-session-operation-scope.md)

## Resolved retries and current response evidence

Read-session diagnostics now distinguish a transient rejection from an operation
outcome. Previously, a valid balance response could retain binding, replay or
provenance errors from an earlier rejected proposal. A closing response needing
review could likewise inherit unrelated mismatch flags. Conversely, a clean or
aborted closing response cleared the page-limit flag, losing the fact that the
read had been truncated.

An accepted operation, continuation or closing candidate clears resolved binding,
dialogue, counter, replay, provenance and operation-mismatch diagnostics before
recording the candidate. A response that reaches semantic publication does the
same before installing its current evidence diagnostics. The clock and
cancellation checks still precede response publication.

This applies to both matching and review outcomes. Review replaces the earlier
foreign-response diagnostic with the issue from the actual response now being
evaluated. Genuine current binding errors, including profile mismatches, remain
visible. Discovery also copies its response-level read issues into the session's
`ReadIssues` field, keeping scalar diagnostics consistent with the returned
`AllAccountEvidence.ResponseIssues`.

A foreign response still leaves its mismatch visible and retains the pending
candidate, counters and deadline. A wrong-state call, terminal replay or snapshot
does not clear unresolved diagnostics. Clearing occurs when a candidate is
accepted or an in-scope response is published, not merely when another API is
called. The snapshot is current diagnostic state, not a historical error log.

## Preserve truncation across dialogue termination

`PageLimitReached` is retained through closing, whether the bank reports clean
closure, abort or a response needing review. Closing can succeed while a preceding
read remains incomplete. The final state describes the dialogue outcome; the
page-limit issue describes the operation's truncation. A closing review may carry
both `ClosingNeedsReview` and `PageLimitReached`.

This change does not reinterpret bank replies, alter response-count semantics,
change the page budget, renew deadlines or mutate caller-owned results. Terminal
cleanup still releases session references while preserving scalar diagnostics.

## Verification and next work

304 additional executable checks reuse existing synthetic fixtures across
initialization, discovery, first balance read, continuation and closing. They
exercise mismatches followed by execution, partial, unavailable and review
outcomes; rejected candidates followed by valid candidates; unresolved errors
across wrong-state calls; exact current binding and semantic details; terminal
replay; and truncated reads followed by closure, abort or closing review.
Caller-owned partial data and terminal reference cleanup remain verified.

The tests reproduced stale diagnostics and lost truncation flags before the fix.
Full Windows CI passed with zero build warnings or errors, including formatting,
the inert host smoke test, all existing suites and the 304 new checks. No wire
fixture or generator change is required.

Next is initialization result handoffs across all supported read-session
initialization overloads under M1-05. Authentication, activation and transport
remain pending.
