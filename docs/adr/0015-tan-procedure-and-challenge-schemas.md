# ADR 0015: TAN procedure and challenge schemas

- **Status:** Accepted (bounded untrusted schema evidence only)
- **Date:** 2026-09-06
- **Related:** [PIN/TAN envelope](0014-pin-tan-envelope-and-parameters.md),
  [parameter evidence](0012-fints-parameter-evidence.md),
  [response correlation](0011-fints-responses-and-dialogue-correlation.md)

## Source and scope

Reviewed **Security – Sicherheitsverfahren PIN/TAN**, 2020-07-10, from the
official [FinTS specification](https://www.fints.org/de/spezifikation).
The download hash is recorded in ADR 0014. Relevant sections are B.5.1–B.5.2
(HITANS/HITAN versions 6/7), parameter data-dictionary versions 6/7,
procedure data-dictionary versions 6/7, and challenge, expiry, input-format,
hash-method, DK-method and time/dialogue-scope definitions.

`FinTsTanParameterSet` recognizes HITANS versions 6 and 7.
`FinTsTanChallengeSet` recognizes HITAN versions 6 and 7. Other versions stay
opaque with their original source. No parser chooses a highest version or
falls back from an unsupported version. These are schema readers, not a TAN
implementation, supported-bank declaration or SCA state machine.

## Procedure parameters

HITANS preserves common order/signature constraints and the uninterpreted
PIN/TAN security-class filler. Its parameter group carries reported one-step
permission, permission for multiple TAN-requiring orders per message and the
order-hash method. The hash code is validated as 0/1/2; no hash is computed.

Each procedure retains its source segment/version and ordered raw components.
Typed accessors cover the security function (900–997), process variant 1/2,
technical identity, method/version/name, TAN input constraints, return-value
label and bound, multiple-TAN/cancellation flags, time/dialogue scope, account
requirements, challenge-class and structure flags, initialization mode,
medium-name/HHD-response requirements and optional active-media count.
Procedure variant 1 requires the not-applicable time/dialogue-scope code 4.
This parser does not interpret procedure identifiers as executable plugins.

Version 7 recognizes the exact method identifiers `Decoupled` and
`DecoupledPush` for conditional field occupancy. Both omit TAN-input length
and format. `Decoupled` requires maximum status-query count and initial/next
query delays, with optional manual-confirmation and automatic-query flags.
Other methods, including `DecoupledPush`, cannot populate those polling fields.
Absent values remain null; zero and N remain distinct observations. No timer,
polling loop, push connection, confirmation or TAN prompt is created.

The nested repeated groups use 21 positional components in version 6 and 26
in version 7. Optional suffix fields may be omitted from the final group;
preceding groups require placeholders so subsequent groups remain positional.
An incomplete or shifted supported layout fails rather than searching for a
plausible next security function. The protocol allows up to 98 procedures per
advertisement. The existing 256-component local syntax budget permits at most
12 version-6 or 9 version-7 procedures; it is not widened in this increment.
Additional bounds are 128 HITANS occurrences (including unknown versions) and
1,024 understood procedures per parameter set.

Duplicate advertisement versions and duplicate security functions within an
advertisement remain separate, with explicit duplicate indicators. There is
no preferred candidate, permitted-procedure list or enabled operation result.
Method-specific standards such as HHD, required standard versions, user
permission from reply 3920, complete/fresh BPD, selected dialogue procedure
and cross-parameter consistency still need further validation.

## Challenge evidence

HITAN requires a request-segment reference and a supported process code.
Version 6 accepts 1/2/3/4; version 7 additionally accepts S. Schema validation
checks conditional reference and challenge-text presence. A populated order
hash is permitted only for process 1 and is binary, up to 256 bytes. Hash
necessity and exact mirroring depend on the selected procedure and request;
the structural parser cannot establish either.

The order reference is at most 35 bytes, challenge text at most 2,048 decoded
bytes, and medium name at most 32 bytes. Optional HHD content remains opaque
binary under a local 64 KiB limit. Its internal format is not decoded,
rendered, fetched or executed. An absent binary field uses an empty text
position; a populated binary field must be nonempty.

Optional expiry is a valid calendar date and time together, or an omitted/
empty group. It remains raw local date/time without a timezone; parsing it
does not establish freshness or expiry against the local clock. Whether a
medium name is required needs the selected procedure's active-media context.

Challenge text, including markup and escaped FinTS syntax, is retained exactly.
It is never treated as HTML. A future display layer must implement the allowed
formatting rules explicitly and preserve visible challenge content. Binary
challenge decoding, format negotiation and device integration remain pending.

The exact reference placeholder `noref` has an observation property only.
Neither it nor any challenge-text filler establishes an SCA exemption,
authorization, completion or a successful transaction. Reply meanings and
request/dialogue binding need a separate context validator.

At most 32 HITAN occurrences, including unknown versions, are accepted per
response. Duplicate request references remain visible, including collisions
with opaque versions. No challenge is selected or merged. The response source
retains original wire and, where present, its parsed PIN/TAN envelope.
Parsing makes no domain-account, endpoint, cache, credential or transport change.

## Verification and next work

Six independently generated public fixtures and 249 checks cover both schema
versions, process codes, decoupled/push distinctions, hash and opaque binary
data, exact text, dummy references, duplicates, unknown versions, conditional
fields, local boundaries, cancellation and fixed diagnostics. The existing
Python producer reproduces the embedded JSON; normal CI needs only .NET.
See the [fixture inventory](../../tests/Broiler.Fond.Kernel.Tests/Fixtures/FinTs/README.md).

Next are permitted-procedure evidence from reply 3920 and request-bound
challenge validation, including procedure/version, order hash/reference,
medium requirements and ambiguous/dummy outcomes, before SCA continuation.
Credential handling, live transport, authenticated ingestion and payments
remain unimplemented.
