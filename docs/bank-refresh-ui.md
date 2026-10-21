# Bank refresh progress and feedback (BR-08)

Status: the supported desktop integration slice is implemented. Both **Refresh Balances** and **Sync All** call the same session-owned batch workflow. Verification uses scripted transport and headless layout tests; no live bank or visual testing was performed.

## Workflow

`BankRefreshPresentation` unlocks the selected profile, checks writable mode and product configuration, and invokes the BR-07 batch service. Both entry points share progress, cancellation and duplicate-operation prevention. The desktop installs a queued UI dispatcher; prompts, projections and control updates resume on that dispatcher while transport awaits remain asynchronous.

Progress distinguishes setup, waiting for a connection slot, PIN entry, discovery, review, balance retrieval and durable saving. The completion dialog reports updated, unchanged, unavailable, unsupported, failed and cancelled accounts, recovery guidance and refreshed totals partitioned by currency. Long feedback uses the existing scrollable alert layout. Cancellation preserves completed saves and previous cached data for unfinished accounts. Profile locking clears retained account results and cancels active work; window disposal suppresses late completion dialogs.

Failures do not manufacture zero balances or fresh timestamps. A received-but-unsaved balance explicitly directs the user to check storage access. Backend explanation strings and arbitrary exception messages are never copied into refresh feedback. Each attempt has a support reference. Opt-in diagnostic export contains only bounded timestamped stage/outcome codes, without credentials, account identities, balances or raw bank payloads. Opting out hides detailed stages from export.

## Procedure provenance and remaining scope

The host can supply an `IBankProcedureProvider`, or explicitly select qualified bank procedure provenance with `HostSession.SelectBankProcedure`. The default session selection is volatile and invalidated by locking, profile changes and relevant connection edits. Missing provenance stops before requesting a PIN. It is never replaced by guessed procedure parameters.

The ordinary desktop still lacks full bank-parameter acquisition and a procedure-selection UI. Signed SCA and durable BPD/UPD caching remain BR-04/BR-05 work. Consequently the default desktop configuration can explain why refresh is unavailable; this milestone does not claim general live-bank readiness. Awaiting-approval UI and safe numeric bank return-code reporting must be integrated when those typed protocol paths are supported. Current feedback uses fixed outcome categories rather than untrusted bank free text.

## Verification

`BankRefreshPresentationTests` exercises both buttons with the real batch service and scripted protocol responses: missing provenance before PIN, durable reads, unchanged amounts, stage ordering, dispatcher affinity, duplicate dispatch, cancellation, credential disposal, redacted diagnostic opt-in/out, storage recovery feedback, lock invalidation and unlocking the previously selected profile. Existing batch and balance tests cover partial success and failed persistence; dialog geometry tests measure and arrange long warnings at small sizes without opening windows.

Run the presentation and kernel test runners and build `src/Broiler.Fond.Host.Desktop`. BR-09 remains responsible for release qualification and any separately authorized bank integration exercise.
