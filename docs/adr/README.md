# Architecture decision records

Architecture decision records (ADRs) capture decisions that constrain the
product across milestones. An accepted ADR records intent; it does not prove
that a feature has been implemented or security-reviewed.

## Status values

- `Proposed`: under discussion and not binding.
- `Accepted`: the current architectural rule.
- `Superseded`: replaced by a later ADR, which must be linked.
- `Rejected`: considered but not adopted.

Changes to an accepted decision require a new ADR. Do not silently rewrite the
history or weaken a boundary in implementation.

## Index

| ADR | Status | Decision |
| --- | --- | --- |
| [0001](0001-kernel-boundary.md) | Accepted | Platform-independent, BCL-only .NET kernel |
| [0002](0002-portable-profile-container.md) | Accepted | XML documents in a compressed, authenticated-encrypted profile stream |
| [0003](0003-user-operated-boundary.md) | Accepted | Purely user-operated client with no telemetry or Broiler financial-data backend |

