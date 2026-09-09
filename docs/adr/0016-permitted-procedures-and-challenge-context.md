# ADR 0016: Permitted-procedure reports and request-bound challenge evidence

- **Status:** Accepted (pure, bounded evidence comparison)
- **Date:** 2026-09-06
- **Related:** [TAN schemas](0015-tan-procedure-and-challenge-schemas.md),
  [PIN/TAN envelopes](0014-pin-tan-envelope-and-parameters.md),
  [dialogue correlation](0011-fints-responses-and-dialogue-correlation.md)

## Source and scope

Reviewed the official [FinTS specification](https://www.fints.org/de/spezifikation),
**Security – Sicherheitsverfahren PIN/TAN**, 2020-07-10: B.5.1–B.5.2,
B.6, B.8.2 and the B.4 initial-challenge, exemption and decoupled-status flows.
The download hash is recorded in ADR 0014.

This increment reads procedure-permission reports and compares a bounded
synthetic request with supplied bank evidence. It builds no request, accepts
no credential, authenticates no party and advances no dialogue or SCA state.
Result names deliberately describe observations rather than authorization.

## Reply 3920

`FinTsPermittedProcedureSet.Parse` reads HIRMS 3920 reports with an empty
element reference. Each report preserves its original reply, containing
segment and ordered security functions. Up to ten functions may be reported:
900–997 for two-step procedures and 999 for one-step. Code 998 is invalid.
At least one function is required. Trailing unused parameter positions may
be empty; holes followed by populated positions fail. Existing reply schemas
reject binary parameters and enforce the ten-parameter bound.

At most 128 reports are accepted per response. Repeated reports and repeated
functions are retained and exposed as ambiguous; no report replaces another.
Missing reports remain missing. A 3920 report can accompany error 9800, so
parsing the report never establishes a usable or still-active dialogue.
No user-permission cache or general reply-code vocabulary is changed.

## Recorded request context

`FinTsTanRequestContext.Parse` observes an existing immutable, unsigned
synthetic frame containing exactly one caller-identified HKTAN version 6/7.
It retains the frame, exact segment and caller-supplied expected bank message
number. Wrapped/signed requests are unsupported. This is a restricted request
observation, not a complete HKTAN request codec.

Supported observations are initial process 1/4 and version-7 status process S.
The operation code, optional order hash/reference and optional selected medium
are bounded and retained. Status requests require an order reference; process
1/S requires the single-approval further-TAN value N. Hashes are nonempty
binary up to 256 bytes and occur only in process 1. Process 4 cannot populate
the further-TAN field. Account, cancellation, challenge-class, class-parameter
and HHD-response positions must remain empty. TAN submission, cancellation,
multiple signers and full request-option schemas remain outside this slice.

## Context comparison

`FinTsTanChallengeEvidence.Evaluate` accepts the observed request, a parameter
set, an explicitly selected procedure belonging to that exact set, permission
reports and parsed challenge evidence. It accumulates issue flags and retains
all inputs. A foreign procedure instance fails rather than matching by text.

The comparison requires one unambiguous report listing the selected function,
a unique advertisement for the selected version and a unique selected function
within that advertisement. Parameters and permission reports must share the
same response instance. Their dialogue ID must match the challenge response,
and their bank message number cannot be later. For an initial request with
dialogue ID zero, these reports must come from the challenge response itself;
evidence from another response is not reused as a new dialogue's permission.
For an established dialogue, earlier evidence with the same raw dialogue ID
can be compared. Its original initialization request, authenticated user binding,
completeness, freshness and storage provenance remain unverified.

Response message number, request-message reference and raw dialogue IDs must
match the supplied request context. Initial dialogue assignment requires
message number 1 on both sides and a nonzero, non-placeholder assigned ID.
Every response-body reference must name a number present in the request.
A parsed wrapper must report PIN:2. Plain synthetic responses remain usable
for schema tests; neither shape supplies authentication or cryptographic proof.

The response must contain exactly one HITAN for the selected HKTAN. Extra or
opaque HITAN occurrences remain ambiguous. Request, procedure and challenge
versions/processes must agree. Process S requires the exact `Decoupled`
method, not push or a procedure that expects TAN input.

Order-hash presence follows the selected advertisement and process; required
hash bytes must exactly mirror the observed request. No hash is calculated or
verified against a business order. A supplied order reference must also match
exactly. A missing active-media count requires an initial challenge medium
name. A supplied request medium must match the reported medium. Unsupported
account/challenge-class requirements and a missing required selected medium
produce review issues rather than silently satisfying those options.

Populated expiry remains a review issue because the parser has no trusted
timezone/clock policy. No expiry decision is inferred from a raw local date.

## Outcome observations

Errors, conflicting replies and codes outside the narrow context vocabulary
require review. The generic response parser's code meanings are unchanged.
Relevant status reports must occur on HIRMS for the selected request, at the
whole-segment level. Status reports on other segments, duplicate statuses and
incompatible completion/pending reports cannot yield a clean observation.

When no issue remains, the possible observations are:

- `ChallengeReported`: an initial process 1/4 challenge with one related 0030.
- `DecoupledPendingReported`: a process-S response with one related 3956 and
  the exact order reference. No status query is scheduled.
- `ExemptionReported`: an initial process 1/4 response with one related 3076,
  exact `noref`/`nochallenge` placeholders and no binary HHD challenge.

Any issue yields `NeedsReview`. A placeholder by itself yields no exemption
observation. These results are neither permission to send nor proof that SCA,
an operation or a dialogue has completed. The evaluator is repeatable and pure:
it consumes no response, offers no replay protection, changes no counter and
continues no operation. Replay/session handling remains a separate requirement.

## Verification and next work

Seven independent public fixtures and 191 checks cover matching initial and
pending evidence, exemption reports, abort-plus-permission reports, exact
hash/reference/medium binding, context mismatches, duplicates, unsupported
requests, conditional requirements, resource bounds, cancellation, fixed
diagnostics and both PIN profile observations. Fixture generation remains
Python standard library only; ordinary CI uses embedded JSON and .NET.
See the [fixture inventory](../../tests/Broiler.Fond.Kernel.Tests/Fixtures/FinTs/README.md).

Next is a bounded in-memory SCA continuation state model using synthetic
evidence, with one pending challenge, replay rejection, terminal cancellation
and explicit timeout/poll limits. Credential submission, full HKTAN codecs,
live transport, authenticated ingestion and payments remain unimplemented.
