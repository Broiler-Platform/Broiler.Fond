# ADR 0004: Profile envelope v1 review candidate

- **Status:** Proposed — not approved for user profile persistence
- **Date:** 2026-09-05
- **Extends:** [ADR 0002](0002-portable-profile-container.md)
- **Human security review:** Pending

## Scope

This candidate specifies exact encrypted-envelope bytes and interoperability
fixtures. A test-only reference codec exercises it; the shipped kernel and host
have no encryption, unlock, or profile-file implementation. Acceptance requires
review of this document, [ADR 0005](0005-profile-snapshots-v1.md), the vectors,
and measured KDF/host qualification. No algorithm fallback is permitted.

All sizes below are bytes. Integers are unsigned little-endian unless explicitly
marked otherwise. Arithmetic must be checked before allocation or offset use.
Unknown versions, algorithm IDs, flags, and nonzero reserved bytes fail closed.

## Fixed 96-byte header

| Offset | Size | Value |
| --- | --- | --- |
| 0 | 8 | ASCII `BFONDPRF` (no terminator) |
| 8 | 2 | Container version `1` |
| 10 | 2 | Header size `96` |
| 12 | 1 | KDF ID `1`: PBKDF2-HMAC-SHA-256 then HKDF-SHA-256 |
| 13 | 1 | AEAD ID `1`: AES-256-GCM, 16-byte tag |
| 14 | 1 | Payload ID `1`: ZIP containing versioned XML |
| 15 | 1 | Passphrase rule ID `1`: Unicode NFC, strict UTF-8 |
| 16 | 4 | PBKDF2 iterations, inclusive range 600,000–5,000,000 |
| 20 | 4 | Frame capacity, exactly 65,536 |
| 24 | 32 | Profile salt, stable across ordinary saves |
| 56 | 32 | Fresh snapshot salt for every encryption attempt |
| 88 | 4 | Fresh random nonce prefix for every encryption attempt |
| 92 | 4 | Reserved, all zero |

The header contains no profile identifier, timestamp, lineage, account, or bank
metadata. File size and linkage through the stable salt remain observable. It is
untrusted until tag verification. Validate the entire fixed header and resource
bounds before KDF work; never use header lengths as unchecked allocation sizes.

## Passphrase and key derivation

Reject null/empty input, invalid Unicode (including unpaired surrogates), more
than 1,024 UTF-16 code units before normalization, or more than 1,024 UTF-8 bytes
after NFC normalization. Preserve whitespace, case, and all valid code points;
do not trim, replace malformed text, or truncate. Empty profile fields are valid
where their schema allows them; an empty passphrase is not.

1. `P = strict_UTF8(NFC(passphrase))`, with no BOM.
2. `master = PBKDF2-HMAC-SHA-256(P, profileSalt, iterations, 32)`.
3. `content = HKDF-SHA-256(IKM=master, salt=snapshotSalt,
   info=ASCII("Broiler.Fond/profile-envelope/v1/content"), L=32)`.

The master key may be retained only for the unlocked session; cache identity
includes profile salt, KDF ID, normalization ID, and iteration count. Rotation
changes that identity and clears all old cached key material. Generate salts and
prefix with `RandomNumberGenerator.Fill`. Never reuse them for another candidate,
retry, interrupted write, or modified snapshot. Fixed values exist only in the
public synthetic fixtures. Clear mutable passphrase/key/plaintext buffers in
`finally` paths; managed strings and OS copies cannot be reliably erased.

The candidate iteration bounds are a review proposal, not calibrated release
policy. On the supported Windows floor, warm up then measure at least 30 unlocks
per candidate count, recording CPU, RAM, OS, runtime, power mode, median and p95.
Choose a release creation count within the envelope bounds targeting 500–1,000
ms median and at most 2,000 ms p95 for key derivation. Security review must approve
both the release minimum and the measurements; do not lower the candidate floor
automatically to meet latency. Readers retain the hard upper bound before work.
A local development machine timing does not qualify the supported Windows floor.
PBKDF2 remains CPU-hard, not memory-hard; a weak passphrase is still guessable.

## Frames and authenticated completion

The header is followed by zero or more data records and exactly one final record.
Every record is `descriptor || ciphertext || tag`:

| Descriptor offset | Size | Meaning |
| --- | --- | --- |
| 0 | 8 | Ordinal, starting at zero, incremented for every record |
| 8 | 4 | Plaintext/ciphertext length |
| 12 | 1 | `0` for data, `1` for final; no other bits |

For each record, `nonce = header[88..92] || uint64_big_endian(ordinal)` (12 bytes)
and `AAD = exact_header_bytes || exact_descriptor_bytes` (109 bytes). Encrypt
with the content key and a 16-byte GCM tag. Ciphertext length equals plaintext
length. The descriptor itself is authenticated through AAD.

Data records have lengths 1–65,536. All except the last data record must be full;
after a short data record only the final record is allowed. The final record has
length zero, flag `1`, no ciphertext, and its own tag and next ordinal. Even an
empty payload has this final record, although empty payload is invalid as ZIP.
Require physical EOF immediately after its tag. Trailing bytes, missing/duplicate
records, reorder, ordinal gaps, truncation, altered length or final flag, and tag
failure reject the generation. Never return a partially decoded snapshot as good.

The compressed payload limit is 1,073,741,824 (1 GiB), at most 16,384 data frames.
The maximum envelope size is `96 + payloadLength + 29 * (dataFrames + 1)`; a
maximum-sized payload is 1,074,217,085 bytes including all framing. The final
ordinal can be at most 16,384, far below nonce-counter exhaustion.

## Seekable production reader requirements

Full data frame `n` starts at `96 + n * (65,536 + 29)`. Physical length constrains
the possible last-data/final-record positions; validate their descriptors and
tags rather than trusting the position calculation. Authenticate the final frame
and its implied total compressed length before exposing a seekable ZIP view.
Authenticate each accessed frame before returning its plaintext, with a bounded
cleared cache. Before declaring a whole generation valid or promoting a save,
verify every frame and every archive/document/manifest invariant. Random access
does not waive full-generation verification. No plaintext temporary file is used.

The test reference is intentionally simpler: it validates the whole envelope in
memory, has an additional 8 MiB test-payload cap, and exposes no file API. It is
not the production streaming implementation or a performance qualification.

## Verification and approval

The committed vectors include fixed header bytes, normalized passphrase bytes,
master/content keys, compressed payload, nonce/AAD/descriptor/tag and ciphertext
digest for every frame, complete envelope bytes, and final SHA-256. All material
is synthetic and public. Python's independent fixture producer and the .NET BCL
reference must agree byte for byte for the same compressed input. ZIP compressor
output itself may vary; see ADR 0005. Normal CI only reads committed fixtures.

Negative checks include wrong passphrases, every header-byte position, malformed
KDF limits, data/tag/descriptor mutations, reordered/deleted/duplicated/spliced
frames, short-frame misuse, missing final tags, appended bytes, malformed Unicode,
and passphrase/frame size boundaries. Tests do not substitute for cryptographic
review, cross-host CI evidence, or save/recovery fault qualification.

Runtime contracts consulted: [.NET PBKDF2](https://learn.microsoft.com/en-us/dotnet/api/system.security.cryptography.rfc2898derivebytes.pbkdf2?view=net-10.0),
[HKDF](https://learn.microsoft.com/en-us/dotnet/api/system.security.cryptography.hkdf.derivekey?view=net-10.0),
and [AES-GCM authentication](https://learn.microsoft.com/en-us/dotnet/api/system.security.cryptography.aesgcm.decrypt?view=net-10.0).
The framing and limits above are project design choices. HKDF's algorithm and
independent known-answer check use [RFC 5869](https://www.rfc-editor.org/rfc/rfc5869.html).
