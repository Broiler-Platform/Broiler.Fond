using System.Globalization;
using Broiler.Fond.Host.Desktop.Hosting;
using Broiler.Fond.Host.Desktop.Integration.Clipboard;
using Broiler.Fond.Host.Desktop.Integration.SystemTray;
using Broiler.Fond.Host.Desktop.Integration.Theme;
using Broiler.Fond.Host.Desktop.Qualification;
using Broiler.Fond.Host.Desktop.Windowing;
using Broiler.Fond.Host.Desktop.Windowing.Headless;
using Broiler.Fond.Host.Desktop.Windowing.Linux;
using Broiler.Fond.Host.Desktop.Windowing.Mac;
using Broiler.Fond.Host.Desktop.Windowing.Windows;
using Broiler.Fond.Kernel.Hosting;
using Broiler.Fond.Presentation.Services;
using Broiler.Fond.Presentation.Sessions;
using Broiler.Fond.Presentation.Views;

namespace Broiler.Fond.Host.Desktop.SelfTest;

/// <summary>
/// Comprehensive headless verification engine for the cross-platform desktop application host (BUI-05).
/// Verifies platform qualification guards, windowing adapters, OS integration seams, system tray,
/// theme synchronization, ephemeral clipboard wiping, and workspace hosting.
/// </summary>
public static class DesktopHostSelfTest
{
    public static int Run()
    {
        Console.WriteLine("Running Broiler Fond Desktop Host Self-Test & Platform Qualification (BUI-05)...");
        int checks = 0;

        void Assert(bool condition, string message)
        {
            if (!condition)
            {
                throw new InvalidOperationException($"Desktop Host Self-Test check failed: {message}");
            }
            checks++;
        }

        // -------------------------------------------------------------------------
        // 1. Startup Qualification Guard
        // -------------------------------------------------------------------------
        Console.WriteLine("  1. Verifying Desktop Startup Qualification Guard...");
        var qualResult = DesktopStartupQualificationGuard.Qualify();
        Assert(qualResult.IsQualified, "Current host platform passes qualification guard.");
        Assert(qualResult.Violations.Count == 0, "Qualification reported zero violations.");
        Assert(qualResult.ChecksPassed.Count >= 3, "Passed all 3 qualification suites (AesGcm, Unicode FormC, Invariant Culture).");

        DesktopStartupQualificationGuard.EnsureQualified();
        Assert(true, "EnsureQualified() executes without throwing on qualified host.");

        DesktopStartupQualificationGuard.AssertProfileStorePreflight("C:\\Valid\\Profile\\Path");
        Assert(true, "AssertProfileStorePreflight() accepts valid profile path.");

        bool threwOnEmptyPath = false;
        try
        {
            DesktopStartupQualificationGuard.AssertProfileStorePreflight("");
        }
        catch (ArgumentException)
        {
            threwOnEmptyPath = true;
        }
        Assert(threwOnEmptyPath, "AssertProfileStorePreflight() rejects empty profile path.");

        string report = DesktopStartupQualificationGuard.FormatDiagnosticReport(qualResult);
        Assert(report.Contains("QUALIFIED [PASS]"), "Diagnostic report indicates QUALIFIED.");
        Assert(report.Contains(qualResult.PlatformDescription), "Diagnostic report contains platform description.");

        foreach (var assembly in new[] { typeof(HostSession).Assembly, typeof(HostPlatformQualification).Assembly, typeof(DesktopHostSelfTest).Assembly })
        {
            Assert(!assembly.GetManifestResourceNames().Any(name => name.Contains("bank-responses", StringComparison.OrdinalIgnoreCase) ||
                name.Contains("Fixtures", StringComparison.OrdinalIgnoreCase)), "Production assemblies do not embed scripted banking fixtures.");
        }

        // -------------------------------------------------------------------------
        // 2. Windows DXGI Presentation Surface Adapter
        // -------------------------------------------------------------------------
        Console.WriteLine("  2. Verifying Windows DXGI Presentation Surface Adapter...");
        using (var winSurface = new WindowsDxgiPresentationSurface())
        {
            Assert(winSurface.BackendType == DesktopBackendType.WindowsDxgi, "Surface reports WindowsDxgi backend.");
            Assert(winSurface.SwapchainFormat == "DXGI_FORMAT_B8G8R8A8_UNORM", "Uses 32-bit BGRA format for DirectComposition.");
            Assert(winSurface.BufferCount == 2, "Double buffering configured.");

            winSurface.Initialize(1280, 800, "Broiler Fond — Windows");
            Assert(winSurface.Width == 1280 && winSurface.Height == 800, "Initial dimensions configured.");

            winSurface.ConfigureDwmAttributes(isDarkMode: true);
            Assert(winSurface.IsDwmDarkModeEnabled, "DWM immersive dark mode enabled.");

            winSurface.Show();
            Assert(winSurface.IsVisible, "Window surface is visible after Show().");
            Assert(winSurface.WindowState == DesktopWindowState.Normal, "Window state is Normal.");

            winSurface.SetDpiScale(1.5);
            Assert(Math.Abs(winSurface.DpiScale - 1.5) < 0.001, "DPI scale updated to 1.5 (144 DPI).");

            var ctx = winSurface.BeginDraw();
            Assert(ctx != null, "Acquired drawing context.");
            winSurface.PresentFrame();
            Assert(winSurface.FrameCount == 1, "Frame count incremented on PresentFrame().");

            winSurface.Minimize();
            Assert(winSurface.WindowState == DesktopWindowState.Minimized, "Window minimized.");

            winSurface.Restore();
            Assert(winSurface.WindowState == DesktopWindowState.Normal, "Window restored.");
        }

        // -------------------------------------------------------------------------
        // 3. Linux Wayland and X11 Fallback Surfaces
        // -------------------------------------------------------------------------
        Console.WriteLine("  3. Verifying Linux Wayland & X11 Fallback Presentation Surfaces...");
        using (var waylandSurface = new LinuxWaylandPresentationSurface())
        {
            Assert(waylandSurface.BackendType == DesktopBackendType.LinuxWayland, "Reports LinuxWayland backend.");
            Assert(waylandSurface.SurfaceRole == "xdg_toplevel", "Surface role is xdg_toplevel.");
            Assert(waylandSurface.IsFractionalScalingSupported, "Fractional scaling supported.");

            waylandSurface.Initialize(1024, 768, "Broiler Fond — Linux Wayland");
            Assert(waylandSurface.ProtocolState == "Mapped", "Protocol state mapped upon initialization.");

            waylandSurface.Show();
            _ = waylandSurface.BeginDraw();
            waylandSurface.PresentFrame();
            Assert(waylandSurface.ProtocolState == "Committed", "Wayland commit cycle executed on PresentFrame().");
            Assert(waylandSurface.FrameCount == 1, "Frame count incremented.");
        }

        using (var x11Surface = new LinuxX11FallbackPresentationSurface())
        {
            Assert(x11Surface.BackendType == DesktopBackendType.LinuxX11Fallback, "Reports LinuxX11Fallback backend.");
            Assert(x11Surface.IsMitShmEnabled, "MIT-SHM shared memory enabled.");

            x11Surface.Initialize(1024, 768, "Broiler Fond — Linux X11");
            x11Surface.Show();
            _ = x11Surface.BeginDraw();
            x11Surface.PresentFrame();
            Assert(x11Surface.FrameCount == 1, "Frame count incremented for X11 surface.");
        }

        // -------------------------------------------------------------------------
        // 4. macOS Metal Cocoa Window Surface
        // -------------------------------------------------------------------------
        Console.WriteLine("  4. Verifying macOS Metal Cocoa Window Surface...");
        using (var macSurface = new MacMetalPresentationSurface())
        {
            Assert(macSurface.BackendType == DesktopBackendType.MacMetalCocoa, "Reports MacMetalCocoa backend.");
            Assert(macSurface.MetalPixelFormat == "MTLPixelFormatBGRA8Unorm", "Metal BGRA8 pixel format.");

            macSurface.Initialize(1280, 800, "Broiler Fond — macOS Metal");
            Assert(Math.Abs(macSurface.BackingScaleFactor - 2.0) < 0.001, "Retina 2.0x backing store scale factor configured.");

            macSurface.Show();
            _ = macSurface.BeginDraw();
            macSurface.PresentFrame();
            Assert(macSurface.FrameCount == 1, "Frame count incremented for Metal surface.");
        }

        // -------------------------------------------------------------------------
        // 5. Headless Offscreen Presentation Surface & Factory
        // -------------------------------------------------------------------------
        Console.WriteLine("  5. Verifying Headless Offscreen Surface & Adapter Factory...");
        using (var headlessSurface = new HeadlessOffscreenPresentationSurface())
        {
            Assert(headlessSurface.BackendType == DesktopBackendType.HeadlessOffscreen, "Reports HeadlessOffscreen backend.");
            headlessSurface.Initialize(800, 600, "Headless Test");
            headlessSurface.Show();
            _ = headlessSurface.BeginDraw();
            headlessSurface.PresentFrame();
            Assert(headlessSurface.FrameCount == 1, "Frame count incremented.");
            Assert(headlessSurface.LastPresentedFrameTimestamp.HasValue, "Recorded frame presentation timestamp.");
        }

        using (var factoryHeadless = DesktopPlatformAdapterFactory.CreateSurface(forceHeadless: true))
        {
            Assert(factoryHeadless.BackendType == DesktopBackendType.HeadlessOffscreen, "Factory creates headless surface when forced.");
        }

        using (var nativeSurface = DesktopPlatformAdapterFactory.CreateSurface(forceHeadless: false))
        {
            Assert(nativeSurface != null, "Factory creates native presentation surface for host OS.");
        }

        // -------------------------------------------------------------------------
        // 6. Desktop System Tray & Minimize-to-Tray
        // -------------------------------------------------------------------------
        Console.WriteLine("  6. Verifying Desktop System Tray & Minimize-to-Tray...");
        using (var tray = new DesktopSystemTray())
        {
            Assert(tray.IsSupported, "System tray supported on desktop platforms.");
            Assert(!tray.IsVisible, "Tray icon initially hidden.");

            tray.ShowTrayIcon("Broiler Fond [Active]");
            Assert(tray.IsVisible, "Tray icon visible after ShowTrayIcon().");
            Assert(tray.Tooltip == "Broiler Fond [Active]", "Tooltip stored.");

            bool menuTriggered = false;
            tray.AddMenuItem("test_open", "Open Broiler Fond", () => menuTriggered = true, isDefault: true);
            Assert(tray.MenuItems.Count == 1, "Menu item added.");

            tray.TriggerMenuItem("test_open");
            Assert(menuTriggered, "Menu item click handler executed.");

            // Test minimize to tray and restore with DesktopWindow
            using var testWindow = new DesktopWindow(new HeadlessOffscreenPresentationSurface());
            testWindow.Show();
            Assert(testWindow.IsVisible, "Test window is visible.");

            tray.MinimizeToTray(testWindow);
            Assert(!testWindow.IsVisible, "Window hidden after MinimizeToTray.");

            tray.RestoreFromTray(testWindow);
            Assert(testWindow.IsVisible, "Window restored and visible after RestoreFromTray.");

            tray.HideTrayIcon();
            Assert(!tray.IsVisible, "Tray icon hidden after HideTrayIcon().");
        }

        // -------------------------------------------------------------------------
        // 7. OS Theme Change Listener
        // -------------------------------------------------------------------------
        Console.WriteLine("  7. Verifying OS Theme Change Listener...");
        using (var themeListener = new DesktopThemeListener())
        {
            Assert(themeListener.CurrentPalette != null, "Active palette initialized.");
            Assert(themeListener.CurrentPalette!.ValidateWcagContrast(), "Active palette meets WCAG 2.1 AA contrast requirements.");

            bool themeEventFired = false;
            ThemeMode observedMode = ThemeMode.StandardLight;
            themeListener.ThemeChanged += (s, mode) =>
            {
                themeEventFired = true;
                observedMode = mode;
            };

            themeListener.SetTheme(ThemeMode.StandardDark);
            Assert(themeEventFired, "ThemeChanged event fired upon SetTheme.");
            Assert(observedMode == ThemeMode.StandardDark, "Observed mode is StandardDark.");
            Assert(themeListener.CurrentThemeMode == ThemeMode.StandardDark, "CurrentThemeMode is StandardDark.");
            Assert(themeListener.CurrentPalette.ValidateWcagContrast(), "Dark palette meets WCAG 2.1 AA contrast.");

            themeListener.SetTheme(ThemeMode.HighContrastBlack);
            Assert(themeListener.IsHighContrastActive, "High-contrast active for HighContrastBlack.");

            themeListener.StartListening();
            Assert(themeListener.IsListening, "Theme listener is monitoring background changes.");
            themeListener.StopListening();
            Assert(!themeListener.IsListening, "Theme listener stopped.");
        }

        // -------------------------------------------------------------------------
        // 8. Secure Transient Clipboard Integration (ADR 0130)
        // -------------------------------------------------------------------------
        Console.WriteLine("  8. Verifying Secure Clipboard Timed Wipe...");
        using (var clipboard = new DesktopClipboardService())
        {
            string secretIban = "DE89370400440532013000";

            // Set transient text with 40ms timeout
            var task = clipboard.SetTransientTextAsync(secretIban, TimeSpan.FromMilliseconds(40));
            task.AsTask().GetAwaiter().GetResult();

            string? retrieved = clipboard.GetTextAsync().AsTask().GetAwaiter().GetResult();
            Assert(retrieved == secretIban, "Clipboard text immediately accessible after copy.");
            Assert(clipboard.TransientSetCount == 1, "TransientSetCount incremented.");

            // Wait for automated timed wipe
            Thread.Sleep(120);

            string? wiped = clipboard.GetTextAsync().AsTask().GetAwaiter().GetResult();
            Assert(wiped == null, "Clipboard text automatically wiped after timeout expiry (ADR 0130).");
            Assert(clipboard.TimedWipeCount >= 1, "TimedWipeCount incremented.");

            // Manual clear test
            clipboard.SetTransientTextAsync("TemporaryRef", TimeSpan.FromMinutes(5)).AsTask().GetAwaiter().GetResult();
            clipboard.ClearAsync().AsTask().GetAwaiter().GetResult();
            string? cleared = clipboard.GetTextAsync().AsTask().GetAwaiter().GetResult();
            Assert(cleared == null, "Manual ClearAsync zeroes clipboard immediately.");
        }

        // -------------------------------------------------------------------------
        // 9. Desktop Platform Seam & Safe Mode
        // -------------------------------------------------------------------------
        Console.WriteLine("  9. Verifying Desktop Platform Seam & Safe Mode...");
        var seam = new DesktopPlatformSeam();
        Assert(seam.HostIdentifier == "Broiler.Fond.Host.Desktop (1.0)", "Host identifier matches desktop host.");
        Assert(seam.PlatformType != HostPlatformType.Unknown, "Platform type detected.");
        Assert(!seam.IsSafeModeActive, "Safe Mode initially inactive.");

        bool safeModeFired = false;
        string? safeModeReason = null;
        seam.SafeModeChanged += (s, e) =>
        {
            safeModeFired = true;
            safeModeReason = e.Reason;
        };

        seam.EnterSafeMode("Reconciliation integrity alert");
        Assert(seam.IsSafeModeActive, "Safe Mode is active after EnterSafeMode.");
        Assert(safeModeFired, "SafeModeChanged event raised.");
        Assert(safeModeReason == "Reconciliation integrity alert", "SafeModeChanged reason preserved.");

        safeModeFired = false;
        seam.ExitSafeMode();
        Assert(!seam.IsSafeModeActive, "Safe Mode inactive after ExitSafeMode.");
        Assert(safeModeFired, "SafeModeChanged event raised on exit.");

        seam.Notifications.Notify(HostNotificationLevel.SecurityAlert, "Security Check", "Host qualification verified.");
        Assert(seam.Notifications.DispatchedNotifications.Count == 1, "Notification dispatched and recorded.");
        Assert(seam.Notifications.DispatchedNotifications[0].Level == HostNotificationLevel.SecurityAlert, "Level preserved.");

        // -------------------------------------------------------------------------
        // 10. DesktopWindow Hosting BroilerMainWindow & Frame Pipeline
        // -------------------------------------------------------------------------
        Console.WriteLine("  10. Verifying DesktopWindow Workspace Layout & Frame Pipeline...");
        using (var session = new HostSession())
        {
            var dialogService = new BroilerDialogService();
            var mainWindow = new BroilerMainWindow(session, dialogService);

            using var hostSurface = new HeadlessOffscreenPresentationSurface();
            using var desktopWindow = new DesktopWindow(hostSurface)
            {
                Content = mainWindow
            };

            desktopWindow.Show();
            Assert(hostSurface.IsVisible, "Host surface is visible after window Show().");
            Assert(hostSurface.FrameCount >= 1, "Layout and frame presented to surface.");

            // Resize test
            hostSurface.Resize(1600, 900);
            Assert(hostSurface.Width == 1600 && hostSurface.Height == 900, "Window surface resized.");
            Assert(hostSurface.FrameCount >= 2, "Re-measured, re-arranged, and presented frame on resize.");

            // Keyboard navigation dispatch
            bool tabHandled = desktopWindow.DispatchKeyDown(new KeyEventArgs(Key.Tab));
            Assert(tabHandled, "Tab key handled by window focus manager.");
            Assert(desktopWindow.FocusManager.FocusedControl != null, "Keyboard focus assigned to interactive control.");

            // Minimize to tray on close
            desktopWindow.MinimizeToTrayOnClose = true;
            desktopWindow.Close();
            Assert(desktopWindow.WindowState == DesktopWindowState.Minimized, "Window minimized instead of closed when MinimizeToTrayOnClose is true.");
        }

        Console.WriteLine($"Broiler Fond Desktop Host Self-Test: {checks} checks completed successfully.");
        return checks;
    }
}
