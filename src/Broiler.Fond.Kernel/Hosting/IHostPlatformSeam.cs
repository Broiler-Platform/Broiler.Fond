namespace Broiler.Fond.Kernel.Hosting;

/// <summary>
/// Event arguments describing a change in host Safe Mode state.
/// </summary>
public sealed class SafeModeChangedEventArgs(bool isActive, string? reason, DateTimeOffset timestamp) : EventArgs
{
    public bool IsActive { get; } = isActive;
    public string? Reason { get; } = reason;
    public DateTimeOffset Timestamp { get; } = timestamp;
}

/// <summary>
/// Defines the root platform seam contract between the platform-independent kernel
/// and any presentation host (Windows, Linux, macOS, mobile).
/// </summary>
public interface IHostPlatformSeam
{
    /// <summary>Identifies the host execution environment for local diagnostic logs.</summary>
    string HostIdentifier { get; }

    /// <summary>Broad platform type categorization.</summary>
    HostPlatformType PlatformType { get; }

    /// <summary>Indicates whether read-only Safe Mode is active.</summary>
    bool IsSafeModeActive { get; }

    /// <summary>Activates Safe Mode with a mandatory audit reason.</summary>
    void EnterSafeMode(string reason);

    /// <summary>Deactivates Safe Mode and restores normal read-write operation.</summary>
    void ExitSafeMode();

    /// <summary>Raised whenever Safe Mode state changes.</summary>
    event EventHandler<SafeModeChangedEventArgs>? SafeModeChanged;

    /// <summary>Gets the notification dispatch channel.</summary>
    IHostNotificationChannel Notifications { get; }

    /// <summary>Gets the interactive credential prompt abstraction.</summary>
    IHostCredentialPrompt CredentialPrompt { get; }

    /// <summary>Gets the transient clipboard service.</summary>
    IHostClipboardService Clipboard { get; }
}
