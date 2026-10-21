# Desktop balance refresh roadmap

- **Status:** BR-01 through BR-03 and supported BR-06/BR-07/BR-08 slices implemented. BR-09 deterministic qualification and documentation are implemented; live qualification and release approval remain pending on incomplete BR-04/BR-05 dependencies. Both desktop buttons invoke the batch workflow, but the default desktop still requires bank procedure acquisition/selection before live use. See [qualification scope and matrix](bank-refresh-qualification.md).
- **Baseline reviewed:** 2026-10-21.
- **Scope:** User-initiated, read-only FinTS balance retrieval in `Broiler.Fond.Host.Desktop`.
- **Related:** [Product roadmap](roadmap.md), [Broiler UI migration](broiler-ui-roadmap.md), [ADR 0145](adr/0145-broiler-ui-cross-platform-presentation-architecture.md).

## 1. Outcome and boundaries

**First-connection follow-up:** Priorities 1 and 2 now provide a desktop Connect coordinator and [anonymous public parameter acquisition](bank-parameter-acquisition.md). The next dependency is explicit procedure selection and login-specific qualification; downloading public BPD alone does not complete BR-04/BR-05 or establish a connected bank session.

Priority 3 now supplies [explicit procedure choice and identified qualification](bank-procedure-selection.md) for the supported no-interactive-approval path. Media discovery, credential-required bootstrap, signed SCA and the remaining desktop discovery/read path still prevent a general live-bank support claim.

Priority 4 completes [pre-PIN admission and durable synchronization recovery](bank-sign-in-persistence.md) for that path. Signed interactive approval remains the next first-connection gap; no live bank is qualified.

Priority 5 implements [signed process-2 text-TAN interactive approval](bank-signed-approval.md) for initial synchronization, including challenge binding, single-use TAN ownership, expiry/cancel/reject handling and message-3 dialogue closing. Decoupled polling, visual/HHD challenges, TAN-medium discovery, read-pipeline signed TAN continuation and the remaining desktop discovery/read steps remain outstanding; no live bank is qualified.

A user configures a bank connection, confirms its endpoint, signs in, verifies discovered accounts, and retrieves balances. A successful refresh updates the encrypted profile and account projections. An unsuccessful refresh preserves the previous balances and explains which stage failed and what the user can do next.

The first supported slice is one explicitly initiated FinTS PIN/TAN connection, one supported bank authentication procedure, and one verified EUR current account. Expand to multiple accounts, supported procedures, and currencies after that slice passes deterministic tests. Support follows bank-advertised capabilities; configuring an IBAN never establishes those capabilities.

Excluded from this track: payments, transaction downloads, EBICS, unattended refresh, automatic account activation, currency conversion, and blanket compatibility claims for all banks. Existing saved balances remain readable even if an online operation fails.

This is an implementation roadmap, not a claim that the kernel or the retired host already supplies a production-ready desktop banking workflow. Milestone status is recorded below; kernel primitives alone do not establish online readiness.

## 2. Verified starting point

| Area | Existing implementation | Remaining integration or limitation |
| --- | --- | --- |
| Refresh buttons | `AccountsView` and `BroilerMainWindow` call `BankRefreshFeedback` | Explain unavailability; do not issue bank requests |
| Local projection | `HostSession.ReloadDataInternal` maps profile balances to `AccountViewModel` | Reloading is not synchronization; must run after a successful bank-data commit |
| Manual setup | `BroilerAccountSetupDialog` and `HostSession.AddManualAccount` save validated account details | Manual bindings use connector `Manual`, state `Quarantined`, and no bank capabilities or invented balances |
| Connection setup | `HostSession.AddInstitutionAndConnection` saves an unconfirmed, unconfigured connection | Login identity is currently a default value unless supplied programmatically; no initial synchronization |
| Endpoint confirmation | `BroilerEndpointConfirmationDialog` exists | Confirmation must be persisted and tied to the exact endpoint actually used |
| Credentials | Credential and SCA dialogs, `FinTsSessionCredential`, `EphemeralSessionCredentialStore` exist | Need scoped lifetime ownership and cancellation; existing string-returning contracts do not guarantee deterministic erasure of every managed copy |
| Initial synchronization | `FinTsSynchronizationExecutionPipeline` exists | Needs desktop orchestration and production request construction |
| Discovery and balance reads | `FinTsReadExecutionPipeline` supports `AllAccountDiscovery` and `BalanceRefresh` | Needs initialization/read/continuation/closing candidate factories; balance mode requires a target binding |
| Multibank orchestration | `MultibankSyncCoordinator` handles connection scheduling, backoff, and ingestion commits | Current execution selects all-account discovery; it is not an account-balance loop |
| Transport | `FinTsHttpsTransport` exists | Needs composition, disposal, timeouts, cancellation, and endpoint confirmation enforcement |
| Persistence | `MultibankSnapshotMutator`, `IProfileStore.MutateAndSave`, identity ledger, authenticated generations exist | Need end-to-end verification of discovery/balance commits and concurrent profile changes |
| UI truthfulness | Missing balances are unavailable; totals are cached; retrieval timestamps are shown | Keep those distinctions throughout online progress, failure, and partial success |
| Product identity | `KernelProduct.FinTsRegistration` contains a registration configuration | Verify its suitability for actual bank access; do not infer registration approval from its presence in source |

Relevant source and test files:

- [Accounts actions](../src/Broiler.Fond.Presentation/Views/AccountsView.cs), [main window](../src/Broiler.Fond.Presentation/Views/BroilerMainWindow.cs), [session](../src/Broiler.Fond.Presentation/Sessions/HostSession.cs), [current refresh feedback](../src/Broiler.Fond.Presentation/Services/BankRefreshFeedback.cs).
- [Read pipeline](../src/Broiler.Fond.Kernel/FinTs/FinTsReadExecutionPipeline.cs), [synchronization pipeline](../src/Broiler.Fond.Kernel/FinTs/FinTsSynchronizationExecutionPipeline.cs), [SCA execution](../src/Broiler.Fond.Kernel/FinTs/FinTsScaExecutionCoordinator.cs).
- [Multibank coordinator](../src/Broiler.Fond.Kernel/Domain/Connections/Orchestration/MultibankSyncCoordinator.cs), [snapshot mutator](../src/Broiler.Fond.Kernel/Domain/Connections/Orchestration/MultibankSnapshotMutator.cs), [HTTPS transport](../src/Broiler.Fond.Kernel/FinTs/Transport/FinTsHttpsTransport.cs).
- [Read pipeline tests](../tests/Broiler.Fond.Kernel.Tests/FinTsReadExecutionPipelineTests.cs), [synchronization tests](../tests/Broiler.Fond.Kernel.Tests/FinTsSynchronizationExecutionPipelineTests.cs), [SCA tests](../tests/Broiler.Fond.Kernel.Tests/FinTsScaExecutionPipelineTests.cs), [desktop feedback tests](../tests/Broiler.Fond.Presentation.Tests/BankRefreshFeedbackTests.cs).

## 3. Intended data flow and ownership

```text
Refresh Balances / Sync All
  → presentation refresh workflow: preflight, progress, cancellation
  → confirm configured bank endpoint and acquire ephemeral credentials
  → synchronize connection if required
  → discover accounts/capabilities if required
  → resolve manual and discovered account identities with user review
  → execute balance pipeline for each eligible account
  → validate bank evidence and prepare immutable ingestion result
  → recheck profile/session/connection identity and commit atomically
  → rebuild projections on UI dispatcher
  → report per-account results and cached-data age
```

- **Kernel:** Protocol framing, transport policy, authentication state, bank evidence, account identity, monetary semantics, immutable transformations. No UI dependencies.
- **Presentation:** Dialog decisions, a testable refresh coordinator, progress/result models, eligibility explanations, and mapping outcomes to user actions. Avoid growing `HostSession` into a protocol engine.
- **Desktop host:** Composition, dispatcher access, lifecycle cancellation, and resource ownership. No protocol serialization inside click handlers.
- **Storage:** The commit boundary. UI success is emitted only after authenticated persistence succeeds.

BR-01 now supplies `IBankRefreshService`, `BalanceRefreshRequest`, `BalanceRefreshProgress`, and `BalanceRefreshResult`. The session owns a coordinator with an unavailable production execution path. Its internal operation seam, backoff tracker, and clock are injectable for deterministic tests. Later milestones must integrate transport, request builders, credential prompts, and UI dispatch without global state.

## 4. Milestones and acceptance criteria

### BR-01 — Refresh contract, eligibility, and lifecycle

**Status:** Implemented 2026-10-21. No production bank execution or durable balance commits are enabled.

**Dependencies:** None. **Deliverable:** Presentation workflow contract and deterministic orchestration tests.

- Define per-account and per-connection results with stable outcome codes, stage, safe explanation, recovery action, and committed-data counts.
- Distinguish profile locked/read-only, Safe Mode, missing login details, unconfirmed endpoint, manual/unverified binding, unsupported balance operation, and connection backoff.
- Define one active workflow per connection and bounded concurrency across connections. Serialize interactive credential/SCA prompts.
- Capture profile ID, branch ID, connection ID, endpoint revision, and a session epoch before dispatching work. Lock, profile switch, endpoint edit, connection removal, and disposal invalidate that work.
- Keep unsupported and blocked accounts visible with reasons; do not silently skip them.

**Acceptance:** No transport is invoked for ineligible input. Duplicate clicks do not duplicate requests. Cancellation and profile changes produce explicit terminal outcomes. Unsupported requests never report success.

Implementation evidence:

- [Contracts](../src/Broiler.Fond.Presentation/Services/BalanceRefreshContracts.cs) define connection/account results, actionable stable outcome codes, stages, committed counts, and captured profile/branch/session/endpoint context.
- [Coordinator](../src/Broiler.Fond.Presentation/Services/BankRefreshService.cs) provides eligibility checks, duplicate suppression, bounded connection concurrency, a shared prompt gate, injected backoff/clock, and safe terminal failure results. Inactive/manual/unsupported accounts stay in results and are excluded from eligible account IDs.
- [Session integration](../src/Broiler.Fond.Presentation/Sessions/HostSession.Refresh.cs) checks context under the session lock. Lock/reopen changes the session epoch; relevant endpoint, login, binding, capability, or connection changes invalidate captured work. Exact endpoint/configuration equality supplies the BR-01 endpoint revision; persisted edit revisions belong to BR-02.
- [Headless tests](../tests/Broiler.Fond.Presentation.Tests/BankRefreshServiceTests.cs) cover preflight blockers, mixed eligibility, duplicate requests, progress order, connection and prompt concurrency, active/queued cancellation, late results, profile switch, endpoint/login edits, removal, disposal, and safe exception reporting. The existing feedback tests cover both desktop buttons.
- Cancellation is cooperative at the operation boundary. Later bank executors must honor the supplied cancellation token, use the shared prompt gate, and obtain the guarded commit boundary introduced in BR-06. The BR-01 seam has no writable store and never reports successful commits.

### BR-02 — Bank setup, endpoint trust, and product identity

**Status:** Implemented 2026-10-21. The deployment must still supply its assigned application registration number; syntax validation does not establish registration approval or live bank interoperability.

**Dependencies:** BR-01. **Deliverable:** A persisted bank context suitable for authentication.

- Add/edit bank user ID and optional customer ID; distinguish both from the profile passphrase. Replace the default `default-user` in real bank workflows with explicit user input.
- Persist endpoint confirmation only after the user confirms the displayed scheme, host, port, and relevant path. Revoke confirmation on endpoint changes; never auto-confirm a manually typed URL.
- Map storage connection/system-status values to kernel types explicitly. Handle legacy values written by earlier desktop builds without inventing bank contact or deleting saved accounts.
- Verify usable FinTS product identification and registration configuration for the intended test/release environment. Explain missing configuration before requesting credentials.
- Validate HTTPS transport behavior, redirect handling, timeout policy, and certificate failures against the existing transport implementation. Credentials must not be forwarded to an unconfirmed replacement endpoint.

**Acceptance:** Save/reopen retains login identity and endpoint trust correctly. Changed endpoints block requests until confirmed again. Configuration failures are actionable and never masquerade as invalid PIN errors.

Implementation evidence:

- [Setup guide](bank-connection-setup.md) documents the add/edit/confirm workflow, review revisions, legacy normalization, product environment settings, and transport boundary.
- [Setup/session handling](../src/Broiler.Fond.Presentation/Sessions/HostSession.ConnectionSetup.cs) uses persisted profile generations for stale-review checks and invalidates bank session evidence when endpoint/login/bank context changes.
- [Configuration mapping](../src/Broiler.Fond.Presentation/Services/BankConnectionSetup.cs) reuses kernel endpoint/login validation, normalizes known legacy patterns without deleting accounts, and fails closed on unknown states.
- [Product configuration](../src/Broiler.Fond.Presentation/Services/BankProductConfiguration.cs) and [confirmed transport](../src/Broiler.Fond.Presentation/Services/ConfirmedBankTransport.cs) prevent placeholder product fallback and unreviewed endpoint replacement.
- [Headless tests](../tests/Broiler.Fond.Presentation.Tests/BankConnectionSetupTests.cs) exercise setup, editing, acknowledgement bypass prevention, persistence, stale/edited/reverted review rejection, normalization, product blockers, redirects, TLS failures, timeouts, cancellation, and profile-lock invalidation. No live bank verification is claimed.

### BR-03 — Production request builders and connection synchronization

**Status:** Supported construction/synchronization slice implemented 2026-10-21. Interactive SCA/status-query adapters remain explicitly unsupported until BR-04; online balance refresh is not enabled.

**Dependencies:** BR-01, BR-02. **Deliverable:** A real request construction path, not test-fixture factories.

- Inventory all required initialization, synchronization, discovery, balance, continuation, SCA, status-query, and closing candidate factories. Identify which serializers already exist and which adapters are missing.
- Construct candidates from the validated connection context, bank parameters, negotiated versions, system ID, dialogue ID, message numbering, and product configuration.
- Run initial synchronization for unsynchronized connections and persist validated system/connection state. Reuse synchronized state only when compatible with the current endpoint and bank response.
- Close dialogues on normal completion and make a bounded best-effort close on eligible failure paths. A close failure must not hide the primary failure or falsely claim data loss.
- Fail with a specific missing-builder/unsupported-version result before network dispatch when request construction is unavailable.

**Acceptance:** Scripted transport tests verify exact request bytes, dialogue/message correlation, and success/failure state transitions. No arbitrary constants, pre-recorded customer identifiers, or fixture-only request builders enter the production path.

Implementation evidence:

- [Builder inventory and integration guide](bank-request-construction.md) documents supported request schemas, input provenance, protocol constants, unavailable SCA adapters, cleanup policy, and remaining dependencies.
- [Production candidate factory](../src/Broiler.Fond.Kernel/FinTs/FinTsProductionCandidateFactory.cs) constructs synchronization, read initialization, discovery, qualified balance versions, continuation and closing candidates using current configuration and bank evidence.
- [Session synchronization](../src/Broiler.Fond.Presentation/Sessions/HostSession.Synchronization.cs) uses explicit product configuration and a confirmed transport, shares admission with BR-01, persists validated closed synchronization state, and rejects stale context at commit.
- [Kernel tests](../tests/Broiler.Fond.Kernel.Tests/FinTsProductionCandidateFactoryTests.cs) and [presentation tests](../tests/Broiler.Fond.Presentation.Tests/BankSynchronizationTests.cs) use scripted bank observations to verify outgoing production construction, persistence, lifecycle handling, and failure behavior. No live bank requests were performed.

### BR-04 — PIN ownership and bank-controlled SCA

**Status:** Credential ownership increment implemented; signed interactive SCA and bounded signed polling remain unfinished. See [authentication integration and procedure matrix](bank-authentication.md) for the supported scope, memory limits, and remaining work. BR-04 is not complete.

**Dependencies:** BR-01; integrate with BR-03. **Deliverable:** A cancellable authentication interaction.

- Acquire a banking PIN immediately before the operation; never reuse the profile passphrase as a banking PIN.
- Prefer disposable credential buffers through the workflow. Audit current `string` results and `SecureInputWrapper` copying; document remaining runtime limits rather than claiming all managed copies are erased.
- Clear editable fields and dispose owned credential buffers on acceptance, cancellation, rejection, expiry, profile lock, and shutdown. Never persist PIN/TAN values.
- Support the first explicitly selected TAN procedure end to end, then expand through a capability matrix. Text/TAN entry, visual challenge rendering, and decoupled app approval have separate acceptance cases.
- Connect challenge responses and bounded decoupled polling to kernel SCA continuation APIs. Respect bank timing, expiry, maximum queries, cancellation, and user rejection.
- Do not retry rejected credentials automatically or show simultaneous bank credential prompts.

**Acceptance:** Headless scripted challenges complete or terminate deterministically. Secret markers do not appear in logs, exceptions shown to users, snapshots, or support exports. Unsupported challenge types explain the limitation and stop.

### BR-05 — Discovery and verification of manual account identities

**Status:** The supported IBAN-based discovery/review/persistence slice is implemented. Full parameter-cache persistence, signed SCA dependencies, broader identity resolution, and desktop action wiring remain. See [discovery integration and verification](bank-account-discovery.md). BR-05 is not complete.

**Dependencies:** BR-03, BR-04. **Deliverable:** Verified account bindings with bank-advertised capabilities.

- Run account discovery after required authentication/synchronization and persist validated bank parameters and account evidence.
- Compare bank-reported accounts to manual entries using existing scoped identity/rediscovery rules. IBAN equality alone must not merge different bank connections or account lifetimes.
- Present an explicit review when associating discovered evidence with a manual account. Preserve its local account ID, display name, and existing history where the domain rules allow relinking.
- Do not activate a `Manual` connector by merely changing its state string. Produce a validated bank binding through the existing identity machinery; preserve provenance and avoid competing active bindings.
- Handle already-known accounts, multiple matches, closed/reopened accounts, currency disagreement, and accounts absent from the bank response.
- Persist each account's supported balance operation and negotiated version; unsupported account types remain visible as such.

**Acceptance:** A confirmed match preserves local identity and becomes eligible for a supported bank balance read. Ambiguous/manual-only accounts remain unverified with clear recovery guidance. Repeated discovery does not duplicate accounts.

### BR-06 — Single-account balance read and durable ingestion

**Status:** Supported API-level single-account read and durable-ingestion slice implemented and verified with scripted transport. The discovery → balance → saved profile → reopened projection path passes headless tests. Signed SCA, full parameter caching, and desktop action wiring remain separate outstanding dependencies. See [balance integration and limits](bank-balance-refresh.md).

**Dependencies:** BR-03 through BR-05. **Deliverable:** The first complete vertical slice.

- Execute `FinTsReadExecutionPipeline` in `BalanceRefresh` mode with the verified target binding and negotiated request factories.
- Process supported continuation pages with bounded limits; validate response correlation, account identity, currency, and bank evidence before mutation.
- Preserve booked and available balance semantics independently. Missing available balance is unavailable, not a copy of booked balance; a valid zero remains zero.
- Retain bank-reported timestamps separately from local retrieval time. Never label local reload time as retrieval time.
- Audit and reuse `MultibankSnapshotMutator.ApplyIngestionResult` where it satisfies balance requirements. Commit using `MutateAndSave` against the latest snapshot, preserving unrelated concurrent edits and ledger references.
- Recheck the captured session epoch and connection context at commit. Discard results if the user locked/switched profiles, removed the connection, or changed its endpoint while a request was running.
- On persistence failure, report that data was received but not saved. Do not update the displayed durable balance or report success.

**Acceptance:** One refresh changes the expected balance documents and ledger only, survives profile reopening, and renders the returned values. Failed/cancelled/stale operations preserve the previous saved balances and timestamps.

### BR-07 — Multiple accounts and Sync All

**Status:** Supported API-level batch orchestration implemented: one scoped PIN per connection, optional reviewed discovery followed by real balance reads, shared connection/institution admission, account-granular commits, backoff, six outcome counts and currency-separated refreshed totals. Desktop action wiring and remaining authentication/parameter dependencies are outstanding. See [batch integration and verification](bank-batch-refresh.md).

**Dependencies:** BR-06. **Deliverable:** Bounded refresh across eligible accounts and connections.

- Extend orchestration beyond its current discovery-only execution. Refresh eligible balances after discovery rather than treating discovery success as balance success.
- Share the authenticated bank context only where supported by protocol/session ownership. Avoid a PIN prompt per account when one authorized session can serve the connection.
- Keep per-institution concurrency and backoff rules. Unrelated banks can complete independently; one rejection must not prevent reporting other successful results.
- Define commit granularity explicitly: connection/account batches may succeed independently; an incomplete individual balance response must not become a valid snapshot.
- Report updated, unchanged, unavailable, unsupported, failed, and cancelled counts. Do not label unchanged amounts as failures if a valid read occurred.

**Acceptance:** Tests with multiple banks include mixed success, authentication failure, timeout, disabled accounts, and cancellation. Only durable successes count as updated; aggregate totals remain partitioned by currency.

### BR-08 — Progress, recoverable errors, and truthful UI

**Status:** Supported desktop slice implemented 2026-10-21; see [workflow, verification and remaining dependencies](bank-refresh-ui.md). Signed approval progress and safe numeric bank codes remain dependent on unfinished authentication/parameter integration.

**Dependencies:** BR-01; progressively integrate BR-02 through BR-07. **Deliverable:** Both refresh actions use the same real workflow.

- Show current stage: checking setup, signing in, awaiting approval, discovering accounts, retrieving balances, saving, completed/partially completed/failed/cancelled.
- Offer cancellation and prevent duplicate dispatch. Keep navigation responsive; marshal projection and control updates through the UI dispatcher.
- Replace the current unconditional `BankRefreshFeedback` limitation message only when a working supported flow is available. Preserve a meaningful unsupported-feature path for other configurations.
- Present a compact completion summary with per-account details, useful bank return codes where safe, and explicit recovery actions. Do not expose raw protocol payloads or credential-bearing exception text.
- Preserve cached amounts on failure; show the recorded retrieval age and latest attempt result separately. Missing balances remain unavailable, not zero.
- Add redacted diagnostic correlation IDs for support, with stage and safe failure codes. Make detailed diagnostics opt-in according to existing application policy.
- Retain scrollable dialog content and wrapping actions; verify geometry for longer bank warnings and approval instructions without requiring live UI automation.

**Acceptance:** Both buttons report the same outcomes. UI tests cover progress ordering, user cancellation, partial success, profile access failure, persistence failure, and stale cached data. No warning is replaced by a misleading success badge.

### BR-09 — Release qualification and documentation

**Status:** Deterministic qualification/documentation slice implemented 2026-10-21. [Evidence and supported matrix](bank-refresh-qualification.md) explicitly record no qualified live bank. Full milestone acceptance remains pending on BR-04/BR-05 completion, separately authorized integration evidence and immutable release-candidate review.

**Dependencies:** BR-01 through BR-08. **Deliverable:** A documented, bounded support claim.

- Run kernel and presentation suites plus desktop build/trimming checks required by repository CI.
- Use deterministic scripted bank responses for protocol and UI workflow verification; keep CI independent of external banks and credentials.
- After deterministic acceptance, perform a separately authorized read-only integration exercise against an appropriate bank test environment or user-approved account. Never run it implicitly as part of tests or this roadmap task.
- Record the actual supported bank/procedure/version matrix and remaining unsupported cases. Validate product registration and endpoint requirements before claiming bank interoperability.
- Update setup help, troubleshooting, and the product/UI roadmaps. Migration completion must not be described as proof that online banking functions are integrated.

**Acceptance:** Reproducible evidence supports the claimed configuration; no secrets are committed or exported. Untested banks/procedures are described as unqualified, not universally supported.

## 5. Error and recovery contract

| Condition | User-facing explanation | Recovery and data behavior |
| --- | --- | --- |
| Locked/read-only profile or Safe Mode | Profile cannot accept refreshed balances | Unlock/resolve mode; no dispatch or writes |
| Missing bank login or unconfirmed endpoint | Connection setup is incomplete | Edit login details/confirm endpoint; do not request or send PIN prematurely |
| Missing production request builder | This operation is not supported by this build | Explain product limitation; no pointless credential retry |
| Invalid/rejected PIN or bank login | Bank sign-in was rejected | Correct credentials; no automatic retry loop |
| SCA required, expired, or rejected | Bank approval is pending or failed | Explicit retry/new challenge where permitted; clear old secrets |
| Manual, ambiguous, or unsupported account | Account is not eligible for this balance read | Discover/review binding or explain unsupported capability |
| Timeout, network interruption, certificate failure | Bank could not be contacted securely | Preserve cache; retry transient failures subject to backoff; never bypass certificate validation |
| Bank maintenance/backoff | Bank temporarily unavailable | Show permitted retry timing; preserve cache |
| Invalid/mismatched bank response | Response could not be validated for this account | Preserve cache; safe code and diagnostic ID; no blind retries |
| No balance returned | Bank did not provide that balance kind | Preserve historical data with an explicit current-attempt result; never substitute zero |
| Save failure | Balance received but could not be saved | Preserve prior durable view; explain storage recovery; no success count |
| Profile/endpoint changed while running | Operation cancelled because its context changed | Discard result; never commit into another profile or connection |
| Some accounts succeed | Partial completion with account-level results | Keep successful commits and explain the rest |

Bank-provided free text is untrusted display data. Sanitize/redact it before presentation or diagnostic storage. Distinguish developer/programming faults from retryable bank failures.

## 6. Verification matrix

| Layer | Required evidence |
| --- | --- |
| Request builders | Negotiated versions; exact bytes; message numbering; dialogue correlation; unsupported candidate paths |
| Synchronization | First sign-in; reused system state; changed endpoint; bank rejection; stale legacy configuration |
| Authentication/SCA | PIN cancellation/rejection/expiry; TAN success/failure; supported visual/push flows; bounded polling |
| Discovery | Manual match; ambiguity; no match; multiple connections; closed/reopened lifetime; currency conflict; repeat discovery |
| Balance semantics | Positive/negative/zero amounts; booked-only; absent available amount; multiple currencies; timestamp absence; continuation limits |
| Storage | Save/reopen; ledger validity; failure injection; concurrent local edits; failed commit preserves previous generation |
| Lifecycle | Double click; simultaneous connections; lock/switch/remove/edit during request; shutdown; cancellation before/after network response |
| UI/reporting | Both entry points; partial success; no fabricated freshness/zero totals; long warning geometry; no swallowed failures |
| Privacy | Secret markers absent from logs, alerts, exceptions, snapshots, exports; owned credential buffers disposed |

Recommended commands for implementation changes:

```powershell
dotnet run --project tests/Broiler.Fond.Kernel.Tests
dotnet run --project tests/Broiler.Fond.Presentation.Tests
dotnet build src/Broiler.Fond.Host.Desktop
```

These commands are validation targets, not evidence that the planned workflow has already been implemented. Add focused cases to the existing runners as each milestone lands.

## 7. Delivery sequence and review checkpoints

1. **Contract and setup:** BR-01 and BR-02. Review eligibility, endpoint persistence, legacy-state handling, and user messages.
2. **Authenticated connection slice:** BR-03 and BR-04. Demonstrate synchronization with scripted transport and supported SCA; no live bank required.
3. **Verified single-account slice:** BR-05 and BR-06, with the relevant BR-08 UI. Demonstrate bank evidence → durable saved balance → reopened UI projection.
4. **Batch reliability:** BR-07 and remaining BR-08. Demonstrate independent partial results, bounded concurrency, and cancellation.
5. **Qualification:** BR-09. Publish only the supported behavior for which evidence exists.

Use separate reviewable changes for contracts/setup, request construction, credentials/SCA, identity verification, ingestion, and batch/UI reporting. Keep each intermediate build truthful about what is still unavailable. No calendar commitment is assigned until request-builder and authentication gaps have been sized.

## 8. Definition of done

- [ ] Both refresh entry points invoke a tested online workflow for the supported configuration.
- [ ] Login, endpoint trust, synchronization, and account verification have explicit persisted states.
- [ ] A manually entered account can be reviewed against bank evidence without losing local identity/history.
- [ ] The bank's booked/available balances and timestamps are saved without fabricated values.
- [ ] Profile lock/switch/connection edits cannot cause stale results to be committed.
- [ ] PIN/TAN lifetimes, SCA, cancellation, and backoff have verified ownership and limits.
- [ ] Partial success and actionable failures are visible per account/connection.
- [ ] Saved balances survive reopening; failed operations leave previous data intact.
- [ ] Deterministic protocol, persistence, lifecycle, and headless presentation tests pass.
- [ ] The supported bank/procedure matrix and any remaining integration limits are documented.
