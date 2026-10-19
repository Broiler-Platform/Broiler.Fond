# ADR 0143: Distributed Electronic Signatures (VEU) and Order Lifecycle

- **Status:** Accepted
- **Date:** 2026-10-19
- **Related:** [ADR 0140](0140-professional-roles-and-four-eyes-approval-workflow.md), [ADR 0141](0141-ebics-protocol-architecture-and-order-types.md), [ADR 0142](0142-iso-20022-batch-processing-and-granular-reconciliation.md), [docs/roadmap.md](../roadmap.md#separate-professional-track), [docs/professional-track.md](../professional-track.md#p-04-distributed-electronic-signatures-veu-and-order-lifecycle)

---

## 1. Context and Problem Statement

In corporate treasury management, financial orders (e.g. multimillion-euro payroll runs, cross-border treasury settlements) are rarely initiated and executed by the same person at the same moment. The EBICS Distributed Electronic Signature (VEU — *Verteilte Elektronische Unterschrift*, also known as Electronic Distributed Signature / EDS) standardizes asynchronous, multi-actor banking authorization:

1. An accounting clerk stages a payment file with a Transport Signature (`T`). The order is received by the bank but remains unexecuted.
2. A first authorized manager reviews order details and applies their First Signature (`A`).
3. A second authorized manager (in another office or on another device) reviews the order and applies their Second Signature (`B`).
4. Once quorum is satisfied, the bank releases and executes the order.
5. Alternatively, an executive with Single Authorization (`E`) authority can unilaterally authorize orders within their statutory limits.

The problem is providing a deterministic, fail-closed state machine in `Broiler.Fond.Kernel.Professional` that manages the VEU lifecycle, prevents single-user dual-signing fraud, enforces statutory signature classes, and parses/generates VEU order queries.

---

## 2. Decision and Architecture

### 2.1 EBICS VEU Signature Classes (`VeuSignatureClass`)

We define canonical EBICS signature classes:
- `Transport` (`T`): Transport signature. Authenticates the file transfer to the bank; conveys no payment authorization.
- `SingleAuthorization` (`E`): Single executive signature (*Einzelunterschrift*). Sufficient to release eligible orders unilaterally.
- `FirstSignature` (`A`): First signature (*Erstunterschrift*). Requires a corresponding second signature before execution.
- `SecondSignature` (`B`): Second signature (*Zweitunterschrift*). Completes authorization when combined with a first signature.

### 2.2 VEU Order State Machine (`VeuOrderState`)

Lifecycle transitions:
1. `StagedWithTransportSignature`: Uploaded with `T`, pending signatory review.
2. `PendingFirstSignature`: Initial authorization state.
3. `PendingSecondSignature`: `A` signature applied; awaiting distinct `B` signature.
4. `CompletelySigned`: Quorum met (`A` + `B` or `E`).
5. `Released`: Order confirmed released by bank.
6. `Rejected`: Terminal rejection by any qualified signatory.
7. `Cancelled`: Terminal revocation by originator prior to full signature.

### 2.3 Strict Governance Invariants

- **Distinct Signatories Invariant:** The same user cannot provide both `A` and `B` signatures for the same order. An attempt by the first signatory to apply the second signature throws `InvalidOperationException`.
- **Class Enforcement:** An order configured for dual authorization (`A` + `B`) cannot transition to `CompletelySigned` from an `A` signature alone.
- **Fail-Closed Terminal State:** No signatures or status updates can be applied to an order once in `CompletelySigned`, `Released`, `Rejected`, or `Cancelled`.

### 2.4 EBICS VEU Protocol Order Types

- `HVZ`: Retrieve overview of pending VEU orders requiring the user's signature.
- `HVD`: Download order details and order data digest for a specific pending VEU order.
- `HVE`: Submit electronic signature (partial or second signature).
- `HVS`: Submit final release signature or cancellation.
- `HVT`: Download transaction status log / signature history for an order.

---

## 3. Consequences and Verification

### Positive
- Fully standards-compliant EBICS VEU workflow for distributed corporate teams.
- Cryptographically verifiable signature progression with strict separation of duties.
- BCL-only implementation without third-party dependencies.

### Negative
- Requires polling bank VEU order queues (`HVZ`) to reflect external signature additions made by co-signatories at other institutions.

### Verification Plan
- Automated test suite `EbicsVeuLifecycleTests`:
  1. Signature class representation and order type mapping (`HVZ`, `HVD`, `HVE`, `HVS`, `HVT`).
  2. Progression from `PendingFirstSignature` -> `PendingSecondSignature` (`A` signature).
  3. Rejection of second signature by the same principal (`A` signatory attempting `B`).
  4. Successful progression to `CompletelySigned` when distinct `B` signature is applied.
  5. Executive single authorization (`E`) workflow transitioning directly to `CompletelySigned`.
  6. Immediate rejection workflow transitioning to terminal `Rejected` state.
  7. Post-terminal signature prevention.
