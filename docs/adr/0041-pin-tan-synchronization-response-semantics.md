# ADR 0041: Assembled PIN/TAN synchronization response semantics

- **Status:** Accepted (local synthetic comparison only)
- **Date:** 2026-09-09
- **Related:** [Unsigned synchronization scope](0029-synchronization-response-context.md),
  [assembled reference binding](0039-pin-tan-assembled-request-reference-binding.md),
  [initialization semantics](0040-pin-tan-initialization-response-semantics.md)

## Source and scope

The synchronization rules reviewed in ADR 0029 and the PIN/TAN envelope sources
recorded in ADRs 0014/0038 remain the basis for this increment. They come from
the [official FinTS specification](https://www.fints.org/de/spezifikation).
No unsigned response is rewritten to simulate signed numbering or envelope scope.

`FinTsPinTanSynchronizationEvidence.Evaluate` compares one assembled response
binding, one synchronization data set and optional explicit prior-dialogue context.
It retains those exact objects and exposes top-level issues, detailed synchronization
issues and a unique report only when every comparison matches. No recovered value
is applied, incremented, persisted or used to retry a request.

## Provenance and request rules

The candidate must be synchronization and the report data set must originate from
the exact bound response instance. Even byte-identical foreign response instances
cannot be mixed. Wrong-kind/source results retain the inputs for inspection but
skip detailed comparison and never select a report. Reparsing reports from the same
response instance is permitted. Binding issues always withhold matching evidence.

The profile follows the candidate's explicitly bound PIN:1/PIN:2 selection; no new
profile override or fallback is introduced. Existing request rules require system
status 1, zero system ID for assignment, and an assigned system for message-number
recovery. PIN/TAN signature-reference synchronization cannot produce the matching
signature context required to construct these candidates.

Message recovery requires the caller's previous dialogue ID and last submitted
message number. A returned counter above that bound, reuse of the previous dialogue
as the newly reported synchronization dialogue, or recovery context supplied for
system assignment requires review. These are consistency checks on caller evidence,
not proof of which messages the bank received or executed.

## Reports, statuses and envelope identity

Exactly one supported HISYN-4 report with the requested mode's shape is required.
Missing, duplicate, empty, conflicting, cross-mode and future reports remain visible
without choosing a winner. System IDs `0` and `unbekannt` cannot become assignments.
The unsigned and assembled comparisons share an internal report-check helper;
the unsigned public behavior and its envelope restrictions remain unchanged.

HIRMS execution must refer to the actual Synchronization role at segment 5, or
HIRMG must report message-level execution. Success on identification, preparation
or signature alone is insufficient. The local vocabulary remains HIRMG 0010/0020
and HIRMS 0020. Errors, conflicting or indeterminate outcomes, unknown/pending
codes, duplicates, element references and nonempty reply parameters require review.
All text and raw status evidence remain unchanged.

Envelope country, institution and user must match the candidate's bound identities.
For message recovery, the envelope system must match the existing request system.
For assignment, this local canonical comparison requires the envelope system to
match the unique nonreserved system ID reported by HISYN, rather than the outgoing
zero placeholder. Other representations require review; this is not a claim of
universal bank compatibility. If report shape or uniqueness is unresolved, no
assignment is selected for the system comparison and report issues already withhold
matching evidence. Envelope timestamp, filler and role fields remain untrusted
schema observations; identity equality provides no authentication.

Other data, including returned BPD/UPD, remain uninterpreted synchronization data
and require separate integration. This increment neither merges parameters nor
silently applies initialization semantics to a synchronization dialogue.

## Closing boundary and ownership

`NextStep` is `CloseAndReinitializeRequired` for matching evidence and
`StopForReview` otherwise. No outcome produces a ready banking session. The
reported dialogue and exact counter/system fields remain untrusted observations;
the comparator does not send a close request or claim a closing acknowledgement.

No credentials or assembled wire buffers enter this API. Parser bounds continue
to limit response/report work; loops observe cancellation. Repeated/concurrent
evaluation is pure, consumes no report and provides no replay barrier. A later
attempt lifecycle must own submission and one-time response handoff. Live input,
authentication, transport, recovery application and session activation stay disabled.

## Verification and next work

Thirty-three independent Python fixtures and 217 checks cover both profiles,
escaped assignments, exact counter bounds, prior-context mismatches, missing/
duplicate/future/conflicting reports, status roles, pending/error observations,
wrong references, envelope identities/profile, missing wrappers and extra parameters.
Additional checks reject foreign sources and initialization candidates, preserve
exact observations, exercise null/cancellation/concurrency behavior and run a
message-recovery candidate through the local synthetic envelope writer. The full
unsigned synchronization suite also passes after report-helper extraction.

Next is a bounded assembled PIN/TAN initialization attempt lifecycle, with explicit
candidate/response ownership and terminal cleanup. Assembled synchronization/closing
lifecycle integration, procedure/challenge checks, secure input, authenticated
transport and controlled-bank acceptance remain pending.
