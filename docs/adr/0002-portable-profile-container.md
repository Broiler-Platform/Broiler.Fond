# ADR 0002: Portable profile container

- **Status:** Accepted (architecture; wire format not implemented)
- **Date:** 2026-09-04
- **Decision owners:** Product architecture and security

## Context

Local profiles must be portable across current and future hosts without a
database engine or third-party serialization, compression, or cryptography
dependency. A profile will contain several independently versioned logical
documents and sensitive financial data.

## Decision

When persistence is introduced, a profile is represented as multiple XML
documents inside a compressed stream, and that complete compressed stream is
encrypted and authenticated. The implementation uses only .NET 10+ runtime
APIs.

The container layers are:

1. bounded, versioned XML documents written and read with .NET XML APIs;
2. a `ZipArchive` compressed stream containing those documents and a manifest;
3. a versioned outer framing format encrypted in bounded frames with
   `AesGcm`; and
4. key derivation and separation based on passphrase material, random salt,
   PBKDF2, and HKDF using .NET cryptography APIs.

The manifest records the included document names, schema versions, sizes, and
digests. Its document digest list excludes the manifest itself; the outer
authenticated encryption protects the entire archive, including the manifest.

Persistence must not write a plaintext profile or plaintext temporary file.
Writers create and validate a new encrypted generation before an atomic
replacement strategy makes it current. Readers enforce strict XML, archive,
frame, size, and allocation limits before materializing untrusted content.

The exact byte framing, XML lexical/canonicalization rules, key parameters,
recovery-generation lifecycle, purge behavior, and cross-platform test vectors
must be specified and human-reviewed before profile persistence is enabled.

## Consequences

- The logical profile remains inspectable and migratable after authorized
  decryption without binding the kernel to a database format.
- Compression occurs before encryption and no secret-dependent attacker input
  is mixed into a remote compression oracle.
- Authentication detects modification within the opened generation, but it is
  not a forensic guarantee against a key holder, local rollback, or deletion.
- Forgotten passphrases are not recoverable by the product; backups protected
  by the same forgotten passphrase do not solve that condition.
- Secure physical erasure cannot be guaranteed on every filesystem or backup
  medium, so privacy-deletion language must state its limits precisely.

## Milestone 0 effect

No serializer, archive, cryptographic container, key derivation, profile file,
or migration is implemented in Milestone 0. Its storage interface is only a
placeholder boundary.

