namespace Broiler.Fond.Kernel.FinTs;

[Flags]
public enum FinTsPinTanDialogueEndContextIssue
{
    None = 0, SynchronizationNeedsReview = 1, DialogueMismatch = 2, CounterMismatch = 4,
    IdentityMismatch = 8, SystemMismatch = 16, SelectionMismatch = 32, HeaderRoleNeedsReview = 64,
}

/// <summary>Local closing candidate after one assembled synchronization response. No session activation or recovery application.</summary>
public sealed class FinTsPinTanDialogueEndContext
{
    private readonly FinTsPinTanSynchronizationEvidence? _synchronization;
    private FinTsPinTanDialogueEndContext(FinTsPinTanSynchronizationEvidence? synchronization, FinTsUnsignedDialogueEndRequest request,
        FinTsPinTanSignatureHeader header, FinTsPinTanDialogueEndContextIssue issues, FinTsPinTanInitializationEvidence? initialization = null)
    { _synchronization = synchronization; Initialization = initialization; Request = request; Header = header; Issues = issues; }
    public FinTsPinTanSynchronizationEvidence Synchronization => _synchronization ?? throw new InvalidOperationException("Context was evaluated for initialization.");
    public FinTsPinTanSynchronizationEvidence? SynchronizationEvidence => _synchronization;
    public FinTsPinTanInitializationEvidence? Initialization { get; }
    public FinTsUnsignedDialogueEndRequest Request { get; }
    public FinTsPinTanSignatureHeader Header { get; }
    public FinTsPinTanDialogueEndContextIssue Issues { get; }
    public bool HasMatchingEvidence => Issues == FinTsPinTanDialogueEndContextIssue.None;

    public static FinTsPinTanDialogueEndContext Evaluate(FinTsPinTanSynchronizationEvidence synchronization,
        FinTsUnsignedDialogueEndRequest request, FinTsPinTanSignatureHeader header, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(synchronization); ArgumentNullException.ThrowIfNull(request); ArgumentNullException.ThrowIfNull(header);
        cancellationToken.ThrowIfCancellationRequested();
        var issues = FinTsPinTanDialogueEndContextIssue.None;
        if (!synchronization.HasMatchingEvidence) { issues |= FinTsPinTanDialogueEndContextIssue.SynchronizationNeedsReview; }
        if (request.Request.DialogueId != synchronization.ReportedDialogueId) { issues |= FinTsPinTanDialogueEndContextIssue.DialogueMismatch; }
        // This restricted path closes the new synchronization dialogue, not the older recovered dialogue.
        if (request.Frame.MessageNumber != 2 || request.ExpectedBankMessageNumber != 2) { issues |= FinTsPinTanDialogueEndContextIssue.CounterMismatch; }
        var previous = synchronization.Request.SignatureEvidence.Header;
        if (header.CountryCode != previous.CountryCode || header.InstitutionId != previous.InstitutionId || header.UserId != previous.UserId)
        { issues |= FinTsPinTanDialogueEndContextIssue.IdentityMismatch; }
        string? system = synchronization.Request.SignatureEvidence.Request.Synchronization?.Synchronization.Mode == FinTsSynchronizationMode.SystemId
            ? synchronization.MatchingReport?.SystemId : previous.SystemId;
        if (system is null || header.SystemId != system || header.SystemId is "0" or "unbekannt")
        { issues |= FinTsPinTanDialogueEndContextIssue.SystemMismatch; }
        if (header.ProfileVersion != previous.ProfileVersion || header.SecurityFunction != previous.SecurityFunction)
        { issues |= FinTsPinTanDialogueEndContextIssue.SelectionMismatch; }
        if (header.Source.Number != 2 || header.SecuritySupplierRole != 1 || header.SecurityParty != 1)
        { issues |= FinTsPinTanDialogueEndContextIssue.HeaderRoleNeedsReview; }
        cancellationToken.ThrowIfCancellationRequested();
        return new(synchronization, request, header, issues);
    }

    public static FinTsPinTanDialogueEndContext EvaluateForInitialization(FinTsPinTanInitializationEvidence initialization,
        FinTsUnsignedDialogueEndRequest request, FinTsPinTanSignatureHeader header,
        int expectedMessageNumber = 2, int expectedBankMessageNumber = 2, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(initialization); ArgumentNullException.ThrowIfNull(request); ArgumentNullException.ThrowIfNull(header);
        cancellationToken.ThrowIfCancellationRequested();
        var issues = FinTsPinTanDialogueEndContextIssue.None;
        if (!initialization.HasMatchingEvidence) { issues |= FinTsPinTanDialogueEndContextIssue.SynchronizationNeedsReview; }
        if (request.Request.DialogueId != initialization.ReportedDialogueId) { issues |= FinTsPinTanDialogueEndContextIssue.DialogueMismatch; }
        if (request.Frame.MessageNumber != expectedMessageNumber || request.ExpectedBankMessageNumber != expectedBankMessageNumber)
        { issues |= FinTsPinTanDialogueEndContextIssue.CounterMismatch; }
        var previous = initialization.Request.SignatureEvidence.Header;
        if (header.CountryCode != previous.CountryCode || header.InstitutionId != previous.InstitutionId || header.UserId != previous.UserId)
        { issues |= FinTsPinTanDialogueEndContextIssue.IdentityMismatch; }
        string? system = previous.SystemId;
        if (system is null || header.SystemId != system || header.SystemId is "0" or "unbekannt")
        { issues |= FinTsPinTanDialogueEndContextIssue.SystemMismatch; }
        if (header.ProfileVersion != previous.ProfileVersion || header.SecurityFunction != previous.SecurityFunction)
        { issues |= FinTsPinTanDialogueEndContextIssue.SelectionMismatch; }
        if (header.Source.Number != 2 || header.SecuritySupplierRole != 1 || header.SecurityParty != 1)
        { issues |= FinTsPinTanDialogueEndContextIssue.HeaderRoleNeedsReview; }
        cancellationToken.ThrowIfCancellationRequested();
        return new(null, request, header, issues, initialization);
    }
}
