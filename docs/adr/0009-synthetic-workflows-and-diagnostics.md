# ADR 0009: Synthetic read workflows and structured diagnostics

- **Status:** Accepted (in-memory diagnostics and test workflow only)
- **Date:** 2026-09-05
- **Related:** [User-operated boundary](0003-user-operated-boundary.md),
  [account values](0008-account-values-and-exact-money.md)

## Decision

The test project owns a bounded, scriptable read-only workflow simulator. It
consumes typed authentication, manual-challenge continuation and account-read
steps. Script outcomes are synthetic; they do not authenticate credentials or
implement a FinTS wire codec, BPD/UPD negotiation or a bank's SCA procedure.
The simulator has no network, persistence, delay, polling or retry operation.
It is a single-threaded test harness, not an implementation of the shipped
kernel/connector interfaces.

Scripts contain 1–16 immutable steps and at most 4,096 raw bank-text characters
per step. Raw text is deliberately hostile public fixture input and never
becomes an error, diagnostic event or result string. Missing, misordered,
wrong-kind and trailing steps cannot produce a successful read. Authentication
failures end the session without consuming a subsequent read.

A manual challenge belongs to exactly one session by object identity, expires
after a synthetic two-minute interval and permits one continuation attempt.
Foreign challenges leave the current challenge pending; cancellation, timeout,
failed continuation and completion end it. There is no automatic continuation.
The two-minute interval is a test policy, not a FinTS or legal requirement.
PIN/TAN input uses mutable character spans (1–128 characters), cleared in
`finally` on every exit, including rejected calls. This does not clear caller
copies, runtime dumps or immutable strings; only public synthetic credentials
may be used here. No credentials are retained across steps.

Successful reads exercise the existing exact rediscovery and value projection
contracts against two synthetic accounts. Partial reads explicitly omit one
balance and produce a partial total. Failures expose no fabricated account
results and cannot mutate the source fixture. This is no evidence of durable
refresh merging, authenticated source provenance or real-bank compatibility.

## Diagnostic and support-preview contract

The kernel's `LocalDiagnosticBuffer` is opt-in and memory-only, with a default
capacity of 256 and a maximum of 1,024 events. Its synchronized ring retains the
newest events in insertion order and reports a saturating dropped-event count.
Disabling clears events and counters; clearing alone preserves the enabled
setting. Already-created preview strings remain immutable and are not revoked
by clearing the buffer.

Only defined event-code and outcome enums plus an explicit timestamp may enter
the buffer. There is no free-text, identifier, amount, raw response, exception,
credential or arbitrary-object payload API. This is an allowlist, not a general
redactor: future connector code must map errors to these structured outcomes
without forwarding raw exception or bank text to another logging facility.

Preview JSON schema 1 has exactly `schemaVersion`, `droppedEvents` and `events`
at its root. Each event contains `code` and `outcome`; UTC `timestamp` is included
only when explicitly requested. Timestamps are excluded by default because
they disclose activity timing. The returned preview is a detached immutable
string with matching counts; no file is written or transmitted. A future
export flow must show the exact content it will export and require explicit
user action. Creating a preview is not export approval.

## Evidence and remaining work

The BCL test runner checks scenario outcomes, terminal/replay behavior, input
clearing, secret sentinels in errors and previews, fixed JSON fields, retention,
concurrent event recording and exact account results. See the
[scenario inventory](../../tests/Broiler.Fond.Kernel.Tests/Simulation/README.md).

Integration found and corrected missing refresh/offline flags on account rows
without a unique snapshot. These account-level flags now survive missing and
ambiguous values independently of snapshot-specific time warnings. Total
freshness continues to describe included amounts; excluded counts and row
warnings describe omitted accounts.

FinTS framing/segment codecs, protocol conformance fixtures, BPD/UPD handling,
actual SCA qualification, live reads, encrypted diagnostic storage, an export
UI, persistence, connection deletion and purge remain pending. The Windows host
stays inert. Nothing in this ADR grants format/security or release approval.
