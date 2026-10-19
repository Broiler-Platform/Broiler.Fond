# ADR 0139: Multi-Version Conformance Harness and Incompatibility Ledger

- **Status:** Accepted
- **Date:** 2026-10-19
- **Related:** [ADR 0101](0101-connector-compatibility-profiles-and-incompatibility-ledger.md), [ADR 0124](0124-fints-3-freeze-and-conformance-hardening.md), [ADR 0135](0135-fints-4-1-protocol-architecture-and-version-negotiation.md), [ADR 0136](0136-fints-4-1-xml-envelope-and-syntax-codec.md), [ADR 0137](0137-fints-4-1-security-profile-and-pin-tan-continuation.md), [ADR 0138](0138-fints-4-1-account-operations-and-iso-20022-integration.md), [docs/roadmap.md](../roadmap.md#m9--fints-41-protocol-expansion), [docs/milestone-9.md](../milestone-9.md#m9-05-multi-version-conformance-harness--incompatibility-ledger-validation)

---

## 1. Context and Problem Statement

With the implementation of FinTS 4.1 protocol negotiation, XML message envelopes, PIN/TAN security procedures, and ISO 20022 business transactions, Broiler Fond requires an integrated multi-version orchestration engine and conformance harness to prove that:
1. **Zero Regression for FinTS 3.0:** The established FinTS 3.0 PIN/TAN engine continues to execute with zero behavioral change or performance regression.
2. **Concurrent Multi-Version Operation:** A user holding accounts at multiple institutions can concurrently sync one connection via FinTS 3.0 and another connection via FinTS 4.1 on the same profile without state collision or cross-contamination.
3. **Truthful Incompatibility Tracking:** Institutions advertising broken, non-standard, or incomplete FinTS 4.1 implementations must be recorded in the `incompatibility-ledger.md` with explicit diagnostic evidence and fail-closed error boundaries.
4. **BCL-Only and Zero-Telemetry Compliance:** All multi-version coordination, conformance verification, and failure injection execute strictly within the .NET 10 shared framework boundary without external dependencies or remote telemetry.

---

## 2. Decision and Architecture

### 2.1 Multi-Version Protocol Coordinator (`FinTsMultiVersionCoordinator`)

We implement `FinTsMultiVersionCoordinator` in `Broiler.Fond.Kernel.FinTs`:
- Evaluates institution BPD and compatibility profiles via `FinTsProtocolNegotiator`.
- Dispatches connection sessions to the appropriate protocol engine:
  - For `FinTsProtocolVersion.FinTs30`: uses `FinTsPinTanReadSessionAttempt`, `FinTsMessageParser`, and HBCI byte syntax writers.
  - For `FinTsProtocolVersion.FinTs41`: uses `FinTs41MessageCodec`, `FinTs41SecurityHeader`/`Trailer`, and XML syntax builders.
- Enforces session-level protocol immutability: once a session begins, it cannot mutate its protocol version.
- Aggregates canonical results (`AccountSourceLocator`, `BalanceSnapshot`, `SourceObservation`, `PaymentAttempt`) into the unified `LocalIdentityLedger`.

### 2.2 Dual-Protocol Conformance Harness (`FinTsMultiVersionConformanceTests`)

The conformance harness verifies:
1. **Side-by-Side Isolation:** Executes simultaneous simulated dialogues for FinTS 3.0 and FinTS 4.1, confirming that message numbers, dialogue IDs, and security references remain strictly disjoint and isolated.
2. **Preference Fallback Boundaries:** Asserts that when `PreferFinTs41` is requested on an endpoint supporting only FinTS 3.0, the coordinator falls back cleanly to FinTS 3.0 with an auditable diagnostic event.
3. **Forced Mode Enforcement:** Asserts that `ForceFinTs41` fails closed when an institution only supports FinTS 3.0, and `ForceFinTs30` fails closed when an institution only supports FinTS 4.1.
4. **Failure Injection Resiliency:** Injects malformed XML, truncated envelopes, and mismatched security references, asserting that attempts fail closed without leaking credentials or leaving dangling state.

### 2.3 Known Incompatibility Ledger Updates

`docs/compatibility/incompatibility-ledger.md` is updated with formal entries for:
- Non-standard XML namespaces or unauthorized FinTS 4.0 drafts.
- Endpoints advertising FinTS 4.1 but omitting mandatory ISO 20022 CAMT schemas.
- Deprecated HBCI 2.x endpoints definitively quarantined.

---

## 3. Consequences and Verification

### Positive
- Unified, transparent multibank access across both FinTS 3.0 and FinTS 4.1.
- Defends the core product principle: truthful, evidence-backed compatibility.
- Comprehensive regression testing ensures complete stability of existing features.

### Negative
- Requires maintaining dual protocol verification suites in repository tests.

### Verification Plan
- Execute `FinTsMultiVersionConformanceTests` covering side-by-side execution, preference negotiation, fallback boundaries, and failure injection.
- Execute full CI pipeline (`eng/Invoke-CI.ps1 -Scope Full`) to verify all kernel tests, packaging specs, and host smoke tests.
