# ADR 0077: Read-session operation scope and page budgets

- **Status:** Accepted (local lifecycle correction)
- **Date:** 2026-10-15
- **Related:** [Read sessions](0072-assembled-read-session-attempt.md),
  [terminal ownership](0075-session-terminal-evidence-ownership.md),
  [evidence provenance](0076-read-session-evidence-provenance.md)

## One operation per initialized session

The existing first-read signature context qualifies only the immediate request
and response at message 2. The session now explicitly supports either one
all-account discovery operation or one balance operation with continuation pages.
Both initial-operation methods require `DialogueOpen`. Once an operation reports
execution, unavailability or a page-limit stop, only dialogue closing remains
available. Direct closing without an operation also remains supported.

Previously the coordinator admitted initial-operation candidates in
`OperationCompleted`, despite the signature context's message-2 restriction.
The state guard now rejects these calls as `WrongState` before candidate or replay
checks. It preserves accepted evidence, counters, deadline and accumulated data.
A subsequent operation requires a fresh initialized session. No later-message
signature API, operation reset or automatic scheduling is introduced.

`RecordBalanceFirstReadCandidate` also requires `HKSAL`. A selected-account
`HKSPA` request can be valid as a generic first-read candidate, but it cannot enter
the balance pipeline. It returns `ContextMismatch` with scalar `OperationMismatch`
diagnostics without occupying the pending slot or consuming any page budget.
The all-account binding already enforces the discovery request shape.

This one-operation scope makes balance aggregation and page accounting belong to
one read. Discovery consumes no balance pages. Recording a continuation consumes
no page; accepting its matching response increments the count exactly once.
Replayed responses cannot add balances or pages, and rejected restarts cannot
reset the budget. Counts remain available after terminal reference cleanup, while
earlier caller-owned result snapshots retain their original contents.

## Distinguish truncation from completion

A partial response at the configured page limit now returns the dedicated
`PageLimitReached` transition, appended to the transition enum to preserve existing
numeric values. Previously this path returned `ExecutionReported`, making a local
budget stop appear to be a completed operation.

The coordinator retains accepted partial data, enters the `Closing` stage, and
exposes no actionable continuation token. No further operation or continuation
can be recorded. The caller can close the dialogue or abandon the session.
Matching page evidence remains available in the returned result; callers must
inspect its transition to distinguish partial data from completion.

Execution and unavailable outcomes at the exact limit retain their respective
transitions. They are not truncation merely because the last accepted page uses
the entire budget. The absolute session deadline is unchanged.

## Verification and next work

New executable tests cover valid selected-account discovery presented to the
balance method, completed and unavailable discovery and balance operations,
completion on a final continuation at the limit, replay accounting, stable caller
snapshots and page budgets of 1, 2, 3 and 128. Every truncated sequence records and
accepts a closing exchange and verifies terminal ownership and retained counts.
The regression suite failed before the changes.

Full Windows CI passed with 857 new checks, alongside the existing 3,610
read-session and 117 provenance checks, with zero build warnings or errors.
All 64 fixtures reproduced byte-for-byte in an isolated generator run.

Two expectations in the existing session fixture are updated: partial page-limit
results now report `PageLimitReached`, and an initial read after completion reports
`WrongState`. The generator retains all 28 session traces and 64 fixture files.

Next is read-session transient diagnostics and response outcome consistency under
M1-05. Authentication, activation and transport remain pending.
