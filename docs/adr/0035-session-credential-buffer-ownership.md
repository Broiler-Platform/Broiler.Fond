# ADR 0035: Session-only credential buffer ownership and cleanup

- **Status:** Accepted (local buffer primitive; synthetic verification only)
- **Date:** 2026-09-08
- **Related:** [Signature-header encoding](0034-pin-tan-signature-header-encoding.md),
  [synthetic workflow cleanup](0009-synthetic-workflows-and-diagnostics.md)

## Decision and ownership transfer

`FinTsSessionCredential` owns one bounded opaque byte value, explicitly marked
Pin or Tan. It is a local ownership primitive, not an authenticated banking
session or a credential-entry path. No institution, user, dialogue or challenge
is inferred or attached, and no bank credential is requested or used by the host.

`CaptureAndClear` accepts a mutable byte span that the caller exclusively owns
during the call. It copies into a separate private array and clears the entire
supplied span on every exit, including malformed input, invalid policy, clock
failure and cancellation. Only the supplied slice is cleared, not adjacent
memory. Passing this API means relinquishing those input bytes even on failure.

The local length bound is 1–99 bytes, independently of bank-advertised limits.
All octets are preserved, including zero and high bytes. This layer performs no
character encoding, PIN format, TAN format, bank policy or protocol validation.
It accepts no immutable string and provides no string conversion, serialization
payload or direct reference to its owned storage.

The private array is allocated pinned from the outset through
[`GC.AllocateArray<byte>`](https://learn.microsoft.com/en-us/dotnet/api/system.gc.allocatearray?view=net-10.0).
This avoids relocation of this particular managed array; no pool or uninitialized
allocation is used. Terminal cleanup overwrites the full array with
[`CryptographicOperations.ZeroMemory`](https://learn.microsoft.com/en-us/dotnet/api/system.security.cryptography.cryptographicoperations.zeromemory?view=net-10.0)
before releasing its reference. The latter API is intended to preserve clearing
against future optimizer changes.

## Access and one-time TAN consumption

`TryCopyTo` accepts an exclusively caller-owned destination span and returns a
copy result plus the number of bytes written. The public snapshot exposes only
kind, state, successful-copy count and remaining duration, never length or bytes.

| Condition | Copy result and state |
| --- | --- |
| Available PIN, sufficient destination | Copied; PIN remains Available |
| Available TAN, sufficient destination | Copied; owned bytes are zeroed immediately and state becomes Consumed |
| Destination too small | DestinationTooSmall; no bytes written or consumption |
| Terminal owner | Unavailable; zero bytes written |
| Deadline/clock failure during copy | Unavailable; copied destination prefix and owned bytes cleared |
| Cancellation during copy | OperationCanceledException; copied prefix, if any, and owned bytes cleared |

Unwritten destination bytes are untouched. A rejected call before copying leaves
the destination unchanged. A deadline or cancellation observed after the internal
copy clears that copied prefix before returning or throwing, reports zero written
bytes and does not increment the successful-copy count.

The caller owns all successfully copied bytes and must clear them promptly,
including on downstream failure. Disposing this object cannot revoke an earlier
copy. TAN consumption means exactly one successful copy-out from this owner;
it does not prove bank submission, prevent reuse of a caller copy or reject
re-capture of the same value in a different owner. PIN availability is local
storage policy and schedules no retry or repeated bank operation.

Every instance operation is serialized by one lock. Concurrent TAN copy requests
produce at most one success. Cancellation and copy-out have a definite order;
if copy wins, the caller already owns that copy. Terminal states are immutable.

## Lifetime and cleanup

The caller supplies a positive access lifetime capped at 15 minutes. It starts
at capture using monotonic `TimeProvider` timestamps. Successful PIN copies,
undersized destinations and snapshots never renew it. Deadline equality expires
the owner. Regression, invalid elapsed time or runtime clock failure terminates
with ClockInvalid and clears bytes. Clock failure text is not propagated.

An expiry observed during capture returns an already-expired owner without
retained bytes. Cancellation during capture throws after disposing any unpublished
owner and clearing the input. Invalid clock configuration fails before copying.

`Cancel` and `Dispose` explicitly clear owned bytes. Dispose never calls the
supplied clock and is idempotent. A finalizer provides fallback clearing for an
abandoned owner; it invokes no caller code and is not a substitute for disposal.

**Expiry is observed on API calls.** There is no timer or background task, so an
idle referenced owner may retain bytes beyond its access deadline until the next
call or explicit disposal. Future session/lock/disconnect integration must dispose
its owners promptly. Scope names and access deadlines alone do not ensure timely
cleanup of abandoned application state.

Pinning and zeroization do not encrypt memory or protect it against process
inspection, swap, crash dumps, termination, arbitrary caller copies or all runtime/
hardware remnants. Finalization is not guaranteed at process shutdown. This
increment makes no complete-memory-erasure or live-security qualification claim.

## Diagnostics and verification

The owner exposes no public value/length property. Default diagnostics and scalar
snapshots exclude source bytes. Errors contain fixed descriptions and parameter
names; arbitrary clock exception text and inner exceptions are not forwarded.
No log, diagnostic event, file, clipboard or network destination is added.

Ten independent Python traces cover reusable PIN copies, one-time TAN consumption,
undersized destinations, cancellation, exact expiry, nonrenewal, clock regression,
disposal before use and opaque bytes at the local limit. Every value is a public
synthetic sentinel or deterministic byte sequence.

The executable suite adds 136 checks. Tests hold a reference to the private test
array to verify actual zeros before reference release, not just the absence of a
field. They also cover input clearing on all failure paths, span boundaries,
unpublished-owner cancellation, post-copy expiry/cancellation, clock failures,
output independence, concurrent PIN/TAN copying, cancellation races and fallback
finalization. Fake clocks require no sleeps or bank operations.

## Next work

Next is credential-aware HNSHA signature-trailer encoding using explicitly owned
buffers and bounded caller output. Character/protocol validation, challenge/request
binding, session cleanup integration, complete request security, authenticated
sessions, transport and secure UI input remain pending. The host stays inert;
this primitive is exercised only with synthetic values.
