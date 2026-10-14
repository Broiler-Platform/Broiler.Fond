# Milestone 1 — Account access and balances

**Status:** In progress, internal development only. No live bank support.

**Updated:** 2026-09-11

The [roadmap](roadmap.md#m1--create-access-to-accounts-and-list-values) remains
the product specification. This backlog breaks M1 into reviewable increments;
completion of one increment does not imply completion of the alpha.

## Implemented increment: manual endpoint model

- [x] Create immutable manual institution configuration with an absolute HTTPS
      endpoint and a separately available ASCII hostname.
- [x] Reject relative/non-HTTPS URLs, embedded user information (including an
      empty user-info delimiter), fragments, raw whitespace/control characters,
      backslashes, invalid ports, and input longer than 2,048 characters.
- [x] Start unconfirmed and require explicit review of the complete displayed
      `FinTsEndpoint.AbsoluteUri`, including its port, path, and query.
- [x] Quarantine any effective endpoint change. Unchanged canonical URLs retain
      confirmation; path comparison is case sensitive. A stale URL review fails.
      Returning to a previously confirmed URL still requires a new confirmation.
- [x] Preserve manually supplied bank identifiers verbatim. A later connector
      must enforce the particular bank's required identifiers.
- [x] Verify malformed input, confirmation transitions, international hostnames,
      IP literals, length boundaries, metadata retention, and a sentinel secret
      in validation exceptions with the BCL-only test runner.

URI parsing provides canonical URL syntax, not proof of bank ownership. The
future setup UI must instruct users to copy from an authenticated bank channel,
display the complete URL and separate hostname, disclose the residual risk of
choosing the wrong institution, and call confirmation only after deliberate
review. Endpoint edits must invalidate the active session; reconfirmation must
be followed by fresh authentication. Neither the UI nor session integration is
implemented yet. Configuration objects alone cannot enforce these host actions.

## Storage format review candidate

- [x] Propose the exact 96-byte header, frame descriptors, nonce/AAD layout,
      authenticated final record, key derivation and resource bounds in
      [ADR 0004](adr/0004-profile-envelope-v1.md).
- [x] Propose bootstrap XML schemas, deterministic lexical rules, ZIP metadata,
      manifest validation and generation/recovery/purge protocol in
      [ADR 0005](adr/0005-profile-snapshots-v1.md).
- [x] Generate five independent public vectors with Python and verify their
      keys, per-frame authentication, complete ciphertext and hashes with .NET.
- [x] Add test-only BCL reference codecs and checks for Unicode/culture/length
      boundaries, header/record tamper, every truncation of the bootstrap
      envelope, frame splicing, hostile XML and unlisted/duplicate ZIP entries.
- [ ] Complete financial-document, ancestry and purge-intent schemas, production
      ZIP preflight and streaming, supported-floor KDF calibration, cross-host
      execution evidence and human format/security review.

These references live only in the verification project. The kernel and host
still expose no profile persistence or cryptographic operation. The format is a
review candidate, not frozen or approved for user data.

## Local identity allocation and validation

- [x] Add an immutable bounded identity inventory and one shared unsigned 64-bit
      allocation namespace for all entity kinds.
- [x] Stage IDs with their identity records and publish a complete validated
      snapshot; reject stale competing batches without overwriting the winner.
- [x] Preserve persisted counter gaps, reject overflow, and reserve MaxValue as
      the exhausted counter so NextId always exceeds every allocated ID.
- [x] Reject duplicate/zero IDs, unknown kinds, invalid counters, dangling or
      mistyped references, and cyclic revision predecessors at writable open.
- [x] Keep invalid source records intact, bound diagnostics/materialization, and
      verify a 100,000-record history without recursive traversal.
- [ ] Integrate full domain payloads, exclusive profile locking, durable commits,
      branch authorization/restore/import, source locators and rediscovery.

[ADR 0006](adr/0006-local-identity-allocation.md) defines the implemented contract
and the persistence responsibilities that remain. Atomic publication currently
applies to the in-memory identity inventory, not to a user profile on disk.

## Account source locators and rediscovery

- [x] Preserve the complete raw account locator, scoped bank fields, duplicate
      field multiplicity, and separate connection/account/incarnation/binding IDs.
- [x] Reuse only a unique exact active binding in a complete discovery batch.
- [x] Quarantine changed/ambiguous evidence, duplicate-looking occurrences,
      inactive bindings and explicit new-lifetime/closure evidence.
- [x] Keep recommendations immutable and preserve every occurrence; do not
      allocate IDs, relink accounts, mutate balances or infer closure from absence.
- [x] Verify 90 checks, including a complete 10,000-account batch, raw identity
      comparisons, resource bounds and integration with the local allocator.
- [ ] Integrate authenticated complete-batch collection, durable replay evidence,
      current-snapshot checks, full payload commits and user-reviewed relinking.

[ADR 0007](adr/0007-account-source-rediscovery.md) defines the decision rules and
their limitations. A planner recommendation does not authenticate a bank response
or establish that the same locator could never be reused by a bank.

## Account values and exact money

- [x] Validate money against a frozen currency reference and parse/add without
      silently rounding, truncating or converting monetary values.
- [x] Preserve balance snapshot/observation/account/incarnation/binding provenance,
      balance kinds, bank and retrieval timestamps, and source stale/type evidence.
- [x] Keep missing, unsupported, ambiguous and mismatched values visibly excluded;
      a supplied zero remains a real available value.
- [x] Preserve cached values after failed/partial refresh and offline operation,
      with explicit age, missing-time and clock-anomaly warnings.
- [x] Compute separate per-currency positive/negative/net totals with partial,
      unavailable and unrepresentable states; never mix balance kinds.
- [x] Add 109 checks, including precision boundaries and a 10,000-account projection.
- [ ] Integrate validated live capabilities, durable response/current-candidate
      selection, full payload commits, UI labels and warning presentation.

[ADR 0008](adr/0008-account-values-and-exact-money.md) defines these pure domain
projections. No balance retrieval, persistence or graphical UI is enabled.

## Synthetic read workflows and diagnostics

- [x] Add a bounded test-only script runner for authentication, one abstract
      manual challenge continuation, discovery and balances.
- [x] Exercise success, invalid credentials, locked access, cancellation,
      timeout, maintenance, changed parameters, malformed and partial reads.
- [x] Reject expired, foreign and replayed challenges; clear supplied synthetic
      PIN/TAN buffers on every exit and stop failures without automatic retries.
- [x] Add opt-in bounded structured in-memory diagnostics and detached immutable
      support previews with timestamps excluded by default.
- [x] Verify 280 checks for outcomes, exact account results, secret sentinels,
      retention, concurrency, preview fields and malformed scripts.
- [x] Preserve account refresh/offline warnings even when a balance snapshot is
      missing or ambiguous, as discovered by the partial-read integration check.
- [ ] Add actual FinTS framing/segment codecs and conformance fixtures, BPD/UPD
      and live SCA qualification; implement encrypted logging and export UI.

[ADR 0009](adr/0009-synthetic-workflows-and-diagnostics.md) and the
[scenario inventory](../tests/Broiler.Fond.Kernel.Tests/Simulation/README.md)
define the boundary. Typed synthetic outcomes are not FinTS wire responses.
No banking operation, persistent log or export/upload channel is enabled.

## FinTS byte syntax and outer framing

- [x] Parse bounded byte sequences with escaped syntax, byte-counted binary
      payloads, exact header identities and preserved empty field/component positions.
- [x] Keep unknown segment codes/versions as untrusted syntax and retain exact
      wire evidence independently of caller buffer mutations.
- [x] Encode individual text/binary elements from explicit bytes without
      guessing a negotiated charset or creating a banking message.
- [x] Check outer byte length, header/trailer consistency, message references
      and plain/reserved-wrapper numbering without opening security payloads.
- [x] Verify six independently generated public vectors and 1,350 checks,
      including every complete-frame truncation and exact resource boundaries.
- [ ] Implement typed administrative/response schemas, dialogue correlation,
      BPD/UPD, security-profile processing and authenticated domain ingestion.

[ADR 0010](adr/0010-fints-byte-syntax-and-framing.md) records the reviewed
specification, local limits and remaining scope. Syntax success is not proof
of a valid banking dialogue, supported business operation or authenticated bank.

## Typed replies and dialogue correlation

- [x] Validate HIRMG/HIRMS version 2 reply schemas with bounded raw text,
      positional parameters and message/request-segment scope.
- [x] Preserve receipt, reported execution, pending authorization, pagination
      and indeterminate-processing meanings; retain unknown codes and body segments.
- [x] Flag contradictory response classes without concealing the original evidence.
- [x] Track one pending local request, exact dialogue/reference IDs and separate
      message counters; reject mismatches and replays without consuming valid state.
- [x] Add atomic concurrent handling, terminal stop and counter exhaustion.
- [x] Verify five independent response fixtures and 177 checks, including a
      complete 9,999-exchange synthetic sequence through exhaustion.
- [ ] Implement BPD/UPD and security schemas, complete code-specific reactions,
      authenticated session processing and domain ingestion.

[ADR 0011](adr/0011-fints-responses-and-dialogue-correlation.md) defines this
single-response-per-request profile. Correlation is mechanical evidence, not
authentication, bank authorization or proof of successful execution.

## BPD/UPD parameter evidence

- [x] Parse bounded HIBPA#3 bank, HIUPA#4 user and HIUPD#6 account parameters.
- [x] Preserve raw identities, absent/zero restrictions, dialogue-scoped UPD,
      account-independent entries, duplicate occurrences and unknown segments.
- [x] Distinguish listed, unlisted-blocked, unknown and ambiguous permission
      evidence without claiming authorization or bank support.
- [x] Validate signature counts and optional limit shapes; keep extensions opaque
      and reject ambiguous shortened extension layouts.
- [x] Verify five independent parameter fixtures and 181 checks, including
      exact account/permission bounds, optional truncation and source-data isolation.
- [ ] Add operation-specific bank schemas, capability matching, authenticated
      context/collection, parameter activation, caching and change handling.

[ADR 0012](adr/0012-fints-parameter-evidence.md) records source specifications,
local restrictions and limitations. Parsed parameters activate no bank or user
capability and cause no endpoint, account, identity or persistence changes.

## Read-operation parameter schemas and capability evidence

- [x] Parse HISALS versions 6–8 and HISPAS versions 1–3 with bounded common
      constraints, version-specific flags and opaque SEPA format identifiers.
- [x] Preserve duplicates and unknown versions; assess an explicitly requested
      version without automatic selection or fallback.
- [x] Compare bank, user, account, institution, permission and signature evidence
      while retaining missing, conflicting, partial and indeterminate states.
- [x] Add five independent fixtures and 108 checks covering schemas, resource
      boundaries, exact scope and conservative comparison outcomes.
- [ ] Add security-profile validation, authenticated complete/fresh parameter
      collection, request/response codecs and actual capability activation.

[ADR 0013](adr/0013-read-capability-evidence.md) defines this single-account
comparison. Matching evidence is not permission to execute a banking operation.

## PIN/TAN response envelopes and parameter evidence

- [x] Validate HNVSK 3/HNVSD 1 for uncompressed PIN:1/PIN:2 response envelopes,
      retaining exact outer/inner bytes and enforcing a shared element budget.
- [x] Parse inner reply schemas explicitly and correlate inner references;
      reject nesting, signatures and client operations in this response slice.
- [x] Parse bounded HIPINS 1 fields, preserving omissions, zero, duplicates,
      future versions and J/N flags without selecting or authorizing a procedure.
- [x] Add five independent fixtures and 202 checks for schemas, limits,
      ambiguity, cancellation, source retention and safe diagnostics.
- [ ] Add authenticated procedure/session context, SCA state, credential
      handling and authenticated transport.

[ADR 0014](adr/0014-pin-tan-envelope-and-parameters.md) records the profile
restrictions. Structural parsing is not authentication or banking permission.

## TAN procedure and challenge schemas

- [x] Parse HITANS versions 6/7 with exact conditional fields for TAN input,
      separate-device approval and polling parameters, without scheduling work.
- [x] Parse HITAN versions 6/7, preserving raw challenge text/binary content,
      order references, expiry and unknown versions with explicit local bounds.
- [x] Retain duplicate procedure/challenge evidence and literal dummy references
      without choosing a procedure, asserting SCA exemption or completing work.
- [x] Add six independent fixtures and 249 checks, including wrapped response
      provenance, conditional fields, boundaries and fixed diagnostics.
- [ ] Complete authenticated user/procedure context, full request-option and
      expiry validation, and session-bound handling of challenge outcomes.

[ADR 0015](adr/0015-tan-procedure-and-challenge-schemas.md) records supported
versions and remaining context validation before SCA continuation.

## Permitted procedures and request-bound challenge evidence

- [x] Parse bounded HIRMS 3920 lists, retaining duplicates and reports that
      accompany a dialogue-abort error without activating permission.
- [x] Observe restricted unsigned HKTAN 6/7 initial/status requests and compare
      exact message, dialogue, procedure, hash, reference and medium evidence.
- [x] Classify scoped challenge, pending-approval and exemption reports only
      when no comparison issue remains; preserve dummy and conflicting outcomes.
- [x] Add seven independent fixtures and 191 checks, including request/response
      provenance, ambiguity, unsupported options, cancellation and safe errors.
- [ ] Integrate authenticated SCA continuation, durable replay protection,
      trusted bank-expiry policy, full request options and credential use.

[ADR 0016](adr/0016-permitted-procedures-and-challenge-context.md) defines this
pure comparison. A clean observation is not authorization or SCA completion.

## Bounded in-memory SCA continuation

- [x] Maintain one current challenge and one explicitly recorded synthetic
      status query, preserving selected context and rejecting concurrent replay.
- [x] Enforce an absolute monotonic deadline, local/advertised query limits,
      first/subsequent waits and explicit manual/automatic trigger rules.
- [x] Make cancellation, stop, review, timeout, counter exhaustion and reported
      execution/exemption terminal; release retained evidence references.
- [x] Add eight independent traces and 123 checks, including fake-clock
      boundaries and concurrent starts, handoffs, queries and responses.
- [ ] Integrate full request/credential codecs, trusted expiry, push delivery,
      authenticated sessions and durable replay handling before live SCA.

[ADR 0017](adr/0017-in-memory-sca-continuation.md) defines the local model.
Recording intent or an execution report authorizes no bank operation.

## Account-discovery and balance schemas

- [x] Parse credential-free HKSPA/HISPA version 1 and HKSAL/HISAL versions
      6/7/8 with explicit version boundaries and original response provenance.
- [x] Preserve exact identifiers, duplicate/non-SEPA accounts, booked/pending
      balances, separate optional amounts, source dates and currency conflicts.
- [x] Bound reports/account repetitions, reject malformed known schemas and
      keep unknown versions opaque, without domain ingestion or sending.
- [x] Add eight independent fixtures and 393 schema checks.
- [ ] Resolve discovery versions 2/3 and validate request/account/capability,
      pagination and authenticated context before live ingestion.

[ADR 0018](adr/0018-account-discovery-and-balance-schemas.md) records the source
version ambiguity and remaining context requirements.

## Single-account read-context evidence

- [x] Observe one unsigned read request in an established synthetic dialogue
      and compare exact account, operation/version and capability evidence.
- [x] Check outer/inner references, separate client/bank counters, unknown and
      duplicate reports, currency conflicts and scoped status observations.
- [x] Compare partial-page tokens against unchanged prior context with sequential
      counters and a local 128-page ceiling, without consuming replay state.
- [x] Add eight independent fixtures and 236 context checks.
- [ ] Add an attempt lifecycle, broader account/no-data handling, authenticated
      sessions and durable replay evidence before domain ingestion.

[ADR 0019](adr/0019-read-request-response-context.md) defines the pure comparison
and its single-account scope. A matching outcome authorizes no bank operation.

## Bounded in-memory read-refresh attempt

- [x] Pin one selected request/capability context, recompute response evidence
      and consume matching responses once per attempt under a shared lock.
- [x] Require explicit continuation, preserving pending state after unrelated
      responses or invalid next requests; stop on bound review failures.
- [x] Enforce absolute monotonic timeout, page/counter limits and terminal
      cancellation/stop, releasing all retained raw evidence references.
- [x] Add eight independent traces and 137 lifecycle checks, including deadline
      boundaries, reference release and concurrent calls.
- [ ] Add explicit no-data outcomes, authenticated sessions, durable replay,
      aggregation and domain ingestion before live reads.

[ADR 0020](adr/0020-in-memory-read-refresh-attempt.md) defines the local lifecycle.
An execution report still provides no authenticated or persisted account value.

## Scoped read-unavailability outcomes

- [x] Interpret request-scoped 3010 as an unavailable report, preserving empty
      discovery and absent report shapes without inventing accounts or values.
- [x] Keep missing responses, data/status conflicts and 9210 errors for review;
      never infer account closure or zero from free-form bank text.
- [x] Terminate the local attempt once with unavailable state, preserving earlier
      caller-owned page evidence and releasing its retained raw references.
- [x] Add eight independent fixtures and 85 checks across context and lifecycle.
- [ ] Extend all-account discovery matching and integrate authenticated sessions,
      durable replay evidence and domain ingestion.

[ADR 0021](adr/0021-scoped-read-unavailability.md) records the generic 3010
meaning and the limited scoped interpretation.

## Bounded all-account discovery evidence

- [x] Compare HKSPA/HISPA-1 all-account responses with exact version, dialogue,
      status and bank/user parameter evidence, including all-accounts-only ads.
- [x] Preserve every returned account and all national/IBAN candidates, exposing
      unknown, ambiguous, duplicate, conflicting and unmatched identities.
- [x] Check per-account permission/signature evidence, bound candidate expansion
      to 4096 links and retain source order without identity allocation or removal.
- [x] Add eight independent fixtures and 105 checks, including 512 clean matches,
      999 unknown returned accounts and candidate-limit boundaries.
- [ ] Add an all-account attempt lifecycle, authenticated sessions, durable replay
      and domain reconciliation before live discovery.

[ADR 0022](adr/0022-all-account-discovery-evidence.md) defines this pure comparison.
Selected-account context and attempt APIs retain their existing scope limits.

## In-memory all-account discovery attempt

- [x] Pin one all-account request and parameter set, reject foreign responses
      before candidate expansion and recompute bound comparisons internally.
- [x] Hand execution, unavailable or review evidence to the caller once, retaining
      ambiguity and unmatched entries; resource failures return no partial result.
- [x] Enforce a fixed monotonic deadline and terminal cancellation/stop, releasing
      retained context and exposing scalar-only lifecycle snapshots.
- [x] Add eight independent traces and 108 checks, including concurrent calls,
      deadline expiry during comparison and reference cleanup.
- [ ] Add full request encoding, authenticated sessions, durable replay and domain
      reconciliation before live discovery.

[ADR 0023](adr/0023-in-memory-all-account-discovery-attempt.md) defines ownership
and local consumption. Caller-owned comparison evidence remains untrusted.

## Credential-free unsigned read-request encoding

- [x] Encode HKSPA-1 and HKSAL-6/7/8 from typed identifier inputs, preserving
      account order, duplicates and exact identifier spelling.
- [x] Escape caller text without lossy conversion, preserve positional omissions
      and emit invariant counters with byte-accurate 12-digit frame lengths.
- [x] Bound fields/repetitions, propagate cancellation and validate generated
      frames through the shared unsigned request schema before returning bytes.
- [x] Add ten independent wire fixtures and 512 checks, including the complete
      single-byte text range, culture independence and discovery integration.
- [ ] Add initialization and full security codecs, authenticated sessions,
      negotiated charset, transport and durable domain ingestion.

[ADR 0024](adr/0024-unsigned-read-request-encoding.md) defines the unsigned format.
Encoding allocates no message counter and grants no permission to send.

## Credential-free dialogue initialization schemas and encoding

- [x] Parse HKIDN-2 identification and HKVVB-3 preparation with exact source
      preservation, explicit versions and bounded typed fields.
- [x] Restrict unsigned initialization frames to dialogue 0/message 1 and enforce
      the anonymous identity tuple without inferring customer or product authority.
- [x] Encode explicit caller inputs, reject padded/lossy text and share escaped
      text and exact frame sizing with the existing unsigned read writer.
- [x] Add eight independent wire fixtures and 295 checks, including malformed
      schemas, context isolation, binary substitutions, bounds and cancellation.
- [ ] Bind initialization responses and parameter scope, then integrate product
      registration, synchronization and authenticated session/security handling.

[ADR 0025](adr/0025-unsigned-dialogue-initialization.md) records the restricted
unsigned scope. No accepted observation creates or activates a session.

## Initialization response binding and parameter scope

- [x] Compare assigned dialogue, outer message and segment references against one
      initialization request and parameters owned by its candidate response.
- [x] Check exact bank identity, explicit user expectation, customer and account
      institution scope, preserving anonymous and dialogue-scoped UPD semantics.
- [x] Retain missing parameters, version-change observations and unknown segments
      without implicit cache reuse, session creation or parameter activation.
- [x] Add fourteen independent fixtures and 120 checks, including status ambiguity,
      source identity, all 512 UPD entries, cancellation and repeated comparison.
- [ ] Add a bounded initialization attempt, authenticated sessions, reviewed cache
      reuse, synchronization and durable integration before live initialization.

[ADR 0026](adr/0026-initialization-response-scope.md) defines the pure comparison.
A matching result is an execution observation, not session authentication.

## In-memory initialization attempt

- [x] Pin one initialization request and expected user context, sharing initial
      validation and response-reference checks with the pure comparator.
- [x] Preserve pending state after unrelated responses, then hand bound execution
      or review evidence to the caller once without activating a session.
- [x] Enforce a fixed monotonic deadline, cancellation/stop and terminal context
      release, with scalar-only snapshots and no automatic retry.
- [x] Add eight independent traces and 125 checks, including deadline expiry
      during comparison, reference cleanup and concurrent transitions.
- [ ] Add synchronization schemas, authenticated sessions, reviewed cache reuse,
      transport and durable replay before live initialization.

[ADR 0027](adr/0027-in-memory-initialization-attempt.md) defines local ownership
and consumption. The reported dialogue remains untrusted caller-owned evidence.

## Credential-free synchronization schemas and encoding

- [x] Parse HKSYN-3 modes and HISYN-4 fields, preserving exact nullable message
      and signature counters, identifiers and original source segments.
- [x] Retain empty, conflicting, duplicate and unknown response observations;
      bound supported and unsupported synchronization reports to 128 per dataset.
- [x] Validate and encode restricted unsigned five-segment requests with explicit
      identified context and system-ID-mode requirements, without recovery actions.
- [x] Add ten independent wire fixtures and 165 checks, including 16-digit
      precision, malformed fields, context isolation, limits and cancellation.
- [ ] Add request/profile response comparison and close/reinitialize handling,
      then authenticated security and explicit recovery workflows.

[ADR 0028](adr/0028-synchronization-schemas.md) defines the schema-only meaning
of returned values. No identifier or message/signature counter is updated.

## Synchronization response context and profile requirements

- [x] Compare first-response dialogue and segment references, scoped execution
      status and exact mode-specific fields while preserving unresolved reports.
- [x] Check explicit PIN/TAN and RAH profile hypotheses, rejecting prohibited
      combinations and leaving the RAH-9 signature layout unresolved for review.
- [x] Require prior-dialogue bounds for message recovery and record the mandatory
      close/reinitialize disposition without applying identifiers or counters.
- [x] Add fifteen independent fixtures and 149 checks, including reserved values,
      status ambiguity, 128 retained reports, context boundaries and cancellation.
- [ ] Add dialogue-end schemas and a closing lifecycle, then authenticated
      security, explicit recovery application and durable integration.

[ADR 0029](adr/0029-synchronization-response-context.md) defines the pure checks.
Matching evidence is never a business-ready or authenticated dialogue.

## Dialogue-end schemas, unsigned encoding and reply evidence

- [x] Parse HKEND-1 and encode restricted three-segment unsigned requests with
      exact escaped dialogue text and explicit independent message counters.
- [x] Bind standard closing replies to the request, keeping normal closure,
      explicit abort and unresolved scope/status observations separate.
- [x] Reject data-bearing or ambiguous replies from matching evidence, preserving
      exact caller-owned source objects without session mutation or consumption.
- [x] Add ten independent fixtures and 142 checks, including exact wire bytes,
      malformed fields, counter limits, context isolation and cancellation.
- [ ] Add a bounded synchronization-and-closing lifecycle, then authenticated
      security, explicit recovery application and durable integration.

[ADR 0030](adr/0030-dialogue-end-schemas-and-encoding.md) defines the unsigned
scope. Reported closure or abort does not prove authenticated termination.

## Bounded synchronization-and-closing attempt

- [x] Pin one synchronization request with its explicit profile/recovery context,
      computing response evidence internally and handing it to the caller once.
- [x] Require an explicit close for the reported dialogue and next client/bank
      counters before reporting the need for fresh initialization.
- [x] Preserve foreign candidates without consumption, terminate bound review
      and abort outcomes separately, and release retained context on termination.
- [x] Share one absolute monotonic deadline across both stages and the closing
      interval, with local cancellation, stop, clock checks and serialized calls.
- [x] Add twelve independent traces and 249 checks, including per-instance replay,
      exact ownership, expiry during comparison and concurrent response delivery.
- [ ] Add request security schemas, credential/session ownership, authenticated
      integration, explicit recovery application and durable replay handling.

[ADR 0031](adr/0031-in-memory-synchronization-and-closing.md) defines local
consumption. No recovered value is applied or authenticated session established.

## Credential-free PIN/TAN signature-header schemas

- [x] Parse restricted HNSHK-4 PIN/TAN headers with exact identity, control
      reference, 16-digit reference number, timestamp and algorithm filler values.
- [x] Enforce PIN:1/999 and PIN:2/900–997 relationships, bounded groups and
      forbidden CID, hash parameter and certificate fields.
- [x] Preserve optional source shapes, zero/unresolved identifiers and filler
      codes without applying state, selecting procedures or authenticating messages.
- [x] Add ten independent fixtures and 295 checks, including all exposed fields,
      malformed/unsupported layouts, date validity, source ownership and cancellation.
- [ ] Add request-bound identity/system/procedure comparison, credential ownership,
      signature trailers, full request security and authenticated integration.

[ADR 0032](adr/0032-pin-tan-signature-header-schemas.md) defines the supported
subset. A parsed header provides no credential, permission or replay guarantee.

## PIN/TAN signature-header request context

- [x] Bind a detached header to an explicit unsigned initialization/synchronization
      request, expected user/control reference and selected profile/procedure.
- [x] Check identity, system and single-header role constraints, allowing zero
      system ID only in the explicit system-ID synchronization context.
- [x] Require sourced HITANS/3920 observations with initialization bank/user/
      reference checks and retain missing, duplicate, foreign or aborted evidence.
- [x] Add eighteen independent fixtures and 139 checks, including exact source
      ownership, advertisement limits, caller validation and cancellation.
- [ ] Add typed signature-header encoding, credential ownership, signature
      trailers, full request security and authenticated integration.

[ADR 0033](adr/0033-pin-tan-signature-request-context.md) defines the pure
comparison. Matching observations never authorize sending or activate procedures.

## Typed PIN/TAN signature-header encoding

- [x] Encode one HNSHK-4 segment from immutable explicit input and validate the
      complete bytes through the existing restricted header schema.
- [x] Preserve exact escaped text, numeric fillers and 16-digit reference values,
      using canonical omissions for unused timestamp/hash/certificate positions.
- [x] Reject unsupported text, invalid bounds and unrepresentable subsecond time
      precision without allocating references, reading clocks or handling credentials.
- [x] Add ten independent fixtures and 336 checks, including exact wire bytes,
      output ownership, culture independence and request-context integration.
- [ ] Add session-only credential ownership and cleanup, signature trailers,
      complete request security and authenticated integration.

[ADR 0034](adr/0034-pin-tan-signature-header-encoding.md) defines local encoding.
The output is one header segment and provides no authorization or authentication.

## Session-only credential buffer ownership

- [x] Capture bounded opaque byte spans into private pinned storage and clear
      the supplied input on every exit, including rejection and cancellation.
- [x] Support reusable PIN copy-out and one successful TAN copy-out per owner,
      with explicit caller ownership of destination bytes.
- [x] Zero actual private storage on consumption, cancellation, disposal or
      observed expiry/clock failure, with fallback finalization.
- [x] Enforce a fixed access deadline without renewal; check it before/after
      copy and clear any copied prefix when expiry/cancellation prevents success.
- [x] Add ten independent traces and 136 checks, including actual zeroization,
      failure-path cleanup, output ownership and concurrent calls.
- [ ] Integrate secure credential input and prompt session disposal, protocol/
      character checks, signature trailers and authenticated request handling.

[ADR 0035](adr/0035-session-credential-buffer-ownership.md) defines ownership
limits. Idle expiry is observed on API calls; callers must dispose promptly and
clear successful copies. No real credential or bank operation is enabled.

## Credential-aware HNSHA signature-trailer encoding

- [x] Encode one HNSHA-2 candidate from matching signature context and explicitly
      owned credentials into bounded caller output, preserving the control reference.
- [x] Check intended initialization/synchronization positions and reject variant-2
      trailer TANs before credential copy-out.
- [x] Preserve printable credential octets with delimiter escaping, an empty
      validation field and canonical TAN omission, without credential strings.
- [x] Clear temporary spans and failed output, retain one-time TAN consumption
      after downstream failure, and recheck PIN availability before/after publication.
- [x] Add twelve independent fixtures and 106 checks, including exact bytes,
      output bounds, ownership, late cancellation/expiry and concurrent TAN use.
- [x] Add bounded plain initialization/synchronization assembly (ADR 0037).
- [x] Add bounded plaintext request-envelope assembly (ADR 0038).
- [ ] Add operation/challenge validation,
      secure input, authenticated sessions and transport.

[ADR 0036](adr/0036-pin-tan-signature-trailer-encoding.md) defines the local codec.
Successful output contains credentials and must be cleared by its caller.

## Bounded PIN/TAN request assembly

- [x] Assemble plain first-dialogue initialization/synchronization candidates from
      matching signature evidence and owned PIN/optional TAN.
- [x] Preserve header/body fields, renumber inserted segments and calculate the
      exact twelve-digit message size with matching outer counters.
- [x] Require a bounded caller reserve before credential copy-out; clear whole-
      message staging and any failed publication without restoring consumed TANs.
- [x] Add fourteen independent fixtures and 157 checks for exact bytes, field
      preservation, output bounds, ownership, late cancellation/expiry and concurrency.
- [x] Add bounded plaintext request-envelope assembly (ADR 0038).
- [x] Add candidate context and response-reference binding (ADR 0039).
- [x] Add assembled initialization semantics (ADR 0040).
- [x] Add assembled synchronization semantics (ADR 0041).
- [ ] Add broader session semantics,
      operation/challenge requirements, secure input and authenticated transport.

[ADR 0037](adr/0037-pin-tan-request-assembly.md) defines the local candidate.
Successful output contains plaintext credentials and remains caller-owned.

## Bounded PIN/TAN request-envelope assembly

- [x] Wrap restricted initialization/synchronization candidates with HNVSK-3 and
      HNVSD-1 using bound profile/identity metadata and canonical public fillers.
- [x] Preserve exact inner bytes, calculate binary/outer lengths and retain logical
      trailer numbering with reserved wrapper positions 998/999.
- [x] Require a complete output reserve before credential copy-out, clear both
      staging spans and erase failed output through the final publication boundary.
- [x] Add seventeen independent fixtures and 207 checks, including exact bytes,
      ownership, timestamps, escaped identities, cancellation/expiry and clock failure.
- [x] Add credential-free candidate context and response-reference binding (ADR 0039).
- [x] Add assembled initialization semantics (ADR 0040).
- [x] Add assembled synchronization semantics (ADR 0041).
- [ ] Add operation/
      challenge requirements, secure input, authenticated sessions and transport.

[ADR 0038](adr/0038-pin-tan-request-envelope-assembly.md) defines the local codec.
The envelope contains plaintext credentials and performs no encryption or sending.

## Assembled PIN/TAN request context and reference binding

- [x] Describe the restricted envelope candidate with immutable code/number/version/
      role metadata, requiring matching signature evidence and no credential buffers.
- [x] Compare envelope profile, assigned dialogue and message references, mapping
      response segments to their actual signed request roles without source rewriting.
- [x] Reject wrong parameter/synchronization roles, absent or foreign references
      and unknown/future data; preserve scoped errors independently of binding.
- [x] Add twenty-three independent fixtures and 361 checks, including exact roles,
      output-map integration, source ownership, erasure independence and concurrency.
- [x] Add assembled initialization semantics (ADR 0040).
- [x] Add assembled synchronization semantics (ADR 0041).
- [ ] Add attempt ownership,
      operation/challenge validation, secure input and authenticated transport.

[ADR 0039](adr/0039-pin-tan-assembled-request-reference-binding.md) defines the
structural comparison. Matching references do not establish execution or identity.

## Assembled PIN/TAN initialization response semantics

- [x] Require initialization candidate kind and exact bound parameter-source
      identity, retaining mismatches without mixing response trees.
- [x] Compare envelope system/bank/user identity and shared BPD/UPD/account
      country, bank, user, customer, protocol and language requirements.
- [x] Interpret execution through bound roles, preserve scoped 3050 and UPD zero,
      and retain missing parameters and untrusted version-change observations.
- [x] Add thirty independent fixtures and 199 checks for matching/review outcomes,
      source mixing, signature-vs-identification scope, cancellation and concurrency.
- [x] Add assembled synchronization semantics (ADR 0041).
- [ ] Add procedure/challenge integration,
      attempt ownership, secure input and authenticated transport.

[ADR 0040](adr/0040-pin-tan-initialization-response-semantics.md) defines the
local comparison. Execution reports do not activate authenticated sessions.

## Assembled PIN/TAN synchronization response semantics

- [x] Require synchronization kind and exact report-response provenance; preserve
      binding issues without mixing source trees or selecting ambiguous reports.
- [x] Compare report shape, bound execution role, envelope identity and explicit
      prior-dialogue/message bounds using shared unsigned report rules.
- [x] Preserve exact reported values and require closing/reinitialization after
      every match; unresolved evidence stops for review without recovery application.
- [x] Add thirty-three independent fixtures and 217 checks, including source mixing,
      report ambiguity, recovery bounds, writer integration and concurrent comparison.
- [x] Add bounded assembled synchronization attempt ownership with mandatory
      closing/reinitialization handoff (ADR 0043).
- [x] Add bounded assembled closing lifecycle ownership (ADR 0046).
- [ ] Add procedure/challenge checks,
      secure input and authenticated transport.

[ADR 0041](adr/0041-pin-tan-synchronization-response-semantics.md) defines the
local comparison. Matching synchronization never activates a banking session.

## Assembled PIN/TAN initialization attempt lifecycle

- [x] Pin one initialization candidate without taking credentials or encoded output;
      recompute binding and semantics against its exact metadata.
- [x] Leave foreign message/reference responses pending, return scoped evidence once
      for execution or review, and preserve immutable terminal states.
- [x] Enforce an absolute deadline with cancellation and clock-failure checks before
      handoff; release metadata on every terminal outcome and disposal.
- [x] Add thirteen independent traces and 156 checks, including source ownership,
      late failures, nonrenewal, scalar diagnostics and concurrent response handoff.
- [x] Add assembled synchronization lifecycle and closing/reinitialization handoff.
- [x] Add bounded assembled closing lifecycle (ADR 0046).
- [ ] Add procedure/challenge integration,
      secure input and authenticated transport.

[ADR 0042](adr/0042-pin-tan-initialization-attempt-lifecycle.md) defines the local
attempt. Recording a candidate does not prove transmission or activate a session.

## Assembled PIN/TAN synchronization attempt lifecycle

- [x] Pin one candidate and exact recovery context; reject wrong request kinds and
      invalid recovery inputs before recording metadata.
- [x] Preserve foreign response scope without renewing the deadline; hand scoped
      synchronization or review evidence to the caller once.
- [x] Require closing/reinitialization after every match, without applying reported
      values or claiming closure; release both references on every terminal path.
- [x] Add twenty-one independent traces and 228 checks for recovery ownership,
      mandatory handoff, review, late failures, scalar diagnostics and concurrency.
- [x] Add credential-aware dialogue-end context/encoding (ADR 0044).
- [x] Add assembled closing response binding and termination semantics (ADR 0045).
- [x] Add bounded assembled closing lifecycle (ADR 0046).
- [ ] Add procedure/challenge integration, secure input and transport.

[ADR 0043](adr/0043-pin-tan-synchronization-attempt-lifecycle.md) defines the local
attempt. Its deadline ends at response handoff; the caller explicitly starts the
separately bounded closing phase defined in ADR 0046.

## PIN/TAN dialogue-end context and encoding

- [x] Bind an explicit closing request/header to matching synchronization evidence,
      with exact dialogue, counters, identity, system, profile and procedure scope.
- [x] Encode a PIN-only closing envelope with HKEND at logical segment 3, exact
      whole-message and binary lengths, and explicit caller header metadata.
- [x] Clear bounded secret staging on every exit and erase published output on
      late credential failure; preflight leaves caller buffers untouched.
- [x] Add twenty independent fixtures and 187 checks for scoped context, escaping,
      recovery counters, failure boundaries and shared-writer compatibility.
- [x] Add assembled closing response binding/termination semantics (ADR 0045).
- [x] Add bounded assembled closing lifecycle (ADR 0046).
- [ ] Add secure input, authenticated transport and live qualification.

[ADR 0044](adr/0044-pin-tan-dialogue-end-context-encoding.md) defines the restricted
candidate. Successful encoding neither closes a dialogue nor activates a session.

## Assembled PIN/TAN closing response binding and termination

- [x] Map candidate metadata to actual encoded segment roles and versions, with
      exact closing dialogue, independent counters and profile comparison.
- [x] Compare envelope identity and restricted termination replies, retaining
      raw closure/abort observations while withholding foreign or unresolved scope.
- [x] Distinguish closure, abort and review without accepting signature/wrapper
      success, duplicate/conflicting termination, unexpected data or unknown status.
- [x] Add forty-six independent fixtures and 409 checks, including actual writer
      integration, source ownership, immutable mappings and concurrent comparison.
- [x] Add bounded closing lifecycle ownership with one-time response handling
      and terminal cleanup (ADR 0046).
- [ ] Add secure input and authenticated transport.

[ADR 0045](adr/0045-pin-tan-closing-response-binding.md) defines the local comparison.
Reported closure is not authenticated closure; reinitialization remains required.

## Assembled PIN/TAN closing attempt lifecycle

- [x] Pin one qualified closing candidate and absolute deadline, recomputing
      reference and semantic checks from the incoming response.
- [x] Return closure, abort or review evidence once; require fresh initialization
      after closure and prevent another close after terminal abort.
- [x] Keep foreign scope pending without deadline renewal; release candidate and
      inherited synchronization metadata on every terminal outcome and disposal.
- [x] Add twenty independent traces and 260 checks for late failures, immutable
      outcomes, scalar diagnostics, concurrency and the synchronization handoff.
- [x] Integrate assembled initialization procedure/permission evidence (ADR 0047).
- [x] Add assembled HIPINS requirements integration (ADR 0048).
- [ ] Add broader session coordination, secure input and
      authenticated transport.

[ADR 0046](adr/0046-pin-tan-closing-attempt-lifecycle.md) defines the local lifecycle.
Synchronization and closing use separate deadlines; neither phase activates a session.

## Assembled initialization procedure and permission integration

- [x] Combine returned HITANS versions 6/7 and preparation-scoped 3920 reports
      with the exact bound response and outgoing pinned selection.
- [x] Preserve all initialization identity/status/parameter checks while recognizing
      only parsed source objects; retain missing, ambiguous and unknown data for review.
- [x] Add explicit one-time combined handoff through the initialization attempt,
      with scalar procedure diagnostics and cleanup across existing failure boundaries.
- [x] Add twenty-eight independent fixtures and 261 checks for version/profile scope,
      source mixing, unresolved observations, legacy behavior and lifecycle integration.
- [x] Integrate HIPINS requirements (ADR 0048).
- [x] Reuse returned evidence in first-read signature context (ADR 0049).
- [ ] Integrate SCA/challenge handling, secure input and authenticated transport.

[ADR 0047](adr/0047-assembled-initialization-procedure-integration.md) defines the
explicit path. Combined matching evidence does not activate or authorize a procedure.

## Assembled initialization HIPINS requirements

- [x] Combine HIPINS with initialization and HITANS/3920 evidence from the exact
      same parameter tree and bound response, recognizing only parsed source segments.
- [x] Preserve optional bounds and operation flags; require review for missing,
      duplicate, contradictory, zero or unsupported requirement observations.
- [x] Expose qualified TAN flags only on full combined matching evidence; keep
      absent operations unlisted and grant no permission or SCA exemption.
- [x] Add explicit one-time requirements handoff with scalar diagnostics and
      existing deadline, cancellation, replay and terminal cleanup guarantees.
- [x] Add thirty independent fixtures and 312 checks for source provenance,
      requirement ambiguity, legacy behavior, lifecycle failures and concurrency.
- [x] Reuse returned evidence in first-read signature context (ADR 0049).
- [ ] Apply credential requirements and integrate session/SCA context, secure
      input and transport.

[ADR 0048](adr/0048-assembled-initialization-hipins-requirements.md) defines the
combined path. Bounds remain reported metadata; no credentials are inspected.

## First-read PIN/TAN signature context

- [x] Reuse the complete returned initialization requirements result for a detached
      first discovery/balance signature header, preserving exact source objects.
- [x] Check dialogue and next counters, original identity/system, unchanged
      profile/function/version, expected control reference and single-header roles.
- [x] Require a reported operation TAN flag and reject continuation tokens;
      withhold all qualified objects when any part of the context needs review.
- [x] Add thirty-seven independent fixtures and 230 checks for provenance,
      scope, unresolved requirements, lifecycle handoff reuse and concurrency.
- [x] Integrate returned read capabilities and single-account permissions (ADR 0050).
- [ ] Integrate credential requirements, signed read encoding, session/SCA context
      and transport.

[ADR 0049](adr/0049-first-read-pin-tan-signature-context.md) defines the pure
comparison. Matching does not authorize an account read or consume a request.

## Initialization read-capability and first-read account integration

- [x] Recognize supported read-advertisement references at preparation and explicitly
      integrate parsed schemas from the exact shared initialization parameter tree.
- [x] Return read schemas through the existing one-time requirements handoff, with
      shared deadline, replay rejection, foreign-scope handling and terminal cleanup.
- [x] Compare one exact returned account's permissions, identity, advertisement
      version and signature requirements against the first-read request.
- [x] Reuse request-option checks and require review for unhandled limits;
      preserve detailed component issues and qualify only the complete result.
- [x] Add forty-three independent fixtures and 390 checks for source mixing,
      permissions, request options, scope, limits, lifecycle failures and concurrency.
- [x] Compare first-read credential requirements (ADR 0051).
- [ ] Integrate owned-credential checks, signed reads, all-account capabilities,
      session/SCA context, secure input and authenticated transport.

[ADR 0050](adr/0050-initialization-read-capability-integration.md) defines the
explicit path. Matching observations activate no capabilities or account state.

## First-read credential requirement comparison

- [x] Compare one supplied PIN or TAN span against a fully matching first-read
      context, returning only scalar issues and retaining no credential bytes.
- [x] Preserve missing PIN bounds and TAN maxima; apply known limits independently
      and validate numeric or supported alphanumeric text without normalization.
- [x] Reject TAN input for decoupled procedures while preserving PIN comparison;
      keep credential completeness, placement and SCA checks separate.
- [x] Add forty-five independent fixtures and 659 checks, including every octet,
      unchanged buffers, cancellation, cultures and concurrent immutable inputs.
- [x] Integrate comparison into PIN-only trailer encoding with owned credential
      validation, expiry/cancellation checks and secret cleanup (ADR 0052).

[ADR 0051](adr/0051-first-read-credential-requirement-comparison.md) defines the
individual-value comparison. A matching result does not authorize submission.

## First-read PIN-only signature-trailer encoding

- [x] Encode HNSHA 2 at fixed first-read segment 4, preserving escaped control
      and PIN bytes without accepting a TAN owner or mutating the unsigned request.
- [x] Require full account context and known PIN bounds before owner access;
      compare actual copied staging bytes with the reported PIN requirements.
- [x] Preserve bounded reserve, lifetime/cancellation checks and temporary-buffer
      erasure; zero the published prefix when a late failure withholds success.
- [x] Add twenty-two independent fixtures and 243 checks for exact bytes,
      preflight, all ownership failure boundaries, cultures and concurrent encoding.
- [x] Assemble the plain PIN-only first-read message with segment/context scope
      and whole-message cleanup (ADR 0053).
- [x] Add read envelopes and candidate metadata (ADR 0054).
- [ ] Integrate TAN/challenge and assembled response handling.

[ADR 0052](adr/0052-first-read-pin-only-trailer-encoding.md) defines the component.
Encoding does not decide TAN omission, establish SCA readiness or authorize sending.

## First-read PIN-only request assembly

- [x] Assemble HNHBK/HNSHK/read/HNSHA/HNHBS with exact assigned dialogue,
      message counters and logical segment numbers, preserving original inputs.
- [x] Preserve explicit operation versions, account fields and optional request
      values with delimiter escaping and an exact final byte-size calculation.
- [x] Require the full bounded reserve before PIN access and apply the existing
      owned-PIN trailer validation, including distinct requirement-review outcomes.
- [x] Clear whole-message staging and any failed published prefix, retaining
      expiry/cancellation checks through final output handoff.
- [x] Add thirty-one independent fixtures and 334 checks for exact frames,
      source preservation, seven ownership boundaries, cultures and concurrency.
- [x] Add the read security envelope and credential-free candidate metadata
      (ADR 0054).
- [ ] Integrate response binding, TAN/challenge and session coordination.

[ADR 0053](adr/0053-first-read-pin-only-request-assembly.md) defines the plain
candidate. Assembly does not authorize sending or establish TAN/SCA completeness.

## First-read PIN-only request envelope

- [x] Bind immutable credential-free candidate metadata to one qualified context
      and the actual wrapper/signature/read segment roles and versions.
- [x] Wrap only the local assembler's output with exact HNVSD binary payload
      length, assigned dialogue, message counters and final framing size.
- [x] Preserve profile, identity and optional timestamp fields using the existing
      plaintext wrapper rules, without parsing secret-bearing wire in production.
- [x] Require bounded output before PIN access and clear staging on every exit;
      retain cancellation/availability checks and full failed-prefix cleanup.
- [x] Add thirty-three independent fixtures and 456 checks, covering exact bytes,
      metadata, all nine ownership observations, cultures and concurrent writes.
- [x] Bind assembled first-read response references to candidate metadata
      (ADR 0055).
- [ ] Integrate response semantics, TAN/challenge and session coordination.

[ADR 0054](adr/0054-first-read-pin-only-request-envelope.md) defines the envelope
candidate. Its metadata contains private account evidence; encoding proves no
TAN omission permission, SCA readiness, sending or response authentication.

## Assembled first-read response reference binding

- [x] Compare the response envelope profile, exact assigned dialogue, bank counter
      and referenced client counter with the qualified first-read candidate.
- [x] Map every non-message body segment to its actual immutable request target,
      preserving missing/unknown references and original response segment objects.
- [x] Keep signature/framing replies distinct from read segment 3; constrain the
      known data vocabulary to the matching read operation without version fallback.
- [x] Preserve status, payload, account and envelope identity observations for
      semantic comparison; matching references establish no read success.
- [x] Add forty-six independent fixtures and 481 checks, including actual-envelope
      integration, source preservation, cancellation, cultures and concurrency.
- [x] Compare assembled read-response semantics using exact-source schemas,
      account/version/currency evidence, envelope identity and scoped statuses
      (ADR 0056).

[ADR 0055](adr/0055-first-read-response-reference-binding.md) defines the pure
comparison. It consumes no response and provides no authentication or replay guard.

## Assembled first-read response semantics

- [x] Require read schemas from the exact bound response and retain raw inputs
      while withholding qualified data on source, binding or semantic review.
- [x] Compare envelope system/key identity with the bound signature header and
      reuse existing exact account, report-version and currency checks.
- [x] Scope status interpretation to assembled read segment 3; keep security
      replies, missing/duplicate data and unsupported status shapes under review.
- [x] Preserve real zero values, optional amounts, unavailable source shapes and
      partial observations with raw opaque tokens, without domain ingestion.
- [x] Add fifty-nine independent fixtures and 495 checks, including exact-source
      provenance, separate identities, cultures, cancellation and concurrency.
- [x] Add a bounded assembled first-read attempt with one candidate/deadline,
      one-time response handoff and terminal metadata cleanup (ADR 0057).

[ADR 0056](adr/0056-assembled-first-read-response-semantics.md) defines the pure
observations. Matching evidence grants no authentication, TAN/SCA completion,
replay protection or permission to continue a partial request.

## Bounded assembled first-read attempt

- [x] Pin one immutable candidate and absolute deadline without credentials,
      encoding or transport; reject second starts without renewing time.
- [x] Recompute binding and semantics from incoming exact-source read data;
      retain foreign-scope candidates and consume scoped responses once.
- [x] Hand out execution, partial, unavailable or review evidence once, releasing
      pending metadata on all terminal outcomes without starting continuation.
- [x] Check lifetime/cancellation through final handoff, handle clock failures
      and reentrant abandonment, and expose only scalar snapshot diagnostics.
- [x] Add twenty-seven independent traces and 544 checks, including all existing
      semantic fixtures, late failures, terminal cleanup and concurrent submissions.
- [x] Bind assembled balance-continuation context to qualified partial evidence,
      unchanged request/account/options and sequential message counters (ADR 0058).

[ADR 0057](adr/0057-assembled-first-read-attempt.md) defines the local owner.
Handoff establishes no authentication, TAN/SCA completion, durable replay guard
or account update; a partial outcome ends this first-read attempt.

## Assembled balance-continuation context

- [x] Require qualified first-read partial evidence and an exact nonempty token
      for the proposed second-page balance request.
- [x] Preserve operation/version, every logical account component, request
      options and dialogue; require client and expected-bank counters 3.
- [x] Inherit the exact original capability/account/national-permission context
      and recheck request requirements without replacing source provenance.
- [x] Withhold qualified origin/request on every mismatch and retain raw inputs
      without encoding, consuming a token or reopening the first-read attempt.
- [x] Add forty-one independent fixtures and 348 checks, including actual partial
      handoff integration, significant token bytes, cultures and concurrency.
- [x] Bind a continuation PIN/TAN signature header to unchanged identity and
      procedure selection with an explicit control reference (ADR 0059).

[ADR 0058](adr/0058-assembled-balance-continuation-context.md) defines this
second-page comparison. Signing, credential validation, encoding and later-page
response/lifecycle integration remain separate work.

## Balance-continuation PIN/TAN signature context

- [x] Require complete continuation qualification before exposing inherited
      account/capability, procedure and HIPINS requirement objects.
- [x] Compare original country/institution/user/system identity, explicit
      profile/function/TAN version, header roles and validated control reference.
- [x] Preserve caller header/reference/timestamp/filler observations without
      freshness, replay, credential-completeness or SCA claims.
- [x] Retain exact context and sources; withhold all qualified outputs on review
      without encoding or reopening the completed first-read owner.
- [x] Add sixty-nine independent fixtures and 531 checks, including all existing
      continuation variants, partial handoff, malformed controls and concurrency.
- [x] Encode the continuation PIN-only trailer with owned-PIN requirement
      validation and bounded secret cleanup (ADR 0060).

[ADR 0059](adr/0059-balance-continuation-pin-tan-signature-context.md) defines
this detached context. Matching fields grant no TAN/SCA waiver or sending authority.

## Continuation PIN-only signature-trailer encoding

- [x] Emit HNSHA-2 at segment 4 with the bound continuation control reference,
      empty validation result and one PIN component, without accepting a TAN.
- [x] Require complete continuation/signature scope and known inherited PIN
      bounds before owner access, then validate actual copied PIN staging bytes.
- [x] Preserve the bounded reserve, lifetime/cancellation checks, temporary-byte
      erasure and final failed-prefix cleanup without renewing PIN lifetime.
- [x] Retain distinct context/requirements/text/availability outcomes, exact
      escaping, unchanged request/response sources and reusable-PIN accounting.
- [x] Add eighty-two independent fixtures and 741 checks, including every context
      preflight, all ownership failure boundaries, cultures and concurrency.
- [x] Assemble the continuation PIN-only plain request with exact message-3
      framing, bound segment roles and whole-message cleanup.

[ADR 0060](adr/0060-continuation-pin-only-trailer-encoding.md) defines the
component. Successful output contains a PIN for the caller to clear and proves
neither TAN omission permission nor complete request/SCA readiness.

## Continuation PIN-only plain request assembly

- [x] Assemble HNHBK/HNSHK/HKSAL/HNSHA/HNHBS with bound dialogue/message-3
      framing, explicit read versions, exact tokens and unchanged account/options.
- [x] Require complete continuation/signature context and a bounded output reserve
      before PIN access; apply inherited requirements through the owned-PIN trailer.
- [x] Preserve source evidence and clear staging and failed whole-message output
      through expiry, cancellation, disposal and clock failures.
- [x] Verify eighty-two independent fixtures and 691 checks, including every
      ownership boundary, cultures, concurrency and untouched destination tails.
- [x] Add continuation envelope assembly with credential-free candidate metadata,
      exact binary payload and final-output cleanup.

[ADR 0061](adr/0061-continuation-pin-only-request-assembly.md) defines this
synthetic codec. It grants no TAN omission, SCA readiness or permission to send.

## Continuation PIN-only request envelope

- [x] Retain a qualified second-page signature context in credential-free candidate
      metadata with exact dialogue/message-3 scope and actual assembled segment roles.
- [x] Wrap the continuation assembler's exact payload in a bounded PIN/TAN envelope,
      preserving header identity/timestamp and binary framing without parsing secrets.
- [x] Preserve inherited PIN checks and one-copy accounting, clear temporary buffers
      and erase failed final output through all nine nested ownership observations.
- [x] Verify eighty-two independent fixtures and 627 checks for exact bytes, metadata,
      context rejection, preflight, failure cleanup, cultures and concurrency.
- [x] Bind continuation response references to actual candidate segment roles,
      dialogue, message counters and profile.

[ADR 0062](adr/0062-continuation-pin-only-request-envelope.md) defines the
synthetic envelope and metadata. Encoding grants no TAN omission or SCA readiness.

## Assembled continuation-response reference binding

- [x] Compare explicit envelope profile, dialogue and message-3 counters against
      the exact continuation candidate, rejecting the retained first-page response.
- [x] Map every non-message body segment to its actual assembled target role,
      preserving missing/unknown references and security/framing reply scope.
- [x] Require balance data to reference HKSAL 3 while retaining unknown/future
      payloads, errors, duplicate data and partial tokens for semantic review.
- [x] Verify fifty-five independent fixtures and 576 checks, including actual
      envelope integration, source identity/byte preservation and concurrency.
- [x] Add continuation-response semantics with exact-source read schemas,
      account/version/currency and envelope identity checks and scoped outcomes.

[ADR 0063](adr/0063-continuation-response-reference-binding.md) defines the pure
comparison. Matching references establish neither execution nor authentication.

## Assembled continuation-response semantics

- [x] Require typed read data from the exact bound response and retain the
      qualified second-page context without accepting predecessor data.
- [x] Check envelope identity and inherited account/version/currency scope using
      the actual assembled read role and unchanged shared semantic rules.
- [x] Preserve separate values, unavailable and partial observations, withholding
      qualified data on review and retaining only same-source raw tokens.
- [x] Verify seventy-four independent fixtures and 676 checks, including source
      mixing, page separation, token/parser bounds, cultures and concurrency.
- [x] Add a bounded assembled continuation attempt with a fixed deadline,
      one-time response handoff and terminal metadata cleanup (ADR 0065).

[ADR 0064](adr/0064-continuation-response-semantics.md) defines the pure
comparison. Matching evidence neither proves pagination progress nor starts a read.

## Bounded assembled continuation attempt

- [x] Pin one immutable second-page candidate and absolute deadline without credentials,
      encoding or transport; reject second starts without renewing time.
- [x] Recompute continuation binding and semantics from incoming exact-source read data;
      retain foreign-scope candidates and consume scoped responses once.
- [x] Hand out execution, partial, unavailable or review evidence once, releasing
      pending metadata on all terminal outcomes without starting further continuation.
- [x] Check lifetime/cancellation through final handoff, handle clock failures
      and reentrant abandonment, and expose only scalar snapshot diagnostics.
- [x] Add twenty-seven independent traces and 551 checks, including all existing
      continuation semantic fixtures, late failures, terminal cleanup and concurrent submissions.
- [x] Extend continuation context to later pages from previous continuation evidence,
      advancing client and expected bank message numbers with opaque token preservation (ADR 0066).
- [ ] Add session coordination, multi-page response aggregation and live transport.

[ADR 0065](adr/0065-assembled-continuation-attempt.md) defines the local owner.
Handoff establishes no authentication, TAN/SCA completion, durable replay guard
or account update; a partial outcome ends this continuation attempt.

## Later-page balance continuation context

- [x] Extend continuation context to later pages from previous continuation evidence,
      advancing client and expected bank message numbers with opaque token preservation.
- [x] Check 128-page limit boundary, sequential message counters, unchanged HKSAL operation
      and version, account components, and retrieval options.
- [x] Preserve original capability provenance recursively through the predecessor chain without
      reselection or replacement.
- [x] Verify forty-one independent Python fixtures and 857 checks, including multi-page execution
      chaining, 128-page progression loop, page limit overflow, culture independence and concurrency.
- [ ] Add session coordination, multi-page response aggregation and live transport.

[ADR 0066](adr/0066-later-page-balance-continuation-context.md) defines the pure
comparison. Matching evidence does not authenticate the bank or schedule requests.

## Assembled balance refresh attempt

- [x] Coordinate multi-page PIN/TAN balance refresh lifecycle across page one and
      continuation pages under a single deadline and configurable page budget (1..128).
- [x] Enforce sequential message counter increments, response replay defenses, predecessor
      and origin context provenance to prevent candidate hijacking.
- [x] Aggregate balances across all accepted pages without loss or double counting,
      projecting losslessly into domain `BalanceSnapshot` models.
- [x] Verify twenty-three independent traces and 459 checks, including single and multi-page
      progressions, immediate unavailable reports, page limit boundaries, counter exhaustion,
      foreign dialogues, timeouts, cancellation, clock regression, and scalar-only diagnostics.
- [ ] Add session coordination, multi-bank scheduling, and live transport.

[ADR 0067](adr/0067-assembled-balance-refresh-attempt.md) defines the multi-page coordinator.
Aggregation and terminal states establish no live authentication or transport.

## Assembled all-account capability context

- [x] Compare one `FinTsPinTanReadSignatureContext`, bound `FinTsReadParameterSet`, and user/bank
      parameter evidence for all-account discovery requests (`HKSPA:3:1++J'`).
- [x] Validate signature context, integrated read parameters, HISPAS-1 advertisements, customer signature
      requirements, unlisted blocked operation policies, limits, and bank/dialogue consistency.
- [x] Expose structured diagnostics (`FinTsPinTanAllAccountCapabilityIssue`) and integrate credential
      comparison without retaining, copying, or logging credential bytes.
- [x] Verify twenty-seven independent Python fixtures and 195 checks in `pin-tan-all-account-capability-context-v1.json`,
      including valid one/two-step profiles, unlisted blocked operations, missing/duplicate ads, limits,
      credential comparison, reflection assertions, argument validation, and cancellation.
- [ ] Add all-account request binding, envelopes, response semantics, and discovery attempt coordination.

[ADR 0068](adr/0068-assembled-all-account-capability-context.md) defines the pure comparison.
Matching evidence does not authorize requests or update stored account inventories.

## All-account discovery PIN-only request binding and envelopes

- [x] Pin one candidate `FinTsPinTanAllAccountCapabilityContext` with matching evidence,
      and assemble candidate metadata (`HNHBK:1:3`, `HNVSK:998:3`, `HNVSD:999:1`, `HNSHK:2:4`, `HKSPA:3:1`, `HNSHA:4:2`, `HNHBS:5:1`).
- [x] Assemble plain candidate messages with exact segment ordering, 12-digit wire length calculation,
      and stack buffer zeroization using `FinTsPinTanSignatureTrailerWriter.TryEncodeAllAccountPinOnly`.
- [x] Wrap candidate requests into `HNVSK:998:3` + binary `HNVSD:999:1` encryption envelopes, stripping
      canonical framing, enforcing timestamp alignment, and zeroizing staged buffers upon exit.
- [x] Verify thirty-seven independent Python fixtures and 408 checks in `pin-tan-all-account-request-envelope-v1.json`,
      including buffer reserve bounds, boundary ownership failure injections, TAN-as-PIN rejection,
      multithreaded parallel execution, culture invariance (`de-DE`, `tr-TR`, `en-US`, `ar-SA`), cancellation, and reflection assertions.
- [x] Add all-account response reference binding and semantics (ADR 0070).
- [x] Add all-account discovery attempt coordination (ADR 0071).

[ADR 0069](adr/0069-all-account-request-envelope.md) defines candidate metadata and envelope formatting.
Assembling a candidate envelope neither authorizes transmission nor mutates stored account profiles.

## All-account response reference binding and semantics

- [x] Correlate parsed response envelopes against assembled all-account candidate segment roles,
      dialogue identities, message counter bounds, and profile versions (`FinTsPinTanAllAccountResponseBinding`).
- [x] Enforce read-source provenance requiring exact object reference equality, rejecting byte-identical
      reparsed responses (`ReadSourceMismatch`).
- [x] Verify envelope security header identity against signature header expectations (`EnvelopeIdentityMismatch`).
- [x] Scope status evaluation strictly to read segment 3, matching returned accounts against bound
      parameters and advertisements via `FinTsAllAccountDiscoveryEvidence.MatchAccounts`.
- [x] Isolate account semantics, flagging duplicate returned identities, institution mismatches, identity
      conflicts, and unmatched user accounts while withholding qualified data on review.
- [x] Verify fifty-two independent Python fixtures and 843 checks in `pin-tan-all-account-response-v1.json`,
      including clean discoveries (single, multiple, unavailable), empty UPD accounts, dialogue/profile mismatches,
      role mismatches, security execution segregation, multithreaded concurrency, and culture invariance.
- [x] Add all-account discovery attempt coordination (ADR 0071).
- [ ] Add authenticated security, activation, and transport under M1-05.

[ADR 0070](adr/0070-all-account-response-semantics.md) defines reference binding and semantics.
Matching evidence neither authorizes bank operations nor mutates stored account profiles.

## Assembled all-account discovery attempt

- [x] Coordinate one immutable candidate `FinTsPinTanAllAccountRequestBinding` under an absolute
      monotonic deadline bounded to fifteen minutes (`FinTsPinTanAllAccountDiscoveryAttempt`).
- [x] Recompute reference binding and semantics dynamically upon response acceptance, rejecting
      unrelated precomputed evidence.
- [x] Handle foreign scope discrepancies (`MessageMismatch`, `MissingSegmentReference`, `UnknownSegmentReference`,
      `SegmentRoleMismatch`) with transient `ContextMismatch` without consuming the active candidate or deadline.
- [x] Provide one-time evidence handoff for execution, unavailable, and review outcomes while releasing
      pending candidate metadata immediately on all terminal transitions.
- [x] Fail closed upon cancellation, late timeout, clock invalidation, or reentrant abandonment, leaving
      handled counts at zero and releasing pending metadata.
- [x] Verify twenty-eight independent Python traces and 553 checks in `pin-tan-all-account-attempt-v1.json`,
      including clean discoveries (single, multiple, unavailable), scoped review outcomes, context mismatches,
      deadline equality, cancellation, disposal, clock regression, multithreaded concurrency, and culture invariance.
- [x] Add assembled PIN/TAN read session attempt coordination (ADR 0072).
- [ ] Add authenticated security, activation, and transport under M1-05.

[ADR 0071](adr/0071-assembled-all-account-discovery-attempt.md) defines the attempt lifecycle.
Terminal handoff neither activates a session nor updates stored account inventories.

## Assembled PIN/TAN read session attempt

- [x] Coordinate multi-stage FinTS 3.0 PIN/TAN read dialogues across initialization, operational read
      (all-account discovery or multi-page balance refresh), and dialogue closing under a single monotonic deadline (`FinTsPinTanReadSessionAttempt`).
- [x] Pin bank-assigned dialogue ID and enforce sequential client and expected bank message numbers (`lastClientNumber + 1`, `lastBankNumber + 1`).
- [x] Reject replayed message numbers and incoming responses with message numbers less than or equal to `_lastBankNumber` with `ReplayRejected`.
- [x] Segregate foreign scope discrepancies (`MessageMismatch`, `MissingSegmentReference`, `UnknownSegmentReference`, `SegmentRoleMismatch`) with transient `ContextMismatch` without consuming the active candidate or deadline.
- [x] Provide caller-owned immutable evidence handoff on terminal outcomes while releasing all pending candidate metadata immediately on all terminal transitions and disposal.
- [x] Expose strictly scalar diagnostics via `FinTsPinTanReadSessionSnapshot` without sensitive credentials, PINs, TANs, IBANs, amounts, or raw payloads.
- [x] Verify twenty-eight independent Python traces and 1,230 checks in `pin-tan-read-session-attempt-v1.json`,
      including clean all-account discovery, clean balance refresh (single-page, multi-page, unavailable), bank abort on close (`9800`),
      scoped review outcomes, foreign scope handling, page limits, single monotonic deadline expiration, cancellation across stages, stop, dispose, clock regression, multithreaded concurrency, and culture invariance.
- [x] Add assembled PIN/TAN synchronization session attempt coordination (ADR 0073).
- [ ] Add authenticated security, activation, and transport under M1-05.

[ADR 0072](adr/0072-assembled-read-session-attempt.md) defines the session coordinator architecture.
Successful session completion neither authorizes live payments nor activates background polling.

## Assembled PIN/TAN synchronization session attempt

- [x] Coordinate two-stage FinTS 3.0 PIN/TAN synchronization dialogues across dialogue synchronization (`StartSynchronization` with `HKSYN`, `AcceptSynchronizationResponse`) and dialogue termination (`RecordClosingCandidate`, `AcceptClosingResponse` with `HKEND`) under a single monotonic deadline (`FinTsPinTanSynchronizationSessionAttempt`).
- [x] Pin bank-assigned dialogue ID from synchronization response and enforce sequential client and expected bank message numbers (client message 1 -> bank message 1 -> client message 2 -> bank message 2).
- [x] Reject replayed message numbers and incoming responses with message numbers less than or equal to `_lastBankNumber` with `ReplayRejected`.
- [x] Segregate foreign scope discrepancies (`MessageMismatch`, `MissingSegmentReference`, `UnknownSegmentReference`, `SegmentRoleMismatch`) with transient `ContextMismatch` without consuming the active candidate or deadline.
- [x] Transition clean dialogue termination (`0100`) to `ReinitializationRequired` with caller-owned evidence handoff containing both `SynchronizationEvidence` and `ClosingEvidence`, enforcing FinTS 3.0 synchronization dialogue closure semantics.
- [x] Transition bank abort (`9800`) to `Aborted`, and scoped review outcomes to `NeedsReview`.
- [x] Expose strictly scalar diagnostics via `FinTsPinTanSynchronizationSessionSnapshot` without sensitive credentials, PINs, TANs, system IDs, or raw payloads.
- [x] Verify twenty-four independent Python traces and 626 checks in `pin-tan-synchronization-session-attempt-v1.json`,
      including clean system ID assignment, clean message recovery, one-step assigned system session, escaped system ID and dialogue session, maximum message recovery, bank abort on sync and closing (`9800`), scoped review outcomes, foreign scope handling, closing dialogue mismatch rejection, monotonic message counter replay rejections, single monotonic deadline expiration, in-flight cancellation, stop before start, dispose, clock regression, second start rejection, missing recovery context rejection, multithreaded concurrency, and culture invariance.
- [ ] Add authenticated security, activation, and transport under M1-05.

[ADR 0073](adr/0073-assembled-synchronization-session-attempt.md) defines the session coordinator architecture.
Clean synchronization dialogue closure yields caller-owned evidence requiring session reinitialization without activating background polling.

## Synchronization session clock boundaries

- [x] Calculate elapsed time from the original start so repeated sub-tick
      observations cannot extend the absolute deadline.
- [x] Fail closed on elapsed-time conversion exceptions and negative elapsed time,
      preserving existing terminal outcomes after reentrant clock callbacks.
- [x] Verify 103 additional checks across synchronization and closing, including
      all response publication boundaries and pending-metadata cleanup.
- [x] Complete terminal evidence ownership and cleanup across session coordinators.

[ADR 0074](adr/0074-synchronization-session-clock-boundaries.md) records the
lifecycle correction. No transport, authentication or activation is introduced.

## Session terminal evidence ownership

- [x] Release candidates, recovery context, evidence, dialogue identifiers and
      read-session collection contents on every terminal outcome.
- [x] Preserve caller-owned results and stable collection snapshots across later
      pages, clean closing, cancellation, timeout, clock failure and disposal.
- [x] Keep scalar progress counts independent of retained account and balance data.
- [x] Verify 108 stage/termination combinations and terminal ownership across the
      existing 52 session traces, adding 2,846 executable checks.
- [x] Harden read-session candidate provenance and cross-stage evidence binding.

[ADR 0075](adr/0075-session-terminal-evidence-ownership.md) records the ownership
contract. Read closing evidence belongs to the returned result after cleanup.
Managed-reference release does not erase caller-owned data or activate banking.

## Read-session evidence provenance

- [x] Bind operation and closing candidates to the exact initialization evidence
      accepted by the session; reject detached and foreign-session evidence.
- [x] Bind continuation candidates to the exact accepted predecessor and original
      capability context, including later pages.
- [x] Preserve active state, progress and deadline on a source mismatch so a valid
      candidate can be retried.
- [x] Correct session fixtures to initialize with their operation's read parameters
      and construct candidates from actual handoffs; verify 117 additional checks.
- [ ] Enforce read-session operation scoping and per-operation page accounting.

[ADR 0076](adr/0076-read-session-evidence-provenance.md) records the provenance
contract. No authentication, activation or transport is introduced.

## Remaining acceptance backlog

| Item | Required outcome and evidence | Status |
| --- | --- | --- |
| M1-01 Storage format | Versioned ADRs for exact XML/ZIP/frame bytes, bounds, key derivation/calibration, generation lifecycle, purge, and cross-host vectors; human review before persistence is enabled | Envelope/bootstrap candidate and independent vectors implemented; full schemas, calibration and review pending |
| M1-02 Profile lifecycle | Lock/unlock, one writer, encrypted generation round-trip, wrong passphrase, tamper/truncation, hostile XML/ZIP, interrupted save and prior-generation recovery, lost-key behavior; verified deletion of retained files | Pending |
| M1-03 Identity | Store-wide allocator and atomic entity allocation, startup invariants, exhaustion, exact rediscovery, separate account/incarnation/binding identity, ambiguous and reused locators without overwrite | Allocation, startup checks, lossless locators and rediscovery planner implemented; durable/domain/connector integration and reviewed relinking pending |
| M1-04 Simulator and diagnostics | Synthetic fixtures for success, invalid credentials, locked access, one SCA path, cancellation, timeout, maintenance, changed parameters, malformed and partial responses; sentinel-secret checks across errors and support export | Synthetic workflows, syntax/response vectors, correlation checks, structured diagnostics and preview sentinels implemented; business/security conformance, actual SCA and export integration pending |
| M1-05 FinTS read-only slice | Registered product identity before user-facing live dialogue, initialization, BPD/UPD and capability negotiation, session-only PIN/TAN, one challenge continuation, discovery and balances | Protocol/parameter/TAN and discovery/balance schemas, unsigned read/initialization/synchronization encoding, synchronization schemas/context/closing attempt, PIN/TAN signature-header schemas/context/encoding, local credential ownership, signature-trailer encoding, plain PIN/TAN request/envelope assembly and assembled-request reference binding/initialization/synchronization semantics and initialization/synchronization attempts, assembled initialization procedure/permission/HIPINS requirements integration, first-read PIN/TAN signature context, single-account read-capability integration, credential requirement comparison and PIN-only read trailer/request/envelope assembly with candidate metadata and assembled read-response reference binding/semantics, bounded first-read attempt and balance-continuation/signature context with PIN-only trailer/plain request/envelope encoding and continuation candidate metadata/response reference binding/semantics/attempt, later-page continuation context, multi-page PIN/TAN balance refresh attempt, assembled all-account capability context, all-account PIN-only request binding and envelopes, all-account response reference binding and semantics, assembled PIN/TAN all-account discovery attempt, assembled PIN/TAN read session attempt coordinator, assembled PIN/TAN synchronization session attempt coordinator, PIN/TAN dialogue-end context/encoding/response binding/termination/attempt, dialogue-end schemas/encoding/reply evidence, initialization schemas/response scope/attempt, selected-account read context/attempt, unavailable and all-account discovery evidence/attempt, restricted challenge comparison and synthetic SCA state implemented; recovery application, full security codecs, authenticated security, activation, transport and live ingestion pending |
| M1-06 Windows setup | Manual setup UI and endpoint-review integration, session invalidation on endpoint edits, application lock, account selection/list/detail, progress, actionable errors, keyboard/screen-reader/high-contrast/text-scale operation | Domain endpoint model implemented; UI pending |
| M1-07 Truthful cached values | Exact amounts/currency and bank/retrieval timestamps, booked/available distinction, missing as unavailable, stale/partial/offline states, no mixed-currency totals, durable replay evidence, unsupported account shells | Exact money and pure account-value/freshness/total projections implemented; connector, replay, durable storage and UI integration pending |
| M1-08 Disconnect and support | Session-buffer cleanup, connection removal with separate cached-data deletion and purge, encrypted local diagnostics, previewed redacted export, no reporting channel | Test-only synthetic buffer cleanup and in-memory structured preview implemented; live session lifecycle, deletion/purge, encrypted logs and export UI pending |
| M1-09 Live acceptance | One controlled institution/account-type/SCA row, bank-view identity/value comparisons and timestamp evidence, first balance within five minutes; no production data in source/CI | Pending external evidence |
| M1-10 Release approval | Product-registration owner/ID, exact-model legal memo, release owner and second security reviewer, artifact-bound approval with no unresolved critical/high findings | Pending human work |

## Verification evidence

On 2026-09-05 the M0 baseline passed `./eng/Invoke-CI.ps1 -Scope Full` locally
on Windows with .NET SDK **10.0.400**, including format verification, the kernel
dependency guard, a warning-free Release build, executable checks, and host smoke
run. The same full command passes for all current endpoint, identity, rediscovery,
value, simulator/diagnostic, FinTS syntax/response/correlation and storage-candidate increments.
Identity adds 84 checks, rediscovery 90, account values 109, and
simulator/diagnostics 280; FinTS syntax adds six independent vectors and 1,350 checks;
typed responses/correlation add five independent vectors and 177 checks;
parameters add five independent vectors and 181 checks;
read schemas/capability matching add five independent vectors and 108 checks;
PIN/TAN envelope/parameter schemas add five independent vectors and 202 checks;
TAN procedure/challenge schemas add six independent vectors and 249 checks;
permitted procedures/request-bound challenge evidence adds seven independent
vectors and 191 checks;
in-memory SCA continuation adds eight independent traces and 123 checks;
account-discovery/balance schemas add eight independent fixtures and 393 checks;
single-account read context adds eight independent fixtures and 236 checks;
in-memory read refresh adds eight independent traces and 137 checks;
scoped read unavailability adds eight independent fixtures and 85 checks;
all-account discovery adds eight independent fixtures and 105 checks;
all-account discovery attempts add eight independent traces and 108 checks;
unsigned read-request encoding adds ten independent vectors and 512 checks;
initialization schemas/encoding add eight independent vectors and 295 checks;
initialization response scope adds fourteen independent vectors and 120 checks;
initialization attempts add eight independent traces and 125 checks;
synchronization schemas/encoding add ten independent vectors and 165 checks;
synchronization context adds fifteen independent vectors and 149 checks;
dialogue-end schemas/reply evidence add ten independent vectors and 142 checks;
synchronization-and-closing attempts add twelve independent traces and 249 checks;
PIN/TAN signature-header schemas add ten independent vectors and 295 checks;
PIN/TAN signature context adds eighteen independent vectors and 139 checks;
PIN/TAN signature-header encoding adds ten independent vectors and 336 checks;
session credential ownership adds ten independent traces and 136 checks;
PIN/TAN signature-trailer encoding adds twelve independent vectors and 106 checks;
PIN/TAN request assembly adds fourteen independent vectors and 157 checks;
PIN/TAN request-envelope assembly adds seventeen independent vectors and 207 checks;
assembled PIN/TAN reference binding adds twenty-three independent vectors and 361 checks;
assembled PIN/TAN initialization semantics adds thirty independent vectors and 199 checks;
assembled PIN/TAN synchronization semantics adds thirty-three independent vectors and 217 checks;
assembled PIN/TAN initialization attempts add thirteen independent traces and 156 checks;
assembled PIN/TAN synchronization attempts add twenty-one independent traces and 228 checks;
PIN/TAN dialogue-end context/encoding adds twenty independent vectors and 187 checks;
PIN/TAN closing response binding/termination adds forty-six independent vectors and 409 checks;
PIN/TAN closing attempts add twenty independent traces and 260 checks;
assembled initialization procedure integration adds twenty-eight independent vectors and 261 checks;
assembled initialization HIPINS requirements add thirty independent vectors and 312 checks;
first-read PIN/TAN signature context adds thirty-seven independent vectors and 230 checks;
integrated first-read capability context adds forty-three independent vectors and 390 checks;
first-read credential requirement comparison adds forty-five independent vectors and 659 checks;
first-read PIN-only trailer encoding adds twenty-two independent vectors and 243 checks;
first-read PIN-only request assembly adds thirty-one independent vectors and 334 checks;
first-read PIN-only request envelopes add thirty-three independent vectors and 456 checks;
assembled first-read response reference binding adds forty-six independent vectors and 481 checks;
assembled first-read response semantics adds fifty-nine independent vectors and 495 checks;
assembled first-read attempts add twenty-seven independent traces and 544 checks;
assembled balance-continuation context adds forty-one independent vectors and 348 checks;
balance-continuation PIN/TAN signature context adds sixty-nine independent vectors and 531 checks;
continuation PIN-only trailer encoding adds eighty-two independent vectors and 741 checks;
continuation PIN-only plain request assembly adds eighty-two independent vectors and 691 checks;
continuation PIN-only request envelopes add eighty-two independent vectors and 627 checks;
continuation-response reference binding adds fifty-five independent vectors and 576 checks;
continuation-response semantics add seventy-four independent vectors and 676 checks;
assembled continuation attempts add twenty-seven independent traces and 551 checks;
later-page balance-continuation context adds forty-one independent vectors and 857 checks;
assembled PIN/TAN read refresh attempts add twenty-three independent traces and 459 checks;
assembled all-account capability context adds twenty-seven independent vectors and 195 checks;
all-account discovery request envelopes add thirty-seven independent vectors and 408 checks;
all-account response reference binding and semantics adds fifty-two independent vectors and 843 checks;
assembled PIN/TAN all-account discovery attempts add twenty-eight independent traces and 553 checks;
assembled PIN/TAN read session attempt coordinator adds twenty-eight independent traces and 1,230 checks;
assembled PIN/TAN synchronization session attempt coordinator adds twenty-four independent traces and 626 checks;
storage adds five independent vectors and 1,752 checks. The checks include a
100,000-record revision chain and 10,000-account rediscovery/value projections.

Linux execution and hosted CI evidence have not been collected in this work.
Human release approvals and the compatibility table remain pending. No bank
connection, credential, persistence, or payment capability is enabled.

The complete Windows CI command also passed on 2026-09-06 after adding the
BPD/UPD, read-capability, PIN/TAN/TAN schemas, request-bound challenge and
in-memory SCA continuation increments, with zero
build warnings or errors.

On 2026-09-07 the full Windows CI command passed again with the discovery/balance
schemas and all 393 new checks. The build had zero warnings or errors; fixture
regeneration reproduced every FinTS JSON file and documentation links passed.

The full Windows CI command passed again on 2026-09-07 with the single-account
read-context increment and its 236 checks, with zero build warnings or errors.
All FinTS fixtures reproduced byte-for-byte and documentation links passed.

The full Windows CI command also passed on 2026-09-07 with the read-refresh
lifecycle and its 137 checks, with zero build warnings or errors. All FinTS
fixtures reproduced byte-for-byte and documentation links passed.

The full Windows CI command passed on 2026-09-07 with scoped read-unavailability
handling and its 85 checks, with zero build warnings or errors. All FinTS fixtures
reproduced byte-for-byte and documentation links passed.

The full Windows CI command passed on 2026-09-07 with all-account discovery
evidence and its 105 checks, with zero build warnings or errors. All FinTS fixtures
reproduced byte-for-byte and documentation links passed.

The full Windows CI command passed on 2026-09-07 with the all-account discovery
attempt and its 108 checks, with zero build warnings or errors. All FinTS
fixtures reproduced byte-for-byte and documentation links passed.

The full Windows CI command passed on 2026-09-07 with unsigned read-request
encoding, ten independent wire vectors and 512 new checks, with zero build
warnings or errors. All FinTS fixtures reproduced byte-for-byte and documentation
links passed.

The full Windows CI command passed on 2026-09-07 with initialization schemas and
encoding, eight independent wire vectors and 295 new checks, with zero build
warnings or errors. All FinTS fixtures reproduced byte-for-byte and documentation
links passed.

The full Windows CI command passed on 2026-09-07 with initialization response
binding, fourteen independent fixtures and 120 new checks, with zero build warnings
or errors. All FinTS fixtures reproduced byte-for-byte and documentation links passed.

The full Windows CI command passed on 2026-09-07 with the initialization attempt,
eight independent traces and 125 new checks, with zero build warnings or errors.
All FinTS fixtures reproduced byte-for-byte and documentation links passed.

The full Windows CI command passed on 2026-09-08 with synchronization schemas
and unsigned encoding, ten independent fixtures and 165 new checks, with zero
build warnings or errors. All FinTS fixtures reproduced byte-for-byte and
documentation links passed.

The full Windows CI command passed on 2026-09-08 with synchronization context
comparison, fifteen independent fixtures and 149 new checks, with zero build
warnings or errors. All FinTS fixtures reproduced byte-for-byte and documentation
links passed.

The full Windows CI command passed on 2026-09-08 with dialogue-end schemas,
unsigned encoding and reply comparison, ten independent fixtures and 142 new
checks, with zero build warnings or errors. All FinTS fixtures reproduced
byte-for-byte and documentation links passed.

The full Windows CI command passed on 2026-09-08 with the synchronization-and-
closing attempt, twelve independent traces and 249 new checks, with zero build
warnings or errors. All FinTS fixtures reproduced byte-for-byte and documentation
links passed.

The full Windows CI command passed on 2026-09-08 with PIN/TAN signature-header
schemas, ten independent fixtures and 295 new checks, with zero build warnings
or errors. All FinTS fixtures reproduced byte-for-byte and documentation links
passed.

The full Windows CI command passed on 2026-09-08 with PIN/TAN signature-header
context comparison, eighteen independent fixtures and 139 new checks, with zero
build warnings or errors. All FinTS fixtures reproduced byte-for-byte and
documentation links passed.

The full Windows CI command passed on 2026-09-08 with typed PIN/TAN signature-
header encoding, ten independent fixtures and 336 new checks, with zero build
warnings or errors. All FinTS fixtures reproduced byte-for-byte and documentation
links passed.

The full Windows CI command passed on 2026-09-08 with session credential buffer
ownership, ten independent traces and 136 new checks, with zero build warnings
or errors. All FinTS fixtures reproduced byte-for-byte and documentation links
passed.

The full Windows CI command passed on 2026-09-08 with PIN/TAN signature-trailer
encoding, twelve independent fixtures and 106 new checks, with zero build warnings
or errors. All FinTS fixtures reproduced byte-for-byte and documentation links
passed.

The full Windows CI command passed on 2026-09-08 with plain PIN/TAN request
assembly, fourteen independent fixtures and 157 new checks, with zero build
warnings or errors. All FinTS fixtures reproduced byte-for-byte and documentation
links passed.

The full Windows CI command passed on 2026-09-08 with PIN/TAN request-envelope
assembly, seventeen independent fixtures and 207 new checks, with zero build
warnings or errors. All FinTS fixtures reproduced byte-for-byte and documentation
links passed.

The full Windows CI command passed on 2026-09-09 with assembled PIN/TAN
request/response reference binding, twenty-three independent fixtures and 361 new
checks, with zero build warnings or errors. All FinTS fixtures reproduced
byte-for-byte and documentation links passed.

The full Windows CI command passed on 2026-09-09 with assembled PIN/TAN
initialization semantics, thirty independent fixtures and 199 new checks, with
zero build warnings or errors. All FinTS fixtures reproduced byte-for-byte and
documentation links passed.

The full Windows CI command passed on 2026-09-09 with assembled PIN/TAN
synchronization semantics, thirty-three independent fixtures and 217 new checks,
with zero build warnings or errors. All FinTS fixtures reproduced byte-for-byte
and documentation links passed.

The full Windows CI command passed on 2026-09-09 with the assembled PIN/TAN
initialization attempt, thirteen independent traces and 156 new checks, with zero
build warnings or errors. All FinTS fixtures reproduced byte-for-byte and
documentation links passed.

The full Windows CI command passed on 2026-09-09 with the assembled PIN/TAN
synchronization attempt, twenty-one independent traces and 228 new checks, with
zero build warnings or errors. All FinTS fixtures reproduced byte-for-byte and
documentation links passed.

The full Windows CI command passed on 2026-09-09 with PIN/TAN dialogue-end
context/encoding, twenty independent vectors and 187 new checks, with zero build
warnings or errors. All FinTS fixtures reproduced byte-for-byte and documentation
links passed.

The full Windows CI command passed on 2026-09-09 with assembled PIN/TAN closing
response binding/termination, forty-six independent vectors and 409 new checks,
with zero build warnings or errors. All FinTS fixtures reproduced byte-for-byte
and documentation links passed.

The full Windows CI command passed on 2026-09-09 with the assembled PIN/TAN
closing attempt, twenty independent traces and 260 new checks, with zero build
warnings or errors. All FinTS fixtures reproduced byte-for-byte and documentation
links passed.

The full Windows CI command passed on 2026-09-09 with assembled initialization
procedure/permission integration, twenty-eight independent vectors and 261 new
checks, with zero build warnings or errors. All FinTS fixtures reproduced
byte-for-byte and documentation links passed.

The full Windows CI command passed on 2026-09-10 with assembled initialization
HIPINS requirements, thirty independent vectors and 312 new checks, with zero
build warnings or errors. All FinTS fixtures reproduced byte-for-byte and
documentation links passed.

The full Windows CI command passed on 2026-09-10 with first-read PIN/TAN
signature context, thirty-seven independent vectors and 230 new checks, with zero
build warnings or errors. All FinTS fixtures reproduced byte-for-byte and
documentation links passed.

The full Windows CI command passed on 2026-09-10 with initialization read-schema
integration and first-read account capability checks, forty-three independent
vectors and 390 new checks, with zero build warnings or errors. All FinTS fixtures
reproduced byte-for-byte and documentation links passed.

The full Windows CI command passed on 2026-09-10 with first-read credential
requirement comparison, forty-five independent vectors and 659 new checks, with
zero build warnings or errors. All FinTS fixtures reproduced byte-for-byte and
documentation links passed.

The full Windows CI command passed on 2026-09-10 with first-read PIN-only trailer
encoding, twenty-two independent vectors and 243 new checks, with zero build
warnings or errors. All FinTS fixtures reproduced byte-for-byte and documentation
links passed.

The full Windows CI command passed on 2026-09-10 with first-read PIN-only request
assembly, thirty-one independent vectors and 334 new checks, with zero build
warnings or errors. All FinTS fixtures reproduced byte-for-byte and documentation
links passed.

The full Windows CI command passed on 2026-09-10 with first-read PIN-only request
envelopes, thirty-three independent vectors and 456 new checks, with zero build
warnings or errors. All FinTS fixtures reproduced byte-for-byte and documentation
links passed.

The full Windows CI command passed on 2026-09-10 with assembled first-read
response reference binding, forty-six independent vectors and 481 new checks,
with zero build warnings or errors. All FinTS fixtures reproduced byte-for-byte
and documentation links passed.

The full Windows CI command passed on 2026-09-10 with assembled first-read
response semantics, fifty-nine independent vectors and 495 new checks, with zero
build warnings or errors. All FinTS fixtures reproduced byte-for-byte and
documentation links passed.

The full Windows CI command passed on 2026-09-10 with the assembled first-read
attempt, twenty-seven independent traces and 544 new checks, with zero build
warnings or errors. All FinTS fixtures reproduced byte-for-byte and documentation
links passed.

The full Windows CI command passed on 2026-09-10 with assembled balance-continuation
context, forty-one independent vectors and 348 new checks, with zero build warnings
or errors. All FinTS fixtures reproduced byte-for-byte and documentation links passed.

The full Windows CI command passed on 2026-09-10 with balance-continuation PIN/TAN
signature context, sixty-nine independent vectors and 531 new checks, with zero
build warnings or errors. All FinTS fixtures reproduced byte-for-byte and
documentation links passed.

The full Windows CI command passed on 2026-09-11 with continuation PIN-only
trailer encoding, eighty-two independent vectors and 741 new checks, with zero
build warnings or errors. All FinTS fixtures reproduced byte-for-byte and
documentation links passed.

The full Windows CI command passed on 2026-10-14 with continuation PIN-only
plain request assembly, eighty-two independent vectors and 691 new checks, with
zero build warnings or errors.

The full Windows CI command passed on 2026-10-14 with continuation PIN-only
request envelopes, eighty-two independent vectors and 627 new checks, with zero
build warnings or errors. All 53 FinTS fixture files reproduced byte-for-byte.

The full Windows CI command passed on 2026-10-14 with assembled continuation
response reference binding, fifty-five independent vectors and 576 new checks,
with zero build warnings or errors. All 54 FinTS fixture files reproduced
byte-for-byte.

The full Windows CI command passed on 2026-10-14 with assembled continuation
response semantics, seventy-four independent vectors and 676 new checks, with
zero build warnings or errors. All 55 FinTS fixture files reproduced byte-for-byte.

The full Windows CI command passed on 2026-10-14 with the assembled continuation
attempt, twenty-seven independent traces and 551 new checks, with zero build
warnings or errors. All 56 FinTS fixture files reproduced byte-for-byte and
documentation links passed.

The full Windows CI command passed on 2026-10-14 with later-page balance-continuation
context, forty-one independent vectors and 857 new checks, with zero build warnings
or errors. All 57 FinTS fixture files reproduced byte-for-byte.

The full Windows CI command passed on 2026-10-14 with the assembled PIN/TAN read
refresh attempt, twenty-three independent traces and 459 new checks, with zero build
warnings or errors. All 58 FinTS fixture files reproduced byte-for-byte and documentation
links passed.

The full Windows CI command passed on 2026-10-14 with assembled all-account
capability context, twenty-seven independent vectors and 195 new checks, with zero
build warnings or errors. All 59 FinTS fixture files reproduced byte-for-byte and
documentation links passed.

The full Windows CI command passed on 2026-10-14 with all-account PIN-only request
binding and envelopes, thirty-seven independent vectors and 408 new checks, with
zero build warnings or errors. All 60 FinTS fixture files reproduced byte-for-byte
and documentation links passed.

The full Windows CI command passed on 2026-10-14 with all-account response reference
binding and semantics, fifty-two independent vectors and 843 new checks, with zero
build warnings or errors. All 61 FinTS fixture files reproduced byte-for-byte and
documentation links passed.

The full Windows CI command passed on 2026-10-14 with the assembled PIN/TAN all-account
discovery attempt, twenty-eight independent traces and 553 new checks, with zero
build warnings or errors. All 62 FinTS fixture files reproduced byte-for-byte and
documentation links passed.

The full Windows CI command passed on 2026-10-14 with the assembled PIN/TAN read
session attempt coordinator, twenty-eight independent traces and 1,230 new checks,
with zero build warnings or errors. All 63 FinTS fixture files reproduced byte-for-byte
and documentation links passed.

The full Windows CI command passed on 2026-10-14 with the assembled PIN/TAN synchronization
session attempt coordinator, twenty-four independent traces and 626 new checks,
with zero build warnings or errors. All 64 FinTS fixture files reproduced byte-for-byte
and documentation links passed.

The full Windows CI command passed on 2026-10-14 with synchronization session
clock hardening and 103 additional checks, with zero build warnings or errors.
Existing wire fixtures and generation remain unchanged.

The full Windows CI command passed on 2026-10-14 with session terminal evidence
ownership and cleanup: 3,610 read-session checks, 1,092 synchronization-session
checks and the existing 103 clock-boundary checks. Build completed with zero
warnings or errors. Wire fixtures and their generator remain unchanged.

The full Windows CI command passed on 2026-10-14 with read-session evidence
provenance and 117 additional checks, alongside 3,610 existing read-session checks.
Build completed with zero warnings or errors. All 64 fixtures reproduce byte-for-byte
from the generator, including corrected session initialization parameter evidence.

Next implementation increment: read-session operation scoping and per-operation
page accounting under M1-05, without activation.
M1-01 remains open for full storage schemas,
calibration and review. Registration,
legal review, controlled-account access, and release-owner assignment require
their respective human owners.
