namespace Broiler.Fond.Kernel.Hosting;

/// <summary>
/// Lifecycle execution states for mobile and sandboxed hosts.
/// </summary>
public enum HostLifecycleState
{
    /// <summary>App is in the active foreground and receiving user interaction.</summary>
    ForegroundActive = 1,

    /// <summary>App is paused, partially obscured, or entering the task switcher. Privacy curtain should engage.</summary>
    ForegroundPaused = 2,

    /// <summary>App is backgrounded or suspended. Volatile credentials must be scrubbed immediately.</summary>
    BackgroundSuspended = 3,

    /// <summary>App process is terminating.</summary>
    Terminating = 4
}

/// <summary>
/// Event arguments for a lifecycle state transition.
/// </summary>
public sealed class HostLifecycleStateChangedEventArgs(
    HostLifecycleState previousState,
    HostLifecycleState newState,
    DateTimeOffset timestamp) : EventArgs
{
    public HostLifecycleState PreviousState { get; } = previousState;
    public HostLifecycleState NewState { get; } = newState;
    public DateTimeOffset Timestamp { get; } = timestamp;
}
