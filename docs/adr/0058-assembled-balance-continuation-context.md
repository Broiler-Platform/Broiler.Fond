# ADR 0058: Assembled balance-continuation context

- **Status:** Accepted (local synthetic context only)
- **Date:** 2026-09-10
- **Related:** [Read continuation rules](0019-read-request-response-context.md),
  [assembled read semantics](0056-assembled-first-read-response-semantics.md),
  [assembled first-read attempt](0057-assembled-first-read-attempt.md)

## Source and scope

This applies ADR 0019's existing continuation rules to assembled first-read
evidence. The reviewed Formals 2017-10-06 B.6.3 source is recorded there and
available through the [official specification](https://www.fints.org/de/spezifikation).
Tokens remain opaque and bound to the dialogue and unchanged retrieval order.
No new protocol versions or status meanings are introduced.

`FinTsPinTanReadContinuationContext.Evaluate` takes one assembled first-read
semantic result and one explicitly supplied unsigned read request. It qualifies
only the immediate second balance page. The predecessor type fixes the original
client/bank counters at 2; the proposed request must use client 3 and expected
bank 3. PageNumber is therefore 2 and describes a proposed context, not a sent
or received page. Arbitrary later pages and recovery remain unsupported.

## Provenance and comparison

The predecessor must have matching evidence, report PartialReported and retain
a non-null continuation token. A raw token on an unresolved result cannot qualify
the next request. Execution and unavailable observations cannot start continuation.
The next request token must be present and exactly equal to the predecessor's
token. Comparison does not trim whitespace, fold case, normalize delimiters,
interpret token structure or assume uniqueness.

Both operations must be HKSAL at the same explicit version. The assigned dialogue
must remain unchanged, and each message counter must advance exactly once. Both
requests must select one account with all-accounts off. All logical national and
international account components must match, including BIC, subaccount, country
and institution. Trailing empty components remain logically equivalent while
their original source shapes are preserved.

All-accounts and maximum-entry options must remain unchanged. The proposed
request is also checked against the exact original capability and selected
national-permission advertisement, reusing existing request/account/permission
rules. Adding an advertised option still changes retrieval context and requires
review; an unadvertised option additionally carries the existing request issue.

Origin is the exact capability context retained by the predecessor candidate.
The API accepts no alternate account, capabilities or procedure selection, so
callers cannot replace that provenance during comparison. It retains the exact
previous evidence and next request without rebuilding or renumbering them.
The logical account comparison helper is shared with the unchanged unsigned path.

Outer flags distinguish previous-result review, missing partial evidence,
operation/token/dialogue/counter/account/options mismatches, unsupported account
scope and request review. RequestIssues retain detailed capability/request flags.
Any issue withholds MatchingOrigin and MatchingRequest. Raw Previous, Request
and Origin remain available for inspection and contain private untrusted data.

## Ownership and limits

The context can use the exact evidence returned by an assembled first-read attempt.
Creating it does not reopen that terminal owner or repeat its handoff. Pure
semantic evidence is also accepted; this API does not require or prove prior
attempt consumption, encoding, submission or authentication.

No credential owner, detached signature header, signature control reference or
encoded secret output enters this API. It performs no signing, TAN/SCA decision,
transport, polling, counter allocation, response consumption, page aggregation
or domain update. The existing first-read signature path still rejects counters
3 and continuation tokens; this context cannot silently extend that encoder.

Original parser bounds constrain tokens and requests. Evaluation observes
cancellation before work and before publication, has fixed null-argument
diagnostics and performs no clock calls. Repeated/concurrent comparisons consume
no evidence. Separate fully qualified source trees remain independent; there is
no cross-attempt replay guard or token freshness claim. Retention is bounded to
one first-read predecessor, not a recursive history of continuation pages.

## Verification and next work

Forty-one independent Python fixtures and 348 checks cover HKSAL-6/7/8, national
and international account fields, preserved entry counts, both PIN profiles,
procedure variants, escaped dialogues/tokens, significant whitespace and the
maximum token length. Negative cases include missing/changed tokens, every account
component, changed versions/options, all-account scope, skipped/repeated counters,
foreign dialogues and non-partial/unresolved predecessors.

Tests check exact source identity, unchanged bytes and inherited capability/account
provenance. They exercise a real one-time partial handoff, terminal-owner isolation,
continued rejection by the first-read signature path, mixed source-tree rejection,
logical empty-component equivalence, cancellation, fixed diagnostics, cultures
and concurrent comparison. All fixtures are public synthetic data.

Next is balance-continuation PIN/TAN signature context with unchanged identity
and procedure selection and an explicit control reference. Continuation credential
validation/encoding, response semantics, later-page ownership, TAN/challenge
integration, session coordination, authenticated transport and live qualification
remain pending. The host stays inert.
