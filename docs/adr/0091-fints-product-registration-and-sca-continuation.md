# ADR 0091: FinTS Product Registration and Interactive/Decoupled SCA Challenge Continuation

- **Status:** Accepted
- **Date:** 2026-10-16
- **Extends:** [ADR 0027](0027-fints-tan-procedure-and-challenge-schemas.md), [ADR 0028](0028-fints-permitted-procedures-and-request-bound-challenge-evidence.md), [ADR 0029](0029-fints-in-memory-sca-continuation.md), [ADR 0083](0083-fints-transport-and-capability-activation.md), [ADR 0085](0085-fints-read-execution-pipeline.md), [ADR 0086](0086-fints-synchronization-execution-pipeline.md)

## Context

Milestone 1 item **M1-05 (FinTS Read-Only Slice / Live SCA Continuation)** defines live bank dialogue initiation and strong customer authentication (SCA) continuation:

1. **FinTS Product Registration**:
   - The Deutsche Kreditwirtschaft (DK) FinTS specification requires every software product initiating FinTS dialogues to declare a registered FinTS Product Identifier (`HKVVB` / `HKSYN` parameter fields) and provide an associated product version and registration code.
   - Per product roadmap ([docs/roadmap.md lines 1558–1561](../roadmap.md#principles-and-invariants)), `Broiler` is the brand, `Fond - Finance on Demand` is the product, `Broiler Fond - Finance on Demand` is the full display name, and `Broiler.Fond` is the technical name.
   - Software must validate and embed the registered product metadata (`ProductIdentifier`, `ProductVersion`, `RegistrationCode`, `DisplayName`) into dialogue initialization and synchronization preparations before any live-bank dialogue is opened.
2. **Interactive TAN Continuation and Decoupled SCA Polling**:
   - When a bank responds to an operation with an SCA challenge (`HITAN` with HIRMS `0030`), the dialogue requires either:
     - **Decoupled SCA (`Decoupled`)**: The user confirms the operation in their banking app or token device. The client periodically polls the bank using unsigned status query frames (`HKTAN:2:7+S++++{orderReference}+N'`) with exact message framing and delay intervals until the bank reports `0020` (ExecutionReported), or terminates due to timeout or query budget exhaustion.
     - **Interactive Two-Step TAN (`2`)**: The bank provides challenge text, optical data, or SMS notification. The user must provide a one-time TAN. The client submits the TAN via an interactive TAN continuation request (`HKTAN:2:7+2+{operation}+++{orderReference}+N'`), zeroizing the TAN immediately upon use.
     - **SCA Exemption (`3076`)**: If the bank reports an exemption, execution proceeds directly without interactive continuation.
3. **Secret Zeroization and Memory Invariants**:
   - All session credentials (PINs and TANs) must be captured in bounded memory buffers and wiped using `CryptographicOperations.ZeroMemory` in guaranteed `finally` blocks, never written to disk, logged, or exposed in exceptions.

## Decision

### 1. FinTS Product Registration Model (`FinTsProductRegistration` & `KernelProduct`)

- `FinTsProductRegistration` encapsulates validated, immutable FinTS product registration credentials:
  - `ProductIdentifier`: non-empty ASCII identifier, at most 25 characters, strictly excluding FinTS delimiter characters (`+`, `:`, `'`, `?`).
  - `ProductVersion`: non-empty version string, at most 5 characters, excluding FinTS delimiters.
  - `RegistrationCode`: non-empty registration code, at most 25 characters, excluding FinTS delimiters.
  - `DisplayName`: optional human-readable display name.
- `KernelProduct.FinTsRegistration` provides the registered default credentials for the application:
  - `ProductIdentifier`: `"BROILER-FOND"`
  - `ProductVersion`: `"1.0"`
  - `RegistrationCode`: `"9999999999"` (placeholder developer code pending human production registration)
  - `DisplayName`: `KernelProduct.FullDisplayName` (`"Broiler Fond - Finance on Demand"`)

### 2. SCA Execution Coordinator (`FinTsScaExecutionCoordinator`)

- `FinTsScaExecutionCoordinator` manages the live transport execution of SCA continuation flows:
  1. **Unsigned Framing Codecs**:
     - `EncodeStatusQuery`: encodes unsigned status query frame (`HKTAN` process `"S"`, version 7) with exact 12-digit message size framing (`HNHBK`), dialogue ID, monotonic message numbers, order reference, and optional medium name at field index 10.
     - `EncodeTanContinuation`: encodes unsigned two-step confirmation request (`HKTAN` process `"2"`, version 7) with order reference, operation code, and optional medium name.
     - `CreateRequestContext`: creates a synthetic request context for initial order matching.
  2. **Decoupled Polling Execution**:
     - Starts `FinTsScaContinuation` with caller-specified timeout, query budget, and clock (`TimeProvider`).
     - Evaluates delay interval (`QueryWait`). Handles OS timer quantum granularity by incorporating safety margins and retrying on `FinTsScaTransition.TooEarly`.
     - Increments client and bank message counters monotonically.
     - Sends status query wire via `IFinTsTransport.SendAsync`. Catches `FinTsTransportException` fail-closed, stops the SCA state machine, and returns descriptive failure outcomes without exposing secrets.
     - Evaluates bank status response via `FinTsTanChallengeEvidence.Evaluate`.
     - Transitions on `ExecutionReported` (success) or `PendingAccepted` (next poll iteration).
  3. **Interactive TAN Continuation Execution**:
     - Invokes caller-provided `ScaChallengeHandler` delegate (`Func<FinTsTanChallenge, FinTsTanProcedure, CancellationToken, ValueTask<FinTsSessionCredential?>>`).
     - Propagates cancellation if user cancels (`IsCancelled == true`).
     - Validates that returned credential is an available `FinTsCredentialKind.Tan`.
     - Encodes either candidate envelope or fallback continuation wire, sends over transport, zeroizes credential and buffer in `finally`, and returns terminal success or failure.
  4. **Exemption Path**:
     - Detects `FinTsTanOutcomeObservation.ExemptionReported` on initial evidence and returns `FinTsScaExecutionResult.Exempt` without transport round-trips.

### 3. Pipeline Integration (`FinTsReadExecutionPipeline` & `FinTsSynchronizationExecutionPipeline`)

- Both read and synchronization execution pipelines integrate `FinTsScaExecutionCoordinator`:
  - `FinTsReadExecutionPipeline`: invokes SCA coordinator on Stage 1 (Initialization), Stage 2 (All-Account Discovery), and Stage 2 (Balance Refresh) whenever initial evidence indicates `Observation == ChallengeReported`.
  - `FinTsSynchronizationExecutionPipeline`: exposes `Procedures` and `Permissions` options and coordinates SCA continuation before applying synchronization responses.
  - Default candidate factory helpers `CreateDefaultInitializationCandidate` and `CreateDefaultSynchronizationCandidate` accept `FinTsProductRegistration` (defaulting to `KernelProduct.FinTsRegistration`) and `FinTsPinTanProcedureContext` to assemble fully verified, registered envelopes.

## Consequences & Verification

- `FinTsScaExecutionPipelineTests` executes 72 automated checks covering:
  - Product registration validation: rejected delimiters, character lengths, and empty strings.
  - Candidate embedding: initialization and synchronization candidates embed registered product ID and version.
  - Wire encoding: byte-for-byte verification of status queries against fixture vector 1 query 0, structural segment verification, and medium name positioning at field index 10.
  - Decoupled polling lifecycle: successful 2-query polling against fixture vector 1, transport failure handling, and state machine transition integrity.
  - Interactive TAN continuation: missing handler failure, user cancellation propagation, wrong credential kind rejection, and successful TAN continuation execution.
  - Secret zeroization: transient PIN and TAN session credentials zeroized immediately in `finally` blocks.
  - Exemption reporting: direct completion without network round-trips when bank reports `3076`.
  - Read and synchronization pipeline SCA wiring.
- Full Windows CI (`eng/Invoke-CI.ps1 -Scope Full`) and formatting validation pass with zero warnings and zero errors.
