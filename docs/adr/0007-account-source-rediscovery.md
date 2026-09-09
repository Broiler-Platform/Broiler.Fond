# ADR 0007: Lossless account sources and exact rediscovery

- **Status:** Accepted (domain planner; connector, durable application and UI pending)
- **Date:** 2026-09-05
- **Related:** [Identity contract](0006-local-identity-allocation.md),
  [roadmap identity and relinking rules](../roadmap.md#collision-free-local-identity-and-source-ambiguity)

## Source locator contract

`AccountSourceLocator` captures the local institution and connection IDs,
connector name and version, configured endpoint, raw IBAN, domestic account
number/bank code, subaccount marker, currency, and every scoped bank-specific
identifier. Institution and connection IDs are distinct and nonzero. The endpoint
is the canonical AbsoluteUri of an already validated manual configuration, not
an endpoint supplied by the bank response. Configuration review/authentication
state and the institution's friendly label are separate from source identity.

Raw account strings retain case, whitespace, leading zeros, Unicode representation
and the distinction between missing and empty. No IBAN checksum, currency support,
account-type support, ownership or bank-authenticity claim follows from a locator.
The future protocol parser must validate encoding and supported input syntax;
the locator itself only bounds and preserves supplied strings.

Each `BankAccountIdentifier` retains issuer, scope, kind and exact value. Its
original list order and multiplicity are preserved in an immutable copy. Exact
comparison treats that list as a multiset using ordinal comparison of all four
fields; a bank changing field order alone does not change identity. Removing a
duplicate does change the tuple. Different values for the same issuer/scope/kind
are flagged as conflicting and cannot establish automatic rediscovery.

Every scalar is bounded to 1,024 UTF-16 code units, and each locator holds at most
32 bank identifiers. The endpoint retains the manual-configuration limit. Issuer,
scope, kind, connector and connector version must be nonblank. Empty identifier
values are retained but do not supply identity evidence. These limits do not
replace the future parser's byte/encoding limits or qualify archive schemas.
Default string representations and validation errors do not print raw identifiers.

The complete tuple participates in equality and hashing. Hashes are process-local
lookup aids only; even a collision must compare the full tuple. No hash is a local
entity ID or a persisted replay key.

## Inputs and output

`KnownAccountBinding` retains separate account, incarnation and binding IDs, its
historical locator, and Active/Closed/Quarantined state. An
`AccountDiscoveryOccurrence` retains its independently allocated ObservationId,
source locator and any explicit bank-reported new-lifetime/closed evidence.
The planner never creates an ObservationId, and a value wrapper alone is not
proof that the connector/store persisted an occurrence correctly.

`AccountRediscoveryPlanner.Plan` operates on a complete bounded batch and returns
one immutable decision for every occurrence, in input order. It has no file,
network, credential, allocator, balance or relinking side effect. A decision can
reuse one existing binding or propose a new/quarantined account candidate. It
never invents an account/incarnation/binding ID for a candidate. Review pools are
ordered by local binding ID for deterministic presentation.

Before matching, reject duplicate binding IDs, duplicate occurrence IDs,
cross-kind local-ID collisions, an incarnation assigned to different accounts,
or a connection assigned to different institutions. Same account/incarnation IDs
can legitimately appear across distinct historical bindings. This validation is
additional to the full identity inventory; it does not substitute for profile
lineage or complete entity-reference validation.

## Decision order

For each occurrence, the first applicable quarantine rule is the summary reason.
All original evidence, including lifetime flags, remains on the occurrence.

| Condition | Decision |
| --- | --- |
| No nonblank IBAN, domestic account/bank-code pair, or scoped bank identifier | Quarantined: insufficient identity, even if one tuple happens to match |
| Conflicting values for the same scoped bank identifier | Quarantined: conflicting bank identifiers |
| More than one occurrence has the exact tuple in this batch | Quarantine every occurrence; preserve multiplicity even without a known binding |
| Bank explicitly reports a new lifetime or closure | Quarantined; exact bytes cannot revive or overwrite the old incarnation |
| More than one historical binding has the exact tuple | Quarantined: ambiguous bindings; do not pick the first or prefer Active silently |
| Exactly one matching binding is Closed or Quarantined | Quarantined: inactive binding |
| No exact match, but related review candidates exist | Quarantined: changed or conflicting locator |
| Exactly one matching binding is Active and none of the above applies | Reuse the exact same account, incarnation and binding IDs |
| No known identity or related candidate | Propose a separate new account; still no allocation or automatic activation |

Distinct exact tuples can reuse distinct bindings even if they share a weaker
identifier. Each exact tuple must occur only once in the batch, so one historical
binding cannot be consumed by two duplicate-looking rows. Matching is independent
of row order. Missing rows do not close accounts, erase cached data or indicate
that a bank has stopped reporting an account.

## Review candidate lookup

When no exact tuple matches, candidate lookup may use an IBAN with ASCII spaces
removed and invariant uppercasing, a lossless domestic pair within the local
institution, or an exact scoped bank field within the institution. These indexes
only suggest review. Their equality can never select a binding for reuse.

If no account anchor is shared but the institution already has known bindings,
the planner conservatively offers those bindings as a review pool. This can also
quarantine a genuinely new account at an existing institution; the UI must allow
the user to keep it separate. A review pool is not a claim that two accounts are
probably the same. Account names, balances, owner similarity and transaction
similarity are not matching evidence in this planner.

This approach quarantines changed connections, connector versions, endpoints,
IBANs, domestic identifiers, subaccounts, currencies or scoped fields when prior
evidence exists. With neither a shared anchor nor an existing institution there
is no basis to infer that a changed locator belongs to an old account; it remains
a separate new candidate and never overwrites one.

## Integration obligations and limits

The connector/application must gate bank access on endpoint confirmation, fresh
authentication and bank-advertised capabilities. A locator can be constructed
from an unconfirmed configuration for historical/domain inspection; a ReuseBinding
recommendation does not authorize contact or prove an authenticated response.

The caller must collect the complete relevant discovery batch before planning,
including pages needed to preserve duplicate multiplicity. Do not plan each row
or page independently and immediately apply results. Durable request/page replay
evidence must be checked separately; identical bytes, row ordinals or reusing an
ObservationId are not a replacement for that evidence.

Before applying any decision, recheck that the immutable account/connection
snapshot used for planning is still current. A closed binding, endpoint edit or
concurrent update invalidates an old recommendation. The future coordinator must
atomically commit occurrences, full entity payloads, local ID allocation, binding
changes and decision provenance under the exclusive profile/session boundary.
Neither stale-plan application nor durable commits are implemented here.

A relink needs explicit user review, stored evidence, algorithm version, time and
an undoable history. A relink changes bindings, never old observation or balance
provenance. The planner intentionally performs no relink and calculates no totals.
The future UI must expose quarantined observations separately and follow the
roadmap's uncertainty rules when producing reports.

If a bank silently reuses a locator and the client never received closure or
new-lifetime evidence, exact bytes alone cannot reveal that fact. This planner
does not claim impossible certainty about such indistinguishable lifetimes.
Additional owner/type/continuity evidence and supported bank semantics belong to
the connector/domain validators before the first live compatibility row.

Limits are 10,000 known bindings, 10,000 occurrences and 100,000 candidate links
per plan. Cancellation or a limit/identity failure returns no partial plan and
changes no input. Limits are resource ceilings, not release-performance claims.

## Verification

The BCL-only test runner adds 90 checks covering raw data preservation, multiset
equality and scope, forced hash collisions, each context/locator change, exact
rediscovery, duplicate occurrences, closed/reopened evidence, ambiguous bindings,
missing rows, bank-specific-only identity, immutable inputs/results, local-ID
conflicts, cancellation and bounds. A complete 10,000-account exact-match batch
passes. An integration check uses the identity ledger to allocate all six local
identities and proves planning preserves the account/incarnation/binding IDs
without advancing the ledger.

No bank fixture, banking capability, persistence operation or balance update is
introduced. Connector replay/continuity evidence, full schemas, stale-plan commit
coordination, user review and undoable relinking remain pending.
