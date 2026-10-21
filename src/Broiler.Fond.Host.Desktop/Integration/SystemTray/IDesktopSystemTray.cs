using Broiler.Fond.Host.Desktop.Windowing;

namespace Broiler.Fond.Host.Desktop.Integration.SystemTray;

/// <summary>
/// Menu item entry in the system tray context menu.
/// </summary>
public sealed class DesktopTrayMenuItem
{
    public string Id { get; }
    public string Text { get; set; }
    public Action? OnClick { get; }
    public bool IsDefault { get; }
    public bool IsSeparator { get; }
    public bool IsEnabled { get; set; } = true;

    public DesktopTrayMenuItem(string id, string text, Action? onClick = null, bool isDefault = false, bool isSeparator = false)
    {
        Id = id;
        Text = text;
        OnClick = onClick;
        IsDefault = isDefault;
        IsSeparator = isSeparator;
    }
}

/// <summary>
/// Platform-neutral contract for system tray icon and minimize-to-tray operations.
/// </summary>
public interface IDesktopSystemTray : IDisposable
{
    /// <summary>Indicates whether the current host platform supports system tray icons.</summary>
    bool IsSupported { get; }

    /// <summary>Indicates whether the tray icon is currently active in the notification area.</summary>
    bool IsVisible { get; }

    /// <summary>Tooltip text displayed on hovering over the tray icon.</summary>
    string Tooltip { get; set; }

    /// <summary>Items in the tray icon context menu.</summary>
    IReadOnlyList<DesktopTrayMenuItem> MenuItems { get; }

    /// <summary>Registers and displays the system tray icon.</summary>
    void ShowTrayIcon(string tooltip = "Broiler Fond - Finance on Demand");

    /// <summary>Removes the tray icon from the notification area.</summary>
    void HideTrayIcon();

    /// <summary>Minimizes the target window and hides it from the taskbar.</summary>
    void MinimizeToTray(DesktopWindow window);

    /// <summary>Restores the window from the tray to normal state.</summary>
    void RestoreFromTray(DesktopWindow window);

    /// <summary>Adds a context menu item to the tray.</summary>
    void AddMenuItem(string id, string text, Action? onClick = null, bool isDefault = false, bool isSeparator = false);

    /// <summary>Triggers a menu item by identifier programmatically.</summary>
    void TriggerMenuItem(string id);

    /// <summary>Raised when the tray icon is clicked (single click).</summary>
    event EventHandler? TrayIconClicked;

    /// <summary>Raised when the tray icon is double-clicked.</summary>
    event EventHandler? TrayIconDoubleClicked;

    /// <summary>Raised when any menu item is triggered.</summary>
    event EventHandler<string>? MenuItemTriggered;
}
