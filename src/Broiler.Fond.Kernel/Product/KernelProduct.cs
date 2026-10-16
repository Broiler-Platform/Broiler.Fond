namespace Broiler.Fond.Kernel.Product;

/// <summary>
/// Identifies the product and the milestone currently in development.
/// </summary>
public static class KernelProduct
{
    /// <summary>The product brand.</summary>
    public const string Brand = "Broiler";

    /// <summary>The product name without the brand.</summary>
    public const string ProductName = "Fond - Finance on Demand";

    /// <summary>The complete user-facing product name.</summary>
    public const string FullDisplayName = "Broiler Fond - Finance on Demand";

    /// <summary>The development milestone, not a declaration of release readiness.</summary>
    public const int Milestone = 1;

    /// <summary>
    /// Designated FinTS 3.0 product registration identity for HKVVB dialogue preparation.
    /// Product ID: "BROILER-FOND", Version: "1.0", Registration Code: "9999999999".
    /// </summary>
    public static readonly FinTsProductRegistration FinTsRegistration = new(
        productIdentifier: "BROILER-FOND",
        productVersion: "1.0",
        registrationCode: "9999999999",
        displayName: FullDisplayName);
}
