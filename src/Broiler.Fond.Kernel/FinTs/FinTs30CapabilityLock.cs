using System.Collections.Frozen;
using System.Collections.ObjectModel;

namespace Broiler.Fond.Kernel.FinTs;

/// <summary>
/// Thrown when a FinTS segment version is requested or received that is outside the frozen FinTS 3.0 matrix.
/// </summary>
public sealed class UnsupportedSegmentVersionException : InvalidOperationException
{
    public string SegmentCode { get; }
    public int RequestedVersion { get; }
    public IReadOnlyList<int> SupportedVersions { get; }

    public UnsupportedSegmentVersionException(string segmentCode, int requestedVersion, IReadOnlyList<int> supportedVersions)
        : base($"Segment '{segmentCode}' version {requestedVersion} is not supported by the frozen FinTS 3.0 matrix. Supported version(s): {string.Join(", ", supportedVersions)}.")
    {
        SegmentCode = segmentCode;
        RequestedVersion = requestedVersion;
        SupportedVersions = supportedVersions;
    }
}

/// <summary>
/// Formal specification and capability lock for FinTS 3.0 PIN/TAN operations.
/// Invariant: Windows 1.0 supports strictly verified FinTS 3.0 segment versions;
/// speculative or unverified protocol versions fail closed.
/// </summary>
public static class FinTs30CapabilityLock
{
    private static readonly Dictionary<string, int[]> Matrix = new(StringComparer.OrdinalIgnoreCase)
    {
        // Envelopes and framing
        ["HNHBK"] = [3],
        ["HNHBS"] = [3],
        ["HNVSK"] = [3],
        ["HNVSD"] = [1],
        ["HNSHK"] = [4],
        ["HNSHA"] = [2],

        // Dialogue initialization and product registration
        ["HKIDN"] = [2],
        ["HKVVB"] = [3],
        ["HKEND"] = [1],

        // Security and challenge procedures
        ["HKTAN"] = [6, 7],
        ["HITAN"] = [6, 7],
        ["HITANS"] = [1, 2, 3, 4, 5, 6, 7],
        ["HIPINS"] = [1],

        // Responses and parameter data
        ["HIRMG"] = [2],
        ["HIRMS"] = [2],
        ["HIBPA"] = [3],
        ["HIUPA"] = [3],
        ["HIUPD"] = [5, 6],

        // Account balances
        ["HKSAL"] = [5, 6, 7],
        ["HISAL"] = [5, 6, 7],
        ["HISALS"] = [1, 2, 3, 4, 5, 6, 7],

        // Account statements (MT940 and CAMT)
        ["HKKAZ"] = [5, 6, 7],
        ["HIKAZ"] = [5, 6, 7],
        ["HIKAZS"] = [1, 2, 3, 4, 5, 6, 7],
        ["HKCAZ"] = [1],
        ["HICAZ"] = [1],
        ["HICAZS"] = [1],

        // SEPA money movement
        ["HKCCS"] = [1],
        ["HICCS"] = [1],
        ["HICCSS"] = [1],
        ["HKIPB"] = [1],
        ["HKSIP"] = [1],

        // Scheduled transfers
        ["HKCDB"] = [1],
        ["HKCME"] = [1],
        ["HKCDL"] = [1],
        ["HICDL"] = [1],

        // Standing orders
        ["HKDMB"] = [1],
        ["HKDME"] = [1],
        ["HKDLS"] = [1],
        ["HIDLS"] = [1],

        // Direct debit returns
        ["HKLAS"] = [4],
        ["HILAS"] = [4],

        // SEPA account parameters
        ["HKSPA"] = [1],
        ["HISPA"] = [1],
        ["HISPAS"] = [1],

        // Verification of Payee
        ["HKVOP"] = [1],
        ["HIVOP"] = [1],
    };

    /// <summary>
    /// Gets the frozen dictionary of supported FinTS 3.0 segment codes and their allowed versions.
    /// </summary>
    public static IReadOnlyDictionary<string, int[]> FrozenMatrix { get; } =
        new ReadOnlyDictionary<string, int[]>(Matrix);

    /// <summary>
    /// Determines whether the specified segment code and version are part of the frozen FinTS 3.0 capability matrix.
    /// </summary>
    public static bool IsSegmentSupported(string code, int version)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(code);
        return Matrix.TryGetValue(code, out int[]? versions) && versions.Contains(version);
    }

    /// <summary>
    /// Returns the allowed versions for a segment code, or an empty list if not recognized.
    /// </summary>
    public static IReadOnlyList<int> GetSupportedVersions(string code)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(code);
        return Matrix.TryGetValue(code, out int[]? versions) ? versions : Array.Empty<int>();
    }

    /// <summary>
    /// Asserts that a segment code and version comply with the frozen FinTS 3.0 matrix, throwing if unsupported.
    /// </summary>
    public static void ValidateSegment(string code, int version)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(code);
        if (!IsSegmentSupported(code, version))
        {
            var supported = GetSupportedVersions(code);
            throw new UnsupportedSegmentVersionException(code, version, supported);
        }
    }
}
