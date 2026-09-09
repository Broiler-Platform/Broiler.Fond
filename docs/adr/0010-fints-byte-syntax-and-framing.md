# ADR 0010: Bounded FinTS 3.0 byte syntax and outer framing

- **Status:** Accepted (syntax/framing only; connector and security pending)
- **Date:** 2026-09-05
- **Related:** [Kernel boundary](0001-kernel-boundary.md),
  [synthetic workflows](0009-synthetic-workflows-and-diagnostics.md)

## Protocol reference

Reviewed the Deutsche Kreditwirtschaft **FinTS 3.0 Formals**, release
2017-10-06, downloaded through the official
[specification page](https://www.fints.org/de/spezifikation) on 2026-09-05.
The site's PDF download links expire, so the stable landing page is recorded.
The downloaded PDF SHA-256 was
`6b4809acd43acd2c6166c486964dee4b84b488a6a6902b29d1221458eaed7239`.
The document is not redistributed in this repository.

The implementation draws on B.1/B.4 (bytes and data formats), B.5.2–B.5.3
(outer message), B.8 (reserved wrapper numbering), F (header fields), and
H.1.1–H.1.5 (syntax). FinTS separates fields with `+`, components with `:`,
and segments with `'`. `?` escapes syntax characters. Binary `@length@`
prefixes count payload bytes, whose delimiters have no syntactic meaning.
Positional and trailing empty elements must be retained. The outer header
contains a fixed 12-digit byte size and protocol version 300; the trailer
repeats the message number. Wrapper segments use reserved numbers 998/999.

## Implementation contract

`FinTsSyntax.ParseSegments` accepts a complete bounded byte sequence and
returns an immutable tree of segments, positional fields and scalar elements.
It validates segment headers but has no business-segment schema registry.
Unknown uppercase alphanumeric codes and numeric versions remain available
as untrusted input; they do not advertise supported banking capabilities.
It preserves standalone segment numbers without demanding a whole message.

The tree owns one copied wire buffer. Scalars refer privately to slices of
that buffer. `CopyValueBytes` explicitly returns unescaped text bytes or raw
binary bytes; an empty binary value remains distinct from omitted text.
`CopyWireBytes` returns the exact original encoding, including empty positions
and leading-zero binary lengths. Caller mutations cannot change stored evidence.
Default object formatting contains type names rather than raw content.
There is no implicit public text decoding or choice of negotiated charset.

`FinTsWireEncoding` encodes individual text/binary elements from caller-selected
bytes. It does not trim or convert values, construct authenticated messages,
or write to a transport. Body field datatypes and character repertoires require
later schemas; even control bytes outside binary data are preserved by this
lexical layer. Consumers must not render or execute them without validation.

`FinTsMessageFrame.Parse` additionally checks the outer HNHBK/HNHBS versions,
unique header/trailer placement, declared byte length, version 300, bounded
dialog IDs and positive matching message numbers. If present, the message
reference must have the expected two scalar components. An empty trailing
optional reference is accepted. Dialog IDs reject control bytes, but their
negotiated repertoire and correspondence to an active dialogue are not proved.

Plain messages require consecutive segment numbers and no security-wrapper
segments. The separate four-segment outer wrapper shape accepts HNVSK:998,
HNVSD:999 and the preserved inner-message trailer number. This is only a
numbering exception: wrapper versions, body schemas, ciphertext and signatures
are not validated, decoded or expanded. Parse success does not authenticate
a response, authorize an action or make the message safe for domain ingestion.

## Local resource and parsing policies

| Limit | Value |
| --- | --- |
| Input and encoded element output | 1,048,576 bytes |
| Segments per parse | 999 |
| Fields per segment, including header DEG | 1,024 |
| Components per field | 256 |
| Total scalar elements, including headers | 32,768 |
| Binary length-prefix digits | 9 |

These are Fond resource limits, not claims of universal bank limits. Counts
are checked before growing collections. Binary lengths are bounded before
advancing through the input; binary contents are never recursively parsed.
The parser checks cancellation before copying input and throughout traversal.
It publishes no partial tree when parsing fails.

This first profile accepts segment codes of 1–6 ASCII uppercase letters/digits,
rejects unnecessary escapes of nonsyntax bytes, and rejects text concatenated
with a binary element. These deliberate restrictions must be revisited with
recorded compatibility evidence if a tested institution requires otherwise.
No automatic normalization or recovery silently changes malformed input.

Failures are `FinTsFormatException` with predefined error categories and fixed
messages; input text, wire bytes and identifiers never appear in errors.
The owned tree is not a secure credential container: it retains its bytes until
collected, and explicitly copied values cannot be revoked. No real PIN/TAN path
is enabled. A later connector needs reviewed secret-buffer lifetimes and must
never log parsed content or feed it to arbitrary serializers for diagnostics.

## Evidence and next work

Six public synthetic fixtures are generated with an independent standard-library
Python producer and verified by the BCL test runner. The 1,350 checks include
every truncation of complete frames, delimiter-bearing binary data, exact
expected tree values, ownership/mutation isolation, omitted positions,
malformed framing, deterministic generated byte cases and exact/over-limit
resource boundaries. See the [fixture inventory](../../tests/Broiler.Fond.Kernel.Tests/Fixtures/FinTs/README.md).

This is syntax evidence, not full FinTS conformance. Next are typed
administrative/response schemas and dialogue correlation, followed by BPD/UPD,
security-profile processing and account/balance ingestion. TLS transport,
registered live dialogue, actual SCA qualification, persistence and graphical
UI remain pending. The Windows development shell remains inert.
