using System.Globalization;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Xml.Schema;
using Broiler.Fond.Kernel.Domain.Identifiers;
using Broiler.Fond.Kernel.Storage;

namespace Broiler.Fond.Kernel.Tests.Storage;

/// <summary>
/// Executable verification suite for M2-05 Transaction Ledger Storage Extension:
/// transactions-v1.xsd embedded schema validation, deterministic XML serialization,
/// byte-exact round-tripping, backward compatibility with legacy v1 profile containers,
/// hostile input and tamper detection, snapshot merge ID remapping, and production ProfileStore lifecycle.
/// </summary>
internal static class TransactionLedgerStorageTests
{
    private const string TestPassphrase = "CorrectHorseBatteryStaple-TxStore-2026!";

    internal static void Run(Action<bool, string> check)
    {
        int checks = 0;
        void Verify(bool condition, string message)
        {
            checks++;
            check(condition, message);
        }

        VerifyTransactionsSerializationAndDeserializationRoundTrip(Verify);
        VerifyFullStoreSnapshotWithTransactionsRoundTrip(Verify);
        VerifyLegacyStoreBackwardCompatibilityAndMigration(Verify);
        VerifyHostileInputAndSchemaViolations(Verify);
        VerifyIdentityAndSemanticInvariants(Verify);
        VerifyManifestTamperRejection(Verify);
        VerifySnapshotMergeWithTransactionsRemapping(Verify);
        VerifyProductionProfileStoreLifecycleWithTransactions(Verify);

        Console.WriteLine($"Transaction ledger storage extension (M2-05): {checks} checks completed successfully.");
    }

    private static void VerifyTransactionsSerializationAndDeserializationRoundTrip(Action<bool, string> verify)
    {
        Guid profileId = Guid.NewGuid();
        Guid branchId = Guid.NewGuid();
        ulong generation = 1;
        DateTimeOffset timestamp = new(2026, 10, 18, 14, 0, 0, TimeSpan.Zero);

        StoreTransactionsDocument doc = new(
            profileId,
            branchId,
            generation,
            Observations:
            [
                new(101, 9, timestamp, "MT940", "28C/001", "PAGE-01"),
                new(102, 9, timestamp.AddMinutes(5), "CAMT", null, null),
            ],
            Transactions:
            [
                new(103, 5, 7, "Booked", "Debit"),
                new(104, 5, 7, "Pending", "CardPayment"),
            ],
            Revisions:
            [
                new(105, 103, 101, 5, 7, 9,
                    Status: "Booked",
                    Kind: "Debit",
                    Amount: "123.45",
                    Currency: "EUR",
                    IsCredit: false,
                    BookingDate: new DateOnly(2026, 10, 17),
                    ValueDate: new DateOnly(2026, 10, 18),
                    BankTimestamp: timestamp.AddHours(-1),
                    RetrievedAt: timestamp,
                    CounterpartyName: "Supermarket SE",
                    CounterpartyIban: "DE02100100100123456789",
                    CounterpartyBic: "PBNKDEFFXXX",
                    RemittanceText: "Groceries and supplies",
                    EndToEndId: "E2E-REF-998877",
                    MandateId: "MAN-12345",
                    CreditorId: "DE98ZZZ09999999999",
                    CustomerReference: "CUST-REF-11",
                    BankReference: "BANK-REF-22",
                    PurposeCode: "GDSV",
                    ReturnReasonCode: null,
                    RawBankCode: "NTRF+123"),
                new(106, 104, 102, 5, 7, 9,
                    Status: "Pending",
                    Kind: "CardPayment",
                    Amount: "49.99",
                    Currency: "EUR",
                    IsCredit: false,
                    BookingDate: new DateOnly(2026, 10, 18),
                    ValueDate: null,
                    BankTimestamp: null,
                    RetrievedAt: timestamp.AddMinutes(5),
                    CounterpartyName: "Coffee Shop GmbH",
                    CounterpartyIban: null,
                    CounterpartyBic: null,
                    RemittanceText: "Latte and pastry",
                    EndToEndId: null,
                    MandateId: null,
                    CreditorId: null,
                    CustomerReference: null,
                    BankReference: null,
                    PurposeCode: null,
                    ReturnReasonCode: "AC04",
                    RawBankCode: "POS-DEB"),
            ],
            Annotations:
            [
                new(107, 103, timestamp, "Weekly grocery shopping", "Groceries", ["food", "supermarket"]),
                new(108, 104, timestamp.AddMinutes(5), null, null, []),
            ],
            Links:
            [
                new(104, 103, timestamp.AddMinutes(10), "EndToEndId"),
            ],
            Checkpoints:
            [
                new(5, 7, 9,
                    WindowStart: new DateOnly(2026, 9, 1),
                    WindowEnd: new DateOnly(2026, 10, 18),
                    LastSyncTimestamp: timestamp,
                    LastContinuationToken: "CONT-TOKEN-XYZ",
                    IsComplete: true,
                    ObservedTransactionCount: 2),
            ]);

        byte[] serialized = StoreArchiveReference.SerializeTransactions(doc);
        verify(serialized.Length > 0, "Transactions serialized to bytes.");

        string xmlText = Encoding.UTF8.GetString(serialized);
        verify(xmlText.Contains("<transactions xmlns=\"urn:broiler:fond:profile:1\" version=\"1\"", StringComparison.Ordinal), "Transactions root has correct namespace and version.");
        verify(xmlText.Contains("Supermarket SE", StringComparison.Ordinal), "Counterparty name serialized.");
        verify(xmlText.Contains("E2E-REF-998877", StringComparison.Ordinal), "EndToEndId serialized.");
        verify(xmlText.Contains("AC04", StringComparison.Ordinal), "Return reason code serialized.");
        verify(xmlText.Contains("Groceries", StringComparison.Ordinal), "User category serialized.");

        // Round-trip through complete snapshot to exercise ParseAndValidateTransactions
        StoreSnapshot fullSnapshot = BuildSnapshotWithTransactions(profileId, branchId, generation, doc);
        byte[] zipBytes = StoreArchiveReference.CreateStoreZip(fullSnapshot);
        StoreSnapshot recovered = StoreArchiveReference.ValidateAndParseStoreZip(zipBytes);

        verify(recovered.Transactions is not null, "Recovered transactions document is not null.");
        StoreTransactionsDocument recDoc = recovered.Transactions!;

        verify(recDoc.ProfileId == profileId, "Profile ID matches.");
        verify(recDoc.BranchId == branchId, "Branch ID matches.");
        verify(recDoc.Generation == generation, "Generation matches.");
        verify(recDoc.Observations.Count == 2, "Two observations recovered.");
        verify(recDoc.Observations[0].StatementFormat == "MT940", "Observation 0 format matches.");
        verify(recDoc.Observations[0].StatementNumber == "28C/001", "Observation 0 statement number matches.");
        verify(recDoc.Observations[0].PageToken == "PAGE-01", "Observation 0 page token matches.");
        verify(recDoc.Transactions.Count == 2, "Two transactions recovered.");
        verify(recDoc.Transactions[0].Status == "Booked", "Transaction 0 status matches.");
        verify(recDoc.Transactions[0].Kind == "Debit", "Transaction 0 kind matches.");
        verify(recDoc.Revisions.Count == 2, "Two revisions recovered.");
        verify(recDoc.Revisions[0].Amount == "123.45", "Revision 0 amount matches.");
        verify(recDoc.Revisions[0].CounterpartyName == "Supermarket SE", "Revision 0 counterparty matches.");
        verify(recDoc.Revisions[0].EndToEndId == "E2E-REF-998877", "Revision 0 EndToEndId matches.");
        verify(recDoc.Revisions[1].ReturnReasonCode == "AC04", "Revision 1 return reason matches.");
        verify(recDoc.Annotations.Count == 2, "Two annotations recovered.");
        verify(recDoc.Annotations[0].Category == "Groceries", "Annotation 0 category matches.");
        verify(recDoc.Annotations[0].Tags.Count == 2, "Annotation 0 has two tags.");
        verify(recDoc.Annotations[0].Tags[0] == "food", "Annotation 0 tag 0 matches.");
        verify(recDoc.Annotations[1].Tags.Count == 0, "Annotation 1 has zero tags.");
        verify(recDoc.Links.Count == 1, "One link recovered.");
        verify(recDoc.Links[0].PendingTransactionId == 104, "Link pending ID matches.");
        verify(recDoc.Links[0].BookedTransactionId == 103, "Link booked ID matches.");
        verify(recDoc.Links[0].MatchCriterion == "EndToEndId", "Link match criterion matches.");
        verify(recDoc.Checkpoints.Count == 1, "One checkpoint recovered.");
        verify(recDoc.Checkpoints[0].WindowStart == new DateOnly(2026, 9, 1), "Checkpoint window start matches.");
        verify(recDoc.Checkpoints[0].WindowEnd == new DateOnly(2026, 10, 18), "Checkpoint window end matches.");
        verify(recDoc.Checkpoints[0].IsComplete == true, "Checkpoint isComplete matches.");
        verify(recDoc.Checkpoints[0].ObservedTransactionCount == 2, "Checkpoint observed count matches.");

        byte[] reSerialized = StoreArchiveReference.SerializeTransactions(recDoc);
        verify(reSerialized.SequenceEqual(serialized), "Re-serialized transactions XML is byte-for-byte identical.");
    }

    private static void VerifyFullStoreSnapshotWithTransactionsRoundTrip(Action<bool, string> verify)
    {
        Guid profileId = Guid.NewGuid();
        Guid branchId = Guid.NewGuid();
        ulong generation = 1;

        StoreTransactionsDocument doc = BuildSampleTransactionsDocument(profileId, branchId, generation);
        StoreSnapshot original = BuildSnapshotWithTransactions(profileId, branchId, generation, doc);

        StoreArchiveReference.ValidateIdentityAndSemanticInvariants(original);
        verify(true, "Identity and semantic invariants validated successfully.");

        byte[] zipBytes = StoreArchiveReference.CreateStoreZip(original);
        verify(zipBytes.Length > 0, "Full store ZIP created.");

        // Verify ZIP entries: documents in canonical order
        using (MemoryStream ms = new(zipBytes))
        using (ZipArchive zip = new(ms, ZipArchiveMode.Read))
        {
            verify(zip.Entries.Count == StoreArchiveReference.StoreDocumentNames.Count, $"Store ZIP contains exactly {StoreArchiveReference.StoreDocumentNames.Count} entries.");
            for (int i = 0; i < StoreArchiveReference.StoreDocumentNames.Count; i++)
            {
                verify(zip.Entries[i].FullName == StoreArchiveReference.StoreDocumentNames[i],
                    $"Entry {i} is canonical '{StoreArchiveReference.StoreDocumentNames[i]}'.");
            }
        }

        StoreSnapshot recovered = StoreArchiveReference.ValidateAndParseStoreZip(zipBytes);
        verify(recovered.Transactions is not null, "Recovered snapshot transactions is not null.");
        verify(recovered.Transactions!.Transactions.Count == doc.Transactions.Count, "Transaction count preserved.");
        verify(recovered.Transactions!.Revisions.Count == doc.Revisions.Count, "Revision count preserved.");
        verify(recovered.Transactions!.Observations.Count == doc.Observations.Count, "Observation count preserved.");
    }

    private static void VerifyLegacyStoreBackwardCompatibilityAndMigration(Action<bool, string> verify)
    {
        Guid profileId = Guid.NewGuid();
        Guid branchId = Guid.NewGuid();
        ulong generation = 1;

        // Build legacy snapshot with 6 non-manifest documents (no transactions)
        StoreSnapshot legacySnap = BuildBaseSnapshot(profileId, branchId, generation);

        byte[] profileBytes = StoreArchiveReference.SerializeProfile(legacySnap.Profile);
        byte[] instBytes = StoreArchiveReference.SerializeInstitutions(legacySnap.Institutions);
        byte[] connBytes = StoreArchiveReference.SerializeConnections(legacySnap.Connections);
        byte[] accBytes = StoreArchiveReference.SerializeAccounts(legacySnap.Accounts);
        byte[] balBytes = StoreArchiveReference.SerializeBalances(legacySnap.Balances);
        byte[] ledBytes = StoreArchiveReference.SerializeLedger(legacySnap.Ledger);

        List<(string Name, byte[] Bytes)> manifestEntries =
        [
            ("profile.xml", profileBytes),
            ("institutions.xml", instBytes),
            ("connections.xml", connBytes),
            ("accounts.xml", accBytes),
            ("balances.xml", balBytes),
            ("ledger.xml", ledBytes),
        ];

        byte[] manifestBytes = StoreArchiveReference.SerializeManifest(profileId, branchId, generation, manifestEntries);

        byte[] legacyZipBytes = StoreArchiveReference.CreateZip(
            ("profile.xml", profileBytes),
            ("institutions.xml", instBytes),
            ("connections.xml", connBytes),
            ("accounts.xml", accBytes),
            ("balances.xml", balBytes),
            ("ledger.xml", ledBytes),
            ("manifest.xml", manifestBytes));

        // Verify that the ZIP contains only 7 entries (LegacyStoreDocumentNames)
        using (MemoryStream ms = new(legacyZipBytes))
        using (ZipArchive zip = new(ms, ZipArchiveMode.Read))
        {
            verify(zip.Entries.Count == 7, "Legacy ZIP contains exactly 7 entries.");
            for (int i = 0; i < 7; i++)
            {
                verify(zip.Entries[i].FullName == StoreArchiveReference.LegacyStoreDocumentNames[i],
                    $"Legacy entry {i} matches '{StoreArchiveReference.LegacyStoreDocumentNames[i]}'.");
            }
        }

        // 1. Parsing legacy ZIP must succeed and yield empty transactions
        StoreSnapshot recoveredLegacy = StoreArchiveReference.ValidateAndParseStoreZip(legacyZipBytes);
        verify(recoveredLegacy.Profile.ProfileId == profileId, "Legacy profile ID matches.");
        verify(recoveredLegacy.Transactions is not null, "Legacy snapshot Transactions is not null (TransactionsOrDefault).");
        verify(recoveredLegacy.Transactions!.Observations.Count == 0, "Legacy transactions has 0 observations.");
        verify(recoveredLegacy.Transactions!.Transactions.Count == 0, "Legacy transactions has 0 transactions.");
        verify(recoveredLegacy.Transactions!.Revisions.Count == 0, "Legacy transactions has 0 revisions.");

        // 2. Saving the recovered snapshot must automatically upgrade to current format
        byte[] upgradedZipBytes = StoreArchiveReference.CreateStoreZip(recoveredLegacy);
        using (MemoryStream ms = new(upgradedZipBytes))
        using (ZipArchive zip = new(ms, ZipArchiveMode.Read))
        {
            verify(zip.Entries.Count == StoreArchiveReference.StoreDocumentNames.Count, $"Upgraded ZIP contains {StoreArchiveReference.StoreDocumentNames.Count} entries including transactions.xml.");
            verify(zip.Entries.Any(e => e.FullName == "transactions.xml"), "Upgraded ZIP has transactions.xml.");
        }

        // 3. Parsing the upgraded ZIP must succeed cleanly
        StoreSnapshot parsedUpgraded = StoreArchiveReference.ValidateAndParseStoreZip(upgradedZipBytes);
        verify(parsedUpgraded.Profile.ProfileId == profileId, "Upgraded profile ID matches.");
        verify(parsedUpgraded.Transactions is not null, "Upgraded transactions is not null.");
    }

    private static void VerifyHostileInputAndSchemaViolations(Action<bool, string> verify)
    {
        Guid profileId = Guid.NewGuid();
        Guid branchId = Guid.NewGuid();
        ulong generation = 1;

        StoreTransactionsDocument doc = BuildSampleTransactionsDocument(profileId, branchId, generation);
        StoreSnapshot validSnapshot = BuildSnapshotWithTransactions(profileId, branchId, generation, doc);

        // 1. Inverted checkpoint date range (windowStart > windowEnd)
        bool caughtInverted = false;
        try
        {
            StoreTransactionsDocument invertedDoc = doc with
            {
                Checkpoints =
                [
                    new(5, 7, 9,
                        WindowStart: new DateOnly(2026, 10, 18),
                        WindowEnd: new DateOnly(2026, 9, 1), // Inverted!
                        DateTimeOffset.UtcNow, null, true, 1),
                ],
            };
            StoreSnapshot invertedSnap = BuildSnapshotWithTransactions(profileId, branchId, generation, invertedDoc);
            byte[] zip = StoreArchiveReference.CreateStoreZip(invertedSnap);
            StoreArchiveReference.ValidateAndParseStoreZip(zip);
        }
        catch (InvalidDataException ex) when (ex.Message.Contains("windowStart", StringComparison.OrdinalIgnoreCase))
        {
            caughtInverted = true;
        }
        verify(caughtInverted, "Inverted checkpoint date range rejected with InvalidDataException.");

        // 2. Lineage mismatch in transactions.xml: wrong profileId
        bool caughtLineageProfile = false;
        try
        {
            StoreTransactionsDocument wrongProfileDoc = doc with { ProfileId = Guid.NewGuid() };
            byte[] txBytes = StoreArchiveReference.SerializeTransactions(wrongProfileDoc);
            byte[] zip = TamperZipEntry(StoreArchiveReference.CreateStoreZip(validSnapshot), "transactions.xml", txBytes);
            StoreArchiveReference.ValidateAndParseStoreZip(zip);
        }
        catch (InvalidDataException ex) when (ex.Message.Contains("Lineage mismatch in transactions.xml", StringComparison.OrdinalIgnoreCase))
        {
            caughtLineageProfile = true;
        }
        verify(caughtLineageProfile, "Lineage mismatch (profileId) rejected with InvalidDataException.");

        // 3. Lineage mismatch in transactions.xml: wrong generation
        bool caughtLineageGen = false;
        try
        {
            StoreTransactionsDocument wrongGenDoc = doc with { Generation = 999 };
            byte[] txBytes = StoreArchiveReference.SerializeTransactions(wrongGenDoc);
            byte[] zip = TamperZipEntry(StoreArchiveReference.CreateStoreZip(validSnapshot), "transactions.xml", txBytes);
            StoreArchiveReference.ValidateAndParseStoreZip(zip);
        }
        catch (InvalidDataException ex) when (ex.Message.Contains("Lineage mismatch in transactions.xml", StringComparison.OrdinalIgnoreCase))
        {
            caughtLineageGen = true;
        }
        verify(caughtLineageGen, "Lineage mismatch (generation) rejected with InvalidDataException.");

        // 4. Schema violation: unknown element
        bool caughtUnknownElement = false;
        try
        {
            byte[] validTxBytes = StoreArchiveReference.SerializeTransactions(doc);
            string validXml = Encoding.UTF8.GetString(validTxBytes);
            string hostileXml = validXml.Replace("<statementFormat>", "<unknownElement>foo</unknownElement><statementFormat>", StringComparison.Ordinal);
            byte[] hostileTxBytes = Encoding.UTF8.GetBytes(hostileXml);
            byte[] zip = TamperZipEntry(StoreArchiveReference.CreateStoreZip(validSnapshot), "transactions.xml", hostileTxBytes);
            StoreArchiveReference.ValidateAndParseStoreZip(zip);
        }
        catch (XmlSchemaValidationException)
        {
            caughtUnknownElement = true;
        }
        catch (InvalidDataException ex) when (ex.InnerException is XmlSchemaValidationException)
        {
            caughtUnknownElement = true;
        }
        verify(caughtUnknownElement, "Schema violation (unknown element) rejected.");

        // 5. Schema violation: missing required attribute (id on observation)
        bool caughtMissingAttr = false;
        try
        {
            byte[] validTxBytes = StoreArchiveReference.SerializeTransactions(doc);
            string validXml = Encoding.UTF8.GetString(validTxBytes);
            string hostileXml = validXml.Replace(" id=\"101\"", "", StringComparison.Ordinal);
            byte[] hostileTxBytes = Encoding.UTF8.GetBytes(hostileXml);
            byte[] zip = TamperZipEntry(StoreArchiveReference.CreateStoreZip(validSnapshot), "transactions.xml", hostileTxBytes);
            StoreArchiveReference.ValidateAndParseStoreZip(zip);
        }
        catch (XmlSchemaValidationException)
        {
            caughtMissingAttr = true;
        }
        catch (InvalidDataException ex) when (ex.InnerException is XmlSchemaValidationException)
        {
            caughtMissingAttr = true;
        }
        verify(caughtMissingAttr, "Schema violation (missing required attribute id) rejected.");

        // 6. Non-canonical formatting (byte-exact round-trip divergence)
        bool caughtNonCanonical = false;
        try
        {
            byte[] validTxBytes = StoreArchiveReference.SerializeTransactions(doc);
            string validXml = Encoding.UTF8.GetString(validTxBytes);
            // Insert extra space before closing tag
            string divergentXml = validXml.Replace("</transactions>", " </transactions>", StringComparison.Ordinal);
            byte[] divergentTxBytes = Encoding.UTF8.GetBytes(divergentXml);
            byte[] zip = TamperZipEntry(StoreArchiveReference.CreateStoreZip(validSnapshot), "transactions.xml", divergentTxBytes);
            StoreArchiveReference.ValidateAndParseStoreZip(zip);
        }
        catch (InvalidDataException ex) when (ex.Message.Contains("Transactions XML failed byte-exact serialization comparison", StringComparison.OrdinalIgnoreCase))
        {
            caughtNonCanonical = true;
        }
        verify(caughtNonCanonical, "Non-canonical XML rejected by byte-exact serialization comparison.");
    }

    private static void VerifyIdentityAndSemanticInvariants(Action<bool, string> verify)
    {
        Guid profileId = Guid.NewGuid();
        Guid branchId = Guid.NewGuid();
        ulong generation = 1;

        StoreTransactionsDocument doc = BuildSampleTransactionsDocument(profileId, branchId, generation);
        StoreSnapshot validSnapshot = BuildSnapshotWithTransactions(profileId, branchId, generation, doc);

        // 1. Missing transaction in ledger
        bool caughtMissingTx = false;
        try
        {
            // Remove transaction 103 and dependent entities (105, 107) from ledger, but keep in transactions.xml
            StoreSnapshot brokenSnapshot = validSnapshot with
            {
                Ledger = validSnapshot.Ledger with
                {
                    Records = validSnapshot.Ledger.Records.Where(r => r.Id != 103 && r.Id != 105 && r.Id != 107).ToList(),
                },
            };
            StoreArchiveReference.ValidateIdentityAndSemanticInvariants(brokenSnapshot);
        }
        catch (InvalidDataException ex) when (ex.Message.Contains("missing from ledger.xml", StringComparison.OrdinalIgnoreCase))
        {
            caughtMissingTx = true;
        }
        verify(caughtMissingTx, "Transaction entity missing from ledger rejected.");

        // 2. Missing revision in ledger
        bool caughtMissingRev = false;
        try
        {
            StoreSnapshot brokenSnapshot = validSnapshot with
            {
                Ledger = validSnapshot.Ledger with
                {
                    Records = validSnapshot.Ledger.Records.Where(r => r.Id != 105).ToList(),
                },
            };
            StoreArchiveReference.ValidateIdentityAndSemanticInvariants(brokenSnapshot);
        }
        catch (InvalidDataException ex) when (ex.Message.Contains("missing from ledger.xml", StringComparison.OrdinalIgnoreCase))
        {
            caughtMissingRev = true;
        }
        verify(caughtMissingRev, "Transaction revision entity missing from ledger rejected.");

        // 3. Entity kind mismatch: observation claimed to be an Account in ledger
        bool caughtKindMismatch = false;
        try
        {
            StoreSnapshot brokenSnapshot = validSnapshot with
            {
                Ledger = validSnapshot.Ledger with
                {
                    Records = validSnapshot.Ledger.Records
                        .Select(r => r.Id == 101 ? new StoreLedgerRecord(101, "Account", null, []) : r)
                        .ToList(),
                },
            };
            StoreArchiveReference.ValidateIdentityAndSemanticInvariants(brokenSnapshot);
        }
        catch (InvalidDataException ex) when (ex.Message.Contains("kind", StringComparison.OrdinalIgnoreCase))
        {
            caughtKindMismatch = true;
        }
        verify(caughtKindMismatch, "Entity kind mismatch in ledger rejected.");

        // 4. Duplicate observation ID in transactions.xml
        bool caughtDupObs = false;
        try
        {
            StoreSnapshot brokenSnapshot = validSnapshot with
            {
                Transactions = validSnapshot.Transactions! with
                {
                    Observations = [validSnapshot.Transactions!.Observations[0], validSnapshot.Transactions!.Observations[0]],
                },
            };
            StoreArchiveReference.ValidateIdentityAndSemanticInvariants(brokenSnapshot);
        }
        catch (InvalidDataException ex) when (ex.Message.Contains("Duplicate observation ID", StringComparison.OrdinalIgnoreCase))
        {
            caughtDupObs = true;
        }
        verify(caughtDupObs, "Duplicate observation ID in transactions.xml rejected.");

        // 5. Duplicate transaction ID in transactions.xml
        bool caughtDupTx = false;
        try
        {
            StoreSnapshot brokenSnapshot = validSnapshot with
            {
                Transactions = validSnapshot.Transactions! with
                {
                    Transactions = [validSnapshot.Transactions!.Transactions[0], validSnapshot.Transactions!.Transactions[0]],
                },
            };
            StoreArchiveReference.ValidateIdentityAndSemanticInvariants(brokenSnapshot);
        }
        catch (InvalidDataException ex) when (ex.Message.Contains("Duplicate transaction ID", StringComparison.OrdinalIgnoreCase))
        {
            caughtDupTx = true;
        }
        verify(caughtDupTx, "Duplicate transaction ID in transactions.xml rejected.");
    }

    private static void VerifyManifestTamperRejection(Action<bool, string> verify)
    {
        Guid profileId = Guid.NewGuid();
        Guid branchId = Guid.NewGuid();
        ulong generation = 1;

        StoreTransactionsDocument doc = BuildSampleTransactionsDocument(profileId, branchId, generation);
        StoreSnapshot validSnapshot = BuildSnapshotWithTransactions(profileId, branchId, generation, doc);
        byte[] validZipBytes = StoreArchiveReference.CreateStoreZip(validSnapshot);

        // 1. Manifest digest mismatch for transactions.xml
        bool caughtTamperedTx = false;
        try
        {
            Dictionary<string, byte[]> entries = ExtractAllZipEntries(validZipBytes);
            byte[] txBytes = entries["transactions.xml"];
            string manifestXml = Encoding.UTF8.GetString(entries["manifest.xml"]);
            string realHash = Convert.ToHexStringLower(SHA256.HashData(txBytes));
            string tamperedManifestXml = manifestXml.Replace(realHash, "0000000000000000000000000000000000000000000000000000000000000000", StringComparison.Ordinal);
            entries["manifest.xml"] = Encoding.UTF8.GetBytes(tamperedManifestXml);
            byte[] tamperedZip = RecreateZipFromEntries(entries);
            StoreArchiveReference.ValidateAndParseStoreZip(tamperedZip);
        }
        catch (InvalidDataException ex) when (ex.Message.Contains("SHA-256 digest mismatch", StringComparison.OrdinalIgnoreCase))
        {
            caughtTamperedTx = true;
        }
        verify(caughtTamperedTx, "Tampered transactions.xml SHA-256 digest in manifest rejected.");

        // 2. Extra unlisted file in store ZIP
        bool caughtUnlistedFile = false;
        try
        {
            Dictionary<string, byte[]> entries = ExtractAllZipEntries(validZipBytes);
            entries["extra-hostile.xml"] = Encoding.UTF8.GetBytes("<hostile/>");
            byte[] tamperedZip = RecreateZipFromEntries(entries);
            StoreArchiveReference.ValidateAndParseStoreZip(tamperedZip);
        }
        catch (InvalidDataException)
        {
            caughtUnlistedFile = true;
        }
        verify(caughtUnlistedFile, "Unlisted extra entry in ZIP rejected by preflight.");
    }

    private static void VerifySnapshotMergeWithTransactionsRemapping(Action<bool, string> verify)
    {
        Guid targetProfileId = Guid.NewGuid();
        Guid targetBranchId = Guid.NewGuid();
        ulong targetGen = 2;

        Guid importedProfileId = Guid.NewGuid();
        Guid importedBranchId = Guid.NewGuid();
        ulong importedGen = 1;

        // Target snapshot has NextId = 500, with IDs 1..14
        StoreSnapshot target = BuildBaseSnapshot(targetProfileId, targetBranchId, targetGen, baseIdOffset: 0, nextId: 500);

        // Imported snapshot has NextId = 200, with transactions and IDs 1..108
        StoreTransactionsDocument importedTx = BuildSampleTransactionsDocument(importedProfileId, importedBranchId, importedGen);
        StoreSnapshot imported = BuildSnapshotWithTransactions(importedProfileId, importedBranchId, importedGen, importedTx, nextId: 200);

        DateTimeOffset mergeSavedAt = new(2026, 10, 18, 16, 0, 0, TimeSpan.Zero);
        StoreSnapshot merged = StoreArchiveReference.MergeSnapshotsWithIdRemapping(
            target, imported, targetProfileId, targetBranchId, targetGen + 1, mergeSavedAt);

        verify(merged.Profile.ProfileId == targetProfileId, "Merged profile ID matches target.");
        verify(merged.Profile.Generation == targetGen + 1, "Merged generation incremented.");
        verify(merged.Transactions is not null, "Merged transactions is not null.");

        // All imported transaction entity IDs must be >= target's NextId (500)
        foreach (StoreTransactionObservationEntry obs in merged.Transactions!.Observations)
        {
            verify(obs.Id >= 500, $"Merged observation ID {obs.Id} remapped to >= 500.");
            verify(obs.AccountBindingId >= 500, $"Merged observation AccountBindingId {obs.AccountBindingId} remapped to >= 500.");
        }

        foreach (StoreTransactionEntry tx in merged.Transactions.Transactions)
        {
            verify(tx.Id >= 500, $"Merged transaction ID {tx.Id} remapped to >= 500.");
            verify(tx.AccountId >= 500, $"Merged transaction AccountId {tx.AccountId} remapped to >= 500.");
            verify(tx.AccountIncarnationId >= 500, $"Merged transaction AccountIncarnationId {tx.AccountIncarnationId} remapped to >= 500.");
        }

        foreach (StoreTransactionRevisionEntry rev in merged.Transactions.Revisions)
        {
            verify(rev.Id >= 500, $"Merged revision ID {rev.Id} remapped to >= 500.");
            verify(rev.TransactionId >= 500, $"Merged revision TransactionId {rev.TransactionId} remapped to >= 500.");
            verify(rev.ObservationId >= 500, $"Merged revision ObservationId {rev.ObservationId} remapped to >= 500.");
        }

        foreach (StoreAnnotationRevisionEntry ann in merged.Transactions.Annotations)
        {
            verify(ann.Id >= 500, $"Merged annotation ID {ann.Id} remapped to >= 500.");
            verify(ann.TransactionId >= 500, $"Merged annotation TransactionId {ann.TransactionId} remapped to >= 500.");
        }

        foreach (StorePendingToBookedLinkEntry link in merged.Transactions.Links)
        {
            verify(link.PendingTransactionId >= 500, $"Merged link pending ID {link.PendingTransactionId} remapped to >= 500.");
            verify(link.BookedTransactionId >= 500, $"Merged link booked ID {link.BookedTransactionId} remapped to >= 500.");
        }

        foreach (StoreStatementSyncCheckpointEntry cp in merged.Transactions.Checkpoints)
        {
            verify(cp.AccountId >= 500, $"Merged checkpoint AccountId {cp.AccountId} remapped to >= 500.");
            verify(cp.AccountIncarnationId >= 500, $"Merged checkpoint AccountIncarnationId {cp.AccountIncarnationId} remapped to >= 500.");
            verify(cp.AccountBindingId >= 500, $"Merged checkpoint AccountBindingId {cp.AccountBindingId} remapped to >= 500.");
        }

        // Validate merged invariants
        StoreArchiveReference.ValidateIdentityAndSemanticInvariants(merged);
        verify(true, "Merged snapshot passes full identity and semantic validation.");
    }

    private static void VerifyProductionProfileStoreLifecycleWithTransactions(Action<bool, string> verify)
    {
        string dir = Path.Combine(Path.GetTempPath(), "BroilerFondTxLifecycleTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);

        try
        {
            Guid profileId = Guid.NewGuid();
            Guid branchId = Guid.NewGuid();
            ulong gen1 = 1;

            StoreTransactionsDocument doc1 = BuildSampleTransactionsDocument(profileId, branchId, gen1);
            StoreSnapshot snap1 = BuildSnapshotWithTransactions(profileId, branchId, gen1, doc1);

            // 1. Create new profile with Gen 1
            using (ProfileStore store = ProfileStore.CreateNew(
                dir, TestPassphrase, "Tx Profile", "Gen 1", profileId, branchId, kdfIterations: ProfileEnvelopeCodec.MinimumIterations))
            {
                verify(store.CurrentSnapshot is not null, "Store created with initial snapshot.");

                // Save Gen 2 with transactions
                ulong gen2 = 2;
                StoreTransactionsDocument doc2 = BuildSampleTransactionsDocument(profileId, branchId, gen2);
                StoreSnapshot snap2 = BuildSnapshotWithTransactions(profileId, branchId, gen2, doc2, nextId: 300);

                ProfileSaveMetrics metrics = store.Save(snap2);
                verify(metrics.Generation == 2, "Gen 2 successfully committed with transactions.");
                verify(store.CurrentSnapshot!.Transactions is not null, "Store snapshot has transactions.");
                verify(store.CurrentSnapshot!.Transactions!.Transactions.Count == 2, "Two transactions stored in Gen 2.");
            }

            // 2. Reopen profile and verify Gen 2 transactions survived
            using (ProfileStore reopenedStore = ProfileStore.Open(dir, TestPassphrase))
            {
                verify(reopenedStore.CurrentGeneration == 2, "Reopened store is at Generation 2.");
                StoreSnapshot reopened = reopenedStore.CurrentSnapshot!;
                verify(reopened.Transactions is not null, "Reopened snapshot has transactions.");
                verify(reopened.Transactions!.Transactions.Count == 2, "Two transactions present after reopen.");
                verify(reopened.Transactions!.Annotations.Count == 2, "Two annotations present after reopen.");

                // 3. Commit Gen 3 via Save with additional annotation
                ulong gen3 = 3;
                StoreTransactionsDocument doc3 = reopened.Transactions! with
                {
                    Generation = gen3,
                    Annotations =
                    [
                        .. reopened.Transactions!.Annotations,
                        new(109, 103, DateTimeOffset.UtcNow, "Tax deductible receipt", "Taxes", ["tax", "2026"]),
                    ],
                };
                StoreSnapshot snap3 = BuildSnapshotWithTransactions(profileId, branchId, gen3, doc3, nextId: 400);
                snap3 = snap3 with
                {
                    Ledger = snap3.Ledger with
                    {
                        Records =
                        [
                            .. snap3.Ledger.Records,
                            new StoreLedgerRecord(109, "AnnotationRevision", null, [(103, "Transaction")]),
                        ],
                    },
                };

                ProfileSaveMetrics m3 = reopenedStore.Save(snap3);
                verify(m3.Generation == 3, "Gen 3 committed with additional annotation.");
                verify(reopenedStore.CurrentSnapshot!.Transactions!.Annotations.Count == 3, "Three annotations in Gen 3.");

                // 4. Perform privacy purge to purge Gen 2 and test transactions preservation
                ulong gen4 = 4;
                StoreTransactionsDocument doc4 = doc3 with
                {
                    Generation = gen4,
                    Annotations = [doc3.Annotations[0]], // Purged annotations 108 and 109
                };
                StoreSnapshot snap4 = BuildSnapshotWithTransactions(profileId, branchId, gen4, doc4, nextId: 500);

                ProfileSaveMetrics purgeMetrics = reopenedStore.ExecutePrivacyPurge(
                    snap4, TestPassphrase, DateTimeOffset.UtcNow, "Privacy purge of superseded transaction annotations.");

                verify(purgeMetrics.Generation == 4, "Gen 4 committed via privacy purge.");
                verify(reopenedStore.CurrentGeneration == 4, "Active generation is 4.");
                verify(reopenedStore.CurrentSnapshot!.Transactions!.Annotations.Count == 1, "Only 1 annotation remains after purge.");
            }
        }
        finally
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
                // Best-effort temp dir cleanup
            }
        }
    }

    // ==========================================
    // Helper Methods
    // ==========================================

    private static StoreSnapshot BuildBaseSnapshot(
        Guid profileId,
        Guid branchId,
        ulong generation,
        ulong baseIdOffset = 0,
        ulong nextId = 100)
    {
        DateTimeOffset savedAt = new(2026, 10, 18, 12, 0, 0, TimeSpan.Zero);

        ulong idInst1 = 1 + baseIdOffset;
        ulong idInst2 = 2 + baseIdOffset;
        ulong idConn1 = 3 + baseIdOffset;
        ulong idConn2 = 4 + baseIdOffset;
        ulong idAcc1 = 5 + baseIdOffset;
        ulong idAcc2 = 6 + baseIdOffset;
        ulong idInc1 = 7 + baseIdOffset;
        ulong idInc2 = 8 + baseIdOffset;
        ulong idBin1 = 9 + baseIdOffset;
        ulong idBin2 = 10 + baseIdOffset;
        ulong idBal1 = 11 + baseIdOffset;
        ulong idBal2 = 12 + baseIdOffset;

        StoreProfileDocument profile = new(
            profileId, branchId, null, null, generation, nextId, savedAt,
            "Household Profile", "Primary profile.");

        StoreInstitutionsDocument institutions = new(
            profileId, branchId, generation,
            [
                new(idInst1, "Test Sparkasse", "https://fints.sparkasse-test.de/fints", "12345678", "GENODED1XYZ", "Confirmed"),
                new(idInst2, "Test Volksbank", "https://hbci.volksbank-test.de/hbci", "87654321", null, "Unconfirmed"),
            ]);

        StoreConnectionsDocument connections = new(
            profileId, branchId, generation,
            [
                new(idConn1, idInst1, "testuser01", "cust100", "Connected", "SYS12345678", "Synchronized", 1, true, savedAt, savedAt, "FinTS 3.0 PIN/TAN"),
                new(idConn2, idInst2, "testuser02", null, "Unconfigured", null, "Required", 1, false, null, null, "FinTS 3.0 PIN/TAN"),
            ]);

        StoreAccountsDocument accounts = new(
            profileId, branchId, generation,
            [
                new(idAcc1, "Girokonto", "EUR"),
                new(idAcc2, "Tagesgeld", "EUR"),
            ],
            [
                new(idInc1, idAcc1, "Active"),
                new(idInc2, idAcc2, "Active"),
            ],
            [
                new(idBin1, idAcc1, idInc1, idInst1, idConn1, "Active", "FinTS 3.0 PIN/TAN", "1.0", "https://fints.sparkasse-test.de/fints",
                    "DE89370400440532013000", "0532013000", "37040044", "00", "EUR",
                    [("DE.SPARKASSE", "ACCOUNT", "INTERNAL", "INT-998877")]),
                new(idBin2, idAcc2, idInc2, idInst1, idConn1, "Active", "FinTS 3.0 PIN/TAN", "1.0", "https://fints.sparkasse-test.de/fints",
                    "DE89370400440532013001", "0532013001", "37040044", "01", "EUR", []),
            ],
            [
                new(idConn1, idAcc1, "HKSAL", 7, "Active", "10000.00"),
                new(idConn1, idAcc1, "HKSPA", 1, "Active", null),
            ]);

        StoreBalancesDocument balances = new(
            profileId, branchId, generation,
            [
                new(idBal1, 20 + baseIdOffset, idAcc1, idInc1, idBin1, "Booked", "1543.21", "EUR", savedAt, savedAt, false, "SALDO"),
                new(idBal2, 21 + baseIdOffset, idAcc2, idInc2, idBin2, "Booked", "50000.00", "EUR", savedAt, savedAt, false, null),
            ]);

        StoreLedgerDocument ledger = new(
            profileId, branchId, generation,
            [
                new(idInst1, "Institution", null, []),
                new(idInst2, "Institution", null, []),
                new(idConn1, "Connection", null, [(idInst1, "Institution")]),
                new(idConn2, "Connection", null, [(idInst2, "Institution")]),
                new(idAcc1, "Account", null, []),
                new(idAcc2, "Account", null, []),
                new(idInc1, "AccountIncarnation", null, [(idAcc1, "Account")]),
                new(idInc2, "AccountIncarnation", null, [(idAcc2, "Account")]),
                new(idBin1, "AccountBinding", null, [(idInc1, "AccountIncarnation"), (idConn1, "Connection")]),
                new(idBin2, "AccountBinding", null, [(idInc2, "AccountIncarnation"), (idConn1, "Connection")]),
                new(idBal1, "BalanceSnapshot", null, [(idBin1, "AccountBinding")]),
                new(idBal2, "BalanceSnapshot", null, [(idBin2, "AccountBinding")]),
            ]);

        return new StoreSnapshot(profile, institutions, connections, accounts, balances, ledger);
    }

    private static StoreTransactionsDocument BuildSampleTransactionsDocument(
        Guid profileId,
        Guid branchId,
        ulong generation)
    {
        DateTimeOffset timestamp = new(2026, 10, 18, 14, 0, 0, TimeSpan.Zero);

        return new StoreTransactionsDocument(
            profileId,
            branchId,
            generation,
            Observations:
            [
                new(101, 9, timestamp, "MT940", "28C/001", "PAGE-01"),
                new(102, 9, timestamp.AddMinutes(5), "CAMT", null, null),
            ],
            Transactions:
            [
                new(103, 5, 7, "Booked", "Debit"),
                new(104, 5, 7, "Pending", "CardPayment"),
            ],
            Revisions:
            [
                new(105, 103, 101, 5, 7, 9,
                    Status: "Booked",
                    Kind: "Debit",
                    Amount: "123.45",
                    Currency: "EUR",
                    IsCredit: false,
                    BookingDate: new DateOnly(2026, 10, 17),
                    ValueDate: new DateOnly(2026, 10, 18),
                    BankTimestamp: timestamp.AddHours(-1),
                    RetrievedAt: timestamp,
                    CounterpartyName: "Supermarket SE",
                    CounterpartyIban: "DE02100100100123456789",
                    CounterpartyBic: "PBNKDEFFXXX",
                    RemittanceText: "Groceries and supplies",
                    EndToEndId: "E2E-REF-998877",
                    MandateId: "MAN-12345",
                    CreditorId: "DE98ZZZ09999999999",
                    CustomerReference: "CUST-REF-11",
                    BankReference: "BANK-REF-22",
                    PurposeCode: "GDSV",
                    ReturnReasonCode: null,
                    RawBankCode: "NTRF+123"),
                new(106, 104, 102, 5, 7, 9,
                    Status: "Pending",
                    Kind: "CardPayment",
                    Amount: "49.99",
                    Currency: "EUR",
                    IsCredit: false,
                    BookingDate: new DateOnly(2026, 10, 18),
                    ValueDate: null,
                    BankTimestamp: null,
                    RetrievedAt: timestamp.AddMinutes(5),
                    CounterpartyName: "Coffee Shop GmbH",
                    CounterpartyIban: null,
                    CounterpartyBic: null,
                    RemittanceText: "Latte and pastry",
                    EndToEndId: null,
                    MandateId: null,
                    CreditorId: null,
                    CustomerReference: null,
                    BankReference: null,
                    PurposeCode: null,
                    ReturnReasonCode: "AC04",
                    RawBankCode: "POS-DEB"),
            ],
            Annotations:
            [
                new(107, 103, timestamp, "Weekly grocery shopping", "Groceries", ["food", "supermarket"]),
                new(108, 104, timestamp.AddMinutes(5), null, null, []),
            ],
            Links:
            [
                new(104, 103, timestamp.AddMinutes(10), "EndToEndId"),
            ],
            Checkpoints:
            [
                new(5, 7, 9,
                    WindowStart: new DateOnly(2026, 9, 1),
                    WindowEnd: new DateOnly(2026, 10, 18),
                    LastSyncTimestamp: timestamp,
                    LastContinuationToken: "CONT-TOKEN-XYZ",
                    IsComplete: true,
                    ObservedTransactionCount: 2),
            ]);
    }

    private static StoreSnapshot BuildSnapshotWithTransactions(
        Guid profileId,
        Guid branchId,
        ulong generation,
        StoreTransactionsDocument doc,
        ulong nextId = 200)
    {
        StoreSnapshot baseSnap = BuildBaseSnapshot(profileId, branchId, generation, nextId: nextId);

        // Add ledger records for all transaction entities:
        // Obs: 101, 102 -> Observation
        // Tx: 103, 104 -> Transaction
        // Rev: 105, 106 -> TransactionRevision
        // Ann: 107, 108 -> AnnotationRevision
        List<StoreLedgerRecord> txLedgerRecords =
        [
            new(101, "Observation", null, [(9, "AccountBinding")]),
            new(102, "Observation", null, [(9, "AccountBinding")]),
            new(103, "Transaction", null, [(5, "Account"), (7, "AccountIncarnation")]),
            new(104, "Transaction", null, [(5, "Account"), (7, "AccountIncarnation")]),
            new(105, "TransactionRevision", null, [(103, "Transaction"), (101, "Observation"), (5, "Account"), (7, "AccountIncarnation"), (9, "AccountBinding")]),
            new(106, "TransactionRevision", null, [(104, "Transaction"), (102, "Observation"), (5, "Account"), (7, "AccountIncarnation"), (9, "AccountBinding")]),
            new(107, "AnnotationRevision", null, [(103, "Transaction")]),
            new(108, "AnnotationRevision", null, [(104, "Transaction")]),
        ];

        return baseSnap with
        {
            Ledger = baseSnap.Ledger with
            {
                Records = [.. baseSnap.Ledger.Records, .. txLedgerRecords],
            },
            Transactions = doc,
        };
    }

    private static byte[] TamperZipEntry(byte[] zipBytes, string targetEntryName, byte[] replacementBytes)
    {
        Dictionary<string, byte[]> entries = ExtractAllZipEntries(zipBytes);
        entries[targetEntryName] = replacementBytes;

        // Recompute manifest
        Guid profileId = Guid.ParseExact(GetXmlAttribute(entries["profile.xml"], "profileId"), "D");
        Guid branchId = Guid.ParseExact(GetXmlAttribute(entries["profile.xml"], "branchId"), "D");
        ulong generation = ulong.Parse(GetXmlAttribute(entries["profile.xml"], "generation"), NumberStyles.None, CultureInfo.InvariantCulture);

        List<(string Name, byte[] Bytes)> manifestEntries = [];
        foreach (string name in StoreArchiveReference.StoreDocumentNames)
        {
            if (name != "manifest.xml" && entries.TryGetValue(name, out byte[]? docBytes))
            {
                manifestEntries.Add((name, docBytes));
            }
        }

        entries["manifest.xml"] = StoreArchiveReference.SerializeManifest(profileId, branchId, generation, manifestEntries);
        return RecreateZipFromEntries(entries);
    }

    private static Dictionary<string, byte[]> ExtractAllZipEntries(byte[] zipBytes)
    {
        Dictionary<string, byte[]> entries = [];
        using MemoryStream ms = new(zipBytes);
        using ZipArchive zip = new(ms, ZipArchiveMode.Read);
        foreach (ZipArchiveEntry entry in zip.Entries)
        {
            using Stream s = entry.Open();
            byte[] buf = new byte[(int)entry.Length];
            s.ReadExactly(buf);
            entries[entry.FullName] = buf;
        }

        return entries;
    }

    private static byte[] RecreateZipFromEntries(Dictionary<string, byte[]> entries)
    {
        using MemoryStream ms = new();
        using (ZipArchive zip = new(ms, ZipArchiveMode.Create, leaveOpen: true))
        {
            foreach ((string name, byte[] data) in entries)
            {
                ZipArchiveEntry entry = zip.CreateEntry(name, CompressionLevel.Optimal);
                using Stream s = entry.Open();
                s.Write(data);
            }
        }

        return ms.ToArray();
    }

    private static string GetXmlAttribute(byte[] xmlBytes, string attributeName)
    {
        string text = Encoding.UTF8.GetString(xmlBytes);
        int idx = text.IndexOf(attributeName + "=\"", StringComparison.Ordinal);
        if (idx < 0)
        {
            throw new InvalidOperationException($"Attribute '{attributeName}' not found in XML.");
        }

        int start = idx + attributeName.Length + 2;
        int end = text.IndexOf('"', start);
        return text[start..end];
    }
}
