# Public bank parameter acquisition — priority 2

## Supported exchange

When **Connections → Connect** has no selected procedure provenance, it now downloads public bank parameters from the confirmed endpoint. It sends a fresh anonymous FinTS 3.0 initialization using the configured product ID/version and bank code. The protocol's anonymous customer marker is used; configured user/customer IDs, profile passphrases, PINs and TANs are not sent.

`FinTsBankParameterBootstrap` validates framing, request/response correlation, assigned dialogue, segment references, bank identity and FinTS 3.0 advertisement. Existing bounded parsers interpret HIBPA, supported HITANS v6/v7, HIPINS v1 and HISALS/HISPAS schemas. Unknown parameter segments/versions, ambiguous advertisements, invalid replies, mismatched institutions and user/account data fail closed. A scoped anonymous 3920 availability report may be observed, but never becomes permission for the configured login.

An accepted result requires a matching successful unsigned HKEND exchange. Failed/uncertain closing results are not retained, and dispatched closes are never blindly replayed. If parsing/qualification/cancellation ends an established, correlated anonymous dialogue before a close was dispatched, cleanup gets one independent two-second budget. A conflicting or unbound response does not provide a usable dialogue for cleanup. The kernel exchange has a 60-second deadline; the presentation operation also bounds admission/wait time to two minutes. No automatic retries occur.

## Ownership and state

`HostSession.BankParameters` owns acquisition and short-lived observations. It shares existing per-connection admission, institution serialization and global bank-operation slots. Production uses the confirmed HTTPS transport; response handlers are injected only through internal test composition.

Only closed, validated results are retained, in memory for at most ten minutes, scoped to profile/branch/session epoch and the current bank/endpoint/login context. Locking, profile replacement or relevant setup changes invalidate observations; changes during acquisition cancel it. No encrypted-profile generation is written by public parameter acquisition. Existing durable connection state, account bindings, capabilities and cached balances remain unchanged.

`GetCurrent` returns typed public observations for later procedure-selection work. It does not populate `BankProcedures` with a fabricated identified request, convert anonymous availability into user permission, or select a security function. Connect reports **ParametersAcquired** and the remaining procedure/login qualification step. Repeating Connect can reuse current observations without another exchange.

## Remaining work

Priority 3 follow-up: [explicit procedure selection and login-specific qualification](bank-procedure-selection.md) now consumes these public observations without promoting anonymous permission reports. The original next-step description below remains applicable to unsupported media/authentication paths and durable caching.

Priority 3 still needs an explicit procedure/media selection UI and the scoped parameter/permission lifecycle needed by authenticated initialization. Priority 4 wires that selected procedure into user sign-in; priority 5 provides supported signed interactive approval. Downloads that require a login, other parameter layouts and additional schemas remain unsupported by this bounded anonymous slice. Public BPD cannot stand in for login-specific UPD or authorization. Persistent BPD/UPD storage is not implemented here.

## Verification

`FinTsBankParameterBootstrapTests` covers production anonymous request bytes, product identity, supported typed advertisements, wrong bank/protocol, unknown statuses/versions, user-data rejection, malformed/correlated responses, closing failure, cancellation and safe transport outcomes. `BankParameterAcquisitionTests` drives the desktop Connect action with scripted transport and verifies pre-confirmation blocking, no credential prompts or profile writes, cache reuse/invalidation, admission conflicts and in-flight lock/login changes. Existing first-connection tests still exercise synchronization when qualified provenance is supplied.

No live bank or visual testing is used. The [qualification record](bank-refresh-qualification.md) still has no live compatibility rows.

Validation on 2026-10-21: full `Invoke-CI.ps1 -Scope Full` passed, including the kernel runner, 2,664 presentation checks, 184 desktop/UI checks, formatting and the Release build with trim analyzers enabled (zero warnings/errors).
