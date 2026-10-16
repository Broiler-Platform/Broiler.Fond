# Security policy

## Current development status

Milestone 1 implementation adds manual endpoint validation, confirmation, and
quarantine; in-memory identity allocation and startup integrity checks; a pure
account rediscovery and exact-money value projection layer; the FinTS 3.0 PIN/TAN read
and synchronization protocol stack (bounded HTTPS transport, session coordinators,
capability activation, read store ingestion, and execution pipelines); encrypted local
diagnostic persistence and privacy-conscious support export; production profile store
lifecycle with atomic promotion and privacy purge; deterministic ambiguous-rediscovery
account relinking; FinTS application registration and interactive/decoupled SCA continuation;
and the Windows host application architecture (`Broiler.Fond.Host.Windows`) with accessible
dialogs and ephemeral credential zeroization to the M0 foundation. Payments remain strictly
out of scope. No live institution support is published without controlled bank acceptance
evidence, and no release approval is claimed before completion of the human release gate.

The storage format candidate has public synthetic vectors, full M1 financial
(`store-v1.xsd`), bootstrap (`bootstrap-v1.xsd`), and purge-intent
(`purge-intent-v1.xsd`) schemas, bounded hostile-input ZIP preflight validation,
supported-floor PBKDF2-HMAC-SHA256 calibration (`600,000..5,000,000` iterations),
and BCL reference codecs in the verification project ([ADRs 0004](docs/adr/0004-profile-envelope-v1.md),
[0005](docs/adr/0005-profile-snapshots-v1.md), [0087](docs/adr/0087-profile-storage-schemas-preflight-and-calibration.md)).
The fixed fixture keys/nonces must never protect user data. Production profile
lifecycle (`M1-02`), seekable streaming readers, cross-host execution evidence,
and human security review remain prerequisites for enabling profile persistence.

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

First-read PIN-only envelopes now wrap only the local assembler's output using
exact binary payload and message lengths. Immutable candidate metadata retains
the qualified context and actual segment roles, without credentials or owners;
its account/identity evidence remains private. Bounded staging is always erased,
and a late ownership failure clears the complete published envelope prefix.
Successful output contains plaintext PIN bytes for the caller to clear. Candidate
creation and encoding prove no TAN/SCA completeness or authenticated response.
See [ADR 0054](docs/adr/0054-first-read-pin-only-request-envelope.md).

Assembled first-read response binding now checks envelope profile, exact dialogue,
message counters and actual request segment roles. Signature reference 2 remains
distinct from read reference 3. Original replies/data and target objects are
retained without rewriting, response consumption or credential access. Matching
references prove no execution, payload validity, exact version/account identity,
envelope identity, authentication or replay protection. These remain separate
semantic and session requirements. See
[ADR 0055](docs/adr/0055-first-read-response-reference-binding.md).

Assembled first-read semantics now require typed data from the exact bound response
and compare envelope system/key identity, report versions, account identities,
currencies and read-scoped statuses. Review issues withhold qualified read data;
raw observations remain private and untrusted. Zero balances, unavailable reports
and partial data remain distinct, and retained tokens authorize no continuation.
Matching semantics establish no submission, TAN/SCA completion, authentication,
freshness, replay protection or cached account update. See
[ADR 0056](docs/adr/0056-assembled-first-read-response-semantics.md).

A bounded assembled first-read attempt now pins one candidate under an absolute
deadline, recomputes response binding/semantics and hands scoped evidence out
once. Foreign references leave it pending without renewing time; scoped review
and execution/partial/unavailable outcomes terminate and release metadata. Late
expiry, cancellation, clock failure or abandonment withholds handoff. Snapshots
contain only scalar diagnostics. Returned private evidence remains caller-owned;
no credential, token or response is retained after terminal cleanup. The attempt
provides no cross-attempt replay guard, SCA completion or automatic continuation.
See [ADR 0057](docs/adr/0057-assembled-first-read-attempt.md).

Assembled balance-continuation context now checks a proposed second-page request
against qualified partial-read evidence, preserving exact tokens, account/options,
dialogue and sequential counters. It inherits the original capability/account
objects and withholds qualified context on every mismatch. Raw source evidence
remains private. It reopens no attempt, consumes no token and proves no freshness,
signature readiness, SCA completion, encoding or sending authority. See
[ADR 0058](docs/adr/0058-assembled-balance-continuation-context.md).

Balance-continuation signature context now requires the complete qualified
continuation plus matching original identity/procedure selection, header roles
and explicit control reference. Every issue withholds qualified requirements and
procedure objects. Supplied timestamps/security references remain observations;
matching context proves no credential completeness, freshness, replay protection,
TAN/SCA readiness or submission. No credentials or encoded output are retained.
See [ADR 0059](docs/adr/0059-balance-continuation-pin-tan-signature-context.md).

Continuation PIN-only trailer encoding now gates on the complete continuation
signature context before PIN access and applies inherited bounds to actual copied
PIN bytes. It preserves the existing bounded staging, lifetime/cancellation checks
and failed-output cleanup. Successful output contains plaintext PIN bytes for the
caller to clear. Encoding neither consumes a TAN nor establishes TAN omission,
SCA readiness or a complete sendable request. See
[ADR 0060](docs/adr/0060-continuation-pin-only-trailer-encoding.md).

Continuation plain request assembly now wraps the qualified header, balance
request and PIN-only trailer in exact message-3 framing. It checks context and
output bounds before owner access, preserves actual PIN checks, and erases the
whole published prefix on late failure. Temporary message storage is always
cleared; successful plaintext output remains caller-owned. It establishes no
TAN omission, SCA readiness or permission to send. See
[ADR 0061](docs/adr/0061-continuation-pin-only-request-assembly.md).

Continuation request envelopes now retain qualified message-3 metadata separately
from first-read candidates and preserve the exact continuation payload as binary
HNVSD bytes. Public wrapper fields come from the bound header. All nested PIN
checks remain active, temporary buffers are cleared and final failure erases the
complete published envelope prefix. Metadata contains private account/identity
evidence but no credential owner or encoded secret bytes. This plaintext wrapper
authenticates nothing and grants no TAN omission, SCA readiness or submission.
See [ADR 0062](docs/adr/0062-continuation-pin-only-request-envelope.md).

Continuation-response reference binding now requires the candidate's exact
message-3 dialogue/counters and envelope profile and retains links to actual
assembled segment roles. The retained predecessor response cannot qualify for
the second page. Errors, pending/partial reports, repeated tokens, duplicates and
unknown data remain observations; matching references do not establish execution,
account/envelope identity, token progress, authentication or replay protection.
Evaluation consumes no state and handles no credentials. See
[ADR 0063](docs/adr/0063-continuation-response-reference-binding.md).

Continuation-response semantics now require typed data from the exact bound
response, match envelope identity and compare the inherited account/version/currency
scope at read segment 3. Any review issue withholds qualified data. Predecessor
or separately parsed data cannot supply a token; same-source raw tokens remain
observations even on review. Page amounts are not combined or ingested, and a
repeated token proves no progress or permission to continue. The pure comparison
handles no credentials, schedules nothing and provides no authentication or replay
protection. See [ADR 0064](docs/adr/0064-continuation-response-semantics.md).

A bounded assembled continuation attempt now pins one second-page candidate
under an absolute deadline, recomputes continuation binding/semantics and hands
scoped evidence out once. Foreign references leave it pending without renewing
time; scoped review and execution/partial/unavailable outcomes terminate and
release metadata. Late expiry, cancellation, clock failure or abandonment
withholds handoff. Snapshots contain only scalar diagnostics. Returned private
evidence remains caller-owned; no credential, token or response is retained
after terminal cleanup. The attempt provides no cross-attempt replay guard,
SCA completion or automatic continuation.
See [ADR 0065](docs/adr/0065-assembled-continuation-attempt.md).

Later-page balance continuation context now checks proposed requests against
prior continuation evidence for pages beyond page two. It enforces a strict
128-page limit boundary (PageLimitExceeded = 1024), sequential message counter
increments, exact unchanged operation/account/options scope, and identical opaque
continuation tokens without normalization. Predecessors must be fully matching
and report partial balance outcome; review or foreign tokens cannot qualify continuation.
Capability provenance is inherited recursively without replacement. The pure comparison
handles no credentials, schedules nothing and provides no authentication or replay protection.
See [ADR 0066](docs/adr/0066-later-page-balance-continuation-context.md).

A multi-page PIN/TAN balance refresh attempt now coordinates the session-level
lifecycle across page one and subsequent continuation pages under a single
lifetime deadline and configurable page budget (1..128, default 16). It enforces
strict message counter monotonicity (+1 increments), validates predecessor
evidence and origin capability provenance to prevent foreign-account candidate
hijacking, and enforces response replay defenses. Balances across all accepted
pages are aggregated without overwriting or double-counting, projecting losslessly
into domain `BalanceSnapshot` models. Terminal states immediately release pending
metadata and snapshot diagnostics expose only scalar values without credentials,
tokens, accounts, or raw responses. Concurrency is safely synchronized under an
internal gate. See [ADR 0067](docs/adr/0067-assembled-balance-refresh-attempt.md).

All-account capability context now evaluates whether all-account SEPA account
discovery (`HKSPA:3:1++J'`) can proceed under PIN/TAN by comparing one
`FinTsPinTanReadSignatureContext`, its bound `FinTsReadParameterSet`, and user/bank
parameter evidence. It validates signature context, integrated read parameters,
HISPAS-1 advertisements, customer signature requirements, unlisted blocked operation
policies, limits, and dialogue consistency. Only clean results expose qualified
advertisements and operation requirements. Credential comparison inspects caller-owned
PIN and optional TAN byte spans against HIPINS bounds and active procedure rules
without copying, logging, or retaining credentials. Unresolved or review states
withhold qualified requirements. Pure evaluation consumes no state and handles no
credentials. See [ADR 0068](docs/adr/0068-assembled-all-account-capability-context.md).

All-account discovery request envelopes now wrap plain all-account discovery
candidates with exact outer framing, `HNVSK:998:3`, and single-binary `HNVSD:999:1`
payloads using caller-owned PIN credentials. Preflight checks validate context
matching and full buffer reserves before credential access. PIN bytes are validated
against HIPINS length bounds and active TAN procedure rules through trailer writing.
Failures across any of the nine ownership observation boundaries immediately zero
the published prefix while leaving tails untouched, with full PIN storage erasure
and exact copy accounting. Envelope output contains plaintext secrets and must be
cleared by callers; the wrapper performs no encryption, authentication, or live
transmission. See [ADR 0069](docs/adr/0069-all-account-request-envelope.md).

All-account response reference binding and semantics now evaluate bank responses to
assembled all-account discovery requests under PIN/TAN. Reference binding correlates
response framing, dialogue, profile versions, message counters, and body segments
against the candidate segment map. Semantic evaluation enforces strict source object
provenance (rejecting byte-identical reparsed instances), verifies envelope identities
against signature headers, scopes status reports strictly to read segment 3, and matches
returned accounts against bound parameters and advertisements. Missing, duplicate,
conflicting, or unmatched account observations yield review issues, withholding qualified
read data. Pure evaluation consumes no state, handles no credentials, and performs no
account ingestion or session activation. See [ADR 0070](docs/adr/0070-all-account-response-semantics.md).

A bounded assembled all-account discovery attempt now coordinates candidate metadata
and one-time evidence handoff under an absolute monotonic deadline. Start pins one
candidate and deadline without handling credentials, encoding, or sending. Response
acceptance validates exact-source dataset provenance, computes candidate binding,
and evaluates semantics under a reentrancy lock. Foreign scope mismatches produce
transient diagnostics without consuming the attempt. Terminal states (execution,
unavailable, review, timeout, cancellation, stop, or clock regression) release pending
candidate metadata immediately. Handed-out evidence is owned by callers; the attempt
persists no account profiles, schedules no live retries, and performs no session
activation. See [ADR 0071](docs/adr/0071-assembled-all-account-discovery-attempt.md).

An assembled PIN/TAN read session attempt coordinator now manages multi-stage
dialogues across initialization, operational read (all-account discovery or multi-page
balance refresh), and closing stages under a single monotonic deadline. Dialogue ID
pinning, sequential message counter verification, response replay defenses, and
transient foreign-scope segregation protect against cross-stage dialogue injection and
replay. Terminal outcomes release all pending candidate references immediately, and
snapshots expose strictly scalar diagnostics. Evidence handoffs remain caller-owned; the
kernel initiates no network traffic, schedules no background retries, and stores no
credentials or accounts. See [ADR 0072](docs/adr/0072-assembled-read-session-attempt.md).

An assembled PIN/TAN synchronization session attempt coordinator now manages two-stage
synchronization dialogues across dialogue synchronization (HKSYN) and dialogue termination
(HKEND) under a single monotonic deadline. Dialogue ID pinning, sequential message counter
verification, response replay defenses, and transient foreign-scope segregation protect
against cross-stage dialogue injection and replay. A clean closure transitions to
`ReinitializationRequired` with a caller-owned terminal evidence handoff containing both
synchronization and closing evidence, enforcing FinTS 3.0 session reinitialization rules.
Terminal outcomes release all pending candidate references immediately, and snapshots
expose strictly scalar diagnostics. The kernel initiates no network transport, schedules
no background retries, and stores no credentials, system IDs, or accounts. See
[ADR 0073](docs/adr/0073-assembled-synchronization-session-attempt.md).

Synchronization session elapsed time now comes from the original start timestamp,
avoiding cumulative truncation at frequent clock observations. Clock conversion
exceptions and negative elapsed time terminate with fixed scalar diagnostics;
reentrant cancellation or abandonment remains terminal and cannot publish evidence.
See [ADR 0074](docs/adr/0074-synchronization-session-clock-boundaries.md).

Session termination now also releases accepted evidence, dialogue identifiers and
read-session collection contents. Caller-owned results and collection snapshots
remain usable, while scalar diagnostic counts survive cleanup. This releases managed
references; it does not erase caller-owned data or guarantee physical memory erasure.
Read closing evidence must be retained from the closing result. See
[ADR 0075](docs/adr/0075-session-terminal-evidence-ownership.md).

Read-session operation, continuation and closing candidates must now reference the
exact initialization and predecessor evidence accepted by that session. Equal wire
values or independent reevaluation do not establish this provenance. Rejected
candidates leave the active state and deadline intact and expose only a scalar
source-mismatch issue. This local binding does not authenticate a bank response.
See [ADR 0076](docs/adr/0076-read-session-evidence-provenance.md).

Read sessions now allow one discovery or balance operation after initialization,
followed by closing. The balance path requires HKSAL; a valid HKSPA candidate
cannot be treated as a balance read. Completed or page-limited operations cannot
restart, switch operation or reset their page budget. Partial results at the
budget return `PageLimitReached`, preserving their data without reporting complete
execution. See [ADR 0077](docs/adr/0077-read-session-operation-scope.md).

Read-session retry diagnostics now clear only when a candidate is accepted or an
in-scope response reaches semantic publication. Current response issues replace
resolved mismatches, including genuine binding errors on review outcomes. Foreign
responses and wrong-state calls leave unresolved diagnostics visible. Page-limit
truncation survives dialogue closing, abort and review so successful closure does
not imply a complete read. See
[ADR 0078](docs/adr/0078-read-session-diagnostic-lifecycle.md).

Every read-session initialization overload now hands its evaluated evidence to the
caller, including semantic review. The most complete qualification controls
`HasMatchingEvidence`; a valid base initialization cannot override rejected
procedures or requirements. Publication remains one-time and source-bound, and
cancellation or expiry before publication returns no partial evidence. Caller-owned
results survive session cleanup. See
[ADR 0079](docs/adr/0079-read-session-initialization-handoffs.md).

Read sessions now advance evaluated response counts and expected sequential bank
message numbers when an in-scope response is evaluated, including semantic review outcomes
and bank aborts. Foreign responses, counter replays, wrong-state calls, and pre-publication
interruptions preserve response counts and sequential counters unchanged. The progress
monotonicity invariant `ResponsesHandled <= RequestsRecorded` is enforced across all
lifecycle states, terminal transitions, disposal, and concurrent races. See
[ADR 0080](docs/adr/0080-read-session-response-accounting.md).

Synchronization sessions similarly track sequential bank message numbers across bank
abort and review outcomes (`BankMessageNumber = 1` on sync abort/review, `BankMessageNumber = 2`
on closing abort/review/close). Transient candidate errors (`DialogueIdMismatch`,
`MessageNumberMismatch`, `ClosingBindingNeedsReview`) and transient response issues are cleared
upon accepted retries. The bank-assigned dialogue identifier is pinned during open dialogue
stages and released upon terminal transitions per ADR 0075. The progress monotonicity invariant
`ResponsesHandled <= RequestsRecorded` and concurrency atomicity across terminal paths are
enforced. See [ADR 0081](docs/adr/0081-synchronization-session-diagnostic-lifecycle.md).

Bank connections and synchronization recovery application enforce pure fail-closed
transitions and credential boundary invariants. The immutable `BankConnection` domain
entity models operational connection states (`Unconfigured`, `Authenticating`,
`ChallengeRequired`, `Connected`, `ReauthenticationRequired`, `TemporarilyUnavailable`,
`Disabled`) with plain-language recovery actions. Banking credentials (PIN/TAN) are
never stored on this entity or in local archives. Recovery application verifies
session evidence, dialogue closure, unique reports, and identity matching before
applying bank-assigned system identifiers (Mode 0) or synchronized message counters
(Mode 1). Disabled connections, aborts, reviews, and malformed reports fail closed
without updating synchronization state or corrupting connection attributes. See
[ADR 0082](docs/adr/0082-synchronization-recovery-application.md).

Bounded FinTS HTTPS transport and capability activation enforce secret-safe network
boundaries and verified parameter projection. Canonical `AccountCapability` domain
entities represent operation and version advertisements with fail-closed validation.
Parameter activation projects parsed BPD and UPD data into active or suspended
capabilities, rejecting disabled or unsynchronized connections and mismatched institution
identities. The bounded `FinTsHttpsTransport` strictly enforces absolute HTTPS endpoints,
rejects userinfo, fragments, and non-HTTPS schemes, bounds request and response payload
sizes (128 KiB request, 1 MiB response with streaming cutoff), applies monotonic timeouts,
and guarantees that raw request/response bytes (including transient PIN and TAN secrets)
are never leaked into exception diagnostics, error messages, or crash reports. See
[ADR 0083](docs/adr/0083-fints-transport-and-capability-activation.md).

FinTS read data ingestion and store binding connect verified read session results to the
local profile identity ledger with pure fail-closed rejections and atomic batch allocation.
`FinTsAccountSourceLocatorFactory` constructs lossless locators preserving raw domestic
numbers, leading zeros, whitespace, letter casing, subaccounts, and country/BIC identifiers.
`FinTsReadIngestionApplication` validates connection operational state and terminal session
execution reports, rejecting disabled/unsynchronized connections, bank aborts (`9800`), and
semantic reviews. Accounts discovered across `HISPA`, `HISAL`, and `HIUPD` evidence are
resolved through `AccountRediscoveryPlanner`: existing bindings are reused, new accounts
are allocated in `Active` status, and conflicting locators (such as unexpected currency
mutations) are quarantined in `Quarantined` status to protect against silent ledger
corruption. Booked, available, and credit-line balance snapshots with bank-reported timestamps
are ingested only for active bindings after validating currency recognition, and all ledger
entities are committed in an atomic batch that rolls back upon concurrency conflicts
(`StaleIdentityBatchException`). Banking credentials remain strictly transient. See
[ADR 0084](docs/adr/0084-fints-read-ingestion-and-store-binding.md).

The FinTS read execution pipeline coordinates end-to-end HTTPS banking communication, dialogue state
machine transitions, continuation pagination, dialogue termination, and store ingestion. Preflight
validation rejects disabled, unsynchronized, unconfirmed, or wrong-credential calls. Wire requests are
encoded into transient staging buffers and wiped with `CryptographicOperations.ZeroMemory` immediately
after transmission. Bank aborts (`9800`) transition connection state to `TemporarilyUnavailable` and
semantic reviews map to `ReauthenticationRequired`. Default closing candidates (`HKEND`) strictly
inherit security and dialogue parameters from accepted initialization evidence. Session credentials
(PINs/TANs) and HNSHA segment contents are excluded from all diagnostic issues and logs. See
[ADR 0085](docs/adr/0085-fints-read-execution-pipeline.md).

The FinTS synchronization execution pipeline coordinates end-to-end HTTPS synchronization dialogues across
transport, dialogue state machine, closing termination, and recovery application. Preflight checks reject
disabled, unconfirmed, or invalid credential states. Wire requests are encoded in transient staging buffers
and zeroed with `CryptographicOperations.ZeroMemory` immediately following transmission. Dialogue aborts
(`9800`) transition connection state to `TemporarilyUnavailable` and semantic reviews map to
`ReauthenticationRequired`. Default closing candidates (`HKEND`) synthesize matching headers inheriting
security parameters and newly assigned or recovered system IDs. Credentials and signature trailers are
strictly omitted from all diagnostics, exceptions, and issue lists. See
[ADR 0086](docs/adr/0086-fints-synchronization-execution-pipeline.md).

The M1 profile storage format candidate now defines complete financial-document schemas (`store-v1.xsd`)
and purge-intent schemas (`purge-intent-v1.xsd`). All documents are validated against strict W3C XML schemas
with DTD prohibited, external resolution disabled, character limits enforced, and depth bounded to 8.
Ancestry metadata preserves parent branch linkages and recovery provenance for roll-forward branches.
Production bounded ZIP preflight directly inspects raw bytes prior to archive instantiation, enforcing single-volume
unencrypted structures, compression methods 0 and 8 only, exclusion of ZIP64/extra fields/archive comments,
name traversal/separator rejection, sequential contiguity, and a 128:1 expansion ceiling to halt decompression bombs.
Supported-floor KDF calibration measures statistical multi-sample timing against target envelope latency
(500–1,000 ms median, max 2,000 ms p95) within 600,000–5,000,000 PBKDF2 iterations, enforcing the fail-closed
security invariant that the candidate floor (600,000) is never lowered automatically. Banking credentials
(PINs/TANs) remain strictly absent from all stored documents and purge manifests. See
[ADR 0087](docs/adr/0087-profile-storage-schemas-preflight-and-calibration.md).

The production profile store lifecycle (`IProfileStore`, `ProfileStore`, `SeekableAuthenticatedProfileStream`,
`ProfileEnvelopeCodec`, `StoreArchiveCodec`) enforces single-writer directory mutual exclusion using an exclusive
OS file lock (`profile.lock`), rejecting concurrent access with `ProfileStoreLockedException`. Generation updates
are staged in temporary staging files and committed via atomic promotion, preserving prior generations for crash
resilience. Branch authorization protects roll-forward ancestry and detects branching conflicts (`ProfileBranchingException`).
Passphrase rotation executes whole-container re-encryption without in-place key reuse. Privacy purge permanently
erases superseded generations through multi-pass cryptographic overwriting (cryptographic random followed by zero-fill)
before file unlinking, coordinated by atomic purge-intent journals (`purge-intent-v1.xsd`) with crash recovery.
Seekable authenticated profile streams enforce per-frame AES-256-GCM verification before returning plaintext bytes,
and `ProfileMasterKeySession` securely zeroes cached derivation keys upon locking or disposal. See
[ADR 0088](docs/adr/0088-production-profile-store-lifecycle.md).

Connection removal and cached-data purge protocol (`ConnectionRemovalPlanner`, `IProfileStore.RemoveConnection`)
enforce explicit user consent and verifiable erasure. Connection deletion transitions connection state, strips
associated bank identifiers, and optionally executes privacy-purged removal of cached accounts, incarnations,
bindings, balance snapshots, and associated diagnostic archives. Opt-in diagnostic persistence (`EncryptedLocalDiagnosticStore`)
records structured operational events to an encrypted local file (`diagnostics.bffond`) using the active
`ProfileMasterKeySession`, ensuring diagnostics cannot be decrypted without the user's profile passphrase. Redacted
support export (`ExportReviewedSupportPreview`) generates a SHA-256 preview-digest bound archive containing solely
pre-reviewed sanitized metadata with absolute exclusion of banking credentials, PINs, TANs, account numbers, and
financial balances. No background reporting or telemetry transport exists. See
[ADR 0089](docs/adr/0089-connection-removal-and-encrypted-diagnostics.md).

Ambiguous-rediscovery account relinking (`AccountRelinkingPlanner`, `IProfileStore.RelinkAccountBinding`) provides
deterministic, auditable resolution when institution accounts mutate domestic locators or appear in ambiguous states.
Rather than silently conflating accounts or guessing identity mappings, conflicting candidates are held in `Quarantined`
status. Relinking executes an authenticated ledger transaction: the source binding transitions from `Quarantined` to
`Active` pointing to the verified target account incarnation, with all prior balance snapshots and historical provenance
intact. If the source account shell was an orphaned rediscovery candidate, it is pruned from the profile generation
and purged via cryptographic overwrite, preventing ghost account pollution while preserving strict auditability. See
[ADR 0090](docs/adr/0090-ambiguous-rediscovery-account-relinking.md).

FinTS product registration (`FinTsProductRegistration`, `KernelProduct.FinTsRegistration`) and interactive/decoupled
SCA challenge continuation (`FinTsScaExecutionCoordinator`) establish rigorous protocol identity and challenge handling.
Product registration enforces standard FinTS 3.0 identification parameters (`Broiler Fond - Finance on Demand`, registered
vendor ID, and product version) in all dialogue initialization envelopes. The SCA coordinator manages multi-step
authentication dialogues (HITANS v6/v7 challenge inspection, user notification of decoupled app approval, challenge
code/matrix display, and HKTAN status polling) under an absolute monotonic deadline. Banking PINs and TANs are held only
in ephemeral buffers and wiped with `CryptographicOperations.ZeroMemory` immediately following transmission. See
[ADR 0091](docs/adr/0091-fints-product-registration-and-sca-continuation.md).

The Windows host application architecture (`Broiler.Fond.Host.Windows`) implements an accessible, responsive presentation
layer adhering strictly to the user-operated security boundary. All views (`MainForm`) and dialogs (`EndpointConfirmationDialog`,
`ProfileUnlockDialog`, `CredentialPromptDialog`, `ScaChallengeDialog`, `ConnectionRemovalDialog`, `SupportExportDialog`)
provide keyboard navigation, high-DPI scaling, and accessible descriptions (`AccessibleRole`, `AccessibleName`).
Balances are displayed with truthful freshness indicators (< 1h, < 24h, Stale, Offline) and strict multi-currency
isolation, preventing misleading mixed-currency summations. Ephemeral credential prompts handle user PINs and TANs
using masked text inputs, zeroizing underlying character and byte buffers in deterministic `finally` blocks. Automated
headless self-tests (`HostSelfTest`) verify security boundaries, dialog accessibility, balance truthfulness, and purge
behavior in CI without user interaction. See [ADR 0092](docs/adr/0092-windows-host-ui-and-support-workflows.md).

User-reviewed ambiguous-rediscovery account relinking UI (`AccountRelinkingDialog`) connects the kernel's deterministic
relinking planner to an accessible, review-first presentation workflow. The dialog displays live plan previews
detailing the exact state transition (`Quarantined` to `Active`), target account incarnation, candidate shell pruning,
and storage action (verified privacy purge vs standard save). Execution requires deliberate profile passphrase confirmation
to authorize encrypted generation write and cryptographic purging of orphaned candidate shells. Automated self-tests
verify the relinking workflow, single generation retention on disk, permanent balance provenance, and session data
reloading. See [ADR 0093](docs/adr/0093-user-reviewed-account-relinking-ui.md).


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

Security vulnerabilities in Broiler Fond should be reported privately to the project
security maintainers rather than through public issue trackers or discussion forums.

### Reporting channel

- **Email:** `security@broiler.dev`
- **PGP Key ID:** `0xBF00F09D1040A56E`
- **Fingerprint:** `4E52 A91B 8B50 8B44 CB91 66AF B3C7 E203 E8BF 00F0`

When submitting a report, please include:
1. Product version and build commit.
2. Operating system and environment details.
3. Step-by-step reproduction instructions or a minimal test script using synthetic test fixtures.
4. An assessment of potential security impact (e.g. unauthorized memory disclosure, cryptographic flaw, boundary bypass).

> [!CAUTION]
> **Confidentiality rule:** Never include real banking credentials, PINs, TANs, production IBANs,
> real profile data containers (`.bffond`), or live packet captures in any vulnerability report. Use only
> synthetic/redacted values.

### Response SLA and coordinated disclosure

- **Acknowledgement:** Within 48 hours of receipt.
- **Triage and severity assessment:** Within 5 business days.
- **Remediation and advisory:** We follow a coordinated 90-day disclosure window. Critical vulnerabilities
  will be prioritized for expedited patch release and coordinated disclosure.
- **Credit:** Reporters will be acknowledged in release notes and security advisories (unless anonymity is requested).

## Release security evidence

Every distributed candidate must pass the repository's prerelease checklist
and receive the two-person approval recorded in [HUMAN_REVIEW.md](HUMAN_REVIEW.md).
Approval applies only to the recorded source revision and artifact digests.
Payment-capable candidates also require an independent penetration test of the
payment path and closure of release-blocking findings.
