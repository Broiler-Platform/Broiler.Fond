# ADR 0053: First-read PIN-only request assembly

- **Status:** Accepted (local synthetic codec only)
- **Date:** 2026-09-10
- **Related:** [Plain request assembly](0037-pin-tan-request-assembly.md),
  [first-read capability context](0050-initialization-read-capability-integration.md),
  [PIN-only read trailer](0052-first-read-pin-only-trailer-encoding.md)

## Plain first-read candidate

`FinTsPinTanReadRequestWriter.TryEncodePinOnly` assembles a plain first-read
candidate from one matching single-account capability context and an owned PIN.
It uses the existing framing, escaping and signature component rules, with no
transport or security-envelope wrapper. The logical segment sequence is:

1. HNHBK 1: exact assigned dialogue and first-read message number 2.
2. HNSHK 2: the bound detached signature-header observations.
3. HKSPA or HKSAL 3: the explicit request operation, version and fields.
4. HNSHA 4: bound control reference and validated PIN-only component.
5. HNHBS 5: the same message number.

The twelve-digit HNHBK size is calculated from the final byte count including both
framing segments. Discovery version 1 and balance versions 6/7/8 retain their
existing single-account qualification requirements. Supplied optional empty
fields, entry-count input and national/international account fields are preserved.
The original unsigned request remains unchanged, including its segment number 2.
Assigned dialogue, account identifiers, control references and literal delimiters
are escaped without normalization or transcoding.

The PIN-only security component does not establish permission to omit a TAN,
challenge completion or SCA readiness. An operation's reported TAN requirement
remains unresolved by assembly. The method accepts no TAN owner and creates no
request record, counter allocation, replay guard or authenticated session.

## Bounds and credential handling

The method requires a full 2,048-byte destination reserve before owner access.
It checks the complete account/capability context, builds only credential-free
framing/header/read fields in a string builder, and checks their conservative
size with the trailer reserve before copying a PIN. Public segment serialization
reuses the established restricted helper; secret-bearing segments never enter it.

The PIN-only trailer writer validates actual copied staging bytes against the
returned PIN requirements. Missing bounds, invalid text, unavailable credentials
and out-of-range values remain distinct outcomes. The request-result enum appends
CredentialRequirementsNeedReview to preserve the trailer's new requirement outcome
without changing existing numeric values. No general parser or string conversion
handles the assembled credential-bearing bytes in production.

One bounded stack buffer stages the whole message; the nested trailer retains its
own bounded buffers. The PIN is copied only once. Availability and cancellation
are checked again before and after final whole-message publication. Every exit
clears staging. A late failure clears the entire copied message prefix and reports
zero bytes, leaving unwritten destination bytes untouched. Earlier failures publish
neither credentials nor partial public framing. Cancellation cancels the owner;
expiry, disposal and clock failures retain their established cleanup behavior.
Successful output contains plaintext PIN bytes and must be cleared by its caller.

Encoding does not renew credential lifetime or consume a reusable PIN. Concurrent
calls use independent staging/output buffers and the synchronized owner. Existing
initialization/synchronization and closing writers retain their behavior.

## Verification and next work

Thirty-one independent Python fixtures and 334 checks cover exact complete frames,
both PIN profiles, TAN versions 6/7, discovery/balance versions, preserved options,
escaped dialogue/account/control/PIN fields, missing bounds, invalid PIN values,
unqualified account scope and unhandled limits. Tests parse public synthetic
output only, checking byte-size framing and all five segment roles.

Additional checks inject token cancellation, owner cancellation/disposal, expiry,
clock failure and regression across five nested-trailer observations and the two
whole-message publication observations. They verify actual owner-array erasure,
complete failed-output cleanup, untouched tails, preflight without owner access,
copy counts, null inputs, culture independence and concurrent assembly.

Next is first-read PIN-only request-envelope assembly with credential-free
candidate metadata. TAN/challenge integration, read-response reference and semantic
binding, all-account capabilities, broader session/SCA coordination, secure input,
authenticated transport and live qualification remain pending. The host stays inert.
