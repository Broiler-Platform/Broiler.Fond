namespace Broiler.Fond.Kernel.FinTs;

[Flags]
public enum FinTsPinTanReadContinuationIssue
{
    None = 0, PreviousNeedsReview = 1, PartialRequired = 2, OperationMismatch = 4, TokenMismatch = 8,
    DialogueMismatch = 16, CounterMismatch = 32, AccountMismatch = 64, OptionsMismatch = 128,
    UnsupportedAccountScope = 256, RequestNeedsReview = 512, PageLimitExceeded = 1024,
}

/// <summary>Pure continuation balance context from one assembled read observation. No signature, encoding,
/// sending, SCA completion, response consumption or continuation scheduling.</summary>
public sealed class FinTsPinTanReadContinuationContext
{
    private FinTsPinTanReadContinuationContext(FinTsPinTanReadEvidence? previous, FinTsPinTanReadContinuationEvidence? previousContinuation,
        FinTsReadRequestContext request, FinTsPinTanReadContinuationIssue issues, FinTsReadContextIssue requestIssues, int pageNumber)
    {
        Previous = previous;
        PreviousContinuation = previousContinuation;
        Request = request;
        Issues = issues;
        RequestIssues = requestIssues;
        PageNumber = pageNumber;
    }
    public FinTsPinTanReadEvidence? Previous { get; }
    public FinTsPinTanReadContinuationEvidence? PreviousContinuation { get; }
    public FinTsReadRequestContext Request { get; }
    /// <summary>Exact original account/capability/procedure context; raw provenance even when this continuation needs review.</summary>
    public FinTsPinTanReadCapabilityContext Origin => Previous?.Request.Context ?? PreviousContinuation!.Request.Context.Continuation.Origin;
    public FinTsPinTanReadContinuationIssue Issues { get; }
    public FinTsReadContextIssue RequestIssues { get; }
    public bool HasMatchingEvidence => Issues == FinTsPinTanReadContinuationIssue.None;
    public FinTsPinTanReadCapabilityContext? MatchingOrigin => HasMatchingEvidence ? Origin : null;
    public FinTsReadRequestContext? MatchingRequest => HasMatchingEvidence ? Request : null;
    public int PageNumber { get; }

    public static FinTsPinTanReadContinuationContext Evaluate(FinTsPinTanReadEvidence previous, FinTsReadRequestContext request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(previous); ArgumentNullException.ThrowIfNull(request); cancellationToken.ThrowIfCancellationRequested();
        var origin = previous.Request.Context; var prior = origin.Signature.Request;
        return EvaluateCore(previous, null, previous.HasMatchingEvidence, previous.Outcome, previous.ReportedContinuationToken,
            previous.Request.DialogueId, previous.Request.MessageNumber, previous.Request.ExpectedBankMessageNumber,
            prior.Request, request, origin, 2, cancellationToken);
    }

    public static FinTsPinTanReadContinuationContext Evaluate(FinTsPinTanReadContinuationEvidence previous, FinTsReadRequestContext request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(previous); ArgumentNullException.ThrowIfNull(request); cancellationToken.ThrowIfCancellationRequested();
        var origin = previous.Request.Context.Continuation.Origin;
        var prior = previous.Request.Context.Request;
        int pageNumber = Math.Min(previous.PageNumber + 1, FinTsReadContextEvidence.MaximumPages + 1);
        return EvaluateCore(null, previous, previous.HasMatchingEvidence, previous.Outcome, previous.ReportedContinuationToken,
            previous.Request.DialogueId, previous.Request.MessageNumber, previous.Request.ExpectedBankMessageNumber,
            prior.Request, request, origin, pageNumber, cancellationToken);
    }

    private static FinTsPinTanReadContinuationContext EvaluateCore(
        FinTsPinTanReadEvidence? previousRead,
        FinTsPinTanReadContinuationEvidence? previousContinuation,
        bool previousHasMatchingEvidence,
        FinTsReadOutcomeObservation previousOutcome,
        string? previousReportedContinuationToken,
        string previousDialogueId,
        int previousMessageNumber,
        int previousExpectedBankMessageNumber,
        FinTsReadRequest before,
        FinTsReadRequestContext request,
        FinTsPinTanReadCapabilityContext origin,
        int pageNumber,
        CancellationToken cancellationToken)
    {
        var issues = FinTsPinTanReadContinuationIssue.None;
        var next = request.Request;
        if (!previousHasMatchingEvidence) { issues |= FinTsPinTanReadContinuationIssue.PreviousNeedsReview; }
        if (previousOutcome != FinTsReadOutcomeObservation.PartialReported || previousReportedContinuationToken is null)
        { issues |= FinTsPinTanReadContinuationIssue.PartialRequired; }
        if (before.Source.Code != "HKSAL" || next.Source.Code != "HKSAL" || before.Source.Version != next.Source.Version)
        { issues |= FinTsPinTanReadContinuationIssue.OperationMismatch; }
        if (next.ContinuationToken is null || next.ContinuationToken != previousReportedContinuationToken)
        { issues |= FinTsPinTanReadContinuationIssue.TokenMismatch; }
        if (FinTsReadContextEvidence.Dialog(request.Frame) != previousDialogueId)
        { issues |= FinTsPinTanReadContinuationIssue.DialogueMismatch; }
        if (request.Frame.MessageNumber != previousMessageNumber + 1 || request.ExpectedBankMessageNumber != previousExpectedBankMessageNumber + 1)
        { issues |= FinTsPinTanReadContinuationIssue.CounterMismatch; }
        if (before.AllAccounts || next.AllAccounts || before.Accounts.Count != 1 || next.Accounts.Count != 1)
        { issues |= FinTsPinTanReadContinuationIssue.UnsupportedAccountScope; }
        if (before.Accounts.Count != 1 || next.Accounts.Count != 1 || !FinTsReadContextEvidence.SameAccount(before.Accounts[0], next.Accounts[0]))
        { issues |= FinTsPinTanReadContinuationIssue.AccountMismatch; }
        if (before.AllAccounts != next.AllAccounts || before.MaximumEntries != next.MaximumEntries)
        { issues |= FinTsPinTanReadContinuationIssue.OptionsMismatch; }
        if (pageNumber > FinTsReadContextEvidence.MaximumPages)
        { issues |= FinTsPinTanReadContinuationIssue.PageLimitExceeded; }
        var requestIssues = FinTsReadContextEvidence.RequestIssues(request, origin.Capability!, origin.NationalAdvertisement);
        if (requestIssues != FinTsReadContextIssue.None) { issues |= FinTsPinTanReadContinuationIssue.RequestNeedsReview; }
        cancellationToken.ThrowIfCancellationRequested();
        return new(previousRead, previousContinuation, request, issues, requestIssues, pageNumber);
    }
}
