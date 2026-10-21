# Explicit bank procedure selection — priority 3

## Connect workflow

After downloading public bank advertisements, **Connections → Connect** opens **Choose Bank Sign-in Procedure**. The chooser has no default selection. It displays bank-advertised TAN methods and an explicit one-step option only if advertised. The user must select and accept an option; cancelling stops before login-specific exchange or PIN entry.

The dialog discloses that the next exchange sends the configured customer identity to the confirmed bank endpoint without a PIN. Unknown/invented choice IDs are rejected against the current observation. Methods requiring a named TAN medium, decoupled approval, HHD/visual challenges or non-text input formats are displayed with an unsupported explanation and cannot be accepted: this slice does not invent a device or imply that media discovery or decoupled polling exists. Supported process-2 text-TAN methods and advertised one-step methods can be selected; see [signed interactive approval](bank-signed-approval.md) for priority 5 continuation.

## Login-specific qualification

`FinTsProcedureQualification` creates a fresh identified, unsigned initialization with explicit product identity and version-zero BPD/UPD requests. It does not copy the anonymous request into an identified context. The bank must return matching bank/user/customer context and a supported, unambiguous advertisement plus a scoped 3920 permission report containing the chosen security function. The existing production candidate factory validates the response and selection before any signed request or PIN is created.

The production evidence evaluator now also interprets supported HIPINS and HISALS/HISPAS advertisements when accompanying HITANS. Unsupported or ambiguous layouts remain blocked; parsing these advertisements does not activate account capabilities. Dialogue-scoped UPD version zero is not retained for reuse after closing. Identified responses that need credentials, other unsupported layouts, expired/cancelled operations, wrong users/banks and missing permission evidence stop before PIN entry.

Successful qualification requires a matching closed exchange. Failed closes are never retained or blindly retried. An established anonymous/identified parameter dialogue whose close has not been dispatched can receive one bounded cleanup attempt. The selected method's definition must still match the public advertisement the user reviewed; a changed definition invalidates the public observation and requires a fresh download and choice.

Only after this qualification does Connect use the existing owned banking PIN prompt and synchronization workflow. A successful synchronization is distinct from account discovery and balance retrieval. A previously synchronized connection can requalify after a lock without falsely reporting a new authenticated sign-in.

## State and lifetime

The default session provider retains qualified selection and provenance only in memory, for at most ten minutes. Lock, profile/branch/epoch replacement, disabled access, relevant bank/login/endpoint changes, Safe Mode or expiry invalidate it. Expected system-ID/contact/state changes caused by synchronization preserve the selected bank/login parameter scope. The provider is not a credential store and is never treated as proof that a fresh bank sign-in succeeded.

A host-supplied `IBankProcedureProvider` remains host-owned; this chooser does not replace its state. No durable BPD/UPD cache, PIN/TAN persistence or automatic background qualification is introduced.

## Verification and limits

Kernel tests cover actual identified request bytes/product identity, matching user scope, missing/unpermitted procedure evidence, unsupported versions, dialogue-only UPD, additional typed parameters, closing failure and cancellation. Headless presentation tests cover the full Connect path from public download through selection, qualification and synchronization; no-default acceptance; cancellation and forged IDs; mismatched users; changed method definitions; unsupported media; credential disposal; lock/reopen requalification; and context edits while choosing. The dialog is measured/arranged at small sizes by the existing geometry suite.

This is the bounded no-interactive-approval slice. Bank-required credential-bearing parameter bootstrap, TAN media discovery/selection, signed TAN continuation and decoupled status polling still require later authentication work. No live bank, visual testing or live compatibility claim is included.

Validation on 2026-10-21: full `Invoke-CI.ps1 -Scope Full` passed, including the kernel runner, 2,771 presentation checks, 184 desktop/UI checks, formatting and the Release build with trim analyzers enabled (zero warnings/errors).

See [public parameter acquisition](bank-parameter-acquisition.md), [online setup](bank-online-setup.md) and the [qualification record](bank-refresh-qualification.md).
