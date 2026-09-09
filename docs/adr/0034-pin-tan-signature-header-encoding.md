# ADR 0034: Typed PIN/TAN signature-header encoding

- **Status:** Accepted (credential-free local encoding only)
- **Date:** 2026-09-08
- **Related:** [Header schemas](0032-pin-tan-signature-header-schemas.md),
  [request context](0033-pin-tan-signature-request-context.md),
  [unsigned request encoding](0024-unsigned-read-request-encoding.md)

## Decision and input ownership

`FinTsPinTanSignatureHeaderWriter.Encode` emits exactly one HNSHK-4 segment
from immutable `FinTsPinTanSignatureHeaderInput` and an explicit segment number.
The input exposes profile/function, control reference, supplier/party roles,
system identifier, exact security reference number, optional date/time, algorithm
code observations and key-name fields. All values are caller-supplied; there is
no implicit clock, reference generator, counter allocation or profile selection.

The input constructor rejects null text. Encoding validates numeric bounds and
text, builds the segment, then validates its complete bytes through the existing
HNSHK-4 parser. It implements the schema subset in ADR 0032 without broadening
that parser or the request-context checks in ADR 0033.

This codec contains no PIN or TAN input. It does not construct a signed message,
add HNSHA or a security envelope, change an existing frame, select cryptography
or send data. Schema-valid roles or segment positions may still require review
under the narrower single-header request comparator.

## Exact bytes and canonical omissions

Text is validated as printable Latin-1 before encoding and all syntax delimiters
are escaped through the shared text encoder. Characters outside that repertoire,
controls and disallowed padding fail; no replacement, normalization or trimming
occurs. Internal spaces and literal delimiter characters are preserved.

The segment number is from 1 through 999. Profile/function and role fields are
bounded before final schema validation. Key numbers and versions are 0–999.
The security reference is an exact `ulong` capped at 9999999999999999, including
zero. Numeric values are emitted with invariant canonical decimal syntax. Numeric
code fillers remain strings, preserving leading zeros such as `001` and `000`.

The writer fixes the supported schema's application area, timestamp qualifier,
hash usage/parameter identifier, signature usage and key type. CID is represented
by the required empty middle component of security identification. There are no
inputs for forbidden CID, certificate or hash-parameter values.

The timestamp is encoded as qualifier only, qualifier/date, or qualifier/date/time,
depending on supplied values. Dates use `yyyyMMdd` and times use `HHmmss`, both
invariant. A time without a date fails. Subsecond precision fails instead of being
silently truncated. Valid minimum/maximum calendar values and midnight remain
exactly representable.

Unused trailing timestamp components, the optional hash-parameter component and
the certificate field are omitted. Therefore this is a typed canonical writer,
not a byte-preserving reserializer of arbitrary parsed omission spellings.
Existing source objects continue to preserve their original bytes separately.

## Validation and evidence boundaries

The resulting bytes are parsed as exactly one segment and validated by
`FinTsPinTanSignatureHeader`. Invalid fields use fixed errors without source
values. Unsupported profile/hash choices retain the schema's unsupported error.
Cancellation is checked before work, during final parsing and before return.
The operation is bounded by field lengths and the existing syntax limits.

Each call returns a new caller-owned byte array. Mutation of that array cannot
alter immutable input, future output or previously parsed source. The input's
default diagnostics exclude identifiers; explicit property access and output
bytes remain private data unsuitable for logging. This introduces no secure
credential container or zeroization guarantee.

Encoding does not require or imply matching procedure evidence. Callers can
encode synthetic candidates for comparison, including schema-valid candidates
that fail a stricter context check. No caller should treat encoding success as
proof of identity, bank assignment, freshness, permission or authentication.

## Verification and next work

Ten independent Python input/wire fixtures cover both profiles, function and
segment bounds, escaped identifiers, omitted/date-only/full timestamps, maximum
fields, exact 16-digit references, zero values and alternate numeric fillers.
The executable suite adds 336 checks for exact bytes, every typed field,
canonical omissions, malformed inputs, unsupported profiles, fractional-second
rejection, nulls, cancellation, culture independence and output ownership.

Integration checks compare an encoded candidate against the independent
initialization/procedure-origin fixture and verify that a schema-valid alternate
position still fails the request comparator's single-header requirement.

Next is bounded session-only PIN/TAN buffer ownership and cleanup, needed before
signature-trailer encoding. HNSHA, complete request security, authenticated
sessions, transport and durable integration remain pending. The host stays inert.
