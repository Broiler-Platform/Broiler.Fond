namespace Broiler.Fond.Presentation.Tests;

internal static class Program
{
    private static readonly List<string> Failures = [];

    private static int Main()
    {
        Console.WriteLine("Running Broiler.Fond.Presentation tests...");

        int checkCount = 0;
        Action<bool, string> check = (condition, message) =>
        {
            if (!condition)
            {
                Failures.Add(message);
                Console.Error.WriteLine($"[FAIL] {message}");
            }
            else
            {
                checkCount++;
            }
        };

        try
        {
            MvvmTests.Run(check);
            Console.WriteLine("  MVVM core and command tests passed.");

            ViewModelTests.Run(check);
            Console.WriteLine("  Presentation ViewModels tests passed.");

            HostSessionTests.Run(check);
            Console.WriteLine("  HostSession headless presentation coordinator tests passed.");

            ServiceSeamTests.Run(check);
            Console.WriteLine("  Dialog, Navigation, and Clipboard service seams tests passed.");

            DialogAndViewsTests.Run(check);
            DialogLayoutTests.Run(check);
            ViewLayoutTests.Run(check);
            AccountSetupTests.RunAsync(check).GetAwaiter().GetResult();
            BankRefreshFeedbackTests.Run(check);
            BankRefreshServiceTests.RunAsync(check).GetAwaiter().GetResult();
            BankConnectionSetupTests.RunAsync(check).GetAwaiter().GetResult();
            BankSynchronizationTests.RunAsync(check).GetAwaiter().GetResult();
            BankDiscoveryTests.RunAsync(check).GetAwaiter().GetResult();
            BankBalanceTests.RunAsync(check).GetAwaiter().GetResult();
            BankBatchTests.RunAsync(check).GetAwaiter().GetResult();
            BankRefreshPresentationTests.Run(check);
            BankConnectTests.RunAsync(check).GetAwaiter().GetResult();
            BankFirstBalanceTests.RunAsync(check).GetAwaiter().GetResult();
            BankParameterAcquisitionTests.RunAsync(check).GetAwaiter().GetResult();
            BankProcedureSelectionTests.RunAsync(check).GetAwaiter().GetResult();
            BankSignInPersistenceTests.RunAsync(check).GetAwaiter().GetResult();
            Console.WriteLine("  Dialog and Views BFSG accessibility and presentation tests passed.");
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"[UNHANDLED EXCEPTION] {ex}");
            return 1;
        }

        if (Failures.Count > 0)
        {
            Console.Error.WriteLine($"\nFAILED: {Failures.Count} check(s) failed in Broiler.Fond.Presentation.Tests.");
            return 1;
        }

        Console.WriteLine($"\nPASSED: All {checkCount} checks completed successfully in Broiler.Fond.Presentation.Tests.");
        return 0;
    }
}
