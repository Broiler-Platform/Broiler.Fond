using Broiler.Fond.Kernel.Product;

namespace Broiler.Fond.Presentation.Services;

/// <summary>Explicit deployment configuration, not a claim of registration approval.</summary>
public sealed record BankProductConfiguration
{
    private BankProductConfiguration(FinTsProductRegistration? registration, string? error) { Registration = registration; Error = error; }
    public FinTsProductRegistration? Registration { get; }
    public string? Error { get; }
    public bool IsConfigured => Registration is not null;
    public static BankProductConfiguration FromSettings(string? assignedProductId, string? version)
    {
        if (string.IsNullOrWhiteSpace(assignedProductId) || assignedProductId.Any(char.IsWhiteSpace) || assignedProductId is "9999999999" or "BROILER-FOND")
            return new(null, "This installation has no assigned FinTS application product ID. The product owner must configure its registered ID before bank sign-in.");
        try
        {
            // The assigned number belongs in HKVVB ProductIdentifier, not merely in a side metadata field.
            return new(new FinTsProductRegistration(assignedProductId, version ?? "1.0", assignedProductId), null);
        }
        catch (ArgumentException)
        {
            return new(null, "The configured FinTS product ID or version is invalid. Ask the application administrator to correct it.");
        }
    }
}
