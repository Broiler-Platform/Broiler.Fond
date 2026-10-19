# ADR 0101: Versioned Connector Compatibility Profiles, Bank Workaround Overrides, and Incompatibility Ledger

## Status
Accepted

## Context
In retail homebanking, while FinTS 3.0 specifies a standardized protocol envelope and segment semantics, individual financial institutions, banking servers, and core IT providers (e.g. Finanz Informatik for Sparkassen, Atruvia for Volks- and Raiffeisenbanken, proprietary core stacks for Deutsche Bank, Commerzbank, and ING) exhibit known quirks, segment version implementation bugs, strict pagination limits, and date range constraints.

The product principles govern how these quirks must be handled:
1. **Zero remote control channel:** Connector compatibility profiles and workarounds are built directly into signed application releases. No remote server or cloud service can push dynamic scripts, toggle bank capabilities, disable institutions, or telemetry-track users.
2. **Strict canonical domain isolation:** Institution-specific workarounds must never leak into the core domain models (`Account`, `Transaction`, `Money`, `BalanceSnapshot`). Workarounds remain encapsulated within connector translation layers.
3. **Local user sovereignty & workaround override:** A user must be able to inspect, enable, or disable any individual workaround or force standard protocol behavior locally without needing remote intervention or permission.
4. **Evidence-backed pass-only support & transparent ledger:** A bank enters the verified support matrix only when passing rigorous fixtures and controlled live testing. Failed, incomplete, or unsafe institution combinations must not simply vanish; they must be durably recorded in an immutable known-incompatibility ledger.

## Decisions

### 1. In-Binary Versioned Compatibility Profiles
We introduce `ConnectorCompatibilityProfile` and `IConnectorCompatibilityRegistry` in `Broiler.Fond.Kernel.Domain.Connections.Compatibility`:
- **Immutable Profile Definition:** Each profile is a typed, immutable record identifying:
  - `ProfileId`: Unique identifier (e.g. `fints-standard`, `fints-sparkasse`, `fints-atruvia`, `fints-ing`, `fints-commerzbank`, `fints-deutschebank`).
  - `Version`: Integer specification version (e.g. `1`).
  - `Description`: Human-readable summary of the institution group and known adjustments.
  - `TargetCriteria`: Predicate matching bank code (BLZ) prefixes, exact BICs, or endpoint hostnames.
- **Typed Workaround Controls:**
  - `PinnedHktanVersion`: Optional integer pinning HKTAN segment versions when a bank advertises an incomplete or buggy newer version (e.g. advertising HKTAN 7 while rejecting process variants).
  - `PinnedHksalVersion`: Optional integer pinning HKSAL balance segment versions (e.g. HKSAL 6 vs 7).
  - `MaximumBatchPageSize`: Bounded page size for queries (e.g. clamping to 30 items for institutions that abort larger responses).
  - `MaximumStatementQueryDays`: Bounded statement date range (e.g. clamping to 90 days for institutions rejecting larger windows).
  - `OmitCustomerIdInSignatureHeader`: Flag indicating whether customer ID should be omitted from HNSHK security party identification.
  - `RequireIso8859Encoding`: Enforces ISO-8859-15 encoding rather than UTF-8 when negotiating syntax.
  - `DisablePageOverlapSync`: Disables transaction page overlap checking for institutions with non-monotonic statement cursors.

### 2. Local Workaround Overrides
We introduce `CompatibilityWorkaroundOverride`:
- Allows users to locally configure:
  - `ForceStandardProfile`: Ignores institution-specific profiles and uses the unadjusted FinTS 3.0 standard.
  - `DisabledWorkaroundKeys`: A set of specific workaround flags to deactivate.
- Overrides are stored in local connection configuration and evaluated deterministically during dialogue creation.

### 3. Built-In Registry and Zero Remote Fetch
The registry is initialized statically in `Broiler.Fond.Kernel` with built-in profiles compiled directly into the binary:
- No network requests are made to fetch compatibility profiles.
- Any new institution profile or bugfix is delivered via standard signed software releases.

### 4. Known-Incompatibility Ledger
We establish `docs/compatibility/incompatibility-ledger.md`:
- Flat, public markdown ledger recording every evaluated institution combination that failed, exhibited safety issues, or remains permanently unsupported.
- Columns: Institution / Endpoint, BLZ/BIC, Connector / Version, Observed Symptom, Technical Root Cause, Affected Builds, Workaround / Disposition, Status.

## Consequences

### Positive
- Ensures predictable, reliable FinTS communication across divergent German banking core platforms.
- Completely preserves the zero-telemetry and zero-remote-control invariant.
- Empowers the user to test and disable workarounds locally if a bank resolves a server bug.
- Transparently documents banking incompatibilities with forensic evidence.

### Negative / Trade-offs
- Adding or adjusting institution profiles requires shipping a software update rather than pushing dynamic server-side rules. This is an intentional security design choice.
