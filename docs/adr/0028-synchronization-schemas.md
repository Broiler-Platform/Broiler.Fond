# ADR 0028: Credential-free synchronization schemas and unsigned encoding

- **Status:** Accepted (schema observations and local unsigned encoding only)
- **Date:** 2026-09-08
- **Related:** [Initialization schemas](0025-unsigned-dialogue-initialization.md),
  [unsigned read encoding](0024-unsigned-read-request-encoding.md)

## Sources and scope

Reviewed Formals, 2017-10-06, C.8–C.8.2.2 (printed pages 66–70), B.4 and the
synchronization-mode/security-reference data-dictionary entries from the
[official FinTS specification](https://www.fints.org/de/spezifikation).
The PDF SHA-256 is
`6b4809acd43acd2c6166c486964dee4b84b488a6a6902b29d1221458eaed7239`.

`FinTsSynchronizationRequest` parses HKSYN version 3 with exactly one mode:
0 for a new system identifier, 1 for the last processed message number, or
2 for signature security references. Unknown modes, unsupported versions,
segment references and malformed known fields fail with fixed diagnostics.

These are observations, not recovery instructions. The specification restricts
signature-ID synchronization by security profile, advises against automating
message-number recovery, and requires ending a synchronization dialogue and
initializing a new one before sending business requests. This increment sends
nothing and does not implement or waive those requirements.

## Returned values and unresolved shapes

`FinTsSynchronizationReport` parses HISYN version 4 with a required request
segment reference and up to four positional fields:

- system identifier: up to 30 decoded characters;
- last processed message number: canonical positive integer from 1 to 9999;
- signing-key security reference: canonical integer of up to 16 digits;
- digital-signature security reference: separate canonical integer of up to
  16 digits, whose requirement depends on the security profile.

Both security references use nullable `ulong` values, preserving every digit
through `9999999999999999` without floating-point rounding. Zero remains a
reported numeric value, distinct from an omitted field. No value is incremented,
merged across institutions or applied to a signature/counter allocator.

The report's `Shape` describes which fields are present. Empty reports remain
empty. System ID, message number and signature-reference groups are distinct;
multiple groups, or a digital-signature reference without a signing-key reference,
remain `Conflicting`. No winner is chosen. Shape does not assert that the report
matches a request mode, uses a permitted profile or proves execution.

For example, system identifier `0` remains a source string for later assignment
checks. A signature-reference shape with one or two counters still requires
request/profile comparison. The report keeps the exact source segment, including
trailing empty fields and original wire spelling.

`FinTsSynchronizationDataSet` preserves missing and duplicate reports without
inventing an outcome. Unknown HISYN versions and unrelated segments stay opaque
in source order. Known malformed HISYN-4 reports fail rather than becoming opaque.
At most 128 synchronization segments are accepted, including unknown versions;
existing frame and response limits also apply.

## Restricted unsigned request context and writer

`FinTsUnsignedSynchronizationRequest` accepts exactly five segments: HNHBK-3,
HKIDN-2, HKVVB-3, HKSYN-3 and HNHBS-1, numbered 1–5. The frame uses dialogue
`0` and message number `1`, with no populated response reference, security
wrapper, signature or business segment.

Anonymous identification is rejected. System-ID mode requires the caller's
system identifier to be `0` and system status to be `Required`. Other mode/profile
requirements remain pending. Existing initialization field validation applies.

`FinTsUnsignedSynchronizationWriter` takes explicit initialization input and mode,
reuses the internal identification/preparation body builder, appends HKSYN and
validates the finished frame before returning caller-owned bytes. Existing
initialization wire fixtures remain unchanged. No product identity, key, signature
sentinel, registered profile or authentication is supplied by the writer.

The same strict Latin-1 text restriction, delimiter escaping, invariant numbers
and exact 12-digit byte length apply. Cancellation is checked at public entry,
during dataset traversal and final frame/schema parsing. Default diagnostics
exclude identifiers and counters; explicit wire/source properties remain untrusted
private data unsuitable for logging. No secure credential storage or erasure
guarantee is added.

## Verification and next work

Ten independent Python fixtures cover all request modes, escaped system IDs,
maximum message and signature values, separate signature counters, empty and
conflicting fields, duplicates, unsupported response versions and missing reports
on an error response. The executable suite adds 165 checks for exact encoded
bytes, source identity, field bounds, malformed numerics/binary fields, context
isolation, 128/129 report limits, cancellation and culture independence.

Next is synchronization response-context comparison, including mode/profile
requirements and the close/reinitialize boundary. Authenticated security,
explicit recovery workflows, transport, durable replay and domain ingestion
remain pending. The host stays inert.
