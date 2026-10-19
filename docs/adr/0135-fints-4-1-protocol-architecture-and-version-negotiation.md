# ADR 0135: FinTS 4.1 Protocol Architecture and Version Negotiation

- **Status:** Accepted
- **Date:** 2026-10-19
- **Related:** [ADR 0010](0010-fints-byte-syntax-and-framing.md), [ADR 0011](0011-fints-response-schemas-and-dialogue.md), [ADR 0014](0014-fints-pintan-envelope-schemas.md), [ADR 0083](0083-fints-transport-and-capability-activation.md), [ADR 0101](0101-connector-compatibility-profiles-and-incompatibility-ledger.md), [ADR 0124](0124-fints-3-freeze-and-conformance-hardening.md), [docs/roadmap.md](../roadmap.md#m9--fints-41-protocol-expansion), [docs/milestone-9.md](../milestone-9.md#m9-01-fints-41-protocol-architecture--dialogue-version-negotiation)

---

## 1. Context and Problem Statement

Following the completion of the Windows 1.0 baseline (Milestone 7) and cross-platform host qualification (Milestone 8), Broiler Fond initiates Milestone 9 to support evidence-backed FinTS 4.1 protocol expansion.

While FinTS 3.0 remains the ubiquitous German retail homebanking protocol, the German Banking Industry Committee (Deutsche Kreditwirtschaft) defines FinTS 4.1 as the successor specification introducing:
1. XML-based message syntax and ISO 20022 schemas replacing legacy HBCI delimited byte framing (`+`, `:`, `'`).
2. Modernized dialogue initialization (`HKIDN` v3/4, `HKVVB` v4), bank parameter data schemas, and PIN/TAN security procedures.
3. Native embedding of CAMT statement and PAIN payment formats inside structured XML envelopes.

However, introducing a new protocol version carries architectural hazards:
- **Silent protocol downgrades and upgrades:** If an endpoint intermittently responds or advertises mixed capabilities, guessing or silently toggling between FinTS 3.0 and FinTS 4.1 can cause authentication failures, replay vulnerabilities, or silent transaction corruption.
- **Protocol conflation:** Codecs must not intermix FinTS 3.0 escaped byte parsing with FinTS 4.1 XML parsing.
- **Strict BCL-only constraint:** The protocol negotiation engine and FinTS 4.1 wire codecs must rely exclusively on .NET 10 shared framework APIs (`System.Xml`), without any third-party XML libraries or unmanaged dependencies.

---

## 2. Decision and Architecture

### 2.1 Protocol Version Enumeration (`FinTsProtocolVersion`)

We define canonical protocol versions in `Broiler.Fond.Kernel.FinTs`:

```csharp
public enum FinTsProtocolVersion
{
    FinTs30 = 300,
    FinTs41 = 410
}
```

Each protocol version binds to an immutable `FinTsProtocolDescriptor`:
- `ProtocolIdentifier`: `"FinTS-3.0"` or `"FinTS-4.1"`.
- `MajorVersion` / `MinorVersion`: (3, 0) and (4, 1).
- `IsXmlSyntax`: `false` for FinTS 3.0 (classic delimited byte syntax); `true` for FinTS 4.1 (XML syntax).
- `SupportedSecurityProfiles`: PIN/TAN procedures certified for that protocol version.

### 2.2 Protocol Version Negotiation Engine (`FinTsProtocolNegotiator`)

Protocol negotiation evaluates three distinct evidence inputs:
1. **Bank Parameter Data (BPD):** Advertised protocol versions in `HIBPA` (e.g. protocol version 3.0 or 4.1).
2. **Connector Compatibility Profile:** Explicit verified compatibility row configuration (`ConnectorCompatibilityProfile`) or known incompatibility overrides.
3. **Connection Preference:** User-configured or default connection preference (e.g. `Auto`, `PreferFinTs41`, `ForceFinTs30`).

```csharp
public interface IFinTsProtocolNegotiator
{
    FinTsNegotiationResult Negotiate(
        FinTsProtocolAdvertisement advertisement,
        FinTsProtocolPreference preference);
}
```

### 2.3 Negotiation Invariants and Fail-Closed Rules

1. **No Silent Downgrade After Dialogue Start:** Once a dialogue begins under a negotiated protocol version (e.g. FinTS 4.1 dialogue initialization sent), the dialogue session is strictly bound to that version. Any mismatch in bank reply fails closed with `FinTsProtocolMismatchException`.
2. **Fail-Closed on Unknown Versions:** If an institution advertises legacy HBCI 2.x, unapproved drafts (e.g. 4.0), or unrecognizable protocol codes, negotiation fails with `UnsupportedProtocolVersionException`.
3. **Explicit Fallback Boundary:** If `Auto` preference is selected and an institution's BPD lacks FinTS 4.1 support, the negotiator resolves to `FinTs30` with an explicit diagnostic log. It never attempts FinTS 4.1 blindly if unadvertised.
4. **Side-by-Side Coexistence:** The existing FinTS 3.0 engine (`FinTs30CapabilityLock`, `FinTsDialogueCoordinator`, `FinTsMessageParser`) remains untouched and fully operational. FinTS 4.1 operates via dedicated, isolated contracts.

---

## 3. Consequences and Verification

### Positive
- Strict version segregation prevents cross-protocol pollution and guarantees zero regression in FinTS 3.0 tests.
- Clear negotiation diagnostics enable precise debugging when banks advertise differing version capabilities.
- Enforces the core product principle of truthful, capability-driven connectivity.

### Negative
- Multi-version support requires distinct wire encoders and decoders in the kernel.

### Verification Plan
- `FinTsProtocolNegotiationTests`:
  1. Validates negotiation resolution across BPD advertisements (`FinTs30` only, `FinTs41` only, dual advertisement).
  2. Asserts correct handling of `Auto`, `PreferFinTs41`, and `ForceFinTs30` preferences.
  3. Verifies fail-closed behavior for unknown/legacy protocol versions (e.g. HBCI 2.2, FinTS 4.0).
  4. Verifies immutability of session protocol bindings and rejection of mid-dialogue version mutation.
