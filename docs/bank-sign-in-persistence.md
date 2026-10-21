# Owned PIN sign-in and durable synchronization — priority 4

Connect now carries its explicitly selected, login-qualified procedure into the existing banking PIN and system-ID synchronization path. The banking PIN is separate from the encrypted profile passphrase. The supported path requires no interactive bank approval; signed TAN/approval handling remains priority 5.

## Ownership and admission

Sign-in reserves the selected bank connection **before** entering or waiting for the PIN prompt. A competing operation receives an already-running outcome before requesting a second credential. The owned prompt transfers one disposable PIN owner; cancellation, expiry, changed profile/setup, failure and successful completion dispose it. Cancelled queued prompts release the connection lease for a later user attempt. The shared prompt gate is released before waiting for network/institution slots, preserving the existing deadlock-avoidance order.

The synchronization service accepts the existing lease and retains all profile/connection/product/endpoint checks. It constructs production requests using the selected procedure and never retries rejected credentials automatically. No credentials are persisted or copied into feedback.

## Durable completion

Only a validated successful synchronization **and matching closed dialogue** may reach persistence. Connection state is committed through the profile store's atomic mutation boundary. The new system ID, synchronized state and connected state become successful UI results only after that save returns.

Progress distinguishes the saving step. A received, closed bank synchronization that cannot be saved reports **ReceivedNotSaved**, explains storage recovery and retains the previous saved connection state. Rejection, uncertain close and changed-context cancellation never publish a new system ID. A failed projection reload or state notification after a successful commit cannot undo the save or turn it into a false save-failure result.

Opening an already synchronized profile still describes existing local state rather than claiming a new authenticated sign-in. Reopen tests verify that UI results agree with the encrypted profile's actual durable state. No accounts or balances are fabricated by synchronization.

## Verification

`BankSignInPersistenceTests` drives Connect with scripted production synchronization responses and a deterministic precommit failure seam. It covers rejection, uncertain close, failed persistence, post-save notification failure, separate owned PIN disposal, no credential retry, redacted errors, reopening each outcome, competing sign-in before PIN and cancelled admission release. Existing synchronization and procedure-selection tests cover cancellation/context changes and the full public-download → explicit choice → login qualification → PIN → synchronization path.

The precommit fault hook is internal test composition; production uses the real authenticated profile store. Existing kernel storage tests cover atomic generation/failure behavior. No live bank or visual tests are involved.

Validation on 2026-10-21: full `Invoke-CI.ps1 -Scope Full` passed, including the kernel runner, 2,794 presentation checks, 184 desktop/UI checks, formatting and the Release build with trim analyzers enabled (zero warnings/errors).

## Remaining scope

Priority 5 supplies [signed process-2 text-TAN interactive approval](bank-signed-approval.md) during synchronization, preserving the same pre-PIN admission, single-use credential ownership and closed-dialogue commit rules. Decoupled polling, visual/HHD challenges, credential-required parameter bootstrap and TAN-medium discovery remain unsupported. Discovery/review and first-balance desktop integration are subsequent priorities. This milestone does not qualify any live bank/procedure combination.
