# Bank authentication integration — BR-04

Status: first credential ownership increment implemented; BR-04 remains in progress.

Priority 4 follow-up completes the supported [owned PIN and durable synchronization path](bank-sign-in-persistence.md): the connection is reserved before prompting, failed saves receive explicit recovery feedback, and reopen tests verify durable outcomes.

Priority 5 follow-up implements [signed process-2 text-TAN approval](bank-signed-approval.md) during synchronization: eligible two-step text-TAN procedures send an actual `HKTAN` process-4 segment in message 1, validate the bank's `HITAN` challenge against the outgoing request, prompt for a single-use disposable TAN owner under the shared prompt gate, send a PIN+TAN-signed `HKTAN` process-2 continuation as message 2, require confirmed completion before closing the dialogue at message 3, and never retry rejected credentials or fall back to unsigned continuations. BR-04 remains incomplete for decoupled status polling, TAN-medium selection, visual/HHD challenges, and read-dialogue SCA continuations.

`HostSession.AuthenticateAndSynchronizeBankConnectionAsync` accepts an explicit bank-advertised procedure context and signature selection. It checks configuration before requesting a separate banking PIN through `IBankCredentialPrompt`, then calls the BR-03 synchronization pipeline. It never reads the profile passphrase. It does not select a TAN procedure automatically or retry rejected credentials.

The session owns the returned `FinTsSessionCredential` until completion. Cancellation, the five-minute operation deadline, profile lock/shutdown, or a connection-context change disposes the owned PIN and cancels the operation. A session-wide gate serializes individual prompts/reviews with refresh interactions; it is released during network scheduling to avoid circular waits with institution gates. Prompt implementations must honor cancellation. Existing caller-owned BR-03 APIs retain their original ownership contract.

`BroilerDialogService` implements the disposable prompt contract. `BroilerCredentialPromptDialog.TakeCredential` transfers ownership exactly once. Accept and Enter use the same bounded validation; empty/oversized input cannot accept. Acceptance clears editable fields before the UI disposes them. Dialog disposal clears an unclaimed owner. The legacy string-returning API remains for existing consumers, but this banking flow never calls it.

## Memory limits

`StandardEdit.Text` is an immutable managed string. Clearing a field drops the application reference but cannot guarantee overwriting runtime copies. `SecureInputWrapper` no longer creates an unnecessary character-array copy and transfers its byte buffer directly for capture. Owned credential and wire buffers are cleared explicitly. These measures do not promise that all operating-system, UI, or managed-runtime copies can be erased. Credentials are never written to the profile by this workflow, and user-facing failures do not include arbitrary exception messages.

## Supported procedure matrix

| Case | Current behavior |
| --- | --- |
| BR-03 parameter-qualified synchronization without interactive approval | Uses a disposable banking PIN and validates the closed exchange |
| Process-2 text-TAN synchronization without medium selection (`MediumNameRequirement == "0"`) | Sends `HKTAN` process 4 in message 1, validates the challenge, prompts for an owned single-use TAN, sends signed `HKTAN` process 2 in message 2, and closes at message 3 |
| Explicit bank-reported SCA exemption | Existing qualified exemption path |
| Text/TAN continuation without a signed builder or unrequested challenge | Stops without sending an unsigned fallback; returned TAN owner is disposed |
| Decoupled app approval | Surfaced as unsupported in procedure selection and stops before polling |
| PhotoTAN / chipTAN / HHD visual rendering or named TAN medium | Surfaced as unsupported in procedure selection; no qualified production renderer or media inventory |

The unsigned HKTAN encoding helpers are protocol utilities, not production authentication adapters. In particular, the previous fallback that ignored the supplied TAN and sent an unsigned continuation has been removed. Existing low-level candidate callbacks do not establish production procedure support.

## Remaining BR-04 work

1. Implement a signed HKTAN continuation/request type with actual dialogue/message counters and qualified bank procedure provenance. The existing initialization binding cannot substitute for it.
2. Validate continuation responses against the actual sent HKTAN context, then hand accepted evidence back to synchronization/read attempts without treating a later response as message one.
3. Select and qualify the first text/TAN procedure end to end, including rejection, expiry, cancellation, and no automatic credential retries.
4. Implement signed decoupled queries with bank-advertised timing, bounded counts and an explicit app-approval interaction; qualify separately.
5. Add visual challenge rendering only with independently tested format support and explicit unsupported-format feedback.
6. Compose the authenticated operation into the discovery/refresh UI when its remaining dependencies are available.

Verification uses headless dialogs and scripted transports only. A successful scripted synchronization does not establish compatibility with a real bank or enable desktop balance refresh.
