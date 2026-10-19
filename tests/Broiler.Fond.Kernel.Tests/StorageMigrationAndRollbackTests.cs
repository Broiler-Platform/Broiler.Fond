using Broiler.Fond.Kernel.Domain.Accounts;
using Broiler.Fond.Kernel.Domain.Identifiers;
using Broiler.Fond.Kernel.Storage;

namespace Broiler.Fond.Kernel.Tests;

public static class StorageMigrationAndRollbackTests
{
    public static void Run()
    {
        Console.WriteLine("Running Storage Migration & Rollback Integrity verification (M7-02)...");
        int checks = 0;

        void Assert(bool condition, string message)
        {
            if (!condition)
            {
                throw new InvalidOperationException($"Storage Migration check failed: {message}");
            }
            checks++;
        }

        Guid profileId = Guid.NewGuid();
        Guid branchId = Guid.NewGuid();
        ulong gen = 1;
        var now = DateTimeOffset.UtcNow;

        // 1. Multi-version migration & transparent defaults
        var m1Profile = new StoreProfileDocument(profileId, branchId, null, null, gen, 500, now, "Legacy M1 Profile", "Initial Note");
        var m1Inst = new StoreInstitutionsDocument(profileId, branchId, gen, []);
        var m1Conn = new StoreConnectionsDocument(profileId, branchId, gen, []);
        var m1Acc = new StoreAccountsDocument(profileId, branchId, gen, [], [], [], []);
        var m1Bal = new StoreBalancesDocument(profileId, branchId, gen, []);
        var m1Ledger = new StoreLedgerDocument(profileId, branchId, gen, [
            new StoreLedgerRecord(100, "Account", null, []),
            new StoreLedgerRecord(200, "BalanceSnapshot", null, [])
        ]);

        // M1 snapshot lacking Transactions, Payments, Templates
        var legacySnap = new StoreSnapshot(
            Profile: m1Profile,
            Institutions: m1Inst,
            Connections: m1Conn,
            Accounts: m1Acc,
            Balances: m1Bal,
            Ledger: m1Ledger,
            Transactions: null,
            Payments: null,
            Templates: null);

        var migrated = StoreMigrationEngine.MigrateAndValidate(legacySnap);
        Assert(migrated.Transactions is not null, "Migrated snapshot populates Transactions document.");
        Assert(migrated.Payments is not null, "Migrated snapshot populates Payments document.");
        Assert(migrated.Templates is not null, "Migrated snapshot populates Templates document.");
        Assert(migrated.Transactions!.Transactions.Count == 0, "Default transactions document is empty.");
        Assert(migrated.Payments!.Attempts.Count == 0, "Default payments document is empty.");
        Assert(migrated.Templates!.Templates.Count == 0, "Default templates document is empty.");
        Assert(migrated.Transactions.ProfileId == profileId, "Default transactions document preserves ProfileId.");
        Assert(migrated.Transactions.Generation == gen, "Default transactions document preserves Generation.");

        // Monotonicity check
        var nonMonotonicSnap = legacySnap with
        {
            Profile = m1Profile with { NextId = 150 } // Max(Id) is 200!
        };
        try
        {
            StoreMigrationEngine.MigrateAndValidate(nonMonotonicSnap);
            Assert(false, "MigrateAndValidate must throw on NextId <= Max(Id).");
        }
        catch (InvalidOperationException)
        {
            Assert(true, "MigrateAndValidate threw on NextId monotonicity violation.");
        }

        // 2. Strict downgrade guard
        StoreDowngradeGuard.CheckFormatVersion(1); // must not throw
        Assert(true, "CheckFormatVersion(1) succeeded.");

        try
        {
            StoreDowngradeGuard.CheckFormatVersion(2);
            Assert(false, "CheckFormatVersion(2) must throw StoreDowngradeException.");
        }
        catch (StoreDowngradeException ex)
        {
            Assert(ex.ArchiveFormatVersion == 2, "Exception captures archive format version 2.");
            Assert(ex.MaxSupportedVersion == 1, "Exception captures max supported version 1.");
            Assert(ex.Message.Contains("update Broiler Fond"), "Message directs user to update.");
        }

        // Schema validation guard
        string[] validSchemas = ["store-v1.xsd", "transactions-v1.xsd", "payments-v1.xsd", "templates-v1.xsd"];
        StoreDowngradeGuard.CheckManifestSchemas(validSchemas);
        Assert(true, "CheckManifestSchemas succeeded for known schemas.");

        string[] futureSchemas = ["store-v1.xsd", "future-ai-trading-v9.xsd"];
        try
        {
            StoreDowngradeGuard.CheckManifestSchemas(futureSchemas);
            Assert(false, "CheckManifestSchemas must throw on unrecognized schema.");
        }
        catch (StoreDowngradeException)
        {
            Assert(true, "CheckManifestSchemas threw StoreDowngradeException for unknown schema.");
        }

        // 3. Lineage rollback guard
        LineageRollbackGuard.ResetForTesting();
        LineageRollbackGuard.RecordGeneration(profileId, branchId, 10);
        LineageRollbackGuard.VerifyGeneration(profileId, branchId, 10);
        LineageRollbackGuard.VerifyGeneration(profileId, branchId, 11);
        Assert(true, "Generations >= 10 verified successfully.");

        LineageRollbackGuard.RecordGeneration(profileId, branchId, 15);
        try
        {
            LineageRollbackGuard.VerifyGeneration(profileId, branchId, 14);
            Assert(false, "VerifyGeneration must throw RollbackDetectedException for generation 14 < 15.");
        }
        catch (RollbackDetectedException rex)
        {
            Assert(rex.KnownHighestGeneration == 15, "Exception captures known highest generation 15.");
            Assert(rex.CandidateGeneration == 14, "Exception captures candidate generation 14.");
        }

        // 4. Cryptographic profile store save and backup roundtrip
        string tempDir = Path.Combine(Path.GetTempPath(), "BroilerFondMigrationTest", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);
        const string passphrase = "Migration-Passphrase-2026!";

        try
        {
            using (var store = ProfileStore.CreateNew(tempDir, passphrase, "Migration Test Profile", "Initial Gen", profileId, branchId))
            {
                var snap = store.CurrentSnapshot ?? throw new InvalidOperationException("Snapshot is null");
                Assert(snap.Profile.Generation == 1, "Initial generation is 1.");

                // Save an updated generation with accounts
                var inst = new StoreInstitutionEntry(10, "Bank", "https://localhost", "10050000", null, "Confirmed");
                var conn = new StoreConnectionEntry(20, 10, "user", null, "Synchronized", "sys", "Active", 1, true, now, now, "3.0");
                var acc = new StoreAccountEntry(100, "Checking", "EUR");
                var inc = new StoreIncarnationEntry(150, 100, "Active");
                var binding = new StoreBindingEntry(200, 100, 150, 10, 20, "Active", "FinTs", "1", "https://localhost", "DE00", "00", "100", null, "EUR", []);

                var records = new List<StoreLedgerRecord>
                {
                    new(10, "Institution", null, []),
                    new(20, "Connection", null, [(10, "Institution")]),
                    new(100, "Account", null, []),
                    new(150, "AccountIncarnation", null, [(100, "Account")]),
                    new(200, "AccountBinding", null, [(150, "AccountIncarnation"), (20, "Connection")])
                };

                var newSnap = snap with
                {
                    Profile = snap.Profile with { Generation = 2, NextId = 500 },
                    Institutions = snap.Institutions with { Generation = 2, Institutions = [inst] },
                    Connections = snap.Connections with { Generation = 2, Connections = [conn] },
                    Accounts = snap.Accounts with
                    {
                        Generation = 2,
                        Accounts = [acc],
                        Incarnations = [inc],
                        Bindings = [binding]
                    },
                    Ledger = snap.Ledger with
                    {
                        Generation = 2,
                        Records = records
                    }
                };

                store.Save(newSnap);
                Assert(store.CurrentGeneration == 2, "Store current generation updated to 2.");

                // Export encrypted backup
                string backupPath = Path.Combine(tempDir, "profile_backup.fondbackup");
                store.ExportBackup(backupPath);
                Assert(File.Exists(backupPath), "Backup file was created.");
                var fileInfo = new FileInfo(backupPath);
                Assert(fileInfo.Length > 256, "Backup file contains encrypted container payload.");
            }

            // Reopen store from disk and verify persistence and migration
            using (var reopened = ProfileStore.Open(tempDir, passphrase))
            {
                Assert(reopened.CurrentGeneration == 2, "Reopened store loaded generation 2.");
                var reloadedSnap = reopened.CurrentSnapshot ?? throw new InvalidOperationException("Reloaded snapshot is null");
                var validated = StoreMigrationEngine.MigrateAndValidate(reloadedSnap);
                Assert(validated.Accounts.Accounts.Count == 1, "Account record preserved across reopen.");
                Assert(validated.Accounts.Accounts[0].DisplayName == "Checking", "Account name preserved.");
            }
        }
        finally
        {
            try
            {
                if (Directory.Exists(tempDir))
                {
                    Directory.Delete(tempDir, recursive: true);
                }
            }
            catch { }
        }

        Console.WriteLine($"Storage Migration & Rollback Integrity (M7-02): {checks} checks completed successfully.");
    }
}
