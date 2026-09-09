# ADR 0011: Typed FinTS replies and local dialogue correlation

- **Status:** Accepted (response schema and mechanical correlation only)
- **Date:** 2026-09-05
- **Related:** [Byte syntax and framing](0010-fints-byte-syntax-and-framing.md),
  [structured diagnostics](0009-synthetic-workflows-and-diagnostics.md)

## References and interpretation

Reviewed the official [FinTS specification downloads](https://www.fints.org/de/spezifikation):
Formals 3.0, 2017-10-06, B.7.2–B.7.5 and F; and **Rückmeldungscodes**,
2026-02-03, B.1/B.3/B.4. The latter PDF was retrieved on 2026-09-05 with SHA-256
`f0ab2a40c93921a31b715c6006e683ebd9ca7a4d3a842120b8c75d2d535c7b2d`.
The Formals source hash is in ADR 0010. No specification PDF is redistributed.

HIRMG/HIRMS version 2 carry message/segment-scoped replies. Each reply has a
four-digit code, optional element reference, required text and optional
parameters. Codes beginning 0, 3 and 9 identify success, warning and error
classes; this classification alone cannot establish completed execution.
In the implemented vocabulary, 0010 reports receipt, 0020 reports execution,
0030 indicates pending authorization, 3040 indicates more information, and
9000 indicates indeterminate processing. Header message references identify
the client request; bank message numbering is a separate sequence. An initial
reply references the bank-assigned dialogue ID, rather than the client's zero.

## Typed schema boundary

`FinTsResponse.Parse` accepts an already-framed, unwrapped message. It requires
exactly one HIRMG, rejects duplicate HIRMS for one request segment, and checks
their reference rules. Unsupported versions and opaque HNVSK/HNVSD wrappers
produce distinct fixed-message errors. Security processing remains pending.

Replies preserve ordered source fields, exact text bytes, element references
and positional parameters, including empty optional parameters. Limits are
99 replies per segment, 80 text bytes, 7 element-reference bytes, 10 parameters
of 35 bytes each, and a local aggregate bound of 4,096 replies. These bounds
apply after syntax unescaping. Text fields reject binary/control data; the
negotiated character repertoire, element-reference target and code-specific
parameter semantics still need validation by later schema consumers.

Unknown four-digit codes retain their class, or `Unknown` for an unrecognized
class digit. Only the five codes above receive a meaning; all others remain
`Uninterpreted`. No bank text matching, substitution or speculative code map
is used. Unknown body segments remain explicitly available in
`UninterpretedSegments`. Their payloads are not normalized into domain data.

Conflicting success/error combinations, repeated success codes in one group,
and message-level success combined with segment errors are retained and
flagged. This is not a full cross-segment conformance checker. The API exposes
evidence flags and no `IsSuccessful`/`CanRetry` or operation-completion decision.
Raw text stays available for a future deliberately decoded, safe presentation
path. Default object formatting and parse errors exclude it.

## Mechanical correlation boundary

`FinTsDialogueCorrelation` is synchronized and memory-only. A fresh tracker
starts client and bank counters at 1. It records one unwrapped client frame,
checks its number and exact dialogue ID, and refuses a second pending request.
For a response it checks the bank number, explicit client-message reference,
exact dialogue/reference IDs and the existence of each supplied segment
reference in the pending request. Case folding, trimming and numeric ID
normalization are forbidden. An initial zero or `unbekannt` response ID cannot
establish a session; such error evidence remains available to the caller.

A mismatch or conflicting response cannot change the pending frame, counters
or established ID. A match atomically consumes the pending request, advances
both separately tracked sequences and establishes the initial assigned ID.
Replays and concurrent duplicate handling cannot consume it twice. This first
profile handles exactly one bank response per client request; continuation
protocols, resumed dialogues and other exchange shapes are not implemented.

`Matched` means mechanical correspondence only. It applies equally to a
correlated error, unknown code or indeterminate result, whose typed evidence
remains available. `Ready` means the tracker has room for a next local
expectation, not that the bank has authorized or will accept another operation.
Higher layers must interpret errors, dialogue termination, capabilities and
security evidence before sending anything. This class has no transport.

`Stop` ends the tracker for explicit local cancellation, timeout or abandonment.
It releases held references and never retries. Reaching counter 9999 also
stops it without wraparound. There is no reset, durable replay ledger or
cross-instance replay protection. Parsed frame copies are not zeroizable
credential storage; real secrets must not be introduced through this seam.

## Evidence and remaining work

Five independently generated public response fixtures and 177 checks cover
typed values, unknown/contradictory codes, malformed schemas, exact bounds,
reference mismatch, replay, concurrent publication/consumption, cancellation
and all 9,999 in-memory exchanges through exhaustion. The preexisting syntax,
workflow, domain and storage-candidate suites remain enabled in full CI.

Next are bounded BPD/UPD schemas and parameter/capability evidence. Signature
and PIN/TAN security profiles, live dialogue, authenticated ingestion, complete
code-specific reactions, persistence, UI and SCA qualification remain pending.
The host remains inert; this decision enables no live bank or payment operation.
