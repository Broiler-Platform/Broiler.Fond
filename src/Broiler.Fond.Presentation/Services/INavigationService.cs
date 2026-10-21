namespace Broiler.Fond.Presentation.Services;

/// <summary>
/// Primary top-level navigation sections within the Broiler Fond workspace.
/// </summary>
public enum NavigationTarget
{
    Accounts,
    Transactions,
    Insights,
    Connections,
    Security
}

/// <summary>
/// Event arguments for workspace navigation transitions.
/// </summary>
public sealed class NavigationTargetChangedEventArgs : EventArgs
{
    public NavigationTarget PreviousTarget { get; }
    public NavigationTarget NewTarget { get; }

    public NavigationTargetChangedEventArgs(NavigationTarget previousTarget, NavigationTarget newTarget)
    {
        PreviousTarget = previousTarget;
        NewTarget = newTarget;
    }
}

/// <summary>
/// Coordinates application-level view transitions across workspace sections.
/// </summary>
public interface INavigationService
{
    NavigationTarget CurrentTarget { get; }

    void NavigateTo(NavigationTarget target);

    event EventHandler<NavigationTargetChangedEventArgs>? CurrentTargetChanged;
}

/// <summary>
/// Headless default implementation of the navigation service for testing and view model coordination.
/// </summary>
public sealed class NavigationService : INavigationService
{
    private NavigationTarget _currentTarget = NavigationTarget.Accounts;

    public NavigationTarget CurrentTarget => _currentTarget;

    public event EventHandler<NavigationTargetChangedEventArgs>? CurrentTargetChanged;

    public void NavigateTo(NavigationTarget target)
    {
        if (_currentTarget == target)
        {
            return;
        }

        NavigationTarget previous = _currentTarget;
        _currentTarget = target;
        CurrentTargetChanged?.Invoke(this, new NavigationTargetChangedEventArgs(previous, target));
    }
}
