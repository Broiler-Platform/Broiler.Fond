# ADR 0030: Dialogue-end schemas, unsigned encoding and reply evidence

- **Status:** Accepted (credential-free synthetic scope only)
- **Date:** 2026-09-08
- **Related:** [Synchronization context](0029-synchronization-response-context.md),
  [unsigned initialization](0025-unsigned-dialogue-initialization.md)

## Sources and scope

Reviewed the following primary documents from the
[official FinTS specification](https://www.fints.org/de/spezifikation):

- Formals, 2017-10-06, C.4 (printed pages 53–54), C.5.3 (57–58), and
  the dialogue-abort rule on page 37. SHA-256:
  `6b4809acd43acd2c6166c486964dee4b84b488a6a6902b29d1221458eaed7239`.
- Messages – Rückmeldungscodes, 2026-02-03, B.1 (printed page 9), code 0100.
  SHA-256:
  `f0ab2a40c93921a31b715c6006e683ebd9ca7a4d3a842120b8c75d2d535c7b2d`.

HKEND version 1 carries the dialogue ID. The bank acknowledges it with a standard
reply containing 0100 and no data segments; no HIEND schema is introduced.
The current code catalogue also permits institution-initiated closure. This
increment compares only replies to an explicitly supplied HKEND request and
does not process unsolicited termination. A 9800 abort ends a dialogue and must
not trigger another dialogue-end request.

Normal identified dialogue termination requires the applicable signature and
security envelope. The anonymous form demonstrates the plain three-segment
layout. The new unsigned writer is a local codec primitive, not a complete
identified request or authority to send one.

## Request contract

`FinTsDialogueEndRequest` preserves the exact source segment and parses HKEND-1
with one nonbinary scalar dialogue ID and no segment reference. IDs contain
1–30 printable Latin-1 characters, excluding padding, controls and reserved
`0`/`unbekannt` values. Text is neither trimmed nor normalized.

`FinTsUnsignedDialogueEndRequest` restricts the frame to HNHBK-3, HKEND-1 and
HNHBS-1, numbered 1–3. The header and HKEND IDs must match exactly. Populated
outer response references, security wrappers and additional segments fail.
An explicitly empty optional reference remains an omission.

Client and expected bank message numbers are independently supplied, each from
2 through 9999. This lower bound is the local established-dialogue restriction
after initialization message 1, not a general schema rule for all FinTS messages.
The writer accepts the client counter, escapes text in both locations, computes
the exact byte length and validates the finished frame. It allocates no counter.
The caller supplies the expected bank counter when creating comparison context.

## Standard-reply comparison

`FinTsDialogueEndEvidence` retains the exact request and response. Matching
requires the expected bank counter, exact header/reference dialogue, the client
message reference and correct HKEND references on all scoped replies.
Any data or opaque segment, including an apparent HIEND, requires review.
Explicitly parsed PIN/TAN envelopes also remain outside this unsigned scope.

The local vocabulary allows message-level 0010/0020/0100/9800 and scoped
0020/0100. Unknown codes, duplicate codes, element references, nonempty reply
parameters, conflicting status and indeterminate processing require review.
Other errors require review. Empty trailing reply parameters remain omissions.
Exactly one termination code is required across both reply levels; 0020 alone
does not establish closure, and duplicate or conflicting termination observations
remain unresolved.

An otherwise matching 0100 yields `ClosureReported`. An otherwise matching
message-level 9800 yields `AbortReported`, while the generic response retains
its error classification. Both codes remain uninterpreted in the generic reply
vocabulary. Neither outcome authenticates termination or proves a connection
was physically closed. Raw `CloseReported` and `AbortReported` flags preserve
code observations even on mismatched responses; callers must inspect the outcome
and issues before using them as bound evidence.

## Ownership, verification and next work

Comparison is pure and repeatable. It consumes no response, owns no active
session, updates no state, and schedules no close, retry or reinitialization.
Existing bounded syntax/response limits apply, with cancellation during reply
traversal. Fixed diagnostics exclude identifiers; explicitly retained source
objects remain private, untrusted data unsuitable for logging.

Ten independent Python fixtures cover message/scoped closure, escaped text,
independent and maximum counters, aborts, missing termination, conflicting
termination, unexpected data, foreign scope and duplicate codes across levels.
The executable suite adds 142 checks, including malformed schemas, context
isolation, envelope restrictions, source ownership, culture independence,
null handling and cancellation.

Next is a bounded synchronization-and-closing lifecycle that explicitly records
HKEND and consumes its response before reporting the need for fresh
initialization. Authenticated security, recovery application, transport,
durable replay and domain ingestion remain pending. The host stays inert.
