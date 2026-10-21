using Broiler.Fond.Presentation.ViewModels;

namespace Broiler.Fond.Presentation.Sessions;

/// <summary>
/// Event arguments for changes to the active account list in the presentation session.
/// </summary>
public sealed class AccountListChangedEventArgs : EventArgs
{
    public IReadOnlyList<AccountViewModel> Accounts { get; }
    public string? Reason { get; }

    public AccountListChangedEventArgs(IReadOnlyList<AccountViewModel> accounts, string? reason = null)
    {
        Accounts = accounts ?? Array.Empty<AccountViewModel>();
        Reason = reason;
    }
}

/// <summary>
/// Event arguments for account balance updates and freshness changes.
/// </summary>
public sealed class BalanceRefreshedEventArgs : EventArgs
{
    public ulong AccountId { get; }
    public string Currency { get; }
    public decimal? BookedAmount { get; }
    public decimal? AvailableAmount { get; }
    public bool IsOffline { get; }

    public BalanceRefreshedEventArgs(
        ulong accountId,
        string currency,
        decimal? bookedAmount,
        decimal? availableAmount,
        bool isOffline)
    {
        AccountId = accountId;
        Currency = currency ?? "EUR";
        BookedAmount = bookedAmount;
        AvailableAmount = availableAmount;
        IsOffline = isOffline;
    }
}

/// <summary>
/// Event arguments for bank connection operational state updates.
/// </summary>
public sealed class SyncStateChangedEventArgs : EventArgs
{
    public ulong? ConnectionId { get; }
    public string State { get; }
    public bool IsSynchronized { get; }

    public SyncStateChangedEventArgs(ulong? connectionId, string state, bool isSynchronized)
    {
        ConnectionId = connectionId;
        State = state ?? string.Empty;
        IsSynchronized = isSynchronized;
    }
}

/// <summary>
/// Event arguments for changes to read-only Safe Mode status.
/// </summary>
public sealed class SafeModeTriggeredEventArgs : EventArgs
{
    public bool IsSafeModeActive { get; }
    public string? Reason { get; }

    public SafeModeTriggeredEventArgs(bool isSafeModeActive, string? reason = null)
    {
        IsSafeModeActive = isSafeModeActive;
        Reason = reason;
    }
}
