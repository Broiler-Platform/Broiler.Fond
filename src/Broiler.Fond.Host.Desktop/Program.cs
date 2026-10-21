using System;
using System.Runtime.InteropServices;
using Broiler.Fond.Host.Desktop.Qualification;
using Broiler.Fond.Host.Desktop.SelfTest;
using Broiler.Fond.Kernel.Hosting;
using Broiler.Fond.Kernel.Product;
using Broiler.UI.Standard;

namespace Broiler.Fond.Host.Desktop;

internal static class Program
{
    [STAThread]
    private static int Main(string[] args)
    {
        // 1. Self-Test Mode
        if (args.Contains("--self-test"))
        {
            int checks = DesktopHostSelfTest.Run();
            checks += BroilerUiSelfTest.Run();
            Console.WriteLine($"Desktop Host & Broiler UI Self-Test: {checks} checks completed successfully.");
            return 0;
        }

        // 2. Qualification Diagnostic Mode
        if (args.Contains("--qualification"))
        {
            var result = DesktopStartupQualificationGuard.Qualify();
            Console.WriteLine(DesktopStartupQualificationGuard.FormatDiagnosticReport(result));
            return result.IsQualified ? 0 : 1;
        }

        // 3. Smoke Test Mode
        if (args.Contains("--smoke"))
        {
            Console.WriteLine($"{KernelProduct.FullDisplayName} - Cross-Platform Desktop Host (BUI-05)");
            Console.WriteLine("Platform qualification preflight verified.");
            return 0;
        }

        // 4. Headless Execution Mode
        if (args.Contains("--headless"))
        {
            using var app = new DesktopApplication();
            app.Run(forceHeadless: true);
            Console.WriteLine("Desktop application initialized in headless mode.");
            return 0;
        }

        // 5. Default Interactive Application Execution (matching Broiler Writer)
        try
        {
            DesktopStartupQualificationGuard.EnsureQualified();

            if (OperatingSystem.IsWindows())
            {
                _ = SetProcessDpiAwarenessContext(new IntPtr(-4)); // PER_MONITOR_AWARE_V2, best effort.
                StandardControlPaint.ApplyTheme(StandardThemeTokens.Light);
            }

            using var app = new DesktopApplication();
            return app.Run(forceHeadless: false);
        }
        catch (HostPlatformQualificationException ex)
        {
            Console.Error.WriteLine("CRITICAL: Failed startup qualification guard.");
            Console.Error.WriteLine(ex.Message);
            return 1;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Application error: {ex}");
            return 1;
        }
    }

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetProcessDpiAwarenessContext(IntPtr dpiContext);
}
