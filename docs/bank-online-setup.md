# Online bank setup coordinator

The Connections toolbar now provides **Connect**, **Cancel Setup** and shared progress. Select a configured connection, confirm its endpoint, then choose Connect. The action unlocks the profile if needed and invokes the session-owned coordinator independently of balance-refresh eligibility. A manual account is not required for synchronization.

`HostSession.OnlineSetup.ConnectAsync` checks writable profile mode, product configuration, endpoint confirmation and connection state. It obtains explicit procedure provenance from `BankProcedures`, then invokes the existing owned-PIN authentication/synchronization service. It allows one setup interaction per session, supports cancellation and a five-minute deadline, and cancels when the profile epoch changes. Existing synchronization guards also reject edited connection contexts and conflicting bank operations.

Only an accepted, closed and durably saved exchange reports synchronization success. A previously synchronized connection is reported as existing local state rather than a newly verified sign-in. This step does not create accounts, activate manual bindings or retrieve balances. Feedback directs the user to the remaining discovery/review step and excludes raw backend exception text.

## Remaining dependencies

Priority 5 adds [signed process-2 text-TAN interactive approval](bank-signed-approval.md) during synchronization: challenge binding, single-use TAN ownership, expiry/cancel/reject handling without credential retry and message-3 dialogue closing are supported. Decoupled approval, visual/HHD procedures, TAN-medium discovery and a desktop account-discovery entry point remain subsequent work.

Priority 4 strengthens [PIN ownership and synchronization persistence](bank-sign-in-persistence.md): pre-PIN admission, explicit saving/failed-save feedback and reopen verification are implemented for the supported no-interactive-approval path. Signed approval remains the next dependency.

Priority 3 now adds [explicit procedure selection and login-specific qualification](bank-procedure-selection.md). When supported advertisements and identified permission evidence qualify, Connect proceeds into the existing PIN/synchronization workflow. It can also requalify after lock. The earlier priority-2 boundary below is superseded for that supported path; media-dependent procedures, credential-required parameter bootstrap and signed interactive approval remain unavailable.

Connect now performs [anonymous public bank-parameter acquisition](bank-parameter-acquisition.md) when no qualified procedure is selected. It reports the validated download and stops before requesting a banking PIN: procedure selection and login-specific qualification remain priority 3 work. An integrating host can provide `IBankProcedureProvider` or explicitly call `SelectBankProcedure`; test fixtures are never a production parameter source. Signed interactive SCA remains unavailable. Discovery/review needs a subsequent desktop entry point. Priority 1's synchronization path and priority 2's bounded anonymous download are implemented; later priorities and live-bank qualification remain outstanding.

## Headless verification

`BankConnectTests` exercises the actual Connections action with scripted synchronization responses and production request construction. A freshly created connection reaches synchronized saved state without direct test mutations of store state. Tests cover selection, endpoint confirmation, missing parameters before PIN, busy/cancel controls, duplicate rejection, cancellation and profile locking, failed closing exchange, safe messages, credential disposal, existing-state reporting and reopen. Existing layout tests verify the expanded toolbar does not overlap the first connection row at narrow widths. No live requests or visual testing are required.

See the [gap analysis](bank-connection-gap-analysis.md) and [qualification limits](bank-refresh-qualification.md).

## Account discovery after Connect

Choose **Discover Accounts** on the selected synchronized connection, review bank-reported accounts, and explicitly confirm the save. A manual account is optional. **Cancel Setup** also cancels discovery. After profile lock, use Connect to qualify a current procedure again. Discovery saves reviewed identities and qualified capabilities; it downloads no balances. See [desktop discovery](bank-desktop-discovery.md) for boundaries and verification.

For a first durable balance, select the connection and choose **Refresh Balances**. After lock/unlock, this action offers current procedure requalification before the bank PIN prompt. See [first balance](bank-first-balance.md).
