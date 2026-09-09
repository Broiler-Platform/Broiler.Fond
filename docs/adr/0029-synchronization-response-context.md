# ADR 0029: Synchronization response context and profile requirements

- **Status:** Accepted (pure synthetic comparison only)
- **Date:** 2026-09-08
- **Related:** [Synchronization schemas](0028-synchronization-schemas.md),
  [initialization context](0026-initialization-response-scope.md)

## Sources and profile scope

Reviewed the following documents from the
[official FinTS specification](https://www.fints.org/de/spezifikation):

- Formals, 2017-10-06, C.8–C.8.2.2: synchronization modes, conditional returned
  counters, reserved signature reference and the close/reinitialize requirement.
  PDF hash is recorded in ADR 0028.
- Security – Sicherheitsverfahren HBCI, 2024-06-11, B.1.2 and system-ID setup:
  the current RAH profile set and card/software distinctions. SHA-256:
  `37a82f7e51386f605154f1d5f47d64dc97ca3cab7161bc8769b9c7ca0f32735e`.
- Security – Sicherheitsverfahren PIN/TAN, 2020-07-10, B.7: system identifiers
  and the absence of PIN/TAN signature-ID recovery. SHA-256:
  `77bb5724cb391434cd7188924a1ff89bff040bf340dc49a09c92cdeb4ee7dc3a`.

`FinTsSynchronizationEvidence` compares an existing restricted unsigned request,
one parsed synchronization dataset, an explicit caller-selected profile hypothesis
and optional prior-dialogue recovery context. It retains those exact objects and
all reported values without applying an identifier or counter update.

The profile hypothesis is not negotiated, authenticated or extracted from a
signature. Supported comparison rules are deliberately narrower than schema
parsing and do not constitute implementation of a security procedure:

| Profile hypothesis | System-ID mode | Message-number mode | Signature-reference mode |
| --- | --- | --- | --- |
| PIN/TAN 1 or 2 | System status required; outgoing ID zero | Assigned system ID and explicit prior context | Prohibited |
| RAH-10 | System status required; outgoing ID zero | Assigned system ID and explicit prior context | One signing-key reference; no digital-signature reference |
| RAH-7 | Prohibited for the card profile | Card ID, status not required and explicit prior context | Separate signing-key and digital-signature references |
| RAH-9 | Prohibited for the card profile | Card ID, status not required and explicit prior context | Requires review; conditional response layout unresolved |
| Unspecified or unknown | Requires review | Requires review | Requires review |

The 2017 Formals counter condition names RAH-7 and historical RDH profiles but
does not resolve the newer RAH-9 case. The comparator does not infer its counter
layout or silently select a legacy profile. Unknown enum values remain review
evidence. An assigned system/card identifier cannot be `0` or `unbekannt`.

## Binding, status and response shape

The response must be the first bank message, reference the request's message
number and use the newly assigned dialogue in both its header and reference.
Reserved or padded dialogue assignments require review. HISYN must reference
HKSYN; HIRMS may reference identification, preparation or synchronization.
Other segments are checked against the preparation reference but remain opaque
and require review, including returned parameter data. This comparator does not
merge or activate initialization parameters.

The narrow status vocabulary accepts HIRMG 0010/0020 and scoped HIRMS 0020.
Execution must be reported for the whole message or the synchronization request;
identification status alone is insufficient. Errors, conflicting classes,
indeterminate/unknown codes, duplicate codes, element references and nonempty
reply parameters require review. Explicitly parsed PIN/TAN envelopes remain
outside this unsigned comparison scope and require review for every hypothesis.

Exactly one supported report with the requested mode's field shape is required.
Missing, duplicate, empty, cross-mode and opaque reports stay available without
selecting a winner. A returned system ID of `0` or `unbekannt` is unresolved.
Signature fields must have the profile-specific layout above. A returned signing
or digital reference equal to the reserved all-nines synchronization sentinel
requires review; it is never treated as a normal recovered counter.

Counters remain exact observations. The comparator does not increment them,
choose the maximum across institutions, re-sign pending work or infer whether an
operation executed. Schema-level zero counters remain distinct from missing fields.

## Prior-dialogue context and the closing boundary

Message-number mode requires `FinTsSynchronizationRecoveryContext`, containing
the caller's previous dialogue ID and last submitted client message number.
The previous ID must be a bounded nonreserved identifier and the message bound
must be from 1 through 9999. A reported message above that bound, reuse of the
previous dialogue ID as the synchronization dialogue, or recovery context supplied
for a different mode requires review.

This is a consistency check on caller-owned observations. HISYN does not transmit
the previous dialogue ID, so the comparison cannot authenticate that association
or prove transport provenance. It initiates no automated recovery or retry.

For matching evidence, `NextStep` is always `CloseAndReinitializeRequired`.
For unresolved evidence it is `StopForReview`, with no selected `MatchingReport`.
This explicitly records that synchronization does not yield a dialogue ready for
business requests. It does not send a close request or claim a close acknowledgement;
the actual closing lifecycle and a fresh authenticated initialization remain pending.

## Bounds, verification and next work

Existing response limits apply, including 128 synchronization reports. Cancellation
is checked through response, reply and report traversal. Default diagnostics
exclude identifiers and recovery values. Explicit source objects remain untrusted
private data unsuitable for logging. Repeated evaluation consumes no replay state.

Fifteen independent Python fixtures cover profile/mode combinations, explicit
prior context, signature layouts, unresolved RAH-9, wrong shapes, empty/opaque/
duplicate reports, foreign references and reported message numbers above the
submitted bound. The executable suite adds 149 checks for those cases, status
ambiguity, reserved values, context boundaries, all 128 reports, source identity,
envelope restrictions, cancellation and the mandatory next-step disposition.

Next is credential-free dialogue-end schemas and unsigned encoding, needed to
make the close/reinitialize boundary concrete before a synchronization lifecycle.
Authenticated profiles, recovery application, transport, durable replay and
domain ingestion remain pending. The host stays inert.
