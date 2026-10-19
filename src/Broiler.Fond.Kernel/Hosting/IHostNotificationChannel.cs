namespace Broiler.Fond.Kernel.Hosting;

/// <summary>
/// Severity level of host-surfaced notifications.
/// </summary>
public enum HostNotificationLevel
{
    Info = 0,
    Warning = 1,
    Error = 2,
    SecurityAlert = 3
}

/// <summary>
/// Platform-neutral notification dispatch channel for user alerts.
/// Invariant: Notifications never transmit telemetry; all alerts are local.
/// </summary>
public interface IHostNotificationChannel
{
    /// <summary>
    /// Dispatches a user-visible notification on the host platform.
    /// </summary>
    void Notify(HostNotificationLevel level, string title, string message);
}
