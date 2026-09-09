using Broiler.Fond.Kernel.Product;

namespace Broiler.Fond.Host.Windows;

internal static class Program
{
    [STAThread]
    private static int Main()
    {
        Console.WriteLine($"{KernelProduct.FullDisplayName} - Milestone {KernelProduct.Milestone} development shell");
        Console.WriteLine("Banking, storage, credential, UI, and network operations are not implemented.");
        return 0;
    }
}
