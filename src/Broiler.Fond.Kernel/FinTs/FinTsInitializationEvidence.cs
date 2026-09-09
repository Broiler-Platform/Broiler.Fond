namespace Broiler.Fond.Kernel.FinTs;

[Flags]
public enum FinTsInitializationIssue
{
    None = 0, MessageMismatch = 1, InvalidAssignedDialogue = 2, ReferenceMismatch = 4,
    ProfileNeedsReview = 8, ResponseNeedsReview = 16, StatusNeedsReview = 32,
    MissingBankParameters = 64, MissingUserParameters = 128, UserContextMissing = 256,
    InstitutionMismatch = 512, UserMismatch = 1024, CustomerMismatch = 2048,
    AccountInstitutionMismatch = 4096, ProtocolNotAdvertised = 8192, LanguageNotAdvertised = 16384,
    UninterpretedParameters = 32768,
}

public enum FinTsInitializationOutcome { NeedsReview, ExecutionReported }

/// <summary>
/// Pure comparison of one unsigned initialization and parameters from one response.
/// No authentication, session creation, cache reuse, response consumption or capability activation.
/// </summary>
public sealed class FinTsInitializationEvidence
{
    private FinTsInitializationEvidence(FinTsUnsignedInitializationRequest request, FinTsParameterSet parameters,
        string? expectedUserId, string dialogue, FinTsInitializationIssue issues)
    { Request = request; Parameters = parameters; ExpectedUserId = expectedUserId; ReportedDialogueId = dialogue; Issues = issues; }
    public FinTsUnsignedInitializationRequest Request { get; }
    public FinTsParameterSet Parameters { get; }
    public FinTsResponse Response => Parameters.Source;
    /// <summary>Explicit caller expectation, distinct from the identification segment's customer ID.</summary>
    public string? ExpectedUserId { get; }
    /// <summary>Raw reported dialogue ID, including on review outcomes. Never an activated session identifier.</summary>
    public string ReportedDialogueId { get; }
    public FinTsInitializationIssue Issues { get; }
    public bool HasMatchingEvidence => Issues == FinTsInitializationIssue.None;
    public FinTsInitializationOutcome Outcome => HasMatchingEvidence ? FinTsInitializationOutcome.ExecutionReported : FinTsInitializationOutcome.NeedsReview;
    public bool? BankVersionChanged => Parameters.Bank is { } bank ? bank.Version != Request.Preparation.BankParameterVersion : null;
    public bool? UserVersionChanged => Parameters.User is { } user ? user.Version != Request.Preparation.UserParameterVersion : null;

    public static FinTsInitializationEvidence Evaluate(FinTsUnsignedInitializationRequest request, FinTsParameterSet parameters,
        string? expectedUserId = null, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request); ArgumentNullException.ThrowIfNull(parameters);
        cancellationToken.ThrowIfCancellationRequested();
        var issues = RequestIssues(request, expectedUserId);
        var response = parameters.Source; var identity = request.Identification; var preparation = request.Preparation;
        issues |= ResponseScopeIssues(request, response, out string dialogue, cancellationToken);
        if (response.PinTanEnvelope is not null) { issues |= FinTsInitializationIssue.ProfileNeedsReview; }
        if (response.HasErrors || response.HasConflictingClasses || response.HasIndeterminateProcessing) { issues |= FinTsInitializationIssue.ResponseNeedsReview; }

        bool messageExecution = false, identityExecution = false, preparationExecution = false;
        foreach (var segment in response.ReplySegments)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var seen = new HashSet<string>(StringComparer.Ordinal);
            foreach (var reply in segment.Replies)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (!seen.Add(reply.Code) || !reply.ElementReference.IsEmpty || reply.Parameters.Any(p => !p.IsEmpty)) { issues |= FinTsInitializationIssue.StatusNeedsReview; }
                bool supported = segment.IsMessageLevel ? reply.Code is "0010" or "0020" : reply.Code == "0020";
                // 3050 is only a scoped update observation; text never decides whether BPD or UPD changed.
                if (!segment.IsMessageLevel && segment.RequestSegmentNumber == preparation.Source.Number && reply.Code == "3050" &&
                    (parameters.Bank is not null || parameters.User is not null)) { supported = true; }
                if (!supported) { issues |= FinTsInitializationIssue.StatusNeedsReview; }
                if (reply.Code != "0020") { continue; }
                if (segment.IsMessageLevel) { messageExecution = true; }
                else if (segment.RequestSegmentNumber == identity.Source.Number) { identityExecution = true; }
                else if (segment.RequestSegmentNumber == preparation.Source.Number) { preparationExecution = true; }
            }
        }
        if (!messageExecution && !(identityExecution && preparationExecution)) { issues |= FinTsInitializationIssue.StatusNeedsReview; }

        issues |= ParameterIssues(identity, preparation, parameters, expectedUserId, cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();
        return new(request, parameters, expectedUserId, dialogue, issues);
    }

    internal static FinTsInitializationIssue ParameterIssues(FinTsInitializationIdentification identity, FinTsInitializationPreparation preparation,
        FinTsParameterSet parameters, string? expectedUserId, CancellationToken cancellationToken)
    {
        var issues = FinTsInitializationIssue.None;
        if (parameters.Bank is not { } bank) { issues |= FinTsInitializationIssue.MissingBankParameters; }
        else
        {
            var institution = bank.Source.Fields[1].Elements;
            if (institution[0].HeaderText() != identity.Country || institution.Count != 2 || institution[1].HeaderText() != identity.Institution)
            { issues |= FinTsInitializationIssue.InstitutionMismatch; }
            if (!bank.ProtocolVersions.Contains(300)) { issues |= FinTsInitializationIssue.ProtocolNotAdvertised; }
            if (preparation.Language != FinTsDialogueLanguage.Standard && !bank.Languages.Contains((int)preparation.Language)) { issues |= FinTsInitializationIssue.LanguageNotAdvertised; }
        }
        if (parameters.User is not { } user)
        {
            if (!identity.IsAnonymous || parameters.Accounts.Count != 0) { issues |= FinTsInitializationIssue.MissingUserParameters; }
        }
        else
        {
            string returnedUser = user.UserId.HeaderText();
            if (expectedUserId is not null && returnedUser != expectedUserId || identity.IsAnonymous && returnedUser.Any(c => c != '9'))
            { issues |= FinTsInitializationIssue.UserMismatch; }
        }
        foreach (var account in parameters.Accounts)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (account.CustomerId.HeaderText() != identity.CustomerId) { issues |= FinTsInitializationIssue.CustomerMismatch; }
            if (!account.HasAccountConnection) { continue; }
            var connection = account.AccountConnection.Elements;
            if (connection[2].HeaderText() != identity.Country || connection.Count != 4 || connection[3].HeaderText() != identity.Institution)
            { issues |= FinTsInitializationIssue.AccountInstitutionMismatch; }
        }
        if (parameters.UninterpretedSegments.Count != 0) { issues |= FinTsInitializationIssue.UninterpretedParameters; }
        cancellationToken.ThrowIfCancellationRequested();
        return issues;
    }

    internal static FinTsInitializationIssue RequestIssues(FinTsUnsignedInitializationRequest request, string? expectedUserId)
    {
        if (expectedUserId is not null && (expectedUserId.Length is < 1 or > 30 || expectedUserId[0] == ' ' || expectedUserId[^1] == ' ' ||
            expectedUserId.Any(c => c < 32 || c is >= (char)127 and <= (char)160 || c > 255))) { throw InitializationFields.Invalid(); }
        return !request.Identification.IsAnonymous && expectedUserId is null ? FinTsInitializationIssue.UserContextMissing : FinTsInitializationIssue.None;
    }

    internal static FinTsInitializationIssue ResponseScopeIssues(FinTsUnsignedInitializationRequest request, FinTsResponse response,
        out string dialogue, CancellationToken cancellationToken)
    {
        var issues = FinTsInitializationIssue.None;
        var header = response.Frame.Syntax.Segments[0].Fields;
        dialogue = header[2].Elements[0].HeaderText();
        if (response.Frame.MessageNumber != 1 || header.Count != 5 || header[4].Elements.Count != 2 ||
            header[4].Elements[0].HeaderText() != dialogue || FinTsSyntax.Number(header[4].Elements[1], 4, false) != request.Frame.MessageNumber)
        { issues |= FinTsInitializationIssue.MessageMismatch; }
        // The initial response references the bank-assigned dialogue, not the outgoing placeholder zero.
        if (dialogue is "0" or "unbekannt" || dialogue[0] == ' ' || dialogue[^1] == ' ') { issues |= FinTsInitializationIssue.InvalidAssignedDialogue; }
        foreach (var segment in response.BodySegments)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (segment.Code == "HIRMG") { continue; }
            if (segment.Code == "HIRMS")
            {
                if (segment.Reference != request.Identification.Source.Number && segment.Reference != request.Preparation.Source.Number) { issues |= FinTsInitializationIssue.ReferenceMismatch; }
            }
            else if (segment.Reference != request.Preparation.Source.Number) { issues |= FinTsInitializationIssue.ReferenceMismatch; }
        }
        return issues;
    }
}
