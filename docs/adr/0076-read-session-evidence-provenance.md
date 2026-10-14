# ADR 0076: Read-session evidence provenance

- **Status:** Accepted (local evidence binding)
- **Date:** 2026-10-14
- **Related:** [Read sessions](0072-assembled-read-session-attempt.md),
  [terminal ownership](0075-session-terminal-evidence-ownership.md)

## Accepted evidence is the source of subsequent candidates

Dialogue identifiers and sequential counters do not identify the initialization
evidence accepted by a particular session. Previously, an operation or closing
candidate could use a separately evaluated initialization with matching values.
Continuation candidates could substitute predecessor evidence when the account,
token and message number matched. A synchronization closing context could also
enter a read session if its dialogue and counters matched.

The read coordinator now requires reference identity at each handoff:

- All-account and first balance candidates must use the exact accepted
  `RequirementsEvidence` in their signature context.
- A continuation must use the exact accepted first-page or latest continuation
  evidence and retain the original first-page capability context.
- Closing must use the exact accepted `InitializationEvidence`.

Byte-identical reparsing, independent semantic reevaluation and evidence from
another session do not satisfy these requirements. Basic initialization without
requirements can still close, but cannot import an operational requirements report
from elsewhere. Callers preparing reads must supply read parameters through the
existing initialization overload, then construct candidates from the returned
requirements evidence. Each continuation is built after its predecessor response
has been accepted. Closing uses the initialization evidence retained by the active
session. No public method signatures change.

A provenance mismatch returns `ContextMismatch` and records the scalar
`EvidenceSourceMismatch` issue. It leaves the active stage, pending slot, accepted
evidence, counters, accumulated data and original deadline intact. A valid candidate
can then be submitted. Existing wrong-state, dialogue and replay checks remain in
place. Reference identity establishes local provenance only; it is not bank
authentication or permission to send a request.

## Fixtures and verification

The original session fixtures initialized with a basic parameter response while
operation candidates embedded a different response containing read parameters.
The generator now uses the operation's initialization response for those sessions.
The test driver accepts its read parameters and constructs operation, continuation
and closing candidates from actual session handoffs. Wire expectations and the
28 existing state traces retain their intended transitions.

117 additional checks cover separately parsed and foreign-session initialization,
missing operational requirements, closing after an operation and direct closing,
synchronization evidence presented to a read session, and both continuation
predecessor overloads. Reevaluation tests reuse the exact original response binding
and request while producing a different evidence instance. Negative candidates
must not advance the session; a valid retry must succeed, including page three.
Terminal ownership checks continue to verify release of retained references.

The new tests failed before the guards were added. Full Windows CI passed after
the fix, including 3,610 existing read-session checks and the synchronization and
ownership suites, with zero build warnings or errors. All 64 generated fixture
files reproduce byte-for-byte from an isolated generator run.

Next is read-session operation scoping and per-operation page accounting under
M1-05. Authenticated security, activation and transport remain pending.
