using System.Diagnostics;
using System.Globalization;
using System.Text;
using Broiler.Fond.Kernel.Domain.Transactions.Export;
using Broiler.Fond.Kernel.Domain.Transactions.Query;
using Broiler.Fond.Kernel.Storage;

namespace Broiler.Fond.Kernel.Tests;

/// <summary>
/// Executable verification suite for M2-06:
/// Transaction ledger projections, multi-criteria filtering, local search performance benchmark
/// (&lt; 300 ms p95 with 100,000 cached records), RFC 4180 CSV export with escaping and exact decimal formatting,
/// live preview generation, and zero secrets guarantee.
/// </summary>
internal static class TransactionSearchAndExportTests
{
    internal static void Run(Action<bool, string> check)
    {
        int checks = 0;
        void Verify(bool condition, string message)
        {
            checks++;
            check(condition, message);
        }

        VerifySearchEngineSnapshotProjection(Verify);
        VerifyQueryFilterMatching(Verify);
        VerifyHighPerformanceSearch100kBenchmark(Verify);
        VerifyTransactionCsvExportRfc4180(Verify);
        VerifyCsvPreviewAndRowLimiting(Verify);
        VerifyZeroSecretsInExport(Verify);

        Console.WriteLine($"Transaction search projections & CSV export (M2-06): {checks} checks completed successfully.");
    }

    private static void VerifySearchEngineSnapshotProjection(Action<bool, string> verify)
    {
        var now = DateTimeOffset.UtcNow;
        var bookingDate = new DateOnly(2026, 10, 15);
        var valDate = new DateOnly(2026, 10, 16);

        // 1. Empty snapshot or null transactions doc
        var emptyAccounts = new StoreAccountsDocument(Guid.NewGuid(), Guid.NewGuid(), 1, [], [], [], []);
        var engineFromEmpty = TransactionSearchEngine.FromSnapshot(emptyAccounts, null);
        verify(engineFromEmpty.Count == 0, "Engine from null transactions doc has 0 items.");
        verify(engineFromEmpty.AllTransactions.Count == 0, "AllTransactions list is empty.");

        // 2. Rich snapshot with 3 transactions, multiple revisions (supersession), annotations, and links
        var acc1 = new StoreAccountEntry(100, "Girokonto Primary", "EUR");
        var acc2 = new StoreAccountEntry(200, "Sparkonto", "EUR");
        var binding1 = new StoreBindingEntry(
            110, 100, 150, 1, 1, "Active", "FinTs", "3.0", "https://localhost",
            "DE89370400440532013000", null, null, null, "EUR", []);
        var binding2 = new StoreBindingEntry(
            210, 200, 250, 1, 1, "Active", "FinTs", "3.0", "https://localhost",
            "DE89370400440532013999", null, null, null, "EUR", []);

        var accountsDoc = new StoreAccountsDocument(
            Guid.NewGuid(), Guid.NewGuid(), 1,
            [acc1, acc2], [], [binding1, binding2], []);

        var obs1 = new StoreTransactionObservationEntry(10, 110, now.AddHours(-2), "camt.053", "2026-01", null);
        var obs2 = new StoreTransactionObservationEntry(20, 110, now.AddHours(-1), "camt.052", null, null);
        var obs3 = new StoreTransactionObservationEntry(30, 210, now, "MT940", "001", null);

        // Tx1: Booked transfer (with an older revision and a newer revision)
        var tx1 = new StoreTransactionEntry(1001, 100, 150, "Booked", "Transfer");
        var rev1Old = new StoreTransactionRevisionEntry(
            2001, 1001, 10, 100, 150, 110, "Booked", "Transfer",
            "1000.00", "EUR", true, bookingDate, valDate, now.AddHours(-3), now.AddHours(-3),
            "Old Counterparty", null, null, "Old Remittance", null, null, null, null, null, null, null, null);
        var rev1New = new StoreTransactionRevisionEntry(
            2002, 1001, 10, 100, 150, 110, "Booked", "Transfer",
            "1250.50", "EUR", true, bookingDate, valDate, now.AddHours(-2), now.AddHours(-2),
            "Arbeitgeber GmbH", "DE12500105170648489890", "HELAADEF100", "Gehalt Oktober 2026",
            "E2E-001", "MAND-1", "CRED-1", "CUST-1", "BANK-REF-1", "SALA", null, "166");

        var ann1 = new StoreAnnotationRevisionEntry(
            4001, 1001, now, "Monthly salary compensation", "Salary", ["Income", "Fixed"]);

        // Tx2: Pending pre-authorization (linked to Tx1)
        var tx2 = new StoreTransactionEntry(1002, 100, 150, "Pending", "Debit");
        var rev2 = new StoreTransactionRevisionEntry(
            2003, 1002, 20, 100, 150, 110, "Pending", "Debit",
            "45.00", "EUR", false, bookingDate, null, null, now.AddHours(-1),
            "Bäcker Müller", null, null, "Kartenzahlung", null, null, null, null, "CARD-REF-2", null, null, "005");

        // Tx3: Savings transfer
        var tx3 = new StoreTransactionEntry(1003, 200, 250, "Booked", "Credit");
        var rev3 = new StoreTransactionRevisionEntry(
            2004, 1003, 30, 200, 250, 210, "Booked", "Credit",
            "500.00", "EUR", true, bookingDate.AddDays(1), bookingDate.AddDays(1), now, now,
            "Internal Transfer", null, null, "Sparrate", null, null, null, null, "SAVE-REF-3", null, null, "051");

        // Pending to booked link between Tx2 and Tx1
        var link = new StorePendingToBookedLinkEntry(1002, 1001, now, "ExactBankReferenceMatch");

        var txDoc = new StoreTransactionsDocument(
            Guid.NewGuid(), Guid.NewGuid(), 2,
            [obs1, obs2, obs3],
            [tx1, tx2, tx3],
            [rev1Old, rev1New, rev2, rev3],
            [ann1],
            [link],
            []);

        var engine = TransactionSearchEngine.FromSnapshot(accountsDoc, txDoc);
        verify(engine.Count == 3, "Engine projected exactly 3 transactions.");

        // Verify Tx1 resolution
        var item1 = engine.AllTransactions.First(t => t.TransactionId == 1001);
        verify(item1.AccountId == 100, "Tx1 account ID is 100.");
        verify(item1.AccountDisplayName == "Girokonto Primary", "Tx1 account display name mapped.");
        verify(item1.AccountIban == "DE89370400440532013000", "Tx1 account IBAN mapped.");
        verify(item1.Amount == 1250.50m, "Tx1 projected latest revision amount 1250.50 (ignoring superseded 1000.00).");
        verify(item1.CounterpartyName == "Arbeitgeber GmbH", "Tx1 projected latest revision counterparty.");
        verify(item1.Category == "Salary", "Tx1 user annotation category projected.");
        verify(item1.UserNote == "Monthly salary compensation", "Tx1 user note projected.");
        verify(item1.Tags.Count == 2 && item1.Tags.Contains("Income"), "Tx1 tags projected.");
        verify(item1.LinkedPendingTransactionId == 1002, "Tx1 booked item has LinkedPendingTransactionId == 1002.");
        verify(item1.LinkedBookedTransactionId == null, "Tx1 booked item has null LinkedBookedTransactionId.");
        verify(item1.StatementFormat == "camt.053", "Tx1 statement format projected.");
        verify(item1.StatementNumber == "2026-01", "Tx1 statement number projected.");

        // Verify Tx2 resolution
        var item2 = engine.AllTransactions.First(t => t.TransactionId == 1002);
        verify(item2.Status == "Pending", "Tx2 status is Pending.");
        verify(item2.IsCredit == false, "Tx2 is debit.");
        verify(item2.Amount == 45.00m, "Tx2 amount is 45.00.");
        verify(item2.LinkedBookedTransactionId == 1001, "Tx2 pending item has LinkedBookedTransactionId == 1001.");
        verify(item2.LinkedPendingTransactionId == null, "Tx2 pending item has null LinkedPendingTransactionId.");

        // Verify ordering: Tx3 (bookingDate + 1) is first in AllTransactions
        verify(engine.AllTransactions[0].TransactionId == 1003, "AllTransactions ordered chronologically descending (Tx3 first).");
    }

    private static void VerifyQueryFilterMatching(Action<bool, string> verify)
    {
        var date = new DateOnly(2026, 10, 15);
        var now = DateTimeOffset.UtcNow;

        var sample = new TransactionItemRecord(
            TransactionId: 10,
            AccountId: 100,
            AccountDisplayName: "Checking",
            AccountIban: "DE89370400440532013000",
            AccountIncarnationId: 150,
            AccountBindingId: 110,
            Status: "Booked",
            Kind: "Debit",
            Amount: 75.50m,
            Currency: "EUR",
            IsCredit: false,
            BookingDate: date,
            ValueDate: date,
            BankTimestamp: now,
            RetrievedAt: now,
            CounterpartyName: "Rewe Supermarkt",
            CounterpartyIban: "DE1234567890",
            CounterpartyBic: "REWEBI100",
            RemittanceText: "Kartenzahlung Filiale 42 Essen",
            EndToEndId: "E2E-42",
            MandateId: null,
            CreditorId: null,
            CustomerReference: "CUST-9",
            BankReference: "BANK-42",
            PurposeCode: "POS",
            ReturnReasonCode: null,
            RawBankCode: "005",
            UserNote: "Weekly groceries",
            Category: "Groceries",
            Tags: ["Living", "Food"],
            StatementFormat: "camt.053",
            StatementNumber: "1",
            LinkedPendingTransactionId: null,
            LinkedBookedTransactionId: null);

        // 1. Account matching
        verify(new TransactionQueryFilter(AccountId: 100).Matches(sample), "Matching AccountId succeeds.");
        verify(!new TransactionQueryFilter(AccountId: 200).Matches(sample), "Non-matching AccountId fails.");

        // 2. Date range matching
        verify(new TransactionQueryFilter(FromDate: date.AddDays(-1)).Matches(sample), "FromDate before booking matches.");
        verify(new TransactionQueryFilter(FromDate: date).Matches(sample), "FromDate equal to booking matches.");
        verify(!new TransactionQueryFilter(FromDate: date.AddDays(1)).Matches(sample), "FromDate after booking fails.");
        verify(new TransactionQueryFilter(ToDate: date.AddDays(1)).Matches(sample), "ToDate after booking matches.");
        verify(new TransactionQueryFilter(ToDate: date).Matches(sample), "ToDate equal to booking matches.");
        verify(!new TransactionQueryFilter(ToDate: date.AddDays(-1)).Matches(sample), "ToDate before booking fails.");

        // 3. Status matching
        verify(new TransactionQueryFilter(Status: "Booked").Matches(sample), "Status 'Booked' matches.");
        verify(new TransactionQueryFilter(Status: "booked").Matches(sample), "Status case-insensitivity matches.");
        verify(new TransactionQueryFilter(Status: "All").Matches(sample), "Status 'All' matches any status.");
        verify(!new TransactionQueryFilter(Status: "Pending").Matches(sample), "Status 'Pending' fails on booked.");

        // 4. Direction matching
        verify(new TransactionQueryFilter(IsCredit: false).Matches(sample), "IsCredit=false matches debit.");
        verify(!new TransactionQueryFilter(IsCredit: true).Matches(sample), "IsCredit=true fails on debit.");

        // 5. Amount bounds
        verify(new TransactionQueryFilter(MinAmount: 70m).Matches(sample), "MinAmount <= 75.50 matches.");
        verify(new TransactionQueryFilter(MinAmount: 75.50m).Matches(sample), "MinAmount == 75.50 matches.");
        verify(!new TransactionQueryFilter(MinAmount: 80m).Matches(sample), "MinAmount > 75.50 fails.");
        verify(new TransactionQueryFilter(MaxAmount: 80m).Matches(sample), "MaxAmount >= 75.50 matches.");
        verify(new TransactionQueryFilter(MaxAmount: 75.50m).Matches(sample), "MaxAmount == 75.50 matches.");
        verify(!new TransactionQueryFilter(MaxAmount: 70m).Matches(sample), "MaxAmount < 75.50 fails.");

        // 6. Category matching
        verify(new TransactionQueryFilter(Category: "Groceries").Matches(sample), "Category 'Groceries' matches.");
        verify(new TransactionQueryFilter(Category: "groceries").Matches(sample), "Category case-insensitivity matches.");
        verify(!new TransactionQueryFilter(Category: "Salary").Matches(sample), "Different category fails.");

        // 7. Tag matching
        verify(new TransactionQueryFilter(Tag: "Food").Matches(sample), "Tag 'Food' matches.");
        verify(new TransactionQueryFilter(Tag: "living").Matches(sample), "Tag case-insensitivity matches.");
        verify(!new TransactionQueryFilter(Tag: "Leisure").Matches(sample), "Different tag fails.");

        // 8. TextQuery token matching across SearchCorpus
        verify(new TransactionQueryFilter(TextQuery: "Rewe").Matches(sample), "Token 'Rewe' matches counterparty.");
        verify(new TransactionQueryFilter(TextQuery: "filiale 42").Matches(sample), "Multi-token 'filiale 42' matches remittance.");
        verify(new TransactionQueryFilter(TextQuery: "groceries weekly").Matches(sample), "Tokens across category and note match.");
        verify(new TransactionQueryFilter(TextQuery: "E2E-42").Matches(sample), "Token matches EndToEndId.");
        verify(!new TransactionQueryFilter(TextQuery: "Rewe Berlin").Matches(sample), "Multi-token with missing token 'Berlin' fails.");
    }

    private static void VerifyHighPerformanceSearch100kBenchmark(Action<bool, string> verify)
    {
        // Generate 100,000 realistic transactions in memory
        const int recordCount = 100_000;
        List<TransactionItemRecord> records = new(recordCount);

        string[] counterparties = ["Aldi Süd", "Rewe Markt", "Edeka", "Deutsche Bahn AG", "Arbeitgeber GmbH", "Vermieter GbR", "Stadtwerke", "Amazon EU"];
        string[] categories = ["Groceries", "Transport", "Salary", "Housing", "Utilities", "Shopping", "Other"];
        DateOnly baseDate = new(2026, 1, 1);
        DateTimeOffset now = DateTimeOffset.UtcNow;

        for (int i = 0; i < recordCount; i++)
        {
            ulong id = (ulong)(i + 1);
            ulong accId = (ulong)((i % 5) + 1);
            bool isCredit = (i % 10) == 0; // 10% credits
            string status = ((i + (int)accId) % 25) == 0 ? "Pending" : "Booked";
            decimal amount = 5.00m + (i % 500) + ((i % 100) * 0.01m);
            DateOnly dt = baseDate.AddDays(i % 365);
            string cp = counterparties[i % counterparties.Length];
            string cat = categories[i % categories.Length];
            string rem = $"Verwendungszweck Buchung #{id} Referenz {i * 13}";

            records.Add(new TransactionItemRecord(
                TransactionId: id,
                AccountId: accId,
                AccountDisplayName: $"Account #{accId}",
                AccountIban: $"DE8937040044053201300{accId}",
                AccountIncarnationId: accId + 10,
                AccountBindingId: accId + 20,
                Status: status,
                Kind: isCredit ? "Credit" : "Debit",
                Amount: amount,
                Currency: "EUR",
                IsCredit: isCredit,
                BookingDate: dt,
                ValueDate: dt,
                BankTimestamp: now,
                RetrievedAt: now,
                CounterpartyName: cp,
                CounterpartyIban: "DE12500105170648489890",
                CounterpartyBic: "HELAADEF100",
                RemittanceText: rem,
                EndToEndId: $"E2E-{id}",
                MandateId: null,
                CreditorId: null,
                CustomerReference: $"CUST-{id}",
                BankReference: $"BANK-{id}",
                PurposeCode: "POS",
                ReturnReasonCode: null,
                RawBankCode: "005",
                UserNote: (i % 50 == 0) ? "Tax deductible" : null,
                Category: cat,
                Tags: [(i % 2 == 0) ? "Regular" : "Occasional"],
                StatementFormat: "camt.053",
                StatementNumber: "1",
                LinkedPendingTransactionId: null,
                LinkedBookedTransactionId: null));
        }

        TransactionSearchEngine engine = new(records);
        verify(engine.Count == recordCount, "Search engine initialized with exactly 100,000 cached records.");

        // Warm up JIT and cache
        for (int w = 0; w < 5; w++)
        {
            _ = engine.Search(new TransactionQueryFilter(TextQuery: "Aldi"));
            _ = engine.Search(new TransactionQueryFilter(AccountId: 1, IsCredit: true));
        }

        // Benchmark 20 realistic queries
        List<double> elapsedMilliseconds = new(20);
        Stopwatch sw = new();

        TransactionQueryFilter[] testFilters =
        [
            new(TextQuery: "Aldi"),
            new(AccountId: 2, Status: "Booked"),
            new(IsCredit: true, MinAmount: 100m, MaxAmount: 300m),
            new(Category: "Groceries", TextQuery: "Rewe"),
            new(FromDate: baseDate.AddDays(30), ToDate: baseDate.AddDays(90)),
            new(TextQuery: "Buchung #500"),
            new(TextQuery: "Tax deductible"),
            new(Status: "Pending", AccountId: 3),
            new(Tag: "Regular", IsCredit: false),
            new(TextQuery: "Deutsche Bahn Transport"),
        ];

        for (int run = 0; run < 20; run++)
        {
            TransactionQueryFilter filter = testFilters[run % testFilters.Length];
            sw.Restart();
            var results = engine.Search(filter);
            sw.Stop();
            elapsedMilliseconds.Add(sw.Elapsed.TotalMilliseconds);
            verify(results.Count > 0, $"Benchmark filter run {run} produced matching results.");
        }

        // Calculate p95 percentile
        elapsedMilliseconds.Sort();
        int p95Index = (int)Math.Ceiling(elapsedMilliseconds.Count * 0.95) - 1;
        double p95Ms = elapsedMilliseconds[Math.Clamp(p95Index, 0, elapsedMilliseconds.Count - 1)];

        verify(p95Ms < 300.0, $"Warm local search p95 ({p95Ms:F2} ms) is strictly below 300 ms requirement with 100,000 cached records.");
    }

    private static void VerifyTransactionCsvExportRfc4180(Action<bool, string> verify)
    {
        var bookingDate = new DateOnly(2026, 10, 15);
        var valDate = new DateOnly(2026, 10, 16);
        var now = DateTimeOffset.UtcNow;

        var items = new List<TransactionItemRecord>
        {
            // Item 1: Standard credit item
            new(
                TransactionId: 1,
                AccountId: 100,
                AccountDisplayName: "Checking",
                AccountIban: "DE89370400440532013000",
                AccountIncarnationId: 150,
                AccountBindingId: 110,
                Status: "Booked",
                Kind: "Transfer",
                Amount: 1250.50m,
                Currency: "EUR",
                IsCredit: true,
                BookingDate: bookingDate,
                ValueDate: valDate,
                BankTimestamp: now,
                RetrievedAt: now,
                CounterpartyName: "Arbeitgeber GmbH",
                CounterpartyIban: "DE12500105170648489890",
                CounterpartyBic: "HELAADEF100",
                RemittanceText: "Gehalt 10/2026",
                EndToEndId: "E2E-1",
                MandateId: null,
                CreditorId: null,
                CustomerReference: "CUST-1",
                BankReference: "BANK-1",
                PurposeCode: "SALA",
                ReturnReasonCode: null,
                RawBankCode: "166",
                UserNote: "Monthly salary",
                Category: "Salary",
                Tags: ["Income", "Fixed"],
                StatementFormat: "camt.053",
                StatementNumber: "1",
                LinkedPendingTransactionId: null,
                LinkedBookedTransactionId: null),

            // Item 2: Item requiring RFC 4180 escaping (commas, quotes, newlines)
            new(
                TransactionId: 2,
                AccountId: 100,
                AccountDisplayName: "Checking",
                AccountIban: "DE89370400440532013000",
                AccountIncarnationId: 150,
                AccountBindingId: 110,
                Status: "Booked",
                Kind: "Debit",
                Amount: 49.99m,
                Currency: "EUR",
                IsCredit: false,
                BookingDate: bookingDate,
                ValueDate: null, // Null value date
                BankTimestamp: null,
                RetrievedAt: now,
                CounterpartyName: "Müller, Schmidt & \"Partner\" GbR", // Commas and quotes
                CounterpartyIban: "DE9876543210",
                CounterpartyBic: "TESTDEFFXXX",
                RemittanceText: "Rechnung 100\r\nZweite Zeile", // CRLF newline
                EndToEndId: null,
                MandateId: null,
                CreditorId: null,
                CustomerReference: null,
                BankReference: null,
                PurposeCode: null,
                ReturnReasonCode: null,
                RawBankCode: null,
                UserNote: "Bürobedarf, Notiz mit \"Anführungszeichen\"",
                Category: "Office",
                Tags: Array.Empty<string>(),
                StatementFormat: "camt.053",
                StatementNumber: null,
                LinkedPendingTransactionId: null,
                LinkedBookedTransactionId: null),
        };

        string csv = TransactionCsvExporter.ExportToCsv(items);
        verify(csv.EndsWith("\r\n", StringComparison.Ordinal), "CSV ends with RFC 4180 CRLF.");

        string[] lines = csv.Split("\r\n", StringSplitOptions.RemoveEmptyEntries);
        // Header + 2 data lines (note: line with internal CRLF inside quotes stays single RFC record)
        verify(csv.StartsWith("BookingDate,ValueDate,Status,Account,AccountIban,CounterpartyName,CounterpartyIban,CounterpartyBic,Amount,Currency,Direction,RemittanceText,EndToEndId,MandateId,CreditorId,CustomerReference,BankReference,PurposeCode,ReturnReasonCode,RawBankCode,Category,Tags,UserNote\r\n", StringComparison.Ordinal), "Exact header row matches RFC 4180 definition.");

        // Item 1 formatting
        verify(csv.Contains("2026-10-15,2026-10-16,Booked,Checking,DE89370400440532013000,Arbeitgeber GmbH,DE12500105170648489890,HELAADEF100,1250.50,EUR,Credit,Gehalt 10/2026,E2E-1,,,CUST-1,BANK-1,SALA,,166,Salary,Income;Fixed,Monthly salary"), "Item 1 exported with exact decimal money, ISO dates, and semicolon tags.");

        // Item 2 escaping
        verify(csv.Contains("\"Müller, Schmidt & \"\"Partner\"\" GbR\""), "Counterparty containing commas and quotes properly escaped with doubled quotes.");
        verify(csv.Contains("\"Rechnung 100\r\nZweite Zeile\""), "Remittance containing CRLF newline properly wrapped in RFC 4180 quotes.");
        verify(csv.Contains("49.99,EUR,Debit"), "Debit direction and exact decimal 49.99 preserved.");
    }

    private static void VerifyCsvPreviewAndRowLimiting(Action<bool, string> verify)
    {
        List<TransactionItemRecord> items = new(20);
        DateOnly dt = new(2026, 10, 15);
        DateTimeOffset now = DateTimeOffset.UtcNow;

        for (int i = 0; i < 20; i++)
        {
            items.Add(new TransactionItemRecord(
                TransactionId: (ulong)(i + 1),
                AccountId: 100,
                AccountDisplayName: "Checking",
                AccountIban: "DE89370400440532013000",
                AccountIncarnationId: 150,
                AccountBindingId: 110,
                Status: "Booked",
                Kind: "Debit",
                Amount: (i + 1) * 10m,
                Currency: "EUR",
                IsCredit: false,
                BookingDate: dt,
                ValueDate: dt,
                BankTimestamp: now,
                RetrievedAt: now,
                CounterpartyName: $"Shop #{i + 1}",
                CounterpartyIban: null,
                CounterpartyBic: null,
                RemittanceText: $"Purchase #{i + 1}",
                EndToEndId: null,
                MandateId: null,
                CreditorId: null,
                CustomerReference: null,
                BankReference: null,
                PurposeCode: null,
                ReturnReasonCode: null,
                RawBankCode: null,
                UserNote: null,
                Category: null,
                Tags: Array.Empty<string>(),
                StatementFormat: "camt.053",
                StatementNumber: "1",
                LinkedPendingTransactionId: null,
                LinkedBookedTransactionId: null));
        }

        // Preview with maxRows = 5
        (string preview5, int total5) = TransactionCsvExporter.GeneratePreview(items, maxRows: 5);
        verify(total5 == 20, "Total count is accurately reported as 20.");
        string[] preview5Lines = preview5.Split("\r\n", StringSplitOptions.RemoveEmptyEntries);
        verify(preview5Lines.Length == 6, "Preview with maxRows=5 contains exactly 1 header + 5 data rows = 6 lines.");

        // Preview with maxRows = 1
        (string preview1, int total1) = TransactionCsvExporter.GeneratePreview(items, maxRows: 1);
        verify(total1 == 20, "Total count is 20.");
        string[] preview1Lines = preview1.Split("\r\n", StringSplitOptions.RemoveEmptyEntries);
        verify(preview1Lines.Length == 2, "Preview with maxRows=1 contains exactly 1 header + 1 data row = 2 lines.");

        // Negative bounds check
        bool caught = false;
        try
        {
            TransactionCsvExporter.GeneratePreview(items, maxRows: 0);
        }
        catch (ArgumentOutOfRangeException)
        {
            caught = true;
        }
        verify(caught, "maxRows < 1 throws ArgumentOutOfRangeException.");
    }

    private static void VerifyZeroSecretsInExport(Action<bool, string> verify)
    {
        var bookingDate = new DateOnly(2026, 10, 15);
        var now = DateTimeOffset.UtcNow;

        var items = new List<TransactionItemRecord>
        {
            new(
                TransactionId: 1,
                AccountId: 100,
                AccountDisplayName: "Checking",
                AccountIban: "DE89370400440532013000",
                AccountIncarnationId: 150,
                AccountBindingId: 110,
                Status: "Booked",
                Kind: "Transfer",
                Amount: 50.00m,
                Currency: "EUR",
                IsCredit: true,
                BookingDate: bookingDate,
                ValueDate: bookingDate,
                BankTimestamp: now,
                RetrievedAt: now,
                CounterpartyName: "Test Merchant",
                CounterpartyIban: "DE1234567890",
                CounterpartyBic: "TESTDEFFXXX",
                RemittanceText: "Payment for goods",
                EndToEndId: null,
                MandateId: null,
                CreditorId: null,
                CustomerReference: null,
                BankReference: null,
                PurposeCode: null,
                ReturnReasonCode: null,
                RawBankCode: null,
                UserNote: null,
                Category: null,
                Tags: Array.Empty<string>(),
                StatementFormat: "camt.053",
                StatementNumber: "1",
                LinkedPendingTransactionId: null,
                LinkedBookedTransactionId: null),
        };

        string csv = TransactionCsvExporter.ExportToCsv(items);

        verify(!csv.Contains("PIN", StringComparison.Ordinal), "Export contains zero PIN references.");
        verify(!csv.Contains("Passphrase", StringComparison.OrdinalIgnoreCase), "Export contains zero Passphrase references.");
        verify(!csv.Contains("ChallengeTan", StringComparison.OrdinalIgnoreCase), "Export contains zero ChallengeTan references.");
        verify(!csv.Contains("Secret", StringComparison.OrdinalIgnoreCase), "Export contains zero Secret references.");
    }
}
