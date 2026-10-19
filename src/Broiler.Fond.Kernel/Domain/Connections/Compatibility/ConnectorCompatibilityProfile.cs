namespace Broiler.Fond.Kernel.Domain.Connections.Compatibility;

/// <summary>
/// An immutable definition of institution-specific FinTS connector characteristics,
/// known implementation quirks, and safe protocol adjustments.
/// Shipped entirely inside application binaries with zero remote control channel.
/// </summary>
public sealed record ConnectorCompatibilityProfile
{
    public required string ProfileId { get; init; }
    public required int Version { get; init; }
    public required string DisplayName { get; init; }
    public required string Description { get; init; }

    /// <summary>Bank code (BLZ) prefixes that match this profile (e.g. "10050").</summary>
    public IReadOnlyList<string> BankCodePrefixes { get; init; } = Array.Empty<string>();

    /// <summary>Exact or prefix BIC strings that match this profile (e.g. "GENODED1").</summary>
    public IReadOnlyList<string> BicPatterns { get; init; } = Array.Empty<string>();

    /// <summary>Endpoint hostname substrings that match this profile (e.g. "fints.banking.de").</summary>
    public IReadOnlyList<string> HostnamePatterns { get; init; } = Array.Empty<string>();

    // -------------------------------------------------------------------------
    // Known Quirk Adjustments & Workarounds
    // -------------------------------------------------------------------------

    /// <summary>Pin HKTAN segment version if the bank's advertised version is buggy or incomplete.</summary>
    public int? PinnedHktanVersion { get; init; }

    /// <summary>Pin HKSAL balance segment version.</summary>
    public int? PinnedHksalVersion { get; init; }

    /// <summary>Maximum batch page size if the bank server errors on larger requests.</summary>
    public int? MaximumBatchPageSize { get; init; }

    /// <summary>Maximum statement date range in days if the bank rejects longer intervals.</summary>
    public int? MaximumStatementQueryDays { get; init; }

    /// <summary>Whether customer ID should be omitted from HNSHK security identification.</summary>
    public bool OmitCustomerIdInSignatureHeader { get; init; }

    /// <summary>Whether ISO-8859 encoding is strictly required by the bank server.</summary>
    public bool RequireIso8859Encoding { get; init; }

    /// <summary>Whether statement pagination overlap detection should be skipped for this bank.</summary>
    public bool DisablePageOverlapSync { get; init; }

    /// <summary>Whether ISO 20022 CAMT statement queries are preferred over SWIFT MT940.</summary>
    public bool PreferCamtOverMt940 { get; init; }

    /// <summary>The default standard profile representing strict, unadjusted FinTS 3.0 specification.</summary>
    public static readonly ConnectorCompatibilityProfile Standard = new()
    {
        ProfileId = "fints-standard",
        Version = 1,
        DisplayName = "FinTS 3.0 Standard",
        Description = "Standard FinTS 3.0 PIN/TAN connector without institution-specific workarounds.",
    };

    /// <summary>
    /// Evaluates whether this profile matches the provided institution criteria.
    /// </summary>
    public bool Matches(string? bankCode, string? bic, string? endpointUri)
    {
        if (!string.IsNullOrWhiteSpace(bankCode))
        {
            foreach (var prefix in BankCodePrefixes)
            {
                if (bankCode.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }
        }

        if (!string.IsNullOrWhiteSpace(bic))
        {
            foreach (var pattern in BicPatterns)
            {
                if (bic.Contains(pattern, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }
        }

        if (!string.IsNullOrWhiteSpace(endpointUri))
        {
            foreach (var host in HostnamePatterns)
            {
                if (endpointUri.Contains(host, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }
        }

        return false;
    }
}
