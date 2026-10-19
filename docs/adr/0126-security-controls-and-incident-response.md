# ADR 0126: Security Controls, Zero-Telemetry Invariants, and Memory Credential Hygiene

- **Status:** Accepted
- **Date:** 2026-10-19
- **Related:** [ADR 0004](0004-profile-envelope-v1.md), [ADR 0009](0009-synthetic-workflows-and-diagnostics.md), [ADR 0083](0083-fints-transport-and-capability-activation.md), [SECURITY.md](../../SECURITY.md), [docs/roadmap.md](../roadmap.md#product-decisions-for-the-first-release), [docs/milestone-7.md](../milestone-7.md#m7-03-security-controls-vulnerability-incident-process--in-memory-credential-hygiene)

---

## 1. Context and Problem Statement

Broiler Fond is built upon a fundamental architectural commitment:
*"Purely user-operated client with direct device-to-bank communication. Broiler operates no aggregation, credential relay, account-information, payment-initiation, or financial-data backend. The application has no analytics, tracking, crash-upload, or background-reporting channel."*

For Windows 1.0, this security boundary must be verified by automated tests rather than assumed:
1. **Zero-telemetry assurance:** Binary inspection and static analysis must prove the absence of third-party telemetry, tracking SDKs, automatic diagnostic upload endpoints, and background phone-home mechanisms.
2. **In-memory credential lifecycle:** Banking PINs, TANs, and PBKDF2 master key buffers are highly sensitive. They must never touch local storage, unencrypted paging files, or debug logs, and must be actively wiped using `CryptographicOperations.ZeroMemory` as soon as their operation completes or the profile locks.
3. **Redacted diagnostic export:** When a user explicitly chooses to export diagnostics for support, all personal financial identifiers (IBANs, account numbers, counterparty names, transaction notes, monetary amounts) must be rigorously masked before presentation.
4. **Coordinated vulnerability disclosure:** A defined incident response process, response SLA, and reporting channel must be documented and maintained.

---

## 2. Decision and Architecture

### 2.1 Security Hygiene Verification Engine (`SecurityHygieneValidator`)

The kernel contains `SecurityHygieneValidator` providing automated static and behavioral verification:
- **Zero-Telemetry Assembly Inspection:** Inspects referenced types, modules, and namespaces across `Broiler.Fond.Kernel` and `Broiler.Fond.Host.Windows`. Asserts zero references to known telemetry/tracking frameworks (e.g. Application Insights, Google Analytics, Sentry, Mixpanel, Segment) and zero unauthenticated HTTP/DNS listeners.
- **Direct-to-Bank Network Enforcement:** Verifies that all outgoing HTTP client requests are targeted exclusively to the user-configured, user-confirmed bank FinTS HTTPS endpoints.
- **Ephemeral Credential Memory Hygiene:** Validates that session credential buffers (`FinTsSessionCredential`, `SecureString`, and byte buffers) actively invoke `CryptographicOperations.ZeroMemory` upon `Dispose()` or session lock, leaving only zero-byte spans in memory.
- **Support Diagnostic Masking:** Verifies that `ExportReviewedSupportPreview` sanitizes all sensitive tokens:
  - IBANs are masked to country code and last 4 digits (`DE** **** **** **** **1234`).
  - Counterparty names and remittance texts are replaced with `[REDACTED]`.
  - Monetary values are stripped of balance amounts.

### 2.2 Vulnerability Incident SLA and Disclosure

Documented in `SECURITY.md`:
- **Reporting Channel:** GitHub Private Vulnerability Reporting or encrypted email.
- **Response SLA:**
  - Initial acknowledgement: $\le 48$ hours.
  - Triage and severity assessment: $\le 5$ business days.
  - Fix development and two-human review: $\le 14$ days for Critical/High.
  - Patch release and advisory publication: $\le 30$ days.
- **Two-Human Gate:** Every distributed patch requires signatures from the named release owner and an independent security reviewer.

---

## 3. Consequences

### Positive
- Enforces user privacy and verifiable zero-telemetry behavior.
- Minimizes credential exposure window in volatile memory.
- Provides a clear, accountable vulnerability disclosure protocol for 1.0.

### Negative / Trade-offs
- Zero-telemetry means crash reporting relies entirely on user-initiated manual support export; developers cannot automatically detect in-the-wild issues without user outreach.
