using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;
using Broiler.Fond.Kernel.Storage;

namespace Broiler.Fond.Kernel.Tests.Storage;

/// <summary>
/// Executable verification suite for M1-02 Production Profile Lifecycle (<see cref="IProfileStore"/>):
/// single-writer directory locking, multi-generation commits, seekable frame authentication,
/// bounded write amplification, explicit branched recovery, privacy purge, passphrase rotation,
/// whole-profile erasure, fault injection, backup/restore, ID-remapping import/merge, and concurrency.
/// </summary>
internal static class ProfileStoreLifecycleTests
{
    private const string TestPassphrase = "CorrectHorseBatteryStaple-ProfileStore-2026!";
    private const string RotatedPassphrase = "RotatedPassphrase-NewLineageSalt-2026#";

    internal static void Run(Action<bool, string> check)
    {
        int checks = 0;
        void Verify(bool condition, string message)
        {
            checks++;
            check(condition, message);
        }

        VerifySingleWriterLockAndReadOnlyMode(Verify);
        VerifyMultiGenerationSaveAndRetention(Verify);
        VerifySeekableAuthenticatedProfileStream(Verify);
        VerifySaveFaultInjectionAndTempCandidates(Verify);
        VerifyCorruptLatestGenerationAndBranchedRecovery(Verify);
        VerifyLineageAndManifestConflictDetection(Verify);
        VerifyPrivacyPurgeLifecycle(Verify);
        VerifyPassphraseRotationLifecycle(Verify);
        VerifyWholeProfileErasureLifecycle(Verify);
        VerifyInterruptedPurgeFaultInjectionAndResume(Verify);
        VerifyBackupRestoreAndImportMerge(Verify);
        VerifyConcurrentAccessAndLocking(Verify);

        Console.WriteLine($"Production ProfileStore lifecycle: {checks} checks completed successfully.");
    }

    private static string CreateTempDirectory()
    {
        string path = Path.Combine(Path.GetTempPath(), "BroilerFondProfileTests", Guid.NewGuid().ToString("N"));
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

    private static StoreSnapshot BuildPopulatedSnapshot(
        Guid profileId,
        Guid branchId,
        ulong generation,
        string label,
        string note,
        int extraAccounts = 0)
    {
        DateTimeOffset now = new(2026, 10, 16, 10, 0, 0, TimeSpan.Zero);
        List<StoreInstitutionEntry> institutions =
        [
            new(1, "Sparkasse Test", "https://fints.sparkasse.example/fints", "10020030", "SPUEDE2UXXX", "Confirmed"),
        ];

        List<StoreConnectionEntry> connections =
        [
            new(2, 1, "user100", "cust100", "Connected", "SYS-001", "Synchronized", 2, true, now, now, "FinTS 3.0 PIN/TAN"),
        ];

        List<StoreAccountEntry> accounts = [];
        List<StoreIncarnationEntry> incarnations = [];
        List<StoreBindingEntry> bindings = [];
        List<StoreCapabilityEntry> capabilities = [];
        List<StoreBalanceEntry> balances = [];
        List<StoreLedgerRecord> ledger =
        [
            new(1, "Institution", null, []),
            new(2, "Connection", null, [(1, "Institution")]),
        ];

        ulong nextId = 3;
        int totalAccounts = Math.Max(1, 1 + extraAccounts);
        for (int i = 0; i < totalAccounts; i++)
        {
            ulong accId = nextId++;
            ulong incId = nextId++;
            ulong binId = nextId++;
            ulong obsId = nextId++;
            ulong balId = nextId++;

            accounts.Add(new StoreAccountEntry(accId, $"Girokonto {i + 1}", "EUR"));
            incarnations.Add(new StoreIncarnationEntry(incId, accId, "Active"));
            bindings.Add(new StoreBindingEntry(
                binId, accId, incId, 1, 2, "Active", "FinTS 3.0 PIN/TAN", "1.0",
                "https://fints.sparkasse.example/fints",
                $"DE8937040044053201{i:D4}", $"053201{i:D4}", "37040044", "00", "EUR",
                [("DE.SPARKASSE", "ACCOUNT", "INTERNAL", $"ACC-{i:D4}")]));
            capabilities.Add(new StoreCapabilityEntry(2, accId, "HKSAL", 7, "Active", "10000.00"));
            balances.Add(new StoreBalanceEntry(balId, obsId, accId, incId, binId, "Booked", $"{1000 + i * 250}.50", "EUR", now, now, false, "SALDO"));

            ledger.Add(new StoreLedgerRecord(accId, "Account", null, []));
            ledger.Add(new StoreLedgerRecord(incId, "AccountIncarnation", null, [(accId, "Account")]));
            ledger.Add(new StoreLedgerRecord(binId, "AccountBinding", null, [(incId, "AccountIncarnation"), (2, "Connection")]));
            ledger.Add(new StoreLedgerRecord(obsId, "Observation", null, [(binId, "AccountBinding")]));
            ledger.Add(new StoreLedgerRecord(balId, "BalanceSnapshot", null, [(binId, "AccountBinding"), (obsId, "Observation")]));
        }

        return new StoreSnapshot(
            new StoreProfileDocument(profileId, branchId, null, null, generation, nextId, now, label, note),
            new StoreInstitutionsDocument(profileId, branchId, generation, institutions),
            new StoreConnectionsDocument(profileId, branchId, generation, connections),
            new StoreAccountsDocument(profileId, branchId, generation, accounts, incarnations, bindings, capabilities),
            new StoreBalancesDocument(profileId, branchId, generation, balances),
            new StoreLedgerDocument(profileId, branchId, generation, ledger));
    }

    private static void VerifySingleWriterLockAndReadOnlyMode(Action<bool, string> verify)
    {
        string dir = CreateTempDirectory();
        try
        {
            using (ProfileStore writer = ProfileStore.CreateNew(dir, TestPassphrase, "Primary Profile", "Created in test"))
            {
                verify(writer.State == ProfileStoreOpenState.Ready, "New store opens in Ready state.");
                verify(!writer.IsReadOnly, "New store is writable.");
                verify(writer.CurrentGeneration == 1UL, "Initial generation is 1.");
                verify(writer.IdentityLedger is not null, "Writable store exposes LocalIdentityLedger.");
                verify(File.Exists(Path.Combine(dir, ProfileStore.LockFileName)), "profile.lock exists on disk.");

                bool secondWriterBlocked = false;
                try
                {
                    using ProfileStore secondWriter = ProfileStore.Open(dir, TestPassphrase, openReadOnly: false);
                }
                catch (ProfileLockConflictException)
                {
                    secondWriterBlocked = true;
                }

                verify(secondWriterBlocked, "Second read-write ProfileStore.Open is rejected with ProfileLockConflictException.");

                using ProfileStore reader = ProfileStore.Open(dir, TestPassphrase, openReadOnly: true);
                verify(reader.IsReadOnly, "Read-only store reports IsReadOnly == true.");
                verify(reader.State == ProfileStoreOpenState.Ready, "Read-only store opens in Ready state.");
                verify(reader.CurrentGeneration == 1UL, "Read-only store sees generation 1.");
                verify(reader.IdentityLedger is null, "Read-only store does not expose a writable LocalIdentityLedger.");

                bool writeOnReadOnlyRejected = false;
                try
                {
                    reader.Save(reader.CurrentSnapshot!);
                }
                catch (InvalidOperationException)
                {
                    writeOnReadOnlyRejected = true;
                }

                verify(writeOnReadOnlyRejected, "Calling Save on a read-only ProfileStore throws InvalidOperationException.");
            }

            // After disposing the first writer, acquiring the write lock must succeed.
            using ProfileStore reopened = ProfileStore.Open(dir, TestPassphrase, openReadOnly: false);
            verify(!reopened.IsReadOnly && reopened.CurrentGeneration == 1UL, "Store reopens writably after prior writer is disposed.");
        }
        finally
        {
            CleanupDirectory(dir);
        }
    }

    private static void VerifyMultiGenerationSaveAndRetention(Action<bool, string> verify)
    {
        string dir = CreateTempDirectory();
        try
        {
            Guid pid = Guid.NewGuid();
            Guid bid = Guid.NewGuid();

            using (ProfileStore store = ProfileStore.CreateNew(dir, TestPassphrase, "Household", "Gen 1", pid, bid))
            {
                StoreSnapshot snap2 = BuildPopulatedSnapshot(pid, bid, 2, "Household", "Gen 2", extraAccounts: 4);
                ProfileSaveMetrics m2 = store.Save(snap2);
                verify(m2.Generation == 2UL, "Saved generation 2.");

                StoreSnapshot snap3 = BuildPopulatedSnapshot(pid, bid, 3, "Household", "Gen 3", extraAccounts: 12);
                ProfileSaveMetrics m3 = store.Save(snap3);
                verify(m3.Generation == 3UL, "Saved generation 3.");

                StoreSnapshot snap4 = BuildPopulatedSnapshot(pid, bid, 4, "Household", "Gen 4", extraAccounts: 40);
                ProfileSaveMetrics m4 = store.Save(snap4);
                verify(m4.Generation == 4UL, "Saved generation 4.");
                verify(m4.WriteAmplificationRatio <= ProfileStore.MaxAllowedWriteAmplificationRatio,
                    $"Write amplification ratio {m4.WriteAmplificationRatio:F4} is within 1.10 bound.");
                verify(m4.CleanupWarnings.Count == 0, "Retention cleanup completed without warnings.");

                string[] genFiles = Directory.GetFiles(dir, "generation-*.bffond")
                    .Select(Path.GetFileName)
                    .OrderBy(n => n, StringComparer.Ordinal)
                    .ToArray()!;

                verify(genFiles.Length == 2, "Retention policy keeps exactly 2 generations (current G and previous G-1).");
                verify(genFiles[0] == ProfileStore.FormatGenerationFileName(3), "Retained generation G-1 is generation 3.");
                verify(genFiles[1] == ProfileStore.FormatGenerationFileName(4), "Retained generation G is generation 4.");

                // Verify that profileSalt is preserved while snapshotSalt and noncePrefix are fresh per generation.
                byte[] header3 = File.ReadAllBytes(Path.Combine(dir, genFiles[0]))[..ProfileEnvelopeCodec.HeaderSize];
                byte[] header4 = File.ReadAllBytes(Path.Combine(dir, genFiles[1]))[..ProfileEnvelopeCodec.HeaderSize];

                verify(header3.AsSpan(24, 32).SequenceEqual(header4.AsSpan(24, 32)), "profileSalt is preserved across generations in the same lineage.");
                verify(!header3.AsSpan(56, 32).SequenceEqual(header4.AsSpan(56, 32)), "snapshotSalt is freshly generated on every save.");
                verify(!header3.AsSpan(88, 4).SequenceEqual(header4.AsSpan(88, 4)), "noncePrefix is freshly generated on every save.");
            }

            using (ProfileStore reopened = ProfileStore.Open(dir, TestPassphrase))
            {
                verify(reopened.CurrentGeneration == 4UL, "Reopened store selects highest valid generation 4.");
                verify(reopened.CurrentSnapshot!.Accounts.Accounts.Count == 41, "All 41 accounts round-tripped in generation 4.");
            }
        }
        finally
        {
            CleanupDirectory(dir);
        }
    }

    private static void VerifySeekableAuthenticatedProfileStream(Action<bool, string> verify)
    {
        string dir = CreateTempDirectory();
        try
        {
            Guid pid = Guid.NewGuid();
            Guid bid = Guid.NewGuid();

            using ProfileStore store = ProfileStore.CreateNew(dir, TestPassphrase, "Large Profile", "Multi-frame test", pid, bid);
            // Build a snapshot whose compressed ZIP exceeds 200 KiB (> 3 frames of 64 KiB)
            StringBuilder hugeNote = new(400_000);
            Random rng = new(42);
            for (int i = 0; i < 60_000; i++)
            {
                hugeNote.Append(rng.Next(100000, 999999).ToString(System.Globalization.CultureInfo.InvariantCulture));
                hugeNote.Append('-');
            }

            StoreSnapshot largeSnap = BuildPopulatedSnapshot(pid, bid, 2, "Large Profile", hugeNote.ToString(), extraAccounts: 80);
            ProfileSaveMetrics metrics = store.Save(largeSnap);
            verify(metrics.CompressedZipBytes > ProfileEnvelopeCodec.FrameSize * 2,
                $"Compressed ZIP ({metrics.CompressedZipBytes} bytes) spans at least 3 encrypted 64 KiB frames.");

            byte[] expectedZip = StoreArchiveCodec.CreateStoreZip(store.CurrentSnapshot!);
            using (SeekableAuthenticatedProfileStream stream = store.OpenCurrentGenerationAuthenticatedStream())
            {
                verify(stream.CanRead && stream.CanSeek && !stream.CanWrite, "SeekableAuthenticatedProfileStream capabilities match contract.");
                verify(stream.Length == expectedZip.Length, "Authenticated payload length matches exact ZIP length.");

                // Seek to End - 22 (EOCD) first, simulating ZipArchive random access
                Span<byte> eocd = stackalloc byte[22];
                stream.Seek(-22, SeekOrigin.End);
                int readEocd = stream.Read(eocd);
                verify(readEocd == 22 && eocd.SequenceEqual(expectedZip.AsSpan(expectedZip.Length - 22, 22)),
                    "Random-access seek to EOCD at end of stream authenticates and matches expected bytes.");

                // Seek across frame boundaries (e.g. offset 65500, reading 200 bytes across frame 0 and frame 1)
                Span<byte> crossFrame = stackalloc byte[200];
                stream.Position = 65_500;
                int readCross = stream.Read(crossFrame);
                verify(readCross == 200 && crossFrame.SequenceEqual(expectedZip.AsSpan(65_500, 200)),
                    "Cross-frame read across 64 KiB boundary authenticates both frames and returns exact plaintext.");

                // Full stream read from Position 0
                stream.Position = 0;
                byte[] fullRead = new byte[expectedZip.Length];
                stream.ReadExactly(fullRead);
                verify(fullRead.AsSpan().SequenceEqual(expectedZip), "Full sequential read matches expected ZIP byte-for-byte.");
            }

            // Verify that tampering with a middle frame or final frame is rejected before returning bytes.
            string gen2Path = Path.Combine(dir, ProfileStore.FormatGenerationFileName(2));
            byte[] rawEnvelope = File.ReadAllBytes(gen2Path);

            byte[] tamperedFrame0 = (byte[])rawEnvelope.Clone();
            tamperedFrame0[ProfileEnvelopeCodec.HeaderSize + ProfileEnvelopeCodec.DescriptorSize + 100] ^= 0xFF;
            using (MemoryStream ms = new(tamperedFrame0, writable: false))
            using (SeekableAuthenticatedProfileStream auth = SeekableAuthenticatedProfileStream.Open(ms, TestPassphrase, leaveOpen: true))
            {
                bool frame0Rejected = false;
                byte[] buf = new byte[64];
                try
                {
                    _ = auth.Read(buf, 0, buf.Length);
                }
                catch (ProfileAuthenticationException)
                {
                    frame0Rejected = true;
                }

                verify(frame0Rejected, "Tampered frame 0 ciphertext is rejected with ProfileAuthenticationException before returning plaintext.");
            }

            byte[] tamperedFinal = (byte[])rawEnvelope.Clone();
            tamperedFinal[^1] ^= 0xFF;
            bool finalRejectedOnOpen = false;
            try
            {
                using MemoryStream ms = new(tamperedFinal, writable: false);
                using SeekableAuthenticatedProfileStream auth = SeekableAuthenticatedProfileStream.Open(ms, TestPassphrase, leaveOpen: true);
            }
            catch (ProfileAuthenticationException)
            {
                finalRejectedOnOpen = true;
            }

            verify(finalRejectedOnOpen, "Tampered final empty record is rejected immediately when opening SeekableAuthenticatedProfileStream.");
        }
        finally
        {
            CleanupDirectory(dir);
        }
    }

    private static void VerifySaveFaultInjectionAndTempCandidates(Action<bool, string> verify)
    {
        string dir = CreateTempDirectory();
        try
        {
            Guid pid = Guid.NewGuid();
            Guid bid = Guid.NewGuid();

            using (ProfileStore store = ProfileStore.CreateNew(dir, TestPassphrase, "Fault Test", "Gen 1", pid, bid))
            {
                // Save generation 2 cleanly
                store.Save(BuildPopulatedSnapshot(pid, bid, 2, "Fault Test", "Gen 2", extraAccounts: 2));
            }

            // Reopen with a fault hook that throws BeforeGenerationPromotion on Save (leaving a valid .tmp file unpromoted)
            using (ProfileStore faultyStore = ProfileStore.Open(
                dir,
                TestPassphrase,
                openReadOnly: false,
                faultHook: point =>
                {
                    if (point == ProfileStoreFaultPoint.BeforeGenerationPromotion)
                    {
                        throw new IOException("Simulated crash before atomic File.Move promotion.");
                    }
                }))
            {
                bool faultTriggered = false;
                try
                {
                    faultyStore.Save(BuildPopulatedSnapshot(pid, bid, 3, "Fault Test", "Gen 3 uncommitted", extraAccounts: 5));
                }
                catch (IOException)
                {
                    faultTriggered = true;
                }

                verify(faultTriggered, "Fault hook triggered before generation promotion.");
            }

            string[] tmpFiles = Directory.GetFiles(dir, "profile.new-*.tmp");
            verify(tmpFiles.Length == 1, "Interrupted save left an unpromoted profile.new-*.tmp candidate.");

            // Also drop a truncated/partial .tmp file to simulate crash DuringTemporaryGenerationWrite
            File.WriteAllBytes(Path.Combine(dir, "profile.new-0123456789abcdef0123456789abcdef.tmp"), [1, 2, 3, 4, 5]);

            using (ProfileStore reopened = ProfileStore.Open(dir, TestPassphrase))
            {
                verify(reopened.State == ProfileStoreOpenState.Ready, "Store opens in Ready state despite unpromoted .tmp candidates.");
                verify(reopened.CurrentGeneration == 2UL, "Store selects last committed generation 2 and ignores .tmp candidates.");
                verify(reopened.Diagnostics.Count(d => d.Code == ProfileDiagnosticCode.UnpromotedTemporaryCandidateIgnored) == 2,
                    "Both unpromoted .tmp candidates are reported via UnpromotedTemporaryCandidateIgnored diagnostics.");
                verify(Directory.GetFiles(dir, "profile.new-*.tmp").Length == 2,
                    "Normal startup never auto-deletes unpromoted files outside an explicit purge flow.");
            }
        }
        finally
        {
            CleanupDirectory(dir);
        }
    }

    private static void VerifyCorruptLatestGenerationAndBranchedRecovery(Action<bool, string> verify)
    {
        string dir = CreateTempDirectory();
        try
        {
            Guid pid = Guid.NewGuid();
            Guid originalBranchId = Guid.NewGuid();

            using (ProfileStore store = ProfileStore.CreateNew(dir, TestPassphrase, "Recovery Profile", "Gen 1", pid, originalBranchId))
            {
                store.Save(BuildPopulatedSnapshot(pid, originalBranchId, 2, "Recovery Profile", "Gen 2", extraAccounts: 3));
            }

            // Corrupt the newest committed generation (generation 2) on disk
            string gen2Path = Path.Combine(dir, ProfileStore.FormatGenerationFileName(2));
            byte[] gen2Bytes = File.ReadAllBytes(gen2Path);
            gen2Bytes[ProfileEnvelopeCodec.HeaderSize + 25] ^= 0xAA;
            File.WriteAllBytes(gen2Path, gen2Bytes);

            using (ProfileStore recoveryStore = ProfileStore.Open(dir, TestPassphrase))
            {
                verify(recoveryStore.State == ProfileStoreOpenState.RecoveryRequired,
                    "When newest generation is corrupt, store enters RecoveryRequired rather than silently loading stale generation 1.");
                verify(recoveryStore.IsReadOnly, "Store in RecoveryRequired state is read-only until explicit recovery.");
                verify(recoveryStore.CurrentSnapshot is null, "CurrentSnapshot is null while in RecoveryRequired state.");
                verify(File.Exists(gen2Path), "Corrupt committed generation 2 file is never automatically deleted.");
                verify(recoveryStore.Diagnostics.Any(d => d.Code == ProfileDiagnosticCode.CorruptLatestGeneration && d.Generation == 2UL),
                    "Diagnostics identify generation 2 as CorruptLatestGeneration.");
                verify(recoveryStore.RecoveryCandidates.Count == 1 && recoveryStore.RecoveryCandidates[0].Generation == 1UL,
                    "Generation 1 is surfaced as a validated recovery candidate.");

                // Perform explicit branched recovery from generation 1
                ProfileSaveMetrics recMetrics = recoveryStore.RecoverFromPriorGeneration(
                    targetGeneration: 1UL,
                    passphrase: TestPassphrase,
                    recoveryNote: "Recovered from generation 1 after generation 2 corruption.");

                verify(recMetrics.Generation == 3UL, "Branched recovery writes maxSeenGeneration + 1 (generation 3).");
                verify(recoveryStore.State == ProfileStoreOpenState.Ready && !recoveryStore.IsReadOnly,
                    "After RecoverFromPriorGeneration, store transitions to writable Ready state.");
                verify(recoveryStore.ProfileId == pid, "Recovered branch preserves ProfileId.");
                verify(recoveryStore.BranchId != originalBranchId, "Recovered branch allocates a new BranchId.");
                verify(recoveryStore.CurrentSnapshot!.Profile.ParentBranchId == originalBranchId,
                    "Recovered branch records ParentBranchId pointing to the prior branch.");
                verify(!string.IsNullOrEmpty(recoveryStore.CurrentSnapshot.Profile.RecoveryProvenance),
                    "Recovered branch records non-empty RecoveryProvenance.");
            }
        }
        finally
        {
            CleanupDirectory(dir);
        }
    }

    private static void VerifyLineageAndManifestConflictDetection(Action<bool, string> verify)
    {
        // 1. Wrong passphrase when no generation authenticates
        string dirWrongPass = CreateTempDirectory();
        try
        {
            using (ProfileStore store = ProfileStore.CreateNew(dirWrongPass, TestPassphrase, "Auth Test", "Gen 1"))
            {
            }

            bool wrongPassRejected = false;
            try
            {
                using ProfileStore _ = ProfileStore.Open(dirWrongPass, "WrongPassphrase-123!");
            }
            catch (ProfileAuthenticationException)
            {
                wrongPassRejected = true;
            }

            verify(wrongPassRejected, "Opening store with wrong passphrase throws ProfileAuthenticationException.");
        }
        finally
        {
            CleanupDirectory(dirWrongPass);
        }

        // 2. Filename vs manifest generation mismatch
        string dirMismatch = CreateTempDirectory();
        try
        {
            using (ProfileStore store = ProfileStore.CreateNew(dirMismatch, TestPassphrase, "Mismatch Test", "Gen 1"))
            {
                store.Save(BuildPopulatedSnapshot(store.ProfileId, store.BranchId, 2, "Mismatch Test", "Gen 2"));
            }

            // Copy generation 1 to generation 5 filename so filename says 5 while manifest says 1
            File.Copy(
                Path.Combine(dirMismatch, ProfileStore.FormatGenerationFileName(1)),
                Path.Combine(dirMismatch, ProfileStore.FormatGenerationFileName(5)));

            using ProfileStore opened = ProfileStore.Open(dirMismatch, TestPassphrase);
            verify(opened.State == ProfileStoreOpenState.RecoveryRequired,
                "Filename/manifest generation mismatch forces RecoveryRequired state.");
            verify(opened.Diagnostics.Any(d => d.Code == ProfileDiagnosticCode.FilenameManifestGenerationMismatch && d.Generation == 5UL),
                "FilenameManifestGenerationMismatch diagnostic is recorded for generation 5.");
        }
        finally
        {
            CleanupDirectory(dirMismatch);
        }

        // 3. Cross-profile / forked-branch conflict in the same directory
        string dirFork = CreateTempDirectory();
        string dirOther = CreateTempDirectory();
        try
        {
            Guid pid = Guid.NewGuid();
            using (ProfileStore store1 = ProfileStore.CreateNew(dirFork, TestPassphrase, "Branch A", "Gen 1", pid, Guid.NewGuid()))
            {
            }

            using (ProfileStore store2 = ProfileStore.CreateNew(dirOther, TestPassphrase, "Branch B", "Gen 1", pid, Guid.NewGuid()))
            {
                store2.Save(BuildPopulatedSnapshot(pid, store2.BranchId, 2, "Branch B", "Gen 2"));
            }

            // Copy unlinked Branch B generation 2 into Branch A directory
            File.Copy(
                Path.Combine(dirOther, ProfileStore.FormatGenerationFileName(2)),
                Path.Combine(dirFork, ProfileStore.FormatGenerationFileName(2)));

            using ProfileStore forked = ProfileStore.Open(dirFork, TestPassphrase);
            verify(forked.State == ProfileStoreOpenState.RecoveryRequired,
                "Unlinked branch fork in directory forces RecoveryRequired state.");
            verify(forked.Diagnostics.Any(d => d.Code == ProfileDiagnosticCode.ForkedBranchConflict),
                "ForkedBranchConflict diagnostic is recorded.");
        }
        finally
        {
            CleanupDirectory(dirFork);
            CleanupDirectory(dirOther);
        }
    }

    private static void VerifyPrivacyPurgeLifecycle(Action<bool, string> verify)
    {
        string dir = CreateTempDirectory();
        try
        {
            Guid pid = Guid.NewGuid();
            Guid bid = Guid.NewGuid();

            using (ProfileStore store = ProfileStore.CreateNew(dir, TestPassphrase, "Purge Test", "Gen 1", pid, bid))
            {
                store.Save(BuildPopulatedSnapshot(pid, bid, 2, "Purge Test", "Gen 2 with sensitive account", extraAccounts: 4));

                // Drop an unpromoted .tmp file to verify privacy purge removes candidates too
                File.WriteAllBytes(Path.Combine(dir, "profile.new-aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa.tmp"), [1, 2, 3]);

                // Execute privacy purge with a reduced snapshot (only 1 account kept)
                StoreSnapshot purged = BuildPopulatedSnapshot(pid, bid, 3, "Purge Test", "Gen 3 after privacy purge", extraAccounts: 0);
                ProfileSaveMetrics purgeMetrics = store.ExecutePrivacyPurge(purged, TestPassphrase);

                verify(purgeMetrics.Generation == 3UL, "Privacy purge commits replacement generation 3.");
                verify(!File.Exists(Path.Combine(dir, ProfileStore.PurgePendingFileName)), "purge.pending is removed after completed privacy purge.");
                verify(!File.Exists(Path.Combine(dir, ProfileStore.PurgeIntentFileName)), "purge.intent.bffond is removed after completed privacy purge.");

                string[] remainingGenFiles = Directory.GetFiles(dir, "generation-*.bffond").Select(Path.GetFileName).ToArray()!;
                verify(remainingGenFiles.Length == 1 && remainingGenFiles[0] == ProfileStore.FormatGenerationFileName(3),
                    "Privacy purge removes all prior generations (including G-1) so no recovery copy with purged data remains.");
                verify(Directory.GetFiles(dir, "profile.new-*.tmp").Length == 0,
                    "Privacy purge removes all recognized temporary candidates.");
            }
        }
        finally
        {
            CleanupDirectory(dir);
        }
    }

    private static void VerifyPassphraseRotationLifecycle(Action<bool, string> verify)
    {
        string dir = CreateTempDirectory();
        try
        {
            Guid pid = Guid.NewGuid();
            Guid bid = Guid.NewGuid();
            byte[] originalProfileSalt;

            using (ProfileStore store = ProfileStore.CreateNew(dir, TestPassphrase, "Rotation Test", "Gen 1", pid, bid))
            {
                store.Save(BuildPopulatedSnapshot(pid, bid, 2, "Rotation Test", "Gen 2", extraAccounts: 3));
                originalProfileSalt = File.ReadAllBytes(Path.Combine(dir, ProfileStore.FormatGenerationFileName(2)))[24..56];

                ProfileSaveMetrics rotMetrics = store.RotatePassphrase(TestPassphrase, RotatedPassphrase, newKdfIterations: 610_000);
                verify(rotMetrics.Generation == 3UL, "Passphrase rotation commits replacement generation 3.");

                string[] remaining = Directory.GetFiles(dir, "generation-*.bffond").Select(Path.GetFileName).ToArray()!;
                verify(remaining.Length == 1 && remaining[0] == ProfileStore.FormatGenerationFileName(3),
                    "Passphrase rotation deletes all old-passphrase generations.");

                byte[] newHeader = File.ReadAllBytes(Path.Combine(dir, remaining[0]))[..ProfileEnvelopeCodec.HeaderSize];
                byte[] newProfileSalt = newHeader[24..56];
                uint newIterations = BinaryPrimitives.ReadUInt32LittleEndian(newHeader.AsSpan(16, 4));
                verify(!originalProfileSalt.AsSpan().SequenceEqual(newProfileSalt), "Passphrase rotation generates a fresh 32-byte profileSalt.");
                verify(newIterations == 610_000U, "Passphrase rotation applies updated KDF iteration count.");
            }

            // Old passphrase must now be rejected
            bool oldPassRejected = false;
            try
            {
                using ProfileStore _ = ProfileStore.Open(dir, TestPassphrase);
            }
            catch (ProfileAuthenticationException)
            {
                oldPassRejected = true;
            }

            verify(oldPassRejected, "Old passphrase cannot unlock profile after rotation.");

            // New passphrase unlocks generation 3 cleanly
            using (ProfileStore reopened = ProfileStore.Open(dir, RotatedPassphrase))
            {
                verify(reopened.State == ProfileStoreOpenState.Ready && reopened.CurrentGeneration == 3UL,
                    "Rotated profile opens in Ready state with new passphrase.");
                verify(reopened.CurrentSnapshot!.Accounts.Accounts.Count == 4,
                    "All account data is preserved across passphrase rotation.");
            }
        }
        finally
        {
            CleanupDirectory(dir);
        }
    }

    private static void VerifyWholeProfileErasureLifecycle(Action<bool, string> verify)
    {
        string dir = CreateTempDirectory();
        try
        {
            using (ProfileStore store = ProfileStore.CreateNew(dir, TestPassphrase, "Erase Test", "Gen 1"))
            {
                store.Save(BuildPopulatedSnapshot(store.ProfileId, store.BranchId, 2, "Erase Test", "Gen 2"));
                store.EraseWholeProfile(TestPassphrase);

                verify(store.State == ProfileStoreOpenState.Erased, "Store state transitions to Erased after EraseWholeProfile.");
                verify(Directory.GetFiles(dir, "generation-*.bffond").Length == 0, "All generation files are removed by EraseWholeProfile.");
                verify(!File.Exists(Path.Combine(dir, ProfileStore.PurgePendingFileName)), "purge.pending is removed last after whole-profile erasure.");
                verify(!File.Exists(Path.Combine(dir, ProfileStore.PurgeIntentFileName)), "purge.intent.bffond is removed last after whole-profile erasure.");
            }
        }
        finally
        {
            CleanupDirectory(dir);
        }
    }

    private static void VerifyInterruptedPurgeFaultInjectionAndResume(Action<bool, string> verify)
    {
        ProfileStoreFaultPoint[] purgeFaultPoints =
        [
            ProfileStoreFaultPoint.PurgeAfterIntentWritten,
            ProfileStoreFaultPoint.PurgeAfterReplacementCommitted,
            ProfileStoreFaultPoint.PurgeDuringSupersededDeletion,
            ProfileStoreFaultPoint.PurgeBeforePendingMarkerRemoval,
        ];

        foreach (ProfileStoreFaultPoint faultPoint in purgeFaultPoints)
        {
            string dir = CreateTempDirectory();
            try
            {
                Guid pid = Guid.NewGuid();
                Guid bid = Guid.NewGuid();

                using (ProfileStore setup = ProfileStore.CreateNew(dir, TestPassphrase, "Fault Purge", "Gen 1", pid, bid))
                {
                    setup.Save(BuildPopulatedSnapshot(pid, bid, 2, "Fault Purge", "Gen 2", extraAccounts: 3));
                }

                // Trigger fault during RotatePassphrase at the specified fault point
                using (ProfileStore faulty = ProfileStore.Open(
                    dir,
                    TestPassphrase,
                    openReadOnly: false,
                    faultHook: pt =>
                    {
                        if (pt == faultPoint)
                        {
                            throw new IOException($"Simulated crash at {faultPoint}.");
                        }
                    }))
                {
                    bool crashed = false;
                    try
                    {
                        faulty.RotatePassphrase(TestPassphrase, RotatedPassphrase);
                    }
                    catch (IOException)
                    {
                        crashed = true;
                    }

                    verify(crashed, $"Simulated crash triggered at {faultPoint}.");
                }

                verify(File.Exists(Path.Combine(dir, ProfileStore.PurgePendingFileName)),
                    $"purge.pending remains on disk after crash at {faultPoint}.");

                // Opening the store after crash must detect purge.pending and enter RecoveryRequired (never silently selecting old generations)
                using (ProfileStore recovering = ProfileStore.Open(dir, TestPassphrase))
                {
                    verify(recovering.State == ProfileStoreOpenState.RecoveryRequired,
                        $"Open after crash at {faultPoint} enters RecoveryRequired state.");
                    verify(recovering.CurrentSnapshot is null,
                        $"Open after crash at {faultPoint} refuses to expose any snapshot until ResumePendingPurge completes.");
                    verify(recovering.Diagnostics.Any(d => d.Code == ProfileDiagnosticCode.IncompletePurgeDetected),
                        $"Open after crash at {faultPoint} emits IncompletePurgeDetected diagnostic.");

                    recovering.ResumePendingPurge(RotatedPassphrase, alternatePassphrase: TestPassphrase);

                    verify(recovering.State == ProfileStoreOpenState.Ready && !recovering.IsReadOnly,
                        $"ResumePendingPurge after {faultPoint} restores Ready writable state.");
                    verify(!File.Exists(Path.Combine(dir, ProfileStore.PurgePendingFileName)),
                        $"ResumePendingPurge after {faultPoint} removes purge.pending.");
                    verify(!File.Exists(Path.Combine(dir, ProfileStore.PurgeIntentFileName)),
                        $"ResumePendingPurge after {faultPoint} removes purge.intent.bffond.");

                    if (faultPoint == ProfileStoreFaultPoint.PurgeAfterIntentWritten)
                    {
                        // Replacement was not yet promoted, so uncommitted rotation rolled back cleanly to Gen 2
                        verify(recovering.CurrentGeneration == 2UL,
                            "When interrupted before replacement promotion, ResumePendingPurge cleans up pending markers and reloads intact Gen 2.");
                    }
                    else
                    {
                        // Replacement Gen 3 was promoted, so ResumePendingPurge completed deletion of Gen 1 and Gen 2
                        verify(recovering.CurrentGeneration == 3UL,
                            $"When interrupted at {faultPoint} after replacement promotion, ResumePendingPurge finishes purging old generations and activates Gen 3.");
                        string[] files = Directory.GetFiles(dir, "generation-*.bffond");
                        verify(files.Length == 1, $"Only replacement generation 3 remains on disk after resuming from {faultPoint}.");
                    }
                }
            }
            finally
            {
                CleanupDirectory(dir);
            }
        }
    }

    private static void VerifyBackupRestoreAndImportMerge(Action<bool, string> verify)
    {
        string dirA = CreateTempDirectory();
        string dirB = CreateTempDirectory();
        string dirRestored = CreateTempDirectory();
        string backupPath = Path.Combine(CreateTempDirectory(), "profile-b-backup.bffond");

        try
        {
            Guid pidA = Guid.NewGuid();
            Guid bidA = Guid.NewGuid();
            using ProfileStore storeA = ProfileStore.CreateNew(dirA, TestPassphrase, "Profile A", "Primary", pidA, bidA);
            storeA.Save(BuildPopulatedSnapshot(pidA, bidA, 2, "Profile A", "Primary with 2 accounts", extraAccounts: 1));

            Guid pidB = Guid.NewGuid();
            Guid bidB = Guid.NewGuid();
            using (ProfileStore storeB = ProfileStore.CreateNew(dirB, RotatedPassphrase, "Profile B", "Secondary", pidB, bidB))
            {
                storeB.Save(BuildPopulatedSnapshot(pidB, bidB, 2, "Profile B", "Secondary with 3 accounts", extraAccounts: 2));
                storeB.ExportBackup(backupPath);
            }

            verify(File.Exists(backupPath), "ExportBackup created verified backup file.");

            // 1. RestoreIntoEmptyProfile preserves ProfileId, BranchId, Generation, and entity IDs
            using (ProfileStore restored = ProfileStore.RestoreIntoEmptyProfile(dirRestored, backupPath, RotatedPassphrase))
            {
                verify(restored.ProfileId == pidB && restored.BranchId == bidB && restored.CurrentGeneration == 2UL,
                    "RestoreIntoEmptyProfile preserves original ProfileId, BranchId, and Generation.");
                verify(restored.CurrentSnapshot!.Accounts.Accounts.Count == 3,
                    "RestoreIntoEmptyProfile preserves all 3 accounts from backup.");
            }

            // 2. ImportAndMerge into non-empty storeA remaps all entity IDs and rewrites references without collision
            ulong nextIdBeforeMerge = storeA.CurrentSnapshot!.Profile.NextId;
            int recordsBeforeMerge = storeA.CurrentSnapshot.Ledger.Records.Count;

            ProfileSaveMetrics mergeMetrics = storeA.ImportAndMerge(backupPath, RotatedPassphrase);
            verify(mergeMetrics.Generation == 3UL, "ImportAndMerge commits generation 3 in target Profile A.");

            StoreSnapshot merged = storeA.CurrentSnapshot!;
            verify(merged.Profile.ProfileId == pidA && merged.Profile.BranchId == bidA,
                "ImportAndMerge preserves target Profile A lineage.");
            verify(merged.Institutions.Institutions.Count == 2, "Merged snapshot contains 2 institutions (1 original + 1 imported).");
            verify(merged.Connections.Connections.Count == 2, "Merged snapshot contains 2 connections (1 original + 1 imported).");
            verify(merged.Accounts.Accounts.Count == 5, "Merged snapshot contains 5 accounts (2 original + 3 imported).");
            verify(merged.Balances.Balances.Count == 5, "Merged snapshot contains 5 balances (2 original + 3 imported).");
            verify(merged.Ledger.Records.Count > recordsBeforeMerge, "Merged ledger contains all original and remapped imported records.");
            verify(merged.Profile.NextId > nextIdBeforeMerge, "Merged NextId counter advanced past all remapped entity IDs.");

            // Verify that imported institution ID was remapped >= nextIdBeforeMerge and connection points to remapped institution ID
            StoreInstitutionEntry importedInst = merged.Institutions.Institutions[1];
            StoreConnectionEntry importedConn = merged.Connections.Connections[1];
            verify(importedInst.Id >= nextIdBeforeMerge, "Imported institution ID was remapped into target NextId space.");
            verify(importedConn.InstitutionId == importedInst.Id, "Imported connection institutionId reference was rewritten to remapped institution ID.");
        }
        finally
        {
            CleanupDirectory(dirA);
            CleanupDirectory(dirB);
            CleanupDirectory(dirRestored);
            CleanupDirectory(Path.GetDirectoryName(backupPath)!);
        }
    }

    private static void VerifyConcurrentAccessAndLocking(Action<bool, string> verify)
    {
        string dir = CreateTempDirectory();
        try
        {
            Guid pid = Guid.NewGuid();
            Guid bid = Guid.NewGuid();

            using (ProfileStore store = ProfileStore.CreateNew(dir, TestPassphrase, "Concurrency Profile", "Gen 1", pid, bid))
            {
                // 1. Concurrent read-write Open attempts from 8 worker tasks while writer lock is held
                int lockConflicts = 0;
                int readOnlySuccesses = 0;
                Task[] lockTasks = new Task[8];
                for (int i = 0; i < lockTasks.Length; i++)
                {
                    int workerIndex = i;
                    lockTasks[i] = Task.Run(() =>
                    {
                        if (workerIndex % 2 == 0)
                        {
                            try
                            {
                                using ProfileStore _ = ProfileStore.Open(dir, TestPassphrase, openReadOnly: false);
                            }
                            catch (ProfileLockConflictException)
                            {
                                Interlocked.Increment(ref lockConflicts);
                            }
                        }
                        else
                        {
                            using ProfileStore ro = ProfileStore.Open(dir, TestPassphrase, openReadOnly: true);
                            if (ro.State == ProfileStoreOpenState.Ready && ro.IsReadOnly)
                            {
                                Interlocked.Increment(ref readOnlySuccesses);
                            }
                        }
                    });
                }

                Task.WaitAll(lockTasks);
                verify(lockConflicts == 4, "All 4 concurrent read-write Open attempts were deterministically blocked by ProfileLockConflictException.");
                verify(readOnlySuccesses == 4, "All 4 concurrent read-only Open attempts succeeded without contention.");

                // 2. Concurrent Save and stream reads on the active thread-safe ProfileStore instance
                Task[] instanceTasks = new Task[6];
                for (int i = 0; i < instanceTasks.Length; i++)
                {
                    int idx = i;
                    instanceTasks[i] = Task.Run(() =>
                    {
                        if (idx < 3)
                        {
                            StoreSnapshot snap = BuildPopulatedSnapshot(pid, bid, 2, "Concurrency Profile", $"Concurrent save {idx}", extraAccounts: idx + 1);
                            _ = store.Save(snap);
                        }
                        else
                        {
                            using SeekableAuthenticatedProfileStream s = store.OpenCurrentGenerationAuthenticatedStream();
                            s.VerifyAllFrames();
                        }
                    });
                }

                Task.WaitAll(instanceTasks);
                verify(store.CurrentGeneration == 4UL, "3 concurrent Save calls serialized cleanly to generation 4.");
            }
        }
        finally
        {
            CleanupDirectory(dir);
        }
    }
}
