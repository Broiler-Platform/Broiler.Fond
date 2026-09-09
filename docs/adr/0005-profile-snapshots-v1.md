# ADR 0005: Snapshot documents and lifecycle review candidate

- **Status:** Proposed — schema coverage and human review incomplete
- **Date:** 2026-09-05
- **Extends:** [ADR 0002](0002-portable-profile-container.md)
- **Envelope:** [ADR 0004](0004-profile-envelope-v1.md)

## Scope and versioning

This increment defines the exact empty-profile (`bootstrap-v1`) document set,
ZIP/XML rules, and proposed generation/recovery/purge protocol. It deliberately
does not invent banking document schemas before those domain contracts exist.
Full M1 profile persistence requires reviewed schemas for institutions,
connections, accounts/incarnations/bindings, balances, and audit history, plus
manifest expansion and associated identity/reference validation. Until then,
bootstrap-v1 is a synthetic interoperability test contract only.

An unsupported document-set version or unknown entry fails closed; no reader
silently drops unfamiliar financial data. M1 must assign an explicit new
document-set version when extending bootstrap-v1. Container version and document
versions are separate; reusing the envelope does not imply schema compatibility.

## Exact bootstrap document set

Exactly two entries, in both local-record and central-directory order:
`profile.xml`, then `manifest.xml`. No directories, duplicate/case-colliding
names, absolute paths, separators, traversal, extra entries, or archive comment.
Entry names are these literal ASCII strings. No extraction to filesystem paths.

Both documents use namespace `urn:broiler:fond:profile:1` and version `1`.
The executable [schema](../../tests/Broiler.Fond.Kernel.Tests/Fixtures/ProfileEnvelope/bootstrap-v1.xsd)
and public [golden bytes](../../tests/Broiler.Fond.Kernel.Tests/Fixtures/ProfileEnvelope/v1.json)
are part of this candidate. Schema order is serialization order.

`profile.xml` has root attributes, in order: `xmlns`, `version`, `profileId`,
`branchId`, `generation`, `nextId`, `savedAt`; children `label`, `note` in order.
UUIDs are nonzero lowercase canonical `D` strings. Generation and nextId are
positive unsigned 64-bit integers, no sign or leading zeros. Saved time is UTC
`yyyy-MM-dd'T'HH:mm:ss.fffffff'Z'` and a valid Gregorian date. Label is at most 256
Unicode scalar values; note at most 1,024. Empty label/note are permitted. Profile
and branch IDs are lineage metadata, not substitutes for entity IDs. There are
no allocated entities in this document set; nextId may retain a higher counter
after a future deletion, but is never reset automatically.

`manifest.xml` has root attributes `xmlns`, `version`, `profileId`, `branchId`,
`generation` and exactly one `entry`, with attributes `name="profile.xml"`,
`schema="1"`, `bytes`, `sha256` in that order. `bytes` is the actual uncompressed
UTF-8 byte count and `sha256` is 64 lowercase hexadecimal digits covering the
exact profile bytes. Lineage and generation must match profile.xml. The manifest
does not hash itself. Envelope authentication protects both documents.

## XML lexical and parser contract

UTF-8 with no BOM; exact declaration `<?xml version="1.0" encoding="utf-8"?>`
followed immediately by the root. No indentation, comments, processing
instructions, CDATA, DTD, external references, extra whitespace, namespace
prefixes, or schema-location hints. Empty elements use ` />`. Quotes are double.
XML writer escaping is used for text and attributes. LF is the sole literal
newline. Invariant-culture scalar formatting is mandatory. Financial/source text
will retain its raw Unicode rather than undergo passphrase NFC normalization.

Use strict UTF-8 decoding and `XmlReaderSettings` with `DtdProcessing.Prohibit`,
`XmlResolver = null`, `MaxCharactersInDocument = 16384`,
`MaxCharactersFromEntities = 1024`, and an embedded trusted schema set whose
resolver is also null. Do not enable inline or schema-location processing.
Reject maximum depth above 8 and any unknown required content. Parse and validate,
then serialize the validated data using the lexical contract and compare exact
bytes; semantic XML equivalence alone does not satisfy canonical byte identity.
The test reference performs this comparison for both bootstrap documents.

## ZIP encoding and bounds

Use ordinary single-volume ZIP, method 8 (DEFLATE), no ZIP encryption, ZIP64,
extra fields, archive/entry comments, platform permissions, symlinks, or appended
data. DOS timestamp is exactly 1980-01-01 00:00:00. Writers use `ZipArchive` with
`CompressionLevel.Optimal`, explicit entry ordering and timestamps, and DOS
archive attribute `0x20`. Readers accept methods 0 (stored) and 8, UTF-8 flag bit
11 and data-descriptor bit 3 only, and signed data descriptors when bit 3 is set.
Central and local names/methods/flags must agree; CRC and sizes must agree with
actual data and descriptors. Local records and central directory occupy the
whole payload without overlap, gaps, prefix/suffix, or hidden records. A bounded
ZIP-structure preflight is required before a production `ZipArchive` constructor;
checking `Entries.Count` afterwards alone is not a hostile-input memory bound.

Bootstrap-specific limits: at most 1 MiB compressed ZIP, exactly two entries,
16 KiB uncompressed per entry, 32 KiB total, 128:1 maximum per-entry expansion
against `max(1, compressedSize)`, and 128-byte entry names. Verify bounds against
actual streaming bytes, not just central-directory declarations. Future M1
document sets must set their own reviewed limits below the envelope's 1 GiB cap.

Deflate output may change across runtime/compressor versions. Writers guarantee
the XML bytes, ordering, metadata policy, and decoded semantics; they do not
promise a universal compressed-byte digest. Envelope vectors encrypt a committed
ZIP byte sequence, so their ciphertext is byte-exact on every supported host.
The Python fixture producer uses a seekable ZIP writer; the .NET test writer
round-trips its own output without assuming identical deflate bytes.

## Proposed managed generation protocol (not implemented)

A dedicated local profile directory holds `profile.lock`, immutable
`generation-{20 decimal digits}.bffond`, and exclusive-created candidates
`profile.new-{32 lowercase hex digits}.tmp`. Filenames are local bookkeeping;
authenticated manifest identity is authoritative. No archive string supplies a
directory or deletion path. Qualify only local fixed-disk filesystems initially.
Hold `FileShare.None` on the lock file and an in-process single-writer guard.

An ordinary save increments generation (overflow fails closed), allocates any
entity IDs in that same snapshot, writes one new encrypted candidate, flushes to
stable storage where available, closes, then reopens and fully verifies envelope,
ZIP, manifest, schemas and identity invariants. Promote without overwrite to the
matching immutable generation filename. Reopen and verify the promoted file
before publishing the in-memory current state. Retain only the current and one
immediately preceding validated generation in the same lineage; cleanup failure
is visible and recorded, never reported as successful privacy deletion. Use a
new snapshot salt/prefix for every attempted candidate, even after failure.

Startup ignores unpromoted candidates. Validate recognized committed files and
choose the highest fully valid generation only within an unambiguous lineage
and branch history. A filename/manifest number mismatch, duplicate generation
with different content, different lineage, or fork conflict enters read-only
recovery rather than guessing. Missing or corrupt newest data produces a visible
recovery choice, not silent success from old data. Wrong passphrase and tamper
are not automatically distinguishable. Never delete a failed file as a repair.
If recovery selects an older valid generation, establish a new writable branch
before allocating; full restore into an empty profile does likewise. Full M1
schemas must encode branch ancestry/recovery provenance before these transitions
are implemented. Import into a nonempty profile requires a complete ID remap and
reference validation; it is not a file overwrite.

## Proposed purge and passphrase rotation protocol (not implemented)

Under the same lock, reserve an exclusive `purge.pending` marker containing only
ASCII `BFONDPURGE1` plus LF and flush it before a privacy-changing operation. Its
presence forbids automatic fallback to old generations. It is a recovery signal,
not an authority to delete files, and may be attacker-created to cause denial of
service. A resumed operation still requires an authenticated replacement and
explicit recovery handling; do not delete anything solely because a marker exists.

Commit and fully verify the replacement generation omitting deleted data (or,
for rotation, using a new passphrase and fresh profile/snapshot salts). Verify the
replacement by reopening with its intended passphrase. Close superseded handles;
remove every older validated same-profile generation and recognized candidate
from this dedicated managed directory. Verify by re-enumeration that no prior
application-managed generation/candidate remains, then remove the pending marker
and record completion durably where supported. Do not retain a recovery copy for
a privacy purge. A candidate whose lineage cannot be authenticated requires
explicit local recovery disposition; it cannot be silently deleted or counted as
purged. Whole-profile erasure similarly requires ownership-validated targets and
post-deletion enumeration, with the marker removed last.

A crash at any point leaves the operation incomplete and prevents selecting old
data as current. The full implementation needs a reviewed durable encrypted
intent schema binding replacement identity, operation type and exact purge
inventory before it can safely resume or finish automatically; bootstrap-v1
does not supply that schema. Rotation must never label an old-passphrase file
purged merely because it cannot be unlocked with the new passphrase. Preserve
the validated old-file inventory across the transaction, clear old keys, and
test fault injection after every write, flush, close, rename and deletion step.

External backups, snapshots, filesystem/SSD remnants, lost passphrases and
rollback by replacement of all valid generations remain the limitations in ADR
0002 and the roadmap. This protocol is not a guarantee of physical erasure or
an independent rollback anchor.

## Review completion checklist

- [x] Bootstrap schemas, lexical bytes, manifest digest and envelope vectors.
- [x] Independent Python fixture generation and .NET verification harness.
- [ ] Full M1 document schemas, ancestry and encrypted purge-intent schema.
- [ ] Production bounded ZIP preflight and seekable authenticating reader.
- [ ] KDF calibration on the supported Windows floor and release minimum.
- [ ] Linux/macOS interoperability evidence (fixtures are suitable for CI).
- [ ] Generation, recovery, purge and rotation fault-injection implementation.
- [ ] Human format/security review before any user profile persistence.

API contracts consulted: [.NET ZipArchive](https://learn.microsoft.com/en-us/dotnet/api/system.io.compression.ziparchive?view=net-10.0)
and [DTD prohibition](https://learn.microsoft.com/en-us/dotnet/api/system.xml.xmlreadersettings.dtdprocessing?view=net-10.0).
The document set, metadata policy, limits and lifecycle above are project choices.
