# ADR 0085: FinTS Read Execution Pipeline and Session Coordinator

- **Status:** Accepted (end-to-end FinTS read execution pipeline coordinating transport, session attempt state machine, pagination, default closing generation, and store ingestion application)
- **Date:** 2026-10-15
- **Related:** [Kernel boundary](0001-kernel-boundary.md),
  [exact money and account values](0004-exact-money-and-account-values.md),
  [assembled read session attempt](0072-assembled-read-session-attempt.md),
  [session terminal evidence ownership](0075-session-terminal-evidence-ownership.md),
  [read session operation scope and page budgets](0077-read-session-operation-scope.md),
  [read session response accounting](0080-read-session-response-accounting.md),
  [synchronization recovery application](0082-synchronization-recovery-application.md),
  [FinTS transport and capability activation](0083-fints-transport-and-capability-activation.md),
  [FinTS read data ingestion and store binding](0084-fints-read-ingestion-and-store-binding.md)

## Context and architectural requirement

Milestone 1 (specifically M1-05 FinTS read-only slice and store binding) requires coordinating live banking dialogue
execution from preflight validation through initialization, operational read, continuation pagination, dialogue
closing, and local store ingestion under pure, fail-closed constraints.

Prior to this increment:
- `IFinTsTransport` and `FinTsHttpsTransport` (ADR 0083) provided streaming HTTPS message exchange.
- `FinTsPinTanReadSessionAttempt` (ADRs 0072–0080) provided a strict state machine enforcing single absolute deadlines,
  dialogue ID pinning, sequential message counters, page budgets, and terminal reference ownership.
- `FinTsReadIngestionApplication` (ADR 0084) provided atomic account rediscovery, identity allocation, and balance
  snapshot ingestion into `LocalIdentityLedger`.
- However, no component coordinated the end-to-end flow: encoding requests, transmitting envelopes, decoding and unwrapping
  responses (`HNVSK`), transitioning session stages, managing balance continuation pagination loops, generating dialogue
  closing candidates (`HKEND`), zeroing transient credential buffers, and delivering consolidated evidence to store ingestion.

This increment delivers `FinTsReadExecutionPipeline`, `FinTsReadExecutionOptions`, `FinTsReadExecutionResult`, and
the `BankConnection.ExecuteReadPipelineAsync` extension method in `Broiler.Fond.Kernel` without third-party dependencies.

## Design and implementation

### 1. Pipeline Execution Stages (`FinTsReadExecutionPipeline`)

`FinTsReadExecutionPipeline.ExecuteAsync(...)` executes four sequential dialogue and ingestion stages:

1. **Preflight Validation:**
   - Verifies the bank connection is not disabled (`RejectedDisabledConnection`).
   - Verifies the bank connection is synchronized or connected (`RejectedUnsynchronizedConnection`).
   - Verifies the endpoint URL is confirmed and absolute HTTPS (`RejectedInvalidEndpoint`).
   - Validates that the provided session credential is an available PIN credential (`RejectedInvalidCredential`).
   - For balance refresh, requires a non-null `TargetAccountBinding` (`RejectedMissingCandidate`).

2. **Stage 1 — Initialization:**
   - Builds client message 1 (initialization candidate) using `options.InitializationCandidateFactory`.
   - Records candidate into `FinTsPinTanReadSessionAttempt` (starting session lifetime clock).
   - Encodes the request envelope into a temporary byte buffer using `FinTsPinTanRequestEnvelopeWriter`.
   - Transmits via `IFinTsTransport.SendAsync`.
   - Unwraps wire response bytes (`ParseWireResponse`), parses BPD, UPD, HITANS, HIRMS 3920, HIPINS, and HISPAS/HISALS,
     and evaluates initialization evidence through `AcceptInitializationResponse`.
   - Clears temporary buffer with `CryptographicOperations.ZeroMemory`.

3. **Stage 2 — Operational Read & Pagination:**
   - **All-Account Discovery (`AllAccountDiscovery`):**
     - Builds HKSPA discovery candidate via `options.AllAccountCandidateFactory`.
     - Encodes PIN-only request envelope, transmits via transport, and feeds response into `attempt.AcceptAllAccountResponse`.
   - **Balance Refresh (`BalanceRefresh`):**
     - Builds initial HKSAL request candidate via `options.BalanceFirstReadCandidateFactory`.
     - Encodes PIN-only envelope, transmits, and evaluates response via `attempt.AcceptBalanceResponse`.
     - **Pagination Loop:** While `attempt` remains in `WaitingForBalanceContinuation` with a valid continuation token:
       - Builds next-page continuation candidate via `options.BalanceContinuationCandidateFactory`.
       - Encodes PIN-only continuation envelope, transmits, and evaluates via `attempt.AcceptBalanceResponse`.
       - Aggregates balances across pages and enforces the configured `MaximumBalancePages` bound.
   - Clears all staging buffers with `CryptographicOperations.ZeroMemory`.

4. **Stage 3 — Dialogue Closing:**
   - Constructs dialogue termination candidate (`HKEND`): uses `options.ClosingCandidateFactory` if supplied, or
     synthesizes a valid default closing candidate (`CreateDefaultClosingCandidate`) derived from accepted initialization
     evidence, sequential message counters, and pinned dialogue ID.
   - Encodes closing request via `FinTsPinTanDialogueEndWriter.TryEncode`.
   - Transmits via transport and evaluates response via `attempt.AcceptClosingResponse`.
   - Clears buffer with `CryptographicOperations.ZeroMemory`.

5. **Stage 4 — Store Ingestion Application:**
   - Synthesizes consolidated session result preserving initialization evidence, procedure evidence, operational evidence,
     aggregated balance snapshots, and closing evidence.
   - Invokes `FinTsReadIngestionApplication.Apply` against `knownBindings` and `LocalIdentityLedger`.
   - On success, returns `FinTsReadExecutionResult.Completed` with the updated connection, committed identity snapshot, and
     ingested balance snapshots.

### 2. Default Closing Candidate Generation (`CreateDefaultClosingCandidate`)

If no custom closing factory is configured, `CreateDefaultClosingCandidate`:
- Derives client message number (`ClientMessageNumber + 1`) and expected bank number (`BankMessageNumber + 1`).
- Generates unsigned `HKEND` body segment via `FinTsUnsignedDialogueEndWriter.Encode`.
- Synthesizes matching `HNSHK` signature header inheriting user, institution, system ID, security function, and profile
  version from accepted initialization evidence.
- Evaluates `FinTsPinTanDialogueEndContext.EvaluateForInitialization` and constructs `FinTsPinTanDialogueEndRequestBinding`.

### 3. Connection State Mapping & Secret Safety

- **Dialogue Aborts (`9800`):** Transition connection state to `BankConnectionState.TemporarilyUnavailable` and record contact.
- **Dialogue Review (`3920` / wrong reference):** Transition connection state to `BankConnectionState.ReauthenticationRequired`.
- **Transient PIN/TAN Buffers:** All stack and heap buffers receiving PIN credentials are zeroed via `CryptographicOperations.ZeroMemory`
  in `finally` blocks immediately after transmission.
- **Diagnostic Hygiene:** Sensitive secrets and HNSHA segment payloads are strictly excluded from exception messages, diagnostic
  strings, and execution result issues.

## Verification

56 executable checks in `FinTsReadExecutionPipelineTests.cs` verify:
- Preflight rejections for disabled connections, unsynchronized connections, unconfirmed endpoints, invalid credential kinds,
  missing target bindings, and missing candidate factories.
- Transport failure handling for simulated network resets, request timeouts, HTTP 500 error responses, and cancellation tokens.
- Bank abort (`9800`) mapping to `TemporarilyUnavailable` and semantic review (`3920`) mapping to `ReauthenticationRequired`.
- End-to-end all-account discovery execution with real FinTS wire fixtures (Vector 0 of `pin-tan-read-session-attempt-v1.json`),
  verifying three transmitted messages, clean dialogue closing, account discovery, and atomic store binding.
- End-to-end multi-page balance refresh execution with pagination (Vector 3 of `pin-tan-read-session-attempt-v1.json`),
  verifying four transmitted messages, continuation token handling, balance aggregation, and snapshot ingestion.
- Default closing candidate generation when `ClosingCandidateFactory` is omitted.
- Secret safety invariants ensuring PIN values and signature trailers never appear in diagnostics.
- Multi-threaded concurrency with 16 parallel threads executing complete read pipelines simultaneously without error or contention.
- Solution builds warning-free and passes `./eng/Invoke-CI.ps1 -Scope Full`.
