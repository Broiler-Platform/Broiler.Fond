# Institution compatibility

Compatibility claims are evidence-backed and intentionally simple. A row is
published only for the exact combination that passed the applicable fixture,
controlled-account, operating-system, and human-review gates.

## Verified support matrix

**Milestone 0 supports no live institutions and performs no FinTS/HBCI
communication.** The empty table is intentional; it must not be interpreted as
general compatibility.

| Institution / manual endpoint label | Endpoint source | Protocol version | SCA/TAN procedure | Account type | Proven operations | Host | Evidence date | Status |
| --- | --- | --- | --- | --- | --- | --- | --- | --- |
| _No verified rows_ | — | — | — | — | — | — | — | Unsupported at M0 |

The first row is a Milestone 1 deliverable and uses a manually configured
endpoint. Marketing, UI text, and release notes must not broaden a row beyond
its recorded protocol, procedure, account type, operations, and host.

## Admission and maintenance rules

- Record only behavior observed against the exact row; do not infer support
  from institution branding or protocol advertising.
- Preserve dated evidence and the tested application/protocol configuration.
- Re-run the row's corpus before every candidate that claims it.
- Remove or mark a row when evidence expires or a regression is unresolved.
- Never use production credentials or financial payloads as repository or CI
  fixtures.

## Known-incompatibility ledger

Known failures are recorded separately so the pass-only support table cannot
hide them. An empty ledger at Milestone 0 means “not tested,” not “no known
problems.”

| Institution / endpoint label | Protocol or procedure | Observed symptom | Evidence date | Workaround | Status |
| --- | --- | --- | --- | --- | --- |
| _No observations — no connector exists at M0_ | — | — | — | — | Not tested |

