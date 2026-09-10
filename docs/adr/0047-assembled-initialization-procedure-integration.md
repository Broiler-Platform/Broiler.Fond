# ADR 0047: Assembled initialization procedure and permission integration

- **Status:** Accepted (local synthetic evidence only)
- **Date:** 2026-09-09
- **Related:** [Signature procedure context](0033-pin-tan-signature-request-context.md),
  [initialization semantics](0040-pin-tan-initialization-response-semantics.md),
  [initialization attempt](0042-pin-tan-initialization-attempt-lifecycle.md)

## Scope

This increment combines assembled initialization evidence with returned HITANS
versions 6/7 and HIRMS 3920 reports. It uses the existing schema and restricted
procedure-comparison rules; it introduces no procedure negotiation, authentication,
SCA continuation, credential access or permission to send. The original signature
candidate's explicitly sourced procedure observations are not replaced.

`FinTsPinTanInitializationProcedureEvidence.Evaluate` takes a response binding,
parsed TAN parameters and parsed permission reports. Parameters must originate
from the exact bound response, and the permission set must retain that same response
object. Identical reparsed bytes are a different source. Mixed sources or wrong
request kind withhold matching advertisement/procedure objects before selection
comparison; raw caller-owned inputs remain accessible for review.

## Selective initialization integration

The original public initialization comparator remains conservative and unchanged
in behavior. An internal shared path recognizes only the exact source segments of
successfully parsed HITANS advertisements and the exact parsed 3920 replies at
the assembled preparation role, HKVVB segment 4. It does not mask general status
or parameter issues and does not copy, rewrite or filter the caller's source tree.

All previous dialogue, message, segment-role, profile, envelope-identity, execution,
bank/user/customer, protocol, language and parameter requirements continue to apply.
3920 is an allowed observation at preparation, not execution evidence. Unknown
codes, non-permission reply parameters, future HITANS versions, HIPINS and other
unintegrated data remain unresolved. Duplicate status reports still require review.
The shared unsigned parameter checker gains an internal exact-segment recognition
argument; its existing callers retain their original behavior.

## Returned selection and permission evidence

Comparison uses the selection pinned in the outgoing signature request: explicit
PIN profile, security function and TAN segment version. It requires a unique
advertisement for that version; duplicate advertisement versions or repeated
security functions require review. Multiple distinct supported versions can coexist
without selecting a version implicitly. Zero maximum orders or a minimum signature
count above one remain outside this restricted path.

PIN:1 requires the returned one-step-allowed flag and explicit reporting of 999.
PIN:2 requires one returned procedure matching the selected function. Permission
reports must be present, unambiguous, scoped to HKVVB 4 and contain that function.
Missing or changed observations are not filled from old cached evidence. A missing
advertisement never falls back to the original signature's procedure context.

Only when every initialization and procedure check matches are MatchingAdvertisement
and MatchingProcedure exposed. One-step matching has no two-step procedure object.
These are exact returned objects, not an active procedure or authority to submit.
The nested Initialization property reports the initialization component alone;
consumers must inspect the combined HasMatchingEvidence/Issues for procedure
qualification. For example, basic execution may be reported while permissions
are missing and the combined result requires review.

## Explicit lifecycle entry point

`FinTsPinTanInitializationAttempt.AcceptProcedureResponse` adds the combined path
without weakening AcceptResponse's existing behavior. It shares the same pending
candidate, deadline, lock and terminal state. Scoped responses hand out component
and combined evidence once and count one handled response. The combined result
selects ExecutionReported versus NeedsReview. Snapshot ProcedureIssues retains
scalar details and ProceduresNeedReview marks the snapshot's top-level issues.

Foreign reference scope leaves the candidate pending; valid later scope clears
transient diagnostics. Mixed permission sources end for review. Neither API can
repeat a handoff already made through the other API. Expiry, cancellation or clock
failure at any processing boundary withholds both evidence objects and releases
pending metadata. The attempt stores no terminal response or procedure tree.

## Verification and next work

Twenty-eight independent Python vectors and 261 checks cover both profiles,
versions 6/7, distinct supported versions, missing or duplicate advertisements,
changed selection, missing/duplicate/mis-scoped permissions, foreign identity or
message scope, bank abort, unknown data, signature requirements and retained status
restrictions. Tests verify exact source ownership, identical-byte source mixing,
legacy comparator behavior, explicit one-time lifecycle handoff, cross-entry-point
replay rejection, late failures and concurrent pure/lifecycle comparison.
All values are public synthetic fixtures.

Next is assembled initialization HIPINS parameter integration and PIN/TAN
requirement checks. Signature-context reuse of returned evidence, broader session
coordination, SCA/challenge integration, secure input, authenticated transport and
live qualification remain pending. The host stays inert.
