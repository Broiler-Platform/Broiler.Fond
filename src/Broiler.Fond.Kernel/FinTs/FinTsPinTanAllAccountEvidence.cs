using System.Collections.ObjectModel;

namespace Broiler.Fond.Kernel.FinTs;

[Flags]
public enum FinTsPinTanAllAccountIssue
{
    None = 0,
    BindingNeedsReview = 1,
    ReadSourceMismatch = 2,
    EnvelopeIdentityMismatch = 4,
    ResponseNeedsReview = 8,
    AccountNeedsReview = 16,
    UnmatchedUserAccounts = 32,
}

/// <summary>Pure all-account discovery response observations. No authentication, credential/SCA completion, replay consumption,
/// account ingestion or session activation.</summary>
public sealed class FinTsPinTanAllAccountEvidence
{
    private FinTsPinTanAllAccountEvidence(
        FinTsPinTanAllAccountResponseBinding binding,
        FinTsReadDataSet data,
        FinTsPinTanAllAccountIssue issues,
        FinTsReadContextIssue responseIssues,
        FinTsReadOutcomeObservation outcome,
        List<FinTsDiscoveryAccountEvidence> accounts,
        List<FinTsAccountParameters> unmatched)
    {
        Binding = binding;
        ReadData = data;
        Issues = issues;
        ResponseIssues = responseIssues;
        Outcome = issues == FinTsPinTanAllAccountIssue.None ? outcome : FinTsReadOutcomeObservation.NeedsReview;
        Accounts = accounts.AsReadOnly();
        UnmatchedUserAccounts = unmatched.AsReadOnly();
    }

    public FinTsPinTanAllAccountResponseBinding Binding { get; }
    public FinTsPinTanAllAccountRequestBinding Request => Binding.Request;
    public FinTsResponse Response => Binding.Response;
    /// <summary>Caller-supplied raw evidence, including on a source mismatch. Inspect Issues before using it.</summary>
    public FinTsReadDataSet ReadData { get; }
    public FinTsPinTanAllAccountIssue Issues { get; }
    public FinTsReadContextIssue ResponseIssues { get; }
    public bool HasMatchingEvidence => Issues == FinTsPinTanAllAccountIssue.None;
    public FinTsReadDataSet? MatchingReadData => HasMatchingEvidence ? ReadData : null;
    public ReadOnlyCollection<FinTsDiscoveryAccountEvidence> Accounts { get; }
    /// <summary>UPD entries with no clean returned match. Absence never proves closure or revocation.</summary>
    public ReadOnlyCollection<FinTsAccountParameters> UnmatchedUserAccounts { get; }
    public FinTsReadOutcomeObservation Outcome { get; }

    public static FinTsPinTanAllAccountEvidence Evaluate(
        FinTsPinTanAllAccountResponseBinding binding,
        FinTsReadDataSet data,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(binding);
        ArgumentNullException.ThrowIfNull(data);
        cancellationToken.ThrowIfCancellationRequested();

        var issues = binding.HasMatchingReferences ? FinTsPinTanAllAccountIssue.None : FinTsPinTanAllAccountIssue.BindingNeedsReview;
        if (!ReferenceEquals(data.Source, binding.Response))
        {
            return new(binding, data, issues | FinTsPinTanAllAccountIssue.ReadSourceMismatch,
                FinTsReadContextIssue.None, FinTsReadOutcomeObservation.NeedsReview, [], []);
        }

        var context = binding.Request.Context;
        var expected = context.Signature.Header;
        if (binding.Response.PinTanEnvelope is { } envelope)
        {
            var key = envelope.SecurityHeader.Fields[6].Elements;
            if (envelope.SystemId.HeaderText() != expected.SystemId ||
                key[0].HeaderText() != expected.CountryCode ||
                key[1].HeaderText() != expected.InstitutionId ||
                key[2].HeaderText() != expected.UserId)
            {
                issues |= FinTsPinTanAllAccountIssue.EnvelopeIdentityMismatch;
            }
        }

        var read = context.Signature.Request.Request;
        var responseIssues = FinTsReadContextEvidence.StatusIssues(
            read,
            data,
            binding.Request.ReadNumber,
            out bool unavailable,
            out _,
            out var outcome,
            cancellationToken);

        if (binding.Response.ReplySegments.Any(s => !s.IsMessageLevel && s.RequestSegmentNumber != binding.Request.ReadNumber))
        {
            responseIssues |= FinTsReadContextIssue.StatusNeedsReview;
        }

        if (data.UninterpretedSegments.Count != 0 || data.Balances.Count != 0)
        {
            responseIssues |= FinTsReadContextIssue.UnexpectedReport;
        }

        if (data.Discovery.Count > 1)
        {
            responseIssues |= FinTsReadContextIssue.DuplicateReport;
        }

        if (data.Discovery.Count == 0 && !unavailable)
        {
            responseIssues |= FinTsReadContextIssue.MissingReport;
        }

        var returned = new List<FinTsReadAccount>();
        foreach (var report in data.Discovery)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (report.Source.Version != 1)
            {
                responseIssues |= FinTsReadContextIssue.VersionMismatch;
            }

            if (report.Accounts.Count == 0 && !unavailable)
            {
                responseIssues |= FinTsReadContextIssue.MissingReport;
            }

            returned.AddRange(report.Accounts);
        }

        FinTsAllAccountDiscoveryEvidence.MatchAccounts(
            context.ReadParameters!,
            context.Advertisement,
            returned,
            unavailable,
            out var matches,
            out var unmatched,
            out bool hasAccountIssues,
            cancellationToken);

        if (hasAccountIssues)
        {
            issues |= FinTsPinTanAllAccountIssue.AccountNeedsReview;
        }

        if (unmatched.Count != 0 && !unavailable)
        {
            issues |= FinTsPinTanAllAccountIssue.UnmatchedUserAccounts;
        }

        if (responseIssues != FinTsReadContextIssue.None)
        {
            issues |= FinTsPinTanAllAccountIssue.ResponseNeedsReview;
        }

        cancellationToken.ThrowIfCancellationRequested();
        return new(binding, data, issues, responseIssues, outcome, matches, unmatched);
    }
}
