using System.Security.Cryptography;
using Broiler.Fond.Kernel.Domain.Accounts;
using Broiler.Fond.Kernel.Domain.Identifiers;
using Broiler.Fond.Kernel.Storage;

namespace Broiler.Fond.Kernel.Tests.Storage;

/// <summary>
/// Executable verification suite for M1-03 / ADR 0090 Ambiguous-Rediscovery Account Relinking
/// (<see cref="AccountRelinkingPlanner"/> and <see cref="IProfileStore.RelinkAccountBinding"/>).
/// </summary>
internal static class AccountRelinkingTests
{
    private const string TestPassphrase = "CorrectHorseBatteryStaple-Relink-2026!";
    private static readonly DateTimeOffset BaseTime = new(2026, 10, 16, 14, 0, 0, TimeSpan.Zero);

    internal static void Run(Action<bool, string> check)
    {
        int checks = 0;
        void Verify(bool condition, string message)
        {
            checks++;
            check(condition, message);
        }

        VerifyRelinkQuarantinedCandidateWithPruning(Verify);
        VerifyRelinkWithoutPruningRetainsPreviousAccountAndGenerations(Verify);
        VerifyRelinkWithOtherBindingsPreventsPruning(Verify);
        VerifyRelinkWithHistoricalBalancesPreservesProvenanceAndPreventsPruning(Verify);
        VerifyRelinkFailClosedRejections(Verify);

        Console.WriteLine($"Account relinking: {checks} checks completed successfully.");
    }

    private static string CreateTempDirectory()
    {
        string path = Path.Combine(Path.GetTempPath(), "BroilerFondRelinkTests", Guid.NewGuid().ToString("N"));
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

    private static StoreSnapshot BuildRelinkingSnapshot(
        Guid profileId,
        Guid branchId,
        ulong generation,
        bool includeBalanceOnQuarantined = false,
        bool includeSecondBindingOnQuarantined = false,
        string targetAccountCurrency = "EUR")
    {
        ulong nextId = 9;
        List<StoreAccountEntry> accounts =
        [
            new StoreAccountEntry(3, "Primary Checking", targetAccountCurrency),
            new StoreAccountEntry(6, "Quarantined Discovery Candidate", "EUR"),
        ];

        List<StoreIncarnationEntry> incarnations =
        [
            new StoreIncarnationEntry(4, 3, "Active"),
            new StoreIncarnationEntry(7, 6, "Active"),
        ];

        List<StoreBindingEntry> bindings =
        [
            new StoreBindingEntry(
                Id: 5,
                AccountId: 3,
                IncarnationId: 4,
                InstitutionId: 1,
                ConnectionId: 2,
                State: "Active",
                Connector: "FinTS 3.0 PIN/TAN",
                ConnectorVersion: "1.0",
                Endpoint: "https://fints.sparkasse.example/fints",
                Iban: "DE89370400440532013000",
                DomesticAccountNumber: "0532013000",
                DomesticBankCode: "37040044",
                Subaccount: "00",
                Currency: "EUR",
                BankIdentifiers: []),
            new StoreBindingEntry(
                Id: 8,
                AccountId: 6,
                IncarnationId: 7,
                InstitutionId: 1,
                ConnectionId: 2,
                State: "Quarantined",
                Connector: "FinTS 3.0 PIN/TAN",
                ConnectorVersion: "1.0",
                Endpoint: "https://fints.sparkasse.example/fints",
                Iban: "DE89370400440532013999",
                DomesticAccountNumber: "0532013999",
                DomesticBankCode: "37040044",
                Subaccount: "01",
                Currency: "EUR",
                BankIdentifiers: []),
        ];

        if (includeSecondBindingOnQuarantined)
        {
            nextId = 10;
            bindings.Add(new StoreBindingEntry(
                Id: 9,
                AccountId: 6,
                IncarnationId: 7,
                InstitutionId: 1,
                ConnectionId: 2,
                State: "Quarantined",
                Connector: "FinTS 3.0 PIN/TAN",
                ConnectorVersion: "1.0",
                Endpoint: "https://fints.sparkasse.example/fints",
                Iban: "DE89370400440532013888",
                DomesticAccountNumber: "0532013888",
                DomesticBankCode: "37040044",
                Subaccount: "02",
                Currency: "EUR",
                BankIdentifiers: []));
        }

        List<StoreCapabilityEntry> capabilities =
        [
            new StoreCapabilityEntry(2, 3, "HKSAL", 7, "Active", null),
            new StoreCapabilityEntry(2, 6, "HKSPA", 1, "Active", null),
        ];

        List<StoreBalanceEntry> balances = [];
        List<StoreLedgerRecord> ledger =
        [
            new StoreLedgerRecord(1, "Institution", null, []),
            new StoreLedgerRecord(2, "Connection", null, [(1, "Institution")]),
            new StoreLedgerRecord(3, "Account", null, []),
            new StoreLedgerRecord(4, "AccountIncarnation", null, [(3, "Account")]),
            new StoreLedgerRecord(5, "AccountBinding", null, [(4, "AccountIncarnation"), (2, "Connection")]),
            new StoreLedgerRecord(6, "Account", null, []),
            new StoreLedgerRecord(7, "AccountIncarnation", null, [(6, "Account")]),
            new StoreLedgerRecord(8, "AccountBinding", null, [(7, "AccountIncarnation"), (2, "Connection")]),
        ];

        if (includeSecondBindingOnQuarantined)
        {
            ledger.Add(new StoreLedgerRecord(9, "AccountBinding", null, [(7, "AccountIncarnation"), (2, "Connection")]));
        }

        if (includeBalanceOnQuarantined)
        {
            ulong obsId = nextId;
            ulong balId = nextId + 1;
            nextId += 2;

            ledger.Add(new StoreLedgerRecord(obsId, "Observation", null, [(8, "AccountBinding")]));
            ledger.Add(new StoreLedgerRecord(balId, "BalanceSnapshot", null, [(obsId, "Observation")]));

            balances.Add(new StoreBalanceEntry(
                Id: balId,
                ObservationId: obsId,
                AccountId: 6,
                IncarnationId: 7,
                BindingId: 8,
                Kind: "Booked",
                Amount: "123.45",
                Currency: "EUR",
                BankTimestamp: BaseTime,
                RetrievedAt: BaseTime,
                IsStale: false,
                SourceLabel: "Test Quarantined Balance"));
        }

        return new StoreSnapshot(
            Profile: new StoreProfileDocument(
                ProfileId: profileId,
                BranchId: branchId,
                ParentBranchId: null,
                RecoveryProvenance: null,
                Generation: generation,
                NextId: nextId,
                SavedAt: BaseTime,
                Label: "Relinking Test Profile",
                Note: "Initial state"),
            Institutions: new StoreInstitutionsDocument(
                profileId,
                branchId,
                generation,
                [
                    new StoreInstitutionEntry(1, "Sparkasse Test", "https://fints.sparkasse.example/fints", "37040044", "SPUEDE2UXXX", "Confirmed"),
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
                accounts,
                incarnations,
                bindings,
                capabilities),
            Balances: new StoreBalancesDocument(
                profileId,
                branchId,
                generation,
                balances),
            Ledger: new StoreLedgerDocument(
                profileId,
                branchId,
                generation,
                ledger));
    }

    private static void VerifyRelinkQuarantinedCandidateWithPruning(Action<bool, string> verify)
    {
        string dir = CreateTempDirectory();
        try
        {
            Guid profileId = Guid.NewGuid();
            Guid branchId = Guid.NewGuid();

            using (ProfileStore store = ProfileStore.CreateNew(dir, TestPassphrase, "Relink Test", "Gen 1", profileId, branchId))
            {
                StoreSnapshot snap2 = BuildRelinkingSnapshot(profileId, branchId, generation: 2);
                store.Save(snap2, BaseTime.AddMinutes(1));

                verify(store.State == ProfileStoreOpenState.Ready, "Store must be ready after creation.");
                verify(store.CurrentSnapshot!.Accounts.Bindings.First(b => b.Id == 8).State == "Quarantined",
                    "Binding 8 should initially be Quarantined.");
                verify(store.CurrentSnapshot.Accounts.Accounts.Any(a => a.Id == 6),
                    "Account 6 should exist before relinking.");

                DateTimeOffset relinkTime = BaseTime.AddMinutes(5);
                AccountRelinkingResult result = store.RelinkAccountBinding(
                    bindingId: 8,
                    targetAccountId: 3,
                    passphrase: TestPassphrase,
                    pruneOrphanedAccount: true,
                    savedAt: relinkTime);

                verify(result.RelinkedBindingId == 8, "Result should identify relinked binding 8.");
                verify(result.PreviousAccountId == 6, "Result should identify previous account 6.");
                verify(result.TargetAccountId == 3, "Result should identify target account 3.");
                verify(result.TargetIncarnationId == 4, "Result should identify target incarnation 4.");
                verify(result.PrunedAccountId == 6, "Result should report pruned account 6.");
                verify(result.PrunedIncarnationId == 7, "Result should report pruned incarnation 7.");
                verify(result.SaveMetrics is not null, "Save metrics should be returned.");
                verify(result.SaveMetrics!.RetainedGenerationFiles.Count == 1,
                    "Privacy purge should leave exactly 1 generation file on disk.");

                StoreSnapshot updated = store.CurrentSnapshot;
                verify(updated.Profile.Generation == 3, "Generation should be incremented to 3.");
                verify(updated.Profile.NextId == 9, "NextId should be strictly preserved (monotonic).");

                StoreBindingEntry relinkedBinding = updated.Accounts.Bindings.First(b => b.Id == 8);
                verify(relinkedBinding.AccountId == 3, "Binding 8 AccountId should be updated to target account 3.");
                verify(relinkedBinding.IncarnationId == 4, "Binding 8 IncarnationId should be updated to target incarnation 4.");
                verify(relinkedBinding.State == "Active", "Binding 8 State should be updated to Active.");

                verify(!updated.Accounts.Accounts.Any(a => a.Id == 6), "Pruned account 6 should be removed from accounts.xml.");
                verify(!updated.Accounts.Incarnations.Any(i => i.Id == 7), "Pruned incarnation 7 should be removed from accounts.xml.");
                verify(!updated.Ledger.Records.Any(r => r.Id == 6 || r.Id == 7),
                    "Pruned account 6 and incarnation 7 should be removed from ledger.xml.");

                StoreLedgerRecord ledgerBinding = updated.Ledger.Records.First(r => r.Id == 8);
                verify(ledgerBinding.References.Any(r => r.TargetId == 4 && r.ExpectedKind == "AccountIncarnation"),
                    "Ledger record for binding 8 must reference target incarnation 4.");
                verify(ledgerBinding.References.Any(r => r.TargetId == 2 && r.ExpectedKind == "Connection"),
                    "Ledger record for binding 8 must reference connection 2.");

                verify(updated.Accounts.Capabilities.Any(c => c.AccountId == 3 && c.OperationCode == "HKSPA"),
                    "Capability HKSPA from previous account 6 should be migrated to target account 3.");
                verify(!updated.Accounts.Capabilities.Any(c => c.AccountId == 6),
                    "No capabilities should remain referencing pruned account 6.");

                verify(store.IdentityLedger is not null, "Store IdentityLedger must remain valid and initialized.");
            }

            // Reopen store to verify persistence
            using (IProfileStore reopened = ProfileStore.Open(dir, TestPassphrase))
            {
                verify(reopened.State == ProfileStoreOpenState.Ready, "Reopened store must be in Ready state.");
                verify(reopened.CurrentGeneration == 3, "Reopened store current generation must be 3.");
                verify(!reopened.CurrentSnapshot!.Accounts.Accounts.Any(a => a.Id == 6),
                    "Pruned account 6 must not exist in reopened store.");
                verify(reopened.CurrentSnapshot.Accounts.Bindings.First(b => b.Id == 8).State == "Active",
                    "Binding 8 must be Active in reopened store.");
            }
        }
        finally
        {
            CleanupDirectory(dir);
        }
    }

    private static void VerifyRelinkWithoutPruningRetainsPreviousAccountAndGenerations(Action<bool, string> verify)
    {
        string dir = CreateTempDirectory();
        try
        {
            Guid profileId = Guid.NewGuid();
            Guid branchId = Guid.NewGuid();

            using (ProfileStore store = ProfileStore.CreateNew(dir, TestPassphrase, "Relink Test", "Gen 1", profileId, branchId))
            {
                StoreSnapshot snap2 = BuildRelinkingSnapshot(profileId, branchId, generation: 2);
                store.Save(snap2, BaseTime.AddMinutes(1));

                AccountRelinkingResult result = store.RelinkAccountBinding(
                    bindingId: 8,
                    targetAccountId: 3,
                    passphrase: TestPassphrase,
                    pruneOrphanedAccount: false);

                verify(result.PrunedAccountId is null, "PrunedAccountId must be null when pruneOrphanedAccount is false.");
                verify(result.PrunedIncarnationId is null, "PrunedIncarnationId must be null when pruneOrphanedAccount is false.");

                StoreSnapshot updated = store.CurrentSnapshot!;
                verify(updated.Accounts.Accounts.Any(a => a.Id == 6), "Account 6 should be retained when pruning is disabled.");
                verify(updated.Accounts.Incarnations.Any(i => i.Id == 7), "Incarnation 7 should be retained when pruning is disabled.");
                verify(updated.Ledger.Records.Any(r => r.Id == 6), "Account 6 should remain in ledger when pruning is disabled.");

                verify(result.SaveMetrics!.RetainedGenerationFiles.Count == 2,
                    "Standard Save without purge should retain both generation files on disk.");
            }
        }
        finally
        {
            CleanupDirectory(dir);
        }
    }

    private static void VerifyRelinkWithOtherBindingsPreventsPruning(Action<bool, string> verify)
    {
        string dir = CreateTempDirectory();
        try
        {
            Guid profileId = Guid.NewGuid();
            Guid branchId = Guid.NewGuid();

            using (ProfileStore store = ProfileStore.CreateNew(dir, TestPassphrase, "Relink Test", "Gen 1", profileId, branchId))
            {
                StoreSnapshot snap2 = BuildRelinkingSnapshot(
                    profileId,
                    branchId,
                    generation: 2,
                    includeSecondBindingOnQuarantined: true);
                store.Save(snap2, BaseTime.AddMinutes(1));

                AccountRelinkingResult result = store.RelinkAccountBinding(
                    bindingId: 8,
                    targetAccountId: 3,
                    passphrase: TestPassphrase,
                    pruneOrphanedAccount: true);

                verify(result.PrunedAccountId is null,
                    "Previous account must NOT be pruned when other bindings reference it.");
                verify(result.PrunedIncarnationId is null,
                    "Previous incarnation must NOT be pruned when other bindings reference it.");

                StoreSnapshot updated = store.CurrentSnapshot!;
                verify(updated.Accounts.Accounts.Any(a => a.Id == 6),
                    "Account 6 must remain present because binding 9 still references it.");
                verify(updated.Accounts.Bindings.First(b => b.Id == 9).AccountId == 6,
                    "Binding 9 must still reference account 6.");
                verify(result.SaveMetrics!.RetainedGenerationFiles.Count == 2,
                    "Non-purged relink must retain both generations.");
            }
        }
        finally
        {
            CleanupDirectory(dir);
        }
    }

    private static void VerifyRelinkWithHistoricalBalancesPreservesProvenanceAndPreventsPruning(Action<bool, string> verify)
    {
        string dir = CreateTempDirectory();
        try
        {
            Guid profileId = Guid.NewGuid();
            Guid branchId = Guid.NewGuid();

            using (ProfileStore store = ProfileStore.CreateNew(dir, TestPassphrase, "Relink Test", "Gen 1", profileId, branchId))
            {
                StoreSnapshot snap2 = BuildRelinkingSnapshot(
                    profileId,
                    branchId,
                    generation: 2,
                    includeBalanceOnQuarantined: true);
                store.Save(snap2, BaseTime.AddMinutes(1));

                ulong initialBalCount = (ulong)store.CurrentSnapshot!.Balances.Balances.Count;
                verify(initialBalCount == 1, "Snapshot must have 1 initial balance snapshot.");

                StoreBalanceEntry initialBal = store.CurrentSnapshot.Balances.Balances[0];
                verify(initialBal.AccountId == 6, "Initial balance AccountId must be 6.");
                verify(initialBal.IncarnationId == 7, "Initial balance IncarnationId must be 7.");
                verify(initialBal.BindingId == 8, "Initial balance BindingId must be 8.");

                AccountRelinkingResult result = store.RelinkAccountBinding(
                    bindingId: 8,
                    targetAccountId: 3,
                    passphrase: TestPassphrase,
                    pruneOrphanedAccount: true);

                verify(result.PrunedAccountId is null,
                    "Previous account must NOT be pruned when historical balance snapshots reference it.");

                StoreSnapshot updated = store.CurrentSnapshot!;
                verify(updated.Accounts.Accounts.Any(a => a.Id == 6),
                    "Account 6 must be preserved because historical balances belong to it.");
                verify(updated.Accounts.Incarnations.Any(i => i.Id == 7),
                    "Incarnation 7 must be preserved because historical balances belong to it.");

                StoreBalanceEntry balanceAfter = updated.Balances.Balances[0];
                verify(balanceAfter.AccountId == 6,
                    "Balance AccountId provenance must permanently remain 6 (ADR 0007 / Roadmap).");
                verify(balanceAfter.IncarnationId == 7,
                    "Balance IncarnationId provenance must permanently remain 7 (ADR 0007 / Roadmap).");
                verify(balanceAfter.BindingId == 8,
                    "Balance BindingId provenance must permanently remain 8.");
            }
        }
        finally
        {
            CleanupDirectory(dir);
        }
    }

    private static void VerifyRelinkFailClosedRejections(Action<bool, string> verify)
    {
        string dir = CreateTempDirectory();
        try
        {
            Guid profileId = Guid.NewGuid();
            Guid branchId = Guid.NewGuid();
            StoreSnapshot snapshot = BuildRelinkingSnapshot(profileId, branchId, 1, targetAccountCurrency: "EUR");

            // 1. Non-existent binding ID
            bool threwMissingBinding = false;
            try
            {
                AccountRelinkingPlanner.PlanRelink(snapshot, bindingId: 999, targetAccountId: 3);
            }
            catch (InvalidOperationException ex) when (ex.Message.Contains("Binding ID 999 does not exist"))
            {
                threwMissingBinding = true;
            }
            verify(threwMissingBinding, "PlanRelink must reject non-existent binding ID.");

            // 2. Non-existent target account ID
            bool threwMissingAccount = false;
            try
            {
                AccountRelinkingPlanner.PlanRelink(snapshot, bindingId: 8, targetAccountId: 999);
            }
            catch (InvalidOperationException ex) when (ex.Message.Contains("Target account ID 999 does not exist"))
            {
                threwMissingAccount = true;
            }
            verify(threwMissingAccount, "PlanRelink must reject non-existent target account ID.");

            // 3. Binding already linked and active on target account
            bool threwAlreadyActive = false;
            try
            {
                AccountRelinkingPlanner.PlanRelink(snapshot, bindingId: 5, targetAccountId: 3);
            }
            catch (InvalidOperationException ex) when (ex.Message.Contains("is already linked to account 3 and is active"))
            {
                threwAlreadyActive = true;
            }
            verify(threwAlreadyActive, "PlanRelink must reject binding already active on target account.");

            // 4. Incompatible currencies
            StoreSnapshot usdSnapshot = BuildRelinkingSnapshot(profileId, branchId, 1, targetAccountCurrency: "USD");
            bool threwIncompatibleCurrency = false;
            try
            {
                AccountRelinkingPlanner.PlanRelink(usdSnapshot, bindingId: 8, targetAccountId: 3);
            }
            catch (InvalidOperationException ex) when (ex.Message.Contains("Cannot relink binding 8 with currency 'EUR' to account 3 with currency 'USD'"))
            {
                threwIncompatibleCurrency = true;
            }
            verify(threwIncompatibleCurrency, "PlanRelink must reject incompatible account/binding currencies.");

            // 5. Duplicate active binding with matching locator on target account under same connection
            StoreBindingEntry duplicateLocatorBinding = snapshot.Accounts.Bindings[0] with
            {
                Id = 8,
                AccountId = 6,
                IncarnationId = 7,
                State = "Quarantined",
            };
            StoreSnapshot dupSnapshot = snapshot with
            {
                Accounts = snapshot.Accounts with
                {
                    Bindings = [snapshot.Accounts.Bindings[0], duplicateLocatorBinding],
                },
            };
            bool threwDuplicateBinding = false;
            try
            {
                AccountRelinkingPlanner.PlanRelink(dupSnapshot, bindingId: 8, targetAccountId: 3);
            }
            catch (InvalidOperationException ex) when (ex.Message.Contains("already has an active binding on connection 2 with matching locator"))
            {
                threwDuplicateBinding = true;
            }
            verify(threwDuplicateBinding, "PlanRelink must reject duplicate active binding with matching locator.");

            // 6. Target account has no incarnations
            StoreSnapshot noIncSnapshot = snapshot with
            {
                Accounts = snapshot.Accounts with
                {
                    Incarnations = snapshot.Accounts.Incarnations.Where(i => i.AccountId != 3).ToList(),
                },
            };
            bool threwNoIncarnations = false;
            try
            {
                AccountRelinkingPlanner.PlanRelink(noIncSnapshot, bindingId: 8, targetAccountId: 3);
            }
            catch (InvalidOperationException ex) when (ex.Message.Contains("has no incarnations in the profile"))
            {
                threwNoIncarnations = true;
            }
            verify(threwNoIncarnations, "PlanRelink must reject target account with no incarnations.");

            // 7. Store-level read-only rejection
            using (ProfileStore store = ProfileStore.CreateNew(dir, TestPassphrase, "Relink Test", "Gen 1", profileId, branchId))
            {
                StoreSnapshot snap2 = BuildRelinkingSnapshot(profileId, branchId, generation: 2);
                store.Save(snap2, BaseTime.AddMinutes(1));
            }

            using (IProfileStore readOnlyStore = ProfileStore.Open(dir, TestPassphrase, openReadOnly: true))
            {
                bool threwReadOnly = false;
                try
                {
                    readOnlyStore.RelinkAccountBinding(8, 3, TestPassphrase);
                }
                catch (InvalidOperationException ex) when (ex.Message.Contains("read-only mode"))
                {
                    threwReadOnly = true;
                }
                verify(threwReadOnly, "RelinkAccountBinding must reject invocation in read-only mode.");
            }

            // 8. Incorrect passphrase rejection
            using (IProfileStore store = ProfileStore.Open(dir, TestPassphrase))
            {
                bool threwBadPassphrase = false;
                try
                {
                    store.RelinkAccountBinding(8, 3, "WrongPassphrase123!");
                }
                catch (ProfileAuthenticationException)
                {
                    threwBadPassphrase = true;
                }
                verify(threwBadPassphrase, "RelinkAccountBinding must reject incorrect passphrase for privacy purge.");
            }
        }
        finally
        {
            CleanupDirectory(dir);
        }
    }
}
