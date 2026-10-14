# Human release review

> **Current status: PENDING — no distributable candidate has been approved.**

This file defines the human approval record for every externally distributed
prerelease and release of **Broiler Fond - Finance on Demand**. It does not
approve the repository, a branch, or any artifact by itself.

The current M1 work provides manual endpoint validation, in-memory identity
allocation/startup checks, account rediscovery and value projections, and a proposed
storage format exercised only by synthetic test references. Structured in-memory
diagnostics, a synthetic read workflow harness and bounded FinTS byte parsing
are also implemented, along with typed response schemas and local dialogue
correlation, bounded BPD/UPD, read-capability and PIN/TAN envelope/HIPINS
structural evidence, plus HITANS/HITAN version 6/7 procedure and challenge
schemas and restricted permitted-procedure/request-bound challenge comparison.
An in-memory synthetic SCA attempt model adds bounded queries, cancellation,
monotonic timeout and per-instance replay rejection. Credential-free discovery
and balance schemas preserve exact source identifiers, separate optional amounts
and source timestamps. Pure single-account read comparisons now check scoped
account/capability evidence, response references and partial-page provenance.
A bounded in-memory read attempt adds explicit request/response consumption,
cancellation, timeout and terminal reference cleanup.
Scoped unavailable observations distinguish absent/empty reports from missing
responses, errors and real zero values without updating cached accounts.
Pure all-account discovery matching preserves unknown, duplicate, conflicting
and unmatched identity evidence without allocating or removing accounts.
Its local attempt adds one-time evidence handoff, cancellation, timeout and
terminal context release; caller-owned comparisons remain untrusted.
Typed unsigned discovery/balance request encoding now has exact independent
wire fixtures, strict text handling and shared schema validation.
Restricted unsigned initialization now covers HKIDN/HKVVB fields and explicit
anonymous identity rules; supplied product text provides no registration evidence.
Initialization response binding now checks dialogue and bank/user/customer scope,
retaining missing parameters and unresolved evidence without session activation.
A local initialization attempt adds pinned user context, one-time response
handling, monotonic timeout, cancellation and terminal reference cleanup.
Credential-free synchronization schemas and unsigned encoding preserve exact
reported identifiers/counters and unresolved shapes without applying recovery state.
Pure synchronization context checks now cover mode/profile requirements and
prior-dialogue bounds, with an explicit close/reinitialize requirement on matches.
Dialogue-end schemas, unsigned encoding and request-bound reply comparison now
distinguish reported closure from explicit abort without changing session state.
A bounded local synchronization-and-closing attempt now adds explicit close
recording, one-time stage evidence, a shared deadline and terminal context cleanup.
Restricted PIN/TAN signature-header schemas now preserve exact header observations
and profile/code constraints without credential handling or authentication.
Pure signature-header context checks now compare explicit initialization/
synchronization identity and sourced HITANS/3920 observations without authorization.
Typed signature-header encoding now has independent exact wire fixtures, strict
text handling and precision checks; it creates no complete signed message.
A local credential ownership primitive now clears transferred input and owned
bytes, with one-time TAN copy-out, explicit disposal and access-time expiry checks.
It is verified with synthetic values; secure input and live session integration
remain pending.
Local HNSHA-2 encoding now checks credential placement and writes to bounded
caller output with temporary/failure-path cleanup; all verification uses public
synthetic credentials and no live path is enabled.
Plain initialization/synchronization assembly now adds exact framing, bound
segment renumbering and whole-message secret cleanup. Plaintext request envelopes
now add bound credential-free metadata, exact binary payload framing and final-output
cleanup. Immutable candidate metadata now compares response references with the
actual assembled segment roles. Matching references do not validate execution or
returned parameter identities. A separate assembled initialization comparator now
checks execution reports, bank/user/customer scope and envelope identities using
the exact bound response; matching observations still activate no session.
Assembled synchronization now compares exact-source reports, bound status and
recovery limits; matching evidence still requires closing and reinitialization.
A local assembled initialization attempt now owns one candidate and deadline,
hands scoped evidence out once and releases pending metadata on terminal outcomes.
The assembled synchronization attempt now retains exact candidate/recovery scope,
returns evidence once under a fixed deadline and releases both references on
terminal outcomes. Matching evidence requires closing/reinitialization; no recovered
values are applied and assembled closing integration remains pending.
A separate closing context and PIN-only envelope encoder now check synchronization
dialogue/counter and identity scope, preserving bounded secret cleanup. Encoding
does not consume the handoff or prove closure. Assembled closing response checks
now bind actual segment roles, dialogue and envelope identity, distinguishing
reported closure/abort from review. A bounded closing attempt now pins one
candidate, returns scoped evidence once and releases metadata on terminal paths.
The synchronization handoff retains exact provenance; reported closure still
requires fresh initialization, with broader session coordination pending.
An explicit initialization path now integrates returned TAN advertisements and
permission reports with exact response provenance and one-time combined handoff.
Missing/ambiguous reports remain review evidence; no procedure is activated.
Combined initialization now integrates HIPINS requirements with exact parameter
provenance, nullable bounds, conservative ambiguity checks and one-time handoff.
Reported TAN flags grant no permission or SCA exemption. First-read signature
context now reuses the combined returned evidence with exact dialogue/counter,
identity and procedure checks. An explicit initialization path now integrates read
advertisements; first-read account checks compare exact returned permissions,
identities, signature requirements and request options. First-read credential
comparison now checks individual supplied byte spans against reported bounds and
TAN formats, returning only fixed issues and preserving caller ownership.
PIN-only first-read trailer encoding now applies those checks to owned staging
bytes and preserves expiry, cancellation and failed-output cleanup. Plain first-read
assembly now adds bound segment numbering, exact framing and whole-message secret
cleanup. First-read envelope assembly now preserves exact binary payloads and
credential-free candidate metadata, with cleanup through final output handoff.
Assembled read-response binding now maps exact source references to the actual
request roles and compares dialogue, counters and profile. Assembled read semantics
now check exact-source data, account/version/currency scope, envelope identity and
read-scoped statuses, preserving unavailable and partial observations. A bounded
assembled read attempt now adds a fixed deadline, one-time response handoff and
terminal pending-metadata cleanup. Balance-continuation context now compares
qualified partial evidence with exact tokens, unchanged account/options and next
counters. Continuation signature context now binds header identity, unchanged
procedure selection and explicit control scope, withholding qualified requirements
on review. Continuation PIN-only trailers now apply inherited bounds to actual
owned PIN bytes with bounded staging and failure cleanup. Continuation plain requests
now preserve exact message-3 framing, tokens and segment roles with whole-message
failure cleanup. Continuation envelopes now preserve exact binary payloads and bound
candidate metadata with final-output cleanup. Continuation response binding now
compares exact message-3 scope and actual segment roles while retaining unresolved
payload/status observations. Continuation semantics now compare exact-source
read data, inherited account/version/currency and envelope identity, preserving
separate second-page outcomes and raw tokens. A bounded assembled continuation
attempt now adds a fixed deadline, one-time response handoff and terminal
metadata cleanup. Later-page balance continuation context now enables continuation
beyond page two with 128-page limit and sequential counter checks. A multi-page
PIN/TAN balance refresh attempt now coordinates page one and sequential continuation
pages with a page budget, monotonic counter tracking, replay defenses and balance
aggregation. All-account capability context under PIN/TAN now evaluates SEPA account
discovery permissions, signature requirements, unlisted blocked operation policies, limits
and HIPINS credential rules without authorization or credential leakage. All-account request assembly, envelopes and candidate metadata now preserve exact framing, segment roles
and whole-message secret cleanup. All-account response reference binding and semantics now correlate
candidate segment roles, verify envelope identity, scope status strictly to read segment 3, and match
returned accounts against parameter evidence with exact-source provenance. A bounded assembled all-account discovery
attempt now coordinates candidate metadata, absolute deadlines and one-time evidence handoff with terminal cleanup.
An assembled PIN/TAN read session attempt coordinator now manages the full FinTS 3.0 PIN/TAN dialogue
lifecycle across initialization, operational read (all-account discovery or multi-page balance refresh), and
dialogue closing under a single monotonic deadline, strict dialogue ID pinning, sequential message counters,
replay defenses, transient foreign-scope segregation, caller-owned evidence handoff, and immediate terminal cleanup.
TAN/challenge integration, full request/session context validation, live SCA continuation, authenticated protocol
security, remaining business codecs, domain ingestion and capability activation remain pending. It is not a
banking-capable prerelease and has no format or release approval.

## Required people

Each candidate requires two named people:

- a **release owner**, accountable for scope, build provenance, and release
  readiness; and
- a **security reviewer**, accountable for an independent review of the exact
  candidate and its evidence.

One person must not fill both roles for the same candidate. Payment-capable
candidates additionally require the independent penetration-test evidence
specified by the roadmap and release checklist.

## Candidate identity

Approval is bound to immutable candidate evidence. Copy this block into the
release record and replace every placeholder.

| Field | Value |
| --- | --- |
| Product | Broiler Fond - Finance on Demand |
| Candidate version | `UNSET` |
| Release channel | `UNSET` (`prerelease` or `release`) |
| Source commit | `UNSET` |
| Build workflow/run | `UNSET` |
| Artifact names | `UNSET` |
| Artifact SHA-256 digests | `UNSET` |
| Review checklist revision | `UNSET` |
| Review opened (UTC) | `UNSET` |

Any source, dependency, configuration, packaging, signing, or artifact change
after review invalidates approval and requires a new candidate record.

## Review procedure

1. Create a candidate record containing the immutable identity above.
2. Complete [the prerelease checklist](docs/release/prerelease-checklist.md)
   against that exact candidate.
3. Record each finding with an owner, severity, disposition, and evidence.
4. Block distribution while any required item is incomplete or any
   release-blocking finding remains open.
5. Have the release owner and security reviewer sign independently.
6. Retain the signed record and evidence with the release metadata.

Allowed record states are `PENDING`, `BLOCKED`, `APPROVED`, and `SUPERSEDED`.
Only an `APPROVED` record for the exact artifacts permits distribution.

## Approval record

The following is deliberately unsigned.

| Role | Name | Decision | Date (UTC) | Evidence/signature |
| --- | --- | --- | --- | --- |
| Release owner | `UNSET` | `PENDING` | `UNSET` | `UNSET` |
| Security reviewer | `UNSET` | `PENDING` | `UNSET` | `UNSET` |

**Candidate decision:** `PENDING`
