using System.Security.Cryptography;
using System.Text;
using Broiler.Fond.Kernel.Domain;
using Broiler.Fond.Kernel.Domain.Accounts;
using Broiler.Fond.Kernel.Domain.Connections;
using Broiler.Fond.Kernel.Domain.Identifiers;
using Broiler.Fond.Kernel.Domain.Transactions.Import;
using Broiler.Fond.Kernel.Storage;

namespace Broiler.Fond.Kernel.Tests;

public static class OfflineStatementImportTests
{
    private static int _checksPassed;

    public static void RunAll()
    {
        _checksPassed = 0;

        TestFormatDetection_Camt();
        TestFormatDetection_SwiftMt();
        TestFormatDetection_HostileAndUnknown();
        TestFormatDetection_StreamAndBytes();
        TestMt940Import_EndToEnd();
        TestCamt053Import_EndToEnd();
        TestOverlappingAndDuplicateRejection();
        TestPreviewOnlyMode();
        TestAccountMatchingVariants();
        TestErrorAndHostileInputRejection();

        Console.WriteLine($"Offline fallback statement file import (M3-04): {_checksPassed} checks completed successfully.");
    }

    private static void TestFormatDetection_Camt()
    {
        const string camt053Xml = """
            <?xml version="1.0" encoding="UTF-8"?>
            <Document xmlns="urn:iso:std:iso:20022:tech:xsd:camt.053.001.02">
                <BkToCstmrStmt>
                    <GrpHdr><MsgId>MSG001</MsgId></GrpHdr>
                </BkToCstmrStmt>
            </Document>
            """;

        const string camt053v8Xml = """
            <Document xmlns="urn:iso:std:iso:20022:tech:xsd:camt.053.001.08">
                <BkToCstmrStmt>
                    <GrpHdr><MsgId>MSG002</MsgId></GrpHdr>
                </BkToCstmrStmt>
            </Document>
            """;

        const string camt052Xml = """
            <?xml version="1.0" encoding="UTF-8"?>
            <Document xmlns="urn:iso:std:iso:20022:tech:xsd:camt.052.001.02">
                <BkToCstmrAcctRpt>
                    <GrpHdr><MsgId>MSG003</MsgId></GrpHdr>
                </BkToCstmrAcctRpt>
            </Document>
            """;

        Assert(StatementFileDetector.Detect(camt053Xml) == StatementFileFormat.Camt053, "Detect camt.053.001.02");
        Assert(StatementFileDetector.Detect(camt053v8Xml) == StatementFileFormat.Camt053, "Detect camt.053.001.08");
        Assert(StatementFileDetector.Detect(camt052Xml) == StatementFileFormat.Camt052, "Detect camt.052.001.02");
    }

    private static void TestFormatDetection_SwiftMt()
    {
        const string mt940Text = """
            :20:TRN12345
            :25:DE89370400440532013000
            :28C:00001/001
            :60F:C260401EUR1000,00
            :61:2604150415CD50,00NTRFNONREF//END2END123
            :86:REMITTANCE INFO
            :62F:C260415EUR950,00
            -
            """;

        const string mt942Text = """
            {1:F01TESTDEFFXXX0000000000}{2:I942TESTDEFFXXXXN}{4:
            :20:INTRA999
            :25:DE89370400440532013000
            :34F:EUR500,00
            :61:2604150415CD25,00NTRFNONREF//END2END456
            :86:INTRADAY ITEM
            -}
            """;

        Assert(StatementFileDetector.Detect(mt940Text) == StatementFileFormat.Mt940, "Detect MT940 statement");
        Assert(StatementFileDetector.Detect(mt942Text) == StatementFileFormat.Mt942, "Detect MT942 report");
    }

    private static void TestFormatDetection_HostileAndUnknown()
    {
        Assert(StatementFileDetector.Detect("") == StatementFileFormat.Unknown, "Empty string is unknown");
        Assert(StatementFileDetector.Detect("   \r\n  \t ") == StatementFileFormat.Unknown, "Whitespace is unknown");
        Assert(StatementFileDetector.Detect("Hello World") == StatementFileFormat.Unknown, "Arbitrary text is unknown");
        Assert(StatementFileDetector.Detect("<?xml version=\"1.0\"?><root><item>123</item></root>") == StatementFileFormat.Unknown, "Non-CAMT XML is unknown");
        Assert(StatementFileDetector.Detect("<Document xmlns=\"urn:iso:std:iso:20022:tech:xsd:pain.001.001.03\" />") == StatementFileFormat.Unknown, "PAIN XML is unknown");
    }

    private static void TestFormatDetection_StreamAndBytes()
    {
        const string xml = "<Document xmlns=\"urn:iso:std:iso:20022:tech:xsd:camt.053.001.02\"><BkToCstmrStmt/></Document>";
        byte[] utf8Bytes = Encoding.UTF8.GetBytes(xml);
        byte[] bomBytes = [0xEF, 0xBB, 0xBF, .. utf8Bytes];

        Assert(StatementFileDetector.Detect(utf8Bytes) == StatementFileFormat.Camt053, "Detect from byte span");
        Assert(StatementFileDetector.Detect(bomBytes) == StatementFileFormat.Camt053, "Detect from byte span with UTF-8 BOM");

        using MemoryStream ms = new(utf8Bytes);
        Assert(StatementFileDetector.Detect(ms) == StatementFileFormat.Camt053, "Detect from stream");
        Assert(ms.Position == 0, "Stream position preserved after detection");
    }

    private static void TestMt940Import_EndToEnd()
    {
        string dir = Path.Combine(Path.GetTempPath(), $"bf-test-mt940-{Guid.NewGuid():N}");
        try
        {
            using ProfileStore store = CreateSeededProfile(dir);

            const string mt940 = """
                :20:STMT-2026-001
                :25:DE89370400440532013000
                :28C:001/01
                :60F:C260401EUR1000,00
                :61:2604100410D120,50NTRFNONREF//E2E-MT-01
                :86:?00Einkauf Supermarkt?32Supermarkt GmbH?31DE11222233334444555566
                :61:2604120412C500,00NTRFNONREF//E2E-MT-02
                :86:?00Gehalt April?32Arbeitgeber AG
                :62F:C260415EUR1379,50
                -
                """;

            var result = StatementFileImporter.Import(store, mt940);

            Assert(result.IsSuccess, "MT940 import must succeed");
            Assert(result.DetectedFormat == StatementFileFormat.Mt940, "Detected format must be MT940");
            Assert(result.TransactionsImportedCount == 2, "2 transactions must be imported");
            Assert(result.TransactionsMatchedCount == 0, "0 transactions matched on initial import");
            Assert(result.AccountId.HasValue, "Matched AccountId must be populated");
            Assert(result.AccountBindingId.HasValue, "Matched AccountBindingId must be populated");

            // Verify store state and invariants
            StoreSnapshot snapshot = store.CurrentSnapshot!;
            Assert(snapshot.Transactions != null, "Transactions document must be present in snapshot");
            Assert(snapshot.Transactions!.Transactions.Count == 2, "Snapshot has 2 transactions");
            Assert(snapshot.Transactions.Revisions.Count == 2, "Snapshot has 2 revisions");
            Assert(snapshot.Transactions.Observations.Count == 2, "Snapshot has 2 observations");

            var obs = snapshot.Transactions.Observations[0];
            Assert(obs.StatementFormat == "FileImport:MT940", "Observation StatementFormat is FileImport:MT940");
            Assert(obs.StatementNumber == "001", "Statement number matches 28C");
            Assert(obs.AccountBindingId == result.AccountBindingId!.Value.Value, "AccountBindingId matches");

            // Verify ledger integrity
            var ledgerRecords = snapshot.Ledger.Records;
            Assert(ledgerRecords.Any(r => r.Id == obs.Id && r.Kind == "Observation"), "Ledger has observation record");
            Assert(ledgerRecords.Any(r => r.Kind == "Transaction"), "Ledger has transaction records");
            Assert(ledgerRecords.Any(r => r.Kind == "TransactionRevision"), "Ledger has revision records");

            // Verify exact money
            var debitRev = snapshot.Transactions.Revisions.First(r => !r.IsCredit);
            Assert(debitRev.Amount == "120.50" && debitRev.Currency == "EUR", "Exact decimal debit amount 120.50 EUR");
            Assert(debitRev.CounterpartyName == "Supermarkt GmbH", "Counterparty name parsed");

            var creditRev = snapshot.Transactions.Revisions.First(r => r.IsCredit);
            Assert(creditRev.Amount == "500.00" && creditRev.Currency == "EUR", "Exact decimal credit amount 500.00 EUR");
        }
        finally
        {
            CleanupDirectory(dir);
        }
    }

    private static void TestCamt053Import_EndToEnd()
    {
        string dir = Path.Combine(Path.GetTempPath(), $"bf-test-camt-{Guid.NewGuid():N}");
        try
        {
            using ProfileStore store = CreateSeededProfile(dir);

            const string camtXml = """
                <?xml version="1.0" encoding="UTF-8"?>
                <Document xmlns="urn:iso:std:iso:20022:tech:xsd:camt.053.001.02">
                    <BkToCstmrStmt>
                        <GrpHdr>
                            <MsgId>MSG-2026-CAMT</MsgId>
                            <CreDtTm>2026-04-15T10:00:00Z</CreDtTm>
                        </GrpHdr>
                        <Stmt>
                            <Id>STMT-CAMT-01</Id>
                            <Acct>
                                <Id><IBAN>DE89370400440532013000</IBAN></Id>
                                <Ccy>EUR</Ccy>
                            </Acct>
                            <Bal>
                                <Tp><CdOrPrtry><Cd>OPBD</Cd></CdOrPrtry></Tp>
                                <Amt Ccy="EUR">2000.00</Amt>
                                <CdtDbtInd>CRDT</CdtDbtInd>
                                <Dt><Dt>2026-04-01</Dt></Dt>
                            </Bal>
                            <Ntry>
                                <Amt Ccy="EUR">45.00</Amt>
                                <CdtDbtInd>DBIT</CdtDbtInd>
                                <Sts>BOOK</Sts>
                                <BookgDt><Dt>2026-04-10</Dt></BookgDt>
                                <ValDt><Dt>2026-04-10</Dt></ValDt>
                                <NtryDtls>
                                    <TxDtls>
                                        <Refs>
                                            <EndToEndId>E2E-CAMT-01</EndToEndId>
                                        </Refs>
                                        <RltdPties>
                                            <Cdtr>
                                                <Nm>Buchhandlung Lesen</Nm>
                                            </Cdtr>
                                        </RltdPties>
                                        <RmtInf>
                                            <Ustrd>Fachbuch FinTS 3.0</Ustrd>
                                        </RmtInf>
                                    </TxDtls>
                                </NtryDtls>
                            </Ntry>
                            <Bal>
                                <Tp><CdOrPrtry><Cd>CLBD</Cd></CdOrPrtry></Tp>
                                <Amt Ccy="EUR">1955.00</Amt>
                                <CdtDbtInd>CRDT</CdtDbtInd>
                                <Dt><Dt>2026-04-15</Dt></Dt>
                            </Bal>
                        </Stmt>
                    </BkToCstmrStmt>
                </Document>
                """;

            var result = StatementFileImporter.Import(store, camtXml);

            Assert(result.IsSuccess, "CAMT import must succeed");
            Assert(result.DetectedFormat == StatementFileFormat.Camt053, "Detected format must be Camt053");
            Assert(result.TransactionsImportedCount == 1, "1 transaction imported");
            Assert(result.TransactionsMatchedCount == 0, "0 matched on initial import");

            StoreSnapshot snapshot = store.CurrentSnapshot!;
            var obs = snapshot.Transactions!.Observations[0];
            Assert(obs.StatementFormat == "FileImport:CAMT.053", "Provenance is FileImport:CAMT.053");
            Assert(obs.StatementNumber == "STMT-CAMT-01", "Statement ID captured");

            var rev = snapshot.Transactions.Revisions[0];
            Assert(rev.Amount == "45.00" && rev.Currency == "EUR", "Amount 45.00 EUR");
            Assert(!rev.IsCredit, "Entry is debit");
            Assert(rev.CounterpartyName == "Buchhandlung Lesen", "Counterparty name parsed");
            Assert(rev.RemittanceText == "Fachbuch FinTS 3.0", "Remittance text parsed");
        }
        finally
        {
            CleanupDirectory(dir);
        }
    }

    private static void TestOverlappingAndDuplicateRejection()
    {
        string dir = Path.Combine(Path.GetTempPath(), $"bf-test-overlap-{Guid.NewGuid():N}");
        try
        {
            using ProfileStore store = CreateSeededProfile(dir);

            const string mt940 = """
                :20:STMT-001
                :25:DE89370400440532013000
                :28C:001/01
                :60F:C260401EUR1000,00
                :61:2604100410D10,00NTRFNONREF//E2E-1
                :86:Item 1
                :61:2604110411D20,00NTRFNONREF//E2E-2
                :86:Item 2
                :62F:C260415EUR970,00
                -
                """;

            // Pass 1: Import 2 items
            var res1 = StatementFileImporter.Import(store, mt940);
            Assert(res1.IsSuccess && res1.TransactionsImportedCount == 2 && res1.TransactionsMatchedCount == 0, "Pass 1 imported 2 items");

            // Pass 2: Re-import exact same file -> 0 new transactions, 2 matched!
            var res2 = StatementFileImporter.Import(store, mt940);
            Assert(res2.IsSuccess, "Pass 2 must succeed");
            Assert(res2.TransactionsImportedCount == 0, "Zero duplicate transactions invariant on re-import");
            Assert(res2.TransactionsMatchedCount == 2, "Pass 2 matched both existing items");

            // Verify store still has exactly 2 transactions
            Assert(store.CurrentSnapshot!.Transactions!.Transactions.Count == 2, "Store still contains exactly 2 transactions");

            // Pass 3: Overlapping file with 1 existing item and 1 new item
            const string overlappingMt940 = """
                :20:STMT-002
                :25:DE89370400440532013000
                :28C:002/01
                :60F:C260411EUR970,00
                :61:2604110411D20,00NTRFNONREF//E2E-2
                :86:Item 2
                :61:2604120412D30,00NTRFNONREF//E2E-3
                :86:Item 3
                :62F:C260415EUR920,00
                -
                """;

            var res3 = StatementFileImporter.Import(store, overlappingMt940);
            Assert(res3.IsSuccess, "Pass 3 overlap import must succeed");
            Assert(res3.TransactionsImportedCount == 1, "Only 1 new item imported from overlap");
            Assert(res3.TransactionsMatchedCount == 1, "1 existing item matched in overlap");
            Assert(store.CurrentSnapshot.Transactions.Transactions.Count == 3, "Store now contains exactly 3 transactions");
        }
        finally
        {
            CleanupDirectory(dir);
        }
    }

    private static void TestPreviewOnlyMode()
    {
        string dir = Path.Combine(Path.GetTempPath(), $"bf-test-preview-{Guid.NewGuid():N}");
        try
        {
            using ProfileStore store = CreateSeededProfile(dir);
            ulong initialGen = store.CurrentGeneration;

            const string mt940 = """
                :20:PREVIEW-001
                :25:DE89370400440532013000
                :28C:001/01
                :60F:C260401EUR1000,00
                :61:2604100410D15,00NTRFNONREF//PREV-1
                :86:Preview Item
                :62F:C260415EUR985,00
                -
                """;

            var options = new StatementImportOptions { PreviewOnly = true };
            var result = StatementFileImporter.Import(store, mt940, options);

            Assert(result.IsSuccess, "Preview must succeed");
            Assert(result.TransactionsImportedCount == 1, "Preview calculates 1 imported");
            Assert(result.TransactionsMatchedCount == 0, "Preview calculates 0 matched");
            Assert(result.ResultingSnapshot == null, "ResultingSnapshot is null in PreviewOnly");

            // Store must be completely unmodified
            Assert(store.CurrentGeneration == initialGen, "Store generation not incremented in PreviewOnly");
            Assert(store.CurrentSnapshot!.Transactions == null || store.CurrentSnapshot.Transactions.Transactions.Count == 0, "Store has 0 transactions");
        }
        finally
        {
            CleanupDirectory(dir);
        }
    }

    private static void TestAccountMatchingVariants()
    {
        string dir = Path.Combine(Path.GetTempPath(), $"bf-test-acct-{Guid.NewGuid():N}");
        try
        {
            using ProfileStore store = CreateSeededProfile(dir);

            // 1. Domestic account number match
            const string mtByAccNum = """
                :20:ACCT-TEST-1
                :25:0532013000
                :28C:001/01
                :60F:C260401EUR1000,00
                :61:2604100410D10,00NTRFNONREF//T1
                :86:Test 1
                :62F:C260415EUR990,00
                -
                """;
            var resAccNum = StatementFileImporter.Import(store, mtByAccNum);
            Assert(resAccNum.IsSuccess, "Match by domestic account number succeeds");

            // 2. SWIFT slash format BLZ/AccNum match
            const string mtBySlash = """
                :20:ACCT-TEST-2
                :25:37040044/0532013000
                :28C:002/01
                :60F:C260401EUR990,00
                :61:2604100410D20,00NTRFNONREF//T2
                :86:Test 2
                :62F:C260415EUR970,00
                -
                """;
            var resSlash = StatementFileImporter.Import(store, mtBySlash);
            Assert(resSlash.IsSuccess, "Match by BLZ/AccNum slash format succeeds");

            // 3. Unmatched account fails closed
            const string mtUnmatched = """
                :20:ACCT-TEST-3
                :25:DE99999999999999999999
                :28C:003/01
                :60F:C260401EUR1000,00
                :61:2604100410D10,00NTRFNONREF//T3
                :86:Test 3
                :62F:C260415EUR990,00
                -
                """;
            var resUnmatched = StatementFileImporter.Import(store, mtUnmatched);
            Assert(!resUnmatched.IsSuccess, "Unmatched account fails closed");

            // 4. Explicit override with TargetAccountId allows importing to target account
            var explicitOpts = new StatementImportOptions
            {
                TargetAccountId = resAccNum.AccountId,
            };
            var resOverride = StatementFileImporter.Import(store, mtUnmatched, explicitOpts);
            Assert(resOverride.IsSuccess, "Explicit TargetAccountId override succeeds for unmatched account ID");
        }
        finally
        {
            CleanupDirectory(dir);
        }
    }

    private static void TestErrorAndHostileInputRejection()
    {
        string dir = Path.Combine(Path.GetTempPath(), $"bf-test-hostile-{Guid.NewGuid():N}");
        try
        {
            using ProfileStore store = CreateSeededProfile(dir);

            // Empty bytes
            var resEmpty = StatementFileImporter.Import(store, ReadOnlySpan<byte>.Empty);
            Assert(!resEmpty.IsSuccess, "Empty bytes rejected");

            // Garbage binary
            byte[] garbage = new byte[1024];
            RandomNumberGenerator.Fill(garbage);
            var resGarbage = StatementFileImporter.Import(store, garbage);
            Assert(!resGarbage.IsSuccess, "Garbage binary rejected");

            // Corrupted MT940 (missing :25: account identification)
            const string corruptedMt = """
                :20:TRN001
                :60F:C260401EUR1000,00
                :61:2604100410D10,00NTRFNONREF//T1
                :62F:C260415EUR990,00
                -
                """;
            var resCorruptMt = StatementFileImporter.Import(store, corruptedMt);
            Assert(!resCorruptMt.IsSuccess, "Corrupted MT940 missing :25: rejected");

            // Corrupted CAMT XML (broken tags)
            const string corruptedCamt = """
                <Document xmlns="urn:iso:std:iso:20022:tech:xsd:camt.053.001.02">
                    <BkToCstmrStmt><GrpHdr><MsgId>UNCLOSED
                """;
            var resCorruptCamt = StatementFileImporter.Import(store, corruptedCamt);
            Assert(!resCorruptCamt.IsSuccess, "Corrupted CAMT XML rejected");
        }
        finally
        {
            CleanupDirectory(dir);
        }
    }

    private static ProfileStore CreateSeededProfile(string dir)
    {
        const string passphrase = "OfflineStatementTestPassphrase123!";
        ProfileStore store = ProfileStore.CreateNew(dir, passphrase, "OfflineTestProfile");

        // Seed with institution, connection, and account binding
        store.MutateAndSave(snapshot =>
        {
            ulong nextId = snapshot.Profile.NextId;
            ulong instId = nextId++;
            ulong connId = nextId++;
            ulong accId = nextId++;
            ulong incId = nextId++;
            ulong binId = nextId++;

            var inst = new StoreInstitutionEntry(
                Id: instId,
                DisplayName: "Test Sparkasse",
                Endpoint: "https://fints.sparkasse.de/fints",
                BankCode: "37040044",
                Bic: null,
                VerificationState: "Confirmed");

            var conn = new StoreConnectionEntry(
                Id: connId,
                InstitutionId: instId,
                UserId: "TESTUSER",
                CustomerId: null,
                State: "Connected",
                SystemId: "0",
                SystemStatus: "Synchronized",
                NextClientMessageNumber: 1,
                IsSynchronized: true,
                LastSuccessfulContact: DateTimeOffset.UtcNow,
                LastAttemptedContact: DateTimeOffset.UtcNow,
                ConnectorVersion: "3.0");

            var acc = new StoreAccountEntry(
                Id: accId,
                DisplayName: "Girokonto Test",
                Currency: "EUR");

            var inc = new StoreIncarnationEntry(
                Id: incId,
                AccountId: accId,
                LifetimeEvidence: "FinTsDiscovery");

            var bin = new StoreBindingEntry(
                Id: binId,
                AccountId: accId,
                IncarnationId: incId,
                InstitutionId: instId,
                ConnectionId: connId,
                State: "Active",
                Connector: "FinTs3",
                ConnectorVersion: "3.0",
                Endpoint: "https://fints.sparkasse.de/fints",
                Iban: "DE89370400440532013000",
                DomesticAccountNumber: "0532013000",
                DomesticBankCode: "37040044",
                Subaccount: null,
                Currency: "EUR",
                BankIdentifiers: []);

            var ledList = new List<StoreLedgerRecord>(snapshot.Ledger.Records)
            {
                new(instId, "Institution", null, []),
                new(connId, "Connection", null, [(instId, "Institution")]),
                new(accId, "Account", null, []),
                new(incId, "AccountIncarnation", null, [(accId, "Account")]),
                new(binId, "AccountBinding", null,
                [
                    (incId, "AccountIncarnation"),
                    (instId, "Institution"),
                    (connId, "Connection"),
                ]),
            };

            return snapshot with
            {
                Profile = snapshot.Profile with { NextId = nextId },
                Institutions = snapshot.Institutions with { Institutions = [inst] },
                Connections = snapshot.Connections with { Connections = [conn] },
                Accounts = snapshot.Accounts with
                {
                    Accounts = [acc],
                    Incarnations = [inc],
                    Bindings = [bin],
                },
                Ledger = snapshot.Ledger with { Records = ledList },
            };
        });

        return store;
    }

    private static void CleanupDirectory(string dir)
    {
        try
        {
            if (Directory.Exists(dir))
            {
                Directory.Delete(dir, recursive: true);
            }
        }
        catch
        {
            // Ignore cleanup errors in test temp folders
        }
    }

    private static void Assert(bool condition, string message)
    {
        if (!condition)
        {
            throw new InvalidOperationException($"Assertion failed: {message}");
        }

        _checksPassed++;
    }
}
