namespace Broiler.Fond.Kernel.Domain.Connections.Compatibility;

/// <summary>
/// Effective connector compatibility configuration resulting from applying user overrides
/// to a matched institution compatibility profile.
/// </summary>
public sealed record ResolvedConnectorCompatibility
{
    public required string EffectiveProfileId { get; init; }
    public required string DisplayName { get; init; }
    public required int? PinnedHktanVersion { get; init; }
    public required int? PinnedHksalVersion { get; init; }
    public required int? MaximumBatchPageSize { get; init; }
    public required int? MaximumStatementQueryDays { get; init; }
    public required bool OmitCustomerIdInSignatureHeader { get; init; }
    public required bool RequireIso8859Encoding { get; init; }
    public required bool DisablePageOverlapSync { get; init; }
    public required bool PreferCamtOverMt940 { get; init; }
    public required IReadOnlyList<string> ActiveWorkarounds { get; init; }

    public static ResolvedConnectorCompatibility Create(
        ConnectorCompatibilityProfile profile,
        CompatibilityWorkaroundOverride? overrides = null)
    {
        ArgumentNullException.ThrowIfNull(profile);
        var ov = overrides ?? CompatibilityWorkaroundOverride.None;

        if (ov.ForceStandardProfile)
        {
            return new ResolvedConnectorCompatibility
            {
                EffectiveProfileId = ConnectorCompatibilityProfile.Standard.ProfileId,
                DisplayName = ConnectorCompatibilityProfile.Standard.DisplayName,
                PinnedHktanVersion = null,
                PinnedHksalVersion = null,
                MaximumBatchPageSize = null,
                MaximumStatementQueryDays = null,
                OmitCustomerIdInSignatureHeader = false,
                RequireIso8859Encoding = false,
                DisablePageOverlapSync = false,
                PreferCamtOverMt940 = false,
                ActiveWorkarounds = Array.Empty<string>(),
            };
        }

        var active = new List<string>();

        int? pinnedHktan = null;
        if (profile.PinnedHktanVersion.HasValue && ov.IsWorkaroundActive(CompatibilityWorkaroundOverride.KeyPinnedHktanVersion))
        {
            pinnedHktan = profile.PinnedHktanVersion;
            active.Add($"{CompatibilityWorkaroundOverride.KeyPinnedHktanVersion}={pinnedHktan}");
        }

        int? pinnedHksal = null;
        if (profile.PinnedHksalVersion.HasValue && ov.IsWorkaroundActive(CompatibilityWorkaroundOverride.KeyPinnedHksalVersion))
        {
            pinnedHksal = profile.PinnedHksalVersion;
            active.Add($"{CompatibilityWorkaroundOverride.KeyPinnedHksalVersion}={pinnedHksal}");
        }

        int? maxPageSize = null;
        if (profile.MaximumBatchPageSize.HasValue && ov.IsWorkaroundActive(CompatibilityWorkaroundOverride.KeyMaximumBatchPageSize))
        {
            maxPageSize = profile.MaximumBatchPageSize;
            active.Add($"{CompatibilityWorkaroundOverride.KeyMaximumBatchPageSize}={maxPageSize}");
        }

        int? maxQueryDays = null;
        if (profile.MaximumStatementQueryDays.HasValue && ov.IsWorkaroundActive(CompatibilityWorkaroundOverride.KeyMaximumStatementQueryDays))
        {
            maxQueryDays = profile.MaximumStatementQueryDays;
            active.Add($"{CompatibilityWorkaroundOverride.KeyMaximumStatementQueryDays}={maxQueryDays}");
        }

        bool omitCustId = false;
        if (profile.OmitCustomerIdInSignatureHeader && ov.IsWorkaroundActive(CompatibilityWorkaroundOverride.KeyOmitCustomerIdInSignatureHeader))
        {
            omitCustId = true;
            active.Add(CompatibilityWorkaroundOverride.KeyOmitCustomerIdInSignatureHeader);
        }

        bool requireIso = false;
        if (profile.RequireIso8859Encoding && ov.IsWorkaroundActive(CompatibilityWorkaroundOverride.KeyRequireIso8859Encoding))
        {
            requireIso = true;
            active.Add(CompatibilityWorkaroundOverride.KeyRequireIso8859Encoding);
        }

        bool disableOverlap = false;
        if (profile.DisablePageOverlapSync && ov.IsWorkaroundActive(CompatibilityWorkaroundOverride.KeyDisablePageOverlapSync))
        {
            disableOverlap = true;
            active.Add(CompatibilityWorkaroundOverride.KeyDisablePageOverlapSync);
        }

        bool preferCamt = false;
        if (profile.PreferCamtOverMt940 && ov.IsWorkaroundActive(CompatibilityWorkaroundOverride.KeyPreferCamtOverMt940))
        {
            preferCamt = true;
            active.Add(CompatibilityWorkaroundOverride.KeyPreferCamtOverMt940);
        }

        return new ResolvedConnectorCompatibility
        {
            EffectiveProfileId = profile.ProfileId,
            DisplayName = profile.DisplayName,
            PinnedHktanVersion = pinnedHktan,
            PinnedHksalVersion = pinnedHksal,
            MaximumBatchPageSize = maxPageSize,
            MaximumStatementQueryDays = maxQueryDays,
            OmitCustomerIdInSignatureHeader = omitCustId,
            RequireIso8859Encoding = requireIso,
            DisablePageOverlapSync = disableOverlap,
            PreferCamtOverMt940 = preferCamt,
            ActiveWorkarounds = active.AsReadOnly(),
        };
    }
}
