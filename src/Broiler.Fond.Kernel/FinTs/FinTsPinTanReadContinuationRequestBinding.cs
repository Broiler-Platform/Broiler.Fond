using System.Collections.ObjectModel;

namespace Broiler.Fond.Kernel.FinTs;

/// <summary>Credential-free metadata for one second-page balance PIN-only envelope candidate, not proof of encoding, sending or SCA readiness.</summary>
public sealed class FinTsPinTanReadContinuationRequestBinding
{
    private FinTsPinTanReadContinuationRequestBinding(FinTsPinTanReadContinuationSignatureContext context)
    {
        Context = context;
        var read = context.Request.Request.Source;
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
    public FinTsPinTanReadContinuationSignatureContext Context { get; }
    public ReadOnlyCollection<FinTsPinTanRequestSegmentBinding> Segments { get; }
    public string DialogueId => FinTsReadContextEvidence.Dialog(Context.Request.Frame);
    public int MessageNumber => Context.Request.Frame.MessageNumber;
    public int ExpectedBankMessageNumber => Context.Request.ExpectedBankMessageNumber;
    public int ProfileVersion => Context.Header.ProfileVersion;
    public string ExpectedUserId => Context.Header.UserId;
    public int ReadNumber => 3;

    public static FinTsPinTanReadContinuationRequestBinding ForEnvelopeCandidate(FinTsPinTanReadContinuationSignatureContext context, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(context); cancellationToken.ThrowIfCancellationRequested();
        if (!context.HasMatchingEvidence) { throw new FinTsFormatException(FinTsSyntaxError.InvalidSignatureContext); }
        return new(context);
    }
}
