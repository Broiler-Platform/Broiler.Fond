# ADR 0022: Bounded all-account discovery evidence

- **Status:** Accepted (pure synthetic comparison only)
- **Date:** 2026-09-07
- **Related:** [Read schemas](0018-account-discovery-and-balance-schemas.md),
  [selected-account context](0019-read-request-response-context.md),
  [unavailability](0021-scoped-read-unavailability.md)

## Source and scope

Reviewed Messages, 2022-04-15, C.10.1.3, printed pages 378–380, from the
[official FinTS specification](https://www.fints.org/de/spezifikation).
The PDF hash is in ADR 0018. HKSPA-1 can request all account connections by
omitting the optional account list. The single-account-request flag governs
whether selected-account queries are supported. Subaccount identity must
remain consistent across UPD and discovery reports.

`FinTsAllAccountDiscoveryEvidence` compares one existing unsigned HKSPA-1
all-account request, one parsed response and a caller-selected parameter set.
It is separate from selected-account capability/context and attempt APIs.
Those APIs retain their previous restrictions; all-account balance requests
remain unsupported here. Discovery versions 2/3 remain opaque as in ADR 0018.

This comparator sends nothing, authenticates nothing and changes no local
account identity, binding, cached value or endpoint. It creates evidence rows,
not account entities or an authoritative inventory.

## Whole-response checks

Bank and user parameters must be present, FinTS 300 advertised, and their
response status suitable for evidence comparison. Parameters must precede the
expected response in the same dialogue. Exactly one HISPAS-1 advertisement
with nonzero order capacity is required. Its single-account flag may be J or N;
an all-accounts-only advertisement does not need to permit a selected request.

The all-account request must contain no selected account entries. Explicit
trailing empty optional fields preserve this logical shape. Existing frame
and restricted unsigned-request parsing rules apply.

Message, segment reference, profile, status and unavailable/partial conflict
rules are shared with selected-account comparison. No status vocabulary is
duplicated or broadened. Exactly one supported HISPA-1 report is expected,
unless a valid scoped 3010 reports unavailability. Unknown segments, balance
reports, duplicate discovery reports and unsupported versions require review.
HKSPA-1 pagination remains unsupported.

## Returned-account matching

Every returned account produces one row in source order, including non-SEPA
shells, unknown accounts and duplicates. The matcher builds indexes over the
supplied UPD national tuples and nonempty IBANs. National keys are structured
tuples of account number, subaccount, country and institution, not concatenated
strings. Identifier text is compared ordinally without normalization.

For each returned row, the candidate set is the union of exact national-tuple
matches and exact nonempty-IBAN matches. Candidates remain in original UPD
order. This preserves both candidates when an IBAN points at one UPD entry
and the national tuple points at another; neither identifier overrides the other.

- No candidate produces `UnknownAccount`.
- Multiple candidates produce `AmbiguousUserAccount`.
- A candidate with a missing or different national tuple, or a conflicting
  previously known IBAN, produces `IdentityConflict`.
- Repeated returned national identities or nonempty IBANs mark every affected
  row `DuplicateReturnedIdentity`, even when other identifiers differ.
- An institution different from the supplied bank parameter identity produces
  `InstitutionMismatch`.
- A unique candidate needs explicitly listed HKSPA permission. Unknown,
  blocked or duplicate permission evidence produces `PermissionNeedsReview`.
  Its reported signatures cannot be below the advertisement minimum; the
  customer-signature lower bound remains at least one.

A previously empty UPD IBAN can acquire a matching discovery observation;
the original parameters are not edited. A non-SEPA report cannot silently
clear a previously populated IBAN.

`MatchedAccount` exposes a unique row candidate only when its row issues are
clear. This is a row-level observation. Callers must also inspect the result's
global `Issues` and `ResponseIssues`; missing parameters or response problems
can still make the overall outcome `NeedsReview`. No row grants ownership,
signing permission or activation.

## Unmatched accounts and absence

`UnmatchedUserAccounts` preserves every supplied UPD entry without a clean
returned row. A normal execution report with unmatched entries requires review.
An unavailable response retains that list without interpreting the absence as
closure, revocation or deletion. An empty report without scoped 3010 still
requires review. Returned values combined with unavailable status conflict.

No account is deduplicated, relinked, quarantined in persistent state or removed.
The result has no completeness or freshness guarantee, even when its supported
observations match. Repeated evaluation consumes no replay state.

## Bounds and verification

Existing schema limits bound UPD to 512 accounts, individual discovery reports
to 999 account repetitions and the response set to 1024 account observations.
Indexes preserve candidate order. At most 4096 total candidate links may be
returned; exceeding that limit fails with a fixed resource error rather than
truncating ambiguity or returning a partial result. Cancellation is checked
through account/index/candidate iteration. Default diagnostics contain no
account identifiers; explicit source objects and candidate lists remain private
untrusted banking data unsuitable for logging.

Eight independent Python fixtures cover known SEPA/non-SEPA accounts, unknown
returned accounts, duplicates in the response or UPD, conflicting identifier
matches, omitted known accounts, missing permission and unavailable discovery.
The executable suite adds 105 checks, including 512 clean matches, 999 unknown
returned accounts, the exact/over-limit candidate budget, source order and
identity, status/reference failures and continued selected-account restrictions.

Next: integrate all-account discovery with an explicit local attempt lifecycle,
response consumption, cancellation and timeout. Authentication, durable replay,
domain reconciliation and live transport remain pending; the host stays inert.
