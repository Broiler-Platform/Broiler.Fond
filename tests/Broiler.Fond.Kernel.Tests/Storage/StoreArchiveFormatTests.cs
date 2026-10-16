using System.Buffers.Binary;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Xml;
using System.Xml.Schema;
using Broiler.Fond.Kernel.Domain.Accounts;
using Broiler.Fond.Kernel.Domain.Connections;
using Broiler.Fond.Kernel.Domain.Institutions;

namespace Broiler.Fond.Kernel.Tests.Storage;

/// <summary>
/// Executable verification suite for M1-01 Storage Format Review Candidate:
/// financial-document schemas, ancestry metadata, purge-intent schemas,
/// production ZIP preflight validation, and supported-floor KDF calibration.
/// </summary>
internal static class StoreArchiveFormatTests
{
    internal static void Run(Action<bool, string> check)
    {
        int checks = 0;
        void Verify(bool condition, string message)
        {
            checks++;
            check(condition, message);
        }

        VerifyFullStoreRoundTrip(Verify);
        VerifyBranchAncestryMetadata(Verify);
        VerifyPurgeIntentSchemas(Verify);
        VerifyZipPreflightHostileRejections(Verify);
        VerifyXmlSchemaRejections(Verify);
        VerifyManifestAndLineageIntegrity(Verify);
        VerifyKdfCalibration(Verify);
        VerifyEncryptedEnvelopeIntegration(Verify);

        Console.WriteLine($"Store archive candidate & preflight: {checks} checks completed successfully.");
    }

    private static void VerifyFullStoreRoundTrip(Action<bool, string> verify)
    {
        Guid profileId = Guid.NewGuid();
        Guid branchId = Guid.NewGuid();
        ulong generation = 1;
        ulong nextId = 100;
        DateTimeOffset savedAt = new DateTimeOffset(2026, 10, 15, 20, 30, 0, TimeSpan.Zero).AddTicks(1234567);

        StoreProfileDocument profile = new(
            profileId, branchId, null, null, generation, nextId, savedAt,
            "Main Household Profile", "Primary profile containing household banking.");

        StoreInstitutionsDocument institutions = new(
            profileId, branchId, generation,
            [
                new(1, "Test Sparkasse", "https://fints.sparkasse-test.de/fints", "12345678", "GENODED1XYZ", "Confirmed"),
                new(2, "Test Volksbank", "https://hbci.volksbank-test.de/hbci", "87654321", null, "Unconfirmed"),
            ]);

        StoreConnectionsDocument connections = new(
            profileId, branchId, generation,
            [
                new(3, 1, "testuser01", "cust100", "Connected", "SYS12345678", "Synchronized", 1, true, savedAt, savedAt, "FinTS 3.0 PIN/TAN"),
                new(4, 2, "testuser02", null, "Unconfigured", null, "Required", 1, false, null, null, "FinTS 3.0 PIN/TAN"),
            ]);

        StoreAccountsDocument accounts = new(
            profileId, branchId, generation,
            [
                new(5, "Girokonto", "EUR"),
                new(6, "Tagesgeld", "EUR"),
            ],
            [
                new(7, 5, "Active"),
                new(8, 6, "Active"),
            ],
            [
                new(9, 5, 7, 1, 3, "Active", "FinTS 3.0 PIN/TAN", "1.0", "https://fints.sparkasse-test.de/fints",
                    "DE89370400440532013000", "0532013000", "37040044", "00", "EUR",
                    [("DE.SPARKASSE", "ACCOUNT", "INTERNAL", "INT-998877")]),
                new(10, 6, 8, 1, 3, "Active", "FinTS 3.0 PIN/TAN", "1.0", "https://fints.sparkasse-test.de/fints",
                    "DE89370400440532013001", "0532013001", "37040044", "01", "EUR", []),
            ],
            [
                new(3, 5, "HKSAL", 7, "Active", "10000.00"),
                new(3, 5, "HKSPA", 1, "Active", null),
            ]);

        StoreBalancesDocument balances = new(
            profileId, branchId, generation,
            [
                new(11, 20, 5, 7, 9, "Booked", "1543.21", "EUR", savedAt, savedAt, false, "SALDO"),
                new(12, 21, 5, 7, 9, "Available", "2043.21", "EUR", savedAt, savedAt, false, null),
                new(13, 22, 5, 7, 9, "CreditLine", "500.00", "EUR", savedAt, savedAt, false, null),
                new(14, 23, 6, 8, 10, "Booked", "50000.00", "EUR", savedAt, savedAt, false, null),
            ]);

        StoreLedgerDocument ledger = new(
            profileId, branchId, generation,
            [
                new(1, "Institution", null, []),
                new(2, "Institution", null, []),
                new(3, "Connection", null, [(1, "Institution")]),
                new(4, "Connection", null, [(2, "Institution")]),
                new(5, "Account", null, []),
                new(6, "Account", null, []),
                new(7, "AccountIncarnation", null, [(5, "Account")]),
                new(8, "AccountIncarnation", null, [(6, "Account")]),
                new(9, "AccountBinding", null, [(7, "AccountIncarnation"), (3, "Connection")]),
                new(10, "AccountBinding", null, [(8, "AccountIncarnation"), (3, "Connection")]),
                new(11, "BalanceSnapshot", null, [(9, "AccountBinding")]),
                new(12, "BalanceSnapshot", null, [(9, "AccountBinding")]),
                new(13, "BalanceSnapshot", null, [(9, "AccountBinding")]),
                new(14, "BalanceSnapshot", null, [(10, "AccountBinding")]),
            ]);

        StoreSnapshot original = new(profile, institutions, connections, accounts, balances, ledger);
        byte[] zipBytes = StoreArchiveReference.CreateStoreZip(original);

        verify(zipBytes.Length > 0, "Store ZIP successfully created.");

        StoreSnapshot recovered = StoreArchiveReference.ValidateAndParseStoreZip(zipBytes);
        verify(recovered.Profile.ProfileId == profileId, "Profile ID matches.");
        verify(recovered.Profile.BranchId == branchId, "Branch ID matches.");
        verify(recovered.Profile.ParentBranchId == null, "Parent Branch ID is null for root.");
        verify(recovered.Profile.RecoveryProvenance == null, "Recovery provenance is null for root.");
        verify(recovered.Profile.Generation == generation, "Generation matches.");
        verify(recovered.Profile.NextId == nextId, "NextId matches.");
        verify(recovered.Profile.Label == "Main Household Profile", "Label matches.");
        verify(recovered.Institutions.Institutions.Count == 2, "Two institutions recovered.");
        verify(recovered.Connections.Connections.Count == 2, "Two connections recovered.");
        verify(recovered.Accounts.Accounts.Count == 2, "Two accounts recovered.");
        verify(recovered.Accounts.Incarnations.Count == 2, "Two incarnations recovered.");
        verify(recovered.Accounts.Bindings.Count == 2, "Two bindings recovered.");
        verify(recovered.Accounts.Bindings[0].BankIdentifiers.Count == 1, "Bank identifier recovered.");
        verify(recovered.Accounts.Capabilities.Count == 2, "Two capabilities recovered.");
        verify(recovered.Balances.Balances.Count == 4, "Four balance snapshots recovered.");
        verify(recovered.Balances.Balances[0].Kind == "Booked", "Balance kind matches.");
        verify(recovered.Balances.Balances[0].Amount == "1543.21", "Balance amount matches.");
        verify(recovered.Ledger.Records.Count == 14, "Fourteen ledger records recovered.");
        verify(recovered.Ledger.Records[2].References.Count == 1, "Ledger reference count matches.");
    }

    private static void VerifyBranchAncestryMetadata(Action<bool, string> verify)
    {
        Guid profileId = Guid.NewGuid();
        Guid rootBranchId = Guid.NewGuid();
        Guid recoveryBranchId = Guid.NewGuid();

        // 1. Child branch with parent branch pointer
        StoreProfileDocument childBranch = new(
            profileId, recoveryBranchId, rootBranchId, "recovery-point-gen-42",
            43, 200, DateTimeOffset.UtcNow, "Recovered Branch", "Created after roll-forward repair.");

        byte[] childXml = StoreArchiveReference.SerializeProfile(childBranch);
        verify(childXml.Length > 0, "Child branch with ancestry serialized.");

        string childXmlStr = Encoding.UTF8.GetString(childXml);
        verify(childXmlStr.Contains($"parentBranchId=\"{rootBranchId:D}\"", StringComparison.Ordinal), "Parent branch ID serialized.");
        verify(childXmlStr.Contains("recoveryProvenance=\"recovery-point-gen-42\"", StringComparison.Ordinal), "Recovery provenance serialized.");

        // 2. Wrap in a minimal valid snapshot to test validation
        StoreSnapshot childSnapshot = new(
            childBranch,
            new(profileId, recoveryBranchId, 43, []),
            new(profileId, recoveryBranchId, 43, []),
            new(profileId, recoveryBranchId, 43, [], [], [], []),
            new(profileId, recoveryBranchId, 43, []),
            new(profileId, recoveryBranchId, 43, []));

        byte[] zip = StoreArchiveReference.CreateStoreZip(childSnapshot);
        StoreSnapshot parsed = StoreArchiveReference.ValidateAndParseStoreZip(zip);
        verify(parsed.Profile.ParentBranchId == rootBranchId, "ParentBranchId round-trip validated.");
        verify(parsed.Profile.RecoveryProvenance == "recovery-point-gen-42", "RecoveryProvenance round-trip validated.");
    }

    private static void VerifyPurgeIntentSchemas(Action<bool, string> verify)
    {
        Guid profileId = Guid.NewGuid();
        Guid branchId = Guid.NewGuid();
        DateTimeOffset now = DateTimeOffset.UtcNow;

        // 1. PrivacyPurge intent
        StorePurgeIntent privacyPurge = new(
            profileId, branchId, 5, "PrivacyPurge", "Pending", now, 6,
            [
                ("generation-00000000000000000001.bffond", "1111111111111111111111111111111111111111111111111111111111111111"),
                ("generation-00000000000000000002.bffond", "2222222222222222222222222222222222222222222222222222222222222222"),
            ],
            "Manual bank connection purge requested by user.");

        byte[] purgeXml = StoreArchiveReference.SerializePurgeIntent(privacyPurge);
        verify(purgeXml.Length > 0, "PrivacyPurge intent serialized.");

        StorePurgeIntent recoveredPurge = StoreArchiveReference.ValidateAndParsePurgeIntent(purgeXml);
        verify(recoveredPurge.OperationType == "PrivacyPurge", "Purge operation type matches.");
        verify(recoveredPurge.Status == "Pending", "Purge status matches.");
        verify(recoveredPurge.ReplacementGeneration == 6, "Replacement generation matches.");
        verify(recoveredPurge.TargetInventory.Count == 2, "Target inventory count matches.");
        verify(recoveredPurge.Note == "Manual bank connection purge requested by user.", "Note matches.");

        // 2. PassphraseRotation intent
        StorePurgeIntent rotationIntent = new(
            profileId, branchId, 10, "PassphraseRotation", "Committed", now, 11,
            [("generation-00000000000000000009.bffond", "9999999999999999999999999999999999999999999999999999999999999999")],
            null);
        byte[] rotationXml = StoreArchiveReference.SerializePurgeIntent(rotationIntent);
        StorePurgeIntent recoveredRotation = StoreArchiveReference.ValidateAndParsePurgeIntent(rotationXml);
        verify(recoveredRotation.OperationType == "PassphraseRotation", "Passphrase rotation type matches.");
        verify(recoveredRotation.Status == "Committed", "Committed status matches.");

        // 3. WholeProfileErasure intent (no replacement generation)
        StorePurgeIntent erasureIntent = new(
            profileId, branchId, 12, "WholeProfileErasure", "Completed", now, null,
            [("generation-00000000000000000012.bffond", "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa")],
            "Complete device erasure.");
        byte[] erasureXml = StoreArchiveReference.SerializePurgeIntent(erasureIntent);
        StorePurgeIntent recoveredErasure = StoreArchiveReference.ValidateAndParsePurgeIntent(erasureXml);
        verify(recoveredErasure.OperationType == "WholeProfileErasure", "Whole profile erasure type matches.");
        verify(recoveredErasure.ReplacementGeneration == null, "No replacement generation on whole profile erasure.");

        // 4. Invalid operation type rejection
        string invalidXml = Encoding.UTF8.GetString(purgeXml).Replace("operationType=\"PrivacyPurge\"", "operationType=\"FakeOperation\"", StringComparison.Ordinal);
        Reject(() => StoreArchiveReference.ValidateAndParsePurgeIntent(Encoding.UTF8.GetBytes(invalidXml)), verify, "Invalid purge operation type fails closed");

        // 5. Invalid status rejection
        string invalidStatusXml = Encoding.UTF8.GetString(purgeXml).Replace("status=\"Pending\"", "status=\"UnknownStatus\"", StringComparison.Ordinal);
        Reject(() => StoreArchiveReference.ValidateAndParsePurgeIntent(Encoding.UTF8.GetBytes(invalidStatusXml)), verify, "Invalid purge status fails closed");
    }

    private static void VerifyZipPreflightHostileRejections(Action<bool, string> verify)
    {
        // 1. Generate valid baseline ZIP
        byte[] validDoc = "<xml />"u8.ToArray();
        byte[] validZip = StoreArchiveReference.CreateZip(("profile.xml", validDoc));
        ZipPreflightResult result = ZipPreflight.Validate(validZip);
        verify(result.Entries.Count == 1, "Baseline valid ZIP passes preflight.");

        // 2. Payload too short
        Reject(() => ZipPreflight.Validate(new byte[21]), verify, "ZIP payload < 22 bytes fails closed");

        // 3. EOCD corrupted signature
        byte[] badEocd = (byte[])validZip.Clone();
        badEocd[^22] = 0;
        Reject(() => ZipPreflight.Validate(badEocd), verify, "Corrupted EOCD signature fails closed");

        // 4. Archive comment present in EOCD (forbidden by ADR 0005)
        byte[] withComment = new byte[validZip.Length + 4];
        validZip.CopyTo(withComment, 0);
        BinaryPrimitives.WriteUInt16LittleEndian(withComment.AsSpan(validZip.Length - 2), 4);
        withComment[^4] = (byte)'t';
        withComment[^3] = (byte)'e';
        withComment[^2] = (byte)'s';
        withComment[^1] = (byte)'t';
        Reject(() => ZipPreflight.Validate(withComment), verify, "ZIP archive comment fails closed");

        // 5. Trailing bytes after EOCD
        byte[] trailing = [.. validZip, 0x00];
        Reject(() => ZipPreflight.Validate(trailing), verify, "Trailing bytes after EOCD fails closed");

        // 6. Encrypted entry (bit 0 set in general purpose flag)
        byte[] encryptedZip = (byte[])validZip.Clone();
        // Offset 6 of local header is flags
        encryptedZip[6] |= 0x01;
        // Central directory flags offset
        uint cdOffset = BinaryPrimitives.ReadUInt32LittleEndian(validZip.AsSpan(validZip.Length - 6));
        encryptedZip[cdOffset + 8] |= 0x01;
        Reject(() => ZipPreflight.Validate(encryptedZip), verify, "Encrypted ZIP entry fails closed");

        // 7. Unsupported compression method (e.g. 9)
        byte[] unsupportedMethodZip = (byte[])validZip.Clone();
        unsupportedMethodZip[8] = 9;
        unsupportedMethodZip[cdOffset + 10] = 9;
        Reject(() => ZipPreflight.Validate(unsupportedMethodZip), verify, "Unsupported compression method fails closed");

        // 8. Directory traversal in entry name
        byte[] traversalZip = StoreArchiveReference.CreateZip(("../profile.xml", validDoc));
        Reject(() => ZipPreflight.Validate(traversalZip), verify, "Directory traversal in entry name fails closed");

        // 9. Path separator in entry name
        byte[] separatorZip = StoreArchiveReference.CreateZip(("dir/profile.xml", validDoc));
        Reject(() => ZipPreflight.Validate(separatorZip), verify, "Path separator in entry name fails closed");

        // 10. Backslash in entry name
        byte[] backslashZip = StoreArchiveReference.CreateZip(("dir\\profile.xml", validDoc));
        Reject(() => ZipPreflight.Validate(backslashZip), verify, "Backslash separator in entry name fails closed");

        // 11. Expansion ratio exceeded (> 128:1)
        byte[] bombContent = new byte[20_000];
        byte[] bombZip = StoreArchiveReference.CreateZip(("profile.xml", bombContent));
        Reject(() => ZipPreflight.Validate(bombZip), verify, "Expansion ratio limit exceeded (> 128:1) fails closed");

        // 12. Max entries exceeded
        ZipPreflightLimits singleEntryLimit = new() { MaxEntries = 1 };
        byte[] twoEntryZip = StoreArchiveReference.CreateZip(("profile.xml", validDoc), ("manifest.xml", validDoc));
        Reject(() => ZipPreflight.Validate(twoEntryZip, singleEntryLimit), verify, "Entry count exceeding limit fails closed");

        // 13. Duplicate entries
        byte[] dupZip = StoreArchiveReference.CreateZip(("profile.xml", validDoc), ("profile.xml", validDoc));
        Reject(() => ZipPreflight.Validate(dupZip), verify, "Duplicate ZIP entries fail closed");

        // 14. Prefix gap before first local header
        byte[] prefixGapZip = [0xFF, .. validZip];
        Reject(() => ZipPreflight.Validate(prefixGapZip), verify, "Prefix gap before first local header fails closed");
    }

    private static void VerifyXmlSchemaRejections(Action<bool, string> verify)
    {
        StoreProfileDocument profile = new(
            Guid.NewGuid(), Guid.NewGuid(), null, null, 1, 10, DateTimeOffset.UtcNow, "Test", "Test Note");
        byte[] validProfileBytes = StoreArchiveReference.SerializeProfile(profile);
        string validProfileXml = Encoding.UTF8.GetString(validProfileBytes);

        // 1. DTD injection
        string dtdXml = validProfileXml.Replace("<profile ", "<!DOCTYPE profile [<!ENTITY e SYSTEM 'file:///sensitive'>]><profile ", StringComparison.Ordinal);
        Reject(() => StoreArchiveReference.ValidateAndParseStoreZip(CreateSingleDocZip("profile.xml", Encoding.UTF8.GetBytes(dtdXml))), verify, "DTD injection fails closed");

        // 2. XML BOM
        byte[] bomXml = [0xEF, 0xBB, 0xBF, .. validProfileBytes];
        Reject(() => StoreArchiveReference.ValidateAndParseStoreZip(CreateSingleDocZip("profile.xml", bomXml)), verify, "XML BOM fails closed");

        // 3. Invalid UUID format (non-hex or wrong dashes)
        string badUuidXml = validProfileXml.Replace(profile.ProfileId.ToString("D"), "not-a-valid-uuid-format-here", StringComparison.Ordinal);
        Reject(() => StoreArchiveReference.ValidateAndParseStoreZip(CreateSingleDocZip("profile.xml", Encoding.UTF8.GetBytes(badUuidXml))), verify, "Malformed UUID fails closed");

        // 4. Generation is zero
        string zeroGenXml = validProfileXml.Replace("generation=\"1\"", "generation=\"0\"", StringComparison.Ordinal);
        Reject(() => StoreArchiveReference.ValidateAndParseStoreZip(CreateSingleDocZip("profile.xml", Encoding.UTF8.GetBytes(zeroGenXml))), verify, "Generation zero fails closed");

        // 5. Missing required attribute
        string missingAttrXml = validProfileXml.Replace("nextId=\"10\"", "", StringComparison.Ordinal);
        Reject(() => StoreArchiveReference.ValidateAndParseStoreZip(CreateSingleDocZip("profile.xml", Encoding.UTF8.GetBytes(missingAttrXml))), verify, "Missing required attribute fails closed");

        // 6. Unknown child element
        string extraElementXml = validProfileXml.Replace("<label>Test</label>", "<label>Test</label><extra>Surprise</extra>", StringComparison.Ordinal);
        Reject(() => StoreArchiveReference.ValidateAndParseStoreZip(CreateSingleDocZip("profile.xml", Encoding.UTF8.GetBytes(extraElementXml))), verify, "Unknown child element fails closed");
    }

    private static void VerifyManifestAndLineageIntegrity(Action<bool, string> verify)
    {
        Guid profileId = Guid.NewGuid();
        Guid branchId = Guid.NewGuid();
        ulong generation = 1;

        StoreSnapshot snapshot = new(
            new(profileId, branchId, null, null, generation, 10, DateTimeOffset.UtcNow, "L", "N"),
            new(profileId, branchId, generation, []),
            new(profileId, branchId, generation, []),
            new(profileId, branchId, generation, [], [], [], []),
            new(profileId, branchId, generation, []),
            new(profileId, branchId, generation, []));

        byte[] validZip = StoreArchiveReference.CreateStoreZip(snapshot);
        verify(validZip.Length > 0, "Valid store zip created.");

        // 1. Lineage mismatch in connections.xml (different generation)
        StoreSnapshot mismatchSnapshot = snapshot with
        {
            Connections = new(profileId, branchId, generation + 1, [])
        };
        byte[] mismatchZip = StoreArchiveReference.CreateStoreZip(mismatchSnapshot);
        Reject(() => StoreArchiveReference.ValidateAndParseStoreZip(mismatchZip), verify, "Lineage generation mismatch across documents fails closed");

        // 2. Tampered document bytes (manifest SHA mismatch)
        byte[] profileBytes = StoreArchiveReference.SerializeProfile(snapshot.Profile);
        byte[] instBytes = StoreArchiveReference.SerializeInstitutions(snapshot.Institutions);
        byte[] connBytes = StoreArchiveReference.SerializeConnections(snapshot.Connections);
        byte[] accBytes = StoreArchiveReference.SerializeAccounts(snapshot.Accounts);
        byte[] balBytes = StoreArchiveReference.SerializeBalances(snapshot.Balances);
        byte[] ledBytes = StoreArchiveReference.SerializeLedger(snapshot.Ledger);

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
        string manifestXml = Encoding.UTF8.GetString(manifestBytes);
        string tamperedManifestXml = manifestXml.Replace(Convert.ToHexStringLower(SHA256.HashData(instBytes)), "0000000000000000000000000000000000000000000000000000000000000000", StringComparison.Ordinal);

        byte[] tamperedZip = StoreArchiveReference.CreateZip(
            ("profile.xml", profileBytes),
            ("institutions.xml", instBytes),
            ("connections.xml", connBytes),
            ("accounts.xml", accBytes),
            ("balances.xml", balBytes),
            ("ledger.xml", ledBytes),
            ("manifest.xml", Encoding.UTF8.GetBytes(tamperedManifestXml)));

        Reject(() => StoreArchiveReference.ValidateAndParseStoreZip(tamperedZip), verify, "Tampered manifest SHA fails closed");
    }

    private static void VerifyKdfCalibration(Action<bool, string> verify)
    {
        KdfHostEnvironment env = SupportedFloorKdfCalibration.GetHostEnvironment();
        verify(!string.IsNullOrWhiteSpace(env.OsDescription), "OS description captured.");
        verify(env.ProcessorCount >= 1, "Processor count >= 1.");
        verify(!string.IsNullOrWhiteSpace(env.FrameworkDescription), "Framework description captured.");

        // Test calibration with 2 samples of minimum iterations (600,000)
        byte[] salt = new byte[32];
        salt[0] = 0x42;
        byte[] password = EnvelopeReference.NormalizePassphrase("KdfCalibrationTestPassphrase1!");

        KdfIterationMetrics metrics = SupportedFloorKdfCalibration.MeasureIterations(
            SupportedFloorKdfCalibration.MinimumAllowedIterations, 2, password, salt);

        verify(metrics.Iterations == 600_000, "Measured iteration count matches.");
        verify(metrics.SampleCount == 2, "Sample count matches.");
        verify(metrics.MinMs <= metrics.MedianMs && metrics.MedianMs <= metrics.MaxMs, "Statistical ordering Min <= Median <= Max holds.");
        verify(metrics.MeanMs > 0, "Mean elapsed time > 0.");

        // Verify iteration bounds enforcement
        Reject(() => SupportedFloorKdfCalibration.MeasureIterations(599_999, 1, password, salt), verify, "Iterations below 600,000 rejected");
        Reject(() => SupportedFloorKdfCalibration.MeasureIterations(5_000_001, 1, password, salt), verify, "Iterations above 5,000,000 rejected");

        // Verify calibration report selection
        KdfCalibrationReport report = SupportedFloorKdfCalibration.Calibrate(
            [SupportedFloorKdfCalibration.MinimumAllowedIterations], 2, "TestPass123!", salt);

        verify(report.RecommendedIterations >= SupportedFloorKdfCalibration.MinimumAllowedIterations, "Recommended iterations within envelope bounds.");
        verify(!string.IsNullOrWhiteSpace(report.EvaluationSummary), "Evaluation summary generated.");
    }

    private static void VerifyEncryptedEnvelopeIntegration(Action<bool, string> verify)
    {
        Guid profileId = Guid.NewGuid();
        Guid branchId = Guid.NewGuid();
        ulong generation = 1;

        StoreSnapshot snapshot = new(
            new(profileId, branchId, null, null, generation, 10, DateTimeOffset.UtcNow, "Encrypted Store", "Note"),
            new(profileId, branchId, generation, [new(1, "Sparkasse", "https://fints.example.com/fints", "12345678", null, "Confirmed")]),
            new(profileId, branchId, generation, []),
            new(profileId, branchId, generation, [], [], [], []),
            new(profileId, branchId, generation, []),
            new(profileId, branchId, generation, [new(1, "Institution", null, [])]));

        byte[] storeZip = StoreArchiveReference.CreateStoreZip(snapshot);
        verify(storeZip.Length > 0, "Store ZIP prepared for encryption.");

        string passphrase = "CorrectPassphrase!123456789";

        // Synthesize envelope header
        byte[] header = new byte[EnvelopeReference.HeaderSize];
        "BFONDPRF"u8.CopyTo(header);
        BinaryPrimitives.WriteUInt16LittleEndian(header.AsSpan(8), 1); // container version
        BinaryPrimitives.WriteUInt16LittleEndian(header.AsSpan(10), EnvelopeReference.HeaderSize);
        header[12] = 1; // KDF ID: PBKDF2 then HKDF
        header[13] = 1; // AEAD ID: AES-256-GCM
        header[14] = 1; // Payload ID: ZIP containing versioned XML
        header[15] = 1; // Passphrase rule: NFC strict UTF-8
        BinaryPrimitives.WriteUInt32LittleEndian(header.AsSpan(16), 600_000); // 600k iterations
        BinaryPrimitives.WriteUInt32LittleEndian(header.AsSpan(20), (uint)EnvelopeReference.FrameSize); // 65,536 frame
        RandomNumberGenerator.Fill(header.AsSpan(24, 32)); // profile salt
        RandomNumberGenerator.Fill(header.AsSpan(56, 32)); // snapshot salt
        RandomNumberGenerator.Fill(header.AsSpan(88, 4));  // nonce prefix

        // Encrypt with reference codec
        byte[] masterKey = EnvelopeReference.DeriveMasterKey(header, passphrase);
        byte[] contentKey = EnvelopeReference.DeriveContentKey(header, masterKey);
        byte[] encryptedEnvelope = EnvelopeReference.Encrypt(storeZip, header, contentKey);

        verify(encryptedEnvelope.Length > storeZip.Length, "Envelope encryption succeeded.");

        // Decrypt with passphrase
        byte[] decryptedZip = EnvelopeReference.Decrypt(encryptedEnvelope, passphrase);
        verify(decryptedZip.AsSpan().SequenceEqual(storeZip), "Decrypted payload byte-identical to original ZIP.");

        // Validate store documents from decrypted ZIP
        StoreSnapshot decryptedSnapshot = StoreArchiveReference.ValidateAndParseStoreZip(decryptedZip);
        verify(decryptedSnapshot.Profile.ProfileId == profileId, "End-to-end decrypted profile ID matches.");
        verify(decryptedSnapshot.Institutions.Institutions.Count == 1, "End-to-end decrypted institution count matches.");
        verify(decryptedSnapshot.Institutions.Institutions[0].DisplayName == "Sparkasse", "End-to-end institution name matches.");
    }

    private static byte[] CreateSingleDocZip(string name, byte[] content) =>
        StoreArchiveReference.CreateZip((name, content));

    private static int FindByteSequence(ReadOnlySpan<byte> source, ReadOnlySpan<byte> target)
    {
        for (int i = 0; i <= source.Length - target.Length; i++)
        {
            if (source.Slice(i, target.Length).SequenceEqual(target))
            {
                return i;
            }
        }
        return -1;
    }

    private static void Reject(Action action, Action<bool, string> verify, string name)
    {
        try
        {
            action();
            verify(false, $"{name} must fail closed.");
        }
        catch (Exception ex) when (ex is InvalidDataException or CryptographicException or
            ArgumentException or FormatException or XmlException or XmlSchemaException)
        {
            verify(true, name);
        }
    }
}
