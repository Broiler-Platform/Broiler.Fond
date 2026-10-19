# Known-incompatibility ledger

This ledger records all evaluated institution endpoints, protocol variants, and configurations that
have failed, exhibited unsafe behavior, or remain unsupported. In Broiler Fond, failed or unsupported
combinations are never silently hidden from documentation; they are recorded here with date-stamped
evidence and technical root causes.

## Principles

1. **Pass-only support table:** The primary verified matrix in [README.md](README.md) contains *only*
   rows that have passed all required automated and live acceptance tests.
2. **Permanent failure tracking:** Combinations that fail or are rejected must be logged in this ledger
   and cannot be omitted or erased.
3. **Transparent root causes:** Every entry records the technical rationale (e.g. protocol specification
   violation, broken SCA challenge format, lack of standard HTTPS TLS, or legacy protocol deprecation).
4. **Local workaround over remote control:** Where an institution quirk can be safely mitigated without
   violating security invariants, a versioned connector compatibility profile is provided. The user can
   inspect and override workarounds locally; no remote server pushes dynamic changes.

---

## Incompatibility Ledger

| Institution / Endpoint | BLZ / BIC | Connector / Version | Observed Symptom | Technical Root Cause | Affected Builds | Workaround / Disposition | Owner | Last Test Date | Status |
| --- | --- | --- | --- | --- | --- | --- | --- | --- | --- |
| Legacy HBCI 2.x Endpoints | Various | HBCI 2.2 / FinTS 2.x | Unsupported protocol framing (`HNHBK:1:2` / `HNHBK:1:1`) | Protocol obsolete; editorial maintenance ended by DK in 2016. Lacks modern SCA dynamic linking. | All versions | Use bank's modern FinTS 3.0 HTTPS endpoint. | Core Team | 2026-10-18 | Permanently Unsupported |
| Plain HTTP / Insecure TLS Endpoints | Various | FinTS 3.0 (unencrypted / self-signed) | TLS handshake failure or rejected URI scheme | Cleartext HTTP or untrusted TLS certificates violate security and privacy invariants. | All versions | Strict HTTPS with valid platform certificate chain required. No insecure bypass allowed. | Security Review | 2026-10-18 | Permanently Rejected |
| Proprietary Web Scrapers / Unofficial APIs | Various | Web / Private JSON APIs | HTML layout breakage, 2FA prompt loops | Fragile, violates purely user-operated direct protocol boundary, requires credential relay or browser scraping. | All versions | Strictly out of scope; Broiler Fond uses only standard FinTS 3.0. | Architecture Review | 2026-10-18 | Permanently Excluded |
| Outdated DKB HBCI Endpoint (`banking.dkb.de/dkb/-`) | `12030000` / `BYLADEM1001` | FinTS 3.0 PIN/TAN | HTTP 404 / 410 Gone | Old legacy endpoint retired by DKB in favor of modernized FinTS 3.0 PIN/TAN cluster endpoint. | All versions | Use current endpoint `https://banking.dkb.de/fints` with verified PIN/TAN. | Connector Team | 2026-10-18 | Superseded Endpoint |
| Postbank legacy non-SEPA statement endpoints | `37010050` / `PBNKDEFFXXX` | FinTS 3.0 `HKKAZ` v5 | Malformed MT940 line wraps breaking SWIFT field `:86:` | Bank server transmits non-standard line-folding in proprietary Swift MT940 segment without standard CRLF delimiters. | Beta builds | Pin `HKKAZ` to version 6/7 or prefer ISO 20022 CAMT `HKCAZ` via compatibility profile `fints-postbank`. | Connector Team | 2026-10-18 | Mitigated via Profile |
| Sparkasse Finanz Informatik (buggy HKTAN 7 advertising) | Various (`10050000`, etc.) | FinTS 3.0 `HKTAN` v7 | Server returns `9050 Teilweise fehlerhaft` on challenge response | Bank core advertises HKTAN version 7 in BPD but fails to parse process variant 2 payloads properly on certain regional servers. | Beta builds | Pin HKTAN to version 6 (`PinnedHktanVersion = 6`) in compatibility profile `fints-sparkasse`. | Connector Team | 2026-10-18 | Mitigated via Profile |
| ING Germany (Single-dialogue concurrency lock) | `50010517` / `INGDDEFFXXX` | FinTS 3.0 PIN/TAN | Dialogue abort with `9930` or rate limit when two parallel sessions open | Bank strictly forbids multiple concurrent authenticated dialogues for the same user login. | Beta builds | Strictly enforced via `InstitutionConcurrencyCoordinator` (per-institution queue). | Concurrency Team | 2026-10-18 | Mitigated via Coordinator |
| FinTS 4.0 Pre-release Draft Endpoints | Various | FinTS 4.0 (Draft) | Unsupported protocol version exception | Unapproved pre-release draft with non-standard XML namespace, superseded by official DK FinTS 4.1 standard. | All versions | Fail-closed via `FinTsProtocolNegotiator`; configure bank's official FinTS 4.1 or FinTS 3.0 endpoint. | Protocol Team | 2026-10-19 | Permanently Unsupported |
| Non-Standard FinTS 4.1 Core (Legacy MT940 in XML) | Various | FinTS 4.1 non-compliant | XML schema validation failure on statement queries | Bank advertises FinTS 4.1 but encapsulates legacy flat MT940 text inside `<HICAZ>` without required ISO 20022 CAMT schemas. | All versions | Reject malformed XML; fall back cleanly to FinTS 3.0 via `FinTsProtocolNegotiator` preference `ForceFinTs30`. | Protocol Team | 2026-10-19 | Incompatible XML Implementation |

---

## Review and Maintenance Policy

1. Every newly attempted institution combination must be recorded here if it fails to pass live verification.
2. An entry can only transition to "Mitigated via Profile" if an automated test verifies the workaround in `ConnectorCompatibilityTests`.
3. Incompatibility entries must never be deleted; if a bank resolves a known bug in a subsequent firmware/core release, the entry status is updated to `Resolved in Bank Firmware <date>` while preserving the historical record.
