using System.Security.Cryptography;
using System.Text;
using Broiler.Fond.Kernel.Diagnostics;
using Broiler.Fond.Kernel.Storage;

namespace Broiler.Fond.Kernel.Tests.Storage;

/// <summary>
/// Executable verification suite for M1-08 Connection Removal, Cached-Data Purge,
/// and Encrypted Local Diagnostic Persistence (<see cref="ConnectionRemovalPlanner"/>,
/// <see cref="IProfileStore.RemoveConnection"/>, and <see cref="EncryptedLocalDiagnosticStore"/>).
/// </summary>
internal static class DisconnectAndDiagnosticPersistenceTests
{
    private const string TestPassphrase = "CorrectHorseBatteryStaple-Disconnect-2026!";
    private static readonly DateTimeOffset BaseTime = new(2026, 10, 16, 12, 0, 0, TimeSpan.Zero);

    internal static void Run(Action<bool, string> check)
    {
        int checks = 0;
        void Verify(bool condition, string message)
        {
            checks++;
            check(condition, message);
        }

        VerifyRetainCachedAccountsDisposition(Verify);
        VerifyDeleteCachedAccountsAndPurgeSingleConnection(Verify);
        VerifyDeleteCachedAccountsPreservesSharedAccountAndInstitution(Verify);
        VerifyEncryptedLocalDiagnosticStoreLifecycle(Verify);
        VerifyEncryptedDiagnosticSchemaAndTamperRejection(Verify);
        VerifyReviewedSupportPreviewExport(Verify);

        Console.WriteLine($"Disconnect & diagnostic persistence: {checks} checks completed successfully.");
    }

    private static string CreateTempDirectory()
    {
        string path = Path.Combine(Path.GetTempPath(), "BroilerFondDisconnectTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return path;
    }

    private static void CleanupDirectory(string path)
    {
        try
        {
            if (Directory.Exists(path))
            {
                Directory.Delete(path, recursive: true);
            }
        }
        catch
        {
            // Best-effort cleanup of temp directory.
        }
    }

    private static StoreSnapshot BuildSingleConnectionSnapshot(Guid profileId, Guid branchId, ulong generation)
    {
        return new StoreSnapshot(
            Profile: new StoreProfileDocument(
                ProfileId: profileId,
                BranchId: branchId,
                ParentBranchId: null,
                RecoveryProvenance: null,
                Generation: generation,
                NextId: 9,
                SavedAt: BaseTime,
                Label: "M1-08 Test Profile",
                Note: "Initial state"),
            Institutions: new StoreInstitutionsDocument(
                profileId,
                branchId,
                generation,
                [
                    new StoreInstitutionEntry(1, "Sparkasse Test", "https://fints.sparkasse.example/fints", "10020030", "SPUEDE2UXXX", "Confirmed"),
                ]),
            Connections: new StoreConnectionsDocument(
                profileId,
                branchId,
                generation,
                [
                    new StoreConnectionEntry(2, 1, "user100", "cust100", "Connected", "SYS-001", "Synchronized", 2, true, BaseTime, BaseTime, "FinTS 3.0 PIN/TAN"),
                ]),
            Accounts: new StoreAccountsDocument(
                profileId,
                branchId,
                generation,
                Accounts:
                [
                    new StoreAccountEntry(3, "Girokonto", "EUR"),
                ],
                Incarnations:
                [
                    new StoreIncarnationEntry(4, 3, "Active"),
                ],
                Bindings:
                [
                    new StoreBindingEntry(
                        5, 3, 4, 1, 2, "Active", "FinTS 3.0 PIN/TAN", "1.0",
                        "https://fints.sparkasse.example/fints",
                        "DE89370400440532013000", "0532013000", "37040044", "00", "EUR",
                        [("DE.SPARKASSE", "ACCOUNT", "INTERNAL", "ACC-0001")]),
                ],
                Capabilities:
                [
                    new StoreCapabilityEntry(2, 3, "HKSAL", 7, "Active", "10000.00"),
                ]),
            Balances: new StoreBalancesDocument(
                profileId,
                branchId,
                generation,
                [
                    new StoreBalanceEntry(7, 6, 3, 4, 5, "Booked", "1250.50", "EUR", BaseTime, BaseTime, false, "SALDO"),
                ]),
            Ledger: new StoreLedgerDocument(
                profileId,
                branchId,
                generation,
                [
                    new StoreLedgerRecord(1, "Institution", null, []),
                    new StoreLedgerRecord(2, "Connection", null, [(1, "Institution")]),
                    new StoreLedgerRecord(3, "Account", null, []),
                    new StoreLedgerRecord(4, "AccountIncarnation", null, [(3, "Account")]),
                    new StoreLedgerRecord(5, "AccountBinding", null, [(4, "AccountIncarnation"), (2, "Connection")]),
                    new StoreLedgerRecord(6, "Observation", null, [(5, "AccountBinding")]),
                    new StoreLedgerRecord(7, "BalanceSnapshot", null, [(5, "AccountBinding"), (6, "Observation")]),
                ]));
    }

    private static void VerifyRetainCachedAccountsDisposition(Action<bool, string> check)
    {
        string dir = CreateTempDirectory();
        try
        {
            Guid profileId = Guid.NewGuid();
            Guid branchId = Guid.NewGuid();
            using ProfileStore store = ProfileStore.CreateNew(dir, TestPassphrase, "M1-08 Retain Test", "Gen 1", profileId, branchId);

            StoreSnapshot snap2 = BuildSingleConnectionSnapshot(profileId, branchId, generation: 2);
            store.Save(snap2, BaseTime.AddMinutes(1));

            int filesBefore = Directory.GetFiles(dir, "generation-*.bffond").Length;
            check(filesBefore == 2, "Two generations must exist on disk before connection removal.");

            ConnectionRemovalResult result = store.RemoveConnection(
                connectionId: 2,
                disposition: ConnectionRemovalDataDisposition.RetainCachedAccountsAndBalances,
                passphrase: TestPassphrase,
                savedAt: BaseTime.AddMinutes(5),
                note: "Disconnect retaining cached data");

            check(result.RemovedConnectionId == 2, "Result must report the removed connection ID.");
            check(result.Disposition == ConnectionRemovalDataDisposition.RetainCachedAccountsAndBalances, "Result must report RetainCachedAccountsAndBalances.");
            check(result.RemovedAccountIds.Count == 0, "Retain mode must not remove any accounts.");
            check(result.RemovedBalanceIds.Count == 0, "Retain mode must not remove any balances.");
            check(result.DeactivatedBindingIds.SequenceEqual([5UL]), "Retain mode must transition binding 5 to Inactive.");
            check(!result.RemovedInstitutionId.HasValue, "Retain mode must retain the institution.");
            check(result.ExternalCopyDisclosureRequired, "External copy disclosure flag must be true.");

            string[] filesAfter = Directory.GetFiles(dir, "generation-*.bffond");
            check(filesAfter.Length == 1, "RemoveConnection must execute a privacy purge so superseded generations are deleted from disk.");

            StoreSnapshot post = store.CurrentSnapshot!;
            check(post.Connections.Connections.Count == 0, "Connection entry must be removed from connections.xml.");
            check(post.Accounts.Capabilities.Count == 0, "Connection capabilities must be removed from accounts.xml.");
            check(post.Accounts.Accounts.Count == 1 && post.Accounts.Accounts[0].Id == 3, "Cached account must remain intact in Retain mode.");
            check(post.Accounts.Incarnations.Count == 1 && post.Accounts.Incarnations[0].Id == 4, "Cached incarnation must remain intact in Retain mode.");
            check(post.Accounts.Bindings.Count == 1 && post.Accounts.Bindings[0].Id == 5 && post.Accounts.Bindings[0].State == "Inactive",
                "Binding must remain present with Inactive state.");
            check(post.Balances.Balances.Count == 1 && post.Balances.Balances[0].Id == 7 && post.Balances.Balances[0].Amount == "1250.50",
                "Cached balance snapshot must remain intact in Retain mode.");
            check(post.Institutions.Institutions.Count == 1 && post.Institutions.Institutions[0].Id == 1,
                "Institution must remain intact in Retain mode.");
            check(post.Profile.NextId == snap2.Profile.NextId, "NextId counter must never move backwards.");
        }
        finally
        {
            CleanupDirectory(dir);
        }
    }

    private static void VerifyDeleteCachedAccountsAndPurgeSingleConnection(Action<bool, string> check)
    {
        string dir = CreateTempDirectory();
        try
        {
            Guid profileId = Guid.NewGuid();
            Guid branchId = Guid.NewGuid();
            using ProfileStore store = ProfileStore.CreateNew(dir, TestPassphrase, "M1-08 Purge Test", "Gen 1", profileId, branchId);

            StoreSnapshot snap2 = BuildSingleConnectionSnapshot(profileId, branchId, generation: 2);
            store.Save(snap2, BaseTime.AddMinutes(1));

            // First verify non-destructive planner option with removeOrphanedInstitution: false
            ConnectionRemovalResult keepInstPlan = ConnectionRemovalPlanner.PlanRemoval(
                snap2,
                connectionId: 2,
                disposition: ConnectionRemovalDataDisposition.DeleteCachedAccountsAndPurge,
                removeOrphanedInstitution: false);
            check(!keepInstPlan.RemovedInstitutionId.HasValue && keepInstPlan.UpdatedSnapshot.Institutions.Institutions.Count == 1,
                "When removeOrphanedInstitution is false, institution must be preserved even if all connections are removed.");

            // Now execute full purge including orphaned institution via ProfileStore.RemoveConnection
            ConnectionRemovalResult result = store.RemoveConnection(
                connectionId: 2,
                disposition: ConnectionRemovalDataDisposition.DeleteCachedAccountsAndPurge,
                passphrase: TestPassphrase,
                removeOrphanedInstitution: true,
                savedAt: BaseTime.AddMinutes(5),
                note: "Disconnect and purge cached data");

            check(result.RemovedConnectionId == 2, "Purge result must report connection 2.");
            check(result.Disposition == ConnectionRemovalDataDisposition.DeleteCachedAccountsAndPurge, "Purge result must report DeleteCachedAccountsAndPurge.");
            check(result.RemovedAccountIds.SequenceEqual([3UL]), "Purge mode must remove exclusive account 3.");
            check(result.RemovedIncarnationIds.SequenceEqual([4UL]), "Purge mode must remove exclusive incarnation 4.");
            check(result.RemovedBindingIds.SequenceEqual([5UL]), "Purge mode must remove binding 5.");
            check(result.RemovedBalanceIds.SequenceEqual([7UL]), "Purge mode must remove balance 7.");
            check(result.RemovedInstitutionId == 1UL, "Purge mode must remove orphaned institution 1.");

            string[] filesAfter = Directory.GetFiles(dir, "generation-*.bffond");
            check(filesAfter.Length == 1, "Purge must leave only the newly committed generation on disk.");

            StoreSnapshot post = store.CurrentSnapshot!;
            check(post.Institutions.Institutions.Count == 0, "Orphaned institution must be purged.");
            check(post.Connections.Connections.Count == 0, "Connection must be purged.");
            check(post.Accounts.Accounts.Count == 0, "Exclusive account must be purged.");
            check(post.Accounts.Incarnations.Count == 0, "Exclusive incarnation must be purged.");
            check(post.Accounts.Bindings.Count == 0, "Binding must be purged.");
            check(post.Accounts.Capabilities.Count == 0, "Capabilities must be purged.");
            check(post.Balances.Balances.Count == 0, "Balances must be purged.");
            check(post.Ledger.Records.Count == 0, "Ledger records for purged entities must be removed.");
            check(post.Profile.NextId == 9, "NextId must remain monotonic so purged entity IDs are never reused.");

            bool threwUnknown = false;
            try
            {
                store.RemoveConnection(999, ConnectionRemovalDataDisposition.DeleteCachedAccountsAndPurge, TestPassphrase);
            }
            catch (InvalidOperationException)
            {
                threwUnknown = true;
            }

            check(threwUnknown, "Removing a non-existent connection ID must fail closed with InvalidOperationException.");
        }
        finally
        {
            CleanupDirectory(dir);
        }
    }

    private static void VerifyDeleteCachedAccountsPreservesSharedAccountAndInstitution(Action<bool, string> check)
    {
        Guid profileId = Guid.NewGuid();
        Guid branchId = Guid.NewGuid();

        // Institution 1 has two connections: Connection 2 and Connection 20.
        // Account 3 (Incarnation 4) is shared between Connection 2 (Binding 5, Obs 6, Bal 7) and Connection 20 (Binding 21, Obs 22, Bal 23).
        // Account 10 (Incarnation 11) is exclusive to Connection 2 (Binding 12, Obs 13, Bal 14).
        StoreSnapshot multi = new(
            Profile: new StoreProfileDocument(
                ProfileId: profileId,
                BranchId: branchId,
                ParentBranchId: null,
                RecoveryProvenance: null,
                Generation: 1,
                NextId: 30,
                SavedAt: BaseTime,
                Label: "Shared Account Test",
                Note: "Multi-connection"),
            Institutions: new StoreInstitutionsDocument(
                profileId,
                branchId,
                1,
                [
                    new StoreInstitutionEntry(1, "Sparkasse Shared", "https://fints.sparkasse.example/fints", "10020030", "SPUEDE2UXXX", "Confirmed"),
                ]),
            Connections: new StoreConnectionsDocument(
                profileId,
                branchId,
                1,
                [
                    new StoreConnectionEntry(2, 1, "userA", "custA", "Connected", "SYS-A", "Synchronized", 2, true, BaseTime, BaseTime, "FinTS 3.0 PIN/TAN"),
                    new StoreConnectionEntry(20, 1, "userB", "custB", "Connected", "SYS-B", "Synchronized", 2, true, BaseTime, BaseTime, "FinTS 3.0 PIN/TAN"),
                ]),
            Accounts: new StoreAccountsDocument(
                profileId,
                branchId,
                1,
                Accounts:
                [
                    new StoreAccountEntry(3, "Shared Girokonto", "EUR"),
                    new StoreAccountEntry(10, "Exclusive Tagesgeld", "EUR"),
                ],
                Incarnations:
                [
                    new StoreIncarnationEntry(4, 3, "Active"),
                    new StoreIncarnationEntry(11, 10, "Active"),
                ],
                Bindings:
                [
                    new StoreBindingEntry(5, 3, 4, 1, 2, "Active", "FinTS 3.0 PIN/TAN", "1.0", "https://fints.sparkasse.example/fints", "DE89370400440532013000", "0532013000", "37040044", "00", "EUR", []),
                    new StoreBindingEntry(21, 3, 4, 1, 20, "Active", "FinTS 3.0 PIN/TAN", "1.0", "https://fints.sparkasse.example/fints", "DE89370400440532013000", "0532013000", "37040044", "00", "EUR", []),
                    new StoreBindingEntry(12, 10, 11, 1, 2, "Active", "FinTS 3.0 PIN/TAN", "1.0", "https://fints.sparkasse.example/fints", "DE89370400440532019999", "0532019999", "37040044", "00", "EUR", []),
                ],
                Capabilities:
                [
                    new StoreCapabilityEntry(2, 3, "HKSAL", 7, "Active", null),
                    new StoreCapabilityEntry(2, 10, "HKSAL", 7, "Active", null),
                    new StoreCapabilityEntry(20, 3, "HKSAL", 7, "Active", null),
                ]),
            Balances: new StoreBalancesDocument(
                profileId,
                branchId,
                1,
                [
                    new StoreBalanceEntry(7, 6, 3, 4, 5, "Booked", "100.00", "EUR", BaseTime, BaseTime, false, "SALDO"),
                    new StoreBalanceEntry(23, 22, 3, 4, 21, "Booked", "150.00", "EUR", BaseTime, BaseTime, false, "SALDO"),
                    new StoreBalanceEntry(14, 13, 10, 11, 12, "Booked", "5000.00", "EUR", BaseTime, BaseTime, false, "SALDO"),
                ]),
            Ledger: new StoreLedgerDocument(
                profileId,
                branchId,
                1,
                [
                    new StoreLedgerRecord(1, "Institution", null, []),
                    new StoreLedgerRecord(2, "Connection", null, [(1, "Institution")]),
                    new StoreLedgerRecord(20, "Connection", null, [(1, "Institution")]),
                    new StoreLedgerRecord(3, "Account", null, []),
                    new StoreLedgerRecord(4, "AccountIncarnation", null, [(3, "Account")]),
                    new StoreLedgerRecord(5, "AccountBinding", null, [(4, "AccountIncarnation"), (2, "Connection")]),
                    new StoreLedgerRecord(6, "Observation", null, [(5, "AccountBinding")]),
                    new StoreLedgerRecord(7, "BalanceSnapshot", null, [(5, "AccountBinding"), (6, "Observation")]),
                    new StoreLedgerRecord(21, "AccountBinding", null, [(4, "AccountIncarnation"), (20, "Connection")]),
                    new StoreLedgerRecord(22, "Observation", null, [(21, "AccountBinding")]),
                    new StoreLedgerRecord(23, "BalanceSnapshot", null, [(21, "AccountBinding"), (22, "Observation")]),
                    new StoreLedgerRecord(10, "Account", null, []),
                    new StoreLedgerRecord(11, "AccountIncarnation", null, [(10, "Account")]),
                    new StoreLedgerRecord(12, "AccountBinding", null, [(11, "AccountIncarnation"), (2, "Connection")]),
                    new StoreLedgerRecord(13, "Observation", null, [(12, "AccountBinding")]),
                    new StoreLedgerRecord(14, "BalanceSnapshot", null, [(12, "AccountBinding"), (13, "Observation")]),
                ]));

        ConnectionRemovalResult result = ConnectionRemovalPlanner.PlanRemoval(
            multi,
            connectionId: 2,
            disposition: ConnectionRemovalDataDisposition.DeleteCachedAccountsAndPurge,
            removeOrphanedInstitution: true);

        StoreSnapshot post = result.UpdatedSnapshot;
        check(result.RemovedAccountIds.SequenceEqual([10UL]), "Only exclusive account 10 must be purged; shared account 3 must remain.");
        check(result.RemovedIncarnationIds.SequenceEqual([11UL]), "Only exclusive incarnation 11 must be purged; shared incarnation 4 must remain.");
        check(result.RemovedBindingIds.SequenceEqual([5UL, 12UL]), "Bindings 5 and 12 belonging to connection 2 must be purged.");
        check(result.RemovedBalanceIds.SequenceEqual([7UL, 14UL]), "Balances 7 and 14 belonging to connection 2 must be purged.");
        check(!result.RemovedInstitutionId.HasValue && post.Institutions.Institutions.Count == 1,
            "Institution 1 must be preserved because connection 20 still references it.");
        check(post.Connections.Connections.Count == 1 && post.Connections.Connections[0].Id == 20, "Connection 20 must remain intact.");
        check(post.Accounts.Accounts.Count == 1 && post.Accounts.Accounts[0].Id == 3, "Shared account 3 must remain intact.");
        check(post.Accounts.Bindings.Count == 1 && post.Accounts.Bindings[0].Id == 21, "Connection 20's binding 21 must remain intact.");
        check(post.Balances.Balances.Count == 1 && post.Balances.Balances[0].Id == 23, "Connection 20's balance 23 must remain intact.");
        check(post.Accounts.Capabilities.Count == 1 && post.Accounts.Capabilities[0].ConnectionId == 20, "Connection 20's capabilities must remain intact.");
    }

    private static void VerifyEncryptedLocalDiagnosticStoreLifecycle(Action<bool, string> check)
    {
        string dir = CreateTempDirectory();
        try
        {
            byte[] salt = RandomNumberGenerator.GetBytes(32);
            using ProfileMasterKeySession session = ProfileEnvelopeCodec.CreateMasterKeySession(
                salt,
                ProfileEnvelopeCodec.MinimumIterations,
                TestPassphrase);

            EncryptedLocalDiagnosticStore store = new(dir, capacity: 4, enabled: false);
            check(!store.IsEnabled, "EncryptedLocalDiagnosticStore must default to disabled.");
            store.Record(LocalDiagnosticCode.SessionStarted, LocalDiagnosticOutcome.Pending, BaseTime);
            store.SaveToDisk(session);
            check(!File.Exists(store.FilePath), "Saving while disabled must not create diagnostics.bffond.");

            store.SetEnabled(true);
            store.Record(LocalDiagnosticCode.SessionStarted, LocalDiagnosticOutcome.Pending, BaseTime);
            store.Record(LocalDiagnosticCode.Authentication, LocalDiagnosticOutcome.Succeeded, BaseTime.AddSeconds(5));
            store.SaveToDisk(session);
            check(File.Exists(store.FilePath), "Saving while enabled must create encrypted diagnostics.bffond.");

            byte[] rawBytes = File.ReadAllBytes(store.FilePath);
            string rawAscii = Encoding.ASCII.GetString(rawBytes);
            check(rawAscii.StartsWith("BFONDPRF", StringComparison.Ordinal), "Encrypted diagnostics file must use the BFONDPRF v1 envelope header.");
            check(!rawAscii.Contains("SessionStarted", StringComparison.Ordinal) &&
                  !rawAscii.Contains("Authentication", StringComparison.Ordinal) &&
                  !rawAscii.Contains("schemaVersion", StringComparison.Ordinal),
                "Raw diagnostics.bffond bytes on disk must not expose plaintext enum names or JSON property names.");

            // Round-trip into a second store instance
            EncryptedLocalDiagnosticStore reloaded = new(dir, capacity: 4, enabled: false);
            bool loaded = reloaded.TryLoadFromDisk(session);
            check(loaded && reloaded.IsEnabled, "TryLoadFromDisk must restore enabled state and return true when diagnostics.bffond exists.");

            DiagnosticSupportPreview preview = reloaded.CreateSupportPreview(includeTimestamps: true);
            check(preview.EventCount == 2 && preview.DroppedEventCount == 0, "Reloaded store must preserve recorded event count and dropped counter.");
            check(preview.Json.Contains("\"SessionStarted\"", StringComparison.Ordinal) &&
                  preview.Json.Contains("\"Authentication\"", StringComparison.Ordinal),
                "Reloaded preview must contain the exact recorded enum codes.");

            // Disabling must wipe memory and delete diagnostics.bffond from disk
            reloaded.SetEnabled(false);
            check(!File.Exists(store.FilePath), "SetEnabled(false) must immediately delete diagnostics.bffond from disk.");
            check(reloaded.CreateSupportPreview().EventCount == 0, "SetEnabled(false) must clear in-memory events.");

            // Re-enable, save, and test PurgeFromDisk
            reloaded.SetEnabled(true);
            reloaded.Record(LocalDiagnosticCode.ReadCompleted, LocalDiagnosticOutcome.Succeeded, BaseTime);
            reloaded.SaveToDisk(session);
            string staleTmp = Path.Combine(dir, "diagnostics.new-stale.tmp");
            File.WriteAllBytes(staleTmp, [1, 2, 3]);
            reloaded.PurgeFromDisk();
            check(!File.Exists(store.FilePath) && !File.Exists(staleTmp),
                "PurgeFromDisk must delete diagnostics.bffond and any leftover diagnostics.new-*.tmp files.");
            check(reloaded.CreateSupportPreview().EventCount == 0, "PurgeFromDisk must clear in-memory events.");
        }
        finally
        {
            CleanupDirectory(dir);
        }
    }

    private static void VerifyEncryptedDiagnosticSchemaAndTamperRejection(Action<bool, string> check)
    {
        string dir = CreateTempDirectory();
        try
        {
            byte[] salt = RandomNumberGenerator.GetBytes(32);
            using ProfileMasterKeySession session = ProfileEnvelopeCodec.CreateMasterKeySession(
                salt,
                ProfileEnvelopeCodec.MinimumIterations,
                TestPassphrase);
            using ProfileMasterKeySession wrongSession = ProfileEnvelopeCodec.CreateMasterKeySession(
                salt,
                ProfileEnvelopeCodec.MinimumIterations,
                "WrongPassphrase-2026!");

            EncryptedLocalDiagnosticStore store = new(dir, capacity: 8, enabled: true);
            store.Record(LocalDiagnosticCode.SessionStarted, LocalDiagnosticOutcome.Pending, BaseTime);
            store.SaveToDisk(session);

            // Wrong key must throw ProfileAuthenticationException
            EncryptedLocalDiagnosticStore reader = new(dir, capacity: 8, enabled: false);
            bool threwWrongKey = false;
            try
            {
                reader.TryLoadFromDisk(wrongSession);
            }
            catch (ProfileAuthenticationException)
            {
                threwWrongKey = true;
            }

            check(threwWrongKey, "Loading diagnostics.bffond with the wrong session key must fail with ProfileAuthenticationException.");

            // Tampering ciphertext must throw ProfileAuthenticationException
            byte[] bytes = File.ReadAllBytes(store.FilePath);
            bytes[^1] ^= 0xFF;
            File.WriteAllBytes(store.FilePath, bytes);
            bool threwTampered = false;
            try
            {
                reader.TryLoadFromDisk(session);
            }
            catch (ProfileAuthenticationException)
            {
                threwTampered = true;
            }

            check(threwTampered, "Tampered diagnostics.bffond ciphertext must fail with ProfileAuthenticationException.");

            // Write a validly encrypted envelope containing a forbidden free-text property in the JSON payload
            WriteCustomDiagnosticPayload(
                store.FilePath,
                session,
                """{"schemaVersion":1,"droppedEvents":0,"events":[{"code":"SessionStarted","outcome":"Pending","timestamp":"2026-10-16T12:00:00+00:00","message":"Forbidden free text"}]}""");

            bool threwForbiddenProperty = false;
            try
            {
                reader.TryLoadFromDisk(session);
            }
            catch (InvalidDataException)
            {
                threwForbiddenProperty = true;
            }

            check(threwForbiddenProperty, "Encrypted diagnostic payload containing forbidden free-text JSON properties must fail closed with InvalidDataException.");
        }
        finally
        {
            CleanupDirectory(dir);
        }
    }

    private static void VerifyReviewedSupportPreviewExport(Action<bool, string> check)
    {
        string dir = CreateTempDirectory();
        try
        {
            EncryptedLocalDiagnosticStore store = new(dir, capacity: 8, enabled: true);
            store.Record(LocalDiagnosticCode.SessionStarted, LocalDiagnosticOutcome.Pending, BaseTime);
            store.Record(LocalDiagnosticCode.ReadCompleted, LocalDiagnosticOutcome.Succeeded, BaseTime.AddSeconds(2));

            DiagnosticSupportPreview preview = store.CreateSupportPreview(includeTimestamps: false);
            check(!string.IsNullOrWhiteSpace(preview.PreviewSha256) && preview.PreviewSha256.Length == 64,
                "DiagnosticSupportPreview must expose a 64-character lowercase SHA-256 hex digest.");

            string exportPath = Path.Combine(dir, "exported-support-preview.json");

            // Mismatched confirmation digest must fail closed without writing the file
            bool threwMismatch = false;
            try
            {
                EncryptedLocalDiagnosticStore.ExportReviewedSupportPreview(
                    preview,
                    confirmedPreviewSha256: new string('0', 64),
                    destinationFilePath: exportPath);
            }
            catch (InvalidOperationException)
            {
                threwMismatch = true;
            }

            check(threwMismatch && !File.Exists(exportPath),
                "ExportReviewedSupportPreview must reject a mismatched confirmation digest and leave no file on disk.");

            // Matching confirmation digest must write the exact reviewed JSON bytes
            EncryptedLocalDiagnosticStore.ExportReviewedSupportPreview(
                preview,
                confirmedPreviewSha256: preview.PreviewSha256.ToUpperInvariant(),
                destinationFilePath: exportPath);

            check(File.Exists(exportPath), "ExportReviewedSupportPreview must write the destination file when the digest matches.");
            string exportedText = File.ReadAllText(exportPath, Encoding.UTF8);
            check(exportedText == preview.Json, "Exported support preview file must match the exact reviewed JSON string.");
        }
        finally
        {
            CleanupDirectory(dir);
        }
    }

    private static void WriteCustomDiagnosticPayload(string filePath, ProfileMasterKeySession session, string jsonPayload)
    {
        byte[] plaintextBytes = Encoding.UTF8.GetBytes(jsonPayload);
        byte[] header = ProfileEnvelopeCodec.CreateHeader(session.ProfileSalt, session.Iterations);
        byte[] contentKey = session.DeriveContentKey(header);
        try
        {
            using FileStream fs = new(filePath, FileMode.Create, FileAccess.Write, FileShare.None);
            ProfileEnvelopeCodec.EncryptToStream(fs, plaintextBytes, header, contentKey);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(contentKey);
            CryptographicOperations.ZeroMemory(header);
        }
    }
}
