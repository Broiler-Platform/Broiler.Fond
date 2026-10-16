# ADR 0081: Synchronization-session counter tracking, diagnostic lifecycle, and result handoffs

- **Status:** Accepted (symmetrical counter tracking, diagnostic clearing, and result handoffs)
- **Date:** 2026-10-15
- **Related:** [Synchronization sessions](0073-assembled-synchronization-session-attempt.md),
  [terminal ownership](0075-session-terminal-evidence-ownership.md),
  [read session diagnostics](0078-read-session-diagnostic-lifecycle.md),
  [read session accounting](0080-read-session-response-accounting.md)

## Context and architectural requirement

FinTS 3.0 PIN/TAN dialogue coordinators must maintain symmetrical lifecycle,
accounting, and diagnostic guarantees across operational read dialogues
(`FinTsPinTanReadSessionAttempt`) and synchronization dialogues
(`FinTsPinTanSynchronizationSessionAttempt`).

ADR 0078 and ADR 0080 established for read sessions that:
1. Sequential bank message counters advance when an in-scope response is evaluated,
   including bank abort (`9800`) and semantic review outcomes (`RejectedForReview`).
2. Resolved transient candidate errors (such as dialogue mismatches or replay rejections)
   and foreign response binding errors are cleared when a valid candidate is recorded or
   an in-scope response is evaluated.
3. Callers receive an explicit `HasMatchingEvidence` property on session results to determine
   whether clean evidence was established without inspecting internal outcome enums.

Prior to this increment, `FinTsPinTanSynchronizationSessionAttempt` exhibited several
omissions:
- Bank abort (`9800`) or review (`!evidence.HasMatchingEvidence`) on the initial synchronization
  response left `_lastBankNumber` at 0 rather than advancing it to 1, leaving the snapshot
  counter un-incremented despite handling the response.
- Bank abort or review on the dialogue closing response left `_lastBankNumber` at 1 rather than 2.
- Transient candidate issues (`DialogueIdMismatch`, `ClosingBindingNeedsReview`) set by rejected
  candidate attempts were not cleared upon subsequent recording of an accepted closing candidate.
- Transient response issues (`ClosingBindingNeedsReview`) set by foreign closing responses were
  not cleared when an in-scope closing response was subsequently evaluated.
- `FinTsPinTanSynchronizationSessionResult` lacked the public `HasMatchingEvidence` helper property
  present on `FinTsPinTanReadSessionResult`.
- The active dialogue identifier was not accessible via a public `DialogueId` property during
  the open dialogue stages.

`FinTsPinTanSynchronizationSessionAttempt` now aligns these behaviors with the core session
lifecycle contract.

## Counter tracking and dialogue identifier lifecycle

Sequential message counters advance deterministically for every in-scope response evaluated
across both synchronization and closing stages:

| Stage and transition | Semantic outcome | Handled count | Counter update |
| --- | --- | --- | --- |
| Synchronization: `CloseRequired` | Clean sync | 0 -> 1 | client: 1, bank: 1 |
| Synchronization: `AbortReported` | Bank abort (terminal) | 0 -> 1 | client: 1, bank: 1 |
| Synchronization: `RejectedForReview` | Needs review (terminal) | 0 -> 1 | client: 1, bank: 1 |
| Closing: `ReinitializationRequired` | Clean close (terminal) | 1 -> 2 | client: 2, bank: 2 |
| Closing: `AbortReported` | Bank abort (terminal) | 1 -> 2 | client: 2, bank: 2 |
| Closing: `RejectedForReview` | Needs review (terminal) | 1 -> 2 | client: 2, bank: 2 |

Dialogue identifier lifecycle:
- Upon receiving a matching synchronization response, `_dialogueId` is pinned from the response
  frame and exposed to callers via `public string? DialogueId`.
- During active dialogue stages (`AwaitingCloseRequest`, `AwaitingCloseResponse`), `DialogueId`
  returns the pinned bank dialogue identifier.
- Upon any terminal outcome (`ReinitializationRequired`, `Aborted`, `NeedsReview`, `Cancelled`,
  `TimedOut`, `Stopped`, `ClockInvalid`), ADR 0075 terminal ownership rules release the internal
  reference (`_dialogueId = null`). Callers access the reported dialogue identifier through the
  handed-out `FinTsPinTanSynchronizationEvidence.ReportedDialogueId` on the result.

## Transient diagnostic lifecycle

Diagnostic state in `FinTsPinTanSynchronizationSessionAttempt` distinguishes between persistent
semantic outcomes and transient retry issues:

1. **Candidate retry clearing:** When a candidate is rejected due to `DialogueIdMismatch` or
   `MessageNumberMismatch`, the coordinator records the transient issue and preserves the active
   stage. When a valid closing candidate is subsequently recorded, these transient issues and any
   transient closing binding issues are cleared (`_issues = None; _closingBindingIssues = None;`).
2. **Response retry clearing:** When a foreign response is presented during the closing stage,
   the coordinator records `ClosingBindingNeedsReview` without consuming the candidate or deadline.
   When an in-scope closing response is subsequently evaluated, transient issues are cleared on
   clean closure (`_issues = None;`) or replaced with the authoritative terminal status
   (`AbortReported` or `ClosingStatusNeedsReview`).

## Result handoff property

`FinTsPinTanSynchronizationSessionResult` now provides the uniform public property:

```csharp
public bool HasMatchingEvidence =>
    ClosingEvidence is not null
        ? ClosingEvidence.Outcome == FinTsDialogueEndOutcome.ClosureReported
        : (SynchronizationEvidence?.HasMatchingEvidence ?? false);
```

This guarantees callers have an identical evidence qualification contract across both
`FinTsPinTanReadSessionResult` and `FinTsPinTanSynchronizationSessionResult`.

## Monotonic progress and concurrency atomicity

- **Progress Monotonicity:** Across all 24 synchronization session vectors and all terminal paths,
  `ResponsesHandled <= RequestsRecorded` is strictly maintained.
- **Concurrency Atomicity:** 16 concurrent threads racing to submit responses on synchronization
  abort, synchronization review, closing abort, or closing review yield exactly one thread with the
  authoritative semantic transition and handled count increment, while all 15 competing threads
  receive `Terminal` without inflating handled counters or corrupting message sequence numbers.

## Verification

70 executable checks in `FinTsPinTanSynchronizationSessionDiagnosticTests.cs` verify:
- Bank abort on synchronization (`9800`) advancing handled count to 1 and bank message number to 1.
- Scoped review on synchronization advancing handled count to 1 and bank message number to 1.
- Bank abort on closing (`9800`) advancing handled count to 2 and bank message number to 2.
- Scoped review on closing advancing handled count to 2 and bank message number to 2.
- Clean closing advancing handled count to 2, bank message number to 2, and clearing transient issues.
- Candidate retry clearing `DialogueIdMismatch` upon valid closing candidate submission.
- Active `DialogueId` availability during `AwaitingCloseRequest` and terminal release.
- 16-thread concurrency atomicity across all terminal paths.
- Progress monotonicity invariant across all 24 session vectors.

No runtime package dependencies are added; platform-neutral BCL net10.0 only.
Session activation, credential persistence, and live transport remain disabled.
