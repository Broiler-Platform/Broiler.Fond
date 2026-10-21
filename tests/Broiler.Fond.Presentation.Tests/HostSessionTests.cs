using Broiler.Fond.Presentation.Services;
using Broiler.Fond.Presentation.Sessions;

namespace Broiler.Fond.Presentation.Tests;

internal static class HostSessionTests
{
    public static void Run(Action<bool, string> check)
    {
        string tempDir = Path.Combine(Path.GetTempPath(), "BroilerFond_PresTest_" + Guid.NewGuid().ToString("N"));
        try
        {
            Directory.CreateDirectory(tempDir);
            using var session = new HostSession();

            // Initial state
            check(!session.IsUnlocked, "Initial session is locked.");
            check(session.Accounts.Count == 0, "Initial accounts list is empty.");
            check(session.Connections.Count == 0, "Initial connections list is empty.");
            check(session.Transactions.Count == 0, "Initial transactions list is empty.");
            check(!session.IsSafeModeActive, "Safe mode is initially inactive.");

            // Event tracking
            bool accountListChangedFired = false;
            string? accountListReason = null;
            session.AccountListChanged += (s, e) =>
            {
                accountListChangedFired = true;
                accountListReason = e.Reason;
            };

            bool safeModeFired = false;
            bool safeModeState = false;
            session.SafeModeTriggered += (s, e) =>
            {
                safeModeFired = true;
                safeModeState = e.IsSafeModeActive;
            };

            bool stateChangedFired = false;
            session.StateChanged += (s, e) => stateChangedFired = true;

            // Create new profile
            string passphrase = "CorrectHorseBatteryStaple2026!";
            session.CreateNewProfile(tempDir, passphrase, "Presentation Test Profile");

            check(session.IsUnlocked, "Session is unlocked after profile creation.");
            check(accountListChangedFired, "AccountListChanged event fired on profile creation.");
            check(stateChangedFired, "StateChanged event fired on profile creation.");
            check(session.Metadata?.ProfileId != Guid.Empty, "Profile ID generated.");
            var profileId = session.Metadata?.ProfileId;

            // Safe Mode behavior
            stateChangedFired = false;
            session.IsSafeModeActive = true;
            check(session.IsSafeModeActive, "Safe mode is active.");
            check(safeModeFired && safeModeState, "SafeModeTriggered event fired with true.");
            check(stateChangedFired, "StateChanged event fired on safe mode change.");

            // While in safe mode, payment creation is blocked
            bool paymentBlocked = false;
            try
            {
                session.CreatePaymentDraft(
                    sourceAccountId: 1,
                    beneficiaryName: "Bob",
                    beneficiaryIban: "DE89370400440532013000",
                    beneficiaryBic: null,
                    amount: 50m,
                    remittanceUnstructured: "Test",
                    remittanceCreditorReference: null,
                    executionMode: "StandardCreditTransfer",
                    executionDate: DateOnly.FromDateTime(DateTime.UtcNow));
            }
            catch (InvalidOperationException)
            {
                paymentBlocked = true;
            }
            check(paymentBlocked, "Payment draft creation blocked by Safe Mode.");

            // Disabling safe mode
            safeModeFired = false;
            session.IsSafeModeActive = false;
            check(!session.IsSafeModeActive, "Safe mode disabled.");
            check(safeModeFired && !safeModeState, "SafeModeTriggered event fired with false.");

            // Lock session
            accountListChangedFired = false;
            accountListReason = null;
            session.Lock();

            check(!session.IsUnlocked, "Session is locked after Lock().");
            check(accountListChangedFired, "AccountListChanged fired on lock.");
            check(accountListReason == "Locked", "AccountListChanged reason is 'Locked'.");
            check(session.Accounts.Count == 0, "Accounts list cleared on lock.");

            // Re-unlock
            var dialogs = new BroilerDialogService
            {
                HeadlessProfileUnlockHandler = _ => Task.FromResult(new ProfileUnlockResult(true, passphrase, false)),
                HeadlessAlertHandler = (_, message) =>
                {
                    check(false, "Unexpected unlock alert: " + message);
                    return Task.CompletedTask;
                }
            };
            check(session.EnsureUnlockedAsync(dialogs, tempDir).GetAwaiter().GetResult(),
                "Unlock flow recognizes an existing generation-based profile.");
            check(session.IsUnlocked, "Session re-unlocked successfully.");
            check(session.Metadata?.ProfileId == profileId, "Unlock flow preserves the existing profile identity.");
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
            catch
            {
                // Best-effort cleanup of temp directory
            }
        }
    }
}
