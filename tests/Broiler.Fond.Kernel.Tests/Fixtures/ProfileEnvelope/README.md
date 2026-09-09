# Public storage format candidate vectors

All passphrases, keys, salts, identifiers, and content here are synthetic and
public. They are never production secrets. Never reuse these keys or nonces to
protect real data. The malformed-record tests deliberately reuse public vector
key/nonce material to probe the proposed format grammar.

`v1.json` was produced by the independent Python implementation in
[`eng/Generate-StorageVectors.py`](../../../../eng/Generate-StorageVectors.py),
using Python 3.11 and cryptography 47.0.0 on 2026-09-05. The .NET 10 BCL test
reference verifies every field and the exact ciphertext against the committed
answers. Python and cryptography are **not** kernel, host, or CI dependencies.
Routine tests only consume embedded JSON/XSD; they never regenerate answers.

| Vector | Purpose |
| --- | --- |
| empty-envelope-not-a-zip | Final-record authentication with no data records; invalid as a profile ZIP |
| bootstrap-zip | Fixed ZIP bytes, profile/manifest lexical bytes, empty label and non-ASCII note |
| framing-pattern-65536 | Exactly one full data frame |
| framing-pattern-65537 | Full frame followed by a one-byte tail |
| framing-pattern-131089 | Two full frames and a short tail, for ordering/deletion/splice checks |

Every vector records its passphrase and canonically equivalent decomposed form,
strict normalized UTF-8, header, PBKDF2 master key, HKDF content key, compressed or
framing-only payload, all record descriptors/nonces/AAD/tags/ciphertext hashes,
complete envelope, and final digest. Framing patterns intentionally bypass ZIP
because these vectors test the encryption layer independently of archive parsing.
`bootstrap-v1.xsd` is the trusted schema embedded in the test executable.

Run normal verification from the repository root:

```powershell
./eng/Invoke-CI.ps1 -Scope Full
```

For an intentional format revision, use the optional maintainer command below,
review the ADR and fixture diff together, and independently verify the new bytes:

```powershell
python eng/Generate-StorageVectors.py
```

Do not regenerate expected values merely because a test failed. Compressor
versions may produce different ZIP bytes; a regeneration changes the golden
compressed input and its ciphertext together. Such a change needs explicit
review even when the XML digest and envelope rules remain the same.

The test references are bounded in-memory specifications, not a production
profile store. Full ZIP structural preflight, production streaming, complete M1
schemas, filesystem fault injection, supported-floor KDF calibration and human
security review remain pending in [ADR 0005](../../../../docs/adr/0005-profile-snapshots-v1.md).
