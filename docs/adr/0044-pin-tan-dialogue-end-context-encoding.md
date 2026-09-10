# ADR 0044: PIN/TAN dialogue-end context and encoding

- **Status:** Accepted (local synthetic candidate only)
- **Date:** 2026-09-09
- **Related:** [Unsigned closing](0030-dialogue-end-schemas-and-encoding.md),
  [request envelopes](0038-pin-tan-request-envelope-assembly.md),
  [synchronization lifecycle](0043-pin-tan-synchronization-attempt-lifecycle.md)

## Sources and restricted path

Reviewed the closing example on printed pages 238–239 and message layout table
of Security - Sicherheitsverfahren PIN/TAN, 2020-07-10, alongside Formals,
2017-10-06, C.8's mandatory close and reinitialize rule, from the
[official FinTS specification](https://www.fints.org/de/spezifikation).
The cached source SHA-256 values are respectively
`77bb5724cb391434cd7188924a1ff89bff040bf340dc49a09c92cdeb4ee7dc3a`
and `6b4809acd43acd2c6166c486964dee4b84b488a6a6902b29d1221458eaed7239`.

The PIN/TAN closing example contains HNSHK-4, HKEND-1 and HNSHA-2 with PIN only,
inside HNVSK-3/HNVSD-1. This increment implements that restricted layout following
an assembled synchronization response. It adds no general established-session
codec, TAN-bearing close, HKTAN, authentication or transport.

## Explicit closing context

`FinTsPinTanDialogueEndContext.Evaluate` takes the exact synchronization evidence,
an unsigned closing request and a separately supplied signature header. It retains
all three as caller-owned observations and exposes fixed issue flags. Review
evidence, including a bank abort, cannot produce a matching closing context.

The closing dialogue must exactly match the synchronization response. Both client
and expected bank counters must be 2 in this restricted path: the prior recovered
counter, including 9999, belongs to a different dialogue and is never used as the
closing counter. This local restriction does not narrow the unsigned schema.

Country, institution, user, PIN profile and security function must match the
original synchronization signature evidence. The signature header must occupy
logical segment 2 with customer supplier/party roles. For system assignment, the
header must use the unique matching assigned system ID; for message recovery it
must retain the original system ID. Selecting the assigned ID in a local candidate
does not persist it or activate a session. This is an explicit local policy that
still requires interoperability qualification before live use.

Procedure evidence remains the original synchronization context's observations;
closing does not negotiate or activate a procedure. Control reference, signature
reference and optional timestamp are explicit caller inputs validated by the
existing header schema. The codec allocates no reference, claims no freshness and
offers no replay registry. Context evaluation is pure and repeatable; it does not
consume the synchronization attempt's handoff.

## Encoding and ownership

`FinTsPinTanDialogueEndWriter.TryEncode` requires a matching context and a PIN
owner, with no TAN argument. The complete 2048-byte caller reserve is checked
before credential copy-out. An owner of the wrong kind remains unconsumed.

The outer frame uses the exact escaped dialogue and message 2. HNVSK and HNVSD
retain reserved numbers 998/999. The binary payload contains HNSHK 2, HKEND 3,
HNSHA 4, followed outside the payload by HNHBS 5. Both the twelve-digit whole-frame
size and the binary payload length reflect actual encoded bytes. The public
envelope identity and timestamp come from the bound closing header. Existing
canonical PIN/TAN plaintext fillers and escaping rules are reused.

Public header/segment formatting and the private signature-trailer core are shared
with earlier writers. Their public request contracts and wire results remain
unchanged. PIN copy-out, validation and escaping occur only in bounded stack spans;
production code never parses secret output or turns credential bytes into strings.
The 1024-byte payload and 2048-byte envelope staging areas are always zeroed.

Successful output contains plaintext PIN bytes and is the caller's responsibility
to clear. Any failure reports zero bytes. Late expiry, cancellation or clock failure
after publication erases the entire published envelope prefix; unused output stays
untouched. Cancellation cancels the PIN owner and propagates. Successful PIN
copy-out remains reusable according to the credential owner's lifetime contract.
Encoding can be repeated and does not imply one-time sending or closure.

## Verification and next work

Twenty independent Python vectors and 187 checks cover both profiles, assigned and
recovered system scope, maximum recovered counters, escaped dialogue/system/control/
PIN values, maximum PIN length, foreign dialogue/counters/identity/profile/function,
header roles and bank abort. Additional checks cover full-reserve preflight, exact
source ownership, invalid or disposed credentials, cancellation/expiry/clock faults
at seven credential boundaries, final-output clearing, culture independence and
null handling. Existing trailer and request/envelope suites verify shared-code
compatibility. All credentials and identities are public synthetic fixtures.

Next is assembled PIN/TAN closing response reference binding and termination
semantics, followed by bounded closing lifecycle integration. A written candidate
does not acknowledge closure or remove the requirement to reinitialize. The host
remains inert; secure input, authenticated transport and live acceptance are pending.
