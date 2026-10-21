using Broiler.Fond.Presentation.Sessions;
using Broiler.UI;

namespace Broiler.Fond.Presentation.Services;

/// <summary>Both desktop refresh actions enter the same cancellable batch presentation controller.</summary>
internal static class BankRefreshFeedback
{
    public static Task ShowAsync(HostSession session, IDialogService dialogs, IUiDispatcher? dispatcher = null) =>
        RefreshUiContext.RunAsync(dispatcher, () => session.RefreshPresentation.RunAsync(dialogs));

    internal static string BuildMessage(HostSession session)
    {
        var reasons = new List<string>();
        if (!session.IsUnlocked) reasons.Add("Unlock your profile first.");
        if (session.IsSafeModeActive) reasons.Add("Safe Mode is active; resolve it before bank operations.");
        if (!session.ProductConfiguration.IsConfigured) reasons.Add("Configure the assigned application product ID before bank access.");
        if (session.Connections.Count == 0) reasons.Add("No bank connection is configured. Use Connections → Add Connection.");
        if (session.Accounts.Count == 0) reasons.Add("No accounts are configured. Use Accounts → Add Account, then discover and review bank identities.");
        var snapshot = session.CurrentSnapshot;
        int manual = snapshot?.Accounts.Accounts.Count(a => snapshot.Accounts.Bindings.Any(b => b.AccountId == a.Id && b.Connector == "Manual") &&
            !snapshot.Accounts.Bindings.Any(b => b.AccountId == a.Id && b.Connector == "fints" && b.State == "Active")) ?? 0;
        if (manual > 0) reasons.Add($"{manual} manually entered account(s) require bank discovery and explicit identity review.");
        if (session.Connections.Any(c => !c.IsEndpointConfirmed)) reasons.Add("Verify and confirm each exact bank endpoint before sending banking credentials.");
        reasons.Add("A selected supported bank procedure and current bank parameters are required. Parameter acquisition and some approval procedures remain unavailable.");
        return "No bank request was sent and no balances were updated.\n\n" + string.Join("\n", reasons);
    }
}
