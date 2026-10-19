using System.Globalization;
using Broiler.Fond.Kernel.Domain.Payments;

namespace Broiler.Fond.Kernel.FinTs;

/// <summary>
/// Untrusted BPD parameter advertisement for SEPA standing order operations:
/// HICDBS (List), HICDES (Setup), HICDAS (Modify), and HICDLS (Delete).
/// </summary>
public sealed class FinTsStandingOrderAdvertisement
{
    public const string ListParameterCode = "HICDBS";
    public const string SetupParameterCode = "HICDES";
    public const string ModifyParameterCode = "HICDAS";
    public const string DeleteParameterCode = "HICDLS";

    public bool HasListSupport { get; }
    public bool HasSetupSupport { get; }
    public bool HasModifySupport { get; }
    public bool HasDeleteSupport { get; }
    public int MinimumLeadDays { get; }
    public IReadOnlyList<StandingOrderFrequency> SupportedFrequencies { get; }

    internal FinTsStandingOrderAdvertisement(
        bool hasList,
        bool hasSetup,
        bool hasModify,
        bool hasDelete,
        int minLeadDays = 1,
        IReadOnlyList<StandingOrderFrequency>? frequencies = null)
    {
        HasListSupport = hasList;
        HasSetupSupport = hasSetup;
        HasModifySupport = hasModify;
        HasDeleteSupport = hasDelete;
        MinimumLeadDays = minLeadDays;
        SupportedFrequencies = frequencies ??
        [
            StandingOrderFrequency.Monthly,
            StandingOrderFrequency.TwoMonthly,
            StandingOrderFrequency.Quarterly,
            StandingOrderFrequency.SemiAnnually,
            StandingOrderFrequency.Annually
        ];
    }

    /// <summary>
    /// Converts discovered parameters into a validated <see cref="StandingOrderCapability"/> domain record.
    /// </summary>
    public StandingOrderCapability ToCapability()
    {
        bool isSupported = HasListSupport || HasSetupSupport;
        if (!isSupported)
        {
            return StandingOrderCapability.Unsupported;
        }

        return new StandingOrderCapability(
            IsSupported: true,
            CanCreate: HasSetupSupport,
            CanModify: HasModifySupport,
            CanDelete: HasDeleteSupport,
            SupportedFrequencies: SupportedFrequencies,
            MinimumLeadDays: MinimumLeadDays,
            SupportedSegmentCode: "HKCDB",
            SupportedSegmentVersion: 1);
    }

    /// <summary>
    /// Discovers standing order capabilities from a parameter set by inspecting HICDBS, HICDES, HICDAS, and HICDLS segments.
    /// </summary>
    public static StandingOrderCapability DiscoverCapability(FinTsParameterSet parameterSet)
    {
        ArgumentNullException.ThrowIfNull(parameterSet);

        var allSegments = parameterSet.UninterpretedSegments.Concat(parameterSet.Source.BodySegments);
        bool hasList = false;
        bool hasSetup = false;
        bool hasModify = false;
        bool hasDelete = false;
        int minLeadDays = 1;
        List<StandingOrderFrequency> freqs = [];

        foreach (var seg in allSegments)
        {
            if (seg.Code == ListParameterCode)
            {
                hasList = true;
            }
            else if (seg.Code == SetupParameterCode)
            {
                hasSetup = true;
                if (seg.Fields.Count > 3 && seg.Fields[3].Elements.Count > 0 &&
                    int.TryParse(seg.Fields[3].Elements[0].HeaderText(), NumberStyles.Integer, CultureInfo.InvariantCulture, out int lead))
                {
                    minLeadDays = Math.Max(1, lead);
                }

                // Check optional allowed frequencies list if specified
                if (seg.Fields.Count > 4 && seg.Fields[4].Elements.Count > 0)
                {
                    foreach (var elem in seg.Fields[4].Elements)
                    {
                        if (int.TryParse(elem.HeaderText(), NumberStyles.Integer, CultureInfo.InvariantCulture, out int fVal) &&
                            Enum.IsDefined(typeof(StandingOrderFrequency), fVal))
                        {
                            freqs.Add((StandingOrderFrequency)fVal);
                        }
                    }
                }
            }
            else if (seg.Code == ModifyParameterCode)
            {
                hasModify = true;
            }
            else if (seg.Code == DeleteParameterCode)
            {
                hasDelete = true;
            }
        }

        if (!hasList && !hasSetup)
        {
            return StandingOrderCapability.Unsupported;
        }

        IReadOnlyList<StandingOrderFrequency> finalFreqs = freqs.Count > 0
            ? freqs.Distinct().ToList().AsReadOnly()
            : StandingOrderCapability.StandardDefault.SupportedFrequencies;

        return new StandingOrderCapability(
            IsSupported: true,
            CanCreate: hasSetup,
            CanModify: hasModify,
            CanDelete: hasDelete,
            SupportedFrequencies: finalFreqs,
            MinimumLeadDays: minLeadDays,
            SupportedSegmentCode: "HKCDB",
            SupportedSegmentVersion: 1);
    }
}
