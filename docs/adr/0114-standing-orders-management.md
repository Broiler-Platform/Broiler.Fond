# ADR 0114: Standing Orders Management

## Status
Accepted

## Context
Milestone 5 introduces everyday payment workflows. Standing orders (SEPA-Daueraufträge) represent recurring, automated credit transfers executed periodically by the debtor bank:
1. **Recurrence Cadence:** Recurrence frequency specified by statutory and FinTS standards (Monthly, Two-Monthly, Quarterly, Semi-Annually, Annually), coupled with execution day (1..31 or last calendar day).
2. **Lifecycle Operations:** Banks expose 4 distinct business transactions:
   - Querying active standing orders (`HKCDB` -> `HICDB`).
   - Setting up a new standing order (`HKCDE`).
   - Modifying an existing standing order (`HKCDA`).
   - Deleting or cancelling a standing order (`HKCDL`).
3. **Truthful State & Authoritative Bank Evidence:** A standing order is maintained on bank systems. Local state reflects bank reality; changes, setups, or deletions must be confirmed by authoritative bank responses (`HICDE`, `HICDA`, `HICDL`, or refreshed `HICDB` order lists).
4. **Zero Third-Party Dependencies:** Implemented strictly using .NET 10 shared framework APIs within `Broiler.Fond.Kernel`.

## Decisions

### 1. Standing Order Domain Model
We implement in `src/Broiler.Fond.Kernel/Domain/Payments/StandingOrder.cs`:
- `StandingOrderFrequency`:
  - `Monthly = 1`: Every month.
  - `TwoMonthly = 2`: Every two months.
  - `Quarterly = 3`: Every quarter (3 months).
  - `SemiAnnually = 6`: Every half-year (6 months).
  - `Annually = 12`: Once a year (12 months).
- `StandingOrderStatus`:
  - `Active = 1`: Confirmed active at the bank.
  - `ModificationPending = 2`: Modification submitted, awaiting bank confirmation.
  - `CancellationPending = 3`: Cancellation submitted, awaiting bank confirmation.
  - `Cancelled = 4`: Bank confirmed cancellation/deletion.
- `StandingOrder`: Domain record with `Id`, `SourceAccountId`, `BankOrderReference`, `Amount`, `Beneficiary`, `Remittance`, `Frequency`, `ExecutionDay`, `FirstExecutionDate`, `LastExecutionDate`, `Status`, `CreatedAt`.

### 2. Standing Order Capability & BPD Advertisement
We implement `StandingOrderCapability` in `src/Broiler.Fond.Kernel/Domain/Payments/StandingOrderCapability.cs`:
- `IsSupported`: Bank supports standing orders.
- `CanCreate`: Bank supports `HKCDE`.
- `CanModify`: Bank supports `HKCDA`.
- `CanDelete`: Bank supports `HKCDL`.
- `SupportedFrequencies`: Allowed frequency values.
- `MinimumLeadDays`: Minimum advance notice to set up or change standing orders.

We implement `FinTsStandingOrderAdvertisement` in `src/Broiler.Fond.Kernel/FinTs/FinTsStandingOrderAdvertisement.cs` to parse BPD parameter segments (`HICDBS`, `HICDES`, `HICDAS`, `HICDLS`).

### 3. Cadence & Date Calculations (`StandingOrderScheduleCalculator`)
We implement `StandingOrderScheduleCalculator` in `src/Broiler.Fond.Kernel/Domain/Payments/StandingOrderScheduleCalculator.cs`:
- Calculates the next execution date given `frequency`, `executionDay`, and `referenceDate`.
- Accurately adjusts for short months (e.g. day 31 in February or April adjusts to 28/29 or 30).
- Honors `LastExecutionDate` by returning null once the terminal execution date has elapsed.

### 4. Standing Order Validation (`StandingOrderValidator`)
We implement `StandingOrderValidator` in `src/Broiler.Fond.Kernel/Domain/Payments/StandingOrderValidator.cs`:
- Validates frequency against bank capability.
- Validates execution day is between 1 and 31.
- Validates first execution date $\ge$ `evaluationDate + MinimumLeadDays`.
- Validates `LastExecutionDate > FirstExecutionDate` when specified.
- Validates beneficiary IBAN, EUR currency, and strictly positive amount.

### 5. FinTS 3.0 Wire Codecs (`FinTsStandingOrderRequestWriter`)
We implement in `src/Broiler.Fond.Kernel/FinTs/FinTsStandingOrderRequestWriter.cs`:
- `BuildHkcdbSegment`: Encodes list query (`HKCDB`).
- `EncodeUnsignedListQuery`: Wraps `HNHBK`, `HKCDB`, `HNHBS`.
- `BuildHkcdeSegment`: Encodes setup order (`HKCDE`) with recurrence parameters and pain.001 payload.
- `EncodeUnsignedSetup`: Wraps `HNHBK`, `HKCDE`, `HNHBS`.
- `BuildHkcdaSegment`: Encodes modification order (`HKCDA`) with bank order reference and updated parameters.
- `EncodeUnsignedModify`: Wraps `HNHBK`, `HKCDA`, `HNHBS`.
- `BuildHkcdlSegment`: Encodes deletion order (`HKCDL`) with bank order reference.
- `EncodeUnsignedDelete`: Wraps `HNHBK`, `HKCDL`, `HNHBS`.

### 6. FinTS 3.0 Response Parser (`FinTsStandingOrderResponseParser`)
We implement `FinTsStandingOrderResponseParser` in `src/Broiler.Fond.Kernel/FinTs/FinTsStandingOrderResponseParser.cs`:
- Parses `HICDB` segments into standing order models.
- Extracts order references from `HICDE`, `HICDA`, `HICDL` confirmation segments.

## Consequences

### Positive
- Full lifecycle management (list, setup, edit, delete) of SEPA standing orders.
- Strict adherence to FinTS 3.0 specification without external dependencies.
- Reliable recurrence calculations handling month-end edge cases deterministically.

### Negative / Trade-offs
- Banks with non-standard recurrence options outside 1, 2, 3, 6, 12 months require specific compatibility overrides.
