# Bank account discovery and review — BR-05

Status: production discovery/review integration implemented for the supported IBAN-based slice; BR-05 remains in progress. This work does not enable desktop balance refresh.

## Entry points and prerequisites

`HostSession.DiscoverBankAccountsAsync` accepts a current `ConnectionSetupContext`, a caller-owned banking PIN, an explicit `FinTsPinTanProcedureContext`, and a selected signature procedure. The endpoint must be confirmed, the assigned application product ID configured, and the connection synchronized with an assigned system ID. The operation shares reservations and bounded network concurrency with synchronization and refresh.

The method builds initialization, HKSPA discovery, and closing requests through `FinTsProductionCandidateFactory`. It requires a supported, validated account response and successful dialogue close before returning a `BankDiscoveryReview`. It runs kernel ingestion against a **detached** identity ledger. Neither tentative kernel allocations nor the returned review mutate the open profile.

`DiscoverAndReviewBankAccountsAsync` composes the BR-04 disposable PIN prompt with discovery and `IBankAccountDiscoveryReview`. It serializes each prompt/review using the shared bank prompt gate, releases that gate during network work, clears its owned PIN before account review, and cancels on profile changes, lock, shutdown, caller cancellation, or its five-minute deadline. The existing BR-04 signed SCA gaps remain: an unsupported challenge stops the operation without saving associations or attempting an unsigned continuation.

## Evidence and matching

The supported association requires one consistent HIUPD account and one matching HISPA account in the same validated dialogue. The canonical source retains the institution, connection, exact endpoint, connector/version, IBAN, domestic account/subaccount/bank code, currency, bank customer/country identifiers, and the discovered BIC. Matching preserves bank strings; it does not normalize changed bank identifiers into an exact source match.

The kernel `AccountRediscoveryPlanner` supplies reuse/conflict decisions. The presentation planner adds stricter manual-association guards:

- **Known bank binding:** reuse only an exact active `fints` binding whose incarnation is not reported closed.
- **Manual account:** propose an association only for one quarantined `Manual` binding on the same connection, institution, and endpoint, with the same IBAN and currency, a single unspecified incarnation, and no existing bank binding or conflicting rediscovery candidate. The user must confirm it.
- **New account:** offer a separate local identity for a new bank source, or an explicitly reviewed source clearly distinct from existing account identifiers. Another connection's matching IBAN is never a merge instruction.
- **Needs review:** duplicate occurrences, incomplete or inconsistent HIUPD/HISPA identity, unknown currency, conflicting currency, inactive bindings, ambiguous candidates, changed identity, and closed/reopened lifetimes cannot be confirmed through this initial workflow. HISPA-only observations remain visible without an invented currency.

An unconfirmed manual account stays quarantined. This workflow cannot reactivate an old bank lifetime by changing a state string. General conflict resolution and national-only account association are outside the supported slice.

## Balance capabilities

After accepting discovery, the workflow probes the bounded balance candidate builder against the exact accepted user parameters and the discovered BIC. It does not send a balance request. A qualified HKSAL version is projected into `StoreCapabilityEntry`; no supported advertisement means unavailable capability. Missing or unsupported capability never becomes an assumed default version.

The saved version records bank-advertised protocol support, not proof that interactive SCA or a future balance exchange will succeed. BR-06 must requalify the target and request against fresh initialization evidence before sending.

## Explicit review and atomic save

`BroilerDialogService.ReviewBankAccountsAsync` shows the source endpoint, bank login, IBAN, BIC, currency, target name, and balance support for each proposed association. The user can skip individual proposals and cancel the final review. A missing window/reviewer cancels. A final summary includes blocked observations and explains revocation of old operational permission for absent/changed bank sources.

`ConfirmBankAccountDiscovery` accepts only row indexes the issued plan permits. Plans belong to one session, profile/branch, epoch, connection configuration, and persisted generation. They expire after ten minutes and are consumed on a save attempt. Changes during discovery/review invalidate the result; a concurrent edit is never overwritten.

Confirmed manual associations retain the existing account ID, display name, incarnation, and history. They allocate a new bank-provenance binding through the identity allocation batch; the manual binding remains quarantined. Confirmed new sources allocate distinct account/incarnation/binding identities. The save includes the complete resulting ledger and next-ID value along with account evidence, connection contact state, and qualified capability versions. No balance documents, historical bindings, or unrelated profile documents are rewritten.

For a completed discovery, previously active bank sources absent or changed in the reviewed list become quarantined and lose cached HKSAL permission. Manual entries remain unchanged. A transient unavailable/error response is not evidence of closure: the current read pipeline rejects it without changing saved state. Unselected new observations are not persisted as duplicate candidate shells; the next discovery can propose them again.

## Verification

Headless tests in `BankDiscoveryTests` cover production outgoing HKSPA bytes, validated closure, review-before-save, manual identity preservation, durable ledger/binding/capability reopening, exact rediscovery without duplicates, BIC retention, unsupported capability revocation, credential disposal before review, review cancellation, raw-exception redaction, duplicate bank observations, currency conflicts, closed incarnations, distinct connection scopes, unchanged historical balance provenance, failed close, stale review, shared admission, and profile-lock cancellation.

The bank response fixtures and test-only changes to their parameter advertisements are embedded only in the test assembly. No fixture identities, bank credentials, or response payloads enter production request construction. No live bank access or visual testing is used.

## Remaining BR-05 work

- Persist and restore a versioned full BPD/UPD parameter cache with its endpoint/login/product/procedure provenance. This increment persists account-source identifiers and qualified capability projections, not raw reusable parameter documents. The API still requires explicit bank procedure provenance from its caller.
- Complete BR-04's supported signed SCA exchanges before claiming discovery compatibility with banks requiring approval.
- Add separately qualified national-only association and explicit conflict/lifetime resolution workflows; ambiguous or inactive identities currently remain unverified.
- Wire discovery actions into the desktop connection/account workflow alongside BR-06/BR-08. Existing refresh buttons do not invoke this entry point yet.

Only the documented supported slice is implemented; the milestone is not marked complete while these dependencies remain.
