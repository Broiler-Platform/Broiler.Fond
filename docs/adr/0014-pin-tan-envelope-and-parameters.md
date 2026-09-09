# ADR 0014: PIN/TAN response envelopes and parameter evidence

- **Status:** Accepted (bounded structural parsing only)
- **Date:** 2026-09-06
- **Related:** [Byte framing](0010-fints-byte-syntax-and-framing.md),
  [response correlation](0011-fints-responses-and-dialogue-correlation.md),
  [BPD/UPD](0012-fints-parameter-evidence.md),
  [read capability evidence](0013-read-capability-evidence.md)

## Sources

Reviewed the official [FinTS specification](https://www.fints.org/de/spezifikation):

- **Security – Sicherheitsverfahren PIN/TAN**, 2020-07-10, B.8.1, B.9,
  the PIN/TAN parameter data dictionary and F.2 synthetic-message examples.
  Download SHA-256:
  `77bb5724cb391434cd7188924a1ff89bff040bf340dc49a09c92cdeb4ee7dc3a`.
- **Security – Sicherheitsverfahren HBCI**, 2024-06-11, B.5.3–B.5.4 and
  the key-name, security-identity, timestamp and algorithm data dictionaries.
  Download SHA-256:
  `37a82f7e51386f605154f1d5f47d64dc97ca3cab7161bc8769b9c7ca0f32735e`.

The PIN/TAN-specific rules override generic HBCI fields: profile PIN:1 or
PIN:2, security function 998 (plaintext), empty CID/certificate/IV value,
and filler key metadata. PIN/TAN transport protection belongs to HTTPS.
Parsing these fields performs no cryptography and authenticates no institution,
system ID, timestamp, user, message or operation.

## Response envelope contract

`FinTsPinTanEnvelope.Parse` accepts a previously validated outer frame with
HNVSK version 3 at reserved number 998 and HNVSD version 1 at number 999.
It checks scalar/group cardinalities, required fields, profile versions,
function 998 and compression 0. Other profiles, versions, encrypted functions
and compression modes fail explicitly without fallback or decompression.

The security header validates role 1/4, identity-party code 1/2 and a required
reported system ID of at most 30 bytes. Timestamp identifier 1 permits an
omitted date/time, a calendar date, and an optional valid time only with a date.
No freshness decision or conversion to a trusted clock is made.

Algorithm metadata validates usage 2, mode 2/18/19, algorithm 13/14, a nonempty
binary filler of at most 512 bytes, a numeric key-parameter identifier of at
most three characters and IV identifier 1. These values select no algorithm
implementation. Key names preserve raw country/institution/key identity,
require key type V and canonical three-digit maximum key number/version.
The parser permits omitted optional placeholders or retained empty text
positions; populated CID, certificate and IV fields are rejected, including
empty binary values that would still populate those fields.

HNVSD contains exactly one nonempty binary field. Its plaintext is parsed once
as inner syntax. Inner numbering must start at 2, increase without gaps and
end immediately before the outer trailer number. Nested wrappers, embedded
message headers/trailers, signature segments and HK-prefixed client operations
are outside this response-only slice and fail. Other unknown segments remain
opaque. A wrapper alone need not contain a typed reply; response parsing
separately requires HIRMG and validates the existing reply schemas.

The original outer frame and exact inner byte representation remain owned,
immutable evidence. `FinTsResponse.ParsePinTan` explicitly consumes a parsed
envelope, retaining it and its original frame; it never synthesizes a different
wire message. The plain `Parse` entry point still rejects wrappers. Response
correlation now checks all inner segment references through `BodySegments`,
including uninterpreted segments. Rejected candidates leave the pending request
intact. This remains local mechanical correlation, and outgoing wrapped
requests remain unsupported.

The outer 1 MiB bound also bounds the contained bytes. Parsing takes a second
owned snapshot of the inner body. Existing per-segment/field limits apply,
with at most 995 inner segments and 999 combined segments. The outer and inner
trees share a total limit of 32,768 elements. The syntax parser records actual
element counts, including omitted reference positions, for this check.
There is no recursion, decompression, transport, credential builder or logger.
Raw buffers remain sensitive untrusted evidence until collected; they are not
a credential container and are not passed to domain account ingestion.

## HIPINS contract

`FinTsPinTanParameterSet.Parse` consumes existing parameter evidence and
recognizes HIPINS version 1. It preserves the common maximum-order,
minimum-signature and raw security-class fields. PIN/TAN security class has
no processing meaning and is never interpreted as a TAN procedure.

The first five optional components preserve minimum PIN length, maximum PIN
length, maximum TAN length, user-ID label and customer-ID label. Bounds are
at most two digits, and labels at most 30 decoded bytes. Missing bounds stay
null; explicit zero stays zero. Conflicting minimum/maximum PIN lengths remain
visible rather than being repaired or used for credential acceptance.

Subsequent operation/TAN-required pairs preserve order and duplicates. Operation
codes remain exact, and only J/N flags are accepted. The protocol permits 999
pairs; the existing local 256-component syntax budget limits this implementation
to 125 pairs after the five prefix positions. Larger advertisements fail.
At most 128 HIPINS occurrences, including unknown versions, are processed.
Unknown versions and unrelated segments remain uninterpreted.

`GetOperationEvidence` distinguishes missing/unsupported evidence, an unlisted
operation, a reported J/N flag and ambiguity. Duplicate advertisements,
coexisting future versions, duplicate selected-operation entries and conflicting
PIN bounds never produce unique evidence. It does not combine this evidence
with BPD/UPD into an enabled capability. HIPINS listing still requires general
bank/user permission, and N does not establish a general SCA exemption.
Response status, freshness, completeness, user-permitted procedures, transport
authentication and challenge state require separate validation.

## Verification and next work

Five independent public fixtures and 202 checks cover both profiles, raw binary
octets, omitted/zero fields, flags, future/duplicate advertisements, inner
reference correlation, malformed schemas, boundary limits, cancellation and
fixed diagnostics. See the [fixture inventory](../../tests/Broiler.Fond.Kernel.Tests/Fixtures/FinTs/README.md).
The Python standard-library producer regenerates fixtures; ordinary CI uses
embedded JSON and .NET only.

Next are bounded HITANS procedure and HITAN challenge schemas, with explicit
version support and synthetic evidence before SCA continuation. Procedure
selection, credentials, live transport, authenticated ingestion, persistent
parameter caches, bank qualification and payments remain unimplemented.
