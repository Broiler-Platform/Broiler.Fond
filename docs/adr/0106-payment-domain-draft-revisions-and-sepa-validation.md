# ADR 0106: Payment Domain Models, Draft Revisions & SEPA Validation

## Status
Accepted

## Context
Milestone 4 ("Safe SEPA money movement") introduces write capabilities into Broiler Fond, beginning with single SEPA credit transfers. In financial software, irreversible money movement requires extraordinary rigor to eliminate ambiguous states, accidental submissions, and data entry errors.

Key principles from the product specification ([`docs/roadmap.md`](../roadmap.md)):
1. **Read-before-write & draft safety:** A payment draft is an editable local intent. A draft must never be mistaken for a submitted order, nor can it contact a bank directly.
2. **Exact decimal money:** Payment amounts must use exact decimal arithmetic paired with the mandatory EUR currency (`Money`). Floating-point arithmetic is strictly forbidden.
3. **Strict BCL-only boundary:** All validation (including IBAN checksums, SEPA rulebook length constraints, and remittance formatting) must be implemented strictly using .NET 10 shared framework APIs with zero third-party packages or native libraries.
4. **Immutable draft revisions:** Any edit to a draft creates a distinct immutable revision (`PaymentDraftRevision`) linked to its predecessor. This provides a clear audit trail of what was reviewed and authorized.
5. **Fail-closed validation:** Drafts must pass rigorous local validation before advancing to the Verification of Payee (VoP) or authorization stages.

## Decisions

### 1. SEPA IBAN Validation (`SepaIbanValidator`)
- Implements strict ISO 7064 / ISO 13616 MOD 97-10 verification.
- Validates structure: two-letter ISO 3166-1 alpha-2 country code, two check digits, followed by alphanumeric BBAN characters (15 to 34 total characters).
- Enforces official national length requirements for SEPA scheme countries (e.g. DE=22, AT=20, FR=27, IT=27, ES=24, NL=18, BE=16, CH=21, PL=28, GB=22, etc.).
- Calculates MOD 97-10 remainder using iterative division across variable-length alphanumeric character streams without memory allocations or numeric overflow. Valid IBANs yield remainder `1`.
- Sanitizes input: trims leading and trailing whitespace, strips embedded whitespace, and normalizes ASCII characters to uppercase.

### 2. Value Objects: Beneficiary, Remittance, and Execution Mode
- **`PaymentBeneficiary`**:
  - `Name`: trimmed string, 1 to 70 characters per SEPA Credit Transfer Scheme Rulebook.
  - `Iban`: canonical uppercase, space-stripped IBAN verified by `SepaIbanValidator`.
  - `Bic`: optional BIC/SWIFT code (8 or 11 characters) validated against ISO 9362 syntax.
- **`PaymentRemittance`**:
  - `UnstructuredText`: optional unstructured remittance text up to 140 characters.
  - `StructuredReference`: optional ISO 11649 RF creditor reference or national structured format.
  - Invariant: at least one remittance field should typically be provided, but unstructured is capped at 140 characters.
- **`PaymentExecutionMode`**:
  - Enum indicating execution timing: `StandardCreditTransfer` (same-day or next-business-day execution per standard SEPA rules).

### 3. Payment Draft Entity & Immutable Revisions
- **`PaymentDraft`**:
  - Root aggregate holding stable local entity identity `Id` (`LocalEntityId`).
  - Provenance bindings: `SourceAccountId`, `SourceAccountBindingId`, and `SourceAccountIncarnationId`.
  - State: `PaymentDraftStatus` (`Draft`, `Validated`, `Archived`).
  - Links to `CurrentRevisionId` (`LocalEntityId`).
  - Timestamps: `CreatedAt`, `UpdatedAt`.
- **`PaymentDraftRevision`**:
  - Immutable record capturing exact payment intent: `Id`, `DraftId`, `RevisionNumber` (monotonically increasing integer starting at 1), `PredecessorRevisionId`, `Timestamp`.
  - Payment parameters: `Beneficiary`, `Amount` (exact `Money`), `Remittance`, `ExecutionDate` (`DateOnly`), and optional `CustomerReference` (local label/EndToEndId prefix).

### 4. SEPA Payment Draft Validator (`SepaPaymentDraftValidator`)
- Validates a `PaymentDraftRevision` against SEPA credit transfer invariants:
  - Currency must be strictly `"EUR"`.
  - Amount must be strictly greater than zero (`> 0.00 EUR`).
  - Amount must have at most 2 decimal places (exact Euro cents, no sub-cent fractions).
  - Beneficiary name must be non-empty and at most 70 characters.
  - Beneficiary IBAN must pass `SepaIbanValidator.Validate`.
  - Remittance unstructured text must not exceed 140 characters.
  - Execution date cannot be in the past (`ExecutionDate >= today`).
- Returns a structured `SepaValidationResult` containing a boolean `IsValid` and a read-only list of typed `SepaValidationError` entries with specific error codes:
  - `InvalidCurrency`
  - `InvalidAmount`
  - `AmountExceedsScale`
  - `MissingBeneficiaryName`
  - `BeneficiaryNameTooLong`
  - `InvalidIban`
  - `RemittanceTooLong`
  - `ExecutionDateInPast`

## Consequences

### Positive
- Strict separation between local draft intent and bank-facing orders guarantees drafts cannot accidentally cross the network boundary.
- Immutable revisions preserve full provenance and history of user edits.
- Rigorous IBAN MOD 97-10 check catches typographical errors locally before initiating any bank communication.
- Pure BCL implementation preserves zero-dependency kernel invariant.

### Negative / Trade-offs
- Creating a new revision for every draft modification adds minor storage overhead, but provides essential tamper-evidence and auditability for financial transactions.
