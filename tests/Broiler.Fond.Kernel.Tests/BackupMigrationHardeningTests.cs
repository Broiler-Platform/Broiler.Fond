using System.Security.Cryptography;
using Broiler.Fond.Kernel.Domain.Identifiers;
using Broiler.Fond.Kernel.Storage;

namespace Broiler.Fond.Kernel.Tests;

public static class BackupMigrationHardeningTests
{
    private static int _checksPassed;

    public static void RunAll()
    {
        _checksPassed = 0;

        TestLineageForkDetection_DivergentBranchGeneration();
        TestLineageForkDetection_ConflictingEntityIdentity();
        TestLineageForkDetection_ConflictingRevisionPredecessor();
        TestMergeSnapshots_RejectForkConflictsTrue();
        TestMergeSnapshots_RejectForkConflictsFalse_RemapsCleanly();
        TestMandatoryReauthenticationForImportedConnections();
        TestAcyclicRevisionGraphsAndReferenceIntegrityInMergedSnapshot();
        TestProfileStore_ImportAndMerge_EndToEnd();

        Console.WriteLine($"Backup migration, fork detection, and ID remapping hardening (M3-05): {_checksPassed} checks completed successfully.");
    }

    private static void Verify(bool condition, string message)
    {
        if (!condition)
        {
            throw new InvalidOperationException($"Assertion failed: {message}");
        }

        _checksPassed++;
    }

    private static void TestLineageForkDetection_DivergentBranchGeneration()
    {
        Guid profileId = Guid.NewGuid();
        Guid branchId = Guid.NewGuid();
        DateTimeOffset t1 = new(2026, 10, 18, 12, 0, 0, TimeSpan.Zero);
        DateTimeOffset t2 = t1.AddMinutes(5);

        StoreSnapshot target = BuildMinimalSnapshot(profileId, branchId, 2UL, 10UL, t1);
        StoreSnapshot importedDivergent = BuildMinimalSnapshot(profileId, branchId, 2UL, 12UL, t2);

        LineageForkReport report = LineageForkDetector.Detect(target, importedDivergent);
        Verify(report.HasConflicts, "Divergent timestamp/nextId on same branch and generation must report conflict.");
        Verify(report.Conflicts.Any(c => c.Kind == ForkConflictKind.DivergentBranchGeneration),
            "Report must contain DivergentBranchGeneration conflict.");

        // Identical generation on same branch has no conflict
        StoreSnapshot identical = BuildMinimalSnapshot(profileId, branchId, 2UL, 10UL, t1);
        LineageForkReport noConflictReport = LineageForkDetector.Detect(target, identical);
        Verify(!noConflictReport.HasConflicts, "Identical branch generation must report zero conflicts.");

        // Different branchId has no DivergentBranchGeneration conflict
        Guid otherBranch = Guid.NewGuid();
        StoreSnapshot otherBranchSnap = BuildMinimalSnapshot(profileId, otherBranch, 2UL, 10UL, t1);
        LineageForkReport otherBranchReport = LineageForkDetector.Detect(target, otherBranchSnap);
        Verify(!otherBranchReport.Conflicts.Any(c => c.Kind == ForkConflictKind.DivergentBranchGeneration),
            "Different branch IDs must not report DivergentBranchGeneration conflict.");
    }

    private static void TestLineageForkDetection_ConflictingEntityIdentity()
    {
        Guid profileId = Guid.NewGuid();
        Guid branch1 = Guid.NewGuid();
        Guid branch2 = Guid.NewGuid();
        DateTimeOffset now = new(2026, 10, 18, 12, 0, 0, TimeSpan.Zero);

        // Target has entity ID 5 as Account
        StoreSnapshot target = BuildMinimalSnapshot(profileId, branch1, 2UL, 20UL, now,
            extraLedger: [new StoreLedgerRecord(5, "Account", null, [])]);

        // Imported has entity ID 5 as Institution (conflicting kind)
        StoreSnapshot importedConflictingKind = BuildMinimalSnapshot(profileId, branch2, 2UL, 20UL, now,
            extraLedger: [new StoreLedgerRecord(5, "Institution", null, [])]);

        LineageForkReport reportKind = LineageForkDetector.Detect(target, importedConflictingKind);
        Verify(reportKind.HasConflicts, "Conflicting entity kind on same profile must report conflict.");
        Verify(reportKind.Conflicts.Any(c => c.Kind == ForkConflictKind.ConflictingEntityIdentity && c.EntityId == 5UL),
            "Report must identify ConflictingEntityIdentity on entity ID 5.");

        // Target and imported have entity ID 5 as Account, but imported has different references
        StoreSnapshot targetWithRef = BuildMinimalSnapshot(profileId, branch1, 2UL, 20UL, now,
            extraLedger: [new StoreLedgerRecord(5, "AccountBinding", null, [(1, "Connection")])]);
        StoreSnapshot importedWithDifferentRef = BuildMinimalSnapshot(profileId, branch2, 2UL, 20UL, now,
            extraLedger: [new StoreLedgerRecord(5, "AccountBinding", null, [(2, "Connection")])]);

        LineageForkReport reportRef = LineageForkDetector.Detect(targetWithRef, importedWithDifferentRef);
        Verify(reportRef.Conflicts.Any(c => c.Kind == ForkConflictKind.ConflictingEntityIdentity && c.EntityId == 5UL),
            "Report must identify ConflictingEntityIdentity when entity structure/references differ.");
    }

    private static void TestLineageForkDetection_ConflictingRevisionPredecessor()
    {
        Guid profileId = Guid.NewGuid();
        Guid branch1 = Guid.NewGuid();
        Guid branch2 = Guid.NewGuid();
        DateTimeOffset now = new(2026, 10, 18, 12, 0, 0, TimeSpan.Zero);

        // Both snapshots have distinct revisions claiming predecessor revision ID 10
        StoreSnapshot target = BuildMinimalSnapshot(profileId, branch1, 3UL, 30UL, now,
            extraLedger: [new StoreLedgerRecord(11, "TransactionRevision", 10UL, [])]);

        StoreSnapshot imported = BuildMinimalSnapshot(profileId, branch2, 3UL, 30UL, now,
            extraLedger: [new StoreLedgerRecord(12, "TransactionRevision", 10UL, [])]);

        LineageForkReport report = LineageForkDetector.Detect(target, imported);
        Verify(report.HasConflicts, "Two distinct revisions claiming predecessor 10 on the same profile must report conflict.");
        Verify(report.Conflicts.Any(c => c.Kind == ForkConflictKind.ConflictingRevisionPredecessor),
            "Report must identify ConflictingRevisionPredecessor.");

        // Different profiles do not report ConflictingRevisionPredecessor
        StoreSnapshot differentProfile = BuildMinimalSnapshot(Guid.NewGuid(), branch2, 3UL, 30UL, now,
            extraLedger: [new StoreLedgerRecord(12, "TransactionRevision", 10UL, [])]);
        LineageForkReport diffProfileReport = LineageForkDetector.Detect(target, differentProfile);
        Verify(!diffProfileReport.Conflicts.Any(c => c.Kind == ForkConflictKind.ConflictingRevisionPredecessor),
            "Different profiles must not report ConflictingRevisionPredecessor.");
    }

    private static void TestMergeSnapshots_RejectForkConflictsTrue()
    {
        Guid profileId = Guid.NewGuid();
        Guid branch1 = Guid.NewGuid();
        DateTimeOffset now = new(2026, 10, 18, 12, 0, 0, TimeSpan.Zero);

        // Snapshots on the same profile and same branch with divergent SavedAt timestamps (DivergentBranchGeneration)
        StoreSnapshot target = BuildPopulatedTestSnapshot(profileId, branch1, 2UL, 20UL, now);
        StoreSnapshot importedFork = BuildPopulatedTestSnapshot(profileId, branch1, 2UL, 20UL, now.AddMinutes(10));

        bool threw = false;
        try
        {
            StoreArchiveCodec.MergeSnapshotsWithIdRemapping(
                target, importedFork, profileId, branch1, 3UL, now.AddHours(1),
                rejectForkConflicts: true);
        }
        catch (InvalidOperationException ex) when (ex.Message.Contains("Lineage fork conflict detected"))
        {
            threw = true;
        }

        Verify(threw, "MergeSnapshotsWithIdRemapping with rejectForkConflicts: true must throw InvalidOperationException on fork conflict.");
    }

    private static void TestMergeSnapshots_RejectForkConflictsFalse_RemapsCleanly()
    {
        Guid profileId = Guid.NewGuid();
        Guid branch1 = Guid.NewGuid();
        Guid branch2 = Guid.NewGuid();
        DateTimeOffset now = new(2026, 10, 18, 12, 0, 0, TimeSpan.Zero);

        StoreSnapshot target = BuildPopulatedTestSnapshot(profileId, branch1, 2UL, 20UL, now);
        StoreSnapshot imported = BuildPopulatedTestSnapshot(profileId, branch2, 2UL, 20UL, now);

        ulong targetNextIdBeforeMerge = target.Profile.NextId;

        StoreSnapshot merged = StoreArchiveCodec.MergeSnapshotsWithIdRemapping(
            target, imported, profileId, branch1, 3UL, now.AddHours(1),
            rejectForkConflicts: false);

        Verify(merged.Profile.NextId > targetNextIdBeforeMerge, "Target NextId counter must advance past all remapped IDs.");
        Verify(merged.Institutions.Institutions.Count == 2, "Merged snapshot must contain 2 institutions.");
        Verify(merged.Connections.Connections.Count == 2, "Merged snapshot must contain 2 connections.");
        Verify(merged.Accounts.Accounts.Count == 2, "Merged snapshot must contain 2 accounts.");
        Verify(merged.Accounts.Bindings.Count == 2, "Merged snapshot must contain 2 bindings.");
        Verify(merged.Balances.Balances.Count == 2, "Merged snapshot must contain 2 balances.");

        // Imported entities were allocated IDs >= targetNextIdBeforeMerge
        StoreInstitutionEntry importedInst = merged.Institutions.Institutions[1];
        StoreConnectionEntry importedConn = merged.Connections.Connections[1];
        StoreAccountEntry importedAcc = merged.Accounts.Accounts[1];
        StoreBindingEntry importedBin = merged.Accounts.Bindings[1];

        Verify(importedInst.Id >= targetNextIdBeforeMerge, "Imported institution ID must be remapped >= targetNextIdBeforeMerge.");
        Verify(importedConn.Id >= targetNextIdBeforeMerge, "Imported connection ID must be remapped >= targetNextIdBeforeMerge.");
        Verify(importedConn.InstitutionId == importedInst.Id, "Imported connection's institution reference must point to remapped institution ID.");
        Verify(importedAcc.Id >= targetNextIdBeforeMerge, "Imported account ID must be remapped >= targetNextIdBeforeMerge.");
        Verify(importedBin.AccountId == importedAcc.Id, "Imported binding's account reference must point to remapped account ID.");
        Verify(importedBin.ConnectionId == importedConn.Id, "Imported binding's connection reference must point to remapped connection ID.");

        // Full invariant validation passes
        StoreArchiveCodec.ValidateIdentityAndSemanticInvariants(merged);
        _checksPassed++;
    }

    private static void TestMandatoryReauthenticationForImportedConnections()
    {
        Guid profileId = Guid.NewGuid();
        Guid branch1 = Guid.NewGuid();
        Guid branch2 = Guid.NewGuid();
        DateTimeOffset now = new(2026, 10, 18, 12, 0, 0, TimeSpan.Zero);

        StoreSnapshot target = BuildPopulatedTestSnapshot(profileId, branch1, 2UL, 50UL, now, targetConnState: "Connected");

        // Build imported snapshot with various connection states
        StoreSnapshot imported = BuildMultiConnectionSnapshot(profileId, branch2, 2UL, 50UL, now,
            connStates: ["Connected", "Authenticating", "ChallengeRequired", "TemporarilyUnavailable", "Disabled", "Unconfigured"]);

        StoreSnapshot merged = StoreArchiveCodec.MergeSnapshotsWithIdRemapping(
            target, imported, profileId, branch1, 3UL, now.AddHours(1),
            rejectForkConflicts: false);

        // Target connection should remain untouched ("Connected")
        Verify(merged.Connections.Connections[0].State == "Connected",
            "Target connection state must remain untouched.");

        // Imported connections in active or transient states must transition to ReauthenticationRequired
        var importedConns = merged.Connections.Connections.Skip(1).ToList();
        Verify(importedConns[0].State == "ReauthenticationRequired", "Imported 'Connected' must transition to ReauthenticationRequired.");
        Verify(importedConns[1].State == "ReauthenticationRequired", "Imported 'Authenticating' must transition to ReauthenticationRequired.");
        Verify(importedConns[2].State == "ReauthenticationRequired", "Imported 'ChallengeRequired' must transition to ReauthenticationRequired.");
        Verify(importedConns[3].State == "ReauthenticationRequired", "Imported 'TemporarilyUnavailable' must transition to ReauthenticationRequired.");

        // Inactive states must be preserved as-is
        Verify(importedConns[4].State == "Disabled", "Imported 'Disabled' state must be preserved as-is.");
        Verify(importedConns[5].State == "Unconfigured", "Imported 'Unconfigured' state must be preserved as-is.");
    }

    private static void TestAcyclicRevisionGraphsAndReferenceIntegrityInMergedSnapshot()
    {
        Guid profileId = Guid.NewGuid();
        Guid branch1 = Guid.NewGuid();
        Guid branch2 = Guid.NewGuid();
        DateTimeOffset now = new(2026, 10, 18, 12, 0, 0, TimeSpan.Zero);

        // Target snapshot with transaction and revisions
        StoreSnapshot target = BuildSnapshotWithTransactionRevisions(profileId, branch1, 2UL, 50UL, now, prefix: "T");
        StoreSnapshot imported = BuildSnapshotWithTransactionRevisions(profileId, branch2, 2UL, 50UL, now, prefix: "I");

        StoreSnapshot merged = StoreArchiveCodec.MergeSnapshotsWithIdRemapping(
            target, imported, profileId, branch1, 3UL, now.AddHours(1),
            rejectForkConflicts: false);

        // Verify transaction observations, transactions, revisions, and annotations
        var txDoc = merged.TransactionsOrDefault;
        Verify(txDoc.Observations.Count == 2, "Merged snapshot must contain 2 observations.");
        Verify(txDoc.Transactions.Count == 2, "Merged snapshot must contain 2 transactions.");
        Verify(txDoc.Revisions.Count == 4, "Merged snapshot must contain 4 revisions (2 original + 2 imported).");
        Verify(txDoc.Annotations.Count == 2, "Merged snapshot must contain 2 annotations.");

        // Check that remapped imported revisions point to remapped imported transaction ID
        var importedTx = txDoc.Transactions[1];
        var importedRevs = txDoc.Revisions.Where(r => r.TransactionId == importedTx.Id).ToList();
        Verify(importedRevs.Count == 2, "Imported transaction must have 2 remapped revisions.");
        Verify(importedRevs[0].AccountId == importedTx.AccountId, "Remapped revision must match remapped account ID.");

        // Check remapped annotation points to remapped transaction
        var importedAnn = txDoc.Annotations[1];
        Verify(importedAnn.TransactionId == importedTx.Id, "Remapped annotation must point to remapped transaction ID.");

        // Full invariant check validates acyclic revision graph and reference integrity
        StoreArchiveCodec.ValidateIdentityAndSemanticInvariants(merged);
        _checksPassed++;
    }

    private static void TestProfileStore_ImportAndMerge_EndToEnd()
    {
        string dirTarget = Path.Combine(Path.GetTempPath(), "bf-m305-target-" + Guid.NewGuid().ToString("N"));
        string dirImported = Path.Combine(Path.GetTempPath(), "bf-m305-import-" + Guid.NewGuid().ToString("N"));
        string backupFile = Path.Combine(dirImported, "backup-source.bffond");

        try
        {
            Directory.CreateDirectory(dirTarget);
            Directory.CreateDirectory(dirImported);

            const string passphraseTarget = "TargetStoreSecret123!";
            const string passphraseImport = "ImportStoreSecret456!";

            Guid targetPid = Guid.NewGuid();
            Guid targetBid = Guid.NewGuid();
            ulong genBefore;
            using (ProfileStore targetStore = ProfileStore.CreateNew(dirTarget, passphraseTarget, "Target Profile", "Initial", targetPid, targetBid))
            {
                targetStore.Save(BuildPopulatedTestSnapshot(targetPid, targetBid, 2UL, 20UL, DateTimeOffset.UtcNow, targetConnState: "Connected"));

                Guid importPid = Guid.NewGuid();
                Guid importBid = Guid.NewGuid();
                using (ProfileStore importStore = ProfileStore.CreateNew(dirImported, passphraseImport, "Imported Profile", "Initial", importPid, importBid))
                {
                    importStore.Save(BuildPopulatedTestSnapshot(importPid, importBid, 2UL, 20UL, DateTimeOffset.UtcNow, targetConnState: "Connected"));
                    importStore.ExportBackup(backupFile);
                }

                Verify(File.Exists(backupFile), "ExportBackup must produce verified backup file.");

                // Import and merge into targetStore
                genBefore = targetStore.CurrentGeneration;
                ProfileSaveMetrics metrics = targetStore.ImportAndMerge(backupFile, passphraseImport, rejectForkConflicts: true);

                Verify(metrics.Generation == genBefore + 1, "ImportAndMerge must advance generation by 1.");
                Verify(targetStore.CurrentSnapshot!.Connections.Connections.Count == 2, "Target store must have 2 connections after merge.");

                // Verify imported connection transitioned to ReauthenticationRequired
                StoreConnectionEntry importedConn = targetStore.CurrentSnapshot.Connections.Connections[1];
                Verify(importedConn.State == "ReauthenticationRequired",
                    "Imported connection in active state must transition to ReauthenticationRequired.");
            }

            // Verify reopen targetStore after disposing write lock
            using ProfileStore reopened = ProfileStore.Open(dirTarget, passphraseTarget);
            Verify(reopened.CurrentGeneration == genBefore + 1, "Reopened store must open at merged generation.");
            Verify(reopened.CurrentSnapshot!.Accounts.Accounts.Count == 2, "Reopened store must contain both accounts.");
        }
        finally
        {
            try { Directory.Delete(dirTarget, recursive: true); } catch { }
            try { Directory.Delete(dirImported, recursive: true); } catch { }
        }
    }

    // ==========================================
    // Test Helpers
    // ==========================================

    private static StoreSnapshot BuildMinimalSnapshot(
        Guid profileId,
        Guid branchId,
        ulong generation,
        ulong nextId,
        DateTimeOffset savedAt,
        IReadOnlyList<StoreLedgerRecord>? extraLedger = null)
    {
        List<StoreLedgerRecord> records = [new StoreLedgerRecord(1, "Institution", null, [])];
        if (extraLedger != null)
        {
            records.AddRange(extraLedger);
        }

        return new StoreSnapshot(
            new StoreProfileDocument(profileId, branchId, null, null, generation, nextId, savedAt, "Test", "Minimal"),
            new StoreInstitutionsDocument(profileId, branchId, generation, [new StoreInstitutionEntry(1, "Bank", "https://bank.example/fints", "10020030", null, "Confirmed")]),
            new StoreConnectionsDocument(profileId, branchId, generation, []),
            new StoreAccountsDocument(profileId, branchId, generation, [], [], [], []),
            new StoreBalancesDocument(profileId, branchId, generation, []),
            new StoreLedgerDocument(profileId, branchId, generation, records),
            new StoreTransactionsDocument(profileId, branchId, generation, [], [], [], [], [], []));
    }

    private static StoreSnapshot BuildPopulatedTestSnapshot(
        Guid profileId,
        Guid branchId,
        ulong generation,
        ulong nextId,
        DateTimeOffset savedAt,
        string targetConnState = "Connected",
        bool corruptEntityKindOnId2 = false)
    {
        List<StoreLedgerRecord> ledger =
        [
            new(1, "Institution", null, []),
            new(2, corruptEntityKindOnId2 ? "Account" : "Connection", null, [(1, "Institution")]),
            new(3, "Account", null, []),
            new(4, "AccountIncarnation", null, [(3, "Account")]),
            new(5, "AccountBinding", null, [(4, "AccountIncarnation"), (2, "Connection")]),
            new(6, "Observation", null, [(5, "AccountBinding")]),
            new(7, "BalanceSnapshot", null, [(5, "AccountBinding"), (6, "Observation")]),
        ];

        return new StoreSnapshot(
            new StoreProfileDocument(profileId, branchId, null, null, generation, nextId, savedAt, "Test", "Populated"),
            new StoreInstitutionsDocument(profileId, branchId, generation, [new StoreInstitutionEntry(1, "Bank", "https://bank.example/fints", "10020030", null, "Confirmed")]),
            new StoreConnectionsDocument(profileId, branchId, generation, [new StoreConnectionEntry(2, 1, "user1", null, targetConnState, "SYS1", "Sync", 1, true, savedAt, savedAt, "FinTS 3.0 PIN/TAN")]),
            new StoreAccountsDocument(profileId, branchId, generation,
                [new StoreAccountEntry(3, "Girokonto", "EUR")],
                [new StoreIncarnationEntry(4, 3, "Active")],
                [new StoreBindingEntry(5, 3, 4, 1, 2, "Active", "FinTS 3.0 PIN/TAN", "1.0", "https://bank.example/fints", "DE89370400440532013000", "0532013000", "37040044", null, "EUR", [])],
                []),
            new StoreBalancesDocument(profileId, branchId, generation, [new StoreBalanceEntry(7, 6, 3, 4, 5, "Booked", "1250.00", "EUR", savedAt, savedAt, false, "SALDO")]),
            new StoreLedgerDocument(profileId, branchId, generation, ledger),
            new StoreTransactionsDocument(profileId, branchId, generation, [], [], [], [], [], []));
    }

    private static StoreSnapshot BuildMultiConnectionSnapshot(
        Guid profileId,
        Guid branchId,
        ulong generation,
        ulong nextId,
        DateTimeOffset savedAt,
        IReadOnlyList<string> connStates)
    {
        List<StoreConnectionEntry> conns = [];
        List<StoreLedgerRecord> ledger = [new(1, "Institution", null, [])];

        ulong currentId = 2;
        foreach (string state in connStates)
        {
            ulong cid = currentId++;
            conns.Add(new StoreConnectionEntry(cid, 1, $"user_{cid}", null, state, $"SYS{cid}", "Sync", 1, true, savedAt, savedAt, "FinTS 3.0 PIN/TAN"));
            ledger.Add(new StoreLedgerRecord(cid, "Connection", null, [(1, "Institution")]));
        }

        return new StoreSnapshot(
            new StoreProfileDocument(profileId, branchId, null, null, generation, nextId + (ulong)connStates.Count, savedAt, "Test", "MultiConn"),
            new StoreInstitutionsDocument(profileId, branchId, generation, [new StoreInstitutionEntry(1, "Bank", "https://bank.example/fints", "10020030", null, "Confirmed")]),
            new StoreConnectionsDocument(profileId, branchId, generation, conns),
            new StoreAccountsDocument(profileId, branchId, generation, [], [], [], []),
            new StoreBalancesDocument(profileId, branchId, generation, []),
            new StoreLedgerDocument(profileId, branchId, generation, ledger),
            new StoreTransactionsDocument(profileId, branchId, generation, [], [], [], [], [], []));
    }

    private static StoreSnapshot BuildSnapshotWithTransactionRevisions(
        Guid profileId,
        Guid branchId,
        ulong generation,
        ulong nextId,
        DateTimeOffset savedAt,
        string prefix)
    {
        List<StoreLedgerRecord> ledger =
        [
            new(1, "Institution", null, []),
            new(2, "Connection", null, [(1, "Institution")]),
            new(3, "Account", null, []),
            new(4, "AccountIncarnation", null, [(3, "Account")]),
            new(5, "AccountBinding", null, [(4, "AccountIncarnation"), (2, "Connection")]),
            new(6, "Observation", null, [(5, "AccountBinding")]),
            new(7, "Transaction", null, [(3, "Account"), (4, "AccountIncarnation")]),
            new(8, "TransactionRevision", null, [(7, "Transaction"), (6, "Observation"), (3, "Account"), (4, "AccountIncarnation"), (5, "AccountBinding")]),
            new(9, "TransactionRevision", 8UL, [(7, "Transaction"), (6, "Observation"), (3, "Account"), (4, "AccountIncarnation"), (5, "AccountBinding")]),
            new(10, "AnnotationRevision", null, [(7, "Transaction")]),
        ];

        StoreTransactionObservationEntry obs = new(6, 5, savedAt, "CAMT.053", "001", null);
        StoreTransactionEntry tx = new(7, 3, 4, "Booked", "Transfer");
        StoreTransactionRevisionEntry rev1 = new(8, 7, 6, 3, 4, 5, "Booked", "Transfer", "50.00", "EUR", false,
            new DateOnly(2026, 10, 15), new DateOnly(2026, 10, 15), savedAt, savedAt,
            $"{prefix} Counterparty", null, null, $"{prefix} Note 1", null, null, null, null, null, null, null, null);
        StoreTransactionRevisionEntry rev2 = new(9, 7, 6, 3, 4, 5, "Booked", "Transfer", "50.00", "EUR", false,
            new DateOnly(2026, 10, 15), new DateOnly(2026, 10, 15), savedAt, savedAt,
            $"{prefix} Counterparty Corrected", null, null, $"{prefix} Note 2", null, null, null, null, null, null, null, null);
        StoreAnnotationRevisionEntry ann = new(10, 7, savedAt, $"{prefix} User Note", "Groceries", ["tag1"]);

        return new StoreSnapshot(
            new StoreProfileDocument(profileId, branchId, null, null, generation, nextId, savedAt, "Test", "Revisions"),
            new StoreInstitutionsDocument(profileId, branchId, generation, [new StoreInstitutionEntry(1, "Bank", "https://bank.example/fints", "10020030", null, "Confirmed")]),
            new StoreConnectionsDocument(profileId, branchId, generation, [new StoreConnectionEntry(2, 1, "user1", null, "Connected", "SYS1", "Sync", 1, true, savedAt, savedAt, "FinTS 3.0 PIN/TAN")]),
            new StoreAccountsDocument(profileId, branchId, generation,
                [new StoreAccountEntry(3, "Girokonto", "EUR")],
                [new StoreIncarnationEntry(4, 3, "Active")],
                [new StoreBindingEntry(5, 3, 4, 1, 2, "Active", "FinTS 3.0 PIN/TAN", "1.0", "https://bank.example/fints", "DE89370400440532013000", "0532013000", "37040044", null, "EUR", [])],
                []),
            new StoreBalancesDocument(profileId, branchId, generation, []),
            new StoreLedgerDocument(profileId, branchId, generation, ledger),
            new StoreTransactionsDocument(profileId, branchId, generation, [obs], [tx], [rev1, rev2], [ann], [], []));
    }
}
