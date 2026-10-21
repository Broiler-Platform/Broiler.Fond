# Desktop bank-account discovery and review (priority 6)

Implemented 2026-10-21. Verification uses scripted bank responses and headless presentation checks; no live bank or visual testing was performed.

## User flow

In Connections, select the bank connection and use **Connect** to qualify a supported procedure and save synchronized bank access. Then choose **Discover Accounts**. Manual account entry is optional.

Discovery requests the banking PIN, validates initialization, sends the actual HKSPA account-list request, and requires a validated closing exchange. The owned PIN is disposed before review opens. Review shows the bank, endpoint, login, account identifiers, currency, proposed local association and qualified balance version. Confirm intended associations and the final save; cancel preserves previous profile data. Accounts without a supported balance capability cannot become refresh-eligible merely by being reviewed.

Unique matching manual accounts retain their identity, name and history. New reviewed bank accounts get allocated identities. Ambiguous or conflicting associations cannot be confirmed. Existing matching verified bindings remain subject to the established discovery planner; missing or changed bank sources lose active capabilities while historical balances remain intact. An empty selection can still reconcile existing evidence, so the final save confirmation remains required.

After saving, the action reports that no balances were downloaded and directs the user to refresh eligible reviewed accounts. Account bindings and qualified capabilities survive reopening. Procedure selection is deliberately volatile: after lock/unlock, use Connect again to qualify a current selection before discovery.

## Lifetime and feedback

Connect and Discover Accounts share one session-owned setup operation, progress and **Cancel Setup**. Both actions are disabled during an active setup operation. Discovery owns the per-connection reservation before PIN entry and holds it through explicit review/save, shares the institution and prompt gates, and has a five-minute lifetime. Cancellation, profile lock or changed setup discards uncommitted review evidence. A review token is scoped, expires and cannot be replayed after an attempted save.

Missing writable profile, product configuration, synchronized state or current procedure yields guidance before PIN entry. Transport failures, unsupported discovery/approval, cancellation and failed saves have bounded messages; arbitrary backend error text is excluded. A successful storage commit is reported as saved even if the subsequent display reload fails, with instructions to reopen the profile.

## Verification and remaining scope

Headless tests exercise the desktop Connect-to-discovery path without direct store mutations, new-account creation without a manual prerequisite, review cancellation, credential disposal, no HKSAL transmission, durable reopen, shared busy/cancel behavior and procedure expiry after lock. Existing discovery tests cover manual identity/history preservation, conflicting/duplicate sources, capability revocation, replay protection and failed protocol closure.

Discovery supports the existing bounded signed read/discovery pipeline. Banks requiring unsupported SCA during discovery remain blocked; priority 5's synchronization text-TAN continuation does not imply discovery SCA support. Priority 7 adds the [first-balance desktop action and requalification flow](bank-first-balance.md). Live bank/product/procedure qualification remains priority 8 and requires separate authorization.

For a first durable balance, select the connection and choose **Refresh Balances**. After lock/unlock, this action offers current procedure requalification before the bank PIN prompt. See [first balance](bank-first-balance.md).
