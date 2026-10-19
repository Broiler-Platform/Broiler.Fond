# ADR 0140: Professional Roles and Four-Eyes Approval Workflow

- **Status:** Accepted
- **Date:** 2026-10-19
- **Related:** [ADR 0106](0106-payment-domain-draft-revisions-and-sepa-validation.md), [ADR 0108](0108-durable-payment-state-machine-and-attempt-allocation.md), [docs/roadmap.md](../roadmap.md#separate-professional-track), [docs/professional-track.md](../professional-track.md#p-01-professional-domain-model-multi-user-roles--four-eyes-approval-workflow)

---

## 1. Context and Problem Statement

Corporate and business banking operations require strict internal controls, separation of duties, and authorization limits that differ fundamentally from the single-user consumer homebanking model.

The roadmap states:
> *"Do not add professional workflows by merely exposing more buttons in the consumer permission model. Create a separate plan for: EBICS connectivity, CAMT/pain batch exchange and bulk payments, multiple users, roles, four-eyes approval, and distributed signatures, accounting exports and longer audit retention, organization-level key recovery, policy, support, and compliance."*

Corporate money movement hazards include:
1. **Unauthorized single-actor payments:** An operator could stage and unilaterally execute high-value transactions without managerial review.
2. **Self-approval loopholes:** A user holding multiple roles could attempt to approve their own initiated payment order.
3. **Audit ambiguity:** Lack of immutable provenance regarding which user prepared, reviewed, and authorized an order.
4. **Architectural boundary:** Professional multi-user abstractions must reside in `Broiler.Fond.Kernel.Professional` without polluting or destabilizing the lightweight consumer domain.

---

## 2. Decision and Architecture

### 2.1 Professional Roles (`ProfessionalRole`)

We define canonical corporate roles:
- `Viewer`: Read-only access to account balances, transaction lists, and financial reports.
- `Operator`: Can configure drafts, import bulk PAIN payment batches, and stage EBICS order requests. Cannot authorize execution.
- `Approver`: Qualified managerial authority permitted to review payment details, perform Verification of Payee (VoP) assessment, and provide electronic signatures.
- `Administrator`: Manages organizational policies, approval thresholds, user provisioning, and audit exports.

### 2.2 Four-Eyes Approval Engine (`FourEyesApprovalEngine`)

Corporate policy defines an approval threshold (`ApprovalThreshold`, e.g. EUR 5,000.00). Any payment or order batch equal to or exceeding the threshold triggers the mandatory four-eyes approval workflow:
1. **Request Creation:** An `Operator` creates an `ApprovalRequest` bound to an immutable `OrderId`. Status: `PendingApproval`.
2. **Distinct Principals Invariant:** Self-approval is strictly forbidden. The engine throws `InvalidOperationException` if `ApproverUserId == InitiatorUserId`.
3. **Quorum Verification:** Requires two distinct qualified `Approver` signatures. The first approval transitions the state to `PartiallyApproved`; the second transitions to `Approved`.
4. **Immediate Rejection:** Any rejecting decision transitions the request immediately to `Rejected` (terminal).
5. **No Post-Terminal Decisions:** Decisions submitted against terminal requests (`Approved`, `Rejected`, `Cancelled`) fail closed.

### 2.3 Extended Corporate Audit Logging

Every decision creates an immutable `CorporateAuditEntry`:
- `Timestamp`: Monotonic UTC.
- `UserId`: Qualified user identifier.
- `Role`: Role held at decision time.
- `Action`: E.g. `InitiateOrder`, `FirstApproval`, `SecondApproval`, `RejectOrder`.
- `TargetReference`: Order ID or Batch ID.
- `DigitalSignatureToken`: Cryptographic signature binding the decision to the order content hash.

---

## 3. Consequences and Verification

### Positive
- Strict corporate governance and separation of duties enforced at the domain kernel level.
- Complete immunity to self-approval and single-operator fraud.
- Clean isolation between consumer and professional models.

### Negative
- Professional workflows require multi-user identity management and policy configuration.

### Verification Plan
- `ProfessionalApprovalWorkflowTests`:
  1. Role permissions: verify that `Operator` cannot approve and `Viewer` cannot initiate.
  2. Four-eyes threshold evaluation: sub-threshold orders auto-qualify; super-threshold orders require dual approval.
  3. Rejection of self-approval: initiator attempting approval throws `InvalidOperationException`.
  4. Quorum progression: `PendingApproval` -> `PartiallyApproved` -> `Approved`.
  5. Rejection behavior and post-terminal rejection of decisions.
  6. Audit entry creation and verification.
