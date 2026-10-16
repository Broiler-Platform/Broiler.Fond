# ADR 0079: Read-session initialization result handoffs

- **Status:** Accepted (caller-owned evidence handoff)
- **Date:** 2026-10-15
- **Related:** [Read sessions](0072-assembled-read-session-attempt.md),
  [terminal ownership](0075-session-terminal-evidence-ownership.md),
  [evidence provenance](0076-read-session-evidence-provenance.md)

## Complete handoffs for every initialization overload

The read session's basic and procedure-aware initialization overloads could
report `DialogueOpen` without any initialization evidence in the returned result.
`HasMatchingEvidence` consequently returned false, and callers had to fetch
session properties before terminal cleanup removed those references. Semantic
review results on those paths also omitted their diagnostic evidence.

`FinTsPinTanReadSessionResult` now exposes `InitializationEvidence` and
`ProcedureEvidence` alongside the existing `RequirementsEvidence`. Each scoped
initialization response returns exactly the layers evaluated by its overload:

| Initialization input | Returned evidence |
| --- | --- |
| Basic parameters | Initialization |
| Procedures and permissions | Initialization and procedure qualification |
| Procedures, permissions and HIPINS | Initialization, procedure and requirements qualification |
| Those inputs plus read parameters | The same three layers, with read parameters retained in requirements evidence |

The returned layers share the original object references: requirements reference
the returned procedure evidence, and procedure evidence references the returned
initialization evidence. No reparsing or reevaluation occurs at handoff. On
successful initialization these are also the exact objects retained by the
session, so callers can build closing or operation candidates directly from the
result while preserving provenance.

Review outcomes return the same diagnostic layers even though the session has
already released its own references. Evidence presence does not imply matching
qualification. `HasMatchingEvidence` checks requirements when present, otherwise
procedure qualification, otherwise basic initialization. A matching lower layer
cannot override a review outcome from a higher layer.

## Publication and ownership boundaries

The new properties are null for foreign responses, calls before initialization,
wrong-state calls, repeated or upgraded initialization calls, terminal replay and
responses interrupted before publication. Closing and operational results do not
repeat initialization handoffs. Concurrent submissions still yield one complete
initialization result.

Caller-owned results survive clean closing, semantic review, cancellation,
stopping, disposal, timeout and clock failure. Session cleanup still releases
its references. Existing transitions, response counts, deadline handling,
qualification rules and public initialization method signatures are unchanged.
The result API additions do not activate a session or authenticate a bank.

## Verification and next work

1,099 additional checks reuse existing synthetic fixtures across all four
overloads. They cover matching and review outcomes, exact source identity,
qualification precedence, foreign-response rejection, closing constructed from
returned evidence, repeated and upgraded calls, terminal ownership, token
cancellation, and eight concurrent submissions. Cancellation and deadline expiry
are injected at all three clock boundaries around binding and semantic evaluation
for every overload, verifying that no partial handoff escapes.

The tests reproduced missing basic and procedure handoffs before publication was
corrected. Full Windows CI passed with zero build warnings or errors, including
formatting, the inert host smoke test, existing suites and all 1,099 new checks.
No wire fixture or generator change is required.

Next is read-session response accounting across review and terminal outcomes
under M1-05. Authentication, activation and transport remain pending.
