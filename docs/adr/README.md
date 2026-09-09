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
| [0004](0004-profile-envelope-v1.md) | Proposed | Exact v1 encrypted-envelope framing, KDF bounds, and independent vectors |
| [0005](0005-profile-snapshots-v1.md) | Proposed | Bootstrap XML/ZIP contract and generation/recovery/purge protocol candidate |
| [0006](0006-local-identity-allocation.md) | Accepted | In-memory store-wide identity batches, exhaustion and startup validation |
| [0007](0007-account-source-rediscovery.md) | Accepted | Lossless source locators and exact rediscovery with quarantined candidates |
| [0008](0008-account-values-and-exact-money.md) | Accepted | Exact money, immutable balance provenance, freshness and per-currency totals |
| [0009](0009-synthetic-workflows-and-diagnostics.md) | Accepted | Test-only read workflows and opt-in structured in-memory diagnostics/previews |
| [0010](0010-fints-byte-syntax-and-framing.md) | Accepted | Bounded FinTS 3.0 byte syntax, opaque outer framing and independent synthetic vectors |
| [0011](0011-fints-responses-and-dialogue-correlation.md) | Accepted | Typed HIRMG/HIRMS replies and single-pending-request in-memory correlation |
| [0012](0012-fints-parameter-evidence.md) | Accepted | Bounded HIBPA/HIUPA/HIUPD evidence and explicit permission-policy distinctions |
| [0013](0013-read-capability-evidence.md) | Accepted | Read-operation parameter schemas and conservative explicit-version capability evidence |
| [0014](0014-pin-tan-envelope-and-parameters.md) | Accepted | Bounded PIN/TAN response-envelope and HIPINS parameter evidence |
| [0015](0015-tan-procedure-and-challenge-schemas.md) | Accepted | HITANS 6/7 procedure and HITAN 6/7 challenge schemas with untrusted evidence preservation |
| [0016](0016-permitted-procedures-and-challenge-context.md) | Accepted | HIRMS 3920 reports and pure request-bound challenge comparison |
| [0017](0017-in-memory-sca-continuation.md) | Accepted | One in-memory SCA attempt with bounded queries, monotonic timeout and per-instance replay rejection |
| [0018](0018-account-discovery-and-balance-schemas.md) | Accepted | Credential-free HKSPA/HISPA-1 and HKSAL/HISAL-6/7/8 schemas preserving untrusted account and amount evidence |
| [0019](0019-read-request-response-context.md) | Accepted | Pure single-account read context comparison with scoped status and bounded partial-page provenance |
| [0020](0020-in-memory-read-refresh-attempt.md) | Accepted | One in-memory read attempt with explicit continuation, bounded lifetime/pages and per-instance response consumption |
| [0021](0021-scoped-read-unavailability.md) | Accepted | Scoped 3010 unavailable observations, distinct empty/missing/data shapes and terminal local handling |
| [0022](0022-all-account-discovery-evidence.md) | Accepted | Bounded all-account discovery comparison preserving unknown, conflicting, duplicate and unmatched account evidence |
| [0023](0023-in-memory-all-account-discovery-attempt.md) | Accepted | One local all-account discovery attempt with one-time evidence handoff, timeout, cancellation and terminal reference cleanup |
| [0024](0024-unsigned-read-request-encoding.md) | Accepted | Typed unsigned HKSPA-1 and HKSAL-6/7/8 encoding with strict text preservation, escaped bytes and exact frame lengths |
| [0025](0025-unsigned-dialogue-initialization.md) | Accepted | Restricted HKIDN-2/HKVVB-3 initialization schemas and unsigned encoding with explicit anonymous identity and caller-supplied product fields |
| [0026](0026-initialization-response-scope.md) | Accepted | Pure initialization response binding with exact bank/user/customer scope, explicit missing parameters and untrusted version observations |
| [0027](0027-in-memory-initialization-attempt.md) | Accepted | One local initialization attempt with pinned user context, one-time evidence handoff, timeout, cancellation and terminal cleanup |
| [0028](0028-synchronization-schemas.md) | Accepted | HKSYN-3/HISYN-4 schemas and unsigned synchronization encoding with exact counters and preserved unresolved response shapes |
| [0029](0029-synchronization-response-context.md) | Accepted | Pure synchronization mode/profile and response comparison with explicit prior-dialogue context and required close/reinitialize disposition |
| [0030](0030-dialogue-end-schemas-and-encoding.md) | Accepted | HKEND-1 schemas, unsigned encoding and pure request-bound closure/abort reply evidence |
| [0031](0031-in-memory-synchronization-and-closing.md) | Accepted | One local synchronization-and-closing attempt with explicit close recording, one-time stage evidence, a shared deadline and terminal cleanup |
| [0032](0032-pin-tan-signature-header-schemas.md) | Accepted | Restricted credential-free HNSHK-4 PIN/TAN header schemas with exact identity/reference observations and profile/code rules |
| [0033](0033-pin-tan-signature-request-context.md) | Accepted | Pure initialization/synchronization signature-header comparison with explicit identity, system and sourced HITANS/3920 context |
| [0034](0034-pin-tan-signature-header-encoding.md) | Accepted | Typed credential-free HNSHK-4 encoding with exact text/numbers, canonical omissions and precision validation |
| [0035](0035-session-credential-buffer-ownership.md) | Accepted | Bounded local PIN/TAN byte ownership with input clearing, one-time TAN copy-out, access deadlines and owned-buffer zeroization |
| [0036](0036-pin-tan-signature-trailer-encoding.md) | Accepted | Local HNSHA-2 encoding with explicit signature context, TAN placement restrictions, bounded secret output and failure cleanup |
| [0037](0037-pin-tan-request-assembly.md) | Accepted | Bounded plain PIN/TAN initialization/synchronization assembly with exact framing, renumbering and whole-message secret cleanup |
| [0038](0038-pin-tan-request-envelope-assembly.md) | Accepted | Plaintext PIN/TAN request envelopes with bound metadata, exact binary payloads and cleanup across all output boundaries |
| [0039](0039-pin-tan-assembled-request-reference-binding.md) | Accepted | Credential-free candidate segment maps and source-preserving response-reference comparison for assembled PIN/TAN requests |
| [0040](0040-pin-tan-initialization-response-semantics.md) | Accepted | Assembled initialization status and parameter/envelope identity comparison with exact response provenance and untrusted version observations |
| [0041](0041-pin-tan-synchronization-response-semantics.md) | Accepted | Assembled synchronization report/status/envelope checks with exact provenance, recovery bounds and mandatory close/reinitialize next step |
