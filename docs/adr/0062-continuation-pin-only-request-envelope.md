# ADR 0062: Continuation PIN-only request envelope

- **Status:** Accepted (local synthetic codec only)
- **Date:** 2026-10-14
- **Related:** [First-read envelope](0054-first-read-pin-only-request-envelope.md),
  [continuation plain assembly](0061-continuation-pin-only-request-assembly.md)

## Bound continuation candidate

`FinTsPinTanReadContinuationRequestBinding.ForEnvelopeCandidate` requires a fully
qualified second-page signature context. It retains that exact context and an
immutable map of HNHBK 1:3, HNVSK 998:3, HNVSD 999:1, HNSHK 2:4, HKSAL 3 at
the explicit request version, HNSHA 4:2 and HNHBS 5:1. Metadata exposes the
assigned dialogue, client and expected bank message numbers (both 3), profile
and expected user identity. Its separate type keeps first-read response APIs
from accepting a continuation candidate accidentally.

Construction accepts no credentials, stores no encoded secret output and consumes
no request, counter or replay state. The context retains private account and
identity evidence; credential-free metadata is not suitable for public logging.
Candidate construction does not prove encoding success: missing inherited PIN
bounds can still prevent encoding. Invalid context reports fixed diagnostics.

## Exact envelope bytes and ownership

`FinTsPinTanReadContinuationRequestEnvelopeWriter.TryEncodePinOnly` wraps only
bytes produced by the continuation plain assembler. It checks and removes that
assembler's canonical public framing by known lengths, then copies the exact
HNSHK/HKSAL/HNSHA payload into a single binary HNVSD element. It never accepts
arbitrary caller wire, parses secret-bearing data or converts PIN bytes to strings.

The writer reuses ADR 0038's public wrapper helper, retaining the bound profile,
system/user/institution identity and optional header timestamp. PIN/TAN function
998, eight zero filler bytes and compression 0 remain unchanged. Exact payload
length and final twelve-digit message size are calculated from encoded bytes.
No new protocol interpretation is introduced; this wrapper provides no encryption
or authentication.

A 3,072-byte caller reserve and public-field bounds check precede PIN access.
The nested assembler validates actual PIN bytes against inherited requirements,
copying them once. Distinct context, requirement, text and availability results
are preserved. Both the 2,048-byte plain buffer and 3,072-byte envelope buffer
are bounded stack storage cleared on every exit.

The envelope adds availability/cancellation checks before and after its final
publication, for nine nested ownership observations. Failure after publication
reports zero written bytes and erases the complete copied prefix; earlier failures
leave caller output untouched. Unwritten destination tails are preserved.
Cancellation cancels and clears the owner; expiry, disposal and clock failure
retain existing erasure behavior. Successful output contains plaintext PIN bytes
for the caller to clear. Reusable PIN lifetime is not renewed.

The API accepts no TAN, makes no TAN-omission or SCA-readiness decision, and starts
no transport or session activity. Concurrent callers use independent buffers and
the synchronized PIN owner. Original request and predecessor response are intact.

## Verification and next work

Eighty-two independent Python fixtures and 627 checks cover full envelope/payload
bytes, all inherited context and PIN review cases, profiles, procedure versions,
HKSAL versions 6/7/8, exact tokens/options, escaped fields, maximum lengths and
absent/date-only/full timestamps. Every metadata entry is compared with actual
outer/inner segments. Tests verify response-only parsing still rejects client
signatures, missing-bound preflight without owner queries, source preservation,
copy counts and actual PIN-storage erasure.

Failure injection covers token cancellation, owner cancellation/disposal, expiry,
clock exceptions and regression at all nine ownership observations, including
final-envelope prefix erasure and untouched tails. Reserve bounds, wrong owner
kind, null inputs, cultures, concurrent writes and candidate immutability are
checked. Only public synthetic credentials are parsed in tests.

Full Windows CI passed with zero warnings/errors. All 53 FinTS fixture files
reproduced byte-for-byte, and existing fixture contents were preserved.

Next is assembled continuation-response reference binding against this candidate's
actual segment roles and message-3 metadata. Later-page semantics/lifecycle,
TAN/challenge integration, authenticated transport and live qualification remain
pending. The host stays inert.
