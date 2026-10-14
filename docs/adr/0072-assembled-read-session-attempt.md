# ADR 0072: Bounded assembled PIN/TAN read session attempt coordinator

- **Status:** Accepted (local synthetic lifecycle only)
- **Date:** 2026-10-14
- **Related:** [Bounded assembled all-account discovery attempt](0071-assembled-all-account-discovery-attempt.md),
  [Bounded assembled PIN/TAN read refresh attempt](0067-assembled-read-refresh-attempt.md),
  [Assembled PIN/TAN closing attempt](0062-pin-tan-closing-attempt.md),
  [Assembled PIN/TAN initialization attempt](0059-pin-tan-initialization-attempt.md)

## Session architecture and multi-stage coordination

`FinTsPinTanReadSessionAttempt` coordinates the full lifecycle of an assembled FinTS 3.0 PIN/TAN dialogue across three sequential stages:
1. **Dialogue Initialization (`Initialization` stage):**
   - Initiated via `StartInitialization(candidate)` with an assembled initialization request candidate (`FinTsPinTanRequestBinding`, client message 1).
   - Validated via `AcceptInitializationResponse` with BPD/UPD parameters, TAN procedure advertisement (`FinTsTanParameterSet`), permissions (`FinTsPermittedProcedureSet`), PIN/TAN method parameters (`FinTsPinTanParameterSet`), and optional read parameters (`FinTsReadParameterSet`).
   - On matching execution evidence, captures and pins the bank-assigned `_dialogueId`, bank message counter, and initialization evidence, transitioning to `DialogueOpen` in the `Operation` stage.
2. **Read Operation (`Operation` stage):**
   - Supports all-account discovery (`RecordAllAccountCandidate` + `AcceptAllAccountResponse` with `FinTsPinTanAllAccountRequestBinding` / `FinTsReadDataSet`).
   - Supports balance refresh (`RecordBalanceFirstReadCandidate` + `AcceptBalanceResponse` with `FinTsPinTanReadRequestBinding`, followed by zero or more continuation pages via `RecordBalanceContinuationCandidate` + `AcceptBalanceResponse` up to a bounded maximum page limit).
   - Supports direct transition to closing without operational read requests when only dialogue synchronization or initialization check is required.
   - On clean execution or unavailable reporting, transitions to `OperationCompleted` (or `PageLimitReached`) in the `Closing` stage, aggregating balance reports or discovered accounts.
3. **Dialogue Termination / Closing (`Closing` stage):**
   - Records an assembled dialogue termination candidate (`RecordClosingCandidate` with `FinTsPinTanDialogueEndRequestBinding`), requiring sequential client message and expected bank message numbers.
   - Evaluates bank termination response (`AcceptClosingResponse` with `FinTsResponse`).
   - Clean closure reports (`0100`) transition to `Closed` in the `Terminal` stage; bank abort reports (`9800`) transition to `Aborted`.

## Single monotonic deadline and lifecycle enforcement

The attempt enforces a single absolute monotonic deadline across all stages, configured by the caller (bounded up to fifteen minutes) and evaluated using an optional `TimeProvider`.
- The monotonic deadline begins upon `StartInitialization`. Time consumed during initialization reduces the remaining time available for subsequent operational and closing exchanges. Operational requests never extend, refresh, or reset the deadline.
- When elapsed time reaches or exceeds the lifetime limit, the coordinator transitions immediately to `TimedOut` in the `Terminal` stage.
- Clock regression, negative elapsed time, or clock exceptions fail closed to `ClockInvalid`.
- Caller cancellation via `CancellationToken`, `Cancel()`, `Stop()`, or `Dispose()` transitions to `Cancelled` or `Stopped`, releasing all pending context without executing network operations or altering caller objects.

## Replay defense and dialogue pinning

The coordinator enforces strict protocol defenses against sequence corruption, message injection, and replay:
- **Dialogue ID Pinning:** Once established in initialization, every subsequent operational and closing candidate must match `_dialogueId`. Any candidate specifying a mismatched dialogue ID is rejected with `ContextMismatch` and flagged with `FinTsPinTanReadSessionIssue.DialogueIdMismatch`.
- **Monotonic Message Counters:** Sequential client message numbers (`_lastClientNumber + 1`) and expected bank message numbers (`_lastBankNumber + 1`) are verified on every recorded candidate. Replay of already observed or decreasing message numbers returns `ReplayRejected` with `FinTsPinTanReadSessionIssue.ReplayDetected`. Gaps or counter mismatches return `ContextMismatch` with `FinTsPinTanReadSessionIssue.MessageNumberMismatch`.
- **Response Replay Immunity:** Incoming bank responses with message numbers less than or equal to `_lastBankNumber` within the dialogue are rejected with `ReplayRejected` without modifying active state or consuming pending candidates.
- **Transient Context Mismatch Segregation:** Foreign-scope response issues (`MessageMismatch`, `MissingSegmentReference`, `UnknownSegmentReference`, or `SegmentRoleMismatch`) return `ContextMismatch` without consuming the pending candidate or advancing `ResponsesHandled`. The active candidate remains pinned under its original deadline, and subsequent valid responses clear transient mismatch diagnostics.

## Caller-owned evidence and terminal cleanup

Public methods are synchronized via a monitor lock, ensuring thread-safe execution and atomic state transitions under concurrent submissions.
- Every terminal transition (`Closed`, `Aborted`, `NeedsReview`, `Cancelled`, `TimedOut`, `Stopped`, `ClockInvalid`) immediately zeros all internal pending candidate references (`_pendingInitialization`, `_pendingAllAccount`, `_pendingBalanceFirstRead`, `_pendingBalanceContinuation`, `_pendingClosing`).
- Terminal outcomes expose caller-owned, immutable evidence containers: `RequirementsEvidence`, `ProcedureEvidence`, `InitializationEvidence`, `AllAccountEvidence`, `BalanceFirstPageEvidence`, `BalanceContinuationPages`, `AggregatedBalances`, and `ClosingEvidence`.
- `FinTsPinTanReadSessionSnapshot` exposes strictly scalar diagnostics: `State`, `Stage`, `RequestsRecorded`, `ResponsesHandled`, `ClientMessageNumber`, `BankMessageNumber`, `PagesAccepted`, `BalancesReported`, `AccountsDiscovered`, `RemainingTime`, and scalar diagnostic issue flags. Snapshots contain no credentials, PINs, TANs, IBANs, amounts, tokens, or raw wire payloads.
- Successful dialogue completion neither authorizes financial transfers nor activates live background polling. The kernel remains inert and purely user-operated.

## Verification

`FinTsPinTanReadSessionAttemptTests` verifies 28 independent Python-generated synthetic traces and 1,230 checks in `pin-tan-read-session-attempt-v1.json`:
- Clean all-account discovery dialogues terminating in `Closed`
- Clean all-account unavailable outcomes followed by clean closure
- Clean balance refresh dialogues: single-page and multi-page pagination loops
- Clean balance refresh unavailable outcomes followed by clean closure
- Clean session closing without operational read requests
- Bank abort on close (`9800`) transitioning to `Aborted`
- Scoped review outcomes at initialization, all-account discovery, balance read, and dialogue closure
- Foreign response scope (`ContextMismatch`) preserving active state across initialization, operation, and closing stages
- Candidate dialogue mismatch and counter replay rejections
- Response counter replay rejection in continuation state
- Page limit enforcement transitioning to `PageLimitReached` and completing dialogue closure
- Deadline equality and single monotonic deadline expiration across multi-stage lifecycles
- In-flight cancellation across initialization, operation, and closing stages
- Pre-cancellation and stop-before-start boundaries
- Reentrant abandonment, disposal, and clock regression boundaries
- Rejection of second `StartInitialization` and out-of-order stage transitions (`WrongState`)
- Multithreaded concurrent response submission producing exactly one evidence handoff
- Constructor bounds validation for lifetime, maximum balance pages (up to 128), and positive clock frequency
- Scalar-only reflection assertions verifying absence of sensitive reference fields in snapshots
- Culture invariance across `de-DE`, `tr-TR`, `en-US`, and `ar-SA`.
