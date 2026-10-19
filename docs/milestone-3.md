# Milestone 3 — Reliable multibank read-only beta

- **Brand:** Broiler
- **Product:** Fond - Finance on Demand
- **Technical name:** `Broiler.Fond`

**Status:** Complete (Milestone 0, 1, and 2 complete; Milestone 3 complete).

**Last updated:** 2026-10-18

The [roadmap](roadmap.md#m3--reliable-multibank-read-only-beta) defines the product specification.
This backlog breaks Milestone 3 into reviewable increments; completion of one increment does not
imply completion of the beta.

---

## 1. Outcome

The read-only product is supportable across its declared verified compatibility table before payment
code is enabled. The application coordinates multiple financial institutions and logins concurrently,
manages institution-specific concurrency, enforces permission-aware foreground refresh, handles
advanced SCA flows (decoupled app approval and visual challenges), isolates bank quirks through versioned
compatibility profiles, imports offline statements, and hardens profile restore and support diagnostics.

---

## 2. Milestone Principles and Invariants

- **Read-only boundary:** Milestone 3 remains strictly read-only. No payment code or money-movement
  capabilities are enabled until read-only sync is proven across all declared institutions.
- **Strict BCL-only kernel boundary:** `Broiler.Fond.Kernel` has zero NuGet packages or native dependencies.
- **Per-institution serialization & cross-institution concurrency:** Concurrent network requests across
  different institutions are encouraged; concurrent requests to the *same* institution login are strictly
  serialized to prevent session invalidation, PIN lockout, or bank rate limiting.
- **Ephemeral session credentials & zero persistence:** Banking PINs and TANs are held only in bounded
  in-memory buffers during an active, user-opted-in foreground session. Credentials are never written to disk,
  never persisted in archives or logs, and never used in unattended background loops.
- **Zero remote control channel:** Connector compatibility profiles and workarounds are built directly into
  signed binaries. No remote server can push profile updates, disable banks, or telemetry-track users.
- **Atomic per-connection commits:** Each institution's sync operates as an independent transaction.
  A network or authentication failure on one institution never aborts or corrupts cached data from another.
- **Truthful connection states:** Connections explicitly transition between `Unconfigured`, `Authenticating`,
  `ChallengeRequired`, `Connected`, `ReauthenticationRequired`, `TemporarilyUnavailable`, and `Disabled`.
  Transient maintenance or backoff windows are explicitly tracked with bank-advertised or monotonic delays.
- **Offline data resilience:** Local storage remains crash-resilient and internally consistent across
  abrupt process termination, partial responses, network loss, and system clock adjustments.

---

## 3. Work Breakdown Structure (M3-01 through M3-06)

### M3-01: Multibank Connection Orchestration & Per-Institution Concurrency

- [x] Design and implement multibank connection orchestration ([ADR 0100](adr/0100-multibank-connection-orchestration-and-concurrency.md)):
  - Per-institution concurrency coordinator: serialize dialogues to the same bank endpoint/login while executing independent institutions concurrently.
  - Retry and backoff policy: handle transient network interruptions, bank maintenance (e.g. `9050` / `9010`), and rate limits with non-blocking backoff and explicit `TemporarilyUnavailable` state.
  - Atomic local commits: each connection's refresh commits independently to the local profile store without blocking or rolling back other connections.
  - Permission-aware foreground session coordinator: execute scheduled or manual refreshes using active in-memory session PINs only when explicitly permitted by user; fail fast to `UserActionRequired` without repetitive dialog prompts when credentials expire.
- [x] Add unit and integration verification suites for multibank concurrency, serialization, fail-closed backoff, and independent commit isolation (`MultibankOrchestrationTests`).

### M3-02: Versioned Connector Compatibility Profiles & Bank Incompatibility Ledger

- [x] Implement versioned connector compatibility profiles ([ADR 0101](adr/0101-connector-compatibility-profiles-and-incompatibility-ledger.md)):
  - Built-in compatibility engine shipped in application binaries with zero remote control channel.
  - Define institution-specific quirks (e.g. segment version pinning, encoding overrides, pagination limitations, date bounds).
  - Support local user override to disable workarounds per institution or connection.
- [x] Establish known-incompatibility ledger in `docs/compatibility/incompatibility-ledger.md` recording failed combinations, evidence, affected builds, and reasons.
- [x] Verify profile resolution, workaround overrides, and ledger maintenance (`ConnectorCompatibilityTests`).

### M3-03: Advanced SCA Procedures & Decoupled / App Approval Continuation

- [x] Extend FinTS 3.0 SCA continuation for advanced bank procedures ([ADR 0102](adr/0102-decoupled-sca-and-visual-challenge-continuation.md)):
  - Decoupled / app-approval continuation: handle `HITAN` decoupled process variants, bank push notifications, non-blocking polling loops at bank-specified intervals, monotonic deadlines, and user cancellation.
  - Visual and matrix challenges: extract and parse flicker code, QR code, and photoTAN payloads without third-party native libraries.
- [x] Verify decoupled polling, timeout fail-closed semantics, and visual challenge parsers (`AdvancedScaContinuationTests`).

### M3-04: Offline Fallback Statement File Import (CAMT & MT940)

- [x] Implement offline statement file import engine ([ADR 0103](adr/0103-offline-statement-file-import.md)):
  - Ingest local MT940/MT942 and CAMT (`camt.052`, `camt.053`) files directly without bank credentials.
  - Reuse canonical wire parsers (`Mt940Parser`, `CamtXmlParser`) and deduplication logic.
  - Preserve explicit `FileImport` observation provenance while correlating with existing account bindings and identity ledger.
- [x] Verify file ingestion, format detection, duplicate rejection, and identity integrity (`OfflineStatementImportTests`).

### M3-05: Backup Migration, Fork Detection, and ID Remapping Hardening

- [x] Harden profile import, backup migration, and fork detection ([ADR 0104](adr/0104-backup-migration-and-fork-detection-hardening.md)):
  - Verify acyclic revision graphs and reference integrity across merged snapshots.
  - Detect divergent lineage forks with conflicting entity revisions.
  - Require explicit reauthentication for imported connections by transitioning them to `ReauthenticationRequired`.
- [x] Verify cross-snapshot remapping, fork conflict rejection, and reauthentication requirements (`BackupMigrationHardeningTests`).

### M3-06: Windows Host Multibank Management, Freshness & Support Export

- [x] Implement multibank management and support views in `Broiler.Fond.Host.Windows` ([ADR 0105](adr/0105-windows-host-multibank-management-and-support-export.md)):
  - Multibank dashboard showing connection status, per-account freshness, and individual refresh controls.
  - Connection management dialogs (add connection, reauthenticate, edit endpoint, remove connection).
  - Accessible, user-initiated redacted diagnostic support bundle export with full preview before disk write.
- [x] Verify accessibility, keyboard navigation, and headless self-tests (`HostSelfTest`).

---

## 4. Exit Criteria

- The verified compatibility table reaches at least three manually configured institutions across two banking groups and two distinct SCA procedures.
- Every verified row passes the recorded simulator suite plus 20 consecutive eligible live read runs; outages and cancellations are transparently recorded.
- Connector workarounds can be disabled locally by configuration without remote intervention.
- Offline data remains internally consistent across process termination, network loss, duplicated pages, and clock changes.
- Restore preserves local identities and mappings, creating explicit reconnect tasks without persisting credentials.
- Product, privacy, regulatory classification, and independent security reviews are recorded before public beta distribution.
