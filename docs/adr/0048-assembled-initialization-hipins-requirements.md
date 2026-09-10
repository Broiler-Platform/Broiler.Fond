# ADR 0048: Assembled initialization HIPINS requirements

- **Status:** Accepted (local synthetic evidence only)
- **Date:** 2026-09-10
- **Related:** [HIPINS schema](0014-pin-tan-envelope-and-parameters.md),
  [procedure integration](0047-assembled-initialization-procedure-integration.md),
  [initialization attempt](0042-pin-tan-initialization-attempt-lifecycle.md)

## Scope and provenance

`FinTsPinTanInitializationRequirementsEvidence.Evaluate` combines the existing
initialization and HITANS/3920 checks with returned HIPINS 1 requirements. TAN and
PIN/TAN parameters must share the exact `FinTsParameterSet` instance; that tree
and the permission reports must originate from the exact bound response. Reparsing
identical bytes, or building separate parameter trees on one response, does not
substitute for this provenance. Raw caller-owned observations remain available.

The internal initialization path additionally recognizes only successfully parsed
HIPINS source segments. All existing dialogue, reference, segment-role, envelope,
identity, execution, parameter and procedure checks remain in force. Unsupported
HIPINS versions and other unknown data stay unresolved. The original public
initialization and procedure-only comparators retain their conservative behavior.

## Requirement observations

A matching combined result requires one known HIPINS advertisement and no unknown
HIPINS version. Missing or duplicate advertisements require review. Optional PIN
minimum/maximum and TAN maximum lengths remain nullable, without invented defaults.
Reported minimum PIN length above its maximum is contradictory. Explicit zero
lengths are retained but require review under this local conservative policy; this
is not a claim that the protocol schema forbids zero. Zero maximum orders and a
minimum signature count above one are unsupported by this restricted path.

Repeated operation codes require review even when their J/N flags agree. Only a
fully matching combined result exposes `MatchingAdvertisement` and qualified
operation observations. Missing operations remain Unlisted; unresolved combined
evidence returns Unknown. N reports that a TAN is not required for the operation;
it does not grant BPD/UPD permission, authenticate a response or waive SCA.

These checks compare reported metadata. They do not read credentials, validate
credential lengths against inputs, merge HIPINS and HITANS TAN-length bounds,
choose field precedence, activate capabilities or construct a later signature
context. Original labels and bounds remain attached to their source objects.

## One-time handoff

`FinTsPinTanInitializationAttempt.AcceptRequirementsResponse` shares the existing
pending candidate, lock, deadline and terminal state. A scoped response hands out
one requirements result with its exact procedure and initialization components.
The outer result determines ExecutionReported versus NeedsReview. Nested component
matches alone do not qualify the full requirements result. Snapshot RequirementsIssues
retains scalar diagnostics, with RequirementsNeedReview in the top-level issues.

Foreign reference scope leaves the candidate pending without renewing the deadline;
later valid scope clears transient diagnostics. Mixed sources terminate for review.
All three acceptance APIs share replay rejection. Cancellation, expiry and clock
failure at processing boundaries withhold all component handoffs and release the
pending candidate. The attempt retains no terminal response or parameter tree.

## Verification and remaining work

Thirty independent Python fixtures and 312 checks cover both PIN profiles, TAN
versions 6/7, missing/zero/conflicting bounds, duplicate or future advertisements,
duplicate J/N flags, absent operations, escaped labels, foreign scope, identity and
unsupported requirements. Additional checks cover exact source retention, source
mixing, legacy behavior, one-time lifecycle handoff, late failures and concurrency.
All values are public synthetic data.

Next is reuse of returned combined initialization evidence in later PIN/TAN
signature context with explicit dialogue and procedure provenance. Credential
requirement application, broader session coordination, SCA/challenge integration,
secure input, authenticated transport and live qualification remain pending.
The host stays inert.
