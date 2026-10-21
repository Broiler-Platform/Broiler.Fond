# Single-account balance refresh — BR-06

Status: the supported single-account read and durable-ingestion slice is implemented and tested with scripted bank responses. BR-07/BR-08 now connect desktop refresh actions to it; BR-04/BR-05 authentication/parameter-cache dependencies remain incomplete. This is not a claim of live-bank compatibility.

## Entry points and admission

`HostSession.GetBankBalanceContext(accountId)` captures one active, reviewed `fints` binding, its incarnation, and a saved active HKSAL capability. Manual-only, ambiguous, closed, unconfirmed, unsynchronized, or unsupported accounts cannot enter this workflow. The supported account has an IBAN and one discovered BIC, with matching local/bank currency.

`RefreshBankAccountBalanceAsync(context, pin, procedures, selection)` uses a caller-owned banking PIN and explicit bank procedure provenance. `AuthenticateAndRefreshBankAccountBalanceAsync` owns a disposable banking PIN acquired through `IBankCredentialPrompt`; it never reads the profile passphrase, serializes bank prompts, and clears the owned credential on completion, cancellation, expiry, lock, or source-context change. There are no automatic credential retries.

The operation shares per-connection reservations and bounded global network admission with synchronization and discovery. It creates a transport tied to the exact confirmed endpoint. It captures the profile/branch and session epoch, then continuously checks the connection, institution, target source tuple, incarnation, currency, and capability. A bank-login/endpoint change, removal, source change, or profile switch/lock invalidates the result.

Unrelated edits may advance the profile generation without cancelling this read. A changed target name is preserved; a changed target currency, lifetime, source, or capability invalidates the operation. Older BR-02/BR-05 generation-bound review tokens retain their original conservative behavior.

## Production request and response flow

1. Build initialization with the configured application product and explicitly selected procedure.
2. Find one exact matching account in fresh accepted HIUPD evidence, including the reviewed connection/source identifiers, currency, customer/country identifiers and discovered BIC.
3. Construct the HKSAL candidate using `FinTsProductionCandidateFactory`; the qualified version must still equal the reviewed capability version. A changed account or version requires rediscovery/review rather than a silent fallback.
4. Run `FinTsReadExecutionPipeline` with `VerifiedTargetOnly = true`, the verified target binding, and production first-read, continuation and closing factories. No balance candidate for another target is sent.
5. Follow accepted continuation tokens with correlated counters, up to sixteen pages. A page-limit result never becomes a partial durable balance.
6. Require a complete terminal read and validated, correlated dialogue close before committing. Unsupported SCA stops the exchange; it does not enable an unsigned continuation.

The pipeline preserves the original first-page evidence before closing clears its transient attempt state. Strict ingestion validates the exact initialization, first-page, continuation chain, report references, terminal outcome, and closing counters/dialogue. Parsed reports from another response cannot substitute for the accepted source objects.

## Target-only ingestion and balance semantics

`FinTsTargetBalanceIngestion` operates only on the existing verified account/incarnation/binding. It never performs discovery, relinking, or account allocation. Fresh user parameters must reproduce the reviewed source tuple, including the BIC. Every balance page must match the requested account and currency.

Identical balance reports repeated across continuation pages produce one observation set. Conflicting booked values/dates, available values, or credit-line values across those pages are rejected; the workflow does not guess which report is current. The supported projection is booked balance, independently reported available amount, and independently reported credit line. Other FinTS balance fields are not projected by this slice.

- Preserve the sign and exact decimal amount; a valid zero remains zero.
- Persist unavailable observations for missing available/credit-line values. This prevents older values from being shown as current and never copies booked balance into available balance.
- Keep bank calendar fields separate from the local retrieval timestamp, captured after the completed exchange. The existing portable timestamp model stores bank calendar fields using a zero-offset convention; the wire carries no timezone, so that offset is not a claim about the bank's actual timezone. Amount-only available/credit-line fields do not receive an invented bank timestamp.
- Serialize decimal amounts using invariant, non-rounded decimal text. The former two-decimal formatting could lose source precision; scientific notation is not introduced into the portable decimal schema.

Account projections choose the active bank binding before a quarantined manual entry and use the newer observation ID to break equal retrieval-time ties. Missing available balance is therefore displayed as unavailable even when a previous successful read supplied an available amount.

## Durable commit

Network execution uses a detached ledger. The commit calls `MutateAndSave` against the latest authoritative profile and rechecks the complete source context inside the mutation. It reapplies the accepted target evidence against a fresh detached copy of the latest identity ledger, allocating only observation/snapshot IDs at that point. Tentative network-time IDs never overwrite concurrently allocated profile IDs.

The commit reuses `MultibankSnapshotMutator.ApplyIngestionResult` for connection contact state and balance documents, then includes the complete committed ledger and next-ID value in the same generation. Account identities, names, bindings, capabilities, previous observations, history and unrelated profile documents remain intact. The full resulting snapshot must pass identity/semantic validation before save.

Only a completed save updates the displayed durable balance and reports `Saved`. A failure after qualified data receipt reports `ReceivedNotSaved` with the previous saved balance still authoritative. Cancellation, incomplete evidence, failed close, conflicting identity/currency, and stale context do not update saved balance amounts or retrieval timestamps. Arbitrary transport, prompt, or persistence exception messages are not exposed in the presentation result.

## Verification and remaining integration

Headless presentation tests cover the BR-05 review → production HKSAL → exact target ingestion → encrypted save → reopen → projected balance path. They also cover zeros, missing available balance, precision and debit sign, pagination, conflicting pages, unsupported identity/currency, failed close/save, pre-cancellation, owned-PIN disposal, duplicate admission, lock, bank-login changes, historical provenance, and unrelated edits with fresh ID allocation. Kernel tests independently exercise strict ingestion, substituted reports, missing continuation provenance, and page-budget exhaustion.

No live bank requests or visual testing are used. Real bank access still depends on completed signed SCA support and appropriate bank procedure/parameter acquisition. The API currently requires caller-supplied procedure provenance; the full durable BPD/UPD cache is pending BR-05 work. [BR-07 batch refresh](bank-batch-refresh.md) composes these operations, and [BR-08](bank-refresh-ui.md) supplies shared desktop progress, cancellation and feedback.
