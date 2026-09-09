# ADR 0008: Exact money and trustworthy account-value projections

- **Status:** Accepted (domain projection; live ingestion, persistence and UI pending)
- **Date:** 2026-09-05
- **Related:** [Identity allocation](0006-local-identity-allocation.md),
  [account rediscovery](0007-account-source-rediscovery.md)

## Exact money and currency recognition

`Money` is an immutable validated reference value containing a decimal amount
and an explicitly recognized currency. The M0 struct is replaced so a default
zero-initialized struct cannot masquerade as valid money with no currency.
Missing money is represented by null. Constructors do not accept floating-point
inputs, round to minor units, trim/fold currency codes, or silently convert units.

`CurrencyCatalog` freezes the alphabetic codes in SIX ISO 4217 List One published
2026-01-01, retrieved 2026-09-05. The project excludes the test/no-currency markers
XTS and XXX from monetary values. Recognition includes the listed currency/fund
codes; it is not a claim of bank support, account-type support, payment eligibility,
or available exchange rates. Unknown, historical-only, lowercase or padded codes
are rejected. The original source locator/observation must remain available for
an unsupported code; the application must not silently delete or convert it.

The reference is [SIX List One XML](https://www.six-group.com/dam/download/financial-information/data-center/iso-currrency/lists/list-one.xml),
linked from the [maintenance agency's standards page](https://www.six-group.com/en/products-services/financial-information/market-reference-data/data-standards.html).
There is no installed-client currency lookup or background refresh. Future schema
versions must retain their applicable currency reference when codes change; this
increment does not implement historical-currency migration or archive reading.

`Money.ParseExact` accepts bounded invariant decimal text: optional minus, one or
more ASCII integer digits, optional dot and 1–28 fractional digits, at most 64
characters. Integer leading zeros except the single zero, a plus sign, whitespace,
group separators, exponent notation and non-ASCII digits are rejected. Negative
zero is numerically zero. Redundant trailing fractional zeros may be removed only
when that preserves the exact value. Original protocol lexical text belongs to
the immutable observation; this parser is not a FinTS amount codec or localized UI
parser. A caller supplying an already rounded decimal cannot recover its original
precision, so raw string ingestion must use the exact parser after protocol decoding.

Decimal arithmetic can lose precision at representability boundaries; see the
[.NET decimal contract](https://learn.microsoft.com/en-us/dotnet/api/system.decimal?view=net-10.0).
The implementation accumulates signed `BigInteger` coefficients in units of
10^-28, then converts only if the result fits the 96-bit decimal coefficient
without rounding. `Money.Add` rejects different currencies and throws on an
unrepresentable exact result. Source precision such as EUR 1.001 is preserved;
payment minor-unit validation remains a separate later operation.

## Immutable balance provenance

`BalanceSnapshot` now carries distinct snapshot, observation, account, incarnation
and binding IDs; kind; nullable money; optional bank timestamp; retrieval timestamp;
the bank/source stale marker; and an optional lossless source-type label. Unknown
and Other kinds require that label. Labels are capped at 1,024 code units.
Timestamps and their original offsets remain unchanged, including anomalous values
that must be visible for diagnosis rather than rewritten.

Snapshot IDs identify balance entities. One observation can supply multiple
balance kinds for the same provenance, but cannot be assigned conflicting
account/incarnation/binding origins. Duplicate snapshot IDs must be resolved by
durable replay logic before projection; equal amounts or timestamps do not prove
replay. Constructors validate structural IDs, not their durable allocation or bank
authenticity. No raw PIN, TAN or connector response text is stored in these models.

## Projection contract

`AccountValueProjection.Create` accepts a bounded selection of accounts, an
explicit evaluation instant, a positive maximum age, and exactly one requested
kind: Booked or Available. Each immutable input includes the current selected
binding, a capability classification, current candidate snapshots, refresh outcome
and offline status. It is not an unfiltered historical snapshot list.

The result exposes the evaluation instant, age policy and kind. It does not read
the system clock, initiate refresh, select a newest-by-timestamp winner, alter
history, reconcile observations, allocate IDs or persist anything. The host must
reproject as time/state changes; yesterday's computed IsCurrent is not perpetual.

For each selected account:

- identity-only unsupported accounts and inactive bindings yield no usable value;
- an unrecognized account currency remains a visible excluded row;
- zero candidates of the requested kind means unavailable;
- multiple candidates of that kind remain ambiguous, even if their values match;
- one candidate must match all three selected account/incarnation/binding IDs;
- a null money value stays unavailable, and a currency mismatch stays excluded;
- only the remaining unique compatible candidate exposes its Money value.

Every row retains the input and candidate evidence. A single excluded source can
still be inspected through Source, but Value is null. An explicit bank-supplied
zero remains Available. Booked values never fall back to available values, credit
lines or unknown source types. CreditLine/Other/Unknown snapshots retain their
distinct semantics but are not inputs to the asset/available total operations.

SupportedCurrentAccount is a caller-supplied domain classification, not evidence
that a live compatibility row has passed. The future connector/application must
derive it from validated account type and bank capabilities. No institution is
made supported by this increment.

## Freshness and failures

Freshness uses independent flags, so several warnings can be retained together:
source marked stale, retrieval expired, bank timestamp expired, bank time missing,
future timestamp, bank time after retrieval, failed refresh, partial refresh and
offline mode. Expiration starts at age greater than or equal to the explicit
maximum. Timestamp comparisons use UTC instants while preserving source offsets.

A recent retrieval does not make an old bank timestamp current. A missing bank
time is unknown, not substituted with retrieval time. Future or inconsistent
timestamps do not become fresh merely because the age calculation is negative.
Flags do not erase a uniquely supported cached amount. IsCurrent requires an
Available row with no freshness flags. Failed/partial/offline refresh therefore
keeps good cached data visible with warnings. These flags do not implement the
connection/SCA state machine or last-attempt/contact history.

## Totals and exclusions

Produce separate, deterministically ordered currency groups for the requested
balance kind. Each group reports positive total, absolute negative magnitude,
net, included/excluded counts, combined freshness warnings and arithmetic status.
For supported current accounts, positive/negative Booked balances support separate
asset/liability views. Available totals are labeled available and are not net
worth. There is no mixed-currency grand total or implicit conversion.

A group with no available values returns Unavailable and null amounts, not zero.
A supplied zero may produce an exact zero total. Excluded same-currency rows make
a subtotal partial. Rows with unknown currency cannot be assigned to a group;
the top-level HasExcludedAccounts flag must remain visible, even if an individual
known-currency group is complete. No group asserts that the whole portfolio is
complete. Freshness warnings remain independent of arithmetic completeness.

Positive and negative coefficients are accumulated separately, without decimal
rounding. All three final amounts must be representable exactly; otherwise the
group reports Unrepresentable with all amounts null. A representable net cannot
hide an unrepresentable positive or negative subtotal. Source rows remain intact
for inspection. No saturation, partial arithmetic result or rounded number is
presented as an exact total.

## Validation, bounds and integration

Reject duplicate selected account/incarnation/binding identities, indistinguishable
selected source locators, cross-kind local-ID collisions, inconsistent connection
ownership, conflicting observation provenance and duplicate snapshot IDs. These
conditions must not produce a plausible doubled portfolio total. Size, identity
or cancellation failures return no partial projection. Inputs remain unchanged.
Limits are 10,000 selected accounts and 64 current snapshots per account, with
existing locator/identity bounds retained. These are resource ceilings, not a
supported-bank/profile-size or performance claim.

Before live application, the connector/store must authenticate complete responses,
preserve raw observations, validate capabilities and identity lineage, resolve
durable replay/current-candidate selection, reject stale application plans and
commit complete entity payloads atomically. The UI must show exclusions, type,
currency, timestamps and all relevant freshness/partial/arithmetic warnings.
No production persistence, live connector, UI, currency conversion or payment
validation is enabled here.

## Verification

The BCL-only runner adds 109 checks: exact parsing/addition, currency codes and
rejections, signed coefficient boundaries, culture invariance, precision loss,
zero versus missing, kind isolation, unsupported/ambiguous/mismatched values,
unchanged provenance, freshness boundaries/clock anomalies, cached failure states,
separate currencies and signs, partial/unavailable/unrepresentable totals, input
identity failures and immutability. A 10,000-account projection totals exactly.
The full existing endpoint, identity, rediscovery and storage-vector checks remain.
