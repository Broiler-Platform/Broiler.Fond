# ADR 0075: Session terminal evidence ownership

- **Status:** Accepted (local lifecycle correction)
- **Date:** 2026-10-14
- **Related:** [Read sessions](0072-assembled-read-session-attempt.md),
  [synchronization sessions](0073-assembled-synchronization-session-attempt.md),
  [clock boundaries](0074-synchronization-session-clock-boundaries.md)

## Ownership and cleanup

Read and synchronization coordinators previously cleared pending requests at
termination but retained accepted evidence and dialogue identifiers. A completed
or disposed coordinator could therefore keep account, balance, parameter and
system-ID data reachable through those evidence graphs.

Every terminal transition now releases the coordinator's candidate, recovery,
evidence and dialogue references. Read sessions also clear accepted continuation
pages and aggregated balances. Cancellation, timeout, invalid clocks, stopping,
disposal, clean closure, bank abort and review outcomes share this cleanup path.
Scalar diagnostics and progress counts survive independently of retained data.

Evidence already returned belongs to the caller. Synchronization closing captures
the accepted synchronization evidence before cleanup and returns it alongside the
closing evidence for clean, abort and review outcomes. Terminal replay returns no
new evidence. Read closing evidence is available from the closing result; the
coordinator's `ClosingEvidence` property is null after termination, like its other
evidence properties. Consumers must retain their results if evidence is needed
after closing or disposal.

The read coordinator's `AggregatedBalances` and `BalanceContinuationPages`
properties now return read-only collection snapshots. Previously they wrapped
mutable internal lists, so later pages could change an earlier handoff and cleanup
would empty it. Copies preserve the caller's sequence while the coordinator can
release its own references. Evidence objects themselves are not modified.

This is managed-reference cleanup, not guaranteed memory erasure. Caller-owned
results and copies remain the caller's responsibility. Expiry remains observed
through the existing clock-checking APIs; no background timer is introduced.
The kernel remains inert with no authenticated security, transport or activation.

## Verification and next work

The existing 28 read and 24 synchronization fixture traces now inspect all retained
reference fields at terminal boundaries, excluding the lock and clock infrastructure.
Additional lifecycle matrices exercise 108 combinations of stage and termination,
covering cancellation, cancelled tokens, stopping, disposal, deadline equality and
clock conversion failure. Tests preserve scalar counts, verify result ownership,
check collection snapshots across later pages and cleanup, and reject restart.
Closing tests cover caller handoffs for clean, abort and review outcomes and
withhold evidence on replay.

The tests reproduced retained references and changing collection views before the
fix. Full Windows CI passed afterward with zero build warnings or errors: 3,610
read-session checks, 1,092 synchronization-session checks, and the unchanged 103
clock-boundary checks. This adds 2,846 checks without changing wire fixtures or
their generator.

Next is read-session candidate provenance and cross-stage evidence binding under
M1-05. Authentication, activation and transport remain pending.
