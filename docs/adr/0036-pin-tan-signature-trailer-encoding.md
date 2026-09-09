# ADR 0036: Credential-aware PIN/TAN signature-trailer encoding

- **Status:** Accepted (local synthetic codec only)
- **Date:** 2026-09-08
- **Related:** [Signature context](0033-pin-tan-signature-request-context.md),
  [header encoding](0034-pin-tan-signature-header-encoding.md),
  [credential ownership](0035-session-credential-buffer-ownership.md)

## Source and scope

Reviewed the [official FinTS specification](https://www.fints.org/de/spezifikation):
PIN/TAN 2020-07-10 B.9.7 (printed page 79) and the user-defined signature data
dictionary (page 117), plus HBCI 2024-06-11 B.5.2 (page 34). Their hashes are
recorded in ADR 0032. HNSHA version 2 contains the control reference, an empty
validation result for PIN/TAN, and the user-defined PIN/optional TAN group.
Both credential fields have a schema maximum of 99 characters.

`FinTsPinTanSignatureTrailerWriter` writes one local HNSHA-2 candidate into an
explicitly supplied destination. It accepts matching signature-context evidence,
an owned PIN, an optional owned TAN and the trailer segment number. It creates
no complete message, cryptographic signature, envelope, authenticated session
or transport operation.

## Context and TAN placement

The supplied evidence must satisfy ADR 0033's initialization/synchronization
comparison. Its exact header supplies the control reference. The requested
trailer position must equal the unsigned request's segment count plus one:
5 for initialization, 6 for synchronization. These are the intended positions
after inserting HNSHK; a future assembly step must also renumber HNHBS.
No existing request frame is mutated or reinterpreted as signed.

A TAN may be supplied for PIN:1, or PIN:2 with the uniquely matched procedure's
process variant 1. PIN:2 variant 2 must transmit its TAN through HKTAN, so a
trailer TAN is rejected before credential access. PIN-only encoding remains
possible for either profile. Whether a particular operation needs a TAN, and
whether omission or submission is permitted by HIPINS and the pending challenge,
requires later operation/context integration. This writer does not decide those
requirements or infer an exemption.

Missing/mismatched evidence, the wrong position, disallowed TAN placement,
undersized destinations and incorrect credential kinds fail without copying
credential values. Kind inspection observes each owner's clock as usual.
Supplying one PIN owner in both roles is rejected.

## Bounded output and secret handling

The caller must reserve at least `MaximumEncodedLength` (440 bytes), even if
the eventual output is shorter. This is a conservative worst-case bound for
three-digit segment numbering, an escaped 14-character control reference and
two fully escaped 99-byte credentials. Checking that reserve before copy-out
prevents a short destination from unnecessarily consuming a TAN. Success reports
the actual written prefix; unused bytes are untouched.

Credential bytes pass through fixed-size stack spans: 99 bytes for PIN, 99 for
TAN and 440 for staging output. All three spans are zero-initialized and cleared
with `CryptographicOperations.ZeroMemory` in a finally block on every exit.
Production encoding does not convert credentials to strings, allocate credential
arrays, use a general syntax parser or retain parsed secret source objects.
Tests may parse public synthetic fixtures to inspect their structure.

PIN/TAN input must be nonempty printable Latin-1 octets; controls and bytes
127–160 are rejected. Supported high octets, literal syntax delimiters and
spaces are preserved exactly. There is no trimming or transcoding. The caller
must supply correctly encoded bytes and separately validate bank-specific limits.
The writer escapes `+`, `:`, apostrophe, `?` and `@` in place. Validation-result
content is always omitted; absent TAN also omits its colon/component.

The owned PIN is copied first and checked before requesting the TAN. A successful
TAN copy-out consumes and clears that owner immediately, before validation of its
text and final output publication. Thus invalid TAN text or a subsequent failure
leaves the TAN consumed. It is never restored for an automatic retry.

The PIN's availability is checked again before and after output publication.
If it expires or is cancelled while the TAN is consumed, output is withheld.
Late expiry or cancellation clears any already-written destination prefix and
reports zero bytes written. Cancellation throws after cancelling both supplied
owners; other failures return fixed result codes. Temporary credential spans are
cleared regardless of outcome.

## Ownership limits

Successful output contains plaintext credentials. The caller must clear it
promptly on every downstream exit and must not pass it through general logging,
diagnostic or immutable-string APIs. PIN ownership remains with the caller;
non-cancellation validation failures do not dispose a still-available PIN.
TAN consumption is one copy-out per owner, not proof of bank submission or a
cross-instance replay barrier.

The operation does not atomically own both credential lifecycles or an active
bank session. A caller may cancel an owner after successful handoff; that cannot
revoke the handed-out bytes. The lifetime, pinning, finalization and complete-
memory-erasure limitations in ADR 0035 continue to apply to copied output and
runtime intermediates. No secure UI or live credential entry path is enabled.

## Verification and next work

Twelve independent Python fixtures cover PIN-only and PIN/TAN trailers, both
profiles, variant 1 acceptance and variant 2 rejection, synchronization numbering,
escaped control/credential text, maximum escaped values, spaces, invalid octets
and unresolved context. The executable suite adds 106 checks for exact wire bytes,
empty validation fields, source ownership, TAN zeroization/consumption, output
bounds, wrong kinds/positions, cancellation, post-publication expiry, concurrent
TAN use and culture independence. All credentials are public synthetic values.

Next is bounded PIN/TAN initialization and synchronization message assembly,
preserving owned output and cleanup across header, body and trailer. Complete
security envelopes, operation/challenge validation, secure input, authenticated
sessions and transport remain pending. The host stays inert.
