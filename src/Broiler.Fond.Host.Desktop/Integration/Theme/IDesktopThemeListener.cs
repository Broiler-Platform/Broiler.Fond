namespace Broiler.Fond.Host.Desktop.Integration.Theme;

using System;

/// <summary>
/// Operational theme modes.
/// </summary>
public enum ThemeMode
{
    StandardLight,
    StandardDark,
    HighContrastBlack,
    HighContrastWhite
}

/// <summary>
/// Contract for OS theme synchronization with Broiler UI design tokens.
/// Synchronizes light, dark, and high-contrast modes with host operating system settings.
/// </summary>
public interface IDesktopThemeListener : IDisposable
{
    /// <summary>Current active theme mode.</summary>
    ThemeMode CurrentThemeMode { get; }

    /// <summary>Current semantic theme palette.</summary>
    ThemePalette CurrentPalette { get; }

    /// <summary>Indicates whether accessibility high-contrast mode is currently active.</summary>
    bool IsHighContrastActive { get; }

    /// <summary>Indicates whether background monitoring of OS theme changes is running.</summary>
    bool IsListening { get; }

    /// <summary>Raised when the system or application theme changes.</summary>
    event EventHandler<ThemeMode>? ThemeChanged;

    /// <summary>Queries the host OS and updates the active theme mode.</summary>
    void DetectSystemTheme();

    /// <summary>Explicitly sets the active theme mode.</summary>
    void SetTheme(ThemeMode mode);

    /// <summary>Starts background monitoring of OS theme changes.</summary>
    void StartListening();

    /// <summary>Stops background monitoring.</summary>
    void StopListening();
}
