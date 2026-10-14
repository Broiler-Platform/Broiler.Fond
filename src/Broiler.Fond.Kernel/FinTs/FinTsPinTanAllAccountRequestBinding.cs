using System.Collections.ObjectModel;

namespace Broiler.Fond.Kernel.FinTs;

/// <summary>Credential-free metadata for one all-account discovery PIN-only envelope candidate, not proof of encoding, sending or SCA readiness.</summary>
public sealed class FinTsPinTanAllAccountRequestBinding
{
    private FinTsPinTanAllAccountRequestBinding(FinTsPinTanAllAccountCapabilityContext context)
    {
        Context = context;
        var read = context.Signature.Request.Request.Source;
        List<FinTsPinTanRequestSegmentBinding> segments = [
            new("HNHBK", 1, 3, FinTsPinTanRequestSegmentRole.MessageHeader),
            new("HNVSK", 998, 3, FinTsPinTanRequestSegmentRole.EnvelopeHeader),
            new("HNVSD", 999, 1, FinTsPinTanRequestSegmentRole.EnvelopeData),
            new("HNSHK", 2, 4, FinTsPinTanRequestSegmentRole.SignatureHeader),
            new(read.Code, 3, read.Version, FinTsPinTanRequestSegmentRole.ReadOperation),
            new("HNSHA", 4, 2, FinTsPinTanRequestSegmentRole.SignatureTrailer),
            new("HNHBS", 5, 1, FinTsPinTanRequestSegmentRole.MessageTrailer),
        ];
        Segments = segments.AsReadOnly();
    }

    public FinTsPinTanAllAccountCapabilityContext Context { get; }
    public ReadOnlyCollection<FinTsPinTanRequestSegmentBinding> Segments { get; }
    public string DialogueId => FinTsReadContextEvidence.Dialog(Context.Signature.Request.Frame);
    public int MessageNumber => Context.Signature.Request.Frame.MessageNumber;
    public int ExpectedBankMessageNumber => Context.Signature.Request.ExpectedBankMessageNumber;
    public int ProfileVersion => Context.Signature.Header.ProfileVersion;
    public string ExpectedUserId => Context.Signature.Header.UserId;
    public int ReadNumber => 3;

    public static FinTsPinTanAllAccountRequestBinding ForEnvelopeCandidate(FinTsPinTanAllAccountCapabilityContext context, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(context);
        cancellationToken.ThrowIfCancellationRequested();
        if (!context.HasMatchingEvidence)
        {
            throw new FinTsFormatException(FinTsSyntaxError.InvalidSignatureContext);
        }
        return new(context);
    }
}
