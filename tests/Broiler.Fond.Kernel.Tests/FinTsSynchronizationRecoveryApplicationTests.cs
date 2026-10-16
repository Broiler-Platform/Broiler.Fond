using System.Reflection;
using System.Text.Json;
using Broiler.Fond.Kernel.Domain.Connections;
using Broiler.Fond.Kernel.Domain.Identifiers;
using Broiler.Fond.Kernel.Domain.Institutions;
using Broiler.Fond.Kernel.FinTs;

namespace Broiler.Fond.Kernel.Tests;

internal static class FinTsSynchronizationRecoveryApplicationTests
{
    internal static void Run(Action<bool, string> check)
    {
        int count = 0;
        void Verify(bool condition, string message) { count++; check(condition, message); }

        using var sessionsDoc = JsonDocument.Parse(
            Assembly.GetExecutingAssembly().GetManifestResourceStream("FinTs.pin-tan-synchronization-session-attempt-v1.json")!);
        var vectors = sessionsDoc.RootElement.GetProperty("vectors");

        var cleanSyncVector = VectorNamed(vectors, "clean-assignment-session");
        var cleanRecoveryVector = VectorNamed(vectors, "clean-message-recovery-session");
        var abortSyncVector = VectorNamed(vectors, "bank-abort-on-synchronization");
        var reviewSyncVector = VectorNamed(vectors, "scoped-review-on-synchronization");
        var abortCloseVector = VectorNamed(vectors, "bank-abort-on-closing");
        var reviewCloseVector = VectorNamed(vectors, "scoped-review-on-closing");
        var maxRecoveryVector = VectorNamed(vectors, "maximum-message-recovery-session");

        var institutionConfig = ManualInstitutionConfiguration.Create(
            "Test Bank",
            "https://fints.example.com/banking",
            bankCode: "PUBLIC-BANK",
            bic: "TESTDEFFXXX")
            .ConfirmEndpoint("https://fints.example.com/banking");

        var connId = new ConnectionId(101);
        var instId = new InstitutionId(202);
        var baseTime = new DateTimeOffset(2026, 10, 15, 12, 0, 0, TimeSpan.Zero);

        // 1. BankConnection domain validation and factory checks
        {
            VerifyThrows<ArgumentException>(() =>
                BankConnection.Create(new ConnectionId(0), instId, institutionConfig, "user1"),
                "Zero ConnectionId throws ArgumentException.");

            VerifyThrows<ArgumentException>(() =>
                BankConnection.Create(connId, new InstitutionId(0), institutionConfig, "user1"),
                "Zero InstitutionId throws ArgumentException.");

            VerifyThrows<ArgumentNullException>(() =>
                BankConnection.Create(connId, instId, null!, "user1"),
                "Null institutionConfiguration throws ArgumentNullException.");

            VerifyThrows<ArgumentException>(() =>
                BankConnection.Create(connId, instId, institutionConfig, ""),
                "Empty userId throws ArgumentException.");

            VerifyThrows<ArgumentException>(() =>
                BankConnection.Create(connId, instId, institutionConfig, "   "),
                "Whitespace userId throws ArgumentException.");

            VerifyThrows<ArgumentException>(() =>
                BankConnection.Create(connId, instId, institutionConfig, new string('u', 36)),
                "User ID exceeding 35 characters throws ArgumentException.");

            VerifyThrows<ArgumentException>(() =>
                BankConnection.Create(connId, instId, institutionConfig, "user\0id"),
                "User ID containing control character throws ArgumentException.");

            VerifyThrows<ArgumentException>(() =>
                BankConnection.Create(connId, instId, institutionConfig, "user1", customerId: new string('c', 36)),
                "Customer ID exceeding 35 characters throws ArgumentException.");

            VerifyThrows<ArgumentException>(() =>
                BankConnection.Create(connId, instId, institutionConfig, "user1", customerId: "cust\rid"),
                "Customer ID containing control character throws ArgumentException.");

            VerifyThrows<ArgumentException>(() =>
                BankConnection.Create(connId, instId, institutionConfig, "user1", systemId: new string('s', 31)),
                "System ID exceeding 30 characters throws ArgumentException.");

            VerifyThrows<ArgumentException>(() =>
                BankConnection.Create(connId, instId, institutionConfig, "user1", connectorVersion: ""),
                "Empty connector version throws ArgumentException.");

            // Initial unconfigured state
            var initial = BankConnection.Create(connId, instId, institutionConfig, "user1");
            Verify(initial.Id == connId && initial.InstitutionId == instId &&
                initial.State == BankConnectionState.Unconfigured &&
                initial.SystemId is null && !initial.IsSynchronized &&
                initial.NextClientMessageNumber == 1 &&
                initial.LastSuccessfulContact is null && initial.LastAttemptedContact is null,
                "Initial connection starts in Unconfigured state with null SystemId and IsSynchronized false.");

            // Initial with existing valid system ID
            var preconfigured = BankConnection.Create(connId, instId, institutionConfig, "user1", systemId: "SYS123");
            Verify(preconfigured.State == BankConnectionState.Connected &&
                preconfigured.SystemId == "SYS123" && preconfigured.IsSynchronized,
                "Connection with pre-existing system ID starts in Connected state and IsSynchronized true.");
        }

        // 2. BankConnection state machine and recovery action mappings
        {
            var conn = BankConnection.Create(connId, instId, institutionConfig, "user1");
            Verify(conn.RecoveryAction == "Initial synchronization required to establish customer system identification.",
                "Unconfigured state provides correct recovery action.");

            conn = conn.WithState(BankConnectionState.Authenticating);
            Verify(conn.RecoveryAction == "Authentication in progress.",
                "Authenticating state provides correct recovery action.");

            conn = conn.WithState(BankConnectionState.ChallengeRequired);
            Verify(conn.RecoveryAction == "Complete strong customer authentication challenge on your registered device.",
                "ChallengeRequired state provides correct recovery action.");

            conn = conn.WithState(BankConnectionState.Connected);
            Verify(conn.RecoveryAction == "Connection ready for operations.",
                "Connected state provides correct recovery action.");

            conn = conn.WithState(BankConnectionState.ReauthenticationRequired);
            Verify(conn.RecoveryAction == "Re-synchronization and credential review required.",
                "ReauthenticationRequired state provides correct recovery action.");

            conn = conn.WithState(BankConnectionState.TemporarilyUnavailable);
            Verify(conn.RecoveryAction == "Bank system temporarily unavailable; retry dialogue later.",
                "TemporarilyUnavailable state provides correct recovery action.");

            conn = conn.WithDisabled();
            Verify(conn.State == BankConnectionState.Disabled && !conn.IsSynchronized &&
                conn.RecoveryAction == "Connection deactivated; manual reactivation required.",
                "Disabled state provides correct recovery action and clears IsSynchronized.");

            var reactivated = conn.WithReactivated(BankConnectionState.Unconfigured);
            Verify(reactivated.State == BankConnectionState.Unconfigured,
                "WithReactivated restores connection from Disabled to Unconfigured.");

            VerifyThrows<ArgumentException>(() => conn.WithReactivated(BankConnectionState.Disabled),
                "WithReactivated throws when target state is Disabled.");

            // Counter transitions
            var withNext = conn.WithNextClientMessageNumber(42);
            Verify(withNext.NextClientMessageNumber == 42, "WithNextClientMessageNumber updates message counter.");
            VerifyThrows<ArgumentOutOfRangeException>(() => conn.WithNextClientMessageNumber(0),
                "Message counter < 1 throws ArgumentOutOfRangeException.");
            VerifyThrows<ArgumentOutOfRangeException>(() => conn.WithNextClientMessageNumber(10000),
                "Message counter > 9999 throws ArgumentOutOfRangeException.");

            // Contact timestamps
            var withContact = conn.WithSuccessfulContact(baseTime);
            Verify(withContact.LastSuccessfulContact == baseTime && withContact.LastAttemptedContact == baseTime,
                "WithSuccessfulContact sets both contact timestamps.");
            var withAttempt = conn.WithAttemptedContact(baseTime.AddMinutes(5));
            Verify(withAttempt.LastAttemptedContact == baseTime.AddMinutes(5),
                "WithAttemptedContact sets only attempted contact timestamp.");

            // Value equality
            var connA = BankConnection.Create(connId, instId, institutionConfig, "user1");
            var connB = BankConnection.Create(connId, instId, institutionConfig, "user1");
            Verify(connA.Equals(connB) && connA.GetHashCode() == connB.GetHashCode(),
                "Two identical BankConnection instances compare equal and have equal hash code.");
            Verify(!connA.Equals(connA.WithState(BankConnectionState.Connected)),
                "BankConnections with differing state compare unequal.");
            string str = connA.ToString();
            Verify(str.Contains("BankConnection"), "ToString contains type description.");
        }

        // 3. Mode 0 Recovery Application (Kundensystem-ID vergeben)
        {
            var syncResult = RunCleanSyncSession(cleanSyncVector);
            Verify(syncResult.Transition == FinTsPinTanSynchronizationSessionTransition.ReinitializationRequired &&
                syncResult.HasMatchingEvidence && syncResult.SynchronizationEvidence?.MatchingReport?.SystemId is not null,
                "Clean assignment session produces valid ReinitializationRequired result.");

            // Extract the user ID from the vector's candidate
            var candUserId = syncResult.SynchronizationEvidence!.Request.ExpectedUserId;
            var connection = BankConnection.Create(connId, instId, institutionConfig, candUserId);

            var recoveryResult = connection.ApplySynchronization(syncResult, baseTime);
            Verify(recoveryResult.IsSuccess && recoveryResult.Outcome == FinTsSynchronizationRecoveryOutcome.Applied,
                "Mode 0 recovery application succeeds with Applied outcome.");
            Verify(recoveryResult.AppliedSystemId == syncResult.SynchronizationEvidence.MatchingReport!.SystemId,
                "Mode 0 recovery reports the assigned SystemId.");
            Verify(recoveryResult.SynchronizedMessageNumber == 1,
                "Mode 0 recovery resets client message number to 1.");

            var updated = recoveryResult.UpdatedConnection;
            Verify(updated.State == BankConnectionState.Connected, "Updated connection transitions to Connected.");
            Verify(updated.SystemId == syncResult.SynchronizationEvidence.MatchingReport.SystemId,
                "Updated connection stores the assigned SystemId.");
            Verify(updated.IsSynchronized, "Updated connection is marked IsSynchronized.");
            Verify(updated.NextClientMessageNumber == 1, "Updated connection NextClientMessageNumber is 1.");
            Verify(updated.LastSuccessfulContact == baseTime && updated.LastAttemptedContact == baseTime,
                "Updated connection records contact timestamps.");
            Verify(updated.RecoveryAction == "Connection ready for operations.",
                "Updated connection recovery action reports ready for operations.");
        }

        // 4. Mode 1 Recovery Application (Letzte Nachrichtennummer abgleichen)
        {
            var syncResult = RunCleanSyncSession(cleanRecoveryVector, isRecovery: true);
            Verify(syncResult.Transition == FinTsPinTanSynchronizationSessionTransition.ReinitializationRequired &&
                syncResult.HasMatchingEvidence && syncResult.SynchronizationEvidence?.MatchingReport?.LastMessageNumber is not null,
                "Clean message recovery session produces valid ReinitializationRequired result.");

            var candUserId = syncResult.SynchronizationEvidence!.Request.ExpectedUserId;
            var preSyncSysId = syncResult.SynchronizationEvidence.Request.SignatureEvidence.Request.Synchronization!.Identification.SystemId;
            var connection = BankConnection.Create(connId, instId, institutionConfig, candUserId, systemId: preSyncSysId);

            var recoveryResult = connection.ApplySynchronization(syncResult, baseTime);
            Verify(recoveryResult.IsSuccess && recoveryResult.Outcome == FinTsSynchronizationRecoveryOutcome.Applied,
                "Mode 1 recovery application succeeds with Applied outcome.");

            int bankReportedLast = syncResult.SynchronizationEvidence.MatchingReport!.LastMessageNumber!.Value;
            int expectedNextMsg = bankReportedLast + 1;
            Verify(recoveryResult.SynchronizedMessageNumber == expectedNextMsg,
                "Mode 1 recovery computes next client message number as bank reported + 1.");

            var updated = recoveryResult.UpdatedConnection;
            Verify(updated.State == BankConnectionState.Connected && updated.IsSynchronized,
                "Updated connection remains Connected and IsSynchronized.");
            Verify(updated.NextClientMessageNumber == expectedNextMsg,
                "Updated connection NextClientMessageNumber is synchronized to next message number.");
            Verify(updated.SystemId == preSyncSysId, "Updated connection preserves pre-existing SystemId.");
        }

        // 5. Maximum message recovery boundary
        {
            var maxResult = RunCleanSyncSession(maxRecoveryVector, isRecovery: true);
            var candUserId = maxResult.SynchronizationEvidence!.Request.ExpectedUserId;
            var sysId = maxResult.SynchronizationEvidence.Request.SignatureEvidence.Request.Synchronization!.Identification.SystemId;
            var connection = BankConnection.Create(connId, instId, institutionConfig, candUserId, systemId: sysId);

            var recoveryResult = connection.ApplySynchronization(maxResult, baseTime);
            Verify(recoveryResult.IsSuccess && recoveryResult.Outcome == FinTsSynchronizationRecoveryOutcome.Applied,
                "Maximum message recovery succeeds with Applied outcome.");
            Verify(recoveryResult.UpdatedConnection.IsSynchronized, "Maximum message recovery leaves connection synchronized.");
        }

        // 6. Fail-closed rejections: Null arguments and disabled connection
        {
            var cleanResult = RunCleanSyncSession(cleanSyncVector);
            var candUserId = cleanResult.SynchronizationEvidence!.Request.ExpectedUserId;
            var connection = BankConnection.Create(connId, instId, institutionConfig, candUserId);

            VerifyThrows<ArgumentNullException>(() =>
                FinTsSynchronizationRecoveryApplication.Apply(null!, cleanResult),
                "Null connection throws ArgumentNullException.");

            VerifyThrows<ArgumentNullException>(() =>
                FinTsSynchronizationRecoveryApplication.Apply(connection, null!),
                "Null result throws ArgumentNullException.");

            // Disabled connection
            var disabledConn = connection.WithDisabled();
            var disabledRes = disabledConn.ApplySynchronization(cleanResult, baseTime);
            Verify(disabledRes.Outcome == FinTsSynchronizationRecoveryOutcome.RejectedDisabledConnection &&
                disabledRes.Issues.HasFlag(FinTsSynchronizationRecoveryIssue.ConnectionDisabled) &&
                !disabledRes.IsSuccess,
                "Disabled connection rejects recovery with RejectedDisabledConnection.");
            Verify(ReferenceEquals(disabledRes.UpdatedConnection, disabledConn),
                "RejectedDisabledConnection leaves connection unmodified.");
        }

        // 7. Fail-closed rejections: Bank abort on synchronization (9800)
        {
            var abortResult = RunSyncAbortSession(abortSyncVector);
            var candUserId = abortResult.SynchronizationEvidence!.Request.ExpectedUserId;
            var connection = BankConnection.Create(connId, instId, institutionConfig, candUserId);

            var res = connection.ApplySynchronization(abortResult, baseTime);
            Verify(res.Outcome == FinTsSynchronizationRecoveryOutcome.BankAborted &&
                res.Issues.HasFlag(FinTsSynchronizationRecoveryIssue.BankAborted) &&
                !res.IsSuccess,
                "Sync abort produces BankAborted recovery outcome.");
            Verify(res.UpdatedConnection.State == BankConnectionState.TemporarilyUnavailable,
                "Bank abort transitions connection to TemporarilyUnavailable.");
            Verify(res.UpdatedConnection.LastAttemptedContact == baseTime &&
                res.UpdatedConnection.LastSuccessfulContact is null,
                "Bank abort updates LastAttemptedContact without recording successful contact.");
        }

        // 8. Fail-closed rejections: Bank abort on closing (9800)
        {
            var abortCloseResult = RunCloseAbortSession(abortCloseVector);
            var candUserId = abortCloseResult.SynchronizationEvidence!.Request.ExpectedUserId;
            var connection = BankConnection.Create(connId, instId, institutionConfig, candUserId);

            var res = connection.ApplySynchronization(abortCloseResult, baseTime);
            Verify(res.Outcome == FinTsSynchronizationRecoveryOutcome.BankAborted &&
                res.Issues.HasFlag(FinTsSynchronizationRecoveryIssue.BankAborted) &&
                !res.IsSuccess,
                "Closing abort produces BankAborted recovery outcome.");
            Verify(res.UpdatedConnection.State == BankConnectionState.TemporarilyUnavailable,
                "Closing abort transitions connection to TemporarilyUnavailable.");
        }

        // 9. Fail-closed rejections: Scoped review on synchronization
        {
            var reviewResult = RunSyncReviewSession(reviewSyncVector);
            var candUserId = reviewResult.SynchronizationEvidence!.Request.ExpectedUserId;
            var connection = BankConnection.Create(connId, instId, institutionConfig, candUserId);

            var res = connection.ApplySynchronization(reviewResult, baseTime);
            Verify(res.Outcome == FinTsSynchronizationRecoveryOutcome.ReviewRequired &&
                res.Issues.HasFlag(FinTsSynchronizationRecoveryIssue.ReviewRequired) &&
                !res.IsSuccess,
                "Sync review produces ReviewRequired recovery outcome.");
            Verify(res.UpdatedConnection.State == BankConnectionState.ReauthenticationRequired,
                "Sync review transitions connection to ReauthenticationRequired.");
        }

        // 10. Fail-closed rejections: Scoped review on closing
        {
            var reviewCloseResult = RunCloseReviewSession(reviewCloseVector);
            var candUserId = reviewCloseResult.SynchronizationEvidence!.Request.ExpectedUserId;
            var connection = BankConnection.Create(connId, instId, institutionConfig, candUserId);

            var res = connection.ApplySynchronization(reviewCloseResult, baseTime);
            Verify(res.Outcome == FinTsSynchronizationRecoveryOutcome.ReviewRequired &&
                res.Issues.HasFlag(FinTsSynchronizationRecoveryIssue.ReviewRequired) &&
                !res.IsSuccess,
                "Closing review produces ReviewRequired recovery outcome.");
            Verify(res.UpdatedConnection.State == BankConnectionState.ReauthenticationRequired,
                "Closing review transitions connection to ReauthenticationRequired.");
        }

        // 11. Fail-closed rejections: Identity mismatch (user and bank code)
        {
            var cleanResult = RunCleanSyncSession(cleanSyncVector);

            // User mismatch
            var wrongUserConn = BankConnection.Create(connId, instId, institutionConfig, "completely_different_user");
            var userMismatchRes = wrongUserConn.ApplySynchronization(cleanResult, baseTime);
            Verify(userMismatchRes.Outcome == FinTsSynchronizationRecoveryOutcome.RejectedIdentityMismatch &&
                userMismatchRes.Issues.HasFlag(FinTsSynchronizationRecoveryIssue.UserMismatch) &&
                !userMismatchRes.IsSuccess,
                "User mismatch rejects recovery with RejectedIdentityMismatch.");

            // Bank code mismatch
            var candUserId = cleanResult.SynchronizationEvidence!.Request.ExpectedUserId;
            var wrongBankConfig = ManualInstitutionConfiguration.Create(
                "Wrong Bank",
                "https://fints.example.com/banking",
                bankCode: "87654321")
                .ConfirmEndpoint("https://fints.example.com/banking");
            var wrongBankConn = BankConnection.Create(connId, instId, wrongBankConfig, candUserId);
            var bankMismatchRes = wrongBankConn.ApplySynchronization(cleanResult, baseTime);
            Verify(bankMismatchRes.Outcome == FinTsSynchronizationRecoveryOutcome.RejectedIdentityMismatch &&
                bankMismatchRes.Issues.HasFlag(FinTsSynchronizationRecoveryIssue.BankCodeMismatch) &&
                !bankMismatchRes.IsSuccess,
                "Bank code mismatch rejects recovery with RejectedIdentityMismatch.");
        }

        // 12. Fail-closed rejections: Incomplete or invalid result structures
        {
            var cleanResult = RunCleanSyncSession(cleanSyncVector);
            var candUserId = cleanResult.SynchronizationEvidence!.Request.ExpectedUserId;
            var connection = BankConnection.Create(connId, instId, institutionConfig, candUserId);

            // Synthetic result with missing closing evidence
            var missingClosing = new FinTsPinTanSynchronizationSessionResult(
                FinTsPinTanSynchronizationSessionTransition.ReinitializationRequired,
                FinTsPinTanSynchronizationSessionStage.Terminal,
                cleanResult.SynchronizationEvidence,
                closingEvidence: null);
            var missingCloseRes = connection.ApplySynchronization(missingClosing, baseTime);
            Verify(missingCloseRes.Outcome == FinTsSynchronizationRecoveryOutcome.RejectedInvalidResult &&
                missingCloseRes.Issues.HasFlag(FinTsSynchronizationRecoveryIssue.MissingClosingEvidence),
                "Missing closing evidence rejects recovery with RejectedInvalidResult.");

            // Synthetic result with missing synchronization evidence
            var missingSync = new FinTsPinTanSynchronizationSessionResult(
                FinTsPinTanSynchronizationSessionTransition.ReinitializationRequired,
                FinTsPinTanSynchronizationSessionStage.Terminal,
                synchronizationEvidence: null,
                cleanResult.ClosingEvidence);
            var missingSyncRes = connection.ApplySynchronization(missingSync, baseTime);
            Verify(missingSyncRes.Outcome == FinTsSynchronizationRecoveryOutcome.RejectedInvalidResult &&
                missingSyncRes.Issues.HasFlag(FinTsSynchronizationRecoveryIssue.MissingSynchronizationEvidence),
                "Missing synchronization evidence rejects recovery with RejectedInvalidResult.");

            // Synthetic result with wrong transition
            var wrongTrans = new FinTsPinTanSynchronizationSessionResult(
                FinTsPinTanSynchronizationSessionTransition.CandidateRecorded,
                FinTsPinTanSynchronizationSessionStage.Synchronization,
                cleanResult.SynchronizationEvidence,
                cleanResult.ClosingEvidence);
            var wrongTransRes = connection.ApplySynchronization(wrongTrans, baseTime);
            Verify(wrongTransRes.Outcome == FinTsSynchronizationRecoveryOutcome.RejectedInvalidResult &&
                wrongTransRes.Issues.HasFlag(FinTsSynchronizationRecoveryIssue.InvalidResultState),
                "Wrong session transition rejects recovery with RejectedInvalidResult.");
        }

        // 13. Concurrency atomicity: 16 parallel threads applying recovery
        {
            var cleanResult = RunCleanSyncSession(cleanSyncVector);
            var candUserId = cleanResult.SynchronizationEvidence!.Request.ExpectedUserId;
            var connection = BankConnection.Create(connId, instId, institutionConfig, candUserId);

            var results = new FinTsSynchronizationRecoveryResult[16];
            Parallel.For(0, 16, i =>
            {
                results[i] = connection.ApplySynchronization(cleanResult, baseTime);
            });

            bool allSucceeded = results.All(r => r.IsSuccess && r.Outcome == FinTsSynchronizationRecoveryOutcome.Applied);
            bool allIdentical = results.All(r => r.AppliedSystemId == results[0].AppliedSystemId &&
                r.SynchronizedMessageNumber == results[0].SynchronizedMessageNumber &&
                r.UpdatedConnection.Equals(results[0].UpdatedConnection));

            Verify(allSucceeded && allIdentical,
                "16 concurrent recovery applications produce identical, deterministic Applied results.");
        }

        // 14. Customer ID matching and mismatching
        {
            var cleanResult = RunCleanSyncSession(cleanSyncVector);
            var candUserId = cleanResult.SynchronizationEvidence!.Request.ExpectedUserId;

            // Matching customerId
            var matchCustConn = BankConnection.Create(connId, instId, institutionConfig, candUserId, customerId: "PUBLIC-CUSTOMER");
            var matchCustRes = matchCustConn.ApplySynchronization(cleanResult, baseTime);
            Verify(matchCustRes.IsSuccess && matchCustRes.Outcome == FinTsSynchronizationRecoveryOutcome.Applied,
                "Matching customerId applies recovery successfully.");
            Verify(matchCustRes.UpdatedConnection.CustomerId == "PUBLIC-CUSTOMER",
                "Updated connection retains matching customerId.");

            // Mismatched customerId
            var mismatchCustConn = BankConnection.Create(connId, instId, institutionConfig, candUserId, customerId: "OTHER-CUSTOMER");
            var mismatchCustRes = mismatchCustConn.ApplySynchronization(cleanResult, baseTime);
            Verify(mismatchCustRes.Outcome == FinTsSynchronizationRecoveryOutcome.RejectedIdentityMismatch &&
                mismatchCustRes.Issues.HasFlag(FinTsSynchronizationRecoveryIssue.UserMismatch) &&
                !mismatchCustRes.IsSuccess,
                "Mismatched customerId rejects recovery with RejectedIdentityMismatch.");
        }

        // 15. Mode 0 updating pre-existing system ID and resetting counter
        {
            var cleanResult = RunCleanSyncSession(cleanSyncVector);
            var candUserId = cleanResult.SynchronizationEvidence!.Request.ExpectedUserId;

            // Connection with prior system ID and advanced message counter
            var priorConn = BankConnection.Create(connId, instId, institutionConfig, candUserId, systemId: "OLD-SYSTEM-ID")
                .WithNextClientMessageNumber(25);
            Verify(priorConn.SystemId == "OLD-SYSTEM-ID" && priorConn.NextClientMessageNumber == 25,
                "Prior connection starts with old system ID and message counter 25.");

            var reassignResult = priorConn.ApplySynchronization(cleanResult, baseTime);
            Verify(reassignResult.IsSuccess && reassignResult.Outcome == FinTsSynchronizationRecoveryOutcome.Applied,
                "Mode 0 re-synchronization applies to connection with prior system ID.");

            var newSysId = cleanResult.SynchronizationEvidence!.MatchingReport!.SystemId;
            Verify(reassignResult.UpdatedConnection.SystemId == newSysId,
                "Reassigned connection updates to newly assigned bank system ID.");
            Verify(reassignResult.UpdatedConnection.NextClientMessageNumber == 1,
                "Mode 0 re-synchronization resets NextClientMessageNumber from 25 back to 1.");
        }

        // 16. Immutability guarantee
        {
            var cleanResult = RunCleanSyncSession(cleanSyncVector);
            var candUserId = cleanResult.SynchronizationEvidence!.Request.ExpectedUserId;
            var original = BankConnection.Create(connId, instId, institutionConfig, candUserId);

            var originalState = original.State;
            var originalSysId = original.SystemId;
            var originalSync = original.IsSynchronized;
            var originalCounter = original.NextClientMessageNumber;

            var res = original.ApplySynchronization(cleanResult, baseTime);
            Verify(res.IsSuccess && !ReferenceEquals(original, res.UpdatedConnection),
                "Recovery application returns a new BankConnection instance.");
            Verify(original.State == originalState &&
                original.SystemId == originalSysId &&
                original.IsSynchronized == originalSync &&
                original.NextClientMessageNumber == originalCounter,
                "Original BankConnection remains completely unmodified (pure immutability).");
        }

        // 17. Concurrent mixed submissions
        {
            var cleanResult = RunCleanSyncSession(cleanSyncVector);
            var abortResult = RunSyncAbortSession(abortSyncVector);
            var candUserId = cleanResult.SynchronizationEvidence!.Request.ExpectedUserId;
            var connection = BankConnection.Create(connId, instId, institutionConfig, candUserId);

            var cleanOutcomes = new bool[8];
            var abortOutcomes = new bool[8];

            Parallel.For(0, 16, i =>
            {
                if (i % 2 == 0)
                {
                    var r = connection.ApplySynchronization(cleanResult, baseTime);
                    cleanOutcomes[i / 2] = r.IsSuccess && r.Outcome == FinTsSynchronizationRecoveryOutcome.Applied &&
                        r.UpdatedConnection.State == BankConnectionState.Connected;
                }
                else
                {
                    var r = connection.ApplySynchronization(abortResult, baseTime);
                    abortOutcomes[i / 2] = !r.IsSuccess && r.Outcome == FinTsSynchronizationRecoveryOutcome.BankAborted &&
                        r.UpdatedConnection.State == BankConnectionState.TemporarilyUnavailable;
                }
            });

            Verify(cleanOutcomes.All(b => b), "All concurrent clean applications succeeded without cross-contamination.");
            Verify(abortOutcomes.All(b => b), "All concurrent abort applications transitioned without cross-contamination.");
        }

        Console.WriteLine($"FinTS synchronization recovery application and connection state transition passed ({count} checks).");
    }

    private static FinTsPinTanSynchronizationSessionResult RunCleanSyncSession(JsonElement vector, bool isRecovery = false)
    {
        using var attempt = new FinTsPinTanSynchronizationSessionAttempt(TimeSpan.FromMinutes(5));
        var syncCand = MakeSyncCand(vector);

        FinTsSynchronizationRecoveryContext? recovery = null;
        if (isRecovery && vector.GetProperty("recovery").ValueKind != JsonValueKind.Null)
        {
            var recElem = vector.GetProperty("recovery");
            recovery = new FinTsSynchronizationRecoveryContext(
                recElem.GetProperty("previousDialogueId").GetString()!,
                recElem.GetProperty("lastSubmittedMessageNumber").GetInt32());
        }

        attempt.StartSynchronization(syncCand, recovery);
        var syncResult = attempt.AcceptSynchronizationResponse(
            FinTsSynchronizationDataSet.Parse(Response(vector.GetProperty("syncResponse"))));

        var closeCand = ClosingCandidate(vector.GetProperty("closingCandidate"), syncResult.SynchronizationEvidence!);
        attempt.RecordClosingCandidate(closeCand);
        return attempt.AcceptClosingResponse(Response(vector.GetProperty("closingResponse")));
    }

    private static FinTsPinTanSynchronizationSessionResult RunSyncAbortSession(JsonElement vector)
    {
        using var attempt = new FinTsPinTanSynchronizationSessionAttempt(TimeSpan.FromMinutes(5));
        var syncCand = MakeSyncCand(vector);
        attempt.StartSynchronization(syncCand);
        return attempt.AcceptSynchronizationResponse(
            FinTsSynchronizationDataSet.Parse(Response(vector.GetProperty("syncResponse"))));
    }

    private static FinTsPinTanSynchronizationSessionResult RunSyncReviewSession(JsonElement vector)
    {
        using var attempt = new FinTsPinTanSynchronizationSessionAttempt(TimeSpan.FromMinutes(5));
        var syncCand = MakeSyncCand(vector);
        attempt.StartSynchronization(syncCand);
        return attempt.AcceptSynchronizationResponse(
            FinTsSynchronizationDataSet.Parse(Response(vector.GetProperty("syncResponse"))));
    }

    private static FinTsPinTanSynchronizationSessionResult RunCloseAbortSession(JsonElement vector)
    {
        using var attempt = new FinTsPinTanSynchronizationSessionAttempt(TimeSpan.FromMinutes(5));
        var syncCand = MakeSyncCand(vector);
        attempt.StartSynchronization(syncCand);
        var syncResult = attempt.AcceptSynchronizationResponse(
            FinTsSynchronizationDataSet.Parse(Response(vector.GetProperty("syncResponse"))));
        var closeCand = ClosingCandidate(vector.GetProperty("closingCandidate"), syncResult.SynchronizationEvidence!);
        attempt.RecordClosingCandidate(closeCand);
        return attempt.AcceptClosingResponse(Response(vector.GetProperty("closingResponse")));
    }

    private static FinTsPinTanSynchronizationSessionResult RunCloseReviewSession(JsonElement vector)
    {
        using var attempt = new FinTsPinTanSynchronizationSessionAttempt(TimeSpan.FromMinutes(5));
        var syncCand = MakeSyncCand(vector);
        attempt.StartSynchronization(syncCand);
        var syncResult = attempt.AcceptSynchronizationResponse(
            FinTsSynchronizationDataSet.Parse(Response(vector.GetProperty("syncResponse"))));
        var closeCand = ClosingCandidate(vector.GetProperty("closingCandidate"), syncResult.SynchronizationEvidence!);
        attempt.RecordClosingCandidate(closeCand);
        return attempt.AcceptClosingResponse(Response(vector.GetProperty("closingResponse")));
    }

    private static FinTsPinTanRequestBinding MakeSyncCand(JsonElement vector) =>
        FinTsPinTanRequestBinding.ForEnvelopeCandidate(
            FinTsPinTanSignatureTrailerTests.Evidence(vector.GetProperty("syncCandidate")));

    private static FinTsPinTanDialogueEndRequestBinding ClosingCandidate(JsonElement element, FinTsPinTanSynchronizationEvidence syncEvidence)
    {
        var unsignedClosing = FinTsUnsignedDialogueEndRequest.Parse(
            FinTsMessageFrame.Parse(Convert.FromBase64String(element.GetProperty("requestBase64").GetString()!)),
            element.GetProperty("expectedBankMessageNumber").GetInt32());
        var closingHeader = FinTsPinTanSignatureHeader.Parse(
            FinTsSyntax.ParseSegments(Convert.FromBase64String(element.GetProperty("headerBase64").GetString()!)).Segments.Single());
        var closingContext = FinTsPinTanDialogueEndContext.Evaluate(syncEvidence, unsignedClosing, closingHeader);
        return FinTsPinTanDialogueEndRequestBinding.ForEnvelopeCandidate(closingContext);
    }

    private static FinTsResponse Response(JsonElement element)
    {
        var frame = FinTsMessageFrame.Parse(Convert.FromBase64String(
            element.TryGetProperty("wireBase64", out var wireElem)
                ? wireElem.GetString()!
                : element.GetProperty("responseBase64").GetString()!));
        return element.GetProperty("wrapped").GetBoolean()
            ? FinTsResponse.ParsePinTan(FinTsPinTanEnvelope.Parse(frame))
            : FinTsResponse.Parse(frame);
    }

    private static JsonElement VectorNamed(JsonElement array, string name)
    {
        foreach (var element in array.EnumerateArray())
        {
            if (element.GetProperty("name").GetString() == name) { return element; }
        }
        throw new KeyNotFoundException($"Vector '{name}' not found.");
    }

    private static void VerifyThrows<T>(Action action, string message) where T : Exception
    {
        bool threw = false;
        try { action(); }
        catch (T) { threw = true; }
        catch (Exception) { }
        if (!threw) { throw new InvalidOperationException($"Expected exception of type {typeof(T).Name}: {message}"); }
    }
}
