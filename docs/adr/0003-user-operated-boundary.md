# ADR 0003: User-operated boundary

- **Status:** Accepted
- **Date:** 2026-09-04
- **Decision owners:** Product architecture and privacy

## Context

The product is intended to let a person operate their own bank access without
placing credentials or financial data in a Broiler-operated intermediary. The
operating model also needs an unambiguous privacy promise.

## Decision

Broiler Fond - Finance on Demand is a purely user-operated client.

- The installed client communicates directly with a financial institution
  endpoint explicitly configured by the user.
- The initial institution directory is manual. No institution is inferred from
  a central Broiler service.
- Broiler operates no credential relay, account aggregation, account-
  information service, payment-initiation service, synchronization service, or
  financial-data backend for the product.
- The client contains no telemetry, analytics, tracking identifier, automatic
  crash upload, or background reporting path.
- Bank credentials and financial data are not sent to Broiler or an unrelated
  third party. Network exchange is limited to an operation the user performs
  with the selected institution, plus separately reviewed distribution/update
  infrastructure that must never receive banking payloads or credentials.
- Any diagnostic export is created locally, explicitly initiated by the user,
  reviewable before sharing, and never transmitted automatically.

Any future proposal that introduces a relay, cloud synchronization, remote
financial-data processing, or telemetry changes the product boundary and
requires a new ADR plus legal, privacy, threat-model, and human release review.

## Consequences

- Institution connectivity must work directly from the client or remain
  unsupported.
- Operational support cannot depend on installed-user telemetry; evidence must
  come from controlled fixtures, simulators, test accounts, and user-chosen
  diagnostic exports.
- The compatibility promise starts deliberately small and expands only through
  verified rows.
- Local compromise remains in the threat model and cannot be transferred to a
  server-side control plane.

## Milestone 0 effect

The Windows host is inert and the kernel exposes no bank operation. There is no
network, directory, credential, diagnostic-export, or telemetry implementation
in Milestone 0.

