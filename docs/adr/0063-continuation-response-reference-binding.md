# ADR 0063: Assembled continuation-response reference binding

- **Status:** Accepted (local structural comparison only)
- **Date:** 2026-10-14
- **Related:** [First-read reference binding](0055-first-read-response-reference-binding.md),
  [continuation envelopes](0062-continuation-pin-only-request-envelope.md)

## Scope

`FinTsPinTanReadContinuationResponseBinding.Evaluate` compares a qualified
second-page balance candidate with one parsed response. It retains the exact
candidate and response objects, an immutable list of source/target links and the
existing reference-issue flags. The protocol rules and supported read vocabulary
remain those recorded in ADR 0055; no new status interpretation is introduced.

The response must have an explicitly parsed PIN/TAN envelope with the candidate's
profile. Its bank message number and referenced client number must match the
candidate's counters (both 3). The outer and referenced dialogue must equal its
assigned dialogue. Missing references, earlier/later counters and foreign or padded
dialogues remain mismatches. There is no zero-dialogue substitution.

Both the first read and second page use read segment 3, so segment-reference
matching alone cannot distinguish them. The exact first-page response retained
in the continuation provenance fails the new message comparison. Conversely,
the first-read comparator rejects a message-3 response for its message-2 candidate.

## Segment roles and retained observations

Each non-HIRMG body segment produces a link to its exact source segment and the
matching descriptor from the candidate's seven-entry map. Missing and unknown
references remain distinct flags with null targets. HIRMS may name any actual
request segment; a success or error naming HNSHK 2 remains attached to security,
not the balance operation at 3. The comparison never renumbers response segments.

HISAL data must reference read segment 3. HISPA data carries SegmentRoleMismatch
because the qualified continuation is a balance request. Known HISAL versions
6/7/8 remain structural observations, including a version different from the
request. Future versions and unknown data codes carry UninterpretedData without
fallback. Business payload parsing and exact version equality remain semantic
work, as in the first-read binder.

Matching references do not imply success or completeness. Message aborts, scoped
errors, pending/unavailable reports, duplicate data and conflicting statuses retain
their original evidence. Partial-report parameters, including a repeated token,
remain unvalidated observations; this comparison neither approves progress nor
schedules another page. Account/currency scope and envelope system/key identity
are also outside this structural layer.

## Ownership and limits

Evaluation receives no credential, encoded secret output, clock or transport.
It consumes no response or replay state and does not prove successful encoding,
submission, authentication, TAN omission or SCA readiness. Candidates with missing
PIN bounds can still have structurally matching references. Retained account and
response data remain private and untrusted.

Existing parser budgets bound response size, and the target map has seven entries.
Cancellation is checked before work, for each body segment and before publication.
Null inputs produce fixed diagnostics without source values. Repeated, reparsed
and concurrent comparisons preserve source identity without mutation. Existing
first-read and other response comparators remain unchanged.

## Verification and next work

Fifty-five independent Python fixtures and 576 checks cover message-3 binding,
both profiles, procedure versions, HKSAL versions 6/7/8, account/token/options
variants, missing wrappers, wrong profiles, stale/future counters, dialogue
mismatches, all seven reply target roles, missing/unknown references, discovery
data, future/unknown payloads and combined issues.

Tests verify original request, predecessor and response byte preservation, exact
source/target identity, immutable links, cancellation, null diagnostics, cultures
and concurrency. Eight integration cases compare every target with actual
synthetic envelope output and retain usable binding after credential disposal
and output erasure. Additional tests preserve malformed/duplicate/foreign data
for semantic review and reject the exact predecessor and first/second-page
cross-bindings. Only public synthetic credential output is parsed in tests.

Full Windows CI passed with zero warnings/errors. All 54 FinTS fixture files
reproduced byte-for-byte; existing fixture contents were preserved.

Next is assembled continuation-response semantics: exact-source read data,
account/version/currency and envelope identity checks, scoped outcomes and partial
token observations. Later-page lifecycle, TAN/challenge integration, authenticated
transport and live qualification remain pending. The host remains inert.
