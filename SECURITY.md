# Security policy

## Milestone 0 status

Milestone 0 contains project boundaries, contracts, policy checks, and an inert
Windows host. It does **not** implement bank connectivity, credential handling,
profile persistence, balance retrieval, or payments. No live institution is
supported and no security certification or release approval is claimed.

## Supported versions

There are no supported public versions yet.

| Version | Security support |
| --- | --- |
| None | No distributable version exists |

This table must be updated before the first external distribution.

## Intended trust boundary

The accepted product boundary is a purely user-operated client:

- the installed client communicates directly with a user-configured financial
  institution endpoint;
- Broiler operates no credential relay, account aggregation, payment
  initiation, or financial-data backend;
- financial data and secrets remain on the user's device except when sent to
  the selected institution as part of an explicit banking operation; and
- the product contains no telemetry, analytics, tracking identifier, automatic
  crash upload, or background reporting path.

These are architecture requirements, not claims that unfinished features have
already been made secure.

## Reporting a vulnerability

The private vulnerability-reporting channel is **not configured yet**. This is
a release blocker: the project must not be distributed externally until a
monitored private channel, responsible owner, response process, and public
instructions have been established here.

Until then:

- do not place credentials, PINs, TANs, account data, profile files, packet
  captures, or undisclosed vulnerability details in a public issue; and
- if you already have access to a project maintainer through a private trusted
  channel, send only enough information to establish contact and agree on a
  secure transfer method.

No response-time or remediation-time commitment exists before that reporting
process is established.

## Release security evidence

Every distributed candidate must pass the repository's prerelease checklist
and receive the two-person approval recorded in [HUMAN_REVIEW.md](HUMAN_REVIEW.md).
Approval applies only to the recorded source revision and artifact digests.
Payment-capable candidates also require an independent penetration test of the
payment path and closure of release-blocking findings.

