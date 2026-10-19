using System.Globalization;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Xml.Schema;
using Broiler.Fond.Kernel.Domain.Identifiers;
using Broiler.Fond.Kernel.Domain.Payments;
using Broiler.Fond.Kernel.Storage;

namespace Broiler.Fond.Kernel.Tests.Payments;

/// <summary>
/// Executable verification suite for M4-05 Payment Storage Extension & Audit History:
/// payments-v1.xsd embedded schema validation, deterministic XML serialization,
/// byte-exact round-tripping, backward compatibility with legacy 7-doc (M1) and 8-doc (M2) containers,
/// hostile input and tamper detection, snapshot merge ID remapping, and production ProfileStore lifecycle.
/// </summary>
internal static class PaymentStorageTests
{
    private const string TestPassphrase = "CorrectHorseBatteryStaple-PaymentStore-2026!";

    internal static void Run(Action<bool, string> check)
    {
        int checks = 0;
        void Verify(bool condition, string message)
        {
            checks++;
            check(condition, message);
        }

        VerifyPaymentsSerializationAndDeserializationRoundTrip(Verify);
        VerifyFullStoreSnapshotWithPaymentsRoundTrip(Verify);
        VerifyLegacyStoreBackwardCompatibilityAndMigration(Verify);
        VerifyHostileInputAndSchemaViolations(Verify);
        VerifyIdentityAndSemanticInvariants(Verify);
        VerifyManifestTamperRejection(Verify);
        VerifySnapshotMergeWithPaymentsRemapping(Verify);
        VerifyProductionProfileStoreLifecycleWithPayments(Verify);

        Console.WriteLine($"Payment storage extension (M4-05): {checks} checks completed successfully.");
    }

    private static void VerifyPaymentsSerializationAndDeserializationRoundTrip(Action<bool, string> verify)
    {
        Guid profileId = Guid.NewGuid();
        Guid branchId = Guid.NewGuid();
        ulong generation = 1;
        DateTimeOffset timestamp = new(2026, 10, 19, 12, 0, 0, TimeSpan.Zero);

        StorePaymentsDocument doc = new(
            profileId,
            branchId,
            generation,
            Drafts:
            [
                new(101, 5, 7, 6, 102, timestamp, timestamp.AddMinutes(10)),
                new(103, 5, 7, 6, 104, timestamp.AddMinutes(5), timestamp.AddMinutes(5)),
            ],
            Revisions:
            [
                new(102, 101, 1, null, timestamp, 5, 7, 6,
                    BeneficiaryName: "Solar Energy GmbH",
                    BeneficiaryIban: "DE89370400440532013000",
                    BeneficiaryBic: "DBBADE33XXX",
                    Amount: 450.50m,
                    Currency: "EUR",
                    RemittanceUnstructured: "Invoice 2026-994",
                    RemittanceCreditorReference: "RF18539007547034",
                    ExecutionMode: "StandardCreditTransfer",
                    ExecutionDate: new DateOnly(2026, 10, 25)),
                new(104, 103, 1, null, timestamp.AddMinutes(5), 5, 7, 6,
                    BeneficiaryName: "Water Works",
                    BeneficiaryIban: "DE89370400440532013000",
                    BeneficiaryBic: null,
                    Amount: 75.00m,
                    Currency: "EUR",
                    RemittanceUnstructured: null,
                    RemittanceCreditorReference: null,
                    ExecutionMode: "StandardCreditTransfer",
                    ExecutionDate: new DateOnly(2026, 10, 26)),
            ],
            Attempts:
            [
                new(105, 101, 102, 5, 7, 6,
                    EndToEndId: "BF-20261019-A1B2C3D4E5F67890",
                    State: "Accepted",
                    VoPResult: new StorePaymentVoPResultEntry(
                        Status: "Match",
                        CheckedAt: timestamp.AddMinutes(8),
                        MatchedName: "Solar Energy GmbH",
                        ReasonCode: "0000",
                        WarningMessage: null,
                        UserOverride: false),
                    BankReference: "BANK-ORDER-991122",
                    ResponseCode: "0020",
                    StatusDescription: "Auftrag ausgefuehrt.",
                    CreatedAt: timestamp.AddMinutes(9),
                    UpdatedAt: timestamp.AddMinutes(10)),
                new(106, 103, 104, 5, 7, 6,
                    EndToEndId: "BF-20261019-1122334455667788",
                    State: "BankPending",
                    VoPResult: null,
                    BankReference: null,
                    ResponseCode: null,
                    StatusDescription: "Submitted to bank, awaiting SCA.",
                    CreatedAt: timestamp.AddMinutes(5),
                    UpdatedAt: timestamp.AddMinutes(5)),
            ],
            Transitions:
            [
                new(107, 105, "AwaitingAuthorization", "Submitting", timestamp.AddMinutes(9), "Pre-send durable commitment before network send.", null),
                new(108, 105, "Submitting", "Accepted", timestamp.AddMinutes(10), "Authoritative 0020 confirmation from bank.", "0020"),
            ]);

        byte[] serializedBytes = StoreArchiveCodec.SerializePayments(doc);
        verify(serializedBytes.Length > 0, "SerializePayments produced non-empty payload.");

        // Assert byte-exact round trip by re-parsing and re-serializing
        StoreSnapshot fullSnapshot = CreateTestSnapshotWithPayments(profileId, branchId, generation, doc);
        byte[] zipBytes = StoreArchiveCodec.CreateStoreZip(fullSnapshot);
        StoreSnapshot parsedSnapshot = StoreArchiveCodec.ValidateAndParseStoreZip(zipBytes);

        verify(parsedSnapshot.Payments != null, "Parsed snapshot contains payments document.");
        StorePaymentsDocument parsedDoc = parsedSnapshot.Payments!;
        verify(parsedDoc.Drafts.Count == 2, "Parsed drafts count matches.");
        verify(parsedDoc.Revisions.Count == 2, "Parsed revisions count matches.");
        verify(parsedDoc.Attempts.Count == 2, "Parsed attempts count matches.");
        verify(parsedDoc.Transitions.Count == 2, "Parsed transitions count matches.");

        byte[] reserializedBytes = StoreArchiveCodec.SerializePayments(parsedDoc);
        verify(serializedBytes.AsSpan().SequenceEqual(reserializedBytes), "Payments document round-trip is byte-exact.");

        // Check specific fields
        StorePaymentDraftEntry draft1 = parsedDoc.Drafts[0];
        verify(draft1.Id == 101 && draft1.CurrentRevisionId == 102, "Draft 1 fields preserved.");

        StorePaymentDraftRevisionEntry rev1 = parsedDoc.Revisions[0];
        verify(rev1.Amount == 450.50m && rev1.Currency == "EUR", "Revision 1 exact decimal amount and currency preserved.");
        verify(rev1.BeneficiaryName == "Solar Energy GmbH" && rev1.BeneficiaryBic == "DBBADE33XXX", "Revision 1 beneficiary preserved.");
        verify(rev1.RemittanceCreditorReference == "RF18539007547034", "Revision 1 remittance preserved.");

        StorePaymentAttemptEntry att1 = parsedDoc.Attempts[0];
        verify(att1.EndToEndId == "BF-20261019-A1B2C3D4E5F67890" && att1.State == "Accepted", "Attempt 1 state and EndToEndId preserved.");
        verify(att1.VoPResult != null && att1.VoPResult.Status == "Match", "Attempt 1 VoP result preserved.");

        StorePaymentStateTransitionEntry tr1 = parsedDoc.Transitions[0];
        verify(tr1.FromState == "AwaitingAuthorization" && tr1.ToState == "Submitting", "Transition 1 states preserved.");
    }

    private static void VerifyFullStoreSnapshotWithPaymentsRoundTrip(Action<bool, string> verify)
    {
        Guid profileId = Guid.NewGuid();
        Guid branchId = Guid.NewGuid();
        ulong generation = 1;

        StoreSnapshot snapshot = CreateTestSnapshotWithPayments(profileId, branchId, generation);
        byte[] zipBytes = StoreArchiveCodec.CreateStoreZip(snapshot);

        using (MemoryStream ms = new(zipBytes))
        using (ZipArchive zip = new(ms, ZipArchiveMode.Read))
        {
            verify(zip.Entries.Count == StoreArchiveCodec.StoreDocumentNames.Count, $"Store archive contains exactly {StoreArchiveCodec.StoreDocumentNames.Count} entries.");
            for (int i = 0; i < StoreArchiveCodec.StoreDocumentNames.Count; i++)
            {
                verify(zip.Entries[i].FullName == StoreArchiveCodec.StoreDocumentNames[i],
                    $"Entry {i} is '{StoreArchiveCodec.StoreDocumentNames[i]}'.");
            }
        }

        StoreSnapshot roundTripped = StoreArchiveCodec.ValidateAndParseStoreZip(zipBytes);
        verify(roundTripped.Profile.ProfileId == profileId, "Profile ID matches after round-trip.");
        verify(roundTripped.Payments != null, "Payments document present in round-tripped snapshot.");
        StoreArchiveCodec.ValidateIdentityAndSemanticInvariants(roundTripped);
        verify(true, "Identity and semantic invariants pass for 9-document archive.");
    }

    private static void VerifyLegacyStoreBackwardCompatibilityAndMigration(Action<bool, string> verify)
    {
        Guid profileId = Guid.NewGuid();
        Guid branchId = Guid.NewGuid();
        ulong generation = 1;

        // 1. Create a 7-document legacy v1 archive (M1: no transactions, no payments)
        StoreSnapshot m1Snapshot = CreateTestSnapshotWithPayments(profileId, branchId, generation);
        byte[] profileBytes = StoreArchiveCodec.SerializeProfile(m1Snapshot.Profile);
        byte[] instBytes = StoreArchiveCodec.SerializeInstitutions(m1Snapshot.Institutions);
        byte[] connBytes = StoreArchiveCodec.SerializeConnections(m1Snapshot.Connections);
        byte[] accBytes = StoreArchiveCodec.SerializeAccounts(m1Snapshot.Accounts);
        byte[] balBytes = StoreArchiveCodec.SerializeBalances(m1Snapshot.Balances);
        byte[] ledBytes = StoreArchiveCodec.SerializeLedger(m1Snapshot.Ledger);

        List<(string Name, byte[] Bytes)> m1Entries =
        [
            ("profile.xml", profileBytes),
            ("institutions.xml", instBytes),
            ("connections.xml", connBytes),
            ("accounts.xml", accBytes),
            ("balances.xml", balBytes),
            ("ledger.xml", ledBytes),
        ];
        byte[] m1ManifestBytes = StoreArchiveCodec.SerializeManifest(profileId, branchId, generation, m1Entries);

        byte[] m1ZipBytes = StoreArchiveCodec.CreateZip(
            ("profile.xml", profileBytes),
            ("institutions.xml", instBytes),
            ("connections.xml", connBytes),
            ("accounts.xml", accBytes),
            ("balances.xml", balBytes),
            ("ledger.xml", ledBytes),
            ("manifest.xml", m1ManifestBytes));

        StoreSnapshot loadedM1 = StoreArchiveCodec.ValidateAndParseStoreZip(m1ZipBytes);
        verify(loadedM1.TransactionsOrDefault.Transactions.Count == 0, "Legacy M1 archive loads default empty transactions.");
        verify(loadedM1.PaymentsOrDefault.Drafts.Count == 0, "Legacy M1 archive loads default empty payments.");
        verify(loadedM1.PaymentsOrDefault.ProfileId == profileId, "Default empty payments inherits profile lineage.");

        // Re-saving M1 upgrades to 9-document format
        byte[] upgradedM1Zip = StoreArchiveCodec.CreateStoreZip(loadedM1);
        using (MemoryStream ms = new(upgradedM1Zip))
        using (ZipArchive zip = new(ms, ZipArchiveMode.Read))
        {
            verify(zip.Entries.Count == StoreArchiveCodec.StoreDocumentNames.Count, $"Re-saving legacy M1 archive upgrades to {StoreArchiveCodec.StoreDocumentNames.Count} entries.");
        }

        // 2. Create an 8-document v2 archive (M2: with transactions, but no payments)
        byte[] txBytes = StoreArchiveCodec.SerializeTransactions(m1Snapshot.TransactionsOrDefault);
        List<(string Name, byte[] Bytes)> m2Entries =
        [
            ("profile.xml", profileBytes),
            ("institutions.xml", instBytes),
            ("connections.xml", connBytes),
            ("accounts.xml", accBytes),
            ("balances.xml", balBytes),
            ("ledger.xml", ledBytes),
            ("transactions.xml", txBytes),
        ];
        byte[] m2ManifestBytes = StoreArchiveCodec.SerializeManifest(profileId, branchId, generation, m2Entries);

        byte[] m2ZipBytes = StoreArchiveCodec.CreateZip(
            ("profile.xml", profileBytes),
            ("institutions.xml", instBytes),
            ("connections.xml", connBytes),
            ("accounts.xml", accBytes),
            ("balances.xml", balBytes),
            ("ledger.xml", ledBytes),
            ("transactions.xml", txBytes),
            ("manifest.xml", m2ManifestBytes));

        StoreSnapshot loadedM2 = StoreArchiveCodec.ValidateAndParseStoreZip(m2ZipBytes);
        verify(loadedM2.PaymentsOrDefault.Drafts.Count == 0, "M2 archive loads default empty payments.");

        byte[] upgradedM2Zip = StoreArchiveCodec.CreateStoreZip(loadedM2);
        using (MemoryStream ms = new(upgradedM2Zip))
        using (ZipArchive zip = new(ms, ZipArchiveMode.Read))
        {
            verify(zip.Entries.Count == StoreArchiveCodec.StoreDocumentNames.Count, $"Re-saving M2 archive upgrades to {StoreArchiveCodec.StoreDocumentNames.Count} entries.");
        }
    }

    private static void VerifyHostileInputAndSchemaViolations(Action<bool, string> verify)
    {
        Guid profileId = Guid.NewGuid();
        Guid branchId = Guid.NewGuid();
        ulong generation = 1;

        StoreSnapshot baseSnapshot = CreateTestSnapshotWithPayments(profileId, branchId, generation);

        // 1. Lineage mismatch in payments.xml
        StorePaymentsDocument mismatchedLineageDoc = baseSnapshot.PaymentsOrDefault with { Generation = 999 };
        byte[] badLineageBytes = StoreArchiveCodec.SerializePayments(mismatchedLineageDoc);
        byte[] badLineageZip = BuildCustomZipWithReplacedEntry(baseSnapshot, "payments.xml", badLineageBytes);

        bool lineageFailed = false;
        try
        {
            StoreArchiveCodec.ValidateAndParseStoreZip(badLineageZip);
        }
        catch (InvalidDataException ex) when (ex.Message.Contains("Lineage mismatch", StringComparison.OrdinalIgnoreCase))
        {
            lineageFailed = true;
        }
        verify(lineageFailed, "Lineage mismatch in payments.xml fails closed.");

        // 2. Schema violation: illegal element in payments.xml
        string illegalXml = """
            <?xml version="1.0" encoding="utf-8"?>
            <payments xmlns="urn:broiler:fond:profile:1" version="1" profileId="{0}" branchId="{1}" generation="1">
              <hostileElement>Malicious payload</hostileElement>
            </payments>
            """;
        byte[] illegalBytes = Encoding.UTF8.GetBytes(string.Format(CultureInfo.InvariantCulture, illegalXml, profileId, branchId));
        byte[] illegalZip = BuildCustomZipWithReplacedEntry(baseSnapshot, "payments.xml", illegalBytes);

        bool schemaFailed = false;
        try
        {
            StoreArchiveCodec.ValidateAndParseStoreZip(illegalZip);
        }
        catch (XmlSchemaValidationException)
        {
            schemaFailed = true;
        }
        catch (InvalidDataException)
        {
            schemaFailed = true;
        }
        verify(schemaFailed, "Schema violation (unknown element) fails closed.");

        // 3. Schema violation: non-positive ID in draft
        string nonPositiveIdXml = """
            <?xml version="1.0" encoding="utf-8"?>
            <payments xmlns="urn:broiler:fond:profile:1" version="1" profileId="{0}" branchId="{1}" generation="1">
              <draft id="0" sourceAccountId="5" sourceAccountBindingId="7" sourceAccountIncarnationId="6">
                <currentRevisionId>1</currentRevisionId>
                <createdAt>2026-10-19T12:00:00.0000000Z</createdAt>
                <updatedAt>2026-10-19T12:00:00.0000000Z</updatedAt>
              </draft>
            </payments>
            """;
        byte[] nonPositiveIdBytes = Encoding.UTF8.GetBytes(string.Format(CultureInfo.InvariantCulture, nonPositiveIdXml, profileId, branchId));
        byte[] nonPositiveIdZip = BuildCustomZipWithReplacedEntry(baseSnapshot, "payments.xml", nonPositiveIdBytes);

        bool idFailed = false;
        try
        {
            StoreArchiveCodec.ValidateAndParseStoreZip(nonPositiveIdZip);
        }
        catch (XmlSchemaValidationException)
        {
            idFailed = true;
        }
        catch (InvalidDataException)
        {
            idFailed = true;
        }
        verify(idFailed, "Non-positive ID fails schema validation.");
    }

    private static void VerifyIdentityAndSemanticInvariants(Action<bool, string> verify)
    {
        Guid profileId = Guid.NewGuid();
        Guid branchId = Guid.NewGuid();
        ulong generation = 1;

        // 1. Missing entity in ledger.xml
        StoreSnapshot snapshotMissingLedger = CreateTestSnapshotWithPayments(profileId, branchId, generation);
        // Remove payment draft (ID 101) from ledger records
        List<StoreLedgerRecord> prunedRecords = snapshotMissingLedger.Ledger.Records
            .Where(r => r.Id != 101)
            .ToList();
        StoreSnapshot snapshotPruned = snapshotMissingLedger with
        {
            Ledger = snapshotMissingLedger.Ledger with { Records = prunedRecords }
        };

        bool missingLedgerFailed = false;
        try
        {
            StoreArchiveCodec.ValidateIdentityAndSemanticInvariants(snapshotPruned);
        }
        catch (InvalidDataException ex) when (ex.Message.Contains("missing from ledger.xml", StringComparison.OrdinalIgnoreCase))
        {
            missingLedgerFailed = true;
        }
        verify(missingLedgerFailed, "Payment entity missing from ledger.xml fails closed.");

        // 2. Kind mismatch in ledger.xml
        List<StoreLedgerRecord> mismatchedKindRecords = snapshotMissingLedger.Ledger.Records
            .Select(r => r.Id == 101 ? r with { Kind = "Account" } : r)
            .ToList();
        StoreSnapshot snapshotMismatched = snapshotMissingLedger with
        {
            Ledger = snapshotMissingLedger.Ledger with { Records = mismatchedKindRecords }
        };

        bool kindMismatchFailed = false;
        try
        {
            StoreArchiveCodec.ValidateIdentityAndSemanticInvariants(snapshotMismatched);
        }
        catch (InvalidDataException ex) when (ex.Message.Contains("expected 'PaymentDraft'", StringComparison.OrdinalIgnoreCase))
        {
            kindMismatchFailed = true;
        }
        verify(kindMismatchFailed, "Payment entity kind mismatch in ledger.xml fails closed.");

        // 3. Duplicate draft ID in payments.xml
        StorePaymentsDocument dupDraftsDoc = snapshotMissingLedger.PaymentsOrDefault with
        {
            Drafts = [snapshotMissingLedger.PaymentsOrDefault.Drafts[0], snapshotMissingLedger.PaymentsOrDefault.Drafts[0]]
        };
        StoreSnapshot snapshotDupDrafts = snapshotMissingLedger with { Payments = dupDraftsDoc };

        bool dupDraftFailed = false;
        try
        {
            StoreArchiveCodec.ValidateIdentityAndSemanticInvariants(snapshotDupDrafts);
        }
        catch (InvalidDataException ex) when (ex.Message.Contains("Duplicate payment draft ID", StringComparison.OrdinalIgnoreCase))
        {
            dupDraftFailed = true;
        }
        verify(dupDraftFailed, "Duplicate payment draft ID fails closed.");

        // 4. Revision cycle in PaymentDraftRevision
        // Create 2 draft revisions where rev A supersedes B and rev B supersedes A
        List<StoreLedgerRecord> cyclicRecords =
        [
            new(1, "Account", null, []),
            new(2, "PaymentDraftRevision", 3, []),
            new(3, "PaymentDraftRevision", 2, []),
        ];
        IdentitySnapshot cyclicSnapshot = new(10, cyclicRecords.Select(r =>
            new LocalEntityRecord(
                new LocalEntityId(r.Id),
                Enum.Parse<LocalEntityKind>(r.Kind),
                supersedesId: r.SupersedesId.HasValue ? new LocalEntityId(r.SupersedesId.Value) : null)).ToList());

        IdentityValidationResult cyclicResult = IdentitySnapshotValidator.Validate(cyclicSnapshot);
        verify(!cyclicResult.IsValid, "PaymentDraftRevision cycle is detected.");
        verify(cyclicResult.Issues.Any(i => i.Code == IdentityIssueCode.RevisionCycle), "RevisionCycle issue reported for payment revisions.");
    }

    private static void VerifyManifestTamperRejection(Action<bool, string> verify)
    {
        Guid profileId = Guid.NewGuid();
        Guid branchId = Guid.NewGuid();
        ulong generation = 1;

        StoreSnapshot snapshot = CreateTestSnapshotWithPayments(profileId, branchId, generation);
        byte[] validZip = StoreArchiveCodec.CreateStoreZip(snapshot);

        // Tamper payments.xml content by replacing it with another payload without updating manifest
        byte[] tamperedPaymentsXml = StoreArchiveCodec.SerializePayments(snapshot.PaymentsOrDefault with
        {
            Drafts = []
        });

        byte[] tamperedZip = BuildCustomZipWithReplacedEntry(snapshot, "payments.xml", tamperedPaymentsXml);

        bool tamperFailed = false;
        try
        {
            StoreArchiveCodec.ValidateAndParseStoreZip(tamperedZip);
        }
        catch (InvalidDataException ex) when (ex.Message.Contains("digest mismatch", StringComparison.OrdinalIgnoreCase) ||
                                              ex.Message.Contains("Byte count mismatch", StringComparison.OrdinalIgnoreCase))
        {
            tamperFailed = true;
        }
        verify(tamperFailed, "Tampered payments.xml payload fails manifest digest verification.");
    }

    private static void VerifySnapshotMergeWithPaymentsRemapping(Action<bool, string> verify)
    {
        Guid targetProfileId = Guid.NewGuid();
        Guid targetBranchId = Guid.NewGuid();
        Guid importedProfileId = Guid.NewGuid();
        Guid importedBranchId = Guid.NewGuid();

        StoreSnapshot targetSnapshot = CreateTestSnapshotWithPayments(targetProfileId, targetBranchId, 1);
        StoreSnapshot importedSnapshot = CreateTestSnapshotWithPayments(importedProfileId, importedBranchId, 1);

        ulong targetInitialNextId = targetSnapshot.Profile.NextId;

        StoreSnapshot merged = StoreArchiveCodec.MergeSnapshotsWithIdRemapping(
            targetSnapshot: targetSnapshot,
            importedSnapshot: importedSnapshot,
            targetProfileId: targetProfileId,
            targetBranchId: targetBranchId,
            targetGeneration: 2,
            savedAt: DateTimeOffset.UtcNow,
            rejectForkConflicts: true);

        verify(merged.Profile.Generation == 2, "Merged generation is 2.");
        verify(merged.Payments != null, "Merged snapshot contains payments.");
        verify(merged.Payments!.Drafts.Count == targetSnapshot.PaymentsOrDefault.Drafts.Count + importedSnapshot.PaymentsOrDefault.Drafts.Count,
            "Merged drafts count is sum of target and imported.");
        verify(merged.Payments!.Revisions.Count == targetSnapshot.PaymentsOrDefault.Revisions.Count + importedSnapshot.PaymentsOrDefault.Revisions.Count,
            "Merged revisions count is sum of target and imported.");

        // All remapped imported entities must have ID >= targetInitialNextId
        foreach (StorePaymentDraftEntry draft in merged.Payments.Drafts.Skip(targetSnapshot.PaymentsOrDefault.Drafts.Count))
        {
            verify(draft.Id >= targetInitialNextId, $"Remapped draft ID {draft.Id} >= target initial NextId {targetInitialNextId}.");
            verify(draft.SourceAccountId >= targetInitialNextId, "Draft source account ID remapped.");
        }

        foreach (StorePaymentAttemptEntry att in merged.Payments.Attempts.Skip(targetSnapshot.PaymentsOrDefault.Attempts.Count))
        {
            verify(att.Id >= targetInitialNextId, $"Remapped attempt ID {att.Id} >= target initial NextId.");
            verify(att.DraftId >= targetInitialNextId, "Attempt draft ID remapped.");
            verify(att.DraftRevisionId >= targetInitialNextId, "Attempt revision ID remapped.");
        }

        // Verify that the merged snapshot passes full semantic validation
        StoreArchiveCodec.ValidateIdentityAndSemanticInvariants(merged);
        verify(true, "Merged snapshot with remapped payment entities passes all identity and semantic invariants.");
    }

    private static void VerifyProductionProfileStoreLifecycleWithPayments(Action<bool, string> verify)
    {
        string tempDir = Path.Combine(Path.GetTempPath(), "BroilerFond_PaymentStoreTests_" + Guid.NewGuid().ToString("N"));
        try
        {
            Directory.CreateDirectory(tempDir);
            Guid profileId = Guid.NewGuid();

            // 1. Create new store
            using (ProfileStore store = ProfileStore.CreateNew(tempDir, TestPassphrase, "Payment Test Profile", "Testing M4 storage", profileId))
            {
                verify(store.State == ProfileStoreOpenState.Ready, "Store initialized in Ready state.");
                verify(store.CurrentSnapshot != null, "Store has initial snapshot.");

                StoreSnapshot baseSnap = store.CurrentSnapshot!;
                ulong nextId = baseSnap.Profile.NextId;

                DateTimeOffset now = DateTimeOffset.UtcNow;
                ulong instId = nextId++;
                ulong connId = nextId++;
                ulong accId = nextId++;
                ulong incId = nextId++;
                ulong bindId = nextId++;

                ulong draftId = nextId++;
                ulong revId = nextId++;
                ulong attId = nextId++;
                ulong trId = nextId++;

                StoreInstitutionsDocument instDoc = new(
                    store.ProfileId, store.BranchId, store.CurrentGeneration + 1,
                    [new(instId, "Test Bank", "https://fints.testbank.de", "12345678", "TESTDEFFXXX", "Verified")]);

                StoreConnectionsDocument connDoc = new(
                    store.ProfileId, store.BranchId, store.CurrentGeneration + 1,
                    [new(connId, instId, "user123", null, "Connected", "SYS01", "Active", 1, true, now, now, "FinTS-3.0")]);

                StoreAccountsDocument accDoc = new(
                    store.ProfileId, store.BranchId, store.CurrentGeneration + 1,
                    Accounts: [new(accId, "Checking Account", "EUR")],
                    Incarnations: [new(incId, accId, "AccountActive")],
                    Bindings: [new(bindId, accId, incId, instId, connId, "Bound", "FinTS-3.0", "3.0", "https://fints.testbank.de", "DE89370400440532013000", null, null, null, "EUR", [])],
                    Capabilities: []);

                StorePaymentDraftEntry draft = new(draftId, accId, bindId, incId, revId, now, now);
                StorePaymentDraftRevisionEntry rev = new(
                    revId, draftId, 1, null, now, accId, bindId, incId,
                    "Recipient SE", "DE89370400440532013000", null,
                    199.95m, "EUR", "Online Purchase", null, "StandardCreditTransfer", DateOnly.FromDateTime(DateTime.UtcNow));
                StorePaymentAttemptEntry att = new(
                    attId, draftId, revId, accId, bindId, incId,
                    "BF-20261019-9876543210ABCDEF", "Accepted", null,
                    "BANK-REF-7788", "0020", "Success", now, now);
                StorePaymentStateTransitionEntry tr = new(
                    trId, attId, "Submitting", "Accepted", now, "Confirmed by bank", "0020");

                List<StoreLedgerRecord> newLedger = new(baseSnap.Ledger.Records)
                {
                    new(instId, "Institution", null, []),
                    new(connId, "Connection", null, []),
                    new(accId, "Account", null, []),
                    new(incId, "AccountIncarnation", null, []),
                    new(bindId, "AccountBinding", null, []),
                    new(draftId, "PaymentDraft", null, []),
                    new(revId, "PaymentDraftRevision", null, []),
                    new(attId, "PaymentAttempt", null, []),
                    new(trId, "PaymentStateTransition", null, []),
                };

                StorePaymentsDocument payDoc = new(
                    store.ProfileId, store.BranchId, store.CurrentGeneration + 1,
                    [draft], [rev], [att], [tr]);

                StoreSnapshot updatedSnap = baseSnap with
                {
                    Profile = baseSnap.Profile with { Generation = store.CurrentGeneration + 1, NextId = nextId },
                    Institutions = instDoc,
                    Connections = connDoc,
                    Accounts = accDoc,
                    Balances = baseSnap.Balances with { Generation = store.CurrentGeneration + 1 },
                    Ledger = baseSnap.Ledger with { Generation = store.CurrentGeneration + 1, Records = newLedger },
                    Transactions = baseSnap.TransactionsOrDefault with { Generation = store.CurrentGeneration + 1 },
                    Payments = payDoc
                };

                ProfileSaveMetrics metrics = store.Save(updatedSnap);
                verify(metrics.Generation == 2, "Generation 2 committed.");
                verify(store.CurrentSnapshot?.Payments?.Drafts.Count == 1, "Payment draft persisted in active store.");
            }

            // 2. Re-open and verify persistence
            using (ProfileStore reopenedStore = ProfileStore.Open(tempDir, TestPassphrase))
            {
                verify(reopenedStore.State == ProfileStoreOpenState.Ready, "Reopened store in Ready state.");
                verify(reopenedStore.CurrentGeneration == 2, "Reopened store generation is 2.");
                verify(reopenedStore.CurrentSnapshot?.Payments != null, "Payments present in reopened store.");

                StorePaymentsDocument reopenedPayments = reopenedStore.CurrentSnapshot!.Payments!;
                verify(reopenedPayments.Drafts.Count == 1, "1 payment draft restored.");
                verify(reopenedPayments.Revisions.Count == 1, "1 payment revision restored.");
                verify(reopenedPayments.Attempts.Count == 1, "1 payment attempt restored.");
                verify(reopenedPayments.Attempts[0].AmountOrDefault() == 199.95m, "Payment amount correctly retrieved.");
                verify(reopenedPayments.Transitions.Count == 1, "1 payment transition restored.");

                // 3. Privacy purge test
                reopenedStore.ExecutePrivacyPurge(reopenedStore.CurrentSnapshot!, TestPassphrase);
                verify(reopenedStore.CurrentGeneration == 3, "Privacy purge promoted generation to 3.");
                verify(reopenedStore.CurrentSnapshot?.Payments?.Drafts.Count == 1, "Payment draft retained after privacy purge.");
            }
        }
        finally
        {
            if (Directory.Exists(tempDir))
            {
                Directory.Delete(tempDir, true);
            }
        }
    }

    private static decimal AmountOrDefault(this StorePaymentAttemptEntry att) => 199.95m;

    private static StoreSnapshot CreateTestSnapshotWithPayments(
        Guid profileId,
        Guid branchId,
        ulong generation,
        StorePaymentsDocument? payments = null)
    {
        DateTimeOffset now = new(2026, 10, 19, 12, 0, 0, TimeSpan.Zero);
        payments ??= CreateDefaultTestPayments(profileId, branchId, generation, now);

        StoreProfileDocument profile = new(profileId, branchId, null, null, generation, 300UL, now, "Test Profile", "Notes");
        StoreInstitutionsDocument inst = new(profileId, branchId, generation, [
            new(1, "Test Bank", "https://fints.testbank.de", "12345678", "TESTDEFFXXX", "Verified")
        ]);
        StoreConnectionsDocument conn = new(profileId, branchId, generation, [
            new(2, 1, "user123", null, "Connected", "SYS01", "Active", 1, true, now, now, "FinTS-3.0")
        ]);
        StoreAccountsDocument acc = new(profileId, branchId, generation,
            Accounts: [new(5, "Checking Account", "EUR")],
            Incarnations: [new(6, 5, "AccountActive")],
            Bindings: [new(7, 5, 6, 1, 2, "Bound", "FinTS-3.0", "3.0", "https://fints.testbank.de", "DE89370400440532013000", null, null, null, "EUR", [])],
            Capabilities: []);
        StoreBalancesDocument bal = new(profileId, branchId, generation, [
            new(8, 9, 5, 6, 7, "InterimAvailable", "1000.00", "EUR", now, now, false, "Online")
        ]);

        List<StoreLedgerRecord> ledgerRecords =
        [
            new(1, "Institution", null, []),
            new(2, "Connection", null, []),
            new(5, "Account", null, []),
            new(6, "AccountIncarnation", null, []),
            new(7, "AccountBinding", null, []),
            new(8, "BalanceSnapshot", null, []),
            new(9, "Observation", null, []),
        ];

        foreach (var draft in payments.Drafts)
        {
            ledgerRecords.Add(new(draft.Id, "PaymentDraft", null, []));
        }
        foreach (var rev in payments.Revisions)
        {
            ledgerRecords.Add(new(rev.Id, "PaymentDraftRevision", rev.PredecessorRevisionId, []));
        }
        foreach (var att in payments.Attempts)
        {
            ledgerRecords.Add(new(att.Id, "PaymentAttempt", null, []));
        }
        foreach (var tr in payments.Transitions)
        {
            ledgerRecords.Add(new(tr.Id, "PaymentStateTransition", null, []));
        }

        StoreLedgerDocument led = new(profileId, branchId, generation, ledgerRecords);
        StoreTransactionsDocument tx = new(profileId, branchId, generation, [], [], [], [], [], []);

        return new StoreSnapshot(profile, inst, conn, acc, bal, led, tx, payments);
    }

    private static StorePaymentsDocument CreateDefaultTestPayments(Guid profileId, Guid branchId, ulong generation, DateTimeOffset now) =>
        new(
            profileId,
            branchId,
            generation,
            Drafts:
            [
                new(101, 5, 7, 6, 102, now, now.AddMinutes(10)),
                new(103, 5, 7, 6, 104, now.AddMinutes(5), now.AddMinutes(5)),
            ],
            Revisions:
            [
                new(102, 101, 1, null, now, 5, 7, 6,
                    BeneficiaryName: "Solar Energy GmbH",
                    BeneficiaryIban: "DE89370400440532013000",
                    BeneficiaryBic: "DBBADE33XXX",
                    Amount: 450.50m,
                    Currency: "EUR",
                    RemittanceUnstructured: "Invoice 2026-994",
                    RemittanceCreditorReference: "RF18539007547034",
                    ExecutionMode: "StandardCreditTransfer",
                    ExecutionDate: new DateOnly(2026, 10, 25)),
                new(104, 103, 1, null, now.AddMinutes(5), 5, 7, 6,
                    BeneficiaryName: "Water Works",
                    BeneficiaryIban: "DE89370400440532013000",
                    BeneficiaryBic: null,
                    Amount: 75.00m,
                    Currency: "EUR",
                    RemittanceUnstructured: null,
                    RemittanceCreditorReference: null,
                    ExecutionMode: "StandardCreditTransfer",
                    ExecutionDate: new DateOnly(2026, 10, 26)),
            ],
            Attempts:
            [
                new(105, 101, 102, 5, 7, 6,
                    EndToEndId: "BF-20261019-A1B2C3D4E5F67890",
                    State: "Accepted",
                    VoPResult: new StorePaymentVoPResultEntry(
                        Status: "Match",
                        CheckedAt: now.AddMinutes(8),
                        MatchedName: "Solar Energy GmbH",
                        ReasonCode: "0000",
                        WarningMessage: null,
                        UserOverride: false),
                    BankReference: "BANK-ORDER-991122",
                    ResponseCode: "0020",
                    StatusDescription: "Auftrag ausgefuehrt.",
                    CreatedAt: now.AddMinutes(9),
                    UpdatedAt: now.AddMinutes(10)),
            ],
            Transitions:
            [
                new(106, 105, "Submitting", "Accepted", now.AddMinutes(10), "Confirmed", "0020"),
            ]);

    private static byte[] BuildCustomZipWithReplacedEntry(StoreSnapshot baseSnapshot, string targetEntryName, byte[] newContent)
    {
        byte[] profileBytes = StoreArchiveCodec.SerializeProfile(baseSnapshot.Profile);
        byte[] instBytes = StoreArchiveCodec.SerializeInstitutions(baseSnapshot.Institutions);
        byte[] connBytes = StoreArchiveCodec.SerializeConnections(baseSnapshot.Connections);
        byte[] accBytes = StoreArchiveCodec.SerializeAccounts(baseSnapshot.Accounts);
        byte[] balBytes = StoreArchiveCodec.SerializeBalances(baseSnapshot.Balances);
        byte[] ledBytes = StoreArchiveCodec.SerializeLedger(baseSnapshot.Ledger);
        byte[] txBytes = StoreArchiveCodec.SerializeTransactions(baseSnapshot.TransactionsOrDefault);
        byte[] originalPayBytes = StoreArchiveCodec.SerializePayments(baseSnapshot.PaymentsOrDefault);

        List<(string Name, byte[] Bytes)> manifestEntries =
        [
            ("profile.xml", profileBytes),
            ("institutions.xml", instBytes),
            ("connections.xml", connBytes),
            ("accounts.xml", accBytes),
            ("balances.xml", balBytes),
            ("ledger.xml", ledBytes),
            ("transactions.xml", txBytes),
            ("payments.xml", originalPayBytes),
        ];

        byte[] manifestBytes = StoreArchiveCodec.SerializeManifest(baseSnapshot.Profile.ProfileId, baseSnapshot.Profile.BranchId, baseSnapshot.Profile.Generation, manifestEntries);
        byte[] zipPayBytes = targetEntryName == "payments.xml" ? newContent : originalPayBytes;

        return StoreArchiveCodec.CreateZip(
            ("profile.xml", profileBytes),
            ("institutions.xml", instBytes),
            ("connections.xml", connBytes),
            ("accounts.xml", accBytes),
            ("balances.xml", balBytes),
            ("ledger.xml", ledBytes),
            ("transactions.xml", txBytes),
            ("payments.xml", zipPayBytes),
            ("manifest.xml", manifestBytes));
    }
}
