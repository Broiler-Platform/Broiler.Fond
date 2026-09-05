# Human release review

> **Current status: PENDING — no distributable candidate has been approved.**

This file defines the human approval record for every externally distributed
prerelease and release of **Broiler Fond - Finance on Demand**. It does not
approve the repository, a branch, or any artifact by itself.

Milestone 0 is an inert engineering scaffold. It is not a banking-capable
prerelease and has no release approval.

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

