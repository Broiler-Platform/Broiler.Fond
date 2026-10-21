# Multiple accounts and Sync All — BR-07

Status: the supported batch orchestration slice is implemented and tested. Both desktop refresh buttons now use it through [BR-08 progress and feedback](bank-refresh-ui.md). Signed SCA and full parameter-cache acquisition remain BR-04/BR-05 dependencies; no live-bank compatibility is claimed.

## Entry point and selection

`HostSession.BankBatchRefresh.RefreshAsync` accepts:

- `BankBatchRequest`: null connection IDs selects all configured connections; null account IDs selects accounts bound to those connections. IDs are deduplicated. An explicitly empty account selection does no bank work. Missing/unassigned requested accounts remain visible in the result.
- `IBankCredentialPrompt`: a separate disposable banking PIN prompt.
- `IBankProcedureProvider`: explicit bank procedure provenance and signature selection for each connection. Missing/unsupported provenance stops before collecting a PIN; there is no first-procedure or product fallback.
- An optional `IBankAccountDiscoveryReview`, required when `DiscoverAccounts` is requested.
- Optional progress and cancellation.

Default batch refresh reads existing reviewed, supported bindings. With `DiscoverAccounts = true`, it performs a production discovery exchange, explicit review, and durable review commit **before** reading eligible balances. Discovery completion alone never counts as a balance update. Cancelling review stops that connection without starting its balance reads. Unsynchronized connections remain blocked; batch refresh does not silently synchronize or activate accounts.

The existing kernel multibank coordinator remains a discovery-oriented workflow. This desktop batch service composes the production BR-05 discovery and strict BR-06 target-balance APIs rather than treating that coordinator's discovery success as a balance result. BR-08 routes desktop actions to this service; the BR-01 preflight API remains available separately.

## Scheduling and credential ownership

- One connection lease reserves the complete sequence, including waiting, sign-in, optional discovery/review, and all account reads. Duplicate batch calls and single-account/discovery/synchronization operations cannot own that connection at the same time.
- Institution gates are keyed by the profile's institution identity and shared by batch, synchronization, discovery and single-account balance entry points. Connections assigned to the same institution run serially. They wait for their institution **before** consuming batch worker slots, so a second login at a busy institution cannot starve an unrelated bank.
- Two connection workers are active by default. The session's existing two-operation network limit still applies, including requests started outside this batch service.
- Accounts within one connection run sequentially. Each balance uses a fresh initialization/read/continuation/close dialogue; dialogue IDs, counters and SCA outcomes are not shared across accounts.
- One owned PIN is collected per connection and reused only for the selected read operations of that connection, including optional reviewed discovery. The prompt explains that scope. Credentials are never shared across connections or persisted.
- Prompts and discovery review use the session-wide interaction gate. The gate is released during network work so unrelated bank sessions can progress.
- Each connection interaction has a five-minute deadline. Completion, rejection, cancellation, lock/profile switch, source-context changes, service disposal or shutdown disposes its PIN. Provider/prompt/reviewer implementations must honor their cancellation contract. Rejected credentials stop further reads on that connection without re-prompting or automatically retrying the PIN.

Unlike BR-05's standalone helper, the batch intentionally retains its scoped PIN through optional account review so the user does not have to enter it again for the immediately following authorized balance reads. It remains bounded, disposable and limited to that connection. No guarantee is made that every managed/UI-runtime copy can be overwritten; see [credential memory limits](bank-authentication.md).

## Backoff and independent failures

The batch uses the existing `ConnectionBackoffTracker` retry policy. Transport failures record a fixed, redacted reason, stop remaining reads on that connection, and start its backoff window. Backoff is checked before sign-in and again after waiting for scheduling slots. Repeated calls share the tracker for the current profile session; changing the selection cannot bypass it. A successful durable read resets that connection's transient-failure history.

A new profile/session epoch gets a fresh tracker so reused numeric connection IDs do not inherit another profile's retry history. Backoff is runtime batch policy, not a persistent bank lockout or a credential-retry mechanism. Low-level caller-owned single-operation APIs retain their explicit caller-managed retry contract.

One bank's rejection, timeout, unsupported account or unavailable procedure does not cancel peer institutions. A save failure belongs to the affected account; later independently eligible accounts may still complete. An authentication failure or cancellation stops remaining accounts of that connection. Disposing the batch service cancels its active runs and lets them unwind their reservations, institution gates and credential owners.

## Commit granularity and truthful results

The durable unit is **one complete account balance exchange**, using BR-06's latest-snapshot `MutateAndSave` path. No batch-wide rollback is attempted. Earlier durable successes remain successful if a later account fails, the caller cancels, or the profile locks. Partial/incomplete individual responses cannot become valid snapshots.

BR-06 returns an immutable view of its committed balance values and retrieval timestamp with its result. Batch reporting uses that committed view rather than re-reading an unlocked profile. Therefore a lock or unrelated change immediately after commit cannot erase a success from the summary or substitute a different current value.

Each distinct `(connection, account)` source row has one outcome:

| Outcome | Meaning |
| --- | --- |
| Updated | A complete balance exchange was durably saved and its booked/available/credit values differ from the previous projection |
| Unchanged | A complete exchange was durably saved, with the same values; refreshed timestamps are still a successful result |
| Unavailable | Missing/unverified/closed source, disabled/unsynchronized connection, backoff, duplicate ownership, or no selected source |
| Unsupported | Missing account capability, unsupported bank procedure, or an unqualified request version |
| Failed | An attempted account exchange, authentication, or persistence step failed; details distinguish authentication and received-but-not-saved outcomes |
| Cancelled | User cancellation, expiry, context change, or cancellation of remaining account work |

Unavailable preflight rows retain their explanatory status when eligible account work is later cancelled. Connection results distinguish completed, partial, blocked, failed, cancelled and already running. A connection with both durable saves and other outcomes is partial. Counts are source rows, so an ambiguous account bound to several selected connections can have several non-success rows; BR-06's unique-active-source guard prevents duplicate successful ingestion.

`SavedObservations` sums only completed account commits, including explicit unavailable observations where BR-06 requires them. Currency totals contain **only this run's durable refreshed booked values**, never old cached values from failures. EUR and USD remain separate groups. A group with no saved amount returns an unavailable total, not zero. Exact decimal overflow/precision loss returns an unavailable total with an overflow flag rather than rounding or failing other results.

Progress reports checking, waiting, signing in, discovering, reviewing, reading each account, and completion. Observers cannot turn a committed success into a bank failure. Arbitrary exception messages, raw protocol payloads and PIN/TAN values are not included in batch results or retry reasons.

## Context and discovery review

The run pins profile/branch, epoch, connection settings, endpoint and system identity. Contact timestamps may advance after its own successful reads; unrelated bank/account saves do not invalidate the connection batch. Each account still captures and validates its own exact BR-06 source, lifetime and capability before dispatch and commit.

Batch discovery uses its connection lease to validate source context independently of unrelated generation changes, then builds review proposals against the latest profile. Review commit tokens remain generation-bound: an edit made while the user reviews a proposal can still invalidate that proposal, requiring another explicit review. Discovery and reviewed account-state saves do not contribute to balance success counts.

## Verification and remaining dependencies

Headless scripted tests cover multiple accounts and institutions, mixed success/timeout, independent currency totals and overflow, unchanged durable reads, closed accounts, disabled connections, missing procedures, backoff across calls, explicit/deduplicated selections, authentication rejection without retries, per-account save failure, discovery followed by real balance reads, review cancellation, institution serialization, shared network bounds, duplicate batch/single-account admission, service disposal, observer failures, lock immediately after commit, credential disposal and reopened saved data. A regression test queues an institution-owning batch behind a standalone PIN prompt and verifies both finish, guarding the prompt/institution lock ordering. No live bank access or visual testing is used.

BR-08 now supplies shared progress, cancellation and partial-result feedback. A host must supply qualified procedure provenance; full desktop acquisition/selection, persistent BPD/UPD caching, signed SCA and broader identity resolution remain outstanding BR-04/BR-05 work. Unsupported configurations stop explicitly. This milestone does not enable unattended refresh or claim that every configured bank/account is supported.
