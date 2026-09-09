# ADR 0019: Read-request/response context evidence

- **Status:** Accepted (pure synthetic comparison only)
- **Date:** 2026-09-07
- **Extended by:** [Scoped read unavailability](0021-scoped-read-unavailability.md)
- **Related:** [Read schemas](0018-account-discovery-and-balance-schemas.md),
  [capability evidence](0013-read-capability-evidence.md),
  [dialogue correlation](0011-fints-responses-and-dialogue-correlation.md)

## Sources and scope

Reviewed FinTS 3.0 **Formals**, 2017-10-06, B.6.3, in the
[official specification](https://www.hbci-zka.de/dokumente/spezifikation_deutsch/fintsv3/FinTS_3.0_Formals_2017-10-06_final_version.pdf).
The source describes 3040 continuation tokens as opaque, reusable only within
the dialogue that supplied them, with the same retrieval order. The PDF hash
and Messages account/balance sources are recorded in ADR 0018.

`FinTsReadRequestContext` observes exactly one unsigned HKSPA-1 or HKSAL-6/7/8
segment between validated frame headers. It requires an established, nonzero
dialogue, no message reference on the client request, and an explicit expected
bank message number in 1–9999. Credential/signature/wrapper request paths,
initialization and multi-operation frames are unsupported.

`FinTsReadContextEvidence.Evaluate` compares this context, a parsed read-data
set and caller-selected capability evidence. It returns accumulated issue flags
and one of `NeedsReview`, `ExecutionReported` or `PartialReported`. A match
means only that the supported structural observations agree. It grants no
permission to send and establishes no authentication, ownership or freshness.

## Account, capability and response comparison

This increment supports exactly one selected account with the all-accounts
flag off. Empty/all-account or repeated-account requests remain schema-readable
but receive `UnsupportedAccountScope`; no first-account winner is selected.

The capability must have matching evidence and the exact operation/version.
Its parameter response must precede the expected response in the same dialogue.
The request's populated national tuple must exactly match the selected UPD
account, preserving subaccount and institution components. A request containing
only international identifiers must match a nonempty UPD IBAN. A discovery
report may supply a previously absent IBAN but cannot replace a known one.
A non-SEPA report conflicting with a previously known IBAN requires review.

Maximum-entry input requires the selected advertisement's explicit J flag;
unknown permission in older versions does not imply permission. National fields
in HKSAL-7/8 additionally require an explicitly selected HISPAS advertisement
from the same parameter set, unique at that exact version, with national-account
permission J. BIC is compared against the request when present; this is not BIC
validation or independent bank-identity evidence.

The outer response dialogue and client-message reference must match the request.
The bank message number must match the caller's explicit expectation; client
and bank counters are independent. Inner segment references are checked too.
Explicitly parsed PIN/TAN profile-2 responses are supported structurally;
other profiles require review and no profile is authenticated here.

Exactly one matching business report/account is required. Wrong operations,
unknown versions, additional parameter/challenge/opaque segments, duplicate or
missing reports, wrong references and mismatched versions require review.
Discovery compares national identity against both request and UPD. Balance
reports compare every logical account component against the request and also
check the UPD identity. Omitted trailing empty components have the same logical
value as explicit empty components; original source bytes remain attached.

Currency disagreements inside a balance report or against a populated UPD
currency require review. Any due date requires review until credit-card account
type context is implemented. Optional amounts and source dates are not changed.
The initial comparator kept empty discovery and 3010 reports for review.
ADR 0021 adds scoped unavailable observations without inventing an account or
zero balance.

## Status and continuation evidence

The initial comparison vocabulary contains message-level 0010/0020 and
request-scoped 0020/3040; ADR 0021 adds conditional request-scoped 3010 handling.
Element-specific statuses, extra nonempty execution
parameters, pending/error/unknown/conflicting statuses and missing scoped
execution/partial reports require review. A scoped 3040 takes precedence when
0020 is also present. No outcome is called complete or successful.

A balance partial report requires exactly one 3040 with one nonempty opaque
token; extra trailing empty parameter positions are allowed. HKSPA-1
continuation remains unsupported. The reported token is retained even when
other issues require review; its presence alone is not permission to continue.

A request with a continuation token requires caller-supplied prior-page
evidence that matched and reported partial data. The next comparison requires:

- Exact token equality, unchanged operation/version/account/options and dialogue.
- The same capability object and selected national-permission advertisement.
- Each message counter advancing by one from the previous request context.
- At most 128 pages, a conservative local resource policy.

Changed options, foreign/recreated parameter selection, rejected previous pages,
missing tokens, skipped counters and continuing after an execution report require
review. Tokens are not parsed, normalized, assumed unique or used as replay keys.
The result stores its page number but does not retain the previous-page object,
avoiding an implicit chain of retained wire buffers.

This is a pure comparator: it can be called again with identical input or used
to compare multiple candidate branches. It consumes no request/response and
provides no replay prevention, page aggregation, scheduling or durable evidence.
An in-memory attempt owner and later authenticated/durable integration must
enforce lifecycle and ingestion rules separately.

## Verification and remaining work

Eight independent Python fixtures select exact issue sets, outcomes and tokens.
The executable suite adds 236 checks covering all supported read versions,
account and parameter scope, source reference preservation, option permissions,
status conflicts, partial-page provenance, independent counters, wrapped
responses, the 128-page boundary, cancellation and fixed diagnostics. Existing
fixtures reproduce byte-for-byte. No credentials or bank requests are used.

Next: a bounded in-memory read-refresh attempt owning one selected-account
request at a time, with explicit cancellation, timeout and response consumption.
All-account discovery, no-data outcomes, broader account forms/types, full
credential/message codecs, authenticated sessions, durable replay protection,
page aggregation and domain ingestion remain open. The Windows host stays inert.
