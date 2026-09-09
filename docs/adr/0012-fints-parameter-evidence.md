# ADR 0012: Bounded BPD/UPD parameter evidence

- **Status:** Accepted (untrusted schema evidence; activation pending)
- **Date:** 2026-09-06
- **Related:** [Response schemas](0011-fints-responses-and-dialogue-correlation.md),
  [account identity](0007-account-source-rediscovery.md)

## References

Reviewed FinTS 3.0 **Formals**, 2017-10-06, D.2, E.2–E.3, F, H.1.2 and I.3,
and **Messages – Multibankfähige Geschäftsvorfälle**, 2022-04-15, B.1–B.3,
via the official [specification downloads](https://www.fints.org/de/spezifikation).
The Formals PDF hash is recorded in ADR 0010. The Messages PDF downloaded on
2026-09-06 has SHA-256
`a3db32dc27b596f67dea0d4447d07011d8945dc589aafcacb869018ea7d94afd`.
The documents are not redistributed.

HIBPA#3 describes general bank parameters. HIUPA#4 supplies user parameter
version and interpretation of unlisted operations: usage 0 means blocked,
usage 1 means unknown. HIUPD#6 supplies account details and permission entries.
UPD version zero is dialogue-scoped. The minimum timeout concerns life-indicator
spacing; the maximum concerns expected dialogue termination. These are not a
single numeric range. Optional limits may exist at the bank even when absent
from UPD. Formals admits 35-byte account-owner names despite its 27-byte table.

## Implemented contract

`FinTsParameterSet.Parse` reads the uninterpreted segments of a `FinTsResponse`.
It returns immutable source-bearing HIBPA#3, HIUPA#4 and HIUPD#6 models.
Duplicate HIBPA/HIUPA headers, malformed known schemas and unsupported known
versions fail with fixed error categories. Unimplemented segment codes remain
explicitly in `UninterpretedSegments`; HIKOM cannot edit a configured endpoint.

Bank evidence preserves versions, language/protocol lists, operation-type count,
message size in KiB and both timeout fields. Optional numeric omissions remain
null, distinct from supplied zero. Repeated list entries and unimplemented
numeric protocol versions are preserved; no protocol is selected automatically.
Language values are limited to the reviewed schema's codes 1–3.

User and account identifiers remain raw unescaped bytes in the source tree.
There is no trimming, case folding, IBAN truncation, account allocation or
rediscovery application. The parser accepts 35-byte owner names intact and
rejects IBANs over 34 bytes instead of shortening identity evidence. Missing
account sections do not imply deletion. Account-independent entries and their
truncated optional fields remain distinguishable from account-bound entries.
Duplicate account occurrences are retained without choosing or merging them.

Permission entries preserve the operation, required signature count, optional
limit and exact source fields. Limits validate bounded shape, decimal lexical
bytes, currency syntax and conditional amount/day fields without computing an
available balance or approving a transaction. Account and operation limits
cannot coexist. Unknown alphabetic currency codes remain source evidence;
country-specific account/bank-code validity and currency recognition are not
asserted by these schemas.

`GetOperationEvidence` is scoped to an account object from the same parameter
set. It returns `Listed`, `UnlistedBlocked`, `Unknown` or `Ambiguous`. Duplicate
permission entries are ambiguous even if they look identical. A missing HIUPA
cannot imply that an absent operation is blocked. `Listed` only means an entry
was supplied; no security, bank capability, signature or SCA requirement has
been satisfied by this query. It never returns an execution authorization.

## Resource and layout policies

The local aggregate bounds are 512 HIUPD occurrences and 8,192 permission
entries per parse, within the existing wire/tree limits. Each HIUPD has at most
999 positional permission slots. Raw text lengths and scalar/group cardinality
are checked; binary values and bytes outside the basic printable ranges are
rejected in typed text. Checks also close the previously admitted A0 octet in
typed reply text and dialogue IDs; the underlying byte parser remains lossless.

The account extension follows a repeatable optional permission field. This
profile accepts it only after all 999 explicit slots. A shortened nonempty
scalar in the permission region is rejected as `UnsupportedParameterLayout`;
it is not guessed to be JSON or an operation. Extensions of at most 2,048 bytes
are preserved without parsing or acting on them. Other institution layouts
require fixtures and an explicit interpretation before support is widened.

Every model retains the original response, including error/correlation evidence.
Parsing is not contingent on a success code and cannot hide a failed response.
No BPD/UPD snapshot is activated, persisted or inherited from a prior response.
Matching institution/user/request context, authenticated complete collection,
cache replacement, change handling and dialogue-scoped expiration remain
responsibilities of a future integration layer. Default formatting and errors
exclude raw bank, user and account data. These retained trees are not suitable
for real credential storage.

## Evidence and next increment

Five independently generated public parameter fixtures and 181 checks cover
raw identifiers, policy distinctions, duplicate retention, optional fields,
limits, opaque extensions, cancellation and exact/over-limit resource bounds.
All existing domain, response, syntax and storage-candidate checks remain enabled.

Next are read-operation parameter schemas and conservative BPD/UPD capability
matching, followed by security-profile processing before authenticated domain
ingestion. Live transport, SCA, credentials, persistence and UI remain pending;
the Windows development shell stays inert.
