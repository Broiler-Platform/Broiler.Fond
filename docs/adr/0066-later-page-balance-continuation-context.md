# ADR 0066: Later-page balance continuation context

- **Status:** Accepted (local synthetic context only)
- **Date:** 2026-10-14
- **Related:** [Assembled balance-continuation context](0058-assembled-balance-continuation-context.md),
  [continuation response semantics](0064-continuation-response-semantics.md),
  [bounded continuation attempt](0065-assembled-continuation-attempt.md)

## Source and scope

This extends ADR 0058's assembled balance-continuation context to later pages beyond
page two (e.g. page three and onwards up to the protocol maximum). The underlying
pagination rules from Formals 2017-10-06 B.6.3 and ADR 0019 are maintained: continuation
tokens remain opaque, tied to the exact dialogue, and retrieval options must remain
strictly identical.

`FinTsPinTanReadContinuationContext.Evaluate` adds an overload accepting one assembled
continuation semantic result (`FinTsPinTanReadContinuationEvidence`) and one proposed
unsigned read request (`FinTsReadRequestContext`). The predecessor represents an
already-executed continuation page $K \ge 2$ with client and bank message counters
$N \ge 3$. The proposed request must increment both client and expected bank message
counters by exactly 1 ($N + 1$). The resulting context assigns `PageNumber` to $K + 1$
bounded by the protocol limit.

## Provenance, page limits and comparison

The predecessor continuation evidence must be fully matching (`HasMatchingEvidence`),
report `PartialReported` outcome, and retain a non-null continuation token. A raw token
retained on a foreign or review response cannot qualify the next page request. Execution
and unavailable outcomes terminate continuation and cannot qualify further requests.

The proposed request continuation token must be present and byte-for-byte identical to
the predecessor's reported continuation token. Comparison performs no whitespace trimming,
case folding, delimiter normalization or structural interpretation.

The page number is computed as `Math.Min(previous.PageNumber + 1, MaximumPages + 1)`.
When the resulting page exceeds `FinTsReadContextEvidence.MaximumPages` (128), the context
flags `FinTsPinTanReadContinuationIssue.PageLimitExceeded` (1024) and withholds
`MatchingOrigin` and `MatchingRequest`. Bounded progression prevents unbounded pagination loops.

Both operations must be `HKSAL` at the exact same version. The assigned dialogue must
remain unchanged. Both requests must select one account with all-accounts disabled.
All logical national and international account components must match, with empty component
equivalence preserved. Maximum entries and all-accounts flags must remain identical.
The proposed request is qualified against the exact original capability context and
selected national-permission advertisement.

Origin provenance is preserved through the predecessor chain via
`PreviousContinuation.Request.Context.Continuation.Origin`. Callers cannot replace the root
account, capability or procedure selection. The context exposes `PreviousContinuation` for
later-page predecessors while retaining `Previous` for the immediate second page.

## Downstream pipeline integration

The downstream pipeline developed in ADR 0059 through ADR 0065 already formats, binds,
and executes continuation messages dynamically based on `candidate.MessageNumber` and
`candidate.PageNumber`:
- `FinTsPinTanReadContinuationSignatureContext` attaches the detached PIN/TAN header and procedure selection.
- `FinTsPinTanReadContinuationRequestBinding` binds segment positions and metadata for message $N$.
- `FinTsPinTanReadContinuationRequestWriter` and `EnvelopeWriter` encode plaintext and envelope wire messages.
- `FinTsPinTanReadContinuationResponseBinding` matches bank message $N$ and client reference $N$.
- `FinTsPinTanReadContinuationEvidence` observes response data for page $K$.
- `FinTsPinTanReadContinuationAttempt` manages the bounded one-time response lifecycle.

No new downstream codecs or binding classes are required for pages 3 through 128.

## Verification and next work

Forty-one independent Python fixtures in `pin-tan-read-later-continuation-context-v1.json`
and 857 checks cover:
- HKSAL-6, 7 and 8 across national and international accounts, entry count options, procedure profiles and escaped dialogues/tokens.
- Opaque token preservation with significant whitespace, delimiters, and maximum token length (35 chars).
- Negative cases: missing/empty/changed tokens, case sensitivity, untrimmed tokens, foreign dialogues, repeated/skipped client and bank counters, version/account/national mismatches, options mismatches, non-partial predecessors, review predecessors, missing token predecessors, and combined mismatches.
- Multi-page execution chain: Page 1 attempt (`FinTsPinTanReadAttempt`) -> Page 2 continuation attempt (`FinTsPinTanReadContinuationAttempt`) -> Page 3 continuation context (`FinTsPinTanReadContinuationContext`), verifying exact provenance chaining back to the root first-read origin.
- Complete bounded 128-page loop from page 3 through 128 (all matching evidence) and overflow at page 129 (`PageLimitExceeded`).
- Concurrency (16 parallel threads), pre/post cancellation, culture independence (`de-DE`, `ar-SA`, `tr-TR`), and fixed null-argument diagnostics without leaking sensitive data.

Next is session-level multi-page coordination, TAN/challenge integration, and full authenticated transport.
The host remains inert.
