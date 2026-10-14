# ADR 0074: Synchronization session clock boundaries

- **Status:** Accepted (local lifecycle correction)
- **Date:** 2026-10-14
- **Related:** [Synchronization session coordinator](0073-assembled-synchronization-session-attempt.md)

## Absolute deadline and failure handling

The synchronization session coordinator now calculates elapsed time directly
from its original start timestamp. Summing individually converted intervals can
discard fractions of a TimeSpan tick on every observation, allowing frequent
API calls to extend the intended deadline. A high-frequency synthetic clock
demonstrates this with four quarter-tick observations at a one-tick lifetime.
The final observation must terminate at deadline equality.

Both timestamp acquisition and elapsed-time conversion now run inside the same
failure boundary. TimeProvider conversion reads the provider's frequency and
can throw independently of GetTimestamp. Previously, such an exception escaped
the coordinator without releasing pending metadata. Conversion failures and
negative elapsed time now terminate ClockInvalid with scalar ClockFailure
diagnostics and pending-candidate cleanup. The original exception is not exposed.
Timestamp regression uses direct comparison, avoiding subtraction overflow.

A custom clock can invoke Cancel, Stop or Dispose reentrantly. Activity is
checked after timestamp acquisition and conversion. A callback that already
terminated the session cannot have its outcome replaced by a subsequent clock
exception or timeout from the interrupted observation. Existing terminal state
and response counts remain intact, and no new evidence is handed out.

This change preserves the existing start, synchronization and closing APIs,
protocol interpretation, fifteen-minute maximum lifetime and API-observed expiry.
It does not introduce a timer, send a request or authenticate a session.

## Verification and next work

103 additional executable checks reuse the existing public synthetic session
fixture. They cover fractional elapsed-time accumulation, timestamp and frequency
callbacks during synchronization, waiting for a closing candidate and accepting
the closing response. Cases include conversion exceptions, negative elapsed
time, regression, expiry, cancellation, stopping, disposal and callbacks that
terminate and then throw or advance the clock beyond the deadline.

All three clock observations surrounding response binding, semantic evaluation
and final publication are tested for conversion failure in both response stages.
Tests inspect actual pending fields, terminal state, remaining time, evidence
withholding and unchanged handled counts. The new suite reproduced an uncaught
conversion exception before the fix. Full Windows CI then passed with zero
warnings or errors, including the existing 24 synchronization session traces and
626 checks. No wire fixture or generator change is required.

Next is session terminal evidence ownership and cleanup across the read and
synchronization coordinators. Broader session coordination, authenticated
security and transport remain pending under M1-05. Live banking remains disabled.
