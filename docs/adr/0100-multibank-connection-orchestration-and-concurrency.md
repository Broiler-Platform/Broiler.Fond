# ADR 0100: Multibank Connection Orchestration, Concurrency Boundaries, and Permission-Aware Foreground Refresh

## Status
Accepted

## Context
In Broiler Fond - Finance on Demand, a consumer may maintain accounts across multiple distinct financial institutions (e.g. Commerzbank, Deutsche Bank, Sparkasse, ING) as well as multiple distinct logins at the same institution (e.g. personal and joint logins). Milestone 3 requires reliable multibank operation while preserving the core product principles:
1. **Direct client-to-bank communication:** Zero Broiler-operated backend, proxy, or cloud aggregation.
2. **Ephemeral credentials:** Banking PINs and TANs are held exclusively in volatile memory for active sessions; they are never written to disk, stored in profile XML archives, or passed to unattended background daemons.
3. **Truthful states:** Connection operational states (`Unconfigured`, `Authenticating`, `ChallengeRequired`, `Connected`, `ReauthenticationRequired`, `TemporarilyUnavailable`, `Disabled`) must accurately reflect bank status.
4. **Resilience without ambiguous retries:** Transient network or maintenance conditions must be isolated, avoiding lockouts or hammering bank servers.
5. **No silent conflation or cross-connection contamination:** A failure on one institution must not abort, corrupt, or block another institution's sync.

FinTS 3.0 dialogues are strictly stateful per dialogue:
- Each `BankConnection` tracks a monotonic sequence of `NextClientMessageNumber` and active `SystemId`. Concurrent requests over the same connection would corrupt message number synchronization.
- Many bank servers enforce concurrency limits or IP/user rate throttling per institution. Running concurrent dialogues against the same bank endpoint/login can trigger dialogue aborts (`9800 Dialogabbruch`) or security blocks.
- Conversely, requests to *different* banking institutions are completely independent and should execute concurrently up to a bounded concurrency ceiling (e.g. 2–4 concurrent institutions) to minimize total refresh latency for multibank users.

## Decisions

### 1. Per-Institution Serialization and Cross-Institution Concurrency
We introduce `InstitutionConcurrencyCoordinator` in `Broiler.Fond.Kernel.Domain.Connections`:
- **Connection Boundary:** Each `ConnectionId` has an exclusive execution lock. Concurrent operations on the same connection are strictly queued/serialized.
- **Institution Boundary:** Each `InstitutionId` (or bank endpoint hostname + BLZ) has an exclusive lock or dedicated queue. Dialogues to the same financial institution are serialized to avoid server-side rate limits or session aborts.
- **Cross-Institution Concurrency:** Dialogues targeting *different* institutions execute in parallel up to a bounded maximum concurrency limit (default 3, configurable between 1 and 8).
- **Cancellation & Timeouts:** All semaphore/lock acquisitions accept `CancellationToken` and monotonic timeouts, guaranteeing zero thread deadlock and zero lingering locks on cancellation.

### 2. Transient Failure Backoff and Maintenance Isolation
We implement `ConnectionBackoffTracker` and `ConnectionRetryPolicy`:
- **Transient Failure Classification:**
  - FinTS maintenance and temporary errors: `9010` (transient processing error), `9050` (partial dialogue abort), `9800` (dialogue cancelled by bank due to system load/maintenance).
  - Network transport errors: HTTP 502/503/504, connection timeouts, reset by peer, transient DNS failures.
  - Note: Credential failures (`9930`, `9931`, `9942`, wrong PIN/TAN) and permanent security lockouts are classified as `AuthenticationFailed` / `ReauthenticationRequired` and never trigger automatic backoff retries.
- **Monotonic Exponential Backoff:**
  - Base delay: 30 seconds.
  - Multiplier: 2x per consecutive failure.
  - Maximum ceiling: 30 minutes (1,800 seconds).
  - Backoff tracking: Stores `ConsecutiveFailures`, `BackoffUntilUtc`, and `LastFailureReason`.
  - While `UtcNow < BackoffUntilUtc`, the connection is in `BankConnectionState.TemporarilyUnavailable`. Subsequent refresh attempts are skipped without sending network traffic, reporting `BackoffActive` with the remaining duration.
  - Any successful contact immediately resets consecutive failures and clears active backoff.

### 3. Permission-Aware Foreground Session Refresh
We introduce `ForegroundSessionRefreshCoordinator`:
- **Foreground Only:** Scheduled and manual background refreshes execute exclusively while the client is running in the foreground with an unlocked profile store. There are no background OS services, cron daemons, or server jobs.
- **Ephemeral Session Credential Ownership:**
  - Credentials are provided via `ISessionCredentialProvider` or registered in-memory for the duration of the unlocked `HostSession`.
  - If a connection lacks an in-memory PIN, unattended refresh skips network requests and marks the connection as `UserActionRequired` ("PIN required for session refresh; unattended dialogs prohibited").
  - If a bank returns an interactive SCA challenge (e.g. `HITAN` with TAN prompt or photoTAN):
    - In **Interactive/Manual** mode: the registered interactive challenge handler is invoked.
    - In **Unattended/Scheduled** mode: the dialogue aborts cleanly (`HKEND`), credentials are wiped, and the connection status transitions to `ChallengeRequired` / `UserActionRequired` without opening modal popup loops.

### 4. Atomic Independent Local Store Commits
We implement atomic, isolated commit coordination:
- Each connection's sync (accounts, balances, transaction observations) is isolated from other connections.
- When connection sync completes, updates are committed to the local `IProfileStore` via an atomic read-modify-write operation (`CommitConnectionSyncResult`).
- If Connection A and Connection B complete around the same time, their store snapshots are serialized so that Connection B applies its changes on top of Connection A's newly generated snapshot, ensuring monotonic generation progression (`Gen N -> Gen N+1 -> Gen N+2`) without race conditions or lost data.
- If Connection A fails, Connection B commits successfully. Neither connection can corrupt or discard the other's records.

## Consequences

### Positive
- Prevents bank session aborts, message counter desynchronization, and account lockouts by strictly serializing operations to the same bank.
- Accelerates multi-institution refresh by executing distinct institutions concurrently.
- Protects bank endpoints and user IP reputation through fail-closed exponential backoff during maintenance.
- Enforces strict user privacy: credentials remain ephemeral and in-memory; no unattended password prompts spam the user.
- Assures crash resilience and data integrity: store commits are independent and atomic per connection.

### Negative / Trade-offs
- Refreshes for multiple logins at the *same* bank will run sequentially rather than concurrently, slightly increasing total duration for users with multiple accounts at one institution. This trade-off is required by FinTS protocol safety.
- Foreground scheduled refresh cannot complete if the user has not entered a PIN in the active session or if an interactive TAN challenge is requested. This is an intentional security design: the client will never guess, automate, or bypass SCA.
