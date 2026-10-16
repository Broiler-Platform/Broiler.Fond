# ADR 0082: Synchronization Recovery Application and Bank Connection State Transition

- **Status:** Accepted (pure fail-closed recovery application and connection state transition)
- **Date:** 2026-10-15
- **Related:** [Synchronization sessions](0073-assembled-synchronization-session-attempt.md),
  [terminal ownership](0075-session-terminal-evidence-ownership.md),
  [synchronization session diagnostics](0081-synchronization-session-diagnostic-lifecycle.md),
  [manual institution setup](0001-kernel-boundary.md)

## Context and architectural requirement

FinTS 3.0 PIN/TAN dialogue coordinators (`FinTsPinTanSynchronizationSessionAttempt`) produce
caller-owned `FinTsPinTanSynchronizationSessionResult` instances ending in `ReinitializationRequired`
with `SynchronizationEvidence` and `ClosingEvidence`.

Under FinTS 3.0 Formals C.5.3 and C.8, synchronization dialogues cannot execute business transactions;
once closed, subsequent operations must occur in a newly initialized dialogue using the assigned
system identifier and synchronized message counter.

Prior to this increment:
- The kernel lacked an explicit connection entity (`BankConnection`) and operational state model
  (`BankConnectionState`) representing an authentication context in the local store.
- No recovery application mechanism existed to apply bank-assigned system identifiers (Mode 0)
  or synchronized message counters (Mode 1) to an active connection.
- Bank connection status transitions resulting from clean synchronization closure, bank abort (`9800`),
  or semantic review were unmapped.

This increment establishes the domain connection entity and pure recovery application engine.

## Domain connection model and state machine

`Broiler.Fond.Kernel.Domain.Connections` introduces:

1. **`BankConnectionState`**: Represents the explicit operational connection states mandated by
   the product specification:
   - `Unconfigured`: Configured with endpoint and credentials, but not yet synchronized or assigned a system ID.
   - `Authenticating`: Authentication or synchronization dialogue is actively in progress.
   - `ChallengeRequired`: SCA challenge (e.g. decoupled TAN) is pending user action.
   - `Connected`: Synchronized and ready for authorized financial dialogues.
   - `ReauthenticationRequired`: Credentials expired, changed, or dialogue reviewed; fresh synchronization/authentication required.
   - `TemporarilyUnavailable`: Bank reported maintenance, abort (`9800`), or temporary network failure; retry later without credential change.
   - `Disabled`: Explicitly deactivated by the user or security policy; no dialogues permitted until reactivated.

2. **`BankConnection`**: Immutable representation of one bank-access context and its operational state:
   - Properties: `Id`, `InstitutionId`, `InstitutionConfiguration`, `UserId`, `CustomerId`, `State`,
     `SystemId`, `SystemStatus`, `NextClientMessageNumber`, `IsSynchronized`, `LastSuccessfulContact`,
     `LastAttemptedContact`, `ConnectorVersion`.
   - Security Invariant: Banking credentials (PIN/TAN) are **never** persisted on this entity or in local archives.
   - Plain-Language Recovery Action: The `RecoveryAction` property provides human-readable guidance matching the current connection state.
   - Pure Immutability: State transitions (`WithState`, `WithSynchronization`, `WithDisabled`, `WithReactivated`, etc.)
     return new instances; existing instances remain completely unmodified.

## Pure synchronization recovery application

`FinTsSynchronizationRecoveryApplication.Apply` provides pure, fail-closed evaluation of a
`FinTsPinTanSynchronizationSessionResult` against a `BankConnection`:

| Precondition / Input | Outcome | Connection Transition | Action |
| --- | --- | --- | --- |
| Connection is `Disabled` | `RejectedDisabledConnection` | Unmodified (`Disabled`) | Fail-closed rejection |
| Session result is `AbortReported` | `BankAborted` | `TemporarilyUnavailable` | Update `LastAttemptedContact` |
| Session result is `RejectedForReview`, `ContextMismatch`, or `WrongState` | `ReviewRequired` | `ReauthenticationRequired` | Update `LastAttemptedContact` |
| Missing/unmatched evidence, missing closing evidence, or closing != `ClosureReported` | `RejectedInvalidResult` | Unmodified | Fail-closed rejection |
| Missing unique matching synchronization report | `RejectedMissingReport` | Unmodified | Fail-closed rejection |
| User ID or Bank Code mismatch against connection configuration | `RejectedIdentityMismatch` | Unmodified | Fail-closed rejection |
| Mode 0 (`SystemId`): invalid, zero, or "unbekannt" system ID | `RejectedInvalidSystemId` | Unmodified | Fail-closed rejection |
| Mode 0 (`SystemId`): valid assigned system ID (1..30 chars) | `Applied` | `Connected`, `IsSynchronized = true` | Set `SystemId`, reset `NextClientMessageNumber = 1` |
| Mode 1 (`LastMessageNumber`): invalid counter (< 1, > 9999, or > recovery context bound) | `RejectedInvalidMessageNumber` | Unmodified | Fail-closed rejection |
| Mode 1 (`LastMessageNumber`): valid bank-confirmed counter | `Applied` | `Connected`, `IsSynchronized = true` | Set `NextClientMessageNumber = lastMsg + 1` |
| Mode 2 (`SignatureReferences`): unsupported in PIN/TAN | `RejectedInvalidResult` | Unmodified | Fail-closed rejection |

## Verification

62 executable checks in `FinTsSynchronizationRecoveryApplicationTests.cs` verify:
- `BankConnection` validation boundaries (zero IDs, empty user ID, length limits, control characters).
- Explicit `BankConnectionState` transitions and plain-language `RecoveryAction` mappings.
- Clean Mode 0 system ID assignment updating connection state, system ID, contact timestamps, and resetting client message number to 1.
- Clean Mode 1 message number recovery advancing client message number to `lastMsg + 1` while preserving existing system ID.
- Maximum message recovery counter boundary handling.
- Fail-closed rejections for null inputs, disabled connections, bank abort on synchronization and closing, scoped review on synchronization and closing, user ID mismatch, customer ID mismatch, bank code mismatch, missing closing evidence, and wrong session transitions.
- Re-synchronization updating pre-existing system IDs and resetting advanced message counters.
- Pure immutability guarantee: input connection instances are never mutated.
- 16-thread concurrency atomicity across homogeneous and mixed concurrent recovery applications.

Zero third-party packages or runtime dependencies; platform-neutral BCL net10.0 only.
Credentials remain strictly transient and unpersisted.
