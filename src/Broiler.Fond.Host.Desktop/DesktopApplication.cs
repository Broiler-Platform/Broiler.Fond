using Broiler.Fond.Host.Desktop.Hosting;
using Broiler.Fond.Host.Desktop.Integration.Clipboard;
using Broiler.Fond.Host.Desktop.Integration.SystemTray;
using Broiler.Fond.Host.Desktop.Integration.Theme;
using Broiler.Fond.Host.Desktop.Qualification;
using Broiler.Fond.Host.Desktop.Windowing;
using Broiler.Fond.Host.Desktop.Windowing.Windows;
using Broiler.Fond.Presentation.Services;
using Broiler.Fond.Presentation.Sessions;
using Broiler.Fond.Presentation.Views;

namespace Broiler.Fond.Host.Desktop;

/// <summary>
/// Main application host coordinating windowing, OS integration seams, and the banking workspace.
/// Invariant: Pure BCL implementation with zero third-party packages or telemetry.
/// </summary>
public sealed class DesktopApplication : IDisposable
{
    private readonly DesktopStartupQualificationGuardWrapper _guard = new();
    private readonly DesktopThemeListener _themeListener = new();
    private readonly DesktopClipboardService _clipboard = new();
    private readonly DesktopSystemTray _systemTray = new();
    private readonly BroilerDialogService _dialogService = new();
    private readonly DesktopPlatformSeam _seam;
    private readonly HostSession _session = new(BankProductConfiguration.FromSettings(
        Environment.GetEnvironmentVariable("BROILER_FOND_FINTS_PRODUCT_ID"),
        Environment.GetEnvironmentVariable("BROILER_FOND_FINTS_PRODUCT_VERSION")));

    private DesktopWindow? _mainWindow;
    private bool _isDisposed;

    public DesktopPlatformSeam Seam => _seam;
    public HostSession Session => _session;
    public DesktopWindow? MainWindow => _mainWindow;
    public DesktopSystemTray SystemTray => _systemTray;
    public DesktopThemeListener ThemeListener => _themeListener;
    public DesktopClipboardService Clipboard => _clipboard;

    public DesktopApplication()
    {
        // 1. Startup Platform Qualification Preflight (ADR 0130)
        DesktopStartupQualificationGuard.EnsureQualified();

        _seam = new DesktopPlatformSeam(_dialogService, _clipboard);
    }

    /// <summary>
    /// Initializes and starts the desktop application shell.
    /// </summary>
    public void Initialize(bool forceHeadless = false)
    {
        ObjectDisposedException.ThrowIf(_isDisposed, this);

        // 2. Start OS Theme Synchronization
        _themeListener.StartListening();

        // 3. Create Main Banking Workspace Shell
        var workspace = new BroilerMainWindow(_session, _dialogService);

        // 4. Create Native Desktop Window
        var surface = DesktopPlatformAdapterFactory.CreateSurface(forceHeadless);
        _mainWindow = new DesktopWindow(surface)
        {
            Content = workspace,
            Title = "Broiler Fond - Finance on Demand",
            MinimizeToTrayOnClose = true
        };

        // 5. Configure System Tray
        ConfigureSystemTray();
    }

    /// <summary>
    /// Runs the application window.
    /// On Windows desktop, executes the native Direct2D window message loop (matching Broiler Writer).
    /// In headless environments, initializes offscreen surfaces.
    /// </summary>
    public int Run(bool forceHeadless = false)
    {
        if (OperatingSystem.IsWindowsVersionAtLeast(7) && !forceHeadless)
        {
            _themeListener.StartListening();
            using var window = new FondDesktopWindow(_session, _dialogService);
            return window.Run();
        }

        Initialize(forceHeadless);
        _mainWindow?.Show();
        return 0;
    }

    private void ConfigureSystemTray()
    {
        if (!_systemTray.IsSupported)
        {
            return;
        }

        _systemTray.Tooltip = "Broiler Fond - Finance on Demand";
        _systemTray.ShowTrayIcon();

        _systemTray.AddMenuItem("open", "Open Broiler Fond", () =>
        {
            if (_mainWindow != null)
            {
                _systemTray.RestoreFromTray(_mainWindow);
            }
        }, isDefault: true);

        _systemTray.AddMenuItem("lock", "Lock Profile", () =>
        {
            _session.Lock();
        });

        _systemTray.AddMenuItem("safemode", "Toggle Safe Mode", () =>
        {
            if (_seam.IsSafeModeActive)
            {
                _seam.ExitSafeMode();
            }
            else
            {
                _seam.EnterSafeMode("User activated Safe Mode via System Tray");
            }
        });

        _systemTray.AddMenuItem("sep", "-", isSeparator: true);

        _systemTray.AddMenuItem("exit", "Exit", () =>
        {
            _session.Lock();
            _mainWindow?.Surface.Close();
        });
    }

    public void Dispose()
    {
        if (!_isDisposed)
        {
            _isDisposed = true;
            _systemTray.Dispose();
            _themeListener.Dispose();
            _clipboard.Dispose();
            _mainWindow?.Dispose();
            _session.Dispose();
        }
    }

    private sealed class DesktopStartupQualificationGuardWrapper { }
}
