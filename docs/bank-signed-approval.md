# Signed interactive text-TAN approval — priority 5

Connect now supports signed interactive text-TAN approval during initial bank sign-in and system-ID synchronization for explicitly selected, login-qualified two-step procedures (`ProcessVariant == "2"`, `MediumNameRequirement == "0"`, non-decoupled, non-visual/HHD, `SmsAccountRequirement != "2"`, bounded numeric or alphanumeric input).

## Request construction and challenge binding

When `FinTsProductionCandidateFactory.SynchronizationOptions` is invoked with a `textTanPrompt` callback and the selected procedure qualifies for process-2 text-TAN, message 1 is constructed by `CreateSynchronizationApprovalRequest()`:

- Outer 6-segment unsigned synchronization frame (`HNHBK`, `HKIDN`, `HKVVB`, `HKSYN:4:3+0'`, `HKTAN:5:v+4+HKIDN'`, `HNHBS`) wrapped in a signed PIN/TAN security envelope (`HNVSK`/`HNVSD` with `HNSHK` at segment 2 and PIN-only `HNSHA` at segment 7).
- No synthetic `HKTAN` request context or unsigned fallback is ever used; if a bank returns a `HITAN` challenge to a request that did not include an actual `HKTAN` segment, synchronization fails closed (`RejectedDialogueOutcome`).
- `FinTsSignedTextTanRequest.Create` binds the bank's message-1 response to the exact origin `FinTsPinTanRequestBinding`, verifies bank/user/envelope identity, checks that the returned `HITANS` procedure definition still matches the selected procedure byte-for-byte, validates `FinTsTanChallengeEvidence` (`ChallengeReported`, non-empty non-placeholder order reference, no HHD binary payload, non-empty challenge text without angle-bracket markup), and starts a two-minute monotonic freshness window. Each origin request can be continued at most once.

## TAN ownership, encoding, and confirmation

`FinTsSignedTextTanExecution.ExecuteAsync` coordinates the single-use continuation round trip:

- Prompts the user via the session-serialized `IBankCredentialPrompt` (`IsTan: true`) with sanitized challenge text and the qualified procedure name.
- Registers cancellation cleanup and a `finally` block so any acquired `FinTsSessionCredential` (`FinTsCredentialKind.Tan`) is deterministically erased/disposed even if cancellation races prompt completion.
- `FinTsSignedTextTanRequest.TryEncode` requires the full destination buffer reserve before touching credentials, checks procedure `MaximumTanLength` and numeric `InputFormat == "1"` constraints against the copied TAN bytes, consumes the TAN owner exactly once (`FinTsCredentialState.Consumed`), and encodes message 2 (`HNHBK` counter 2, `HNSHK:2:4` with security reference 2 and fresh control reference, `HKTAN:3:v+2++++<orderRef>+N'`, `HNSHA:4:2` with `PIN:TAN`, `HNHBS:5:1+2'`).
- `ValidConfirmation` verifies message-2 correlation (`DialogueId`, message number 2, reference to client message 2), envelope identity and system ID consistency, a single `HITAN` process-2 completion referencing segment 3 with the exact order reference and no residual challenge/expiry fields, and `0020` execution status without errors or `0030` pending status.
- Only after `ValidConfirmation` succeeds does `FinTsPinTanSynchronizationSessionAttempt.AcceptSignedApproval` accept the synchronization evidence and advance the dialogue to message-3 closing (`HKEND` with client/expected bank message number 3).

## Remaining scope

Decoupled status polling (`HKTAN` process `S`), TAN-medium discovery/selection (`HKTAB`/`HITAB`), and visual HHD/flicker/matrix challenge rendering remain unsupported and are surfaced as non-selectable or blocked before TAN dispatch. Desktop account discovery/review (priority 6), first-balance desktop orchestration (priority 7), and live-bank qualification (priority 8) remain subsequent priorities.

See [gap analysis](bank-connection-gap-analysis.md), [authentication integration](bank-authentication.md), [procedure selection](bank-procedure-selection.md), and [sign-in persistence](bank-sign-in-persistence.md).
