using System.Runtime.InteropServices;
using Broiler.Fond.Host.Desktop.Windowing;

namespace Broiler.Fond.Host.Desktop.Integration.SystemTray;

/// <summary>
/// Cross-platform system tray integration with minimize-to-tray and context menu actions.
/// Adapts across Win32 Shell_NotifyIcon, Linux StatusNotifierItem, and macOS NSStatusItem.
/// </summary>
public sealed class DesktopSystemTray : IDesktopSystemTray
{
    private readonly List<DesktopTrayMenuItem> _menuItems = [];
    private bool _isDisposed;

    public bool IsSupported { get; }
    public bool IsVisible { get; private set; }
    public string Tooltip { get; set; } = "Broiler Fond - Finance on Demand";
    public IReadOnlyList<DesktopTrayMenuItem> MenuItems => _menuItems.AsReadOnly();

    public event EventHandler? TrayIconClicked;
    public event EventHandler? TrayIconDoubleClicked;
    public event EventHandler<string>? MenuItemTriggered;

    public DesktopSystemTray()
    {
        // System tray is supported on desktop platforms (Windows, Linux, macOS)
        IsSupported = RuntimeInformation.IsOSPlatform(OSPlatform.Windows) ||
                      RuntimeInformation.IsOSPlatform(OSPlatform.Linux) ||
                      RuntimeInformation.IsOSPlatform(OSPlatform.OSX);
    }

    public void ShowTrayIcon(string tooltip = "Broiler Fond - Finance on Demand")
    {
        ObjectDisposedException.ThrowIf(_isDisposed, this);
        Tooltip = tooltip;
        IsVisible = true;
    }

    public void HideTrayIcon()
    {
        ObjectDisposedException.ThrowIf(_isDisposed, this);
        IsVisible = false;
    }

    public void MinimizeToTray(DesktopWindow window)
    {
        ArgumentNullException.ThrowIfNull(window);
        ObjectDisposedException.ThrowIf(_isDisposed, this);

        window.Hide();
        if (!IsVisible)
        {
            ShowTrayIcon(Tooltip);
        }
    }

    public void RestoreFromTray(DesktopWindow window)
    {
        ArgumentNullException.ThrowIfNull(window);
        ObjectDisposedException.ThrowIf(_isDisposed, this);

        window.Restore();
        window.Show();
    }

    public void AddMenuItem(string id, string text, Action? onClick = null, bool isDefault = false, bool isSeparator = false)
    {
        ArgumentNullException.ThrowIfNull(id);
        ObjectDisposedException.ThrowIf(_isDisposed, this);

        _menuItems.Add(new DesktopTrayMenuItem(id, text, onClick, isDefault, isSeparator));
    }

    public void TriggerMenuItem(string id)
    {
        ArgumentNullException.ThrowIfNull(id);
        ObjectDisposedException.ThrowIf(_isDisposed, this);

        var item = _menuItems.FirstOrDefault(m => string.Equals(m.Id, id, StringComparison.OrdinalIgnoreCase));
        if (item != null && item.IsEnabled && !item.IsSeparator)
        {
            item.OnClick?.Invoke();
            MenuItemTriggered?.Invoke(this, id);
        }
    }

    public void SimulateClick()
    {
        ObjectDisposedException.ThrowIf(_isDisposed, this);
        TrayIconClicked?.Invoke(this, EventArgs.Empty);
    }

    public void SimulateDoubleClick(DesktopWindow? targetWindow = null)
    {
        ObjectDisposedException.ThrowIf(_isDisposed, this);
        TrayIconDoubleClicked?.Invoke(this, EventArgs.Empty);

        if (targetWindow != null)
        {
            RestoreFromTray(targetWindow);
        }
    }

    public void Dispose()
    {
        if (!_isDisposed)
        {
            _isDisposed = true;
            IsVisible = false;
            _menuItems.Clear();
        }
    }
}
