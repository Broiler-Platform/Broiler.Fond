using System.Globalization;

namespace Broiler.Fond.Kernel.FinTs;

[Flags]
public enum FinTsPinTanReadSignatureIssue
{
    None = 0, InitializationNeedsReview = 1, DialogueMismatch = 2, CounterMismatch = 4,
    IdentityMismatch = 8, SystemMismatch = 16, SelectionMismatch = 32, ControlMismatch = 64,
    HeaderRoleNeedsReview = 128, ContinuationNeedsReview = 256, OperationRequirementsNeedReview = 512,
}

/// <summary>Detached header context for the first read after an assembled initialization report.
/// Pure evidence only: no capability authorization, SCA exemption, replay protection or message encoding.</summary>
public sealed class FinTsPinTanReadSignatureContext
{
    private FinTsPinTanReadSignatureContext(FinTsPinTanInitializationRequirementsEvidence initialization, FinTsReadRequestContext request,
        FinTsPinTanSignatureHeader header, FinTsPinTanSignatureSelection selection, string control,
        FinTsPinTanReadSignatureIssue issues, FinTsPinTanOperationEvidence operation)
    { Initialization = initialization; Request = request; Header = header; Selection = selection; ControlReference = control; Issues = issues; OperationEvidence = issues == 0 ? operation : FinTsPinTanOperationEvidence.Unknown; }
    public FinTsPinTanInitializationRequirementsEvidence Initialization { get; }
    public FinTsReadRequestContext Request { get; }
    public FinTsPinTanSignatureHeader Header { get; }
    public FinTsPinTanSignatureSelection Selection { get; }
    public string ControlReference { get; }
    public FinTsPinTanReadSignatureIssue Issues { get; }
    public bool HasMatchingEvidence => Issues == FinTsPinTanReadSignatureIssue.None;
    /// <summary>Reported HIPINS flag only when the whole context matches. N does not waive SCA.</summary>
    public FinTsPinTanOperationEvidence OperationEvidence { get; }
    public FinTsTanAdvertisement? MatchingAdvertisement => HasMatchingEvidence ? Initialization.Procedures.MatchingAdvertisement : null;
    public FinTsTanProcedure? MatchingProcedure => HasMatchingEvidence ? Initialization.Procedures.MatchingProcedure : null;
    public FinTsPinTanAdvertisement? MatchingRequirements => HasMatchingEvidence ? Initialization.MatchingAdvertisement : null;

    public static FinTsPinTanReadSignatureContext Evaluate(FinTsPinTanInitializationRequirementsEvidence initialization,
        FinTsReadRequestContext request, FinTsPinTanSignatureHeader header, FinTsPinTanSignatureSelection selection,
        string controlReference, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(initialization); ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(header); ArgumentNullException.ThrowIfNull(selection);
        cancellationToken.ThrowIfCancellationRequested();
        SignatureContextFields.Text(controlReference, 14);
        if (controlReference == "0") { throw SignatureContextFields.Invalid(); }
        var issues = FinTsPinTanReadSignatureIssue.None;
        if (!initialization.HasMatchingEvidence) { issues |= FinTsPinTanReadSignatureIssue.InitializationNeedsReview; }
        var origin = initialization.Procedures.Initialization;
        var previous = origin.Request.SignatureEvidence;
        if (FinTsReadContextEvidence.Dialog(request.Frame) != origin.ReportedDialogueId) { issues |= FinTsPinTanReadSignatureIssue.DialogueMismatch; }
        // This local path covers only the immediate next request/response, not arbitrary later messages or recovery.
        if (request.Frame.MessageNumber != 2 || request.ExpectedBankMessageNumber != 2) { issues |= FinTsPinTanReadSignatureIssue.CounterMismatch; }
        if (request.Request.ContinuationToken is not null) { issues |= FinTsPinTanReadSignatureIssue.ContinuationNeedsReview; }
        if (header.CountryCode != previous.Header.CountryCode || header.InstitutionId != previous.Header.InstitutionId || header.UserId != previous.Request.ExpectedUserId)
        { issues |= FinTsPinTanReadSignatureIssue.IdentityMismatch; }
        if (header.SystemId != previous.Header.SystemId || header.SystemId is "0" or "unbekannt") { issues |= FinTsPinTanReadSignatureIssue.SystemMismatch; }
        var pinned = previous.Request.Selection;
        if (selection.ProfileVersion != pinned.ProfileVersion || selection.SecurityFunction != pinned.SecurityFunction || selection.TanSegmentVersion != pinned.TanSegmentVersion ||
            header.ProfileVersion != selection.ProfileVersion || header.SecurityFunction != selection.SecurityFunction.ToString(CultureInfo.InvariantCulture))
        { issues |= FinTsPinTanReadSignatureIssue.SelectionMismatch; }
        if (header.ControlReference != controlReference) { issues |= FinTsPinTanReadSignatureIssue.ControlMismatch; }
        if (header.Source.Number != 2 || header.SecuritySupplierRole != 1 || header.SecurityParty != 1) { issues |= FinTsPinTanReadSignatureIssue.HeaderRoleNeedsReview; }
        var operation = initialization.GetOperationEvidence(request.Request.Source.Code);
        if (initialization.HasMatchingEvidence && operation is not (FinTsPinTanOperationEvidence.TanReportedRequired or FinTsPinTanOperationEvidence.TanReportedNotRequired))
        { issues |= FinTsPinTanReadSignatureIssue.OperationRequirementsNeedReview; }
        cancellationToken.ThrowIfCancellationRequested();
        return new(initialization, request, header, selection, controlReference, issues, operation);
    }
}
