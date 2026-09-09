# ADR 0018: Account-discovery and balance schemas

- **Status:** Accepted (credential-free schema observations only)
- **Date:** 2026-09-07
- **Related:** [Read capabilities](0013-read-capability-evidence.md),
  [responses](0011-fints-responses-and-dialogue-correlation.md),
  [account values](0008-account-values-and-exact-money.md)

## Sources and supported versions

Reviewed the official [FinTS specification](https://www.fints.org/de/spezifikation):

- **Messages – Multibankfähige Geschäftsvorfälle**, 2022-04-15,
  B.1–B.6, C.2.1.2 (printed pages 45–54), C.10.1.3–C.10.1.5
  (printed pages 378–387). PDF SHA-256:
  `a3db32dc27b596f67dea0d4447d07011d8945dc589aafcacb869018ea7d94afd`.
- **Formals**, 2017-10-06, B.4.1–B.4.2 for canonical unsigned comma
  amounts, currency and calendar formats. PDF SHA-256:
  `6b4809acd43acd2c6166c486964dee4b84b488a6a6902b29d1221458eaed7239`.

`FinTsReadRequest.Parse` observes an existing unsigned business segment:
HKSPA version 1 or HKSAL versions 6/7/8. Unsupported request versions fail
explicitly. `FinTsReadDataSet.Parse` reads HISPA version 1 and HISAL versions
6/7/8 from an existing `FinTsResponse`, retaining the exact response and segment
objects. Unsupported response versions and unrelated segments remain opaque.

The Messages tables under discovery versions 2/3 label the HISPA response
`Version: 1` and `Anzahl: 2` or `3`. This conflicts with an assumption that
those headings establish HISPA versions 2/3. Discovery versions 2/3 remain
unsupported until authoritative clarification and pagination-layout review.
The existing HISPAS parameter reader's wider version coverage is evidence
parsing, not a promise of an available request/response codec.

## Preserved observations

- National account tuples retain account number, subaccount, country and
  institution exactly. International tuples allow international, national or
  combined identifiers. Populated international identifiers require both
  IBAN and BIC; populated national tuples require account number and country.
  SEPA discovery J requires IBAN/BIC, while N requires those fields empty.
  Non-SEPA account shells and duplicates survive. Identifiers are never
  normalized, checksum-validated, deduplicated or allocated a local identity.
- Empty HKSPA-1 means all accounts. HKSAL always requires a supplied account,
  including when its all-accounts flag is J. Optional entry counts are positive
  canonical integers up to 9999. Continuation tokens preserve up to 35 decoded
  bytes. Parsing grants no permission to use either option.
- Booked and pending balances remain separate signed observations. Unsigned
  amounts use the canonical comma grammar, at most 15 decoded characters,
  and exact `decimal` values with no rounding. C/D remains separately visible,
  including D with zero. Three-uppercase-letter source currencies do not
  imply a catalog match or currency conversion.
- Credit line, available, already used, overdraft and version-8 amount
  garnishable from month change remain separate optional amounts. Missing
  fields stay null. An overdraft requires an explicit zero available amount.
  Currency disagreements are retained and flagged by `HasCurrencyConflict`;
  callers must resolve them before domain ingestion, despite the specification
  requiring account currency for each amount. No totals or available amount
  are derived from another balance or credit field.
- Source balance timestamps, booking timestamps and optional due dates remain
  distinct. Calendar dates and optional HHmmss times are validated. No timezone,
  trusted retrieval time, freshness or trusted expiry is invented.
- Original HIRMG/HIRMS status remains on `Source`. An empty report or bank error
  creates no balance. A partial response creates no completeness assertion.
  Request references are structural observations, not an account/session match.

## Bounds and trust boundary

Discovery admits at most 999 account repetitions per segment. The response
set admits at most 128 read reports (including unsupported versions) and 1024
total account observations across discovery and balances. Existing byte,
segment, field and component budgets apply first. Cancellation is checked on
entry and during report/account iteration. Invalid known schemas fail without
returning a partial set. Errors and default object diagnostics contain no
source account fields. Raw source objects and explicit properties still contain
untrusted data and must not be logged or treated as credential containers.

Only trailing empty optional repetitions are accepted; interior holes are
rejected. Optional groups may retain empty components within their shape bound.
No ambiguous repeated-field pagination layout is inferred.

Country-specific bank-code rules, IBAN/BIC validity, UPD subaccount consistency,
national-account permissions, entry-count permission, continuation provenance,
account-type due-date rules and request-to-report/account matching remain
context work. Full message/credential writers, authenticated sessions, durable
replay handling and domain ingestion are not implemented. The host stays inert.

## Verification and next increment

Eight independently generated Python fixtures contain unsigned request/response
frames and explicitly selected .NET-independent expectations. The executable
suite additionally tests malformed and conditional fields, exact decimal/date
boundaries, absent values, duplicate accounts/reports, unknown versions,
resource boundaries, error/partial provenance, cancellation and fixed diagnostics.
Existing fixture files reproduce byte-for-byte.

Next: pure read-request/response context evidence that compares exact versions,
request references, account tuples, capabilities and partial-page provenance
before any authenticated domain ingestion. Passing that comparison must still
not authorize transmission or persist a balance.
