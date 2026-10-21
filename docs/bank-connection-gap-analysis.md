# First bank connection: implementation gaps

Reviewed 2026-10-21 by tracing production desktop call sites. No live bank requests, credentials or visual testing were used. This investigation covers whether a user can establish a new connection, not only refresh an already prepared connection.

**Priority 1 follow-up:** Implemented the session-owned `BankConnectionSetupCoordinator` and the Connections **Connect** / **Cancel Setup** actions. A new endpoint-confirmed connection can now synchronize through the desktop action when a host supplies qualified procedure provenance. Missing provenance yields a specific pre-PIN capability message. Priorities 2–8 remain; the original blocking-path observations below describe the investigation baseline.

**Priority 2 follow-up:** Connect now performs a bounded, validated anonymous public BPD download and retains only closed observations for the current context. See [parameter acquisition](bank-parameter-acquisition.md). Explicit procedure selection and login-specific parameter qualification remain priority 3; anonymous data does not enable authenticated synchronization by itself.

**Priority 3 follow-up:** Implemented the [explicit chooser and identified qualification exchange](bank-procedure-selection.md), volatile scoped selection and lock/reopen requalification. Connect can now enter the existing synchronization workflow without programmatic procedure injection for the supported no-interactive-approval configuration. TAN media discovery and credential-required parameter bootstrap remain unsupported; signed approval and the later discovery/read desktop path still need work.

**Priority 4 follow-up:** Completed [owned PIN admission and durable synchronization feedback](bank-sign-in-persistence.md). Sign-in reserves the connection before prompting, preserves durable state across rejection/uncertain close/save failure, and reports received-but-unsaved system state explicitly. Scripted reopen tests cover each outcome. Signed interactive approval remains priority 5.

**Priority 5 follow-up:** Implemented [signed process-2 text-TAN approval](bank-signed-approval.md) for initial synchronization. Connect now sends an actual `HKTAN` process-4 synchronization request for eligible text-TAN procedures, validates request-bound `HITAN` challenges, collects a single-use owned TAN credential, dispatches a PIN+TAN-signed `HKTAN` process-2 continuation at message 2, verifies confirmation before closing at message 3 (`HKEND`), and enforces expiry, cancellation, rejection, and no-retry boundaries. Decoupled polling, TAN-medium selection, and visual/HHD challenges remain unsupported; priorities 6–8 remain.

**Priority 6 follow-up:** Connections now offers **Discover Accounts**, with owned PIN acquisition, closed discovery, explicit account review and durable capability activation. New accounts need no manual prerequisite; matching and ambiguity protections remain enforced. See [desktop discovery](bank-desktop-discovery.md). First-balance orchestration (priority 7) and live qualification (priority 8) remain.

**Priority 7 follow-up:** Added selected-connection **Refresh Balances** in Connections, using the existing durable batch service and shared progress/cancellation. After lock/unlock it explicitly requalifies missing procedure state. Typed blocker feedback distinguishes setup, discovery and procedure requirements; read/save/login outcomes remain separate. A scripted desktop test reaches a durable first balance without procedure injection or direct store mutation. See [first balance](bank-first-balance.md). Priority 8 live qualification remains.

**Priority 8 preparation:** Added an [exact live qualification worksheet](bank-live-qualification.md) and offline candidate inventory script. Live qualification remains pending a named target, verified product/endpoint, explicit authorization overriding the earlier live-test restriction, and operator-supplied evidence. No bank request or compatibility claim has been made.

## Finding

The supported desktop path now runs from connection setup through qualified procedure selection, synchronization, explicit account discovery/review and a first durable balance read. This path is covered by scripted responses and headless tests. Unsupported authentication/read variants remain blocked, and no exact live bank/product/procedure combination has been qualified. The observations below record the original investigation baseline, before priorities 1–7.

## Original blocking path (investigation baseline)

1. `ConnectionsView.ConfigureConnectionAsync` saves bank/login details and offers endpoint confirmation. It does not invoke authentication, synchronization or discovery. `HostSession.AddInstitutionAndConnection` deliberately saves `Unconfigured`, no assigned system ID, and `IsSynchronized = false`.
2. `BankBatchRefreshService` rejects a connection unless it is `Connected`, synchronized and endpoint-confirmed. It does not invoke initial synchronization to satisfy those conditions. Therefore clicking either refresh button cannot initialize a new connection.
3. The separate `AuthenticateAndSynchronizeBankConnectionAsync` API exists but has no production desktop caller. It also requires the caller to supply bank-advertised procedure provenance and explicit signature selection.
4. The default procedure provider begins empty. The only population path is the programmatic `SelectBankProcedure` API; no production desktop action calls it. The selection is volatile and is cleared on lock/relevant context changes. No desktop bootstrap retrieves the initial parameters and populates a procedure choice. Kernel parsers/writers are available, including anonymous initialization primitives, but are not a complete, bank-qualified bootstrap service.
5. Both refresh buttons use `BankBatchRequest` defaults, where `DiscoverAccounts = false`. The batch discovery/review branch exists, but the desktop provides no first-account discovery action. A manual account is intentionally quarantined and has no verified bank balance capability. “Relink Accounts” is not a substitute for acquiring fresh bank evidence.
6. The production candidate factory explicitly rejects interactive SCA construction. Credential/challenge dialogs and kernel coordinators do not supply the missing signed TAN continuation and status-query builders. A bank requiring such approval cannot complete the intended desktop flow.

## Required implementation, in dependency order

| Priority | Work | Completion evidence |
| --- | --- | --- |
| 1 | Add a session-owned first-connection coordinator and an explicit **Connect / Set Up Online Access** action | A new unconfigured connection reaches synchronization through ordinary desktop actions, without test-only store mutations |
| 2 | Acquire initial bank parameters over the confirmed endpoint; validate bank/login/product scope, supported versions and permitted procedures | Scripted bootstrap success, rejected/mismatched/malformed responses, cancellation and context changes; no invented procedure evidence |
| 3 | Present supported bank-advertised procedure/media choices and retain scoped parameter state | No automatic unsupported fallback; stale/changed bank context invalidates or revalidates cached evidence; reconnect after lock works without programmatic injection |
| 4 | Wire the owned banking PIN prompt and system-ID synchronization; persist only accepted closed results | New system ID and connected state survive reopen; rejection, uncertain close and failed saves preserve previous state |
| 5 | Implement signed interactive approval for the chosen supported procedure | Challenge binding, TAN ownership, expiry/cancel/reject paths; bounded signed status polling if decoupled approval is included |
| 6 | Run discovery, explicit account review and capability activation from the desktop setup flow | Manual accounts match bank evidence without losing identity/history; ambiguous accounts stay blocked; users can discover accounts without creating them manually first |
| 7 | Invoke the existing balance batch service and distinguish setup/login/discovery/read/save outcomes | First durable balance and retrieval time survive reopening; later refreshes work after a normal lock/unlock |
| 8 | Qualify one exact bank/product/procedure combination after separate authorization | Dated read-only integration evidence and a bounded compatibility row; no universal support claim |

Parameter acquisition and SCA may require several exchanges rather than this table's conceptual sequence. The coordinator must model the actual supported bank procedure instead of assuming every bank authenticates in one fixed order.

## Deployment and user prerequisites

The host reads `BROILER_FOND_FINTS_PRODUCT_ID` and `BROILER_FOND_FINTS_PRODUCT_VERSION` when creating its session. An assigned product identity must be configured; there is currently no desktop settings editor for it, and syntactic acceptance does not verify registration approval. This review did not inspect the running app's environment or verify an external registration.

The user must have a writable unlocked profile outside Safe Mode, correct bank code and FinTS HTTPS endpoint, actual bank login/customer identity, enabled bank access and a supported authentication procedure. The banking PIN is separate from the encrypted profile passphrase. Correct settings alone cannot repair the missing implementation above.

## Feedback gap

The batch service already distinguishes unsynchronized setup, missing procedure provenance and unverified accounts, but the desktop formatter reduces several of those cases to generic recovery text. A connection with no account rows can show account-setup guidance even when synchronization is the immediate blocker. Introduce typed setup/authentication failure reasons and render the specific required next action; continue excluding arbitrary bank/exception free text. This would make it clear whether a user can correct configuration or the build lacks a required capability.

## What can be reused

Confirmed HTTPS transport; endpoint/login persistence and invalidation; owned PIN prompts; production synchronization and read candidate construction; separate synchronization/discovery APIs; reviewed account matching; strict target balance ingestion; durable per-account commits; batch concurrency/backoff; shared progress, cancellation and redacted feedback. The main missing work is upstream first-connection orchestration, parameter lifecycle, signed SCA and desktop entry points.

See [qualification limits](bank-refresh-qualification.md) and the [balance refresh roadmap](balance-refresh-roadmap.md).
