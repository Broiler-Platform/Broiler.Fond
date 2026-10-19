# ADR 0113: Scheduled SEPA Credit Transfers and Bank Order Lifecycle

## Status
Accepted

## Context
Milestone 5 introduces everyday payment capabilities. While standard SEPA credit transfers (M4) execute as soon as possible and instant transfers (M5-01) settle within seconds, many consumer obligations require future-dated execution:
1. **Scheduled SEPA Credit Transfers (SEPA-Terminüberweisung):** Orders submitted to the bank with a designated future execution date ($D > \text{today}$).
2. **Order Lifecycle & Advance Lead Times:** Banks enforce parameters such as minimum advance lead time ($\ge 1$ business day) and maximum forward lead time (e.g. up to 365 or 730 days).
3. **Cancellation & Revocation:** Prior to the bank's processing cut-off on or before execution date, users must be able to cancel scheduled orders.
4. **Truthful State Invariant:** A scheduled transfer is distinct from an executed or settled payment. It remains in `Pending` state until execution date and statement booking evidence or status query confirms execution. A cancellation write remains pending/unknown until authoritative bank acknowledgment confirms revocation.
5. **Zero Third-Party Dependencies:** Implemented strictly with .NET 10 shared framework APIs within `Broiler.Fond.Kernel`.

## Decisions

### 1. Scheduled Payment Capability & BPD Advertisement
We implement `ScheduledPaymentCapability` in `src/Broiler.Fond.Kernel/Domain/Payments/ScheduledPaymentCapability.cs`:
- `IsSupported`: Boolean indicating bank support.
- `MinimumLeadDays`: Minimum advance notice in days (default 1).
- `MaximumLeadDays`: Maximum advance scheduling horizon in days (default 365).
- `CanModify`: Whether the bank permits editing existing scheduled orders.
- `CanCancel`: Whether the bank permits online deletion of scheduled orders.
- `SupportedSegmentCode`: FinTS 3.0 segment code (`HKCME`).
- `SupportedSegmentVersion`: FinTS segment version.

We also implement `FinTsScheduledPaymentAdvertisement` in `src/Broiler.Fond.Kernel/FinTs/FinTsScheduledPaymentAdvertisement.cs` to parse BPD parameter segments (`HICMES`).

### 2. Scheduled Payment Order Entity & States
We introduce `ScheduledOrder` and `ScheduledOrderStatus` in `src/Broiler.Fond.Kernel/Domain/Payments/ScheduledOrder.cs`:
- `ScheduledOrderStatus`:
  - `Pending = 1`: Registered at the bank, awaiting future execution date.
  - `Executed = 2`: Execution date reached and confirmed via account activity or bank status.
  - `Cancelled = 3`: Bank confirmed cancellation prior to execution.
  - `CancellationPending = 4`: Cancellation request submitted, awaiting bank confirmation.

### 3. Date Boundary Validation (`ScheduledPaymentValidator`)
We implement `ScheduledPaymentValidator` in `src/Broiler.Fond.Kernel/Domain/Payments/ScheduledPaymentValidator.cs`:
- Enforces that execution date $D$ satisfies:
  $$\text{evaluationDate} + \text{MinimumLeadDays} \le D \le \text{evaluationDate} + \text{MaximumLeadDays}$$
- Rejects today or past dates for scheduled transfers.
- Rejects dates exceeding the bank's advance limit with `SepaValidationErrorCode.ScheduledDateOutsideLeadWindow`.

### 4. FinTS 3.0 Wire Codecs
We implement in `src/Broiler.Fond.Kernel/FinTs/FinTsScheduledTransferRequestWriter.cs`:
- `BuildHkcmeSegment`: Encodes `HKCME` (SEPA-Terminüberweisung einreichen) containing international account connection, SEPA pain descriptor, and binary ISO 20022 XML payload.
- `EncodeUnsignedScheduled`: Wraps `HNHBK`, `HKCME`, and `HNHBS`.
- `BuildHkcbdSegment`: Encodes `HKCBD` (SEPA-Terminüberweisung löschen) containing account connection and bank order reference.
- `EncodeUnsignedCancellation`: Wraps `HNHBK`, `HKCBD`, and `HNHBS`.

### 5. ISO 20022 `pain.001.001.03` ReqdExctnDt
The existing `SepaPainDocumentWriter` populates `<ReqdExctnDt>` with the exact future date specified in the validated draft revision.

## Consequences

### Positive
- Strict compliance with FinTS 3.0 specifications for scheduled transfers (`HKCME`, `HICMES`, `HKCBD`).
- Honest tracking of scheduled vs. executed money movements, preventing premature booking assumptions.
- BCL-only implementation preserving the kernel dependency boundary.

### Negative / Trade-offs
- Banks that do not advertise `HICMES` cannot schedule payments through the client; users must execute standard transfers manually on the desired day.
