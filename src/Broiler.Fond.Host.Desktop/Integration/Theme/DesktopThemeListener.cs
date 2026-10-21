using System.Runtime.InteropServices;
using Broiler.UI.Standard;

namespace Broiler.Fond.Host.Desktop.Integration.Theme;

/// <summary>
/// Monitors and synchronizes operating system theme changes (Light, Dark, High-Contrast)
/// with Broiler UI's design tokens and <see cref="ThemePalette"/>.
/// Invariant: Pure BCL implementation; zero third-party packages.
/// </summary>
public sealed class DesktopThemeListener : IDesktopThemeListener
{
    private static readonly IntPtr HkeyCurrentUser = unchecked((IntPtr)(long)(int)0x80000001);
    private const uint RrfRtRegDword = 0x00000010;

    private readonly object _syncLock = new();
    private Timer? _pollingTimer;
    private bool _isDisposed;

    public ThemeMode CurrentThemeMode { get; private set; } = ThemeMode.StandardLight;
    public ThemePalette CurrentPalette => ThemePalette.Get(CurrentThemeMode);
    public bool IsHighContrastActive => CurrentThemeMode is ThemeMode.HighContrastBlack or ThemeMode.HighContrastWhite;
    public bool IsListening => _pollingTimer != null;

    public event EventHandler<ThemeMode>? ThemeChanged;

    public DesktopThemeListener()
    {
        DetectSystemTheme();
    }

    /// <summary>
    /// Explicitly sets the active theme mode and notifies listeners.
    /// </summary>
    public void SetTheme(ThemeMode mode)
    {
        ObjectDisposedException.ThrowIf(_isDisposed, this);

        lock (_syncLock)
        {
            if (CurrentThemeMode != mode)
            {
                CurrentThemeMode = mode;
                StandardControlPaint.ApplyTheme(mode == ThemeMode.StandardDark ? StandardThemeTokens.Dark : StandardThemeTokens.Light);
                ThemeChanged?.Invoke(this, mode);
            }
        }
    }

    /// <summary>
    /// Queries the host operating system to detect whether Light, Dark, or High-Contrast mode is configured.
    /// </summary>
    public void DetectSystemTheme()
    {
        ObjectDisposedException.ThrowIf(_isDisposed, this);

        ThemeMode detected = DetectFromHostEnvironment();
        SetTheme(detected);
    }

    /// <summary>
    /// Starts background polling to monitor theme modifications by the user or OS.
    /// </summary>
    public void StartListening()
    {
        ObjectDisposedException.ThrowIf(_isDisposed, this);

        lock (_syncLock)
        {
            if (_pollingTimer == null)
            {
                // Poll every 1000 ms for theme changes
                _pollingTimer = new Timer(OnPollTimer, null, TimeSpan.FromMilliseconds(1000), TimeSpan.FromMilliseconds(1000));
            }
        }
    }

    /// <summary>
    /// Stops background theme monitoring.
    /// </summary>
    public void StopListening()
    {
        lock (_syncLock)
        {
            _pollingTimer?.Dispose();
            _pollingTimer = null;
        }
    }

    private void OnPollTimer(object? state)
    {
        if (_isDisposed)
        {
            return;
        }

        try
        {
            ThemeMode detected = DetectFromHostEnvironment();
            lock (_syncLock)
            {
                if (CurrentThemeMode != detected)
                {
                    CurrentThemeMode = detected;
                    ThemeChanged?.Invoke(this, detected);
                }
            }
        }
        catch
        {
            // Ignore transient OS query errors during background poll
        }
    }

    private static ThemeMode DetectFromHostEnvironment()
    {
        // 1. Explicit environment override for testing and headless runs
        string? envTheme = Environment.GetEnvironmentVariable("BROILER_THEME");
        if (!string.IsNullOrWhiteSpace(envTheme))
        {
            if (string.Equals(envTheme, "dark", StringComparison.OrdinalIgnoreCase))
            {
                return ThemeMode.StandardDark;
            }
            if (string.Equals(envTheme, "hc-black", StringComparison.OrdinalIgnoreCase))
            {
                return ThemeMode.HighContrastBlack;
            }
            if (string.Equals(envTheme, "hc-white", StringComparison.OrdinalIgnoreCase))
            {
                return ThemeMode.HighContrastWhite;
            }
            if (string.Equals(envTheme, "light", StringComparison.OrdinalIgnoreCase))
            {
                return ThemeMode.StandardLight;
            }
        }

        // 2. Windows OS detection
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            return DetectWindowsTheme();
        }

        // 3. Linux OS detection
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
        {
            return DetectLinuxTheme();
        }

        // 4. macOS detection
        if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
        {
            return DetectMacOsTheme();
        }

        return ThemeMode.StandardLight;
    }

    private static ThemeMode DetectWindowsTheme()
    {
        try
        {
            // Query Personalize\AppsUseLightTheme: 0 = Dark, 1 = Light
            int data = 1;
            uint pcbData = 4;
            int status = RegGetValue(
                HkeyCurrentUser,
                @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize",
                "AppsUseLightTheme",
                RrfRtRegDword,
                out _,
                out data,
                ref pcbData);

            if (status == 0 && data == 0)
            {
                return ThemeMode.StandardDark;
            }
        }
        catch
        {
            // Fall through to light mode if registry query is restricted
        }

        return ThemeMode.StandardLight;
    }

    private static ThemeMode DetectLinuxTheme()
    {
        string? gtkTheme = Environment.GetEnvironmentVariable("GTK_THEME");
        if (!string.IsNullOrWhiteSpace(gtkTheme) && gtkTheme.Contains("dark", StringComparison.OrdinalIgnoreCase))
        {
            return ThemeMode.StandardDark;
        }

        string? colorScheme = Environment.GetEnvironmentVariable("COLOR_SCHEME");
        if (string.Equals(colorScheme, "dark", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(colorScheme, "prefer-dark", StringComparison.OrdinalIgnoreCase))
        {
            return ThemeMode.StandardDark;
        }

        return ThemeMode.StandardLight;
    }

    private static ThemeMode DetectMacOsTheme()
    {
        string? appleInterfaceStyle = Environment.GetEnvironmentVariable("AppleInterfaceStyle");
        if (string.Equals(appleInterfaceStyle, "Dark", StringComparison.OrdinalIgnoreCase))
        {
            return ThemeMode.StandardDark;
        }

        return ThemeMode.StandardLight;
    }

    [DllImport("advapi32.dll", EntryPoint = "RegGetValueW", CharSet = CharSet.Unicode, SetLastError = false)]
    private static extern int RegGetValue(
        IntPtr hkey,
        string subKey,
        string value,
        uint flags,
        out uint type,
        out int data,
        ref uint pcbData);

    public void Dispose()
    {
        if (!_isDisposed)
        {
            _isDisposed = true;
            StopListening();
        }
    }
}
