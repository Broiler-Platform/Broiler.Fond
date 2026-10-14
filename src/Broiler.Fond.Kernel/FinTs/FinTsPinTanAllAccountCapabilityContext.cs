namespace Broiler.Fond.Kernel.FinTs;

[Flags]
public enum FinTsPinTanAllAccountCapabilityIssue
{
    None = 0,
    SignatureNeedsReview = 1,
    MissingReadParameters = 2,
    MissingAdvertisement = 4,
    AdvertisementNeedsReview = 8,
    ParametersNeedReview = 16,
    SignatureRequirementsNeedReview = 32,
    OperationBlocked = 64,
    LimitsNeedReview = 128,
    RequestNeedsReview = 256,
}

/// <summary>First-read all-account capability, permission and parameter observations under PIN/TAN.
/// Pure evidence only: no authorization, credential validation, sending, account allocation or relinking.</summary>
public sealed class FinTsPinTanAllAccountCapabilityContext
{
    private FinTsPinTanAllAccountCapabilityContext(FinTsPinTanReadSignatureContext signature, FinTsReadParameterSet? readParameters,
        FinTsReadAdvertisement? advertisement, FinTsPinTanAllAccountCapabilityIssue issues)
    {
        Signature = signature;
        ReadParameters = readParameters;
        Advertisement = advertisement;
        Issues = issues;
    }

    public FinTsPinTanReadSignatureContext Signature { get; }
    public FinTsReadParameterSet? ReadParameters { get; }
    public FinTsReadAdvertisement? Advertisement { get; }
    public FinTsPinTanAllAccountCapabilityIssue Issues { get; }
    public bool HasMatchingEvidence => Issues == FinTsPinTanAllAccountCapabilityIssue.None;
    public FinTsReadAdvertisement? MatchingAdvertisement => HasMatchingEvidence ? Advertisement : null;
    public FinTsPinTanOperationEvidence OperationEvidence => HasMatchingEvidence ? Signature.OperationEvidence : FinTsPinTanOperationEvidence.Unknown;

    public static FinTsPinTanAllAccountCapabilityContext Evaluate(FinTsPinTanReadSignatureContext signature,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(signature);
        cancellationToken.ThrowIfCancellationRequested();
        var issues = signature.HasMatchingEvidence ? FinTsPinTanAllAccountCapabilityIssue.None : FinTsPinTanAllAccountCapabilityIssue.SignatureNeedsReview;
        var request = signature.Request;
        var read = request.Request;
        if (read.Source.Code != "HKSPA" || read.Source.Version != 1 || !read.AllAccounts || read.Accounts.Count != 0 || read.ContinuationToken is not null)
        {
            issues |= FinTsPinTanAllAccountCapabilityIssue.RequestNeedsReview;
        }

        var reads = signature.Initialization.ReadParameters;
        if (reads is null)
        {
            issues |= FinTsPinTanAllAccountCapabilityIssue.MissingReadParameters;
            return new(signature, null, null, issues);
        }

        var source = reads.Source;
        if (!ReferenceEquals(source, signature.Initialization.PinTan.Source))
        {
            issues |= FinTsPinTanAllAccountCapabilityIssue.ParametersNeedReview;
        }
        if (source.Bank is null || !source.Bank.ProtocolVersions.Contains(300) || source.User is null ||
            source.Source.HasErrors || source.Source.HasConflictingClasses)
        {
            issues |= FinTsPinTanAllAccountCapabilityIssue.ParametersNeedReview;
        }
        if (!signature.Initialization.HasMatchingEvidence &&
            source.Source.ReplySegments.SelectMany(s => s.Replies).Any(r => r.Meaning is not (FinTsReplyMeaning.ReceiptReported or FinTsReplyMeaning.ExecutionReported)))
        {
            issues |= FinTsPinTanAllAccountCapabilityIssue.ParametersNeedReview;
        }
        if (FinTsReadContextEvidence.Dialog(source.Source.Frame) != FinTsReadContextEvidence.Dialog(request.Frame) ||
            source.Source.Frame.MessageNumber >= request.ExpectedBankMessageNumber)
        {
            issues |= FinTsPinTanAllAccountCapabilityIssue.ParametersNeedReview;
        }

        if (source.Bank is not null)
        {
            var bankIdentity = source.Bank.Source.Fields[1].Elements;
            foreach (var account in source.Accounts)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (account.HasAccountConnection)
                {
                    var accountInstitution = account.AccountConnection.Elements.Skip(2).ToArray();
                    if (bankIdentity.Count != accountInstitution.Length || !bankIdentity.Zip(accountInstitution).All(pair =>
                        pair.First.CopyValueBytes().AsSpan().SequenceEqual(pair.Second.CopyValueBytes())))
                    {
                        issues |= FinTsPinTanAllAccountCapabilityIssue.ParametersNeedReview;
                        break;
                    }
                }
            }
        }

        var advertisements = reads.Advertisements.Where(a => a.Operation == FinTsReadOperation.SepaAccountDetails && a.Version == 1).ToList();
        if (advertisements.Count == 0)
        {
            issues |= FinTsPinTanAllAccountCapabilityIssue.MissingAdvertisement;
        }
        else if (advertisements.Count > 1)
        {
            issues |= FinTsPinTanAllAccountCapabilityIssue.AdvertisementNeedsReview;
        }

        FinTsReadAdvertisement? advertisement = advertisements.Count == 1 ? advertisements[0] : null;
        if (advertisement is not null)
        {
            if (advertisement.MaximumOrders == 0)
            {
                issues |= FinTsPinTanAllAccountCapabilityIssue.AdvertisementNeedsReview;
            }
            if (advertisement.MinimumSignatures != 1)
            {
                issues |= FinTsPinTanAllAccountCapabilityIssue.SignatureRequirementsNeedReview;
            }
        }

        if (source.User is not null)
        {
            if (source.Accounts.Count > 0 && source.Accounts.All(a => source.GetOperationEvidence(a, "HKSPA") == FinTsOperationEvidence.UnlistedBlocked))
            {
                issues |= FinTsPinTanAllAccountCapabilityIssue.OperationBlocked;
            }
            else if (source.Accounts.Count == 0 && source.User.UnlistedOperations == FinTsUnlistedOperationPolicy.Blocked)
            {
                issues |= FinTsPinTanAllAccountCapabilityIssue.OperationBlocked;
            }
            if (source.Accounts.Any(a => source.GetOperationEvidence(a, "HKSPA") == FinTsOperationEvidence.Ambiguous))
            {
                issues |= FinTsPinTanAllAccountCapabilityIssue.ParametersNeedReview;
            }
        }

        if (source.Accounts.Any(a => a.HasAccountLimit || a.Permissions.Any(p => p.Operation == "HKSPA" && p.HasLimit)))
        {
            issues |= FinTsPinTanAllAccountCapabilityIssue.LimitsNeedReview;
        }

        cancellationToken.ThrowIfCancellationRequested();
        return new(signature, reads, advertisement, issues);
    }
}
