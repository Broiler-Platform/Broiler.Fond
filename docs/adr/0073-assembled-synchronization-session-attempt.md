# ADR 0073: Bounded assembled PIN/TAN synchronization session attempt coordinator

- **Status:** Accepted (local synthetic lifecycle only)
- **Date:** 2026-10-14
- **Related:** [Bounded assembled PIN/TAN read session attempt coordinator](0072-assembled-read-session-attempt.md),
  [Assembled PIN/TAN closing attempt](0046-pin-tan-closing-attempt-lifecycle.md),
  [Assembled PIN/TAN synchronization attempt](0043-pin-tan-synchronization-attempt-lifecycle.md),
  [In-memory synchronization and closing attempt](0031-in-memory-synchronization-and-closing.md)

## Context and architectural requirement

FinTS 3.0 PIN/TAN dialogues follow two strict operational archetypes:
1. **Operational Read Dialogue:** Initialized via `HKIDN` + `HKVVB`, followed by read requests (`HKSPA` for all-account discovery, or `HKSAL`/`HKKAZ` for multi-page balance inquiry), and concluded via `HKEND`. This dialogue is coordinated end-to-end by `FinTsPinTanReadSessionAttempt` ([ADR 0072](0072-assembled-read-session-attempt.md)).
2. **Synchronization Dialogue:** Initiated via `HKIDN` + `HKVVB` + `HKSYN` (with mode 0 for system ID assignment, or mode 1/2 for message number recovery), followed immediately by dialogue termination via `HKEND`. Under FinTS 3.0 Formals C.5.3, no operational business transactions may be executed within a synchronization dialogue. Once synchronized, the dialogue must be closed, and subsequent operations must occur in a newly initialized dialogue using the assigned system identifier and synchronized message counter.

Prior to this increment, the PIN/TAN layer provided disjoint single-message attempts: `FinTsPinTanSynchronizationAttempt` ([ADR 0043](0043-pin-tan-synchronization-attempt-lifecycle.md)) for `HKSYN`, and `FinTsPinTanDialogueEndAttempt` ([ADR 0046](0046-pin-tan-closing-attempt-lifecycle.md)) for `HKEND`. A client orchestrating synchronization was required to manage multiple deadlines, manual message counter sequencing, manual dialogue ID propagation, and fragmented error handling across independent instances.

`FinTsPinTanSynchronizationSessionAttempt` unifies the PIN/TAN synchronization dialogue into a bounded, thread-safe, multi-stage session attempt coordinator operating under a single monotonic deadline.

## Two-stage synchronization dialogue coordination

`FinTsPinTanSynchronizationSessionAttempt` coordinates the full lifecycle across two sequential stages:
1. **Synchronization Stage (`Synchronization` stage):**
   - Initiated via `StartSynchronization(candidate, recovery)` with an assembled synchronization request candidate (`FinTsPinTanRequestBinding`, client message 1) and optional recovery context (`FinTsPinTanSynchronizationRecoveryContext`). Mode 0 (system ID assignment) requires null recovery context; mode 1/2 (message number recovery) requires non-null recovery context matching the candidate mode.
   - Evaluated via `AcceptSynchronizationResponse(response)` with parsed `FinTsResponse` and `FinTsSynchronizationDataSet`.
   - On matching execution evidence (`HISYN` report, matching envelope identity, `HIRMG`/`HIRMS` success codes), pins the bank-assigned `_dialogueId`, bank message counter (1), and synchronization evidence. Transitions to `AwaitingCloseRequest` in the `Closing` stage, returning `CloseRequired`.
   - Bank abort (`9800`) transitions to `Aborted`. Scoped review codes or discrepancies transition to `NeedsReview`.
2. **Dialogue Closing Stage (`Closing` stage):**
   - Records an assembled dialogue termination candidate via `RecordClosingCandidate(candidate)` (`FinTsPinTanDialogueEndRequestBinding`, client message 2, expected bank message 2, matching pinned `_dialogueId`).
   - Evaluated via `AcceptClosingResponse(response)` (`FinTsResponse`, bank message 2).
   - Clean closure reports (`0100`) transition to `ReinitializationRequired` in the `Terminal` stage (the mandatory FinTS next step following synchronization dialogue termination), returning caller-owned `FinTsPinTanSynchronizationSessionResult` containing both `SynchronizationEvidence` and `ClosingEvidence`.
   - Bank abort (`9800`) transitions to `Aborted`. Unresolved outcomes transition to `NeedsReview`.

## Monotonic deadline and lifecycle guarantees

- **Single Absolute Monotonic Deadline:** Configured at construction (up to fifteen minutes, default 15m) using an optional `TimeProvider`. Monotonic elapsed time begins upon `StartSynchronization`. The closing exchange operates strictly within the remaining balance of this initial deadline; recording a closing candidate neither resets nor extends the deadline.
- **Fail-Closed on Time & Clock Invalidation:** When elapsed time reaches or exceeds the lifetime limit, the coordinator transitions immediately to `TimedOut` in the `Terminal` stage. Clock regression, negative elapsed time, or clock exceptions transition to `ClockInvalid`.
- **Cancellation & Teardown:** Caller cancellation via `CancellationToken`, `Cancel()`, `Stop()`, or `Dispose()` transitions to `Cancelled` or `Stopped`, immediately clearing all pending candidate metadata without network operations or state corruption.

## Replay defense, dialogue pinning, and foreign scope segregation

- **Dialogue ID Pinning:** Once established by the bank's synchronization response, the closing candidate must specify the identical dialogue ID. Mismatches return `ContextMismatch` with issue flag `DialogueIdMismatch`.
- **Monotonic Message Counters:** Sequential client message numbers (1 then 2) and expected bank message numbers (1 then 2) are enforced. Candidate client message numbers less than or equal to `_lastClientNumber` return `ReplayRejected` with `ReplayDetected`. Gaps or counter mismatches return `ContextMismatch` with `MessageNumberMismatch`.
- **Response Replay Immunity:** Incoming bank responses with message numbers less than or equal to `_lastBankNumber` within the dialogue return `ReplayRejected` without modifying active state or consuming pending candidates.
- **Transient Context Mismatch Segregation:** Foreign-scope response issues (`MessageMismatch`, `MissingSegmentReference`, `UnknownSegmentReference`, `SegmentRoleMismatch`) return `ContextMismatch` without consuming the pending candidate or advancing `ResponsesHandled`. The active candidate remains pinned under its original deadline, and subsequent valid responses clear transient mismatch diagnostics.

## Caller-owned evidence and scalar diagnostics

- **Immediate Candidate Release:** Every terminal transition (`ReinitializationRequired`, `Aborted`, `NeedsReview`, `Cancelled`, `TimedOut`, `Stopped`, `ClockInvalid`) immediately sets internal candidate references (`_pendingSynchronization`, `_recovery`, `_pendingClosing`) to null.
- **Immutable Evidence Handoff:** Terminal outcomes provide caller-owned, immutable evidence containers (`SynchronizationEvidence`, `ClosingEvidence`).
- **Scalar-Only Diagnostics:** `FinTsPinTanSynchronizationSessionSnapshot` exposes strictly scalar diagnostics: `State`, `Stage`, `RequestsRecorded`, `ResponsesHandled`, `ClientMessageNumber`, `BankMessageNumber`, `RemainingTime`, and scalar diagnostic issue flags. Snapshots contain no credentials, PINs, TANs, system IDs, or raw payloads.
- **No Side Effects:** Successful synchronization dialogue completion neither applies recovered values to persistent stores nor authorizes financial transactions. The kernel remains inert and purely user-operated.

## Verification

`FinTsPinTanSynchronizationSessionAttemptTests` verifies 24 independent Python-generated synthetic traces and comprehensive boundary checks in `pin-tan-synchronization-session-attempt-v1.json`:
- Clean system ID assignment synchronization followed by clean dialogue closure (`ReinitializationRequired`)
- Clean message number recovery synchronization followed by clean dialogue closure (`ReinitializationRequired`)
- Bank abort on synchronization (`9800`) transitioning to `Aborted`
- Bank abort on closing (`9800`) transitioning to `Aborted`
- Scoped review outcomes at synchronization and closing stages
- Dialogue ID mismatch on closing candidate returning `ContextMismatch`
- Candidate and response message counter replay rejection returning `ReplayRejected`
- Foreign scope handling (`ContextMismatch`) preserving active state and remaining deadline
- Single monotonic deadline expiration across multi-stage lifecycles (`TimedOut`)
- In-flight cancellation across synchronization and closing stages
- Pre-cancellation, stop-before-start, reentrant abandonment, disposal, and clock regression boundaries
- Rejection of out-of-order calls (`WrongState`)
- Multithreaded concurrent response submission producing exactly one evidence handoff
- Constructor bounds validation for lifetime and positive clock frequency
- Scalar-only reflection assertions verifying absence of sensitive reference fields in snapshots
- Culture invariance across `de-DE`, `tr-TR`, `en-US`, and `ar-SA`.
