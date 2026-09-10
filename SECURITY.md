# Security policy

## Current development status

Milestone 1 development adds manual endpoint validation, confirmation, and
quarantine, in-memory identity allocation/startup integrity checks, and a pure
account rediscovery/value projection layer to the M0 foundation. The Windows host
remains inert. It does
**not** implement bank connectivity, credential handling,
profile persistence, balance retrieval, or payments. No live institution is
supported and no security certification or release approval is claimed.

The proposed storage format has public synthetic vectors and BCL reference
code only in the verification project. The fixed fixture keys/nonces must never
protect user data. Full schemas, production streaming, KDF calibration and human
security review remain prerequisites for enabling profile persistence.

The kernel's opt-in diagnostic buffer accepts only predefined event codes and
outcomes, with optional timestamps in an immutable in-memory support preview.
It has no file export, upload or persistent log. The test project exercises
synthetic read workflows and clears caller-supplied synthetic PIN/TAN buffers;
it implements neither a FinTS codec nor a live credential path. See
[ADR 0009](docs/adr/0009-synthetic-workflows-and-diagnostics.md) for the exact
scope, sentinel checks and remaining integration work.

The bounded FinTS byte parser now preserves untrusted syntax and checks outer
framing. It authenticates no messages or security procedures.
Parsed wire buffers may retain raw data until collected;
they must not be used as a real credential container or logged. Independent
fixtures and fixed-message parse errors are covered by
[ADR 0010](docs/adr/0010-fints-byte-syntax-and-framing.md).

Typed HIRMG/HIRMS responses and in-memory dialogue correlation now reject
malformed schemas, conflicting status and mismatched/replayed references.
Correlation does not authenticate a response or imply execution; errors and
indeterminate outcomes remain explicit. No automatic retry or bank-text logging
is added. The exact scope and current code vocabulary are recorded in
[ADR 0011](docs/adr/0011-fints-responses-and-dialogue-correlation.md).

HIBPA/HIUPA/HIUPD schemas now preserve bounded parameter and permission evidence.
They neither activate permissions nor update endpoints, identities or cached
accounts. Raw source bytes remain untrusted, and unknown/duplicate evidence
cannot silently authorize an operation. [ADR 0012](docs/adr/0012-fints-parameter-evidence.md)
records the remaining context, security and capability-integration requirements.

Read-parameter schemas and explicit-version capability comparison now detect
scope, permission and signature conflicts. A matching result remains untrusted
evidence and cannot authorize a request; no fallback, URI fetch or security
procedure is performed. See [ADR 0013](docs/adr/0013-read-capability-evidence.md).

PIN/TAN envelope parsing now validates uncompressed plaintext response wrappers,
preserves exact outer/inner bytes and checks their combined resource budget.
It rejects nested wrappers, signatures and client operation segments. It
implements no decryption, credential path or transport authentication.
HIPINS schemas preserve reported requirements, duplicates and unknown versions;
a reported N flag does not waive SCA or grant permission. See
[ADR 0014](docs/adr/0014-pin-tan-envelope-and-parameters.md).

HITANS/HITAN version 6/7 schemas now preserve TAN-procedure and challenge
evidence with conditional fields and resource limits. Raw challenge markup
and binary data are never executed or rendered. Separate-device approval
parameters schedule no polling, and dummy references imply no SCA exemption.
See [ADR 0015](docs/adr/0015-tan-procedure-and-challenge-schemas.md).

HIRMS 3920 reports and restricted unsigned-request observations now support
pure challenge-context comparison. Scope, hash/reference/medium mismatches,
abort errors, ambiguous status and unsupported requirements produce review
issues. Reported exemptions require related 3076 and exact dummy fields but
still provide no authorization. The comparison consumes no response and
provides no replay protection or SCA continuation. Full request/user context,
trusted expiry and authenticated session handling remain pending. See
[ADR 0016](docs/adr/0016-permitted-procedures-and-challenge-context.md).

A separate in-memory SCA attempt model now records one challenge and one
outstanding synthetic status request. It pins context instances, rejects
consumed message counters, bounds query count/waits and enforces an absolute
monotonic deadline. Cancellation, failure and terminal observations release
the model's retained evidence references. No credentials, sending, timers or
push transport are added. Replay protection is limited to that model instance;
terminal execution/exemption states remain untrusted reports. See
[ADR 0017](docs/adr/0017-in-memory-sca-continuation.md).

Account-discovery and balance schemas now preserve untrusted identifiers,
separate exact amounts, missing values and source calendar timestamps. Known
malformed fields fail; duplicate observations, currency conflicts and unknown
versions remain visible. Parsing establishes no account ownership, request
authorization, completeness, freshness or authenticated domain state. Account,
capability and continuation context validation remains pending. See
[ADR 0018](docs/adr/0018-account-discovery-and-balance-schemas.md).

Pure single-account read-context comparison now checks exact request/response
versions, dialogue and segment references, account identities, capability
requirements and scoped partial-page tokens. Continuation evidence requires an
unchanged selected context and sequential counters, bounded to 128 pages.
Comparison consumes no response and offers no replay prevention, authentication,
aggregation or domain ingestion. Raw tokens remain untrusted even on review
results. See [ADR 0019](docs/adr/0019-read-request-response-context.md).

A synchronized local read-refresh attempt now pins one selected context and
pending request, recomputes response comparisons and rejects consumed counters
within that attempt. It enforces an absolute monotonic deadline and a bounded
page count; every terminal path releases retained raw evidence references.
This adds no sending, authentication, durable replay protection, aggregation
or cached balance update. See [ADR 0020](docs/adr/0020-in-memory-read-refresh-attempt.md).

Scoped HIRMS 3010 now produces a distinct unavailable observation for selected
reads only. Missing responses, conflicting returned data, unscoped statuses and
9210 errors remain review outcomes. Generic status decoding stays unchanged.
Unavailable observations never imply account closure, empty balances or deletion
of prior values. See [ADR 0021](docs/adr/0021-scoped-read-unavailability.md).

All-account discovery now compares exact national/IBAN candidates against a
bounded supplied UPD set. Unknown accounts, conflicting identifiers, duplicates,
unlisted permissions and unmatched known entries remain visible for review;
candidate expansion is capped without truncating ambiguity. No account is
allocated, relinked or removed and no response is consumed by this pure
comparison. See [ADR 0022](docs/adr/0022-all-account-discovery-evidence.md).

A separate all-account discovery attempt now pins one request and parameter set,
rejects foreign responses before candidate expansion and hands bound comparison
evidence to its caller once. Cancellation, timeout and terminal review release
its retained context. Caller-owned evidence remains untrusted and replay
rejection is limited to that instance. Scalar snapshots contain no account data.
See [ADR 0023](docs/adr/0023-in-memory-all-account-discovery-attempt.md).

The unsigned read-request writer now constructs bounded HKSPA-1 and HKSAL-6/7/8
frames from typed identifier inputs. It rejects lossy text conversion, escapes
syntax characters and validates the complete frame before returning bytes.
It performs no capability selection, charset negotiation, authentication or
sending. Explicit wire buffers remain caller-owned data unsuitable for logging.
See [ADR 0024](docs/adr/0024-unsigned-read-request-encoding.md).

Restricted HKIDN-2/HKVVB-3 initialization schemas and unsigned encoding now
validate first-message framing and the explicit anonymous identity tuple.
Caller-supplied customer, system and product fields remain untrusted observations;
they establish no authenticated identity, product registration or session.
Padded initialization fields and lossy text conversions fail without modifying
identifiers. No synchronization, security envelope or transport is added. See
[ADR 0025](docs/adr/0025-unsigned-dialogue-initialization.md).

Pure initialization response comparison now checks assigned dialogue/reference
scope, bank identity and explicit user/customer expectations. Missing parameters,
unknown segments, unsupported statuses and security wrappers require review;
supplied cache-version numbers never trigger cache reuse. Matching evidence
creates no session or replay state and activates no returned parameter data. See
[ADR 0026](docs/adr/0026-initialization-response-scope.md).

A separate local initialization attempt now pins one request and expected user
identity, rejects unrelated references without consumption and hands bound
comparison evidence to its caller once. Timeout, cancellation and terminal
review release retained context. Snapshots expose only scalar diagnostics.
Per-instance consumption does not establish transport provenance or prevent
replay across new attempts, and no reported dialogue becomes an active session.
See [ADR 0027](docs/adr/0027-in-memory-initialization-attempt.md).

HKSYN-3/HISYN-4 synchronization schemas and unsigned encoding now preserve
bounded system identifiers, message numbers and exact signature-reference values.
Empty, conflicting, duplicate and unsupported observations remain explicit.
No returned value updates an identity or counter, and no recovery request,
signature sentinel or authenticated security profile is created. Mode/profile
binding and the required close/reinitialize flow remain pending. See
[ADR 0028](docs/adr/0028-synchronization-schemas.md).

Pure synchronization context checks now compare references, status, mode-specific
fields and an explicit profile hypothesis. Message recovery requires caller-owned
prior-dialogue context; unresolved profiles and counter layouts require review.
A matching result requires closing and reinitializing, and supplies no permission
to send business requests or apply recovery values. No profile is authenticated
and no close, retry or replay state is created. See
[ADR 0029](docs/adr/0029-synchronization-response-context.md).

HKEND-1 schemas and unsigned encoding now support pure request-bound closing
reply comparison. Normal closure and explicit abort remain separate observations;
foreign references, ambiguous status, data segments and security envelopes require
review. No session is ended, response consumed or retry scheduled. Identified
security and the synchronization closing lifecycle remain pending. See
[ADR 0030](docs/adr/0030-dialogue-end-schemas-and-encoding.md).

A local synchronization-and-closing attempt now pins request/profile context,
hands each bound comparison to its caller once and requires an explicit matching
close before reporting that fresh initialization is needed. Abort, review,
cancellation and timeout terminate separately, releasing retained context.
One absolute monotonic deadline spans both stages. No recovered values are applied,
and replay protection is limited to the attempt instance. See
[ADR 0031](docs/adr/0031-in-memory-synchronization-and-closing.md).

Restricted PIN/TAN HNSHK-4 parsing now preserves header fields, enforces profile/
procedure-code shapes and rejects populated CID, hash parameter and certificate
fields. Exact reference numbers and algorithm fillers confer no replay protection
or cryptographic meaning. No PIN/TAN field, signature trailer, credential ownership
or authenticated request is added. Raw source buffers remain private and unsuitable
for credential storage or logging. See
[ADR 0032](docs/adr/0032-pin-tan-signature-header-schemas.md).

Pure PIN/TAN signature-header comparison now checks initialization/synchronization
identity, system and control expectations against explicitly selected procedures.
HITANS and 3920 must share a supplied initialization response with matching scope;
missing, duplicate, foreign or aborted evidence requires review. Matching selects
only untrusted observations and permits no sending, credential use, authentication
or cache activation. See
[ADR 0033](docs/adr/0033-pin-tan-signature-request-context.md).

Typed HNSHK-4 encoding now emits one credential-free segment from explicit
immutable input, validates it through the existing schema and returns caller-owned
bytes. Strict text handling, canonical omissions and fractional-second rejection
prevent silent alteration. Encoding performs no permission check, credential use,
cryptographic operation or sending. See
[ADR 0034](docs/adr/0034-pin-tan-signature-header-encoding.md).

A standalone session credential buffer now owns bounded opaque PIN/TAN bytes,
clears transferred input on every exit and zeroes its private pinned array before
releasing it. TAN copy-out consumes that owner once; PIN copies remain available
within a fixed access lifetime. Callers must clear successful output copies and
dispose owners promptly. Idle expiry is observed on the next API call, and fallback
finalization is not a shutdown guarantee. This adds no live credential input,
bank operation, memory encryption or complete-erasure guarantee. See
[ADR 0035](docs/adr/0035-session-credential-buffer-ownership.md).

Local HNSHA-2 encoding now uses owned credential buffers and bounded caller
output, with no production credential strings or parsed secret objects. Variant-2
TAN placement in HNSHA is rejected. Successful TAN copy-out stays consumed even
if later encoding fails; temporary spans and failed output are cleared. Successful
output contains plaintext credentials and must be cleared by its caller. No
security envelope, live input or sending is enabled by that writer. See
[ADR 0036](docs/adr/0036-pin-tan-signature-trailer-encoding.md).

Plain PIN/TAN initialization/synchronization assembly now stages the complete
candidate in a bounded stack span, preserving bound fields and exact framing.
Credentials never enter production strings or parsed request objects. Failed
publication clears the entire written prefix; successful output remains the
caller's plaintext cleanup responsibility. Full session semantics,
authentication and transport remain pending. See
[ADR 0037](docs/adr/0037-pin-tan-request-assembly.md).

PIN/TAN request-envelope assembly now wraps that restricted candidate using
bound credential-free metadata, canonical public fillers and the exact inner binary
payload. Both staging spans and any failed caller prefix are cleared. Function
998 contains plaintext; this performs no encryption or transport authentication.
Successful output must be cleared by its caller and kept out of general parsers
and logs. No live credential entry, response acceptance or sending is enabled.
See [ADR 0038](docs/adr/0038-pin-tan-request-envelope-assembly.md).

Assembled-request metadata and response-reference comparison now link original
response segments to their actual outgoing roles without taking credential owners
or encoded request bytes. Matching references do not authenticate envelope identity,
validate returned users/parameters or establish execution success. Correctly scoped
errors stay errors; unknown data and wrong roles remain explicit issues. Comparison
is pure and supplies no submission evidence, response consumption or replay barrier.
See [ADR 0039](docs/adr/0039-pin-tan-assembled-request-reference-binding.md).

Assembled initialization semantics now require the exact parameter response source,
compare bank/user/customer and envelope identity observations, and interpret status
through bound request roles. Signature success cannot substitute for identification
execution. Matching results remain untrusted reports and authorize no session or
cache use; unknown parameters and unsupported procedure/challenge reports require
review. Shared parameter checks preserve unsigned comparison behavior. See
[ADR 0040](docs/adr/0040-pin-tan-initialization-response-semantics.md).

Assembled synchronization now requires exact report provenance, a unique matching
report shape, bound execution status and consistent envelope/recovery identities.
It preserves reported counters and system IDs without applying them. Matching
evidence always requires closing and reinitialization; it creates no ready session,
submission evidence or replay protection. Shared report checks retain the unsigned
comparator's restrictions. See
[ADR 0041](docs/adr/0041-pin-tan-synchronization-response-semantics.md).

The assembled initialization attempt now pins one immutable candidate, recomputes
response checks and returns scoped evidence once under an absolute deadline.
Terminal outcomes and disposal release pending metadata; late cancellation,
expiry or clock failure withhold evidence. Diagnostics contain scalar fields only.
It takes no credentials or encoded output and proves no transmission, session
authentication or cross-instance replay protection. See
[ADR 0042](docs/adr/0042-pin-tan-initialization-attempt-lifecycle.md).

The assembled synchronization attempt now pins candidate and recovery context,
rejects invalid recovery inputs before ownership and returns scoped evidence once.
Every terminal path releases both references; snapshots contain scalar diagnostics.
Matching evidence requires closing/reinitialization and never applies identifiers
or counters. Its deadline ends at handoff and does not time subsequent closing.
No credentials, output buffers, authentication or transmission are owned here. See
[ADR 0043](docs/adr/0043-pin-tan-synchronization-attempt-lifecycle.md).

PIN/TAN dialogue-end encoding now requires matching synchronization evidence and
explicit closing header/request metadata. It binds dialogue, counters, identity,
profile and procedure before accessing a PIN owner. The PIN-only envelope uses
bounded staging cleared on every exit and erases published output on late failure;
successful caller output must be cleared separately. It applies no recovery values,
consumes no lifecycle handoff and proves no closure or authentication. See
[ADR 0044](docs/adr/0044-pin-tan-dialogue-end-context-encoding.md).

Assembled closing-response comparison now requires the exact candidate dialogue,
counters, profile and envelope identity. Replies map to actual outgoing roles;
signature/wrapper success cannot qualify as HKEND termination. Closure and abort
remain distinct from review; raw flags survive foreign scope and must not be used
as matching outcomes. Comparison consumes no response and proves no socket
closure, authentication or session readiness. See
[ADR 0045](docs/adr/0045-pin-tan-closing-response-binding.md).

The assembled closing attempt now pins one candidate, recomputes response checks
and returns scoped closure/abort/review evidence once under an absolute deadline.
Every terminal outcome releases the pending candidate and inherited synchronization
metadata; snapshots contain scalar diagnostics. Reported closure requires fresh
initialization; an abort cannot restart closing. The synchronization and closing
phases have separate deadlines and no automatic dispatch or cross-instance replay
registry. Credentials, output buffers and transport remain outside this owner. See
[ADR 0046](docs/adr/0046-pin-tan-closing-attempt-lifecycle.md).

The explicit assembled initialization procedure path now recognizes parsed HITANS
and preparation-scoped 3920 observations only from the same bound response. It
retains all other initialization checks, rejects source mixing and withholds matching
objects for ambiguous or missing selection/permission evidence. The initialization
attempt hands combined evidence out once under its existing deadline. Component
execution evidence alone does not qualify a procedure; combined issues and the
transition must be inspected. Activation remains pending. See
[ADR 0047](docs/adr/0047-assembled-initialization-procedure-integration.md).

Combined initialization now integrates HIPINS only from the same parameter tree
and bound response as the TAN/permission evidence. Missing bounds remain unknown;
zero or conflicting lengths, duplicate flags and unsupported versions require
review. Only a fully matching combined result exposes qualified operation flags;
N grants no permission or SCA exemption. One-time handoff shares the initialization
deadline and terminal cleanup. No credential validation or HIPINS/HITANS length
precedence is implemented. See
[ADR 0048](docs/adr/0048-assembled-initialization-hipins-requirements.md).

First-read signature context now reuses the complete returned initialization
requirements result, requiring the same dialogue, next counters, original identity
and unchanged procedure selection. Matching component evidence cannot bypass
outer requirements issues. Qualified objects come from the exact returned sources;
unlisted operations, continuation tokens and any unresolved context require review.
This pure comparator adds no replay gate, account authorization, credential access,
signature assembly or transport. See
[ADR 0049](docs/adr/0049-first-read-pin-tan-signature-context.md).

Initialization can now explicitly integrate read schemas from the exact shared
parameter tree. Known read-advertisement references bind to preparation; unknown
versions and unrelated data remain unresolved. The initialization attempt shares
its existing one-time handoff and cleanup across the new acceptance path.
First-read single-account comparison reuses permission, identity, advertisement
and request-option checks. Missing/duplicate observations, unsupported signature
counts and unhandled limits require review. Only the complete outer result exposes
qualified account/advertisement objects. No account authorization, credential access,
state activation or sending is added. See
[ADR 0050](docs/adr/0050-initialization-read-capability-integration.md).

First-read credential comparison now checks one supplied PIN or TAN against a
fully matching account/capability context. It preserves missing bounds, applies
reported TAN maxima independently and checks numeric/text formats. The result is
scalar flags only; the comparator retains no credential bytes or lengths, accesses
no credential owner and leaves input unchanged. Callers must clear their buffers.
A matching value does not prove credential completeness, TAN placement, challenge
scope, authenticity or SCA exemption. See
[ADR 0051](docs/adr/0051-first-read-credential-requirement-comparison.md).

PIN-only first-read trailer encoding now checks reported bounds on the actual
owned PIN staging bytes. Unresolved context and missing bounds fail before owner
access; invalid text or out-of-range PINs emit no output. Shared cleanup clears
temporary buffers and any secret prefix copied before a late failure. Cancellation
clears the owner, and encoding does not renew its lifetime. A successful PIN-only
component does not establish that TAN omission is permitted or that the request
is ready to send. See
[ADR 0052](docs/adr/0052-first-read-pin-only-trailer-encoding.md).

Plain first-read PIN-only assembly now preserves the exact bound dialogue,
signature/read fields and logical segment roles. It computes final framing in
bounded staging and validates the actual owned PIN through the trailer writer.
Failure publishes no message; a late failure erases the entire copied message
prefix. Only public metadata enters string building. Successful output contains
plaintext PIN bytes that the caller must clear. Assembly grants no TAN omission,
SCA readiness or sending authority. See
[ADR 0053](docs/adr/0053-first-read-pin-only-request-assembly.md).

## Supported versions

There are no supported public versions yet.

| Version | Security support |
| --- | --- |
| None | No distributable version exists |

This table must be updated before the first external distribution.

## Intended trust boundary

The accepted product boundary is a purely user-operated client:

- the installed client communicates directly with a user-configured financial
  institution endpoint;
- Broiler operates no credential relay, account aggregation, payment
  initiation, or financial-data backend;
- financial data and secrets remain on the user's device except when sent to
  the selected institution as part of an explicit banking operation; and
- the product contains no telemetry, analytics, tracking identifier, automatic
  crash upload, or background reporting path.

These are architecture requirements, not claims that unfinished features have
already been made secure.

## Reporting a vulnerability

The private vulnerability-reporting channel is **not configured yet**. This is
a release blocker: the project must not be distributed externally until a
monitored private channel, responsible owner, response process, and public
instructions have been established here.

Until then:

- do not place credentials, PINs, TANs, account data, profile files, packet
  captures, or undisclosed vulnerability details in a public issue; and
- if you already have access to a project maintainer through a private trusted
  channel, send only enough information to establish contact and agree on a
  secure transfer method.

No response-time or remediation-time commitment exists before that reporting
process is established.

## Release security evidence

Every distributed candidate must pass the repository's prerelease checklist
and receive the two-person approval recorded in [HUMAN_REVIEW.md](HUMAN_REVIEW.md).
Approval applies only to the recorded source revision and artifact digests.
Payment-capable candidates also require an independent penetration test of the
payment path and closure of release-blocking findings.
