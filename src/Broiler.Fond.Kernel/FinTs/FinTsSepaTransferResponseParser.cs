using Broiler.Fond.Kernel.Domain.Payments;

namespace Broiler.Fond.Kernel.FinTs;

/// <summary>
/// Parsed outcome of a FinTS SEPA credit transfer response.
/// </summary>
public sealed record FinTsSepaTransferResponseResult(
    bool IsSuccess,
    bool IsScaRequired,
    bool IsRejected,
    PaymentState TargetState,
    string? BankOrderReference,
    string PrimaryReturnCode,
    string PrimaryReturnText,
    string? ChallengeText = null,
    string? ChallengeMatrixPayload = null);

/// <summary>
/// Parses HICCS response segments, HITAN challenge segments, and correlated HIRMS/HIRMG codes
/// from an authoritative bank response.
/// </summary>
public static class FinTsSepaTransferResponseParser
{
    public const string HiccsSegmentCode = "HICCS";
    public const string HiipbSegmentCode = "HIIPB";
    public const string HicmeSegmentCode = "HICME";
    public const string HitanSegmentCode = "HITAN";

    public static FinTsSepaTransferResponseResult Parse(
        FinTsResponse response,
        int requestSegmentOrdinal = 2)
    {
        ArgumentNullException.ThrowIfNull(response);

        string? bankOrderRef = null;
        string? challengeText = null;
        string? challengeMatrixPayload = null;
        bool scaDetected = false;

        // 1. Scan for HICCS, HIIPB, or HICME segment
        FinTsSegment? orderSegment = response.BodySegments.FirstOrDefault(s => s.Code is HiccsSegmentCode or HiipbSegmentCode or HicmeSegmentCode)
                                    ?? response.UninterpretedSegments.FirstOrDefault(s => s.Code is HiccsSegmentCode or HiipbSegmentCode or HicmeSegmentCode);

        if (orderSegment is not null && orderSegment.Fields.Count > 0 && orderSegment.Fields[0].Elements.Count > 0)
        {
            bankOrderRef = orderSegment.Fields[0].Elements[0].HeaderText();
        }

        // 2. Scan for HITAN segment (SCA challenge)
        FinTsSegment? hitanSegment = response.BodySegments.FirstOrDefault(s => s.Code == HitanSegmentCode)
                                    ?? response.UninterpretedSegments.FirstOrDefault(s => s.Code == HitanSegmentCode);

        if (hitanSegment is not null)
        {
            scaDetected = true;
            // In FinTS 3.0 HKTAN/HITAN:
            // DE 1: Segmentkopf
            // DE 2: Sicherheitsfunktion (Challenge-Code)
            // DE 3: TAN-Prozess (1, 2, 3, 4, S)
            // DE 4: Auftragsreferenz (order reference)
            // DE 5: Challenge (challenge text)
            // DE 6: Challenge-HHD-UC / Matrix (optional)
            if (hitanSegment.Fields.Count >= 3 && hitanSegment.Fields[2].Elements.Count > 0)
            {
                string hitanRef = hitanSegment.Fields[2].Elements[0].HeaderText();
                if (!string.IsNullOrWhiteSpace(hitanRef))
                {
                    bankOrderRef ??= hitanRef;
                }
            }

            if (hitanSegment.Fields.Count >= 5 && hitanSegment.Fields[4].Elements.Count > 0 && !string.IsNullOrWhiteSpace(hitanSegment.Fields[4].Elements[0].HeaderText()))
            {
                challengeText = string.Join(':', hitanSegment.Fields[4].Elements.Select(e => e.HeaderText()));
            }
            else if (hitanSegment.Fields.Count >= 4 && hitanSegment.Fields[3].Elements.Count > 0)
            {
                challengeText = string.Join(':', hitanSegment.Fields[3].Elements.Select(e => e.HeaderText()));
            }

            if (hitanSegment.Fields.Count >= 6 && hitanSegment.Fields[5].Elements.Count > 0)
            {
                challengeMatrixPayload = hitanSegment.Fields[5].Elements[0].HeaderText();
            }
            else if (hitanSegment.Fields.Count >= 5 && hitanSegment.Fields[4].Elements.Count > 0 && challengeText != hitanSegment.Fields[4].Elements[0].HeaderText())
            {
                challengeMatrixPayload = hitanSegment.Fields[4].Elements[0].HeaderText();
            }
        }

        // 3. Evaluate HIRMS / HIRMG return codes
        string? primaryCode = null;
        string? primaryText = null;
        bool hasError = false;
        bool hasPending = false;

        var replySegments = response.ReplySegments
            .Where(r => r.RequestSegmentNumber == requestSegmentOrdinal || r.IsMessageLevel)
            .ToList();

        foreach (var replySegment in replySegments)
        {
            foreach (var reply in replySegment.Replies)
            {
                string code = reply.Code;
                string text = reply.Text.HeaderText();

                if (primaryCode is null)
                {
                    primaryCode = code;
                    primaryText = text;
                }

                if (code is "3905" or "3920")
                {
                    scaDetected = true;
                }
                else if (code is "0020" or "0010")
                {
                    primaryCode = code;
                    primaryText = text;
                }
                else if (code is "0030")
                {
                    hasPending = true;
                    primaryCode = code;
                    primaryText = text;
                }
                else if (reply.Class == FinTsReplyClass.Error || code.StartsWith('9'))
                {
                    hasError = true;
                    primaryCode = code;
                    primaryText = text;
                }
            }
        }

        primaryCode ??= (hasError ? "9000" : "0020");
        primaryText ??= (hasError ? "Auftrag abgelehnt." : "Auftrag entgegengenommen.");

        if (hasError)
        {
            return new FinTsSepaTransferResponseResult(
                IsSuccess: false,
                IsScaRequired: false,
                IsRejected: true,
                TargetState: PaymentState.Rejected,
                BankOrderReference: bankOrderRef,
                PrimaryReturnCode: primaryCode,
                PrimaryReturnText: primaryText);
        }

        if (scaDetected)
        {
            return new FinTsSepaTransferResponseResult(
                IsSuccess: false,
                IsScaRequired: true,
                IsRejected: false,
                TargetState: PaymentState.BankPending,
                BankOrderReference: bankOrderRef,
                PrimaryReturnCode: primaryCode,
                PrimaryReturnText: primaryText,
                ChallengeText: challengeText,
                ChallengeMatrixPayload: challengeMatrixPayload);
        }

        if (hasPending)
        {
            return new FinTsSepaTransferResponseResult(
                IsSuccess: true,
                IsScaRequired: false,
                IsRejected: false,
                TargetState: PaymentState.BankPending,
                BankOrderReference: bankOrderRef,
                PrimaryReturnCode: primaryCode,
                PrimaryReturnText: primaryText);
        }

        return new FinTsSepaTransferResponseResult(
            IsSuccess: true,
            IsScaRequired: false,
            IsRejected: false,
            TargetState: PaymentState.Accepted,
            BankOrderReference: bankOrderRef,
            PrimaryReturnCode: primaryCode,
            PrimaryReturnText: primaryText);
    }
}
