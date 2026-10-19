# Professional Track — Business Banking, EBICS, and Distributed Approvals

- **Brand:** Broiler
- **Product:** Fond - Finance on Demand (Professional Edition)
- **Technical name:** `Broiler.Fond`

**Status:** Complete (Consumer roadmap Milestones 0 through 9 complete; Professional Track complete).

**Last updated:** 2026-10-19

The [roadmap](roadmap.md#separate-professional-track) defines the high-level requirement:
> *"Do not add professional workflows by merely exposing more buttons in the consumer permission model. Create a separate plan for: EBICS connectivity, CAMT/pain batch exchange and bulk payments, multiple users, roles, four-eyes approval, and distributed signatures, accounting exports and longer audit retention, organization-level key recovery, policy, support, and compliance."*

---

## 1. Outcome

Broiler Fond introduces an enterprise-grade Professional Banking extension to the shared platform-neutral kernel.
It establishes corporate multibank connectivity via EBICS (Electronic Banking Internet Communication Standard),
executes high-volume ISO 20022 bulk payment batches, provides strict role-based access control with four-eyes
authorization and Distributed Electronic Signatures (VEU), and generates certified accounting exports (DATEV CSV
and standard audit trails), while strictly maintaining the zero-telemetry invariant and BCL-only architecture.

---

## 2. Professional Track Principles and Invariants

- **Isolated Professional Domain Model:** Corporate permissions, organization identifiers, and multi-user roles
  exist in dedicated professional domain namespaces (`Broiler.Fond.Kernel.Professional.*`), completely insulated
  from the single-user consumer permission model.
- **Mandatory Four-Eyes Principle:** Money movement exceeding organization-defined thresholds requires dual
  authorization by distinct qualified principals (`Operator` and `Approver`). Self-approval is strictly rejected.
- **Direct Device-to-Bank Protocol:** EBICS connectivity connects directly from the organization's host device
  to the bank's EBICS server via HTTPS/TLS; no Broiler-operated intermediary, relay server, or cloud gateway exists.
- **Strict BCL-only Cryptography:** EBICS digital signatures, key generation, and XML encryption rely strictly on
  .NET 10 runtime cryptographic APIs (`RSA`, `ECDsa`, `AesGcm`, `SHA256`). No unmanaged libraries or NuGet packages.
- **Immutable Extended Audit Ledger:** Every order generation, batch submission, signature release, and rule modification
  is appended to a cryptographically authenticated, tamper-evident corporate audit stream (`StoreAuditDocument`).

---

## 3. Work Breakdown Structure (P-01 through P-05)

### P-01: Professional Domain Model, Multi-User Roles & Four-Eyes Approval Workflow

- [x] Implement professional user roles and four-eyes approval engine ([ADR 0140](adr/0140-professional-roles-and-four-eyes-approval-workflow.md)):
  - Canonical professional roles: `Viewer`, `Operator`, `Approver`, `Administrator`.
  - Four-eyes approval coordinator: threshold evaluation, approval request queue, signature quorum verification, and strict self-approval prohibition.
  - Extended corporate audit trail recording user identity, role, timestamp, action, and digital signature hash.
- [x] Add automated kernel tests for multi-user roles and four-eyes authorization (`ProfessionalApprovalWorkflowTests`).

### P-02: EBICS Protocol Architecture, Order Types & Security Contracts

- [x] Implement EBICS protocol structures and order codecs ([ADR 0141](adr/0141-ebics-protocol-architecture-and-order-types.md)):
  - EBICS versioning (`Ebics 2.5` and `Ebics 3.0`) and host configuration descriptors (`HostId`, `PartnerId`, `UserId`).
  - Standard banking order types: `INI` (bank initialization), `HIA` (public keys upload), `HPB` (bank certificates download), `STA` (account statement retrieval), `CCT` (credit transfers), `CDD` (direct debits).
  - Pure BCL digital signature generation and verification.
- [x] Add automated kernel tests for EBICS protocol structures and signature contracts (`EbicsProtocolTests`).

### P-03: ISO 20022 Batch Processing, Bulk Payments & Granular Reconciliation

- [x] Implement bulk batch processor for credit transfers and direct debits ([ADR 0142](adr/0142-iso-20022-batch-processing-and-granular-reconciliation.md)):
  - High-volume ISO 20022 `pain.001.001.09` (bulk credit transfer) and `pain.008.001.08` (bulk direct debit) generators and parsers.
  - Statutory batch checksum and control sum validation (`CtrlSum`, `NbOfTxs`).
  - Granular reconciliation: link collective bank statement entries (`camt.053`) to individual batch transactions with partial-failure tracking (`RJCT` items).
- [x] Add automated kernel tests for bulk payments and batch reconciliation (`BulkPaymentBatchTests`).

### P-04: Distributed Electronic Signatures (VEU) and Order Lifecycle

- [x] Implement Distributed Electronic Signatures (VEU / EDS) engine ([ADR 0143](adr/0143-distributed-electronic-signatures-veu-lifecycle.md)):
  - Signature categories: Transport signature (`T`), Single authorization (`E`), First signature (`A`), Second signature (`B`).
  - VEU order management: query pending orders via `HVZ`/`HVD`, submit partial signature via `HVE`, and authorize final release via `HVS`.
  - Fail-closed revocation and signature state tracking (`PendingFirstSignature`, `PendingSecondSignature`, `CompletelySigned`, `Rejected`).
- [x] Add automated kernel tests for VEU distributed signature lifecycle (`EbicsVeuLifecycleTests`).

### P-05: Accounting Exports, DATEV Interoperability & Enterprise Compliance

- [x] Implement accounting export engine and compliance reporting ([ADR 0144](adr/0144-accounting-exports-and-datev-interoperability.md)):
  - Standard DATEV export format generator: ASCII/CSV format (Kassenbuch/Posten) with chart of accounts mapping (SKR03 / SKR04), tax keys (BU-Schlüssel), and counter-accounts.
  - Enterprise audit bundle export with cryptographic verification manifest.
  - Organization-level key recovery policy and separation of corporate credentials.
- [x] Add automated kernel tests for accounting exports and DATEV formatting (`AccountingExportTests`).

---

## 4. Exit Criteria

- Professional roles enforce strict separation of duties and prevent single-user bypass of corporate authorization limits.
- EBICS order envelopes generate conformant XML with pure BCL cryptographic signatures.
- High-volume payment batches parse, validate, and reconcile item-by-item against CAMT statements without data loss.
- Distributed electronic signatures (VEU) progress through deterministic multi-signature lifecycles.
- DATEV accounting exports match official format specifications with exact decimal arithmetic.
- Full CI suite passes warning-free across all targets.
