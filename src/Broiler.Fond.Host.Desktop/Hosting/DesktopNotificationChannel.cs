using Broiler.Fond.Kernel.Hosting;

namespace Broiler.Fond.Host.Desktop.Hosting;

/// <summary>
/// Record of a user notification dispatched via the host seam.
/// </summary>
public sealed record DesktopNotificationEntry(
    HostNotificationLevel Level,
    string Title,
    string Message,
    DateTimeOffset Timestamp);

/// <summary>
/// Desktop notification dispatch channel delivering local alerts.
/// Invariant: Pure local dispatch; zero telemetry, analytics, or remote network transmission.
/// </summary>
public sealed class DesktopNotificationChannel : IHostNotificationChannel
{
    private readonly List<DesktopNotificationEntry> _notifications = [];
    private readonly object _syncLock = new();

    public IReadOnlyList<DesktopNotificationEntry> DispatchedNotifications
    {
        get
        {
            lock (_syncLock)
            {
                return _notifications.ToList().AsReadOnly();
            }
        }
    }

    public event EventHandler<DesktopNotificationEntry>? NotificationDispatched;

    public void Notify(HostNotificationLevel level, string title, string message)
    {
        ArgumentNullException.ThrowIfNull(title);
        ArgumentNullException.ThrowIfNull(message);

        var entry = new DesktopNotificationEntry(level, title, message, DateTimeOffset.UtcNow);

        lock (_syncLock)
        {
            _notifications.Add(entry);
        }

        NotificationDispatched?.Invoke(this, entry);
    }

    public void Clear()
    {
        lock (_syncLock)
        {
            _notifications.Clear();
        }
    }
}
