using Broiler.Fond.Host.Windows.SelfTest;
using Broiler.Fond.Host.Windows.Views;
using Broiler.Fond.Kernel.Product;

namespace Broiler.Fond.Host.Windows;

internal static class Program
{
    [STAThread]
    private static int Main(string[] args)
    {
        if (args.Contains("--ui") || (Environment.UserInteractive && !Console.IsOutputRedirected && !args.Contains("--smoke") && !args.Contains("--self-test")))
        {
            ApplicationConfiguration.Initialize();
            Application.Run(new MainForm());
            return 0;
        }

        // Headless / Smoke / Self-Test mode
        int checks = HostSelfTest.Run();
        if (args.Contains("--self-test"))
        {
            Console.WriteLine($"Host Self-Test & BFSG Accessibility: {checks} checks completed successfully.");
        }
        else
        {
            Console.WriteLine($"{KernelProduct.FullDisplayName} - Milestone {KernelProduct.Milestone} development shell");
            Console.WriteLine("Banking, storage, credential, UI, and network operations are not implemented.");
        }
        return 0;
    }
}
