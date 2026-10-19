using System.Collections.Frozen;
using System.Collections.ObjectModel;

namespace Broiler.Fond.Kernel.FinTs;

/// <summary>
/// Canonical protocol versions supported by the Broiler Fond FinTS engine.
/// </summary>
public enum FinTsProtocolVersion
{
    /// <summary>
    /// FinTS 3.0 PIN/TAN with standard delimited byte syntax.
    /// </summary>
    FinTs30 = 300,

    /// <summary>
    /// FinTS 4.1 PIN/TAN with structured XML syntax and ISO 20022 schemas.
    /// </summary>
    FinTs41 = 410
}

/// <summary>
/// User or profile preference governing protocol version negotiation.
/// </summary>
public enum FinTsProtocolPreference
{
    /// <summary>
    /// Negotiate highest available supported version advertised by the bank (prefers 4.1 if advertised, falls back to 3.0).
    /// </summary>
    Auto = 0,

    /// <summary>
    /// Explicitly prefer FinTS 4.1 when available, falling back to 3.0 only if unadvertised.
    /// </summary>
    PreferFinTs41 = 1,

    /// <summary>
    /// Force FinTS 3.0 PIN/TAN syntax; never attempt or negotiate FinTS 4.1.
    /// </summary>
    ForceFinTs30 = 2,

    /// <summary>
    /// Require FinTS 4.1; fail closed if institution does not advertise or support FinTS 4.1.
    /// </summary>
    ForceFinTs41 = 3
}

/// <summary>
/// Protocol capabilities advertised by bank parameter data (BPD) or connector profile.
/// </summary>
public sealed record FinTsProtocolAdvertisement(
    IReadOnlyList<FinTsProtocolVersion> SupportedVersions,
    string? RawAdvertisedProtocol = null,
    bool AdvertisesFinTs41Xml = false)
{
    public static FinTsProtocolAdvertisement FinTs30Only =>
        new([FinTsProtocolVersion.FinTs30], "FinTS3.0", false);

    public static FinTsProtocolAdvertisement FinTs41Only =>
        new([FinTsProtocolVersion.FinTs41], "FinTS4.1", true);

    public static FinTsProtocolAdvertisement Dual30And41 =>
        new([FinTsProtocolVersion.FinTs41, FinTsProtocolVersion.FinTs30], "FinTS3.0,FinTS4.1", true);
}

/// <summary>
/// Immutable descriptor representing a successfully negotiated protocol version.
/// </summary>
public sealed record FinTsNegotiatedProtocol(
    FinTsProtocolVersion Version,
    string ProtocolIdentifier,
    int MajorVersion,
    int MinorVersion,
    bool IsXmlTransport,
    string NegotiationReason);

/// <summary>
/// Outcome of protocol version negotiation.
/// </summary>
public sealed record FinTsNegotiationResult(
    bool IsSuccessful,
    FinTsNegotiatedProtocol? Protocol,
    string? FailureReason = null)
{
    public static FinTsNegotiationResult Success(FinTsNegotiatedProtocol protocol) =>
        new(true, protocol, null);

    public static FinTsNegotiationResult Failure(string reason) =>
        new(false, null, reason);
}

/// <summary>
/// Thrown when an institution advertises an unsupported, legacy, or unrecognized protocol version.
/// </summary>
public sealed class UnsupportedProtocolVersionException : InvalidOperationException
{
    public string AttemptedVersion { get; }

    public UnsupportedProtocolVersionException(string attemptedVersion, string message)
        : base(message)
    {
        AttemptedVersion = attemptedVersion;
    }
}

/// <summary>
/// Thrown when a dialogue attempt receives a message or response with a protocol version mismatch.
/// </summary>
public sealed class FinTsProtocolMismatchException : InvalidOperationException
{
    public FinTsProtocolVersion ExpectedVersion { get; }
    public FinTsProtocolVersion ActualVersion { get; }

    public FinTsProtocolMismatchException(FinTsProtocolVersion expected, FinTsProtocolVersion actual, string message)
        : base(message)
    {
        ExpectedVersion = expected;
        ActualVersion = actual;
    }
}

/// <summary>
/// Contract for negotiating and asserting FinTS protocol versions.
/// </summary>
public interface IFinTsProtocolNegotiator
{
    FinTsNegotiationResult Negotiate(
        FinTsProtocolAdvertisement advertisement,
        FinTsProtocolPreference preference = FinTsProtocolPreference.Auto);

    void ValidateDialogueBinding(
        FinTsProtocolVersion expectedVersion,
        FinTsProtocolVersion actualVersion);
}

/// <summary>
/// Implements deterministic protocol version negotiation and dialogue version binding validation.
/// </summary>
public sealed class FinTsProtocolNegotiator : IFinTsProtocolNegotiator
{
    public static readonly FinTsProtocolNegotiator Instance = new();

    private static readonly FrozenDictionary<FinTsProtocolVersion, (string Identifier, int Major, int Minor, bool IsXml)> Descriptors =
        new Dictionary<FinTsProtocolVersion, (string Identifier, int Major, int Minor, bool IsXml)>
        {
            [FinTsProtocolVersion.FinTs30] = ("FinTS-3.0", 3, 0, false),
            [FinTsProtocolVersion.FinTs41] = ("FinTS-4.1", 4, 1, true)
        }.ToFrozenDictionary();

    public FinTsNegotiationResult Negotiate(
        FinTsProtocolAdvertisement advertisement,
        FinTsProtocolPreference preference = FinTsProtocolPreference.Auto)
    {
        ArgumentNullException.ThrowIfNull(advertisement);

        if (advertisement.SupportedVersions == null || advertisement.SupportedVersions.Count == 0)
        {
            return FinTsNegotiationResult.Failure("Bank parameter data contains no supported protocol version advertisement.");
        }

        // Check for legacy or unapproved protocol versions in raw string
        if (!string.IsNullOrWhiteSpace(advertisement.RawAdvertisedProtocol))
        {
            var raw = advertisement.RawAdvertisedProtocol.Trim();
            if (raw.StartsWith("HBCI", StringComparison.OrdinalIgnoreCase) ||
                raw.Contains("2.0", StringComparison.OrdinalIgnoreCase) ||
                raw.Contains("2.1", StringComparison.OrdinalIgnoreCase) ||
                raw.Contains("2.2", StringComparison.OrdinalIgnoreCase))
            {
                throw new UnsupportedProtocolVersionException(raw,
                    $"Legacy protocol '{raw}' is discontinued by the German Banking Industry Committee and is rejected by Broiler Fond policy.");
            }
        }

        bool supports30 = advertisement.SupportedVersions.Contains(FinTsProtocolVersion.FinTs30);
        bool supports41 = advertisement.SupportedVersions.Contains(FinTsProtocolVersion.FinTs41);

        switch (preference)
        {
            case FinTsProtocolPreference.ForceFinTs30:
                if (!supports30)
                {
                    return FinTsNegotiationResult.Failure("FinTS 3.0 was explicitly forced, but the institution does not advertise FinTS 3.0 support.");
                }
                return FinTsNegotiationResult.Success(CreateDescriptor(FinTsProtocolVersion.FinTs30, "Forced by connection preference."));

            case FinTsProtocolPreference.ForceFinTs41:
                if (!supports41)
                {
                    return FinTsNegotiationResult.Failure("FinTS 4.1 was explicitly forced, but the institution does not advertise FinTS 4.1 support.");
                }
                return FinTsNegotiationResult.Success(CreateDescriptor(FinTsProtocolVersion.FinTs41, "Forced by connection preference."));

            case FinTsProtocolPreference.PreferFinTs41:
            case FinTsProtocolPreference.Auto:
            default:
                if (supports41 && advertisement.AdvertisesFinTs41Xml)
                {
                    return FinTsNegotiationResult.Success(CreateDescriptor(FinTsProtocolVersion.FinTs41, "FinTS 4.1 XML negotiated based on bank capability advertisement."));
                }
                if (supports30)
                {
                    return FinTsNegotiationResult.Success(CreateDescriptor(FinTsProtocolVersion.FinTs30, "FinTS 3.0 negotiated as established baseline."));
                }
                return FinTsNegotiationResult.Failure("Neither FinTS 3.0 nor FinTS 4.1 could be negotiated from the bank capability advertisement.");
        }
    }

    public void ValidateDialogueBinding(
        FinTsProtocolVersion expectedVersion,
        FinTsProtocolVersion actualVersion)
    {
        if (expectedVersion != actualVersion)
        {
            throw new FinTsProtocolMismatchException(
                expectedVersion,
                actualVersion,
                $"FinTS dialogue was bound to '{expectedVersion}', but received response in '{actualVersion}'. Protocol version mutation during dialogue is forbidden.");
        }
    }

    private static FinTsNegotiatedProtocol CreateDescriptor(FinTsProtocolVersion version, string reason)
    {
        var meta = Descriptors[version];
        return new FinTsNegotiatedProtocol(
            version,
            meta.Identifier,
            meta.Major,
            meta.Minor,
            meta.IsXml,
            reason);
    }
}
