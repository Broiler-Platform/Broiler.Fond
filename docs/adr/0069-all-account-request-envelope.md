# ADR 0069: All-account PIN-only request binding and envelopes

- **Status:** Accepted (local synthetic codec only)
- **Date:** 2026-10-14
- **Related:** [Request-envelope assembly](0038-pin-tan-request-envelope-assembly.md),
  [First-read PIN-only request envelope](0054-first-read-pin-only-request-envelope.md),
  [Continuation PIN-only request envelope](0062-continuation-pin-only-request-envelope.md),
  [Assembled all-account capability context](0068-assembled-all-account-capability-context.md)

## Bound all-account envelope candidate

`FinTsPinTanAllAccountRequestBinding.ForEnvelopeCandidate` retains one qualified all-account
capability context (`FinTsPinTanAllAccountCapabilityContext`) and an immutable description of
outgoing message segments:
`HNHBK` 1:3, `HNVSK` 998:3, `HNVSD` 999:1, `HNSHK` 2:4, `HKSPA` 3:1 (role `ReadOperation`),
`HNSHA` 4:2, and `HNHBS` 5:1.
The binding exposes the assigned dialogue ID, client message number (2), expected bank message
number (2), profile version, expected user ID, and read segment ordinal (3).

Candidate construction accepts no credentials and retains no secret bytes. Construction requires
`context.HasMatchingEvidence`; non-matching or review contexts immediately fail with
`FinTsFormatException(FinTsSyntaxError.InvalidSignatureContext)` before credential access or wire
encoding.

## Plain request assembly and trailer encoding

`FinTsPinTanSignatureTrailerWriter.TryEncodeAllAccountPinOnly` extends trailer writing to all-account
discovery. It checks caller-owned PIN bytes against reported HIPINS length bounds and active TAN
procedure rules without retaining or copying secret bytes outside bounded stack buffers.

`FinTsPinTanAllAccountRequestWriter.TryEncodePinOnly` synthesizes a plain message candidate of up to
2,048 bytes with outer framing `HNHBK:1:3+000000000000+300+dialogue+2'`, signature header `HNSHK:2:4`,
renumbered operation `HKSPA:3:1'`, trailer `HNSHA:4:2`, and trailer `HNHBS:5:1+2'`. It computes the
exact 12-digit wire length and erases temporary stack buffers and output prefixes upon failure or
cancellation.

## All-account request envelope

`FinTsPinTanAllAccountRequestEnvelopeWriter.TryEncodePinOnly` wraps the plain candidate output in a
plaintext PIN/TAN envelope (`HNVSK:998:3` and `HNVSD:999:1`).
It strips canonical framing by known lengths, places the exact inner bytes (`HNSHK`, `HKSPA`, `HNSHA`)
into a single binary element within `HNVSD`, preserves assigned dialogue and counter 2, and updates
the outer 12-digit message length field.

The envelope requires a 3,072-byte caller reserve before owner access. Reusable PIN lifetime remains
unchanged. Availability and cancellation are checked before and after publication. A failed write zeroes
any copied output prefix and leaves tails untouched. Successful output contains plaintext credentials
and must be cleared by callers. The codec performs no live transport, encryption, or authentication.

## Verification

`FinTsPinTanAllAccountRequestEnvelopeTests` exercises thirty-seven independent Python-generated
vectors and 408 checks in `pin-tan-all-account-request-envelope-v1.json`. Verification covers:
- Exact plain, payload, and outer envelope bytes against independent fixtures
- Valid all-account discovery under profiles 1 and 2, TAN versions 6 and 7
- Empty UPD account lists, unlisted operations, and `SingleAccountRequestAllowed = N`
- Credential requirement rejections (PIN too short, PIN too long, nullable bounds)
- Invalid credential character rejection (control codes, non-breaking space)
- Escaped dialogue identities (`D+:'?@ü`), escaped control references (`R+:'?@ü`), and maximum bounds
- Timestamp variations (absent, date-only, full timestamp)
- Candidate construction rejection for all review capability contexts
- Failure injection across all nine nested ownership observation boundaries
- Complete output prefix erasure on failure and untouched tail bytes
- Thread safety under parallel execution and culture independence across invariant and non-English cultures
- Reflection checks confirming absence of sensitive fields or credential leaks.
