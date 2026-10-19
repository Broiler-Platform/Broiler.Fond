# ADR 0112: Own-Account Transfers and SEPA Instant Credit Transfers

## Status
Accepted

## Context
Milestone 5 introduces everyday consumer payment operations, starting with M5-01:
1. **Transfers between the user's own accounts (Own-Account Transfers / Umbuchung):** Facilitating money movement between accounts belonging to the user within the same local profile, distinguishing internal bank transfers (same institution/BLZ) from external cross-institution transfers.
2. **SEPA Instant Credit Transfers (SCT Inst):** Real-time electronic money movement executing within seconds (typically $\le 10$ seconds) 24/7/365.

Key invariants and architectural requirements:
- **Capability-Gated Execution:** SEPA Instant credit transfers are enabled strictly when advertised by bank parameter data (BPD via `HIPBS` / `HKIPB` or SEPA instant format descriptor). If an institution does not advertise instant capabilities, the mode cannot be selected.
- **Strict Anti-Downgrade Invariant:** Under no circumstances may an instant payment be silently converted or downgraded to a standard SEPA credit transfer. If the bank or clearing system rejects instant execution, or if the clearing window times out, the client must fail closed and require explicit user direction rather than silently altering the execution contract.
- **Own-Account Transfer Validation:** Source and destination accounts must be distinct accounts within the user's active profile. Internal transfers between accounts at the same institution (matching bank code/BLZ) avoid external clearing network dependencies where supported.
- **Amount Limits & Real-Time Timing:** Instant payments are bounded by the bank-advertised limit (default EPC ceiling of 100,000.00 EUR or lower bank-specific maximum). Because instant payments execute immediately, their execution date must be the current date (they cannot be future-dated).
- **ISO 20022 `pain.001.001.03` Scheme Indicator:** In compliance with the EPC SCT Inst Rulebook, instant credit transfer initiation documents must include `<LclInstrm><Cd>INST</Cd></LclInstrm>` inside `<PmtTpInf>`.
- **Zero Third-Party Dependencies:** Implemented exclusively with .NET 10 shared framework APIs within `Broiler.Fond.Kernel`.

## Decisions

### 1. Payment Execution Mode Domain Enum
We expand `PaymentExecutionMode` in `src/Broiler.Fond.Kernel/Domain/Payments/PaymentExecutionMode.cs`:
- `StandardCreditTransfer = 1`: Standard SEPA Credit Transfer (SCT).
- `InstantCreditTransfer = 2`: SEPA Instant Credit Transfer (SCT Inst).
- `OwnAccountTransfer = 3`: Own-account transfer between accounts in the user's profile.

### 2. Instant Payment Capability & BPD Advertisement
We implement:
- `InstantPaymentCapability`: Immutable domain record capturing `IsSupported`, `MaximumAmount`, `EstimatedExecutionSeconds` (default 10), `FeeNotice`, `SupportedSegmentCode` (`HKIPB`), and `SupportedSegmentVersion`.
- `FinTsInstantPaymentAdvertisement`: Parser for FinTS 3.0 BPD parameter segment `HIPBS` (SEPA Instant Payment Parameter), extracting maximum amounts and format identifiers.

### 3. Strict Anti-Downgrade Guard (`InstantPaymentDowngradeException`)
We define `InstantPaymentDowngradeException` in `src/Broiler.Fond.Kernel/Domain/Payments/InstantPaymentDowngradeException.cs`. Any workflow attempt to route an `InstantCreditTransfer` draft into a standard `HKCCS` submission without explicit user conversion throws this exception.

### 4. Own-Account Transfer Validation (`OwnAccountTransferValidator`)
We implement `OwnAccountTransferValidator` in `src/Broiler.Fond.Kernel/Domain/Payments/OwnAccountTransferValidator.cs`:
- Validates that source and destination account IDs and IBANs are distinct.
- Enforces exact decimal money in EUR.
- Categorizes transfers as internal (`IsInternalTransfer`) when bank codes match.

### 5. Extended SEPA Draft Validator (`SepaPaymentDraftValidator`)
We extend `SepaPaymentDraftValidator` to validate instant payment constraints:
- Verifies that debit institution advertises instant payment capability.
- Validates that amount does not exceed `InstantPaymentCapability.MaximumAmount`.
- Rejects non-current execution dates for instant payments.

### 6. ISO 20022 `pain.001.001.03` Instant Payment Generation
We extend `SepaPainDocumentWriter` in `src/Broiler.Fond.Kernel/FinTs/SepaPainDocumentWriter.cs`:
- Accepts `PaymentExecutionMode`.
- For `InstantCreditTransfer`, emits `<LclInstrm><Cd>INST</Cd></LclInstrm>` within `<PmtTpInf>`.

### 7. FinTS 3.0 `HKIPB` Segment Wire Codec
We extend `FinTsSepaTransferRequestWriter` in `src/Broiler.Fond.Kernel/FinTs/FinTsSepaTransferRequestWriter.cs`:
- `BuildHkipbSegment`: Encodes raw wire bytes for `HKIPB` (SEPA Instant Payment Einzelauftrag).
- `EncodeUnsignedInstant`: Encodes 3-segment unsigned messages (`HNHBK`, `HKIPB`, `HNHBS`).

## Consequences

### Positive
- Fully standards-compliant SEPA Instant and own-account transfer support adhering to EPC SCT Inst rules and FinTS 3.0 specifications.
- Safe-by-design anti-downgrade architecture prevents silent alterations to timing and settlement expectations.
- Zero NuGet dependencies maintained within `Broiler.Fond.Kernel`.

### Negative / Trade-offs
- Banks without `HIPBS` or instant capabilities cannot initiate instant transfers; users must explicitly choose standard SEPA transfers instead of automatic fallback.
