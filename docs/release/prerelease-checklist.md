# Prerelease and release checklist

> **Template status: PENDING. It is not an approval record.**

Use a fresh copy for every externally distributed candidate. Complete it
against the exact source revision and artifacts recorded in
[HUMAN_REVIEW.md](../../HUMAN_REVIEW.md). Milestone 0 is an inert scaffold and
must not be distributed as a banking-capable prerelease.

## Candidate identity

- [ ] Product name is exactly `Broiler Fond - Finance on Demand`.
- [ ] Candidate version and channel are recorded.
- [ ] Exact source commit is recorded and immutable.
- [ ] Reproducible build workflow/run and toolchain versions are recorded.
- [ ] Every artifact name, signature identity, and SHA-256 digest is recorded.
- [ ] Candidate scope and excluded/deferred features are explicit.

## Build and repository controls

- [ ] Clean restore, formatting, analyzer, build, and verification steps pass.
- [ ] Kernel-boundary guard passes on every required host.
- [ ] Kernel uses no third-party package, native asset, platform-specific API,
      host reference, or unsafe code.
- [ ] Generated artifact inventory contains only expected files.
- [ ] Secret and sensitive-financial-data scans pass for source, history,
      fixtures, logs, symbols, and artifacts.
- [ ] Software bill of materials or equivalent BCL/runtime inventory is
      retained with the candidate.

## Functional evidence

- [ ] Each enabled capability has passing deterministic fixtures, negative
      tests, limit tests, and failure-path tests.
- [ ] No placeholder reports success or silently mutates user data.
- [ ] Compatibility claims match only currently verified rows.
- [ ] Known incompatibilities and material limitations are documented.
- [ ] Upgrade, downgrade refusal, recovery, backup, restore, and portability
      behavior are tested when persistence exists.
- [ ] Snapshot save/backup time, peak memory, and write-amplification gates pass
      when persistence exists.

## Security and privacy

- [ ] Threat model covers the exact enabled scope and distribution flow.
- [ ] All release-blocking security findings are closed and retested.
- [ ] Cryptographic formats and parameters have current human review when
      encrypted persistence exists.
- [ ] Cross-host storage vectors pass on every claimed host when portability is
      claimed.
- [ ] No plaintext secret or profile remains in files, temporary files, logs,
      dumps, diagnostics, clipboard state, or build artifacts beyond explicitly
      documented unavoidable operating-system behavior.
- [ ] Privacy deletion and passphrase rotation purge superseded generations and
      temporary artifacts as designed, with physical-erasure limitations stated.
- [ ] Automated and manual egress review finds no telemetry, analytics,
      tracking, automatic crash upload, or background reporting path.
- [ ] Diagnostic export, if present, is local, explicit, bounded, reviewable,
      and never automatically transmitted.
- [ ] A monitored private vulnerability-reporting channel and response owner
      are published in `SECURITY.md`.
- [ ] Packaging, code signing, update signing, rollback behavior, and signature
      verification pass for the channel.

## Banking and protocol evidence

- [ ] Institution endpoints come from the documented manual/authenticated
      source process; unexpected host changes are quarantined for user review.
- [ ] TLS behavior, certificate failure, redirects, timeouts, malformed input,
      archive/XML limits, and protocol downgrade behavior pass negative tests.
- [ ] Credential, PIN, TAN, and in-memory secret lifetimes match the reviewed
      design.
- [ ] Scheduled work cannot proceed without the required in-memory credential
      scope or an explicit user action.
- [ ] Account identity, source observations, balance provenance, transaction
      corrections, replay handling, restore/merge remapping, and relinking
      evidence pass their applicable tests.
- [ ] Regulatory and scheme claims reference the currently applicable official
      material and have been rechecked for the release date.

## Additional payment gate

Complete this section for any candidate that can create, authorize, schedule,
queue, or submit a money movement. Mark it `Not applicable` with justification
for read-only candidates.

- [ ] Payment scope applicability and justification are recorded.
- [ ] Beneficiary, amount, currency, execution date, fees, remittance, and bank
      response are shown for final user confirmation.
- [ ] Verification of Payee result and user decision are retained where
      applicable; ambiguous or unavailable results never become a false match.
- [ ] Idempotency, retry, timeout, unknown-outcome, duplicate-prevention, and
      bank-correction scenarios pass.
- [ ] Payment audit evidence is authenticated and its limits against a key
      holder or local rollback are disclosed.
- [ ] An independent payment-path penetration test covers the exact candidate.
- [ ] Penetration-test release blockers are closed and independently retested.

## Product and human review

- [ ] Accessibility status and unresolved material defects are documented.
- [ ] User-facing privacy, recovery, compatibility, and security limitations are
      accurate and consistent with the implementation.
- [ ] Distribution, update, support, and data flows still satisfy the accepted
      purely user-operated boundary.
- [ ] Required product, privacy, legal/regulatory, and security reviews are
      attached for the exact candidate.
- [ ] Release owner signs the exact candidate identity and artifact digests.
- [ ] A different human security reviewer signs the same candidate identity and
      artifact digests.

## Findings and decision

| Finding | Severity | Owner | Disposition/evidence | Status |
| --- | --- | --- | --- | --- |
| `UNSET` | `UNSET` | `UNSET` | `UNSET` | `OPEN` |

Open critical or high findings block distribution. Any lower-severity accepted
risk requires written rationale, scope, owner, and target milestone.

**Final decision:** `PENDING`

