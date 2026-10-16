# ADR 0086: FinTS Synchronization Execution Pipeline and Session Coordinator

- **Status:** Accepted (end-to-end FinTS synchronization execution pipeline coordinating transport, synchronization session attempt state machine, dialogue closing, and recovery application)
- **Date:** 2026-10-15
- **Related:** [Kernel boundary](0001-kernel-boundary.md),
  [assembled synchronization session attempt](0073-assembled-synchronization-session-attempt.md),
  [synchronization session clock boundaries](0074-synchronization-session-clock-boundaries.md),
  [session terminal evidence ownership](0075-session-terminal-evidence-ownership.md),
  [synchronization session diagnostic lifecycle](0081-synchronization-session-diagnostic-lifecycle.md),
  [synchronization recovery application](0082-synchronization-recovery-application.md),
  [FinTS transport and capability activation](0083-fints-transport-and-capability-activation.md),
  [FinTS read execution pipeline](0085-fints-read-execution-pipeline.md)

## Context and architectural requirement

Milestone 1 (specifically M1-05 FinTS read-only slice and bank connection lifecycle) requires coordinating live banking
dialogue execution for synchronization from preflight validation through synchronization request transmission, dialogue
termination (`HKEND`), and bank connection recovery application under pure, fail-closed constraints.

Prior to this increment:
- `IFinTsTransport` and `FinTsHttpsTransport` (ADR 0083) provided streaming HTTPS message exchange.
- `FinTsPinTanSynchronizationSessionAttempt` (ADRs 0073–0081) provided a strict state machine enforcing single monotonic deadlines,
  dialogue ID pinning, sequential message counters, and terminal reference ownership.
- `FinTsSynchronizationRecoveryApplication` (ADR 0082) provided pure validation and application of Mode 0 (system ID assignment)
  and Mode 1 (message number recovery) evidence to `BankConnection` domain entities.
- `FinTsReadExecutionPipeline` (ADR 0085) provided the end-to-end read execution coordinator, but required a synchronized connection
  as a prerequisite.
- No component coordinated the end-to-end synchronization flow over transport: encoding synchronization request envelopes, transmitting
  them, unwrapping responses (`HNVSK`), transitioning session stages, generating dialogue closing candidates (`HKEND`), zeroing
  transient credential buffers, and delivering evidence to `FinTsSynchronizationRecoveryApplication`.

This increment delivers `FinTsSynchronizationExecutionPipeline`, `FinTsSynchronizationExecutionOptions`, `FinTsSynchronizationExecutionResult`,
and the `BankConnection.ExecuteSynchronizationPipelineAsync` extension method in `Broiler.Fond.Kernel` without third-party dependencies.

## Design and implementation

### 1. Pipeline Execution Stages (`FinTsSynchronizationExecutionPipeline`)

`FinTsSynchronizationExecutionPipeline.ExecuteAsync(...)` executes three sequential dialogue and recovery stages:

1. **Preflight Validation:**
   - Verifies the bank connection is not disabled (`RejectedDisabledConnection`).
   - Verifies the endpoint URL is confirmed and absolute HTTPS (`RejectedInvalidEndpoint`).
   - Validates that the provided session credential is an available PIN credential (`RejectedInvalidCredential`).
   - Requires a valid synchronization candidate factory (`RejectedMissingCandidate`).

2. **Stage 1 — Synchronization Request & Response:**
   - Builds client message 1 (synchronization candidate) using `options.SynchronizationCandidateFactory`.
   - Records candidate into `FinTsPinTanSynchronizationSessionAttempt` (starting session lifetime clock).
   - Encodes the request envelope into a temporary byte buffer using `FinTsPinTanRequestEnvelopeWriter`.
   - Transmits via `IFinTsTransport.SendAsync`.
   - Unwraps wire response bytes (`ParseWireResponse`), parses BPD, UPD, and HISYN synchronization reports, and evaluates
     synchronization evidence through `AcceptSynchronizationResponse`.
   - Maps bank abort (`9800`) to `BankConnectionState.TemporarilyUnavailable` via recovery application.
   - Maps semantic review (`RejectedForReview`) to `BankConnectionState.ReauthenticationRequired` via recovery application.
   - Clears temporary buffer with `CryptographicOperations.ZeroMemory`.

3. **Stage 2 — Dialogue Closing (HKEND):**
   - Constructs dialogue termination candidate (`HKEND`): uses `options.ClosingCandidateFactory` if supplied, or
     synthesizes a valid default closing candidate (`CreateDefaultClosingCandidate`) derived from accepted synchronization
     evidence, sequential message counters, and pinned dialogue ID.
   - Encodes closing request via `FinTsPinTanDialogueEndWriter.TryEncode`.
   - Transmits via transport and evaluates response via `attempt.AcceptClosingResponse`.
   - Clears buffer with `CryptographicOperations.ZeroMemory`.

4. **Stage 3 — Recovery Application:**
   - Invokes `FinTsSynchronizationRecoveryApplication.Apply` against the closing result and connection.
   - On success (Mode 0 system ID assigned or Mode 1 message counter recovered), transitions connection to `BankConnectionState.Connected`
     with `IsSynchronized = true` and updated contact timestamps.
   - On failure, returns `FinTsSynchronizationExecutionResult.Failure` with the updated connection and descriptive issues.

### 2. Default Closing Candidate Generation (`CreateDefaultClosingCandidate`)

If no custom closing factory is configured, `CreateDefaultClosingCandidate`:
- Derives client message number (`ClientMessageNumber + 1`, i.e., 2) and expected bank number (`BankMessageNumber + 1`, i.e., 2).
- Generates unsigned `HKEND` body segment via `FinTsUnsignedDialogueEndWriter.Encode`.
- Synthesizes matching `HNSHK` signature header inheriting user, institution, country, security function, and profile
  version from accepted synchronization evidence, using the newly assigned system ID for Mode 0 or the existing system ID for Mode 1.
- Evaluates `FinTsPinTanDialogueEndContext.Evaluate` and constructs `FinTsPinTanDialogueEndRequestBinding`.

### 3. Connection State Mapping & Secret Safety

- **Dialogue Aborts (`9800`):** Transition connection state to `BankConnectionState.TemporarilyUnavailable` and record contact.
- **Dialogue Review (`RejectedForReview`):** Transition connection state to `BankConnectionState.ReauthenticationRequired`.
- **Transient PIN/TAN Buffers:** All stack and heap buffers receiving PIN credentials are zeroed via `CryptographicOperations.ZeroMemory`
  in `finally` blocks immediately after transmission.
- **Diagnostic Hygiene:** Sensitive secrets and HNSHA segment payloads are strictly excluded from exception messages, diagnostic
  strings, and execution result issues.

## Verification

56 executable checks in `FinTsSynchronizationExecutionPipelineTests.cs` verify:
- Preflight rejections for disabled connections, unconfirmed endpoints, invalid credential kinds, and missing candidate factories.
- Transport failure handling for simulated network resets, request timeouts, HTTP 500 error responses, and cancellation tokens.
- Bank abort (`9800`) mapping to `TemporarilyUnavailable` and semantic review mapping to `ReauthenticationRequired` across both
  synchronization and closing stages.
- End-to-end Mode 0 system ID assignment execution with real FinTS wire fixtures (`clean-assignment-session`), verifying two transmitted
  messages, clean dialogue closing, system ID assignment (`PUBLIC-ASSIGNED`), next client message number reset to 1, and connection
  transition to `Connected`.
- End-to-end Mode 1 message number recovery execution with real FinTS wire fixtures (`clean-message-recovery-session`), verifying two
  transmitted messages, message counter recovery (`11`), and system ID preservation.
- Default closing candidate generation when `ClosingCandidateFactory` is omitted.
- Secret safety invariants ensuring PIN values and signature trailers never appear in diagnostics.
- Multi-threaded concurrency with 16 parallel threads executing complete synchronization pipelines simultaneously without error or contention.
- Solution builds warning-free and passes `./eng/Invoke-CI.ps1 -Scope Full`.
