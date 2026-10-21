# Production bank request construction and synchronization

Updated: 2026-10-21. Implements the supported BR-03 construction/synchronization slice of the [balance refresh roadmap](balance-refresh-roadmap.md). No live bank qualification is implied.

## Boundary

`FinTsProductionCandidateFactory` constructs credential-free request candidates from a validated `BankConnection`, explicitly configured `FinTsProductRegistration`, actual parsed bank procedure context, and an explicitly selected permitted security procedure. It uses the existing bounded kernel encoders and evidence checks. It does not pick the first advertised TAN procedure, obtain credentials, manufacture bank parameters, or authorize a PIN-only read merely because a candidate can be encoded.

The factory requires bank parameter provenance. That provenance must originate from the intended confirmed connection; it is not a substitute for authentication. BR-04 must supply scoped parameter/credential interaction, and BR-05 must bind discovery results to local accounts. The production factory never reads test fixtures or decodes fixed example request bytes.

The supported initial market is Germany/FinTS 3.0 (`280`, protocol `300`) using the kernel's supported PIN/TAN signature profiles, HKIDN:2, HKVVB:3, and HNSHK:4. Signature-profile hash/algorithm/key fields follow the existing restricted PIN/TAN encoder policy; they do not claim support for other security mechanisms. Unknown schemas, missing sources, and ambiguous or unpermitted procedure evidence fail closed.

## Builder inventory

| Request | Production adapter | Source of context | Limits and follow-up |
| --- | --- | --- | --- |
| HKSYN system-ID assignment | `CreateSynchronization(SystemId)` | Current bank/user/customer identity, actual BPD/UPD versions, explicit product ID and permitted selection | Outgoing system ID `0` is the protocol assignment marker, not fabricated assigned state |
| HKSYN last-message recovery | `CreateSynchronization(LastMessageNumber)` | Existing synchronized system plus explicit recovery context when constructing execution options | An options set without recovery context is rejected; additional recovery qualification remains required before exposing a user flow |
| Signature-reference recovery | Explicit `UnsupportedVersion` | None inferred | Not supported by this production slice |
| HKIDN/HKVVB read initialization | `CreateInitialization` | Synchronized system ID, configured product, bank parameter versions and procedure provenance | Cannot start a read with a missing/zero/unassigned system ID |
| HKSPA all-account read | `CreateDiscovery` | This exact attempt's accepted initialization, HIPINS, bank/user read parameters and dialogue | Kernel-supported all-account schema only; bank-advertised version and limits must qualify |
| HKSAL first balance read | `CreateBalance` | Exact account object from the accepted response, account identifiers, operation permission and supported advertisements | Chooses a qualified advertised v6/v7/v8; national-only v6 never silently drops supplied IBAN/BIC data |
| HKSAL continuation | `CreateBalanceContinuation` | Exact accepted predecessor, reported token, account/options, pinned dialogue and both counters | No caller-invented token or page order; continuation budget remains enforced by the existing session attempt |
| HKEND synchronization close | `CreateSynchronizationClosing` | Accepted synchronization evidence, bank-assigned system ID, next dialogue counters | A response that needs review cannot supply a trusted closing context |
| HKEND read close | `CreateClosing` | Accepted initialization evidence and this attempt's latest counters | Exact identity, product, selected procedure and dialogue are checked |
| Interactive HKTAN / status queries | Explicit `UnsupportedSca` boundary (`RequireScaBuilder`) | Requires BR-04 interaction, challenge provenance, timing and continuation binding | No fixture, unsigned request, automatic query, or guessed procedure fallback |

Every new candidate allocates a fresh random control reference. The security reference advances with the dialogue message number; this is scoped dialogue behavior, not a claim of a persistent bank-wide signing counter. Signature date/time comes from the injected clock. Product identity/version and BPD/UPD versions come from explicit configuration and bank evidence rather than historical defaults.

Read candidates preserve the bank's HIPINS/TAN requirement evidence. Constructing a PIN-only envelope does not prove that omitting a TAN is permitted. Before exposing reads, BR-04/BR-06 must establish the authentication/SCA path or reject it before read dispatch.

## Session synchronization API

`HostSession.SynchronizeBankConnectionAsync` is the BR-03 composition boundary. Its caller provides:

- a current `ConnectionSetupContext` review revision;
- a caller-owned, available, ephemeral banking PIN owner;
- bank procedure parameter provenance and an explicit selected procedure;
- a cancellation token.

The method checks the writable/unlocked profile, Safe Mode, current review revision, confirmed endpoint, product configuration, saved-to-domain mapping, disabled state, and candidate qualification. It shares the per-connection operation reservation and a session-wide maximum of two executing bank operations with the BR-01 refresh coordinator. It does not collect a PIN or choose a TAN procedure inside a click handler.

For a context without compatible synchronized state, it runs the existing synchronization pipeline through a scoped confirmed HTTPS transport and closes the synchronization dialogue. Only a validated successful result, including confirmed close, is persisted. The commit rechecks the profile/branch/session/configuration revision under the session lock, changes only the connection document, and preserves account identities and financial data. A synchronized result survives profile reopening.

Compatible confirmed saved state can return `AlreadySynchronized` without network traffic after validating the supplied bank procedure context. Endpoint/login edits reset compatibility through BR-02's invalidation rules. That reuse is not a claim that a subsequent bank request will be accepted; later bank responses must still be evaluated normally.

The session owns/disposes the transport. The caller retains ownership of the PIN owner and must dispose it on all terminal paths. Interactive PIN/SCA ownership and UI dispatch are BR-04/BR-08 responsibilities. Synchronization does not discover accounts, activate manual bindings, refresh balances, or make the desktop refresh buttons operational.

## Failure and cleanup policy

- Candidate failures use fixed `FinTsCandidateFailure` codes without raw credentials, wire payloads, or bank free text.
- Missing bank parameters, wrong bank/customer/user scope, unsupported versions, and unpermitted procedures prevent dispatch.
- Desktop results distinguish already-running, unavailable setup, unsupported candidate, authentication review, cancellation, changed context, failure, and durable synchronization.
- A failed/uncertain close does not persist a new system ID or claim successful synchronization.
- A profile lock/switch, setup edit, disposal, or stale revision cannot commit an in-flight result into a replacement context.
- The read pipeline makes a bounded best-effort close when initialization was accepted but an operation candidate was never recorded/dispatched. Synchronization cleanup similarly requires accepted synchronization evidence with no recorded closing candidate.
- Cleanup gets an independent two-second cancellation budget through the transport's cooperative cancellation contract, clears its temporary secret buffer, and emits only `Closed` or `Failed` through an optional observer. Cleanup or observer failure never replaces the primary failure.
- No close is blindly replayed after a dispatched operation with an unknown outcome or an already-attempted close. If the dialogue/counters/evidence cannot safely support cleanup, it is skipped.

## Verification evidence

[Kernel tests](../tests/Broiler.Fond.Kernel.Tests/FinTsProductionCandidateFactoryTests.cs) construct outgoing requests through production code and use scripted bank response fixtures only as bank observations. Coverage includes exact unsigned bytes/product/version selection, actual envelope segment numbering, fresh controls, invalid procedure/user rejection, synchronization/close, discovery, balance version negotiation, continuation provenance, and cleanup preserving the original failure.

[Presentation tests](../tests/Broiler.Fond.Presentation.Tests/BankSynchronizationTests.cs) cover durable synchronization and reopen, product identity on outgoing bytes, transport disposal, synchronized-state reuse, close failure without persistence, pre-dispatch procedure rejection, shared admission, cancellation, and profile lock/reopen invalidation. The linked JSON resource is test-only; it is not embedded in the application or loaded by production factories.

Existing kernel pipeline tests continue to cover framing/identity/reference mismatch, bank aborts, transport failures, and other protocol terminal paths. Qualification against an actual bank remains BR-09 after the authentication and account-verification workflow exists.
