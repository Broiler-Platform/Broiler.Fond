# ADR 0137: FinTS 4.1 Security Profile and PIN/TAN Continuation

- **Status:** Accepted
- **Date:** 2026-10-19
- **Related:** [ADR 0032](0032-pin-tan-signature-header-schemas.md), [ADR 0033](0033-pin-tan-security-profile-validation.md), [ADR 0034](0034-fints-session-credential-pinned-memory.md), [ADR 0102](0102-decoupled-sca-and-visual-challenge-continuation.md), [ADR 0135](0135-fints-4-1-protocol-architecture-and-version-negotiation.md), [ADR 0136](0136-fints-4-1-xml-envelope-and-syntax-codec.md), [docs/roadmap.md](../roadmap.md#m9--fints-41-protocol-expansion), [docs/milestone-9.md](../milestone-9.md#m9-03-fints-4-1-security-envelope--pin-tan-continuation)

---

## 1. Context and Problem Statement

In FinTS 4.1, security headers and trailers undergo structural XML translation while retaining strict cryptographic context binding and PSD2 Strong Customer Authentication (SCA) compliance. 

PIN/TAN authentication in FinTS 4.1 envelopes requires:
1. XML-based signature headers (`HNSHK` v4) and trailers (`HNSHA` v2) encapsulating user identification, system IDs, procedure codes (e.g. `PIN:1`, `PIN:2`), and ephemeral security timestamps.
2. Two-step SCA challenge continuation (`HKTAN` v7 in XML) supporting pushTAN, photoTAN/matrix code, decoupled app authorization, and chipTAN flicker codes within XML elements (`<challenge>`, `<challengeMatrix>`, `<challengeHhduc>`).
3. Strict credential lifecycle and zeroization: As in FinTS 3.0, PINs and TANs must never be persisted, logged, or retained in persistent XML documents or managed strings. They must be held in pinned memory buffers (`FinTsSessionCredential`) and zeroized immediately across all execution boundaries.

---

## 2. Decision and Architecture

### 2.1 FinTS 4.1 Security Envelope Structures

We define dedicated security envelope models in `Broiler.Fond.Kernel.FinTs`:
- `FinTs41SecurityHeader`:
  - `SecurityProfile`: E.g., `PIN:1` (Transport layer TLS + PIN) or `PIN:2`.
  - `SecurityFunction`: Standard 3-digit TAN procedure code (e.g., `999` for PIN-only sync, `920` for pushTAN, `921` for photoTAN).
  - `SecurityReference`: Dialogue ID and message ordinal.
  - `SecurityTimestamp`: Monotonic UTC creation timestamp.
- `FinTs41SecurityTrailer`:
  - `SecurityReference`: Must correlate strictly with the matching `HNSHK`.
  - Encapsulates transient credential delivery (`PIN` and optional `TAN`).
  - Implements `IDisposable` with zeroization of any internal transient character or byte arrays.

### 2.2 Two-Step SCA Challenge Continuation (`FinTs41ScaContinuation`)

FinTS 4.1 challenge responses from banks encapsulate SCA data within `<HITAN version="7">`:
- `<processVariant>`: `1` (initial challenge presentation), `2` (challenge continuation / TAN submission).
- `<challenge>`: Plain-text user instructions (e.g., "Bitte TAN aus pushTAN-App eingeben").
- `<challengeMatrix>`: Base64-encoded image or matrix code for photoTAN.
- `<challengeHhduc>`: Hex-encoded optical flicker sequence for chipTAN comfort.
- `<tanMediumName>`: Mobile device or card reader alias.

The `FinTs41ScaChallengeParser` decodes these elements into canonical `FinTsTanChallenge` representations, ensuring complete interoperability with existing UI rendering components (`TanChallengeDialog`).

### 2.3 Transient Memory Hygiene and Secret Zeroization

1. **Pinned Buffers:** Raw PIN and TAN characters are retrieved from `FinTsSessionCredential` only for the brief duration of XML serialization.
2. **Immediate Scrubbing:** Once the XML envelope bytes are written or encrypted for HTTPS transport, all intermediate byte arrays and XML buffers holding plain credential text are scrubbed using `Array.Clear` and `CryptographicOperations.ZeroMemory`.
3. **No Leakage in Diagnostics:** `ToString()` representations and diagnostic previews of `HNSHK` and `HNSHA` mask credentials with `***` and never display raw PIN/TAN data.

---

## 3. Consequences and Verification

### Positive
- Robust XML-based PIN/TAN and SCA implementation for FinTS 4.1.
- Complete parity with FinTS 3.0 two-step challenge continuation (pushTAN, photoTAN, decoupled, flicker).
- Adheres strictly to the core zero-telemetry and credential privacy principles.

### Negative
- Requires careful stream and buffer management to ensure managed strings do not retain plain credentials longer than necessary.

### Verification Plan
- `FinTs41SecurityCodecTests`:
  1. Encoding and decoding of `HNSHK` v4 and `HNSHA` v2 XML segments.
  2. Parsing of `<HITAN version="7">` challenge envelopes across text, matrix/photoTAN, and flicker types.
  3. Assertion of credential zeroization and sanitization in `ToString()` / diagnostics.
  4. Validation of signature reference matching between `HNSHK` and `HNSHA`.
