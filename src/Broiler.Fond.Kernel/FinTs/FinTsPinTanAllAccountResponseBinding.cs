using System.Collections.ObjectModel;

namespace Broiler.Fond.Kernel.FinTs;

/// <summary>Pure all-account discovery message/profile/reference comparison. Matching references establish no execution,
/// account identity, payload validity, envelope identity, authentication, consumption or replay protection.</summary>
public sealed class FinTsPinTanAllAccountResponseBinding
{
    private FinTsPinTanAllAccountResponseBinding(FinTsPinTanAllAccountRequestBinding request, FinTsResponse response,
        List<FinTsPinTanResponseSegmentBinding> references, FinTsPinTanResponseBindingIssue issues)
    {
        Request = request;
        Response = response;
        References = references.AsReadOnly();
        Issues = issues;
    }

    public FinTsPinTanAllAccountRequestBinding Request { get; }
    public FinTsResponse Response { get; }
    public ReadOnlyCollection<FinTsPinTanResponseSegmentBinding> References { get; }
    public FinTsPinTanResponseBindingIssue Issues { get; }
    public bool HasMatchingReferences => Issues == FinTsPinTanResponseBindingIssue.None;

    public static FinTsPinTanAllAccountResponseBinding Evaluate(FinTsPinTanAllAccountRequestBinding request, FinTsResponse response,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(response);
        cancellationToken.ThrowIfCancellationRequested();

        var issues = FinTsPinTanResponseBindingIssue.None;
        if (response.PinTanEnvelope is not { } envelope)
        {
            issues |= FinTsPinTanResponseBindingIssue.MissingEnvelope;
        }
        else if (envelope.ProfileVersion != request.ProfileVersion)
        {
            issues |= FinTsPinTanResponseBindingIssue.ProfileMismatch;
        }

        var header = response.Frame.Syntax.Segments[0].Fields;
        if (response.Frame.MessageNumber != request.ExpectedBankMessageNumber ||
            header[2].Elements[0].HeaderText() != request.DialogueId ||
            header.Count != 5 ||
            header[4].Elements.Count != 2 ||
            header[4].Elements[0].HeaderText() != request.DialogueId ||
            FinTsSyntax.Number(header[4].Elements[1], 4, false) != request.MessageNumber)
        {
            issues |= FinTsPinTanResponseBindingIssue.MessageMismatch;
        }

        List<FinTsPinTanResponseSegmentBinding> references = [];
        foreach (var segment in response.BodySegments)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (segment.Code == "HIRMG") { continue; }

            var target = request.Segments.SingleOrDefault(s => s.Number == segment.Reference);
            references.Add(new(segment, target));

            if (segment.Reference is null)
            {
                issues |= FinTsPinTanResponseBindingIssue.MissingSegmentReference;
            }
            else if (target is null)
            {
                issues |= FinTsPinTanResponseBindingIssue.UnknownSegmentReference;
            }

            // A security/framing reply retains its real role even when its code reports success.
            if (segment.Code == "HIRMS") { continue; }

            if ((segment.Code, segment.Version) is not (("HISPA", 1) or ("HISAL", 6 or 7 or 8)))
            {
                issues |= FinTsPinTanResponseBindingIssue.UninterpretedData;
            }

            if (segment.Code is "HISPA" or "HISAL" && (segment.Code != "HISPA" || segment.Reference != request.ReadNumber))
            {
                issues |= FinTsPinTanResponseBindingIssue.SegmentRoleMismatch;
            }
        }

        cancellationToken.ThrowIfCancellationRequested();
        return new(request, response, references, issues);
    }
}
