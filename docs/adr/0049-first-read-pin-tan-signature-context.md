# ADR 0049: First-read PIN/TAN signature context

- **Status:** Accepted (pure synthetic comparison only)
- **Date:** 2026-09-10
- **Related:** [Signature context](0033-pin-tan-signature-request-context.md),
  [unsigned reads](0024-unsigned-read-request-encoding.md),
  [combined initialization requirements](0048-assembled-initialization-hipins-requirements.md)

## Scope and source basis

`FinTsPinTanReadSignatureContext` reuses the complete returned initialization
requirements result for a detached signature header on the first subsequent
discovery or balance request. It retains the exact combined result, unsigned read
context, header and explicit selection. The initialization attempt's one-time
handoff can supply this result; pure comparison does not require or consume a
lifecycle handoff. Existing initialization/synchronization signature APIs and
writers are unchanged.

The [official FinTS specification](https://www.fints.org/de/spezifikation),
PIN/TAN 2020-07-10 B.2 (printed page 20), requires retaining the selected TAN
procedure during a dialogue. Its SHA-256 is
`77bb5724cb391434cd7188924a1ff89bff040bf340dc49a09c92cdeb4ee7dc3a`.
The header identity and control fields use the existing ADR 0033 comparison rules.
The immediate-next-message restriction below is a local implementation boundary,
not a claim that FinTS permits only one read per dialogue.

## Context requirements

The outer initialization requirements result must match. Matching inner procedure
or execution components do not substitute for unresolved HIPINS requirements.
Source mixing rejected by initialization remains rejected here; old procedure
observations from the original outgoing signature are never a fallback.

The unsigned read must use the exact reported initialization dialogue, client
message number 2 and expected bank message number 2. Continuation tokens require
review because this path has no preceding read-page evidence. HKSPA 1 and HKSAL
6/7/8 retain their existing schema behavior, including account lists and all-account
flags. This comparison does not validate account ownership, bank/user permission,
read-advertisement requirements or availability of the selected operation/version.

Country, institution, expected user and system must match the original qualified
initialization signature context. System IDs `0` and `unbekannt` require review;
no identifier is assigned or recovered. The explicit profile, function and TAN
segment version must equal the initialization selection. The candidate header must
match that profile/function, use number 2, supplier role 1 and security party 1,
and equal the caller's validated control reference. Control equality does not
establish an HNSHA link, freshness or replay protection. No uniqueness requirement
or timestamp interpretation is invented.

The read operation must have a qualified HIPINS J/N observation. An unlisted
operation requires review in this first-read path. J and N remain reports; neither
grants permission and N does not waive SCA. Missing optional length bounds remain
nullable. No credential is inspected and HIPINS/HITANS length precedence remains
unimplemented.

## Result ownership and limits

Only a full context match exposes the exact returned TAN advertisement, two-step
procedure (when applicable), HIPINS advertisement and operation flag. Any issue
withholds all matching objects and returns Unknown for the qualified operation
flag. Raw nested inputs remain accessible for review. One-step matching has no
two-step procedure object.

Comparison is pure, bounded by the existing parsers, cancellation-aware and safe
for concurrent use of immutable inputs. Repeated calls can yield matching results;
this API is not a request allocator or replay gate. Caller-owned results survive
initialization-attempt cleanup without reopening that attempt. No read request is
recorded, renumbered, signed, wrapped or sent, and no session is activated.

## Verification and next work

Thirty-seven independent Python fixtures and 230 checks cover discovery/balance
schemas, both PIN profiles, TAN versions 6/7, exact returned source ownership,
dialogue and counter scope, identities, changed procedure selections, control and
header roles, continuation rejection, unlisted operations and unresolved combined
initialization. Additional checks cover mixed sources, one-time initialization
handoff reuse, nullable bounds, cancellation, malformed inputs, fixed diagnostics,
culture independence and concurrent pure comparison. All values are public
synthetic data.

Next is assembled initialization read-capability integration and first-read
account/permission checks. Credential requirement application, signed read
assembly, response binding, session/SCA coordination, secure input, authenticated
transport and live qualification remain pending. The host stays inert.
