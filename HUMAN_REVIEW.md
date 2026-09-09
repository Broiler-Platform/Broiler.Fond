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
Full request/session
context validation, live SCA continuation, authenticated protocol security,
remaining business codecs, domain ingestion and capability activation remain pending. It is not a
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
