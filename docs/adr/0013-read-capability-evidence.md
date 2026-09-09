# ADR 0013: Read-operation schemas and conservative capability evidence

- **Status:** Accepted (parameter schemas and evidence comparison only)
- **Date:** 2026-09-06
- **Related:** [BPD/UPD evidence](0012-fints-parameter-evidence.md),
  [response interpretation](0011-fints-responses-and-dialogue-correlation.md)

## Source and scope

Reviewed the official [FinTS specification](https://www.fints.org/de/spezifikation),
**Messages – Multibankfähige Geschäftsvorfälle**, release 2022-04-15,
C.2.1.2, C.10.1.3–C.10.1.5 and the corresponding data-dictionary entries.
The source download and SHA-256 are recorded in ADR 0012.

HISALS versions 6/7 carry common balance-query constraints; version 8 adds the
entry-count input flag. HISPAS versions 1–3 carry SEPA-account-query options,
with entry-count input from version 2 and reserved remittance positions in
version 3. UPD signature requirements must meet the advertised BPD minimum.
A zero bank signature minimum can describe anonymous access; it does not
establish an unsigned customer operation. Security-class meanings are specific
to the relevant RDH procedures and must not be treated as PIN/TAN requirements.

`FinTsReadParameterSet.Parse` validates these six parameter-segment versions.
It creates no HKSAL/HKSPA request and parses no HISAL/HISPA account response.
Schema support here is not a connector or bank compatibility claim.

## Advertisement contract

Common fields preserve maximum orders, minimum signatures and raw security
class. Version-specific options remain nullable when absent from that version.
SEPA format identifiers remain ordered raw text, including duplicates and empty
optional positions. They are not fetched, registered as schemas, executed or
interpreted as permission to initiate SEPA payments.

The local bound is 128 recognized-code advertisement occurrences, including
unknown versions. HISPAS preserves up to 99 optional format entries of 256
decoded bytes each. Known-version malformed data fails with fixed diagnostics.
Unknown versions and unrelated segments remain explicitly uninterpreted.
Duplicate advertisements remain separate; the parser does not select a winner.

## Explicit-version comparison

`FinTsReadCapabilityEvidence.Evaluate` compares one account from the same
parameter-set instance, one read operation and one caller-selected version.
It returns all applicable issue flags and the exact-version candidates. It
never chooses the highest advertised version or falls back to another version.

A matching result requires general bank parameters advertising protocol 300,
user parameters, an account connection, matching raw institution context,
a unique listed account permission and one understood bank advertisement.
Duplicate account connections or nonempty IBANs, duplicate permissions,
duplicate bank advertisements, missing evidence and blocked/unknown permission
states remain explicit issues. Comparisons do not normalize source identities.

Additional issues cover a UPD signature count below the bank minimum, zero
advertised order capacity, unsupported single-account SEPA queries, and an
absent IBAN for balance versions 7/8. The single-account matcher deliberately
does not reinterpret an all-accounts-only advertisement. Zero order capacity
is conservatively nonmatching; this field is not given the unrelated general
BPD zero-means-unrestricted rule. A known customer-signature lower bound is
at least one; no signature or SCA requirement has been fulfilled by computing it.

Response errors, contradictory replies, pending authorization, pagination and
uninterpreted reply meanings require review. Only receipt/execution-report
meanings clear this comparison's response-review flag; they still do not prove
authentication or complete parameter collection. Unrelated opaque parameters
remain available on the result's source without being activated.

`HasMatchingEvidence` means the supplied fields are consistent under this
limited comparison. It is not authorization to send a request. Institution/user
authentication, negotiated security procedures, complete and fresh BPD/UPD,
actual IBAN/BIC validity, limits, request-option validation, SCA, response
schemas and compatibility qualification remain pending. No matched result
is stored as an enabled feature or applied to the domain account model.

## Evidence and next work

Five independent public fixtures and 108 checks cover every implemented
parameter version, exact/over-limit fields, future versions without fallback,
scope mismatches, duplicates, signature conflicts, response-review flags and
explicit-version matching. Cancellation and default secret-safe diagnostics
remain supported. Fixtures regenerate with the standard-library Python tool;
normal CI requires only the embedded files and .NET.

Next are PIN/TAN envelope and security-parameter schemas with synthetic
wrapped-message fixtures before authenticated ingestion. The host stays inert;
no credentials, live bank traffic, persistence or payments are enabled.
