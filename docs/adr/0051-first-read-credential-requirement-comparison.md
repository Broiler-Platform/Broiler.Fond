# ADR 0051: First-read credential requirement comparison

- **Status:** Accepted (local synthetic comparison only)
- **Date:** 2026-09-10
- **Related:** [Credential ownership](0035-session-credential-buffer-ownership.md),
  [trailer text encoding](0036-pin-tan-signature-trailer-encoding.md),
  [read capability context](0050-initialization-read-capability-integration.md)

## Scope and source basis

`FinTsPinTanReadCredentialComparison.Compare` compares one explicitly identified
PIN or TAN with a fully matching first-read account/capability context. Input is a
caller-owned `ReadOnlySpan<byte>` in the supported single-byte text subset. Output
is a scalar `FinTsPinTanReadCredentialIssue` flags value. No credential bytes,
lengths, owner references or source identifiers are retained in the result.

The [official FinTS specification](https://www.fints.org/de/spezifikation),
PIN/TAN 2020-07-10 data dictionary, defines input-format codes 1 (numeric) and
2 (alphanumeric) on printed page 126, PIN/TAN maximum lengths on pages 133–134,
and minimum PIN length on page 135. The document SHA-256 is
`77bb5724cb391434cd7188924a1ff89bff040bf340dc49a09c92cdeb4ee7dc3a`.
The supported local text subset follows the existing trailer encoder in ADR 0036;
no character-set negotiation or transcoding is introduced.

## Input and requirement checks

An unresolved outer read context returns ContextNeedsReview before credential
content is inspected. Undefined credential kinds and null contexts fail with fixed
argument diagnostics. Cancellation is observed before comparison, during the
bounded byte scan and before the normal result. Empty values and values above
the existing 99-byte local limit return distinct issues without scanning content.

Control bytes and bytes 127–160 are rejected. Supported Latin-1 high bytes, spaces
and literal FinTS delimiters remain unchanged. Lengths count original, unescaped
bytes, not a future escaped wire representation. Leading zeroes are preserved.
This API does not infer UTF-8, trim whitespace or normalize input.

PIN comparison checks the reported HIPINS minimum and maximum separately. Either
missing bound produces MissingPinBounds, while any supplied bound still applies.
No default or cached value fills an omission. Zero or contradictory HIPINS bounds
already prevent a matching initialization/read context.

TAN comparison checks the HIPINS maximum and, for a two-step selection, the exact
returned procedure's maximum and input format. Each maximum is enforced separately;
different maxima are not assigned precedence or merged into a replacement value.
A missing HIPINS maximum remains unresolved even when the procedure supplies one.
An unusable zero procedure maximum requires review. Numeric format admits ASCII
digits only. Alphanumeric format uses the supported text subset, including escaped
syntax characters. One-step comparison has no invented two-step format restriction.

Decoupled procedures reject supplied TAN input with TanInputNotSupported; their
PIN remains comparable. A reported HIPINS N flag does not skip checks on a supplied
TAN and does not establish an SCA exemption.

## Ownership and qualification limits

The comparator does not copy credential spans, convert them to strings, parse them
as FinTS syntax, access credential owners, consume a TAN, encode output or clear
caller input. The caller must prevent concurrent mutation and clear its buffer on
every downstream exit. Results and exception messages contain no credential values.
The existing session credential and trailer APIs retain their current behavior.

None means that this individual value fits the available requirements under this
local comparison. It does not establish that all required credentials are present,
that a PIN or TAN is authentic, that the challenge matches, or that a TAN belongs
in HNSHA rather than HKTAN. It is not a retained validation token or permission to
send. A future encoder must run comparison on its actual owned staging bytes and
maintain cancellation, expiry, one-time TAN handling and secret cleanup.

## Verification and next work

Forty-five independently generated fixtures and 659 checks cover inclusive and
missing bounds, independent TAN maxima, both profiles, TAN versions 6/7, numeric
and alphanumeric formats, leading zeroes, decoupled input, unresolved contexts,
and unchanged buffers. Exhaustive octet checks exercise the supported text and
numeric alphabets. Additional checks cover repeatability, concurrent immutable
inputs, cultures, cancellation, invalid arguments and fixed diagnostics. All input
values are public synthetic bytes, cleared by the tests after use.

Next is first-read PIN-only signature-trailer encoding with owned credential
validation. TAN placement/challenge integration, complete signed read assembly,
all-account capabilities, session/SCA coordination, secure input, authenticated
transport and live qualification remain pending. The host stays inert.
