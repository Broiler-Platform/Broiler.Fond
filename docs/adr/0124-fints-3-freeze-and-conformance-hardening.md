# ADR 0124: FinTS 3.0 Protocol Freeze, Conformance Harness, and Error Hardening

- **Status:** Accepted
- **Date:** 2026-10-19
- **Related:** [ADR 0010](0010-fints-byte-syntax-and-framing.md), [ADR 0011](0011-fints-response-schemas-and-dialogue.md), [ADR 0014](0014-fints-pintan-envelope-schemas.md), [ADR 0101](0101-connector-compatibility-profiles-and-incompatibility-ledger.md), [docs/roadmap.md](../roadmap.md#protocol-and-connector-strategy), [docs/milestone-7.md](../milestone-7.md#m7-01-freeze-and-harden-fints-30-capability-matrix--conformance-verification)

---

## 1. Context and Problem Statement

For the Windows 1.0 release, Broiler Fond must establish a definitive, hardened baseline for German consumer homebanking connectivity. While the German Banking Industry Committee (Deutsche Kreditwirtschaft) publishes FinTS 4.1 specifications, FinTS 3.0 remains the ubiquitous, proven market protocol implemented across more than 2,000 credit institutions. 

Attempting premature FinTS 4.1 features or accepting untested segment variants introduces severe reliability risks:
1. **Protocol drift and unverified capabilities:** Accepting newer or speculative segment versions without complete institution testing violates the core principle of *"Capability-driven behavior and small, evidence-backed compatibility."*
2. **Wild input variation across German banks:** Real-world FinTS endpoints return truncated responses, obsolete or regional code pages (ISO-8859-1, Windows-1252), missing optional segments, undocumented `HIRMG`/`HIRMS` bank codes, and abrupt socket terminations.
3. **Safety boundary for write operations:** An unhandled communication failure or ambiguous response during SEPA money movement or standing order deletion could leave transactions in an undefined state.
4. **BCL-only kernel boundary:** All conformance verification, malformed-input fuzzing, and failure-injection testing must execute strictly using .NET 10 shared framework APIs with zero third-party packages or native dependencies.

---

## 2. Decision and Architecture

### 2.1 Frozen FinTS 3.0 Capability Matrix (`FinTs30CapabilityLock`)

For Windows 1.0, the supported FinTS protocol version and business transactions are formally frozen in `FinTs30CapabilityLock`:

| Segment Tag | Purpose | Frozen Supported Versions | Notes |
| :--- | :--- | :--- | :--- |
| `HNHBK` / `HNHBS` | Message Head / End | Version 3 | Standard FinTS 3.0 envelope framing |
| `HNVSK` / `HNVSD` | Encryption Head / Data | Version 3 (`HNVSK`), Version 1 (`HNVSD`) | PIN/TAN encryption envelope |
| `HNSHK` / `HNSHA` | Signature Head / Trailer | Version 4 (`HNSHK`), Version 2 (`HNSHA`) | PIN/TAN authenticated signing |
| `HKIDN` / `HKVVB` | Identification / Prep | Version 2 (`HKIDN`), Version 3 (`HKVVB`) | Dialogue initialization & product registration |
| `HKTAN` / `HITAN` | Two-Step SCA / TAN | Version 6, Version 7 | Decoupled, pushTAN, chipTAN, smsTAN |
| `HKSAL` / `HISAL` | Account Balances | Version 5, Version 6, Version 7 | Interoperable balance retrieval |
| `HKKAZ` / `HIKAZ` | Account Statements (MT940) | Version 5, Version 6, Version 7 | Swift MT940 structured transaction data |
| `HKCAZ` / `HICAZ` | Account Statements (CAMT) | Version 1 | ISO 20022 camt.052 / camt.053 XML data |
| `HKCCS` / `HICCS` | Single SEPA Credit Transfer | Version 1 | pain.001.001.03 / pain.001.003.03 transfer write |
| `HKIPB` / `HKSIP` | Instant SEPA Credit Transfer | Version 1 | Real-time instant clearing write |
| `HKCDB` / `HKCME` | Scheduled SEPA Credit Transfer| Version 1 | Post-dated single transfer creation/modification |
| `HKCDL` / `HICDL` | Scheduled SEPA Orders List | Version 1 | Query scheduled bank orders |
| `HKDMB` / `HKDME` | Standing Order Management | Version 1 | Create / modify standing orders |
| `HKDLS` / `HIDLS` | Standing Orders List | Version 1 | Query active standing orders |
| `HKLAS` / `HILAS` | Direct-Debit Return Write | Version 4 | Statutory direct debit return reversal write |
| `HKEND` | Dialogue End | Version 1 | Orderly session termination |

Any segment or version outside this matrix is rejected with a typed `UnsupportedSegmentVersionException` or handled through explicit versioned compatibility adapters; it is never silently processed or guessed.

### 2.2 Conformance Test Harness (`FinTs30ConformanceHarness`)

The kernel contains a dedicated conformance test engine (`FinTs30ConformanceHarness`) that evaluates connector pipelines against:
- **Contract compliance:** Guarantees that every request serializer emits exact Formals byte syntax (LF line endings, `+` and `:` separators, `?'` string delimiters, proper escaping for `?+`, `?:`, `??`, `?'`).
- **Response parsing resilience:** Validates that malformed headers, missing mandatory elements, out-of-order segments, and trailing garbage fail safely without crash or partial mutation.
- **Envelope boundary limits:** Enforces strict limits: max message size (1 MiB), max segments per message (2,048), max element nesting depth (16), preventing resource exhaustion attacks.

### 2.3 Failure-Injection and Transient Fault Matrix

The conformance engine verifies system behavior under simulated bank endpoint faults:
1. **Network transport aborts:** Connection dropped before any bytes received, dropped mid-header, dropped mid-payload, or closed cleanly after HTTP 200 with 0 bytes.
2. **Ambiguous write boundary:** Connection dropped after sending a payment or write command where bank execution cannot be verified. Enforces invariant: attempt moves strictly to durable `Unknown`, never auto-retries.
3. **Structured bank rejections:** Standard and edge-case `HIRMG`/`HIRMS` codes:
   - `3060` (Partial execution / warnings) -> Non-fatal, captures warnings truthfully.
   - `3920` (Challenge / TAN required) -> Coordinates SCA prompt.
   - `9050` / `9210` (Authentication failed / PIN blocked) -> Moves connection to `ReauthenticationRequired` or `Disabled`.
   - `9999` (System error at bank) -> Fails closed with descriptive user message.

---

## 3. Consequences

### Positive
- Guarantees 100% reproducible, hardened FinTS 3.0 protocol execution across German institutions for Windows 1.0.
- Completely prevents accidental scope creep or buggy half-implemented FinTS 4.1 features.
- Full test coverage for edge cases, malformed payloads, and hostile inputs without external test dependencies.

### Negative / Trade-offs
- FinTS 4.1 business transactions remain deferred to Milestone 9 (post-1.0).
- Banks using exotic, non-standard segment variations require explicit entries in the versioned compatibility adapter rather than heuristic fallback.
