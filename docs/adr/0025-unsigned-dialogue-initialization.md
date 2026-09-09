# ADR 0025: Credential-free dialogue initialization schemas and encoding

- **Status:** Accepted (restricted unsigned observations and encoding only)
- **Date:** 2026-09-07
- **Related:** [Parameter evidence](0012-fints-parameter-evidence.md),
  [unsigned read encoding](0024-unsigned-read-request-encoding.md)

## Sources and scope

Reviewed Formals, 2017-10-06, B.4.1–B.4.2, C.3.1.2–C.3.1.3, C.5.1 and
the dialogue-ID, language and product data-dictionary entries from the
[official FinTS specification](https://www.fints.org/de/spezifikation).
The PDF SHA-256 remains
`6b4809acd43acd2c6166c486964dee4b84b488a6a6902b29d1221458eaed7239`.

`FinTsInitializationIdentification` observes HKIDN version 2;
`FinTsInitializationPreparation` observes HKVVB version 3. Both preserve their
exact source segment objects and reject unsupported versions, segment references,
binary substitutions, extra/missing fields and malformed known values.

`FinTsUnsignedInitializationRequest` accepts only an HNHBK-3 header, those two
segments in that order and an HNHBS-1 trailer, with contiguous numbers 1–4.
The initial dialogue identifier must be `0` and the message number must be `1`.
A populated outer response reference, established-dialogue request, security
wrapper, signature or additional business segment is outside this scope.
An explicit empty optional outer reference remains an omission.

Identified four-segment frames are local structural observations only: the
regular authenticated initialization layout requires additional security
segments. No session, customer identity, permission or bank response is
authenticated by successful parsing.

## Identification and preparation

Identification retains a three-digit country, a nonempty institution identifier
through 30 bytes, customer and system identifiers through 30 bytes, and system
status 0 or 1. This local subset requires both institution components and does
not check a country catalog, routing code, endpoint or customer ownership.

The anonymous customer marker is exactly **ten nines**, `9999999999`. The `3`
following the marker in the PDF's extracted identification table is a footnote,
not an eleventh digit; C.5.1's response guidance repeats the ten-digit value.
That marker requires system identifier `0` and status `0`. An anonymous flag
describes only this wire tuple and grants no account-discovery or read permission.

Other identities preserve the supplied system identifier and status without
inferring a security profile. Status 0 with a card identifier and status 1 with
system identifier 0 can be observed. The latter does not perform the required
synchronization or prove that normal identified requests are ready to send.

Preparation contains canonical BPD/UPD version integers from 0 through 999,
language 0/1/2/3 (standard/German/English/French), a required product identifier
through 25 bytes and a required product version through 5 bytes. Zero parameter
versions remain zero; they do not create a parameter cache. The language field
does not establish bank support or negotiate a character subset.

## Writer and representation

`FinTsUnsignedInitializationWriter.Encode` takes immutable explicit caller input.
`EncodeAnonymous` supplies only the prescribed anonymous identity tuple. Both
require product identifier and version arguments: neither invents, registers
or certifies a product identity. Synthetic fixtures use public placeholder text.
The repository's registration and release requirements still apply before live use.

Initialization fields use the restricted printable Latin-1 repertoire already
used by the unsigned read writer. Non-Latin-1 text fails without replacement.
In accordance with the reviewed basic-format rule, initialization rejects leading
and trailing ASCII spaces rather than silently trimming identifiers. Internal
spaces and escaped syntax characters remain exact. This stricter initialization
rule does not change the earlier read-observation/encoding policy.

The two unsigned writers share an internal escaped-text and frame-size helper.
It emits invariant counters and a 12-digit size that includes all escaped bytes.
Each public writer still validates its own finished frame through its own schema
before returning caller-owned bytes. The shared helper exposes no public generic
message or security-envelope writer.

Field bounds, cancellation, fixed error categories and default diagnostics are
covered by tests. Explicit input properties and wire buffers may contain private
identifiers and are unsuitable for logging. There is no secure-string container,
credential use, memory-erasure guarantee, transport or persistent state.

## Verification and next work

Eight independent Python fixtures cover first/cached anonymous requests,
identified status variants, all supported language codes, escaped Latin-1 text,
internal spaces and maximum field lengths. The executable suite adds 295 checks
for exact bytes, retained source identity, context isolation, unsupported versions,
malformed fields, binary substitutions, anonymous conflicts, Unicode rejection,
length limits, culture independence, cancellation and safe diagnostics. The
existing read-request wire fixtures verify the shared helper without byte changes.

Next is initialization response binding and parameter-scope evidence under M1-05.
Authenticated initialization, product registration, synchronization, security
codecs, transport, durable replay and domain ingestion remain pending. The host
continues to be an inert development shell.
