# ADR 0052: First-read PIN-only signature-trailer encoding

- **Status:** Accepted (local synthetic codec only)
- **Date:** 2026-09-10
- **Related:** [Credential ownership](0035-session-credential-buffer-ownership.md),
  [signature trailer](0036-pin-tan-signature-trailer-encoding.md),
  [read capability context](0050-initialization-read-capability-integration.md),
  [credential comparison](0051-first-read-credential-requirement-comparison.md)

## Explicit PIN component

`FinTsPinTanSignatureTrailerWriter.TryEncodeReadPinOnly` takes a fully matching
first-read single-account capability context, one owned PIN and a caller-owned
output span. It writes an HNSHA version 2 candidate at segment number 4, following
the intended HNSHK 2 and read operation 3. It retains the detached header's exact
control reference, an empty validation result and one PIN field without a TAN
component. It does not mutate or renumber the unsigned request.

The wire layout and source basis remain those reviewed in ADR 0036. The method
accepts no TAN owner. It deliberately encodes only a PIN security component for
either profile and either reported operation TAN flag. A J flag still reports a
TAN requirement; a successful PIN-only component does not satisfy it. N does not
waive SCA. TAN presence, placement, challenge scope and readiness of a complete
request are separate unresolved decisions. No request is sent or recorded.

## Preflight and actual-byte validation

The outer account/capability result must match. Missing reported PIN minimum or
maximum bounds return CredentialRequirementsNeedReview before querying the owner.
The method then requires the existing full 440-byte worst-case reserve and a PIN
owner; a supplied TAN owner is rejected without copy or consumption. Null inputs
fail with fixed argument diagnostics and zero bytes written.

The shared writer copies the PIN into its bounded stack staging buffer. Existing
printable text checks run first. ADR 0051's comparator then validates these exact
copied bytes against the returned PIN bounds. It does not rely on a previously
computed comparison result or reread another credential value. Invalid text keeps
the existing InvalidCredentialText outcome; length/requirement failures use the
new appended CredentialRequirementsNeedReview outcome. A successful PIN copy is
counted even if subsequent validation fails. The available PIN remains reusable.

Control and PIN delimiters are escaped directly without string conversion,
transcoding or general parsing. The existing initialization/synchronization and
closing entry points do not acquire read-specific PIN requirements.

## Expiry, cancellation and cleanup

The new entry point reuses the existing bounded staging and finally-block erasure.
PIN availability is checked before/after copying and before/after output handoff.
An additional token check immediately before output copy prevents a cancellation
raised by the preceding availability observation from briefly publishing output.
This extra cancellation check also applies to the existing shared writer paths.

Cancellation cancels and clears the supplied owner. Expiry, explicit owner
cancellation/disposal, clock failure or clock regression withhold success. Any
already-copied secret output prefix is zeroed and bytesWritten remains zero;
unwritten destination bytes are untouched. Temporary PIN, TAN and wire staging
spans are always cleared, including when the TAN staging buffer is unused.
Successful output contains a PIN and must be cleared promptly by its caller.

The writer adds no credential timer, TAN consumption, replay protection or session
state. Concurrent calls use separate staging/output buffers and the existing
synchronized reusable PIN owner. Credential lifetime is not renewed by encoding.

## Verification and next work

Twenty-two independent Python wire fixtures and 243 checks cover exact trailer
bytes, both PIN profiles, TAN versions 6/7, decoupled PIN components, missing and
contradictory bounds, short/long PINs, high octets, whitespace, escaped delimiters,
maximum PIN/control sizes, invalid text and unresolved account context.

Additional checks inject token cancellation, owner cancellation/disposal, expiry,
clock failure and regression at all five ownership boundaries. They verify actual
owned-array erasure, zeroed failed-output prefixes, unchanged output tails,
completed-copy accounting, preflight without owner access, wrong credential kind,
null inputs, culture independence and concurrent reusable-PIN encoding. General
parsing occurs only in tests on public synthetic output.

Next is first-read PIN-only request assembly with bound segment numbering and
whole-message secret cleanup. TAN/challenge integration, read response binding,
all-account capabilities, broader session/SCA coordination, secure input,
authenticated transport and live qualification remain pending. The host stays inert.
