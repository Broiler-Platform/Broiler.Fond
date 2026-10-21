using Broiler.Fond.Presentation.Services;

namespace Broiler.Fond.Presentation.Tests;

internal static class ServiceSeamTests
{
    private sealed class MockDialogService : IDialogService
    {
        public Task<bool> ShowEndpointConfirmationAsync(string endpoint, CancellationToken cancellationToken = default) => Task.FromResult(false);
        public Task<AccountSetupResult> ShowAccountSetupAsync(AccountSetupRequest request, CancellationToken cancellationToken = default) =>
            Task.FromResult(new AccountSetupResult(false, 0, "", "", "", true));
        public bool NextConfirmationResult { get; set; } = true;
        public ProfileUnlockResult NextUnlockResult { get; set; } = new(true, "SecretPassphrase123!", false);
        public CredentialPromptResult NextCredentialResult { get; set; } = new(true, "12345", false);
        public PaymentReviewResult NextReviewResult { get; set; } = new(true, false);
        public InstitutionSetupResult NextSetupResult { get; set; } = new(true, "Bank AG", "12345678", "https://bank.de", false);

        public Task<bool> ShowConfirmationAsync(string title, string message, string? confirmLabel = null, string? cancelLabel = null, CancellationToken cancellationToken = default)
        {
            return Task.FromResult(NextConfirmationResult);
        }

        public Task ShowAlertAsync(string title, string message, CancellationToken cancellationToken = default)
        {
            return Task.CompletedTask;
        }

        public Task<ProfileUnlockResult> ShowProfileUnlockAsync(string profileDirectory, CancellationToken cancellationToken = default)
        {
            return Task.FromResult(NextUnlockResult);
        }

        public Task<CredentialPromptResult> ShowCredentialPromptAsync(CredentialPromptRequest request, CancellationToken cancellationToken = default)
        {
            return Task.FromResult(NextCredentialResult);
        }

        public Task<PaymentReviewResult> ShowPaymentReviewAsync(PaymentReviewRequest request, CancellationToken cancellationToken = default)
        {
            return Task.FromResult(NextReviewResult);
        }

        public Task<InstitutionSetupResult> ShowInstitutionSetupAsync(InstitutionSetupRequest request, CancellationToken cancellationToken = default)
        {
            return Task.FromResult(NextSetupResult);
        }
    }

    public static void Run(Action<bool, string> check)
    {
        // 1. NavigationService
        var nav = new NavigationService();
        check(nav.CurrentTarget == NavigationTarget.Accounts, "Initial target is Accounts.");

        NavigationTarget? prevTarget = null;
        NavigationTarget? newTarget = null;
        int navEventsCount = 0;
        nav.CurrentTargetChanged += (s, e) =>
        {
            prevTarget = e.PreviousTarget;
            newTarget = e.NewTarget;
            navEventsCount++;
        };

        nav.NavigateTo(NavigationTarget.Transactions);
        check(nav.CurrentTarget == NavigationTarget.Transactions, "Current target updated to Transactions.");
        check(prevTarget == NavigationTarget.Accounts, "Previous target was Accounts.");
        check(newTarget == NavigationTarget.Transactions, "New target is Transactions.");
        check(navEventsCount == 1, "Exactly one navigation event fired.");

        // No-op navigation to current target
        nav.NavigateTo(NavigationTarget.Transactions);
        check(navEventsCount == 1, "No event fired for re-navigating to same target.");

        nav.NavigateTo(NavigationTarget.Security);
        check(nav.CurrentTarget == NavigationTarget.Security, "Current target updated to Security.");
        check(navEventsCount == 2, "Second navigation event fired.");

        // 2. HeadlessClipboardService
        var clip = new HeadlessClipboardService();
        clip.SetTransientTextAsync("test-clipboard", TimeSpan.FromSeconds(30)).GetAwaiter().GetResult();
        string? text = clip.GetTextAsync().GetAwaiter().GetResult();
        check(text == "test-clipboard", "Clipboard returns set text.");

        clip.ClearAsync().GetAwaiter().GetResult();
        string? cleared = clip.GetTextAsync().GetAwaiter().GetResult();
        check(cleared is null, "Clipboard returns null after ClearAsync.");

        // 3. IDialogService mock dispatch
        var dialogs = new MockDialogService();
        bool confirmed = dialogs.ShowConfirmationAsync("Delete Account", "Are you sure?").GetAwaiter().GetResult();
        check(confirmed, "MockDialogService returns confirmed.");

        var unlockRes = dialogs.ShowProfileUnlockAsync("/path/to/profile").GetAwaiter().GetResult();
        check(unlockRes.Succeeded && unlockRes.Passphrase == "SecretPassphrase123!", "Unlock result received.");

        var credRes = dialogs.ShowCredentialPromptAsync(new CredentialPromptRequest("PIN Prompt", "Enter banking PIN", "Test Bank")).GetAwaiter().GetResult();
        check(credRes.Succeeded && credRes.Credential == "12345", "Credential result received.");
    }
}
