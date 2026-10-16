using System.Globalization;

namespace Broiler.Fond.Kernel.FinTs;

/// <summary>
/// Immutable specification of a negotiated FinTS 3.0 PIN/TAN security profile.
/// Validates signature headers and security envelopes against the negotiated profile.
/// </summary>
public sealed class FinTsSecurityProfile : IEquatable<FinTsSecurityProfile>
{
    public const string MethodName = "PIN";
    public const int DefaultSupplierRole = 1; // Bank client (Kunde)

    public int ProfileVersion { get; }
    public string SecurityFunction { get; }
    public int HashAlgorithmCode { get; }
    public int SupplierRole { get; }

    public FinTsSecurityProfile(
        int profileVersion,
        string securityFunction,
        int supplierRole = DefaultSupplierRole)
    {
        if (profileVersion is not (1 or 2))
        {
            throw new ArgumentOutOfRangeException(
                nameof(profileVersion),
                profileVersion,
                "PIN/TAN profile version must be 1 (one-step/SHA-1) or 2 (two-step/SHA-256).");
        }

        ArgumentException.ThrowIfNullOrWhiteSpace(securityFunction);
        string trimmedFunction = securityFunction.Trim();

        if (trimmedFunction.Length != 3 || !int.TryParse(trimmedFunction, CultureInfo.InvariantCulture, out int functionCode) ||
            functionCode is < 900 or > 999)
        {
            throw new ArgumentException(
                "Security function must be a 3-digit numeric code between 900 and 999.",
                nameof(securityFunction));
        }

        if (supplierRole is not (1 or 2))
        {
            throw new ArgumentOutOfRangeException(
                nameof(supplierRole),
                supplierRole,
                "Supplier role must be 1 (customer) or 2 (bank).");
        }

        ProfileVersion = profileVersion;
        SecurityFunction = trimmedFunction;
        HashAlgorithmCode = profileVersion; // 1 for PIN:1 (SHA-1), 2 for PIN:2 (SHA-256)
        SupplierRole = supplierRole;
    }

    /// <summary>Validates an observed HNSHK signature header against this security profile.</summary>
    public bool ValidateHeader(FinTsPinTanSignatureHeader header, out string? issue)
    {
        ArgumentNullException.ThrowIfNull(header);

        if (header.ProfileVersion != ProfileVersion)
        {
            issue = $"Header profile version {header.ProfileVersion} does not match negotiated profile {ProfileVersion}.";
            return false;
        }

        if (!string.Equals(header.SecurityFunction, SecurityFunction, StringComparison.Ordinal))
        {
            issue = $"Header security function {header.SecurityFunction} does not match negotiated function {SecurityFunction}.";
            return false;
        }

        if (header.SecuritySupplierRole != SupplierRole)
        {
            issue = $"Header supplier role {header.SecuritySupplierRole} does not match expected role {SupplierRole}.";
            return false;
        }

        const string pinTanHashAlgorithm = "999";
        if (!string.Equals(header.HashAlgorithmCode, pinTanHashAlgorithm, StringComparison.Ordinal))
        {
            issue = $"Header hash algorithm {header.HashAlgorithmCode} does not match expected PIN/TAN algorithm {pinTanHashAlgorithm}.";
            return false;
        }

        issue = null;
        return true;
    }

    /// <summary>Validates an observed response envelope against this security profile.</summary>
    public bool ValidateEnvelope(FinTsPinTanEnvelope envelope, out string? issue)
    {
        ArgumentNullException.ThrowIfNull(envelope);

        if (envelope.ProfileVersion != ProfileVersion)
        {
            issue = $"Envelope profile version {envelope.ProfileVersion} does not match negotiated profile {ProfileVersion}.";
            return false;
        }

        issue = null;
        return true;
    }

    public bool Equals(FinTsSecurityProfile? other)
    {
        if (other is null)
        {
            return false;
        }

        if (ReferenceEquals(this, other))
        {
            return true;
        }

        return ProfileVersion == other.ProfileVersion &&
               string.Equals(SecurityFunction, other.SecurityFunction, StringComparison.Ordinal) &&
               HashAlgorithmCode == other.HashAlgorithmCode &&
               SupplierRole == other.SupplierRole;
    }

    public override bool Equals(object? obj) => Equals(obj as FinTsSecurityProfile);

    public override int GetHashCode() => HashCode.Combine(ProfileVersion, SecurityFunction, HashAlgorithmCode, SupplierRole);

    public override string ToString() => $"PIN:{ProfileVersion.ToString(CultureInfo.InvariantCulture)} (Function={SecurityFunction}, Hash={HashAlgorithmCode.ToString(CultureInfo.InvariantCulture)}, Role={SupplierRole.ToString(CultureInfo.InvariantCulture)})";

    public static bool operator ==(FinTsSecurityProfile? left, FinTsSecurityProfile? right)
    {
        if (left is null)
        {
            return right is null;
        }

        return left.Equals(right);
    }

    public static bool operator !=(FinTsSecurityProfile? left, FinTsSecurityProfile? right) => !(left == right);
}
