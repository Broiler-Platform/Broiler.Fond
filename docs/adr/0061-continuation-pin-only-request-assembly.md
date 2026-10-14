# ADR 0061: Continuation PIN-only plain request assembly

- **Status:** Accepted (local synthetic codec only)
- **Date:** 2026-10-14
- **Related:** [First-read assembly](0053-first-read-pin-only-request-assembly.md),
  [continuation trailer](0060-continuation-pin-only-trailer-encoding.md)

## Bound second-page assembly

`FinTsPinTanReadContinuationRequestWriter.TryEncodePinOnly` accepts a qualified
second-page balance signature context, a session PIN owner and caller output.
It emits HNHBK 1:3, HNSHK 2:4, HKSAL 3 at the requested version, HNSHA 4:2 and
HNHBS 5:1. The context supplies the assigned dialogue and message number 3,
unchanged account/options, exact continuation token and explicit signature control.
Optional empty fields and public-field escaping are preserved while renumbering
the proposed read segment. Original request and predecessor evidence remain intact.

The writer uses the existing public-segment helper and continuation trailer.
Wire interpretation and sources remain those of ADRs 0053 and 0060. Only public
observations enter a string builder; PIN bytes remain in bounded byte spans and
never enter strings or the general parser. The twelve-digit HNHBK size includes
all five segments and is calculated from the final encoded byte count.

## Ownership and failure behavior

Unresolved continuation/signature context and insufficient caller reserve fail
before PIN-owner access. The full 2,048-byte reserve and public-field length check
precede trailer encoding. Missing inherited PIN bounds also fail before querying
the owner; actual PIN text and length checks retain distinct results.

The nested trailer copies the PIN once. The assembler adds availability and
cancellation checks before and after whole-message publication, for seven owner
observations in total. Terminal failures erase owned PIN storage through existing
credential behavior. A failure after publication reports zero written bytes and
clears the entire copied message prefix, preserving the untouched destination
tail. Earlier failures leave caller output untouched. Temporary wire storage is
cleared on every exit, and cancellation cancels the PIN owner.

Successful output contains plaintext PIN bytes for its caller to clear. Assembly
does not renew the reusable PIN lifetime, consume a request or counter, decide
TAN omission, establish SCA readiness, wrap an envelope or send anything.
No existing entry point or result-enum value changes.

## Verification and next work

Eighty-two independent Python fixtures and 691 checks cover exact message bytes,
both profiles, TAN procedure versions, HKSAL versions 6/7/8, national account
fields, entry limits, escaped dialogue/control/token/PIN values, optional empty
fields, maximum lengths and every inherited context/credential review case.
Tests verify framing, logical segment roles, original-wire preservation,
preflight without owner queries, PIN copy accounting and actual storage erasure.

Failure injection covers token cancellation, owner cancellation/disposal, expiry,
clock exceptions and clock regression at all seven ownership observations.
Additional checks cover short output, wrong owner kind, null inputs, cultures and
concurrent assembly. Parsing of secret-bearing output occurs only in tests with
public synthetic credentials. Full Windows CI passed with zero warnings/errors.

Next is continuation PIN-only envelope assembly with credential-free candidate
metadata, exact binary payload and final-output cleanup. Later-page responses and
lifecycle handling, TAN/challenge integration, authenticated transport and live
qualification remain pending. The host remains inert.
