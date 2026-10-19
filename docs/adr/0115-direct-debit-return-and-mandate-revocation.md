# ADR 0115: Direct-Debit Return Writes and Mandate Revocation

## Status
Accepted

## Context
Milestone 5 introduces everyday payment workflows, including post-settlement payment remedies under European and German payment regulations (Directive (EU) 2015/2366 / PSD2, Regulation (EU) 260/2012 / SEPA Regulation, and §§ 675x, 676b BGB):
1. **Statutory Return Rights & Deadlines:**
   - **8-Week Unconditional Return:** For authorized SEPA Core Direct Debits (SEPA-Basislastschrift), the payer (debtor) has an unconditional right to a refund within 8 weeks (56 calendar days) from the debit date (§ 675x Abs. 2 BGB, SEPA Rulebook).
   - **13-Month Unauthorized Return:** For unauthorized or fraudulently initiated direct debits (no valid mandate), the refund claim extends up to 13 months (396 calendar days) after the debit date (§ 676b BGB).
2. **Standard Reason Codes:**
   - `MS02`: Return by order of the Debtor / Refusal by Debtor (unconditional 8-week consumer return).
   - `MS03`: Unspecified debtor reason / customer cancellation.
   - `MD01`: No valid mandate (unauthorized debit claim, 13 months).
   - `MD06`: Disputed authorized transaction / refund requested by debtor.
3. **Capability-Gated Execution:** Direct-debit return writes (`HKDRW`) and mandate management/revocation (`HKDMB`) may only be offered if explicitly advertised in bank parameter data (BPD: `HIDRWS`, `HIDMBS`).
4. **Truthful State & Zero Silent Retries:** Submitting a return request to the bank registers the instruction. The local state transitions to `SubmittedAwaitingStatement` and remains pending/unknown until authoritative statement or turnover evidence (`camt.052`, `camt.053`, MT940, or `HIKAZ`) confirms the booking of the credit refund. Network uncertainties enter `Unknown` and must never trigger automatic retries.
5. **BCL-Only Invariant:** Implemented strictly using .NET 10 shared framework APIs within `Broiler.Fond.Kernel`.

## Decisions

### 1. Direct-Debit Return Domain Model
We implement in `src/Broiler.Fond.Kernel/Domain/Payments/DirectDebitReturn.cs`:
- `DirectDebitReturnReason`:
  - `DebtorRequestUnconditional = 1` (`MS02` - 8-week unconditional return)
  - `DebtorRequestGeneral = 2` (`MS03` - unspecified debtor reason)
  - `NoValidMandate = 3` (`MD01` - 13-month unauthorized return)
  - `DisputedTransaction = 4` (`MD06` - disputed authorized debit)
- `DirectDebitReturnStatus`:
  - `PendingSubmission = 1`: Created locally, not yet transmitted.
  - `SubmittedAwaitingStatement = 2`: Bank accepted instruction; waiting for statement credit.
  - `Reconciled = 3`: Matched with bank statement reversal/credit entry.
  - `Rejected = 4`: Bank authoritatively rejected the return request.
  - `Unknown = 5`: Network dropped during transmission; resolution requires statement reconciliation.
- `DirectDebitReturnRequest`: Immutable domain record with:
  - `Id`, `SourceAccountId`, `SourceAccountBindingId`, `SourceAccountIncarnationId`
  - `OriginalBankReference`, `CreditorId`, `MandateId`, `CreditorName`, `CreditorIban`
  - `Amount`, `OriginalBookingDate`, `OriginalDueDate`
  - `Reason`, `Status`, `CreatedAt`, `SubmittedAt`

### 2. Statutory Deadline Enforcement
We implement `DirectDebitStatutoryDeadline` in `src/Broiler.Fond.Kernel/Domain/Payments/DirectDebitStatutoryDeadline.cs`:
- `AuthorizedReturnLimitDays = 56` (8 calendar weeks).
- `UnauthorizedReturnLimitDays = 396` (13 calendar months).
- `IsWithinStatutoryDeadline(DirectDebitReturnReason reason, DateOnly debitBookingDate, DateOnly asOfDate)` calculates elapsed calendar days and enforces the strict statutory ceiling.

### 3. Capability & BPD Advertisement
We implement:
- `DirectDebitReturnCapability` in `src/Broiler.Fond.Kernel/Domain/Payments/DirectDebitReturnCapability.cs`:
  - `IsSupported`: Bank advertises `HIDRWS`.
  - `SupportedReasons`: Allowed reason codes parsed from BPD.
  - `SupportsMandateRevocation`: Bank advertises `HIDMBS`.
- `FinTsDirectDebitReturnAdvertisement` in `src/Broiler.Fond.Kernel/FinTs/FinTsDirectDebitReturnAdvertisement.cs`:
  - Parses `HIDRWS` and `HIDMBS` parameter segments from dialogue initialization or BPD response.

### 4. Direct-Debit Return Validation
We implement `DirectDebitReturnValidator` in `src/Broiler.Fond.Kernel/Domain/Payments/DirectDebitReturnValidator.cs`:
- Gated by `DirectDebitReturnCapability`.
- Gated by safe mode (`IsSafeModeActive` must be false).
- Validates statutory deadline for the requested reason code.
- Validates creditor ID format, mandate ID, positive EUR amount, and debtor account identity.

### 5. FinTS 3.0 Wire Codecs
We implement:
- `FinTsDirectDebitReturnRequestWriter` in `src/Broiler.Fond.Kernel/FinTs/FinTsDirectDebitReturnRequestWriter.cs`:
  - `BuildHkdrwSegment`: Generates `HKDRW` (SEPA-Lastschriftrückbuchung) segment with debtor IBAN/BIC, original reference, reason code, amount:EUR, creditor ID, mandate reference, and original due date.
  - `BuildHkdmbSegment`: Generates `HKDMB` (SEPA-Mandatsverwaltung) segment for mandate revocation (`Aktionscode = 1`).
  - `EncodeUnsignedReturn`: Generates unsigned envelope containing `HKDRW`.
  - `EncodeUnsignedMandateRevocation`: Generates unsigned envelope containing `HKDMB`.
- `FinTsDirectDebitReturnResponseParser` in `src/Broiler.Fond.Kernel/FinTs/FinTsDirectDebitReturnResponseParser.cs`:
  - Parses `HIDRW` confirmation segments or extract correlated `HIRMS` return codes.

## Consequences
- Consumers can initiate direct-debit returns and mandate revocations directly via FinTS when supported by their bank.
- Statutory 8-week and 13-month deadlines are enforced prior to any write attempt.
- Bank statement evidence remains the single source of truth for settled reversals; pending return requests are tracked truth-to-fact without fabricating balance adjustments.
