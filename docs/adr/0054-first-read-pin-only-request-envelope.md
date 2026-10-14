# ADR 0054: First-read PIN-only request envelope

- **Status:** Accepted (local synthetic codec only)
- **Date:** 2026-09-10
- **Related:** [Request-envelope assembly](0038-pin-tan-request-envelope-assembly.md),
  [first-read request assembly](0053-first-read-pin-only-request-assembly.md)

## Bound envelope candidate

`FinTsPinTanReadRequestBinding.ForEnvelopeCandidate` retains one matching
first-read capability context and an immutable map of actual assembled segments:
HNHBK 1:3, HNVSK 998:3, HNVSD 999:1, HNSHK 2:4, HKSPA/HKSAL 3 at its exact
request version, HNSHA 4:2 and HNHBS 5:1. The shared segment-role enum appends
ReadOperation without changing earlier values. The binding exposes the assigned
dialogue, client/expected-bank message numbers, profile and expected user identity.

Construction accepts no credential and retains no credential owner or encoded
secret bytes. Its original context includes private account and identity evidence;
credential-free does not mean suitable for public diagnostics. It consumes no
request, counter or replay state. Candidate construction proves neither successful
encoding nor credential completeness: missing PIN bounds can still prevent encoding.

`FinTsPinTanReadRequestEnvelopeWriter.TryEncodePinOnly` wraps only output produced
by the existing first-read assembler. It removes that assembler's canonical public
framing by known lengths and checks, then puts the exact HNSHK/read/HNSHA bytes into
one HNVSD binary element. It never accepts arbitrary credential-bearing wire or
passes secret bytes through the general parser, string conversion or escaping.

The writer reuses ADR 0038's public wrapper helper and source rules: PIN profile,
function 998, exact header system/user identity and supplied optional timestamp,
eight zero filler bytes, and compression 0. It preserves the assigned dialogue and
message number 2, calculates the exact binary payload length and rewrites the final
twelve-digit message byte size. This plaintext wrapper provides no encryption or
authentication. No new protocol interpretation is introduced beyond ADR 0038.

## Credential and output ownership

The writer requires a 3,072-byte caller reserve before owner access. Bounded stack
buffers hold the 2,048-byte plain candidate and 3,072-byte wrapper; both are cleared
on every exit. Public-field bounds are checked before invoking the assembler.
The existing trailer validates the actual copied PIN against returned requirements,
and all distinct requirement, context and credential outcomes are preserved.

The nested assembler copies the PIN once. The envelope adds availability and
cancellation checks before and after final publication. A late failure reports
zero bytes and clears the entire published prefix, preserving the untouched tail;
earlier failures leave caller output unchanged. Cancellation cancels the owner.
Expiry, disposal and clock failures retain existing owner erasure behavior.
Successful output contains plaintext PIN bytes and must be cleared by its caller.

The method accepts no TAN owner and makes no TAN omission or SCA readiness
decision, including for an operation reported as not requiring a TAN. Reusable
PIN lifetime is unchanged. Independent buffers support concurrent encoding against
immutable metadata and the synchronized owner. No sending or session is activated.

## Verification and next work

Thirty-three independent Python fixtures and 456 checks cover exact envelope and
payload bytes, both profiles, TAN versions 6/7, discovery/balance versions,
preserved options, missing/date-only/full timestamps, escaped public and PIN fields,
requirement failures and rejected account contexts. Tests compare every metadata
entry with the actual synthetic outer/inner segments and verify response-only
parsing still rejects client signatures.

Failure injection covers token cancellation, owner cancellation/disposal, expiry,
clock failure and regression at all nine nested and final ownership observations.
Checks inspect actual owner-array erasure, full failed-prefix cleanup, untouched
tails, preflight without owner access, exact copy counts, null arguments, culture
independence and concurrent writes. Only public synthetic output is parsed in tests.

Next is assembled first-read response reference binding against the candidate's
actual read segment and envelope metadata. Response semantics, TAN/challenge
integration, all-account capabilities, session coordination, secure input,
authenticated transport and live qualification remain pending. The host stays inert.
