# ADR 0136: FinTS 4.1 XML Message Envelope and Syntax Codec

- **Status:** Accepted
- **Date:** 2026-10-19
- **Related:** [ADR 0010](0010-fints-byte-syntax-and-framing.md), [ADR 0011](0011-fints-response-schemas-and-dialogue.md), [ADR 0135](0135-fints-4-1-protocol-architecture-and-version-negotiation.md), [docs/roadmap.md](../roadmap.md#m9--fints-41-protocol-expansion), [docs/milestone-9.md](../milestone-9.md#m9-02-fints-41-xml-message-envelope--syntax-codec)

---

## 1. Context and Problem Statement

FinTS 4.1 transitions message transport framing from the legacy HBCI delimiter syntax (`+`, `:`, `'`, `?` escape) to structured XML envelopes. Every FinTS 4.1 exchange is encapsulated in an XML message containing a message header (`HNHBK` v4), order and response segments (e.g., `HKIDN` v4, `HKVVB` v4, `HIRMG` v2, `HIRMS` v2), and a message trailer (`HNHBS` v4).

Processing XML messages from external bank endpoints introduces critical security and stability considerations:
1. **XML External Entity (XXE) and DTD injection:** Hostile or manipulated bank responses could attempt entity expansion, server-side request forgery (SSRF), or filesystem access.
2. **Decompression and quadratic expansion attacks:** Malicious or oversized XML payloads could cause CPU exhaustion or out-of-memory crashes.
3. **Framing integrity:** Message numbers in `HNHBK` and `HNHBS` must match, dialogue IDs must correlate monotonically, and element order must remain strictly schema-conformant.
4. **BCL-only kernel boundary:** All encoding and decoding must be implemented using .NET 10 shared framework APIs (`System.Xml.XmlWriter`, `System.Xml.XmlReader`) with zero external package dependencies.

---

## 2. Decision and Architecture

### 2.1 FinTS 4.1 XML Message Envelope Model

We define the typed object model for FinTS 4.1 messages in `Broiler.Fond.Kernel.FinTs`:
- `FinTs41XmlMessage`: Top-level message envelope containing:
  - `FinTs41Header`: Dialogue ID, message number, sender BLZ/bank code, recipient endpoint identifier, and creation timestamp.
  - `Segments`: List of ordered `FinTs41Segment` instances (segment tag, version, sequence number, child elements/payload).
  - `FinTs41Trailer`: Message number validating envelope completeness.

### 2.2 Secure XML Parsing Contract (`FinTs41MessageCodec`)

The parser implements strict security hardening on `XmlReader`:
- `DtdProcessing = DtdProcessing.Prohibit`: Completely forbids Document Type Definitions.
- `XmlResolver = null`: Completely disables resolution of external schemas or entities.
- `MaxCharactersInDocument = 1_048_576` (1 MiB): Strict payload size ceiling.
- Maximum nesting depth enforced at 16 levels.
- Maximum segment count enforced at 512 segments per envelope.
- Any violation throws `FinTs41MalformedXmlException` and halts processing immediately.

### 2.3 Deterministic Lexical Serialization

Outgoing FinTS 4.1 messages are generated via `XmlWriter` with deterministic settings:
- Strict UTF-8 without Byte Order Mark (BOM).
- Invariant culture formatting for numbers, timestamps (`O` roundtrip format), and amounts.
- Normalized LF line endings.
- Strict namespace qualification (`urn:org:fints:spec:xml:2008`).

### 2.4 Bank Response and Return Code Decoding

The parser recognizes standard FinTS 4.1 return message segments:
- `HIRMG` (Message Return Codes): Global dialogue status codes (e.g., `0010` Dialogue initialized, `9800` Dialogue aborted).
- `HIRMS` (Segment Return Codes): Per-segment status and warning codes (e.g., `0020` Order accepted, `3010` Not available).

---

## 3. Consequences and Verification

### Positive
- Robust, standardized XML framing fully compliant with FinTS 4.1 specifications.
- Complete immunity to XXE and entity expansion attacks via strict BCL reader configuration.
- Clean separation from FinTS 3.0 legacy byte framing.

### Negative
- XML envelopes have slightly higher byte overhead compared to compact HBCI delimited syntax.

### Verification Plan
- `FinTs41MessageCodecTests`:
  1. Roundtrip serialization and deserialization of FinTS 4.1 initialization message (`HKIDN` v4, `HKVVB` v4).
  2. Verification of matching header and trailer message numbers.
  3. Failure injection: XXE injection rejection, DTD prohibition, oversized payload rejection, mismatched trailer detection.
  4. Structured parsing of `HIRMG` and `HIRMS` return codes.
