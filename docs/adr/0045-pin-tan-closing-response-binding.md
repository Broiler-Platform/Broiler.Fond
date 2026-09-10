# ADR 0045: PIN/TAN closing response binding and termination semantics

- **Status:** Accepted (local synthetic comparison only)
- **Date:** 2026-09-09
- **Related:** [Unsigned closing semantics](0030-dialogue-end-schemas-and-encoding.md),
  [assembled reference binding](0039-pin-tan-assembled-request-reference-binding.md),
  [closing context and encoding](0044-pin-tan-dialogue-end-context-encoding.md)

## Scope and sources

This increment applies ADR 0030's restricted termination vocabulary to ADR 0044's
assembled closing candidate. The source basis remains Formals, 2017-10-06,
C.4/C.5.3 and the abort rule, the 0100 entry in Messages - Rückmeldungscodes,
2026-02-03, and the PIN/TAN closing example on printed pages 238–239 of Security -
Sicherheitsverfahren PIN/TAN, 2020-07-10. Source hashes and the official catalogue
link are recorded in those ADRs. No HIEND schema or unsolicited closing path is
introduced.

## Candidate and reference ownership

`FinTsPinTanDialogueEndRequestBinding.ForEnvelopeCandidate` accepts only a matching
closing context. It retains the exact context and immutable segment metadata:
HNHBK 1, HNVSK 998, HNVSD 999, HNSHK 2, HKEND 3, HNSHA 4 and HNHBS 5 with their
encoded versions and roles. DialogueEnd is appended to the existing role enum,
preserving earlier values. Initialization/synchronization binding APIs remain
unchanged. No PIN owner or encoded output enters candidate construction.

The client and expected bank counters come from the restricted closing context
and remain 2, irrespective of prior recovered counters. A candidate is metadata,
not evidence that encoding, transmission or bank receipt occurred.

`FinTsPinTanDialogueEndResponseBinding.Evaluate` preserves the exact response and
candidate. Matching references require a PIN/TAN envelope of the same profile,
the expected bank counter, exact dialogue in the header and response reference,
and the closing client message number. Unlike first-dialogue binding, this path
never accepts a newly assigned dialogue identifier.

Every non-HIRMG body segment gets a source-preserving reference link to the actual
candidate segment or null for absent/unknown references. HIRMS may map to framing
or security roles as well as HKEND; such mapping is not semantic acceptance.
All data segments require review, including HISYN and an apparent HIEND. Missing
or unknown data references remain separately flagged. Existing reply parsing
rejects missing HIRMS references and duplicate HIRMS targets before comparison.

## Termination comparison

`FinTsPinTanDialogueEndEvidence.Evaluate` takes only the immutable response binding;
there is no second response source to mix with it. It compares the envelope system,
country, institution and user with the candidate's closing signature header.
All binding failures withhold a matching outcome, including missing envelope and
foreign profile. Any uninterpreted data remains an independent issue.

The local vocabulary permits message-level 0010/0020/0100/9800 and HKEND-scoped
0020/0100. Scoped success or closure on HNSHK, HNSHA, outer framing or wrapper
segments requires review. In particular, old unsigned HKEND reference 2 now names
HNSHK and cannot qualify a closing report. Security errors remain mapped evidence
and require review rather than being discarded.

Exactly one termination code is required across reply levels. Matching 0100 yields
ClosureReported; matching message-level 9800 yields AbortReported. An abort still
has the generic error classification. Receipt, execution or pending codes alone
do not establish termination. Duplicate or conflicting termination, unsupported
codes, nonempty element references or reply parameters, other errors, conflicting
classes and indeterminate processing require review. Empty trailing parameters
remain omissions, consistent with the unsigned comparator.

CloseReported and AbortReported are raw observations even for foreign or unresolved
responses. Only Outcome and HasMatchingEvidence qualify those observations.
The generic reply vocabulary remains unchanged and still treats termination codes
as uninterpreted. Neither outcome authenticates the bank, closes a socket, applies
recovered state, activates a session or eliminates the need for reinitialization.

## Verification and next work

Forty-six independent Python vectors and 409 checks cover both profiles, escaped
identifiers, assignment/recovery context, maximum recovered counters, message and
scoped closure, abort, receipt/pending/indeterminate outcomes, duplicate/conflicting
termination, all mapped security/framing roles, foreign counters/dialogue/profile/
identity, missing references and unexpected data. Tests compare the candidate map
with actual synthetic encoded output, preserve exact source identity, reject all
unresolved closing contexts, enforce immutable mappings and check cancellation,
null inputs, cross-candidate isolation and concurrent pure comparison.

Comparison is repeatable and performs no response consumption or replay tracking.
Next is a bounded assembled closing attempt that pins one candidate, consumes a
scoped response once and releases pending metadata on every terminal path, while
preserving the synchronization handoff and reinitialization requirement. Secure
input, transport, authentication and live qualification remain pending; the host
stays inert.
