# ADR 0141: EBICS Protocol Architecture and Order Types

- **Status:** Accepted
- **Date:** 2026-10-19
- **Related:** [ADR 0140](0140-professional-roles-and-four-eyes-approval-workflow.md), [docs/roadmap.md](../roadmap.md#separate-professional-track), [docs/professional-track.md](../professional-track.md#p-02-ebics-protocol-architecture-order-types--security-contracts)

---

## 1. Context and Problem Statement

Corporate banking and treasury operations in European institutions rely heavily on the Electronic Banking Internet Communication Standard (EBICS, French/German/Swiss standard overseen by EBICS SCRL). Unlike consumer FinTS PIN/TAN protocols that rely on reactive human SCA dialogues, EBICS is designed for high-volume, automated, or managerial multi-signature corporate payment transactions.

Key requirements and constraints:
1. **Direct Device-to-Bank Communication:** The client must communicate directly with bank EBICS endpoints over HTTPS/TLS; no cloud proxy, intermediary aggregation service, or Broiler-operated relay is permitted.
2. **Strict BCL-Only Cryptography:** EBICS signature generation, public key encoding, and envelope digest computation must rely exclusively on .NET 10 runtime cryptographic APIs (`RSA`, `ECDsa`, `SHA256`, `AesGcm`). No unmanaged libraries or third-party NuGet packages.
3. **Protocol Multi-Versioning:** Support for both established EBICS 2.5 (`H004`) and modern EBICS 3.0 (`H005`) XML schemas.
4. **Standard Order Types:** Must support cryptographic bank initialization (`INI`, `HIA`, `HPB`), cash management statements (`STA`, `VMK`), and SEPA money movement (`CCT`, `CDD`).

---

## 2. Decision and Architecture

### 2.1 EBICS Versioning and Connection Topology

We introduce `EbicsVersion`:
- `H004`: EBICS 2.5 protocol schema.
- `H005`: EBICS 3.0 protocol schema with modernized BTF (Business Transaction Format) mapping.

Host configuration descriptor `EbicsHostConfig`:
- `HostId`: Bank host identifier (e.g. `EBICS01`).
- `PartnerId`: Corporate customer identifier (e.g. `CUST1002`).
- `UserId`: Individual user identifier within the organization (e.g. `OP001`).
- `ServerUrl`: Bank HTTPS endpoint URL.
- `Version`: `EbicsVersion` (`H004` or `H005`).

### 2.2 Standard Banking Order Types (`EbicsOrderType`)

- **Security & Initialization:**
  - `INI`: Bank initialization — uploads the user's electronic signature public key (`A005`/`A006`).
  - `HIA`: Host initialization — uploads authentication (`X002`) and encryption (`E002`) public keys.
  - `HPB`: Host public keys download — downloads the bank's certificates and public keys for authentication and payload encryption.
  - `SPR`: Suspension of user access.
- **Reporting & Cash Management:**
  - `STA`: End-of-day bank statement retrieval (SWIFT MT940 or ISO 20022 `camt.053`).
  - `VMK`: Interim transaction report / intraday account report (SWIFT MT942 or `camt.052`).
- **Money Movement:**
  - `CCT`: Corporate Customer Credit Transfer (SEPA PAIN `pain.001`).
  - `CDD`: Corporate Customer Direct Debit (SEPA PAIN `pain.008`).

### 2.3 Pure BCL Cryptographic Contracts (`EbicsKeyStore`, `EbicsSigner`)

EBICS requires three distinct cryptographic key pairs per corporate user:
1. **Signature Key (`A005`/`A006`):** Used for signing orders and authorization decisions (RSA 2048/4096 or ECDSA P-256/P-384).
2. **Authentication Key (`X002`):** Used for TLS/HTTP message-level signature authentication.
3. **Encryption Key (`E002`):** Used for symmetric key exchange and XML payload encryption.

`EbicsSigner` provides:
- Canonical XML canonicalization (C14N) digest computation (`SHA256`).
- Digital signature generation and verification using .NET 10 `RSA.SignData` / `VerifyData` with `RSASignaturePadding.Pkcs1` or `Pss`.

### 2.4 EBICS Message Codec (`EbicsMessageCodec`)

Generates structured XML request envelopes:
- `ebicsUnsecuredRequest` (used for `INI` and `HIA` key submission before certificates are exchanged).
- `ebicsRequest` (used for operational requests with authentication header and body payload).
- Validates XML response envelopes, status codes (`000000` OK, `011000` Authentication OK, `091002` Invalid user, etc.).

---

## 3. Consequences and Verification

### Positive
- Enables direct corporate banking connectivity with institutional European banks without SaaS middlemen.
- Clean separation from FinTS PIN/TAN consumer code.
- Fully portable, Native AOT compatible, and dependency-free.

### Negative
- Requires corporate onboarding (INI/HIA letter generation, manual bank activation cycle).

### Verification Plan
- Automated test suite `EbicsProtocolTests`:
  1. Key generation and serialization for A005, X002, and E002 key types.
  2. Pure BCL digital signature generation and verification.
  3. `INI` unsecured request XML envelope generation and syntax parsing.
  4. `HIA` request XML generation with authentication and encryption key parameters.
  5. Operational `ebicsRequest` framing with order type metadata (`CCT`, `STA`).
  6. Return code parsing and error classification.
