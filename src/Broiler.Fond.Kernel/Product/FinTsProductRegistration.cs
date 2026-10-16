namespace Broiler.Fond.Kernel.Product;

/// <summary>
/// Immutable FinTS 3.0 product registration identity for HKVVB dialogue preparation.
/// Defined by the Deutsche Kreditwirtschaft (DK) FinTS specification.
/// </summary>
public sealed record FinTsProductRegistration
{
    public const int MaximumProductIdentifierLength = 25;
    public const int MaximumProductVersionLength = 5;
    public const int MaximumRegistrationCodeLength = 25;

    public FinTsProductRegistration(
        string productIdentifier,
        string productVersion,
        string? registrationCode = null,
        string? displayName = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(productIdentifier);
        ArgumentException.ThrowIfNullOrWhiteSpace(productVersion);

        if (productIdentifier.Length > MaximumProductIdentifierLength)
        {
            throw new ArgumentOutOfRangeException(nameof(productIdentifier),
                $"Product identifier must not exceed {MaximumProductIdentifierLength} characters.");
        }

        if (productVersion.Length > MaximumProductVersionLength)
        {
            throw new ArgumentOutOfRangeException(nameof(productVersion),
                $"Product version must not exceed {MaximumProductVersionLength} characters.");
        }

        ValidateCharacters(productIdentifier, nameof(productIdentifier));
        ValidateCharacters(productVersion, nameof(productVersion));

        if (registrationCode is not null)
        {
            if (string.IsNullOrWhiteSpace(registrationCode) || registrationCode.Length > MaximumRegistrationCodeLength)
            {
                throw new ArgumentOutOfRangeException(nameof(registrationCode),
                    $"Registration code must not exceed {MaximumRegistrationCodeLength} characters.");
            }
            ValidateCharacters(registrationCode, nameof(registrationCode));
        }

        ProductIdentifier = productIdentifier;
        ProductVersion = productVersion;
        RegistrationCode = registrationCode;
        DisplayName = displayName ?? KernelProduct.FullDisplayName;
    }

    /// <summary>The product registration identifier transmitted in HKVVB:3:3 field 4 (max 25 characters).</summary>
    public string ProductIdentifier { get; }

    /// <summary>The product version transmitted in HKVVB:3:3 field 5 (max 5 characters).</summary>
    public string ProductVersion { get; }

    /// <summary>Optional Deutsche Kreditwirtschaft (DK) registration code (e.g. 9999999999 or 25-char assigned ID).</summary>
    public string? RegistrationCode { get; }

    /// <summary>User-facing full product name.</summary>
    public string DisplayName { get; }

    private static void ValidateCharacters(string value, string paramName)
    {
        foreach (char c in value)
        {
            if (c is '+' or ':' or '\'' or '?' or '@' || c < 32 || (c >= 127 && c <= 160))
            {
                throw new ArgumentException($"Value contains forbidden FinTS syntax or non-printable character '{c}'.", paramName);
            }
        }
    }
}
