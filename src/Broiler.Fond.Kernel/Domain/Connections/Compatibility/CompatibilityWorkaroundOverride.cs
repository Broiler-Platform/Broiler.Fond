namespace Broiler.Fond.Kernel.Domain.Connections.Compatibility;

/// <summary>
/// User-configurable overrides allowing individual workarounds or profiles to be disabled
/// locally without remote intervention.
/// </summary>
public sealed record CompatibilityWorkaroundOverride
{
    public const string KeyPinnedHktanVersion = "PinnedHktanVersion";
    public const string KeyPinnedHksalVersion = "PinnedHksalVersion";
    public const string KeyMaximumBatchPageSize = "MaximumBatchPageSize";
    public const string KeyMaximumStatementQueryDays = "MaximumStatementQueryDays";
    public const string KeyOmitCustomerIdInSignatureHeader = "OmitCustomerIdInSignatureHeader";
    public const string KeyRequireIso8859Encoding = "RequireIso8859Encoding";
    public const string KeyDisablePageOverlapSync = "DisablePageOverlapSync";
    public const string KeyPreferCamtOverMt940 = "PreferCamtOverMt940";

    /// <summary>
    /// When true, ignores any matched institution profile and forces the strict FinTS 3.0 Standard profile.
    /// </summary>
    public bool ForceStandardProfile { get; init; }

    /// <summary>
    /// Explicit set of workaround keys that are disabled by user configuration.
    /// </summary>
    public IReadOnlySet<string> DisabledWorkaroundKeys { get; init; } = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

    /// <summary>Default configuration with zero overrides (all profile workarounds active).</summary>
    public static readonly CompatibilityWorkaroundOverride None = new();

    /// <summary>
    /// Checks whether a specific workaround is active given user overrides.
    /// </summary>
    public bool IsWorkaroundActive(string workaroundKey)
    {
        if (ForceStandardProfile)
        {
            return false;
        }

        return !DisabledWorkaroundKeys.Contains(workaroundKey);
    }
}
