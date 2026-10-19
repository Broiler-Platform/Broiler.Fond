using Broiler.Fond.Kernel.Domain.Payments;

namespace Broiler.Fond.Kernel.FinTs;

/// <summary>
/// Parses HIVOP response segments and correlated HIRMS return codes to produce
/// a typed <see cref="VerificationOfPayeeResult"/> per Regulation (EU) 2024/886.
/// </summary>
public static class FinTsVopResponseParser
{
    public const string VopSegmentCode = "HIVOP";

    /// <summary>
    /// Parses an authoritative VoP response from a decoded FinTsResponse.
    /// </summary>
    public static VerificationOfPayeeResult Parse(
        FinTsResponse response,
        string suppliedName,
        int? requestSegmentOrdinal = 2,
        DateTimeOffset? timestamp = null)
    {
        ArgumentNullException.ThrowIfNull(response);
        ArgumentException.ThrowIfNullOrWhiteSpace(suppliedName);

        DateTimeOffset checkedAt = timestamp ?? DateTimeOffset.UtcNow;

        // 1. Check for dedicated HIVOP segment in body or uninterpreted segments
        FinTsSegment? vopSegment = response.BodySegments.FirstOrDefault(s => s.Code == VopSegmentCode)
                                  ?? response.UninterpretedSegments.FirstOrDefault(s => s.Code == VopSegmentCode);

        if (vopSegment is not null)
        {
            return ParseVopSegment(vopSegment, suppliedName, checkedAt);
        }

        // 2. If no HIVOP segment, check HIRMS segment replies correlated to the HKVOP request ordinal
        if (requestSegmentOrdinal.HasValue)
        {
            var replySegment = response.ReplySegments.FirstOrDefault(r => r.RequestSegmentNumber == requestSegmentOrdinal.Value);
            if (replySegment is not null)
            {
                foreach (var reply in replySegment.Replies)
                {
                    switch (reply.Code)
                    {
                        case "3070": // Match
                            return VerificationOfPayeeResult.CreateMatch(suppliedName, checkedAt, rawBankCode: reply.Code);

                        case "3071": // Close Match
                            string? suggested = reply.Parameters.Count > 0
                                ? reply.Parameters[0].HeaderText()
                                : null;
                            string warning = reply.Text.HeaderText();
                            return VerificationOfPayeeResult.CreateCloseMatch(suppliedName, suggested, warning, checkedAt, rawBankCode: reply.Code);

                        case "3072": // No Match
                            return VerificationOfPayeeResult.CreateNoMatch(suppliedName, reply.Text.HeaderText(), checkedAt, rawBankCode: reply.Code);

                        case "3073": // Unavailable
                        case "9050": // Temporarily unavailable
                        case "9010":
                            return VerificationOfPayeeResult.CreateUnavailable(suppliedName, reply.Text.HeaderText(), checkedAt, rawBankCode: reply.Code);
                    }
                }
            }
        }

        // 3. Fallback: check if message-level replies indicate service unavailability
        var messageReply = response.ReplySegments.FirstOrDefault(r => r.IsMessageLevel);
        if (messageReply is not null)
        {
            var errReply = messageReply.Replies.FirstOrDefault(r => r.Class == FinTsReplyClass.Error || r.Class == FinTsReplyClass.Warning);
            if (errReply is not null)
            {
                return VerificationOfPayeeResult.CreateUnavailable(suppliedName, errReply.Text.HeaderText(), checkedAt, rawBankCode: errReply.Code);
            }
        }

        return VerificationOfPayeeResult.CreateUnavailable(suppliedName, "No payee verification response received from bank.", checkedAt);
    }

    private static VerificationOfPayeeResult ParseVopSegment(FinTsSegment segment, string suppliedName, DateTimeOffset checkedAt)
    {
        if (segment.Version is not (1 or 2))
        {
            throw new FinTsFormatException(FinTsSyntaxError.UnsupportedResponseVersion);
        }

        // HIVOP structure:
        // Field 0: Origin account DE
        // Field 1: Result DE: [Code]:[SuggestedName]:[WarningText]:[Ref]
        if (segment.Fields.Count < 2)
        {
            throw new FinTsFormatException(FinTsSyntaxError.InvalidResponse);
        }

        FinTsField resultField = segment.Fields[1];
        if (resultField.Elements.Count == 0)
        {
            throw new FinTsFormatException(FinTsSyntaxError.InvalidResponse);
        }

        string rawCode = resultField.Elements[0].HeaderText().Trim();
        string? suggestedName = resultField.Elements.Count > 1 && !string.IsNullOrWhiteSpace(resultField.Elements[1].HeaderText())
            ? resultField.Elements[1].HeaderText().Trim()
            : null;

        string? warningText = resultField.Elements.Count > 2 && !string.IsNullOrWhiteSpace(resultField.Elements[2].HeaderText())
            ? resultField.Elements[2].HeaderText().Trim()
            : null;

        return rawCode.ToUpperInvariant() switch
        {
            "1" or "MATCH" or "M" =>
                VerificationOfPayeeResult.CreateMatch(suppliedName, checkedAt, rawBankCode: rawCode),

            "2" or "CLOSE_MATCH" or "C" =>
                VerificationOfPayeeResult.CreateCloseMatch(suppliedName, suggestedName, warningText, checkedAt, rawBankCode: rawCode),

            "3" or "NO_MATCH" or "N" =>
                VerificationOfPayeeResult.CreateNoMatch(suppliedName, warningText ?? "Payee name does not match the account holder for this IBAN.", checkedAt, rawBankCode: rawCode),

            "4" or "UNAVAILABLE" or "U" =>
                VerificationOfPayeeResult.CreateUnavailable(suppliedName, warningText ?? "Verification of payee is currently unavailable.", checkedAt, rawBankCode: rawCode),

            _ =>
                VerificationOfPayeeResult.CreateUnavailable(suppliedName, $"Unrecognized VoP result indicator '{rawCode}'.", checkedAt, rawBankCode: rawCode)
        };
    }
}
