namespace Broiler.Fond.Kernel.FinTs;

[Flags]
public enum FinTsPinTanReadCapabilityIssue
{
    None = 0, SignatureNeedsReview = 1, MissingReadParameters = 2, AccountSourceMismatch = 4,
    CapabilityNeedsReview = 8, RequestNeedsReview = 16, SignatureRequirementsNeedReview = 32, LimitsNeedReview = 64,
}

/// <summary>First-read single-account capability and permission observations. No authorization, credential validation or sending.</summary>
public sealed class FinTsPinTanReadCapabilityContext
{
    private FinTsPinTanReadCapabilityContext(FinTsPinTanReadSignatureContext signature, FinTsAccountParameters account,
        FinTsReadAdvertisement? national, FinTsReadCapabilityEvidence? capability, FinTsPinTanReadCapabilityIssue issues, FinTsReadContextIssue requestIssues)
    { Signature = signature; Account = account; NationalAdvertisement = national; Capability = capability; Issues = issues; RequestIssues = requestIssues; }
    public FinTsPinTanReadSignatureContext Signature { get; }
    public FinTsAccountParameters Account { get; }
    public FinTsReadAdvertisement? NationalAdvertisement { get; }
    /// <summary>Component evidence, if the selected account belongs to the exact returned source. Inspect the outer result for qualification.</summary>
    public FinTsReadCapabilityEvidence? Capability { get; }
    public FinTsPinTanReadCapabilityIssue Issues { get; }
    public FinTsReadContextIssue RequestIssues { get; }
    public bool HasMatchingEvidence => Issues == FinTsPinTanReadCapabilityIssue.None;
    public FinTsAccountParameters? MatchingAccount => HasMatchingEvidence ? Account : null;
    public FinTsReadAdvertisement? MatchingAdvertisement => HasMatchingEvidence ? Capability!.Candidates.Single() : null;
    public FinTsPinTanOperationEvidence OperationEvidence => HasMatchingEvidence ? Signature.OperationEvidence : FinTsPinTanOperationEvidence.Unknown;

    public static FinTsPinTanReadCapabilityContext Evaluate(FinTsPinTanReadSignatureContext signature, FinTsAccountParameters account,
        FinTsReadAdvertisement? nationalAdvertisement = null, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(signature); ArgumentNullException.ThrowIfNull(account); cancellationToken.ThrowIfCancellationRequested();
        var issues = signature.HasMatchingEvidence ? FinTsPinTanReadCapabilityIssue.None : FinTsPinTanReadCapabilityIssue.SignatureNeedsReview;
        var reads = signature.Initialization.ReadParameters;
        if (reads is null) { issues |= FinTsPinTanReadCapabilityIssue.MissingReadParameters; }
        else if (!reads.Source.Accounts.Contains(account)) { issues |= FinTsPinTanReadCapabilityIssue.AccountSourceMismatch; }
        if (reads is null || (issues & FinTsPinTanReadCapabilityIssue.AccountSourceMismatch) != 0)
        { return new(signature, account, nationalAdvertisement, null, issues, FinTsReadContextIssue.None); }
        var request = signature.Request;
        var operation = request.Request.Source.Code == "HKSPA" ? FinTsReadOperation.SepaAccountDetails : FinTsReadOperation.Balance;
        var capability = FinTsReadCapabilityEvidence.EvaluateCore(reads, account, operation, request.Request.Source.Version, signature.Initialization, cancellationToken);
        if (account.HasAccountLimit || account.Permissions.Any(p => p.Operation == request.Request.Source.Code && p.HasLimit))
        { issues |= FinTsPinTanReadCapabilityIssue.LimitsNeedReview; }
        if (!capability.HasMatchingEvidence) { issues |= FinTsPinTanReadCapabilityIssue.CapabilityNeedsReview; }
        // Missing signature requirements cannot be inferred from an absent permission; this path supports exactly one customer signature.
        if (capability.MinimumCustomerSignatures != 1) { issues |= FinTsPinTanReadCapabilityIssue.SignatureRequirementsNeedReview; }
        var requestIssues = FinTsReadContextEvidence.RequestIssues(request, capability, nationalAdvertisement);
        if (requestIssues != FinTsReadContextIssue.None) { issues |= FinTsPinTanReadCapabilityIssue.RequestNeedsReview; }
        cancellationToken.ThrowIfCancellationRequested();
        return new(signature, account, nationalAdvertisement, capability, issues, requestIssues);
    }
}
