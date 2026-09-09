# ADR 0032: Credential-free PIN/TAN signature-header schemas

- **Status:** Accepted (restricted synthetic schema only)
- **Date:** 2026-09-08
- **Related:** [PIN/TAN response envelopes](0014-pin-tan-envelope-and-parameters.md),
  [synchronization and closing](0031-in-memory-synchronization-and-closing.md)

## Sources and scope

Reviewed these primary documents from the
[official FinTS specification](https://www.fints.org/de/spezifikation):

- Security – Sicherheitsverfahren PIN/TAN, 2020-07-10, B.7–B.9, especially
  printed pages 73 and 77–79, and the HNSHK example in the message examples.
  SHA-256: `77bb5724cb391434cd7188924a1ff89bff040bf340dc49a09c92cdeb4ee7dc3a`.
- Security – Sicherheitsverfahren HBCI, 2024-06-11, B.5.1 (pages 31–33) and
  the data dictionary for header fields (pages 87–88 and 96–101).
  SHA-256: `37a82f7e51386f605154f1d5f47d64dc97ca3cab7161bc8769b9c7ca0f32735e`.

`FinTsPinTanSignatureHeader` parses one HNSHK version 4 segment. Its schema
contains no PIN or TAN fields. It does not implement HNSHA, construct an entire
request, select a permitted procedure, hash or sign data, or authenticate a
message. Existing unsigned contexts and the PIN/TAN response-envelope parser
continue to reject signature segments in their restricted layouts.

The PIN/TAN-specific rules override the generic HBCI profile/function and
algorithm restrictions where explicitly stated. The parser supports only the
subset below; successful parsing is not conformance approval for a live request.

## Supported fields

| Field | Schema rule and retained meaning |
| --- | --- |
| Segment header | HNSHK-4 without a segment reference; source number preserved without assuming a position in a complete message |
| Profile and function | PIN:1 requires 999; PIN:2 requires a three-digit procedure code from 900 through 997 |
| Control reference | Required text, up to 14 characters, excluding the literal `0`; remains text, so `00` is not converted to zero |
| Security application area | Fixed value 1 |
| Supplier role | 1, 3 or 4; no multi-signature role or authorization qualification |
| Security identification | Party 1 or 2, empty CID, required system identifier up to 30 characters |
| Security reference number | Canonical unsigned decimal, at most 16 digits, retained exactly as `ulong`, including zero |
| Security timestamp | Qualifier 1; optional valid calendar date and optional time, with time requiring a date |
| Hash group | Usage 1, code 3/4/5/6/999, parameter identifier 1, no populated parameter value |
| Signature group | Usage 6; two required numeric code fillers of at most three digits, preserved lexically |
| Key name | Three-digit country, institution up to 30 characters, required user up to 30 characters, key type S, numeric key number/version 0–999 |
| Certificate | Omitted or one empty nonbinary field only |

The institution component may be empty at this schema boundary. All text is
printable Latin-1; the local subset rejects leading/trailing ASCII spaces and
controls while preserving internal spaces and escaped delimiters. No trimming,
normalization or identifier lookup occurs. Key numbers and versions, unlike
code fillers, use canonical numeric syntax without leading zeros.

HNSHK has eleven required fields after its segment header, plus the optional
certificate position. Group counts are checked exactly or against their defined
optional tail. Omitted and explicitly empty timestamp/hash/certificate positions
remain distinguishable through the source segment. Binary substitutions,
including zero-length binary values in forbidden fields, fail.

The PIN/TAN example uses hash code 999 as a placeholder. It is accepted here
alongside the current HBCI dictionary's 3–6 codes as a structural observation;
this does not enable those algorithms or authorize 999 in an HBCI signature.
Other hash codes require unsupported-schema handling. PIN/TAN defines signature
algorithm and operation mode as fillers, so their exact numeric code text is
preserved without applying the RAH algorithm restrictions. Key types D/V and
other profiles or versions remain outside this subset.

## Evidence boundaries

System ID `0` can occur while synchronizing a new system ID. The parser retains
it, and other unresolved identifiers such as `unbekannt`, for later request
context checks. It does not establish that a system identifier was assigned by
the bank, belongs to the user or is appropriate for a specific operation.

PIN/TAN does not rely on signature IDs for duplicate-submission protection.
The 16-digit field remains an exact schema observation, with no counter increment,
allocation, reserved-value interpretation or replay guarantee. A reported
timestamp provides no trusted clock, expiry or freshness evidence.

PIN:2 syntax does not prove the procedure was advertised in HITANS, permitted
for this user by 3920 or appropriate for the pending operation. PIN:1/999
likewise provides no permission or SCA exemption. These are future comparisons
against explicitly supplied request and user context.

The object retains the exact immutable source segment. Parsing is repeatable
and consumes no evidence. Fixed format errors and default diagnostics exclude
source values. Raw parser buffers may still contain other data from their source
document; they are not credential containers and must not be logged. No real
credential handling or zeroization guarantee is introduced.

## Verification and next work

Ten independent Python fixtures cover both profiles, procedure bounds, roles,
parties, escaped identifiers, optional timestamps, leap dates, maximum fields,
zero system/reference observations, alternative fillers and exact 16-digit values.
The executable suite adds 295 checks for all exposed fields, malformed layouts,
unsupported profiles/versions/key types/hash codes, forbidden binary fields,
calendar validity, ownership, culture independence, null input and cancellation.

Next is pure request-bound PIN/TAN signature-header comparison, including
explicit identity/system and permitted-procedure context. Credential ownership,
HNSHA and full request security, envelope integration, authenticated sessions,
transport and durable replay remain pending. The host stays inert.
