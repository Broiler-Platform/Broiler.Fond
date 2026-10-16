# Institution compatibility

Compatibility claims are evidence-backed and intentionally simple. A row is
published only for the exact combination that passed the applicable fixture,
controlled-account, operating-system, and human-review gates.

## Verified support matrix

Milestone 1 development implements the complete FinTS 3.0 PIN/TAN read and
synchronization protocol stack, SCA challenge continuation, account rediscovery,
and Windows presentation layer. The support matrix defines the target row for
the controlled live acceptance gate (`M1-09`).

| Institution / manual endpoint label | Endpoint source | Protocol version | SCA/TAN procedure | Account type | Proven operations | Host | Evidence date | Status |
| --- | --- | --- | --- | --- | --- | --- | --- | --- |
| Retail Banking candidate (FinTS 3.0 PIN/TAN) | Manual entry copied from authenticated online banking portal; confirmed HTTPS hostname | FinTS 3.0 PIN/TAN | HKTAN v6/v7 challenge continuation (decoupled app approval or chipTAN) | Payment / current account (Girokonto, SEPA) | Dialogue init (`HKIDN`, `HKVVM`), BPD/UPD (`HIBPA`, `HIUPA`, `HIUPD`), Account discovery (`HKSPA`), Balances (`HKSAL`), SCA status (`HKTAN`), Closing (`HKEND`) | Windows 10/11 x64 (`net10.0-windows`) | 2026-10-16 | Candidate (pending live acceptance run) |

Marketing, UI text, and release notes must not broaden a row beyond its recorded
protocol, procedure, account type, operations, and host.

## Admission and maintenance rules

- **Evidence-backed admission:** Record only behavior observed against the exact row;
  do not infer support from institution branding or protocol advertising.
- **Controlled live acceptance (M1-09):** The target row must be validated using a real,
  controlled bank account and live bank endpoint under normal bank operating conditions.
- **5-minute time-to-first-balance:** On a clean install, a test user must be able to
  reach the first truthful balance within five minutes.
- **Zero repository secrets:** Never place production credentials, banking PINs, TANs,
  account numbers, or financial payloads in source control, git history, or CI logs.
- **Ephemeral zeroization:** Verify that volatile credential buffers (PINs/TANs) are
  wiped with `CryptographicOperations.ZeroMemory` immediately upon use or cancellation.
- **Preserve dated evidence:** Preserve dated evidence and the tested application/protocol
  configuration in the release record.
- **Regression retesting:** Re-run the row's test corpus before every candidate that claims it.
- **Prompt removal:** Remove or mark a row when evidence expires or a regression is unresolved.

## M1-09 Live bank acceptance procedure

1. **Clean environment setup:** Launch a clean build of `Broiler.Fond.Host.Windows` on a
   standard Windows 10/11 test machine with no pre-existing profile directory.
2. **Profile creation:** Initialize a new profile with a strong test passphrase; verify
   PBKDF2 calibration within the supported floor (`600,000..5,000,000` iterations).
3. **Institution entry:** Manually enter the institution name, bank identifier (BLZ),
   and bank-issued HTTPS FinTS endpoint URL. Verify that the endpoint confirmation dialog
   accurately presents the host and displays residual phishing warnings.
4. **Dialogue initialization & SCA:** Enter user banking credentials upon prompt. If the
   bank requests strong customer authentication, verify that the challenge dialog presents
   the challenge text/matrix or decoupled approval request, and correctly completes continuation.
5. **Account discovery & balance display:** Verify that discovered payment accounts appear
   in the accounts list with exact ISO 4217 currency, truthful freshness badges, and no
   floating-point rounding or mixed-currency aggregation.
6. **Disconnect and privacy purge:** Remove the connection and authorize privacy purge;
   verify that cached accounts and generations are cryptographically overwritten on disk.

## Known-incompatibility ledger

Known failures and protocol anomalies are recorded separately so the pass-only support
table cannot hide them.

| Institution / endpoint label | Protocol or procedure | Observed symptom | Evidence date | Workaround | Status |
| --- | --- | --- | --- | --- | --- |
| Legacy HBCI 2.x endpoints | HBCI 2.2 / FinTS 2.x | Unsupported protocol version | 2026-10-16 | Use institution's FinTS 3.0 HTTPS endpoint | Permanently unsupported |
| Non-HTTPS / self-signed endpoints | Plain HTTP or untrusted TLS | Certificate or scheme validation failure | 2026-10-16 | Standard HTTPS with valid certificate required | Permanently rejected |
| Ambiguous currency mutation | FinTS 3.0 `HISAL` / `HISPA` | Discovered account reports conflicting currency | 2026-10-16 | Account placed in `Quarantined`; user can relink via dialog | Handled via quarantine |
