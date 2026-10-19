using System.Globalization;
using Broiler.Fond.Kernel.Domain.Payments;

namespace Broiler.Fond.Kernel.FinTs;

/// <summary>
/// Untrusted BPD parameter advertisement for SEPA direct-debit return operations:
/// HIDRWS (SEPA-Lastschriftrückbuchung Parameter) and HIDMBS (SEPA-Mandatsverwaltung Parameter).
/// </summary>
public sealed class FinTsDirectDebitReturnAdvertisement
{
    public const string ReturnParameterCode = "HIDRWS";
    public const string MandateParameterCode = "HIDMBS";

    public bool HasReturnSupport { get; }
    public bool HasMandateRevocationSupport { get; }
    public int MaxAdvanceNoticeDays { get; }
    public IReadOnlySet<DirectDebitReturnReason> SupportedReasons { get; }

    internal FinTsDirectDebitReturnAdvertisement(
        bool hasReturn,
        bool hasMandate,
        int maxAdvanceNoticeDays = 30,
        IReadOnlySet<DirectDebitReturnReason>? supportedReasons = null)
    {
        HasReturnSupport = hasReturn;
        HasMandateRevocationSupport = hasMandate;
        MaxAdvanceNoticeDays = maxAdvanceNoticeDays;
        SupportedReasons = supportedReasons ?? new HashSet<DirectDebitReturnReason>
        {
            DirectDebitReturnReason.DebtorRequestUnconditional,
            DirectDebitReturnReason.DebtorRequestGeneral,
            DirectDebitReturnReason.NoValidMandate,
            DirectDebitReturnReason.DisputedTransaction,
        };
    }

    /// <summary>
    /// Converts discovered parameters into a validated <see cref="DirectDebitReturnCapability"/> domain record.
    /// </summary>
    public DirectDebitReturnCapability ToCapability()
    {
        if (!HasReturnSupport)
        {
            return DirectDebitReturnCapability.Unsupported;
        }

        return new DirectDebitReturnCapability(
            IsSupported: true,
            SupportedReasons: SupportedReasons,
            SupportsMandateRevocation: HasMandateRevocationSupport,
            MaxAdvanceNoticeDays: MaxAdvanceNoticeDays);
    }

    /// <summary>
    /// Discovers direct-debit return capabilities from a FinTS response by inspecting HIDRWS and HIDMBS segments.
    /// </summary>
    public static DirectDebitReturnCapability DiscoverCapability(FinTsResponse response)
    {
        ArgumentNullException.ThrowIfNull(response);

        bool hasReturn = false;
        bool hasMandate = false;
        int maxDays = 30;
        HashSet<DirectDebitReturnReason> reasons = [];

        foreach (FinTsSegment segment in response.BodySegments)
        {
            if (segment.Code == ReturnParameterCode)
            {
                hasReturn = true;

                // Expected HIDRWS structure:
                // Field 0: Maximale Anzahl Aufträge
                // Field 1: Anzahl Signaturen
                // Field 2: Sicherheitsklasse
                // Field 3: Erlaubte Rückgabegründe (optional list of ISO reason codes)
                if (segment.Fields.Count > 3 && segment.Fields[3].Elements.Count > 0)
                {
                    foreach (var elem in segment.Fields[3].Elements)
                    {
                        if (elem.IsEmpty) continue;
                        string code = elem.HeaderText();
                        try
                        {
                            reasons.Add(DirectDebitReturnReasonExtensions.FromReasonCode(code));
                        }
                        catch (ArgumentException)
                        {
                            // Unrecognized reason code ignored safely
                        }
                    }
                }
            }
            else if (segment.Code == MandateParameterCode)
            {
                hasMandate = true;
            }
        }

        if (!hasReturn)
        {
            return DirectDebitReturnCapability.Unsupported;
        }

        if (reasons.Count == 0)
        {
            reasons.Add(DirectDebitReturnReason.DebtorRequestUnconditional);
            reasons.Add(DirectDebitReturnReason.DebtorRequestGeneral);
            reasons.Add(DirectDebitReturnReason.NoValidMandate);
            reasons.Add(DirectDebitReturnReason.DisputedTransaction);
        }

        return new DirectDebitReturnCapability(
            IsSupported: true,
            SupportedReasons: reasons,
            SupportsMandateRevocation: hasMandate,
            MaxAdvanceNoticeDays: maxDays);
    }
}
