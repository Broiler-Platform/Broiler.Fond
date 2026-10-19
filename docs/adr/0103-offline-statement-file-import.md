# ADR 0103: Offline Fallback Statement File Import (CAMT & MT940)

## Status
Accepted

## Context
While Broiler Fond primarily connects directly to financial institutions via FinTS 3.0, users frequently possess historical bank statement exports downloaded from web portals (e.g. at the end of the year or after switching banking providers) in SWIFT MT940/MT942 or ISO 20022 CAMT format (`camt.052`, `camt.053`).

Milestone 3 requires an offline fallback statement file import engine adhering to the product principles:
1. **Purely offline & credential-free:** Ingesting statement files must never require, prompt for, or touch banking credentials (PIN/TAN) and never communicate over the network.
2. **Canonical wire parser reuse:** The import engine must reuse the same robust, bounded parsers developed in Milestone 2 (`Mt940Parser` and `CamtXmlParser`) rather than introducing separate, divergent parsing logic.
3. **Lossless observation provenance:** Imported records must be stored as immutable `TransactionObservation` entities with a distinct provenance source indicator (`FileImport:MT940`, `FileImport:CAMT.053`) and source reference metadata (file digest, entry index), clearly distinguishing them from live bank FinTS dialogues.
4. **Multiplicity preservation and deduplication:** Overlapping statement files or re-importing the same file must not create duplicate transactions. The correlation logic must preserve genuine duplicate payments (multiplicity) while recognizing previously ingested records.
5. **Account matching and integrity:** The engine must correlate file account identifiers (IBAN, domestic account number, BLZ) with existing active `Account` and `AccountBinding` entities in the profile store, or allow the user to select the destination account.

## Decisions

### 1. Statement Format Auto-Detection (`StatementFileDetector`)
We implement pure .NET runtime format detection in `Broiler.Fond.Kernel.Domain.Transactions.Import`:
- Detects ISO 20022 XML schemas:
  - `camt.053.001.02` / `camt.053.001.08` (End-of-day bank statement) -> `StatementFileFormat.Camt053`.
  - `camt.052.001.02` (Intraday report) -> `StatementFileFormat.Camt052`.
- Detects SWIFT MT format tags (`:20:`, `:25:`, `:28C:`, `:60F:`, `:61:`, `:86:`) -> `StatementFileFormat.Mt940` / `StatementFileFormat.Mt942`.
- Fails closed for unknown or binary non-statement formats with `StatementFileFormat.Unknown`.

### 2. Statement File Importer (`StatementFileImporter`)
We implement `StatementFileImporter`:
- **Account Binding Correlation:**
  - Extracts declared IBAN and account number from the statement.
  - Matches against `StoreAccountsDocument.Bindings` in the profile store.
  - If a single matching active account is found, uses its `AccountId` and `AccountBindingId`.
  - If no match or multiple matches exist, uses `options.TargetAccountId` and `options.TargetAccountBindingId` provided by the caller/user.
- **Correlation & Multiplicity:**
  - Uses `StatementOverlapSyncPlanner` to correlate statement entries against existing transactions and revisions in the profile store.
  - Generates new `TransactionRevision`, `TransactionObservation`, and `StoreLedgerRecord` entries for new bookings.
  - Recognizes previously imported bookings and increments `TransactionsMatchedCount` without duplicate creation.
- **Observation Provenance:**
  - Each imported observation records `FileImport:<Format>` as its format tag, retaining file details in references.
- **Atomic Local Store Commit:**
  - Commits changes to `IProfileStore` via `MutateAndSave`, ensuring monotonic generation increment and store invariant validation.

## Consequences

### Positive
- Allows users to populate account history offline without needing active bank connectivity or PINs.
- Eliminates code duplication by reusing canonical M2 MT940 and CAMT parsers.
- Prevents duplicate transaction records even if a user imports overlapping or duplicate files.
- Preserves full auditability and provenance of offline file imports versus live bank synchronization.

### Negative / Trade-offs
- If a statement file contains transactions for an account that does not exist in the profile, the user must explicitly link it to a configured account or establish the account first.
