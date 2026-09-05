namespace Broiler.Fond.Kernel.Product;

/// <summary>
/// Identifies the product and the currently scaffolded milestone.
/// </summary>
public static class KernelProduct
{
    /// <summary>The product brand.</summary>
    public const string Brand = "Broiler";

    /// <summary>The product name without the brand.</summary>
    public const string ProductName = "Fond - Finance on Demand";

    /// <summary>The complete user-facing product name.</summary>
    public const string FullDisplayName = "Broiler Fond - Finance on Demand";

    /// <summary>The scaffold milestone. Banking capabilities begin after M0.</summary>
    public const int Milestone = 0;
}
