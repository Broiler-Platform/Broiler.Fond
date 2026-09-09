# Synthetic read workflow fixtures

Run with `dotnet run --project tests/Broiler.Fond.Kernel.Tests` from the repository
root, or `./eng/Invoke-CI.ps1 -Scope Full` for all checks. The suite is registered
in the executable runner and requires no package, bank, credentials or service.

`ReadOnlySimulator` accepts an ordered array of `SimulationStep` objects. For
example, `Authenticate / Authenticated` followed by `ReadAccounts / Success`
performs the synthetic read. For a manual challenge, use `Authenticate /
ManualChallenge`, `ContinueChallenge / Authenticated`, then `ReadAccounts /
Success`; the caller must explicitly supply the returned challenge to Continue.

| Scenarios | Verified result |
| --- | --- |
| Successful read | Both exact existing bindings; EUR 123.45 and -20.01; net 103.44 |
| Partial read | Second balance unavailable; net 123.45 with one exclusion and a partial-refresh row warning |
| Invalid credentials / locked access | Distinct rejection outcomes; no retry or fabricated values |
| Timeout / maintenance / transport failure | Terminal failure; no automatic retry |
| Changed parameters | Explicit reauthentication-required result |
| Malformed response | Rejected without forwarding raw bank text |
| Manual challenge | Explicit continuation, session correlation, one use, fixed synthetic expiry |
| Cancellation | Before authentication or while waiting, explicitly or via token |
| Expiry / replay / foreign challenge | Deadline rejection, terminal replay rejection, foreign token leaves current challenge usable |
| Invalid inputs / scripts | Bounded lengths/count, invalid enums, missing/misordered/trailing/wrong-kind steps rejected |
| Secret handling | Supplied PIN/TAN spans cleared on success, failure and invalid calls; public sentinels absent from preview, result and error text |
| Diagnostics | Opt-in, bounded retention, clear/disable, concurrent count/order accounting, immutable preview and fixed JSON fields; timestamps opt-in |
| Projection regression | Missing/ambiguous balances preserve failed/partial refresh and offline flags |

`SyntheticAccountFixture` allocates public synthetic identities in an in-memory
ledger and uses the reserved `bank.example` hostname. The PIN/TAN/identifier
sentinels and raw text in `SimulatorAndDiagnosticTests` are intentionally public.
Never substitute real bank data or secrets. Partial/failure scenarios do not
change the immutable fixture or persist any financial state.

This is a workflow harness, not a FinTS simulator at the wire level. Replies
are predefined typed outcomes, not parsed protocol bytes. It does not qualify
a TAN procedure, bank, TLS policy, connector or exported support artifact.
The production diagnostic API excludes unsafe payloads by construction; this
suite does not establish that arbitrary application logging is safely redacted.
