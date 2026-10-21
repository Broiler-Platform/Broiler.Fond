# First balance through desktop setup (priority 7)

Implemented 2026-10-21 with scripted bank responses and headless checks. No live bank or visual testing was performed.

## User flow

1. Add the bank connection, review the exact endpoint, and use **Connect** to select a supported procedure and synchronize access.
2. Choose **Discover Accounts**, explicitly review the bank-reported identities and confirm saving supported capabilities. Manual account entry is optional.
3. Select the connection and choose **Refresh Balances** in Connections. This action runs the existing production balance batch for that connection only. Other configured banks are not included.
4. Review the refresh result and cached account values/retrieval time. Updated and unchanged both mean a validated, durably saved bank read. Discovery and synchronized setup alone never count as balance success.

After a normal profile lock/unlock, the Connections refresh action offers procedure requalification when the volatile selection is absent. It downloads current public parameters as necessary, asks for explicit procedure choice and validates identified parameters. Cancelling or failing this stage stops before the balance PIN/request. A synchronized connection does not need a fabricated system ID or repeated synchronization to refresh. The bank PIN is acquired through the shared batch service and disposed after use.

The existing Sync All and Accounts refresh actions retain their batch behavior. If procedure provenance has expired, their feedback directs users to Connections → Refresh Balances to requalify. They do not silently choose a procedure or bypass discovery.

## Results and cancellation

Connections shows the shared refresh progress and **Cancel Refresh** action. Connect, discovery and refresh controls disable during active refresh. The setup coordinator retains **Cancel Setup** during requalification. Closing the owning Connections view cancels its refresh and suppresses late feedback.

Typed connection blockers distinguish profile/product restrictions, unsynchronized bank access, missing reviewed capabilities, missing current procedure, review availability, invalid selection and retry backoff. Account feedback separately reports authentication/approval review, unsupported reads, failed exchange, received-but-unsaved results, cancellation and durable success. Backend exception text is excluded. A successful row includes its retrieval time.

An uncertain/malformed closing exchange cannot authorize a save. A storage failure reports bank data received but not saved. Failed/cancelled accounts retain previous saved values and retrieval times; completed per-account commits remain valid when a larger batch partially completes or is cancelled. Totals remain grouped by currency and include only this run's durable successes.

## Verification and limits

The new first-balance test starts with an ordinary new profile and connection and uses desktop methods for parameter acquisition, explicit procedure selection, synchronization, discovery/review and balance read. It does not inject procedures or mutate stored bank state directly. It verifies the actual HKSAL exchange, selected-connection scope, durable amount/timestamp after reopen, post-unlock requalification (including cancelled choice), unchanged success, malformed closure, save failure, login rejection without retries, shared progress/cancellation and PIN disposal. Existing batch tests cover partial successes, concurrency, per-currency totals and backoff.

Supported bank/procedure behavior remains bounded by the production read pipeline. Synchronization text-TAN support does not imply support for interactive approval during discovery or balance reads. Unsupported approval requests remain blocked. Priority 8 still requires separately authorized qualification of one exact live bank/product/procedure combination; scripted success is not evidence of live compatibility.
