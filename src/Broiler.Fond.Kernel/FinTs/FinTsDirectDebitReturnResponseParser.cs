using Broiler.Fond.Kernel.Domain.Payments;

namespace Broiler.Fond.Kernel.FinTs;

/// <summary>
/// Parsed outcome of a FinTS SEPA direct-debit return response or mandate revocation response.
/// </summary>
public sealed record FinTsDirectDebitReturnResponseResult(
    bool IsSuccess,
    bool IsScaRequired,
    bool IsRejected,
    DirectDebitReturnStatus TargetStatus,
    string? ConfirmationReference,
    string PrimaryReturnCode,
    string PrimaryReturnText,
    string? ChallengeText = null,
    string? ChallengeMatrixPayload = null);

/// <summary>
/// Parses FinTS responses to HKDRW (Lastschriftrückbuchung) and HKDMB (Mandatsverwaltung).
/// </summary>
public static class FinTsDirectDebitReturnResponseParser
{
    public const string HidrwSegmentCode = "HIDRW";
    public const string HidmbSegmentCode = "HIDMB";
    public const string HitanSegmentCode = "HITAN";

    public static FinTsDirectDebitReturnResponseResult Parse(
        FinTsResponse response,
        int requestSegmentOrdinal = 2)
    {
        ArgumentNullException.ThrowIfNull(response);

        string? confirmationRef = null;
        string? challengeText = null;
        string? challengeMatrixPayload = null;
        bool scaDetected = false;

        // 1. Scan for HIDRW or HIDMB segment
        FinTsSegment? confirmSegment = response.BodySegments.FirstOrDefault(s => s.Code is HidrwSegmentCode or HidmbSegmentCode);
        if (confirmSegment is not null && confirmSegment.Fields.Count > 0 && confirmSegment.Fields[0].Elements.Count > 0)
        {
            confirmationRef = confirmSegment.Fields[0].Elements[0].HeaderText();
        }

        // 2. Scan for HITAN segment (SCA challenge)
        FinTsSegment? hitanSegment = response.BodySegments.FirstOrDefault(s => s.Code == HitanSegmentCode);
        if (hitanSegment is not null)
        {
            scaDetected = true;
            if (hitanSegment.Fields.Count > 3 && hitanSegment.Fields[3].Elements.Count > 0)
            {
                challengeText = hitanSegment.Fields[3].Elements[0].HeaderText();
            }

            if (hitanSegment.Fields.Count > 4 && hitanSegment.Fields[4].Elements.Count > 0)
            {
                challengeMatrixPayload = hitanSegment.Fields[4].Elements[0].HeaderText();
            }
        }

        // 3. Inspect replies
        var requestReplies = response.ReplySegments
            .Where(r => r.RequestSegmentNumber == requestSegmentOrdinal)
            .SelectMany(r => r.Replies)
            .ToList();

        var messageReplies = response.ReplySegments
            .Where(r => r.IsMessageLevel)
            .SelectMany(r => r.Replies)
            .ToList();

        var allReplies = requestReplies.Concat(messageReplies).ToList();

        bool hasError = allReplies.Any(r => r.Class == FinTsReplyClass.Error);
        bool hasSuccess = allReplies.Any(r => r.Class == FinTsReplyClass.Success);
        bool hasPendingAuth = allReplies.Any(r => r.Meaning == FinTsReplyMeaning.AuthorizationPending);

        string primaryCode = allReplies.FirstOrDefault()?.Code ?? "0000";
        string primaryText = allReplies.FirstOrDefault()?.Text.HeaderText() ?? "No return text.";

        if (hasError)
        {
            return new FinTsDirectDebitReturnResponseResult(
                IsSuccess: false,
                IsScaRequired: false,
                IsRejected: true,
                TargetStatus: DirectDebitReturnStatus.Rejected,
                ConfirmationReference: confirmationRef,
                PrimaryReturnCode: primaryCode,
                PrimaryReturnText: primaryText);
        }

        if (hasPendingAuth || scaDetected)
        {
            return new FinTsDirectDebitReturnResponseResult(
                IsSuccess: false,
                IsScaRequired: true,
                IsRejected: false,
                TargetStatus: DirectDebitReturnStatus.PendingSubmission,
                ConfirmationReference: confirmationRef,
                PrimaryReturnCode: primaryCode,
                PrimaryReturnText: primaryText,
                ChallengeText: challengeText,
                ChallengeMatrixPayload: challengeMatrixPayload);
        }

        if (hasSuccess)
        {
            return new FinTsDirectDebitReturnResponseResult(
                IsSuccess: true,
                IsScaRequired: false,
                IsRejected: false,
                TargetStatus: DirectDebitReturnStatus.SubmittedAwaitingStatement,
                ConfirmationReference: confirmationRef,
                PrimaryReturnCode: primaryCode,
                PrimaryReturnText: primaryText);
        }

        return new FinTsDirectDebitReturnResponseResult(
            IsSuccess: false,
            IsScaRequired: false,
            IsRejected: false,
            TargetStatus: DirectDebitReturnStatus.Unknown,
            ConfirmationReference: confirmationRef,
            PrimaryReturnCode: primaryCode,
            PrimaryReturnText: primaryText);
    }
}
