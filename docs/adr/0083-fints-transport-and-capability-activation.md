# ADR 0083: Bounded FinTS HTTPS Transport, Parameter/Capability Activation, and Network Boundary

- **Status:** Accepted (pure fail-closed capability activation and secret-safe bounded HTTPS transport)
- **Date:** 2026-10-15
- **Related:** [Kernel boundary](0001-kernel-boundary.md),
  [manual institution setup](0001-kernel-boundary.md),
  [parameter evidence](0012-fints-parameter-evidence.md),
  [read capability evidence](0013-read-capability-evidence.md),
  [PIN/TAN envelopes](0014-pin-tan-envelope-and-parameters.md),
  [synchronization recovery application](0082-synchronization-recovery-application.md)

## Context and architectural requirement

Milestone 1 (specifically M1-05 FinTS read-only slice) requires connecting the pure in-memory protocol codecs
and dialogue coordinators to:
1. Canonical domain account capabilities advertised by financial institutions (`AccountCapability`).
2. Parameter projection from BPD/UPD evidence into verified active account capabilities (`FinTsCapabilityActivation`).
3. PIN/TAN security profile specifications and header/envelope verification (`FinTsSecurityProfile`).
4. An asynchronous, bounded, secret-safe HTTPS transport boundary (`IFinTsTransport`, `FinTsHttpsTransport`).

Prior to this increment:
- The canonical domain entity `AccountCapability` (specified in `roadmap.md:388`) was not yet defined in the kernel.
- No activation engine existed to translate parsed BPD read advertisements (`HISALS`, `HISPAS`) and UPD account permissions (`HIUPD`) into active `AccountCapability` instances for a `BankConnection`.
- No concrete network transport abstraction existed for transmitting FinTS message frames over HTTPS to bank endpoints.
- Error diagnostics lacked structured transport failure categorization (`FinTsTransportIssue`) with a formal secret-safe guarantee ensuring raw request/response bytes containing credentials (PIN/TAN) or financial records are never leaked into exceptions, inner exceptions, or logs.

This increment fulfills these architectural requirements on the shared, unforked `net10.0` kernel using standard BCL APIs exclusively.

## Domain capability model and parameter activation

`Broiler.Fond.Kernel.Domain.Accounts` and `Broiler.Fond.Kernel.FinTs` introduce:

1. **`AccountCapability`**:
   - Immutable representation of an operation and protocol version currently advertised for a specific bank connection and optional account:
     - `ConnectionId`: Nonzero connection identity.
     - `AccountId?`: Stable local account identity if bound to a specific account, or `null` for connection-level / unlinked capabilities (e.g., `HKSYN`, `HKEND`).
     - `OperationCode`: Standard 4 to 6 character uppercase ASCII FinTS business operation code (e.g., `"HKSAL"`, `"HKSPA"`, `"HKSYN"`, `"HKEND"`).
     - `Version`: Protocol version (1..999).
     - `MinimumSignatures`: Lower bound on required signatures (0..9).
     - `MaximumOrders`: Maximum orders permitted per request (0..999).
     - `Status`: Operational status (`Active`, `Suspended`, `Deprecated`).
     - `ActivatedAt`: Monotonic evaluation timestamp.
   - Pure Immutability: `WithStatus(newStatus)` returns a new instance; existing instances remain completely unmodified.
   - Value Equality: Implements `IEquatable<AccountCapability>`, comparing all canonical properties.

2. **`FinTsSecurityProfile`**:
   - Represents the negotiated FinTS 3.0 PIN/TAN security profile:
     - `ProfileVersion`: 1 (one-step/SHA-1) or 2 (two-step/SHA-256).
     - `SecurityFunction`: 3-digit numeric procedure code (e.g., `"999"` for single-step, `"900"`..`"997"` for two-step SCA procedures).
     - `SupplierRole`: 1 (bank client / Kunde) or 2 (bank).
     - `HashAlgorithmCode`: Standardized PIN/TAN algorithm identifier (`"999"` for owner verification).
   - Validates `FinTsPinTanSignatureHeader` (HNSHK) and `FinTsPinTanEnvelope` (HNVSK) against the negotiated profile.

3. **`FinTsCapabilityActivation`**:
   - Pure, fail-closed evaluation projecting parsed `FinTsReadParameterSet` (BPD) and `FinTsAccountParameters` (UPD) into active `AccountCapability` instances for a `BankConnection`:
     - Rejects disabled connections (`RejectedDisabledConnection`).
     - Rejects unconfigured or unsynchronized connections (`RejectedUnsynchronizedConnection`).
     - Rejects missing HIBPA or missing protocol 300 advertisement (`RejectedMissingParameters`).
     - Rejects institution bank code mismatches against connection configuration (`RejectedInstitutionMismatch`).
     - Activates connection-level capabilities (`HKSYN` v3, `HKEND` v1).
     - Evaluates account-level operations (`HKSAL` v6, 7, 8; `HKSPA` v1, 2, 3):
       - Permissions listed as allowed and matching signature/order limits receive `Active` status.
       - Zero order capacity, permission blocked (`UnlistedBlocked`), signature conflicts, or missing IBAN (v7/8) receive `Suspended` status with human-readable diagnostic issues.
       - Reconciles account bindings using an optional `accountResolver` delegate.

## Bounded FinTS HTTPS transport and secret-safe network boundary

`Broiler.Fond.Kernel.FinTs.Transport` introduces:

1. **`IFinTsTransport`**:
   - Asynchronous contract for sending FinTS message payloads over HTTPS:
     `ValueTask<FinTsTransportResponse> SendAsync(Uri endpoint, ReadOnlyMemory<byte> requestPayload, CancellationToken cancellationToken = default);`

2. **`FinTsHttpsTransport`**:
   - Pure BCL implementation based on `HttpClient` and `HttpMessageHandler`.
   - **HTTPS Enforcement**: Strictly enforces absolute `https` scheme, valid host, no user information (`user:pass@`), no fragments (`#`), and endpoint length bounded to 2,048 characters. Insecure schemes (HTTP, FTP, file) and malformed endpoints fail closed with `FinTsTransportIssue.InvalidEndpoint`.
   - **Payload Bounds**:
     - Request limit: Configurable maximum request body (default 128 KiB). Empty requests fail with `EmptyPayload`; oversized requests fail with `PayloadTooLarge`.
     - Response limit: Bounded stream reader enforcing maximum response body (default 1 MiB). Early rejection via advertised `Content-Length` and streaming cutoff to prevent memory exhaustion or zip/decompression bombs.
   - **Timeouts**: Enforces monotonic request timeout (default 30 seconds). Timed-out requests map to `FinTsTransportIssue.Timeout`.
   - **Error Mapping**: Maps transport failures to typed `FinTsTransportIssue` categories: `InvalidEndpoint`, `PayloadTooLarge`, `EmptyPayload`, `Timeout`, `TlsFailure`, `HttpError`, `NetworkFailure`, `Cancelled`.

3. **Secret-Safe Diagnostic Invariant**:
   - Transport exception messages, string representations, and properties **never** echo raw request payload or response payload bytes. Because request payloads contain customer PIN/TAN values in `HNSHA` trailers and response payloads contain financial account data, raw bytes are strictly excluded from exception messages, diagnostic formatting, and crash representations.
   - Endpoints in exceptions retain only safe IDN host and path components; embedded credentials or queries are excluded.

4. **`FinTsSimulatedTransport`**:
   - In-memory simulation of `IFinTsTransport` for deterministic testing of FinTS dialogues and failure injection (TLS failures, timeouts, HTTP errors) without touching real network sockets.

## Verification

49 executable checks in `FinTsTransportAndActivationTests.cs` verify:
- `AccountCapability` factory boundaries (zero IDs, empty/whitespace/lowercase operation codes, versions < 1 or > 999, negative/excessive signatures or orders, undefined statuses).
- Pure immutability: `WithStatus` returns new instances; identical instances satisfy value equality.
- `FinTsSecurityProfile` validation boundaries (profile versions, function ranges, supplier roles) and verification against actual synthetic `HNSHK` headers and `HNVSK` envelopes.
- `FinTsCapabilityActivation` fail-closed rejections (disabled connections, unsynchronized connections, missing HIBPA, missing protocol 300, bank code mismatch).
- Clean activation of BPD/UPD parameters into `HKSYN`, `HKEND`, `HKSAL`, and `HKSPA` capabilities.
- Suspension handling for zero order capacity, unlisted blocked permissions, signature conflicts, single account restriction, and missing IBAN.
- 16-thread concurrency atomicity producing identical, atomic results across parallel activations.
- `FinTsHttpsTransport` boundary validation: rejection of HTTP, FTP, userinfo, empty payloads, and oversized request bodies.
- Clean HTTP POST execution verifying HTTP method, Content-Type (`text/plain`), URI, and byte preservation.
- Non-200 HTTP status code mapping, advertised Content-Length violation rejection, streaming cutoff rejection, timeout mapping, TLS handshake failure mapping, network error mapping, and caller cancellation.
- Sentinel-secret checks verifying that `PinSentinel` and `TanSentinel` are never echoed in any transport exception message or `ToString()`.
- `FinTsSimulatedTransport` queueing, failure injection, and transmission recording.

Zero third-party packages or runtime dependencies; platform-neutral standard BCL net10.0 only.
Credentials remain strictly transient and unpersisted.
