# ADR 0038: Bounded PIN/TAN request-envelope assembly

- **Status:** Accepted (local synthetic codec only)
- **Date:** 2026-09-08
- **Related:** [Response envelopes](0014-pin-tan-envelope-and-parameters.md),
  [signature context](0033-pin-tan-signature-request-context.md),
  [credential ownership](0035-session-credential-buffer-ownership.md),
  [plain request assembly](0037-pin-tan-request-assembly.md)

## Source and scope

Reviewed the [official FinTS specification](https://www.fints.org/de/spezifikation),
PIN/TAN 2020-07-10 B.9.1–B.9.3 and B.9.8–B.9.10 (printed pages 78–79),
plus its F.2.2 initialization example (page 235). HBCI 2024-06-11 B.5.3–B.5.4
(pages 35–36) defines HNVSK-3 and HNVSD-1. Download hashes are recorded in
ADR 0014. PIN/TAN-specific rules override generic HBCI encryption fields.

`FinTsPinTanRequestEnvelopeWriter` wraps the restricted initialization or
synchronization candidate from ADR 0037. Its input is matching immutable signature
evidence, an owned PIN, an optional owned TAN and a caller output span. It accepts
no arbitrary plain wire buffer. Results reuse the fixed request-write outcome
codes; there is no request parser, authenticated session or transport operation.

## Canonical envelope fields

The outer message contains exactly HNHBK-3, HNVSK-3 at reserved number 998,
HNVSD-1 at reserved number 999 and HNHBS-1. Dialogue 0 and message 1 remain
fixed by the restricted request context. The outer trailer retains the plain
candidate's logical segment number: 6 for initialization, 7 for synchronization.

HNVSK uses the bound PIN profile version, function 998 (plaintext), supplier
role 1, identity party 1 and the signature context's exact system identifier.
Its CID is empty. Timestamp identifier 1 reuses the explicit signature date/time;
absent date/time remain omitted, and a date without time remains date-only. No
wall clock is sampled and no freshness assertion is made.

The algorithm group uses canonical public filler metadata: usage 2, mode 2,
algorithm 13, eight zero binary octets, key-parameter identifier 5 and IV
identifier 1 with no IV value. The key name uses the bound country, institution
and user, key type V and filler number/version 0/0. Compression is 0 and the
certificate is omitted. These fields select no encryption algorithm or real key;
they are the local codec's fixed representation of the PIN/TAN filler convention.

HNVSD contains the exact plain inner bytes from HNSHK through HNSHA, including
business segments and original escaping. HNHBK and HNHBS are excluded from that
binary payload. Its decimal binary length is calculated from actual octets;
the twelve-digit outer message size includes the complete envelope and framing.
No double escaping, base64 conversion, compression or nested envelope occurs.

The envelope is plaintext. HTTPS protection belongs to later transport work;
building this structure provides no confidentiality or authentication. Existing
response-only envelope parsing continues to reject signed client request bodies.

## Bounds and ownership

The caller must reserve `MaximumEncodedLength` (3,072 bytes), including when the
actual output will be shorter. Context and reserve failures occur before
credential access. The public prefix, worst-case plain reserve, binary length
digits and ending are checked together before invoking credential-aware assembly.

One 2,048-byte stack span receives the plain candidate, and one 3,072-byte stack
span stages the wrapped output. Only credential-free envelope metadata uses strings and
builders. The assembler's known framing lengths select the inner body without
parsing secret-bearing bytes or creating a credential-bearing heap array/string.
Structural guards reject unexpected assembler framing rather than wrapping it.
Both spans are zero-initialized and cleared with `CryptographicOperations.ZeroMemory`
in a finally block on every exit. Nested writers retain their own cleanup duties.

The existing profile, context, printable-octet and TAN-placement checks continue
to apply. PIN:2 variant 2 rejects TAN submission in HNSHA. A successfully copied
TAN remains consumed even if a later boundary fails; it is never restored for
automatic retry. This is per-owner consumption, not evidence of bank submission.

The envelope writer checks cancellation and PIN availability before and after
copying to caller output, in addition to the nested plain/trailer checks. Failure
reports zero bytes; any published prefix is cleared, while failure before final
publication leaves caller bytes untouched. A clock failure returns a fixed
unavailable outcome. Cancellation cancels supplied owners and throws. Successful
output reports only its actual prefix, leaving the unused destination unchanged.

The caller must clear successful plaintext output on every downstream exit and
must not pass it through general parsers, logs or immutable strings. Cancelling
an owner after successful handoff cannot revoke the handed-out bytes. The access-
time expiry and memory-erasure limits in ADR 0035 remain unchanged. No live
credential entry path or retained secret request object is enabled.

## Verification and next work

Seventeen independent Python fixtures cover both profiles, initialization and
synchronization, permitted/rejected TAN placement, invalid credentials, missing
context, escaped/maximal credential/control fields, optional empty signature
fields, escaped body and envelope identities, and absent/date-only timestamps.
All credentials are public synthetic values.

The executable suite adds 207 checks for exact envelope bytes and binary lengths,
reserved/logical numbering, bound identities, public fillers, source ownership,
output reserves, actual credential-buffer erasure, culture independence and
concurrent TAN use. Cancellation/expiry tests exercise all three output boundaries;
a post-publication clock failure verifies complete failed-prefix cleanup. Synthetic
test parsing also confirms the bank-response parser still rejects client envelopes.

Next is credential-free context and response-reference binding for assembled
initialization/synchronization requests. Existing unsigned comparators must not
be applied to replies referencing renumbered signed business segments. HIPINS/
operation requirements, HKTAN/challenge integration, secure input, authenticated
sessions, transport, registration and controlled-bank acceptance remain pending.
The host stays inert and the candidate authorizes no operation.
