using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using Broiler.Fond.Kernel.Domain.Accounts;
using Broiler.Fond.Kernel.Domain.Identifiers;
using Broiler.Fond.Kernel.Storage;

namespace Broiler.Fond.Kernel.Tests;

public static class CrossHostStoragePortabilityTests
{
    public static void Run()
    {
        Console.WriteLine("Running Cross-Host Storage Portability verification (M8-02)...");
        int checks = 0;

        void Assert(bool condition, string message)
        {
            if (!condition)
            {
                throw new InvalidOperationException($"Cross-Host Storage Portability check failed: {message}");
            }
            checks++;
        }

        // 1. Passphrase Normalization & Cross-Platform Master Key Equivalence
        byte[] profileSalt = new byte[32];
        RandomNumberGenerator.Fill(profileSalt);

        string composedPassphrase = "M\u00FCnchenSicher2026!";
        string decomposedPassphrase = "Mu\u0308nchenSicher2026!";

        bool keysMatch = CrossHostStoragePortability.VerifyNormalizedPassphraseDerivation(
            composedPassphrase, decomposedPassphrase, profileSalt);
        Assert(keysMatch, "Composed and decomposed Unicode passphrases derive byte-for-byte identical master keys.");

        // Additional accented vectors: Zürich, Café
        Assert(CrossHostStoragePortability.VerifyNormalizedPassphraseDerivation("Z\u00FCrichPass!", "Zu\u0308richPass!", profileSalt),
            "Zürich composed vs decomposed passphrases match.");
        Assert(CrossHostStoragePortability.VerifyNormalizedPassphraseDerivation("Caf\u00E9Secret!", "Cafe\u0301Secret!", profileSalt),
            "Café composed vs decomposed passphrases match.");

        // 2. Snapshot Serialization & Lexical Invariant Auditing
        Guid profileId = Guid.NewGuid();
        Guid branchId = Guid.NewGuid();
        StoreSnapshot initialSnapshot = StoreArchiveCodec.CreateEmptySnapshot(
            profileId, branchId, 1UL, DateTimeOffset.UtcNow, "CrossPlatformProfile", "Portability verification");

        var auditResult = CrossHostStoragePortability.AuditSnapshotArchive(initialSnapshot);
        Assert(auditResult.IsValid, "Initial snapshot passes cross-host portability audit.");
        Assert(auditResult.Errors.Count == 0, "Zero portability errors in store archive.");
        Assert(auditResult.ChecksPassed.Count >= 7, "All entries audited for strict LF newlines and flat paths.");

        // 3. Negative Auditing: Detect forbidden backslashes and CRLF newlines
        byte[] goodZip = StoreArchiveCodec.CreateStoreZip(initialSnapshot);

        // Synthesize corrupted ZIP with Windows backslash in entry name
        byte[] badPathZip;
        using (MemoryStream ms = new())
        {
            using (ZipArchive za = new(ms, ZipArchiveMode.Create, leaveOpen: true))
            {
                var entry = za.CreateEntry("invalid\\profile.xml");
                using var es = entry.Open();
                es.Write("<?xml version=\"1.0\" encoding=\"utf-8\"?>\n<root/>\n"u8);
            }
            badPathZip = ms.ToArray();
        }

        var badPathAudit = CrossHostStoragePortability.AuditZipBytes(badPathZip);
        Assert(!badPathAudit.IsValid, "Archive with backslash entry fails portability audit.");
        Assert(badPathAudit.Errors.Any(e => e.Contains("Windows backslash") || e.Contains("path separators")), "Reports forbidden path separator error.");

        // Synthesize corrupted ZIP with CRLF (\r\n) line endings
        byte[] crlfZip;
        using (MemoryStream ms = new())
        {
            using (ZipArchive za = new(ms, ZipArchiveMode.Create, leaveOpen: true))
            {
                var entry = za.CreateEntry("profile.xml");
                using var es = entry.Open();
                es.Write("<?xml version=\"1.0\" encoding=\"utf-8\"?>\r\n<root/>\r\n"u8);
            }
            crlfZip = ms.ToArray();
        }

        var crlfAudit = CrossHostStoragePortability.AuditZipBytes(crlfZip);
        Assert(!crlfAudit.IsValid, "Archive with CRLF newlines fails portability audit.");
        Assert(crlfAudit.Errors.Any(e => e.Contains("Carriage Return")), "Reports forbidden CRLF error.");

        // 4. POSIX vs Windows Path Parsing
        bool pathParsingOk = CrossHostStoragePortability.VerifyPathParsing(
            "/home/user/.fond/generation-00000000000000000005.bffond",
            @"C:\Users\User\AppData\Local\Fond\generation-00000000000000000005.bffond",
            expectedGeneration: 5UL);
        Assert(pathParsingOk, "Generation filename parsing succeeds on both POSIX and Windows path structures.");

        // 5. End-to-End Cross-Host ProfileStore Roundtrip & Restore
        string testRoot = Path.Combine(Path.GetTempPath(), "BroilerCrossHostTest", Guid.NewGuid().ToString("N"));
        string profileDir = Path.Combine(testRoot, "profile");
        string backupDir = Path.Combine(testRoot, "backup");
        string restoreDir = Path.Combine(testRoot, "restored");

        Directory.CreateDirectory(profileDir);
        Directory.CreateDirectory(backupDir);
        Directory.CreateDirectory(restoreDir);

        try
        {
            // Create store using decomposed passphrase (simulating macOS input)
            ProfileStore store = ProfileStore.CreateNew(
                profileDir,
                decomposedPassphrase,
                "CrossPlatformStore",
                note: "Created on simulated macOS host");

            Assert(store.State == ProfileStoreOpenState.Ready, "Store created in Ready state.");
            Assert(store.CurrentGeneration == 1UL, "Initial generation is 1.");

            // Mutate and save generation 2
            store.MutateAndSave(snap =>
            {
                ulong nextId = snap.Profile.NextId;
                ulong instId = nextId++;
                ulong connId = nextId++;
                ulong accId = nextId++;
                ulong incId = nextId++;
                ulong binId = nextId++;

                var inst = new StoreInstitutionEntry(instId, "Test Bank", "https://bank.example/fints", "37040044", "TESTDE00XXX", "Confirmed");
                var conn = new StoreConnectionEntry(connId, instId, "UserLogin", null, "Connected", "0", "Synchronized", 1, true, DateTimeOffset.UtcNow, DateTimeOffset.UtcNow, "3.0");
                var acc = new StoreAccountEntry(accId, "Girokonto Test", "EUR");
                var inc = new StoreIncarnationEntry(incId, accId, "FinTsDiscovery");
                var bin = new StoreBindingEntry(binId, accId, incId, instId, connId, "Active", "FinTs3", "3.0", "https://bank.example/fints", "DE89370400440532013000", null, null, null, "EUR", []);

                var ledList = new List<StoreLedgerRecord>(snap.Ledger.Records)
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

                return snap with
                {
                    Profile = snap.Profile with { NextId = nextId },
                    Institutions = snap.Institutions with { Institutions = [inst] },
                    Connections = snap.Connections with { Connections = [conn] },
                    Accounts = snap.Accounts with
                    {
                        Accounts = [acc],
                        Incarnations = [inc],
                        Bindings = [bin]
                    },
                    Ledger = snap.Ledger with { Records = ledList }
                };
            });

            Assert(store.CurrentGeneration == 2UL, "Store generation advanced to 2.");

            // Export backup
            string backupFile = Path.Combine(backupDir, "backup-gen2.bffond");
            store.ExportBackup(backupFile);
            Assert(File.Exists(backupFile), "Backup exported successfully.");

            // Close the writer
            store.Dispose();

            // Reopen store using composed passphrase (simulating Windows/Linux input)
            ProfileStore reopened = ProfileStore.Open(profileDir, composedPassphrase, openReadOnly: false);
            Assert(reopened.State == ProfileStoreOpenState.Ready, "Store unlocked with composed passphrase.");
            Assert(reopened.CurrentGeneration == 2UL, "Current generation 2 preserved.");
            Assert(reopened.CurrentSnapshot?.Accounts.Accounts.Count == 1, "Account entity preserved.");
            Assert(reopened.CurrentSnapshot?.Accounts.Accounts[0].DisplayName == "Girokonto Test", "Account name preserved.");

            // 6. Concurrency / Single-Writer Locking Enforcement
            bool conflictThrown = false;
            try
            {
                _ = ProfileStore.Open(profileDir, composedPassphrase, openReadOnly: false);
            }
            catch (ProfileLockConflictException)
            {
                conflictThrown = true;
            }
            Assert(conflictThrown, "Concurrent write lock acquisition threw ProfileLockConflictException.");

            // Read-only open concurrently succeeds
            ProfileStore roStore = ProfileStore.Open(profileDir, composedPassphrase, openReadOnly: true);
            Assert(roStore.State == ProfileStoreOpenState.Ready, "Concurrent read-only open succeeded.");
            roStore.Dispose();

            reopened.Dispose();

            // 7. Restore into empty profile directory with cross-host passphrase
            ProfileStore restored = ProfileStore.RestoreIntoEmptyProfile(restoreDir, backupFile, composedPassphrase);
            Assert(restored.State == ProfileStoreOpenState.Ready, "Backup restored successfully into new directory.");
            Assert(restored.CurrentGeneration == 2UL, "Restored generation matches source backup.");
            Assert(restored.CurrentSnapshot?.Accounts.Bindings[0].Iban == "DE89370400440532013000", "Restored IBAN matches source.");
            restored.Dispose();
        }
        finally
        {
            try
            {
                Directory.Delete(testRoot, recursive: true);
            }
            catch
            {
                // Best effort cleanup
            }
        }

        Console.WriteLine($"Cross-Host Storage Portability (M8-02): {checks} checks completed successfully.");
    }
}
