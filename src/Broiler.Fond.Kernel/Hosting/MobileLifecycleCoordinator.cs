namespace Broiler.Fond.Kernel.Hosting;

/// <summary>
/// Coordinates mobile and background lifecycle transitions, memory scrubbing,
/// privacy curtains, and decoupled SCA timeout enforcement.
/// Invariant: Scrubbing wipes all registered volatile secrets upon suspension without exception.
/// </summary>
public sealed class MobileLifecycleCoordinator
{
    private readonly object _syncRoot = new();
    private readonly List<Action> _volatileSecretScrubbers = [];
    private readonly IHostClipboardService? _clipboardService;

    public HostLifecycleState CurrentState { get; private set; } = HostLifecycleState.ForegroundActive;
    public bool IsPrivacyCurtainActive { get; private set; }
    public bool IsDecoupledScaActive { get; private set; }
    public DateTimeOffset? DecoupledScaDeadline { get; private set; }
    public bool DidDecoupledScaExpire { get; private set; }

    public event EventHandler<HostLifecycleStateChangedEventArgs>? StateChanged;
    public event EventHandler<bool>? PrivacyCurtainChanged;

    public MobileLifecycleCoordinator(IHostClipboardService? clipboardService = null)
    {
        _clipboardService = clipboardService;
    }

    /// <summary>
    /// Registers a cleanup callback that will be invoked immediately when the application is suspended or terminated.
    /// </summary>
    public void RegisterVolatileSecret(Action wipeAction)
    {
        ArgumentNullException.ThrowIfNull(wipeAction);
        lock (_syncRoot)
        {
            _volatileSecretScrubbers.Add(wipeAction);
        }
    }

    /// <summary>
    /// Initiates a monitored decoupled SCA approval window (e.g., pushTAN app approval).
    /// </summary>
    public void BeginDecoupledScaWindow(TimeSpan timeout, DateTimeOffset? now = null)
    {
        lock (_syncRoot)
        {
            IsDecoupledScaActive = true;
            DidDecoupledScaExpire = false;
            DecoupledScaDeadline = (now ?? DateTimeOffset.UtcNow).Add(timeout);
        }
    }

    /// <summary>
    /// Terminates the decoupled SCA monitoring window upon successful authentication or cancellation.
    /// </summary>
    public void EndDecoupledScaWindow()
    {
        lock (_syncRoot)
        {
            IsDecoupledScaActive = false;
            DecoupledScaDeadline = null;
            DidDecoupledScaExpire = false;
        }
    }

    /// <summary>
    /// Transitions the coordinator to a new lifecycle state, executing all associated security actions.
    /// </summary>
    public void TransitionTo(HostLifecycleState newState, DateTimeOffset? now = null)
    {
        DateTimeOffset timestamp = now ?? DateTimeOffset.UtcNow;
        HostLifecycleState previousState;
        List<Action>? scrubbersToExecute = null;
        bool shouldScrubClipboard = false;
        bool curtainChanged = false;
        bool newCurtainState = false;

        lock (_syncRoot)
        {
            if (CurrentState == newState)
            {
                return;
            }

            previousState = CurrentState;
            CurrentState = newState;

            switch (newState)
            {
                case HostLifecycleState.ForegroundPaused:
                    if (!IsPrivacyCurtainActive)
                    {
                        IsPrivacyCurtainActive = true;
                        curtainChanged = true;
                        newCurtainState = true;
                    }
                    break;

                case HostLifecycleState.BackgroundSuspended:
                    if (!IsPrivacyCurtainActive)
                    {
                        IsPrivacyCurtainActive = true;
                        curtainChanged = true;
                        newCurtainState = true;
                    }

                    // Collect and clear secret scrubbers
                    scrubbersToExecute = new List<Action>(_volatileSecretScrubbers);
                    _volatileSecretScrubbers.Clear();
                    shouldScrubClipboard = true;
                    break;

                case HostLifecycleState.ForegroundActive:
                    if (IsPrivacyCurtainActive)
                    {
                        IsPrivacyCurtainActive = false;
                        curtainChanged = true;
                        newCurtainState = false;
                    }

                    // Check decoupled SCA timeout upon returning to foreground
                    if (IsDecoupledScaActive && DecoupledScaDeadline.HasValue)
                    {
                        if (timestamp > DecoupledScaDeadline.Value)
                        {
                            DidDecoupledScaExpire = true;
                        }
                    }
                    break;

                case HostLifecycleState.Terminating:
                    scrubbersToExecute = new List<Action>(_volatileSecretScrubbers);
                    _volatileSecretScrubbers.Clear();
                    shouldScrubClipboard = true;
                    break;
            }
        }

        // Execute scrubbers outside the lock to prevent re-entrancy deadlocks
        if (scrubbersToExecute is not null)
        {
            foreach (var scrub in scrubbersToExecute)
            {
                try
                {
                    scrub();
                }
                catch
                {
                    // Fail-safe: individual scrubbing failures must not prevent remaining scrubbers from executing
                }
            }
        }

        if (shouldScrubClipboard && _clipboardService is not null)
        {
            try
            {
                _ = _clipboardService.ClearAsync();
            }
            catch
            {
                // Best-effort clipboard wipe
            }
        }

        if (curtainChanged)
        {
            PrivacyCurtainChanged?.Invoke(this, newCurtainState);
        }

        StateChanged?.Invoke(this, new HostLifecycleStateChangedEventArgs(previousState, newState, timestamp));
    }
}
