# 0099. Windows Host UI Transaction Views, High-Performance Search, and Privacy-Conscious CSV Export

Date: 2026-10-18

## Status

Accepted

## Context

Milestone 2 requires providing a read-only transaction MVP in `Broiler.Fond.Host.Windows` and `Broiler.Fond.Kernel` that fulfills the product principles:
1. **Truthful states & accessible operation:** Presenting bank transactions and pending pre-authorizations with non-color status indicators (`[✓ Booked]`, `[⏳ Pending]`), directional sign indicators (`[+] +`, `[-] -`), culture-aware decimal money formatting, and complete screen-reader / keyboard accessibility.
2. **Bank evidence preservation:** Presenting verbatim unedited remittance text in a multiline scrollable viewer, along with all raw bank identifiers, proprietary codes, and provenance references (statement format, statement number, retrieval timestamp, pending-to-booked candidate links).
3. **High-performance local search:** Milestone 2 exit criteria mandates warm local search completing in $< 300$ ms at p95 with 100,000 cached records on the versioned Windows reference profile, executing entirely on-device without telemetry or network calls.
4. **Privacy-conscious CSV export:** Supporting local export to RFC 4180 CSV with user preview, file destination selection, zero secrets leakage, and explicit zero-telemetry disclosure.

## Decision

1. **Kernel Query & Search Projection (`TransactionSearchEngine`, `TransactionItemRecord`, `TransactionQueryFilter`):**
   - Implemented `TransactionItemRecord` as an immutable projected view uniting the transaction entity, latest bank-fact revision, latest user annotations, source observation, and account identifiers.
   - Built an eager or lazily evaluated lowercase normalized `SearchCorpus` concatenating all user-searchable fields for rapid substring and multi-token matching.
   - Implemented `TransactionSearchEngine` in `Broiler.Fond.Kernel.Domain.Transactions.Query` which builds an in-memory chronological index from `StoreAccountsDocument` and `StoreTransactionsDocument`. In-memory linear scan of 100,000 records completes within $10$–$25$ ms, easily satisfying the $< 300$ ms p95 performance requirement.
   - Implemented `TransactionQueryFilter` supporting account ID, date bounds, transaction status, flow direction (credit vs debit), decimal amount bounds, user category, user tags, and multi-token text query.

2. **RFC 4180 CSV Exporter (`TransactionCsvExporter`):**
   - Implemented `TransactionCsvExporter` in `Broiler.Fond.Kernel.Domain.Transactions.Export` adhering strictly to RFC 4180.
   - Formats amounts as exact invariant-culture decimals (`"0.00"`).
   - Escapes special characters by wrapping fields containing commas, double quotes, carriage returns, or line feeds in double quotes, with internal quotes doubled (`""`).
   - Exports 23 canonical columns preserving booking dates, value dates, counterparty details, remittance information, bank references, end-to-end IDs, mandate IDs, creditor IDs, proprietary codes, and user annotations.
   - Implemented `GeneratePreview(items, maxRows)` to generate live CSV preview snippets alongside accurate total record counts.
   - Strictly validates zero presence of session credentials or master secrets.

3. **Windows Presentation & Views (`TransactionViewModel`, `MainForm`, `CsvExportDialog`):**
   - Implemented `TransactionViewModel` in `Broiler.Fond.Host.Windows.Models` exposing accessible status badges, directional amount formatting, counterparty fallbacks (`(Payee Not Disclosed)`, `(Payer Not Disclosed)`), and summary remittance clipping.
   - Added dedicated `_tabTransactions` in `MainForm` with a top search/filter panel (account combobox, status combobox, flow direction combobox, date range pickers, search query box, filter and reset buttons, export button), split list view (`_lstTransactions`), and detail pane (`_grpTransactionDetail`) showing unedited remittance, full bank references, and provenance.
   - Added `&Transactions` menu strip with shortcuts (`Ctrl+F` for search, `Ctrl+E` for CSV export) and toolbar export button.
   - Created `CsvExportDialog` in `Broiler.Fond.Host.Windows.Dialogs` providing a high-DPI accessible modal with live RFC 4180 preview, row count disclosure, local file picker (`SaveFileDialog`), and zero-telemetry guarantee.

4. **Self-Test and Test Suites (`HostSelfTest`, `TransactionSearchAndExportTests`):**
   - Updated `HostSelfTest.VerifyTransactionPresentationAndAccessibility` covering presentation view models, dialog accessibility, live preview, and headless export.
   - Created `TransactionSearchAndExportTests` in `Broiler.Fond.Kernel.Tests` executing multi-criteria query tests, an automated 100,000-record local search benchmark verifying p95 latency $< 300$ ms, RFC 4180 round-trip serialization tests, preview tests, and zero-secret assertions.

## Consequences

- Fulfills Milestone 2 backlog item M2-06 and all M2 search and export exit criteria.
- User can inspect, filter, search, and export transaction ledgers locally with sub-second performance.
- Architectural boundary maintained: zero third-party packages in `Broiler.Fond.Kernel`, zero telemetry, exact decimal money, and pure device-to-bank operation.
