namespace Broiler.Fond.Kernel.FinTs;

/// <summary>Pure second-page balance response observations. No authentication, credential/SCA completion, replay consumption,
/// continuation scheduling, account ingestion or session activation.</summary>
public sealed class FinTsPinTanReadContinuationEvidence
{
    private FinTsPinTanReadContinuationEvidence(FinTsPinTanReadContinuationResponseBinding binding, FinTsReadDataSet data, FinTsPinTanReadIssue issues,
        FinTsReadContextIssue readIssues, FinTsReadOutcomeObservation outcome, string? continuation)
    { Binding = binding; ReadData = data; Issues = issues; ReadIssues = readIssues; Outcome = issues == FinTsPinTanReadIssue.None ? outcome : FinTsReadOutcomeObservation.NeedsReview; ReportedContinuationToken = continuation; }
    public FinTsPinTanReadContinuationResponseBinding Binding { get; }
    public FinTsPinTanReadContinuationRequestBinding Request => Binding.Request;
    public FinTsResponse Response => Binding.Response;
    public int PageNumber => Request.Context.Continuation.PageNumber;
    /// <summary>Caller-supplied raw evidence, including on a source mismatch. Inspect Issues before using it.</summary>
    public FinTsReadDataSet ReadData { get; }
    public FinTsPinTanReadIssue Issues { get; }
    public FinTsReadContextIssue ReadIssues { get; }
    public bool HasMatchingEvidence => Issues == FinTsPinTanReadIssue.None;
    public FinTsReadDataSet? MatchingReadData => HasMatchingEvidence ? ReadData : null;
    public FinTsReadOutcomeObservation Outcome { get; }
    /// <summary>Raw same-source token, even on a result needing review. Never permission to continue.</summary>
    public string? ReportedContinuationToken { get; }

    public static FinTsPinTanReadContinuationEvidence Evaluate(FinTsPinTanReadContinuationResponseBinding binding, FinTsReadDataSet data,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(binding); ArgumentNullException.ThrowIfNull(data); cancellationToken.ThrowIfCancellationRequested();
        var issues = binding.HasMatchingReferences ? FinTsPinTanReadIssue.None : FinTsPinTanReadIssue.BindingNeedsReview;
        if (!ReferenceEquals(data.Source, binding.Response))
        { return new(binding, data, issues | FinTsPinTanReadIssue.ReadSourceMismatch, FinTsReadContextIssue.None, FinTsReadOutcomeObservation.NeedsReview, null); }
        var context = binding.Request.Context; var expected = context.Header;
        if (binding.Response.PinTanEnvelope is { } envelope)
        {
            var key = envelope.SecurityHeader.Fields[6].Elements;
            if (envelope.SystemId.HeaderText() != expected.SystemId || key[0].HeaderText() != expected.CountryCode ||
                key[1].HeaderText() != expected.InstitutionId || key[2].HeaderText() != expected.UserId)
            { issues |= FinTsPinTanReadIssue.EnvelopeIdentityMismatch; }
        }
        var read = context.Request.Request;
        var readIssues = FinTsReadContextEvidence.StatusIssues(read, data, binding.Request.ReadNumber,
            out bool unavailable, out string? continuation, out var outcome, cancellationToken);
        // Correctly mapped security/framing replies still need interpretation; they cannot supply read completion.
        if (binding.Response.ReplySegments.Any(s => !s.IsMessageLevel && s.RequestSegmentNumber != binding.Request.ReadNumber))
        { readIssues |= FinTsReadContextIssue.StatusNeedsReview; }
        readIssues |= FinTsReadContextEvidence.DataIssues(read, data, context.Continuation.Origin.Capability!, unavailable, cancellationToken);
        if (readIssues != FinTsReadContextIssue.None) { issues |= FinTsPinTanReadIssue.ReadNeedsReview; }
        cancellationToken.ThrowIfCancellationRequested();
        return new(binding, data, issues, readIssues, outcome, continuation);
    }
}
