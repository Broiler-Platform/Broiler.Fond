# ADR 0037: Bounded PIN/TAN initialization and synchronization assembly

- **Status:** Accepted (local synthetic codec only)
- **Date:** 2026-09-08
- **Related:** [Signature context](0033-pin-tan-signature-request-context.md),
  [credential ownership](0035-session-credential-buffer-ownership.md),
  [signature trailer](0036-pin-tan-signature-trailer-encoding.md)

## Source and scope

The [official FinTS specification](https://www.fints.org/de/spezifikation),
Formals 2017-10-06 B.6, defines customer-message segment ordering; B.5.2/B.5.3
define the outer header/trailer. The reviewed Formals PDF has SHA-256
`6b4809acd43acd2c6166c486964dee4b84b488a6a6902b29d1221458eaed7239`.
The HBCI and PIN/TAN header/trailer sources and restrictions recorded in
ADRs 0032 and 0036 continue to apply.

`FinTsPinTanRequestWriter` assembles a plain first-dialogue candidate with
HNHBK-3, HNSHK-4, HKIDN-2, HKVVB-3, optional HKSYN-3, HNSHA-2 and HNHBS-1.
It accepts matching signature evidence, owned PIN/optional TAN and an explicit
caller destination. The result precedes security-envelope construction; it is
not a transport-ready or authenticated request. The host remains inert.

## Binding and numbering

The immutable evidence must pass ADR 0033's complete restricted comparison.
Its header and exact initialization/synchronization request supply the fields.
No independent caller body, inferred identifier, counter, clock value, procedure
fallback or anonymous request is introduced.

HNHBK uses dialogue 0 and message 1. HNSHK occupies position 2, moving HKIDN
to 3 and HKVVB to 4. Synchronization adds HKSYN at 5. HNSHA is 5/6 and
HNHBS is 6/7 for initialization/synchronization respectively. HNHBS also carries
message 1. The twelve-digit size field is calculated from the actual assembled
byte count, including both framing segments.

The schema-validated credential-free source fields are decoded and re-escaped
with their original component structure, preserving optional empty header
fields. The outer header is canonical and omits an empty optional response
reference. Neither the source request nor evidence is mutated. The private
public-field helper must remain limited to these credential-free schemas;
it rejects references and binary elements.

Existing response comparators are bound to unsigned request numbering and must
not be used to correlate this newly numbered candidate. Later signed-request
context integration must bind replies to the actual transmitted segment map.
This increment adds no response acceptance, recovery application or session state.

## Bounded secret output

The caller must reserve `MaximumEncodedLength` (2,048 bytes) before credentials
are accessed, even when the eventual message is shorter. This conservative
bound exceeds the restricted schema maxima and full escaping overhead. An
additional prefix-plus-worst-case-trailer-plus-ending check occurs before
credential copy-out, so future schema growth cannot silently exceed the reserve.
Only actual written bytes are reported; unused caller bytes remain untouched.

One zero-initialized stack span stages the whole request. Only credential-free
header/body fields use strings and builders. The existing trailer writer writes
directly into the stack span; no complete credential-bearing string, heap array,
general parser object, log entry or retained request object is created by the
production assembler. The stack span is cleared with
`CryptographicOperations.ZeroMemory` in a finally block on every exit.

The trailer's printable Latin-1, escaping, kind, context and TAN-placement
restrictions remain enforced. PIN:2 variant 2 rejects a TAN in HNSHA. A TAN is
consumed at copy-out and stays consumed even if validation, expiry or subsequent
publication fails. This does not prove bank submission or prevent cross-owner
replay. No automatic retry is introduced.

After trailer encoding, the assembler checks cancellation and PIN availability
before and after publishing the whole message. It also checks cancellation
immediately after the pre-publication availability check. Expiry or cancellation
withholds success, reports zero bytes and clears any published prefix. Failures
before publication leave the entire caller destination untouched. Cancellation
cancels both supplied owners and throws; ordinary rejected inputs return fixed
outcome codes. Other failures leave a still-available PIN under caller ownership.

Successful output contains plaintext credentials. Its caller must clear it on
every downstream exit and keep it out of logging, immutable strings and general
parsers. Availability checks cannot revoke a successful handoff if an owner is
cancelled afterwards. ADR 0035's lifetime and memory-erasure limits still apply.

## Verification and next work

Fourteen independent Python fixtures cover exact initialization/synchronization
frames, both PIN profiles, TAN acceptance/rejection, escaped and maximum
credential/control fields, invalid octets, unresolved evidence, an optional empty
certificate and escaped preparation fields. All credentials are public synthetic
values. The executable suite adds 157 checks for actual framing/size, contiguous
numbering, unchanged source fields, ownership, output bounds, cancellation and
expiry at both trailer and whole-message boundaries, zeroization, concurrent TAN
use and culture independence.

Next is bounded credential-aware PIN/TAN request security-envelope assembly.
Operation/HIPINS requirements, HKTAN/challenge integration, response binding to
assembled requests, secure input, authenticated sessions, transport, registration
and live-bank acceptance remain pending. This candidate authorizes no operation.
