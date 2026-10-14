# ADR 0060: Continuation PIN-only signature-trailer encoding

- **Status:** Accepted (local synthetic codec only)
- **Date:** 2026-09-11
- **Related:** [First-read PIN-only trailer](0052-first-read-pin-only-trailer-encoding.md),
  [continuation signature context](0059-balance-continuation-pin-tan-signature-context.md)

## Explicit continuation PIN component

`FinTsPinTanSignatureTrailerWriter.TryEncodeReadContinuationPinOnly` accepts one
fully qualified second-page signature context, an owned PIN and caller output.
It emits HNSHA version 2 at logical segment 4, following the intended HNSHK 2 and
HKSAL 3. The control reference comes from the continuation header, not the original
first-read header. The validation result is empty and the security component
contains only the PIN, with no TAN element.

The entry point delegates to the existing bounded trailer core. Wire rules and
their reviewed sources are unchanged from ADRs 0036 and 0052. It supplies the
complete continuation signature qualification as the preflight gate and the
exact inherited original capability context for PIN requirements. A matching
original capability cannot bypass an unresolved continuation or signature header.
Existing first-read, initialization/synchronization and closing entry points
retain their behavior, and no shared enum values change.

Encoding this PIN-only component does not decide TAN omission, satisfy a reported
TAN requirement or establish SCA readiness. Both PIN profiles and reported J/N
operation flags retain their existing scope. The method accepts no TAN owner and
cannot perform TAN placement, challenge continuation or authenticated submission.

## Preflight, validation and ownership

Unresolved continuation/signature context returns ContextNeedsReview before any
PIN-owner query. Missing inherited PIN minimum or maximum bounds returns
CredentialRequirementsNeedReview at the same preflight stage. The existing full
440-byte reserve is required before credential access. A TAN owner passed in the
PIN position is rejected without copying or consuming it; null arguments report
zero written bytes with fixed diagnostics.

The shared core copies the actual PIN once into bounded stack storage, validates
the existing printable single-byte text subset, then compares those same bytes
against the inherited PIN bounds. Invalid text and length/requirement review remain
distinct results. Bounds apply to original unescaped byte length. Spaces, Latin-1
text and delimiters are preserved; control and PIN delimiters are escaped directly
without secret string conversion or general parsing.

The method retains the existing five PIN-owner observations: kind, before/after
copy, and before/after caller-output publication. Cancellation cancels and clears
the PIN owner. Expiry, disposal, explicit cancellation, clock exception or clock
regression withholds success. Any copied output prefix is erased and written
length remains zero; unwritten caller bytes are untouched. All temporary PIN,
unused TAN and wire buffers are cleared on every exit.

Successful output contains plaintext PIN bytes and must be cleared by its caller.
The PIN remains reusable, its lifetime is not renewed, and a completed copy is
counted even if later validation or publication fails. Concurrent encodes use
independent output/staging buffers and the synchronized owner. Original proposed
request and predecessor response bytes remain unchanged.

## Verification and next work

Eighty-two independent Python fixtures and 741 checks cover every continuation
signature-context variant, explicit continuation controls, supported profiles and
procedure versions, all missing-bound combinations, minimum/maximum/over-limit
PIN values, whitespace, escaped delimiters, Latin-1 and invalid control/DEL/NBSP
octets. Maximum PIN and control expansion remains within the bounded reserve.

Tests verify no owner query on every context or missing-bound preflight failure,
exact successful bytes, unchanged source wire, reusable-PIN copy accounting and
actual PIN-array erasure. Failure injection covers token cancellation, explicit
owner cancellation/disposal, expiry and clock exception/regression at all five
ownership observations, including final-output prefix cleanup and untouched tails.
Additional checks cover unavailable owners, wrong credential kind, reserve bounds,
null inputs, cultures and concurrency. General parsing of credential-bearing
output is restricted to tests using public synthetic fixtures.

Next is continuation PIN-only plain request assembly with exact dialogue/message-3
framing, bound segment roles and whole-message cleanup. Envelope assembly,
later-page response/lifecycle handling, TAN/challenge integration, authenticated
transport and live qualification remain pending. The host stays inert.
