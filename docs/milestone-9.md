# Milestone 9 — FinTS 4.1 Protocol Expansion

- **Brand:** Broiler
- **Product:** Fond - Finance on Demand
- **Technical name:** `Broiler.Fond`

**Status:** Complete (Milestones 0 through 9 complete).

**Last updated:** 2026-10-19

The [roadmap](roadmap.md#m9--fints-41-protocol-expansion) defines the product specification.
This backlog breaks Milestone 9 into reviewable increments; completion of one increment does not
imply authorization for production distribution.

---

## 1. Outcome

Broiler Fond expands its protocol capability by introducing evidence-backed FinTS 4.1 protocol execution
alongside established FinTS 3.0 PIN/TAN operations. The shared platform-neutral kernel gains side-by-side
protocol version negotiation, an XML-based message framing and syntax codec, enhanced PIN/TAN security procedures,
native ISO 20022 CAMT and PAIN integration within FinTS 4.1 envelopes, and a multi-version conformance test
harness with comprehensive incompatibility ledger tracking, without weakening FinTS 3.0 guarantees, altering
storage contracts, or introducing third-party dependencies.

---

## 2. Milestone Principles and Invariants

- **Side-by-Side Coexistence:** FinTS 3.0 remains the primary, immutable, fully certified baseline. FinTS 4.1 is
  an opt-in protocol expansion negotiated strictly when advertised by bank parameter data (BPD) or explicit
  compatibility configuration. FinTS 3.0 is never silently upgraded or downgraded.
- **Fail-Closed Version Negotiation:** Protocol negotiation strictly enforces known versions. Unsupported,
  ambiguous, or corrupted version advertisements fail closed without speculative execution or unauthenticated fallback.
- **Strict BCL-only XML & Wire Processing:** All FinTS 4.1 XML parsing and generation use pure .NET 10+ runtime APIs
  (`System.Xml`), enforce `DtdProcessing.Prohibit`, set `XmlResolver` to `null`, and enforce bounded depth, string
  length, and payload size ceilings.
- **Unified Canonical Domain Invariants:** Regardless of whether messages arrive via FinTS 3.0 or FinTS 4.1, all
  account entities, balances, observations, and payment attempts map into the same canonical domain models and
  immutable `LocalIdentityLedger` lineage without loss of provenance.
- **Zero Telemetry and Credential Privacy:** PINs, TANs, and challenge data in FinTS 4.1 workflows are pinned in
  transient memory (`FinTsSessionCredential`), never logged, dumped, or persisted, and zeroized immediately upon completion.

---

## 3. Work Breakdown Structure (M9-01 through M9-05)

### M9-01: FinTS 4.1 Protocol Architecture & Dialogue Version Negotiation

- [x] Implement protocol version enumeration and negotiation framework ([ADR 0135](adr/0135-fints-4-1-protocol-architecture-and-version-negotiation.md)):
  - Canonical `FinTsProtocolVersion` (`FinTs30 = 300`, `FinTs41 = 410`).
  - Negotiation engine: negotiate dialogue protocol version based on BPD capabilities, connection settings, and compatibility profiles.
  - Fail-closed fallback and quarantine: explicit error classification on negotiation mismatch; no silent downgrades.
- [x] Add automated kernel tests for protocol version negotiation (`FinTsProtocolNegotiationTests`).

### M9-02: FinTS 4.1 XML Message Envelope & Syntax Codec

- [x] Implement XML-based wire syntax and framing codec ([ADR 0136](adr/0136-fints-4-1-xml-envelope-and-syntax-codec.md)):
  - FinTS 4.1 XML message envelope (`HNHBK`, `HNHBS`, `HIRMG`, `HIRMS` XML structures).
  - Safe streaming XML parser with strict DTD prohibition, schema depth limits, and node count caps.
  - Roundtrip serialization and deserialization of dialogue initialization and closing envelopes.
- [x] Add automated kernel tests for FinTS 4.1 XML syntax codec (`FinTs41MessageCodecTests`).

### M9-03: FinTS 4.1 Security Envelope & PIN/TAN Continuation

- [x] Implement FinTS 4.1 PIN/TAN security procedures and challenge continuation ([ADR 0137](adr/0137-fints-4-1-security-profile-and-pin-tan-continuation.md)):
  - XML-based signature header (`HNSHK`) and trailer (`HNSHA`) codecs with strong dialogue binding.
  - SCA challenge continuation (`HKTAN` version 7 in XML envelopes) integrating photo/decoupled/flicker flows.
  - Pinned credential zeroization across all XML processing boundaries.
- [x] Add automated kernel tests for FinTS 4.1 security envelope and SCA continuation (`FinTs41SecurityCodecTests`).

### M9-04: FinTS 4.1 Account Operations & Native ISO 20022 Integration

- [x] Implement FinTS 4.1 account discovery, balances, and embedded CAMT/PAIN transactions ([ADR 0138](adr/0138-fints-4-1-account-operations-and-iso-20022-integration.md)):
  - Account discovery (`HKSPA` v3) and balance queries (`HKSAL` v8 / `HKCAZ`).
  - Native embedding and extraction of ISO 20022 `camt.052`/`camt.053` statements within FinTS 4.1 XML segments.
  - SEPA credit transfer initiation (`HKCCS` / `HKIPB` in FinTS 4.1) with Verification of Payee (VoP).
- [x] Add automated kernel tests for FinTS 4.1 account operations and ISO 20022 processing (`FinTs41AccountOperationsTests`).

### M9-05: Multi-Version Conformance Harness & Incompatibility Ledger Validation

- [x] Implement multi-version protocol coordinator and conformance harness ([ADR 0139](adr/0139-multi-version-conformance-harness-and-incompatibility-ledger.md)):
  - Multi-version dialogue coordinator (`FinTsMultiVersionCoordinator`) running FinTS 3.0 and FinTS 4.1 side by side.
  - End-to-end conformance test harness executing dual-protocol synthetic traces.
  - Updated incompatibility ledger (`docs/compatibility/incompatibility-ledger.md`) logging FinTS 4.1 test matrices.
- [x] Execute full CI suite and verify all FinTS 3.0 and FinTS 4.1 verification checks.

---

## 4. Exit Criteria

- FinTS 3.0 and FinTS 4.1 operate side by side with zero regression to established FinTS 3.0 test suites.
- Protocol negotiation cleanly selects the correct protocol version based on BPD advertisements and fails closed on unknown versions.
- FinTS 4.1 XML codec enforces bounded parsing, DTD prohibition, and memory safety without external dependencies.
- Embedded ISO 20022 CAMT and PAIN transactions parse and project accurately into canonical domain entities.
- Full multi-platform CI passes warning-free.
