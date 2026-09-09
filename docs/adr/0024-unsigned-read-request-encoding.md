# ADR 0024: Credential-free unsigned read-request encoding

- **Status:** Accepted (local unsigned encoding only)
- **Date:** 2026-09-07
- **Related:** [Read schemas](0018-account-discovery-and-balance-schemas.md),
  [read context](0019-read-request-response-context.md),
  [discovery attempt](0023-in-memory-all-account-discovery-attempt.md)

## Sources and scope

Rechecked Messages, 2022-04-15, C.2.1.2 (HKSAL 6/7/8) and C.10.1.3
(HKSPA 1), plus Formals, 2017-10-06, B.1, B.6.3 and the message-size definition.
These documents come from the [official FinTS specification](https://www.fints.org/de/spezifikation);
their exact PDF hashes are recorded in ADR 0018. This increment retains that
schema scope and does not infer support for later discovery versions.

`FinTsUnsignedReadRequestWriter` constructs one HNHBK-3 header, one HKSPA-1 or
HKSAL-6/7/8 request and one HNHBS-1 trailer. Segment numbers are fixed at 1/2/3.
The caller supplies an established dialogue identifier and client message number
from 1 to 9999. The writer allocates no counter and emits no response reference.
The expected bank message number remains a separate context-parser argument.

These frames support local schema/context verification. They contain no
initialization, registered product identity, signatures, PIN/TAN envelope or
credentials. They are not complete authenticated bank requests and cannot
authorize sending. The writer does not select versions or evaluate BPD/UPD,
national-account permissions, account ownership or continuation provenance.

## Inputs and exact representation

`FinTsReadAccountInput` holds immutable caller-supplied national and international
identifier strings. Encoding validates the selected schema before returning bytes.
Identifier spelling, whitespace, leading zeros, account order and duplicate
entries remain intact. No checksum, normalization or local identity allocation
is performed. Callers must keep the discovery input list stable during encoding.

HKSPA accepts zero to 999 national account tuples; zero means all accounts.
HKSAL always requires one account tuple, even with its all-accounts flag set.
Version 6 uses national identifiers; versions 7/8 accept international, national
or combined tuples. A national-only schema rejects supplied IBAN/BIC values
instead of silently discarding them. Existing account-pairing, country and
length rules are enforced through the shared request parser.

Optional maximum-entry counts are positive integers through 9999. A null token
means omission; an explicitly empty continuation token is rejected. A token may
contain up to 35 decoded bytes. When a token is present without a maximum count,
the empty positional field is emitted. Unused trailing optional fields and
components are omitted, while required/internal empty positions remain.

The writer accepts printable Latin-1 text under the existing schema-reader
restriction (excluding controls and bytes 127 through 160). Characters outside
Latin-1, including isolated surrogates, fail without replacement or truncation.
This is an explicit local encoding restriction, not charset negotiation or a
claim that every accepted character is permitted by a bank's negotiated subset.

Every syntax delimiter in caller text is escaped by the existing element encoder.
Numbers use invariant decimal notation. The header's 12-digit zero-padded size
counts encoded bytes, including escape bytes, header and trailer. The finished
frame is parsed through the shared frame and unsigned read-context validators
before any bytes are returned. Encoding canonicalizes optional omissions; exact
reproduction of an existing wire representation remains `CopyWireBytes`' role.

## Bounds, cancellation and data ownership

Field and account-count limits bound temporary output; the existing 1 MiB frame
limit is also enforced. Cancellation is checked before work, during discovery
iteration, during final parsing and before return. Exceptions use fixed schema
categories without including caller identifiers or tokens.

The returned byte array belongs to the caller. Input classes use default
diagnostics that exclude identifier contents. Explicit properties and wire bytes
may contain private data and must not be logged. Strings and temporary buffers
are not secure credential containers, and no erasure guarantee is added.

## Verification and next work

Ten independent Python fixtures define typed inputs and exact wire bytes for
discovery all/selected/duplicate cases, every supported balance version,
national/international/combined identifiers, optional-field positions, escaping,
Latin-1 characters, field limits and boundary message numbers.

The executable suite adds 512 checks, including every permitted single-byte
character, rejected controls and Unicode replacement cases, invalid schemas,
999/1000 account boundaries, culture independence, cancellation, fixed diagnostics
and integration with the existing all-account discovery attempt. Existing parser
and lifecycle checks remain in place.

Next is credential-free dialogue-initialization schema and encoding work under
M1-05. Product registration, authenticated sessions, full security codecs, durable
replay, transport and domain ingestion remain pending; the host stays inert.
