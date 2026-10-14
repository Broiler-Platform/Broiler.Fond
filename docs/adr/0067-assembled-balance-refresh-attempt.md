# ADR 0067: Multi-page PIN/TAN balance refresh attempt

- **Status:** Accepted (local synthetic lifecycle only)
- **Date:** 2026-10-14
- **Related:** [Assembled first-read attempt](0057-assembled-first-read-attempt.md),
  [assembled continuation attempt](0065-assembled-continuation-attempt.md),
  [later-page balance continuation context](0066-later-page-balance-continuation-context.md),
  [in-memory read refresh attempt](0020-in-memory-read-refresh-attempt.md)

## Multi-page coordination and lifecycle

`FinTsPinTanReadRefreshAttempt` coordinates an end-to-end multi-page PIN/TAN balance
refresh dialogue across page one and subsequent continuation pages within an established
synthetic session. It owns a single unified lifetime deadline (positive, up to 15 minutes)
and an explicit page budget bounded between 1 and 128 (defaulting to 16).

The coordinator starts in `Ready` with zero recorded requests and zero accepted pages.
Calling `Start` records the first-read candidate (`FinTsPinTanReadRequestBinding`), establishes
the dialogue anchor and initial message counters, and transitions to `AwaitingResponse`.
A second `Start` while active returns `WrongState` without replacing the candidate.

When an accepted page reports a partial balance (`FinTsReadOutcomeObservation.PartialReported`),
the attempt transitions to `WaitingToContinue`. In this state, the caller may record a sequential
continuation candidate (`FinTsPinTanReadContinuationRequestBinding`) via `RecordContinuation`.
A valid continuation candidate re-enters `AwaitingResponse`, increments the recorded request count,
and awaits the subsequent page response. Once a final execution response is accepted
(`FinTsReadOutcomeObservation.ExecutionReported`), the attempt terminates in `ExecutionReported`.
If the bank reports unavailable data (`FinTsReadOutcomeObservation.UnavailableReported`), the attempt
terminates in `UnavailableReported`.

## Page budget, counter sequencing and replay defense

The attempt enforces strict monotonicity and dialogue coherence across all page cycles:
1. **Dialogue and Counter Invariants:** All candidates and responses must match the dialogue ID
   established at `Start`. Client message numbers and expected bank message numbers must strictly
   advance by exactly +1 for each consecutive continuation candidate (`_lastClientNumber + 1` and
   `_lastBankNumber + 1`). Candidates with old or skipped counters return `ReplayRejected` or
   `ContextMismatch`.
2. **Predecessor and Origin Continuity:** Every continuation candidate must prove provenance
   tying back to the attempt's origin capability context (operation code and version, account
   equivalence via `FinTsReadContextEvidence.SameAccount`) and match the predecessor evidence of the
   last accepted page (`_firstPageEvidence` for page 2, or the tail of `_continuationPages` for later
   pages). Mismatched candidates return `ContextMismatch` without altering pending state.
3. **Response Replay Checking:** Prior to state assertions, responses are checked against dialogue
   and bank message counter tracking. Responses re-submitting an already processed or earlier bank
   message number return `ReplayRejected`.
4. **Page Budget Exhaustion:** If a partial response is accepted when the accepted page count reaches
   the configured maximum page limit (`PagesAccepted == MaximumPages`), the attempt terminates in
   `PageLimitReached` instead of waiting for continuation.

## Balance report aggregation and domain projection

The attempt collects all distinct `FinTsBalanceReport` entries across all visited pages into an
unmodified, read-only collection (`Balances`). Balance collection preserves the bank's exact
reported values, booked amounts, pending amounts, credit lines, available amounts, timestamps,
and product labels:
- **No Overwriting:** Subsequent pages append reports; earlier page balances are never replaced or
  overwritten.
- **No Double Counting:** Replayed responses or duplicate candidates cannot duplicate reports.
- **Domain Projection:** The aggregated reports map losslessly into domain `BalanceSnapshot` entities
  with observation allocation, establishing truthful data for `AccountValueProjection` without
  erasing unmapped fields or inventing conversions.

## Diagnostics, cleanup and concurrency

Snapshots (`FinTsPinTanReadRefreshSnapshot`) expose strictly scalar lifecycle metrics: state,
recorded request count, accepted page count, maximum page budget, reported balance count, remaining
time, and typed issue bitmasks (`Issues`, `BindingIssues`, `ReadIssues`, `ContinuationIssues`).
No account, response, candidate, continuation token or credential object is exposed in snapshots.

Every terminal transition (`ExecutionReported`, `UnavailableReported`, `NeedsReview`, `PageLimitReached`,
`CounterExhausted`, `Cancelled`, `TimedOut`, `Stopped`, `ClockInvalid`) immediately clears pending
candidate references. Terminal states are immutable: subsequent calls to `Start`, `RecordContinuation`,
or `AcceptResponse` return `Terminal` without altering state or diagnostics. All mutating operations
are synchronized under an internal monitor gate, ensuring thread safety and race-free concurrent
evaluations.

## Verification

`FinTsPinTanReadRefreshAttemptTests` exercises twenty-three independent Python-generated traces
and 459 checks in `pin-tan-read-refresh-attempt-v1.json`. Verification covers:
- Complete two-page and multi-page continuation lifecycles terminating in `ExecutionReported`
- Single-page executions and immediate `UnavailableReported` terminations
- Page budget boundaries (terminating in `PageLimitReached` at budget limit)
- Message counter exhaustion boundary (terminating in `CounterExhausted` at message 9999)
- Predecessor and origin mismatch rejections, foreign dialogue isolation, and replay defenses
- In-flight cancellation via `CancellationToken` and pre-cancelled tokens
- Monotonic deadline expiry (`TimedOut`) and clock invalidation / regression (`ClockInvalid`)
- Lossless domain projection into `BalanceSnapshot` and `AccountValueProjection`
- Concurrency under parallel response submission ensuring exactly one acceptance
- Reflection assertions confirming strictly scalar snapshot diagnostics.
