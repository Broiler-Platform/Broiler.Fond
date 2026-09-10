# ADR 0050: Initialization read-capability integration

- **Status:** Accepted (local synthetic evidence only)
- **Date:** 2026-09-10
- **Related:** [Read capabilities](0013-read-capability-evidence.md),
  [read request context](0019-read-request-response-context.md),
  [initialization requirements](0048-assembled-initialization-hipins-requirements.md),
  [first-read signature context](0049-first-read-pin-tan-signature-context.md)

## Explicit initialization integration

`FinTsPinTanInitializationRequirementsEvidence.EvaluateWithReadParameters` adds
an explicit read-schema path. Its `ReadParameters` property retains the supplied
`FinTsReadParameterSet`; the original requirements API leaves that property null.
Read, PIN/TAN and TAN parameter sets must share the exact `FinTsParameterSet`,
with permission reports on that same bound response. A separately parsed tree,
even on the same response object, produces SourceMismatch. No source tree is
copied, filtered, rewritten or replaced.

The existing HISALS 6/7/8 and HISPAS 1/2/3 schemas provide the interpretation rules
reviewed against the official FinTS specification in ADR 0013. Assembled response
reference binding now recognizes those versions at HKVVB segment 4. Known read
advertisements therefore no longer carry UninterpretedData solely because of
their code. Unknown versions remain unresolved; absent or old references fail
scope checks. One older initialization fixture updates its expected reference
diagnostic accordingly; its legacy semantic outcome still requires review.

The explicit initialization path recognizes only exact successfully parsed read
advertisement segments alongside HITANS and HIPINS. All previous identity, status,
parameter and procedure requirements remain in force. The original requirements
API still treats read advertisements as unresolved parameters. A matching
initialization result with read schemas establishes no selected account capability;
duplicates, absent selected versions and operation-specific requirements are
evaluated by the account comparator below.

`FinTsPinTanInitializationAttempt.AcceptReadParametersResponse` returns these
schemas within the existing requirements handoff. It shares the pending candidate,
deadline, scalar requirements diagnostics and terminal cleanup. Foreign reference
scope remains pending; mixed sources terminate for review. All four acceptance
APIs share one-time handling, including rejection of a later attempted upgrade
after the older requirements API has handed out a review result. Late cancellation,
expiry or clock failure withholds all components and releases pending metadata.

## First-read account comparison

`FinTsPinTanReadCapabilityContext.Evaluate` takes the first-read signature context,
one exact returned account and an optional explicit national-connection advertisement.
Missing integrated read schemas or an account from a different parameter tree
cannot produce a capability component. No account is selected implicitly.

The comparator reuses the existing read-capability checks for the request's exact
operation and version: bank/user data, institution identity, account connection,
IBAN requirements, listed permissions, duplicate account identities or permissions,
advertisement uniqueness, nonzero order capacity and signature lower bounds.
It refines the generic response-status vocabulary only when the complete
initialization requirements match and retain this exact read-parameter set.
The public generic capability comparator is unchanged and still requires review
for the initialization's 3920 observation. No issue flags are broadly masked.

Existing request checks enforce one selected account, source identity equality,
same-dialogue parameters, advertised entry-count input and an explicitly sourced
HISPAS national-connection option for applicable balance requests. All-account
discovery/balance requests remain outside this comparator. An absent permission
cannot supply an inferred signature requirement. The local single-header path
requires a computed lower bound of exactly one; the existing rule still computes
at least one when both explicit UPD and bank minima are zero. A UPD count below
the bank minimum remains contradictory. Account or operation limits require review
because their application has not been implemented.

Only a completely matching outer result exposes the exact selected account,
read advertisement and qualified HIPINS TAN flag. Matching inner signature or
capability components alone cannot qualify the whole result. N grants no permission
or SCA exemption. Raw inputs and detailed request/capability issues remain available
for review, including when qualification fails.

## Verification and remaining work

Forty-three independent Python fixtures and 390 checks cover both profiles,
TAN versions 6/7, supported read schemas, exact sources, unknown/blocked/duplicate
permissions, missing or duplicate advertisements/accounts, signature conflicts,
limits, account mismatches, request options, unknown data and reference scope.
Additional checks cover identical-byte source mixing, absent read schemas, nested
component qualification, all acceptance paths, late failures, cancellation,
concurrency and one-time lifecycle handoff. All values are public synthetic data.

Next is first-read PIN/TAN credential requirement comparison. Applying credential
bounds, signed read assembly, all-account capability integration, read response
binding, broader session/SCA coordination, secure input, authenticated transport
and live qualification remain pending. No credentials are read, request sent,
capability activated or domain account updated. The host stays inert.
