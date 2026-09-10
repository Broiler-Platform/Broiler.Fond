# Broiler Fond - Finance on Demand

- **Brand:** Broiler
- **Product:** Fond - Finance on Demand
- **Technical name:** `Broiler.Fond`

Broiler Fond - Finance on Demand is a planned privacy-first, multibank
homebanking application. It is a purely user-operated client: the installed
application connects directly to accounts that its user owns or is authorized
to use. Broiler does not receive credentials, account data, transaction data,
or payment instructions.

> **Milestone 1 in progress.** The kernel now validates manual institution
> endpoints, tracks confirmation and quarantine, and provides in-memory identity
> allocation, startup checks, lossless account rediscovery, and exact account-value
> projections with freshness warnings and per-currency totals.
> A test-only read workflow simulator exercises synthetic outcomes; opt-in
> structured diagnostics provide an in-memory support preview.
> A bounded FinTS byte parser now validates syntax and outer framing against
> synthetic fixtures; authenticated protocol security remains pending.
> Typed HIRMG/HIRMS replies and local dialogue correlation now preserve receipt,
> pending, error, unknown and indeterminate outcomes without enabling live reads.
> BPD/UPD parsing preserves bank/user/account parameters and permission evidence;
> it activates no capability or cached state.
> Read-operation schemas now compare explicit-version bank and account evidence,
> exposing missing or conflicting requirements without authorizing requests.
> PIN/TAN response-envelope and HIPINS schemas now preserve bounded structural
> evidence, including reported TAN flags; they authenticate no message.
> TAN-procedure and challenge schemas now cover versions 6/7, preserving
> separate-device approval parameters and raw challenges without live continuation.
> Reported permitted procedures and restricted request-bound challenge checks
> now expose scope mismatches and ambiguous outcomes without authorization.
> A synthetic SCA continuation model now records one pending challenge with
> bounded queries, replay rejection, terminal cancellation and a fixed deadline.
> Account-discovery and balance schemas now preserve exact source identifiers,
> separate optional amounts and source dates against synthetic fixtures.
> Single-account read comparisons now check request/account/capability scope,
> response references and partial-page provenance without ingesting balances.
> A local read-refresh attempt now owns one pending request, enforces timeout
> and page limits, and consumes matching response observations once per attempt.
> Scoped unavailable reports now terminate separately from errors, missing
> responses and real zero balances, preserving their original source shape.
> All-account discovery now matches returned identities against supplied account
> parameters while retaining unknown accounts, ambiguity and unmatched entries.
> Its local attempt now consumes a bound response once, returns comparison
> evidence for inspection, and releases pending context on terminal outcomes.
> A typed unsigned read-request writer now emits exact discovery and balance
> frames with strict text encoding, delimiter escaping and bounded fields.
> Initialization identification/preparation schemas and unsigned encoding now
> preserve explicit customer, system, parameter-version and product fields.
> Initialization responses now compare dialogue/reference and parameter identity
> scope while preserving missing data and unresolved observations for review.
> A local initialization attempt now pins the request and expected user identity,
> returns bound evidence once and releases context on terminal outcomes.
> Synchronization schemas and unsigned encoding now preserve system identifiers,
> message numbers and exact signature counters without applying returned values.
> Synchronization context checks now compare mode/profile requirements and prior
> recovery scope, marking matching evidence as requiring close and reinitialization.
> Dialogue-end schemas and unsigned encoding now bind standard closing replies,
> preserving normal closure, explicit abort and unresolved evidence separately.
> A local synchronization-and-closing attempt now requires an explicit close,
> consumes each stage once and shares one deadline through terminal cleanup.
> PIN/TAN signature-header schemas now preserve identity, reference and timestamp
> observations, enforcing profile/code rules without handling credentials.
> Signature-header context checks now bind initialization/synchronization identity
> and explicitly sourced procedure reports while retaining unresolved evidence.
> Typed signature-header encoding now emits exact escaped bytes with canonical
> optional fields, preserving caller values and rejecting precision loss.
> A local credential buffer now clears transferred input, supports one-time TAN
> copy-out and zeroes owned bytes on consumption, cancellation or disposal.
> Access expiry is checked on API calls; live credential entry remains pending.
> HNSHA trailer encoding now uses owned credentials and bounded caller output,
> checks TAN placement and clears temporary or failed-output secret bytes.
> Plain PIN/TAN initialization and synchronization assembly now preserves bound
> fields, calculates exact framing and clears staged or failed whole-message output.
> PIN/TAN request envelopes now bind credential-free metadata and exact binary payloads,
> with bounded staging and cleanup through the final caller-output handoff.
> Assembled-request metadata now binds response references to actual segment
> roles while preserving errors and unresolved data for later semantic checks.
> Assembled initialization now compares scoped execution reports and parameter/
> envelope identities, retaining missing data and version changes without activation.
> Assembled synchronization now checks report and recovery scope, retaining exact
> values and requiring close/reinitialization after matching observations.
> An assembled initialization attempt now pins one candidate, returns scoped
> evidence once and releases pending metadata on terminal outcomes or disposal.
> An assembled synchronization attempt now owns candidate/recovery metadata and
> returns evidence once under a fixed deadline, requiring closing/reinitialization.
> PIN/TAN closing candidates now bind synchronization dialogue and identity scope,
> encoding PIN-only envelopes with exact framing and failed-output cleanup.
> Assembled closing replies now bind actual segment roles, dialogue and envelope
> identity, distinguishing reported closure, abort and unresolved observations.
> A closing attempt now owns one candidate and deadline, returns scoped evidence
> once, and releases metadata after closure, abort, review or abandonment.
> An explicit initialization path now combines returned TAN procedures and
> permission reports with exact source checks and one-time evidence handoff.
> Combined initialization now checks HIPINS length bounds and reported TAN flags,
> preserving missing or conflicting requirements without granting permission.
> First-read signature context now binds returned initialization evidence to
> dialogue, counters and unchanged procedure selection without signing a request.
> Initialization now integrates returned read advertisements; first-read account
> checks compare explicit permissions and request options without enabling reads.
> First-read credential comparison now checks supplied PIN/TAN bytes against
> reported length and format requirements without retaining or consuming them.
> PIN-only read trailers now validate owned staging bytes against reported bounds
> and clear temporary or failed output bytes through expiry and cancellation.
> Plain first-read assembly now preserves bound fields and segment roles,
> computes exact framing and clears staged or failed whole-message output.
> The host remains an inert development shell.
> It has no bank connection, credential handling,
> persistence, payment, transaction-sync, or graphical UI implementation. It is
> not a registered FinTS product or a production banking client.

## Product direction

- Germany and EUR/SEPA first.
- Windows is the first host. A platform-independent .NET 10+ kernel grows with
  each milestone; Linux, macOS, and mobile hosts follow later.
- The kernel is developed in this repository and has no third-party runtime or
  platform dependency. It uses only the standard .NET runtime libraries.
- FinTS 3.0 as the first live connector; any later protocol must preserve direct
  user-device-to-bank operation behind the same connector boundary.
- Institutions are configured manually in the first milestones.
- Local data is stored as multiple versioned XML documents in a compressed,
  authenticated-encrypted stream using only .NET 10+ runtime APIs.
- No telemetry, analytics, or automatic crash upload is collected.
- Bank-advertised capabilities decide what the application offers. A feature is
  never assumed merely because another institution supports it.
- Read-only account access comes before any money movement.
- Credentials, TANs, financial data, and ambiguous payment outcomes are treated
  as security-critical data and states.

Milestone 1 is planned as a technical alpha that can create access to existing
accounts and list their current values. Transaction detail, reliable multibank
sync, SEPA transfers, recurring payments, and personal-finance features follow
in separately gated milestones.

## Engineering foundation and current development

The M0 foundation remains enforced as M1 development begins:

- `Broiler.Fond.Kernel` targets platform-neutral `net10.0` and contains only
  domain types, manual endpoint validation, in-memory identity allocation,
  startup validation, account rediscovery, exact value projections, bounded
  in-memory diagnostics, FinTS syntax/framing, response/correlation primitives
  and bounded BPD/UPD/read-capability, PIN/TAN envelope/parameter and TAN
  procedure/challenge evidence, restricted request-bound comparisons and
  a bounded in-memory SCA continuation model, account-discovery/balance schemas
  and single-account read-context evidence with a bounded read-refresh lifecycle,
  the host-facing boundary, and
  unimplemented connector/storage seams.
- `Broiler.Fond.Host.Windows` is the initial `net10.0-windows` console shell.
- `Broiler.Fond.Kernel.Tests` is a small BCL-only executable verification
  runner; no external test framework or package feed is required.
- repository build rules and `eng/Verify-KernelBoundary.ps1` reject package,
  platform UI, native/COM, unsafe-code, and out-of-kernel project dependencies
  in the kernel.
- GitHub Actions verifies the full solution and smoke-runs the inert host on
  Windows, then independently builds the kernel and its checks on Linux. CI
  does not publish artifacts.

The [M1 progress and acceptance backlog](docs/milestone-1.md) records completed
work, remaining implementation, and release dependencies. Endpoint confirmation
is a domain primitive; the user-facing setup and authentication flows are pending.
The storage format now has [proposed envelope](docs/adr/0004-profile-envelope-v1.md)
and [snapshot](docs/adr/0005-profile-snapshots-v1.md) specifications, five independent
synthetic vectors, and test-only BCL reference verification. User profile
persistence remains disabled pending complete schemas, calibration and review.
The [identity contract](docs/adr/0006-local-identity-allocation.md) now provides
allocation batches, exhaustion handling and startup integrity checks. Durable
entity commits remain pending. The [rediscovery planner](docs/adr/0007-account-source-rediscovery.md)
preserves exact identities and quarantines changed or ambiguous sources; connector,
durable application and user-review integration remain pending.
The [account-value contract](docs/adr/0008-account-values-and-exact-money.md)
keeps unavailable and stale data explicit and prevents mixed-currency or silently
rounded totals. Balance retrieval and UI integration remain pending.

Prerequisite: a compatible .NET 10 SDK. Run the same full check used by CI:

```powershell
./eng/Invoke-CI.ps1 -Scope Full
```

The platform-neutral subset can be checked with:

```powershell
./eng/Invoke-CI.ps1 -Scope Kernel
```

## Documentation

- [Product specification and roadmap](docs/roadmap.md)
- [Milestone 0 definition and inventory](docs/milestone-0.md)
- [Milestone 1 progress and acceptance backlog](docs/milestone-1.md)
- [Architecture decisions](docs/adr/README.md)
- [Compatibility evidence](docs/compatibility/README.md)
- [Security policy](SECURITY.md)
- [Human review record](HUMAN_REVIEW.md)
- [Prerelease checklist](docs/release/prerelease-checklist.md)

Major implementation decisions are recorded as ADRs under `docs/adr/`.

## License

Broiler Fond - Finance on Demand is licensed under the
[Apache License 2.0](LICENSE).
