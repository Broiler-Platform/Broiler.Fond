namespace Broiler.Fond.Kernel.FinTs;

[Flags]
public enum FinTsPinTanInitializationProcedureIssue
{
    None = 0, InitializationNeedsReview = 1, SourceMismatch = 2, MissingAdvertisement = 4, AmbiguousAdvertisement = 8,
    MissingPermissionReport = 16, AmbiguousPermissions = 32, PermissionScopeMismatch = 64, SelectionNotReported = 128,
    OneStepNotReportedAllowed = 256, AdvertisementRequirementsNeedReview = 512, AmbiguousProcedure = 1024,
}

/// <summary>Initialization and returned HITANS/3920 observations for the pinned selection. No negotiation, activation or authorization.</summary>
public sealed class FinTsPinTanInitializationProcedureEvidence
{
    private FinTsPinTanInitializationProcedureEvidence(FinTsPinTanInitializationEvidence initialization, FinTsTanParameterSet procedures,
        FinTsPermittedProcedureSet permissions, FinTsPinTanInitializationProcedureIssue issues, FinTsTanAdvertisement? advertisement, FinTsTanProcedure? procedure)
    { Initialization = initialization; Procedures = procedures; Permissions = permissions; Issues = issues; MatchingAdvertisement = issues == 0 ? advertisement : null; MatchingProcedure = issues == 0 ? procedure : null; }
    public FinTsPinTanInitializationEvidence Initialization { get; }
    public FinTsTanParameterSet Procedures { get; }
    public FinTsPermittedProcedureSet Permissions { get; }
    public FinTsPinTanInitializationProcedureIssue Issues { get; }
    public bool HasMatchingEvidence => Issues == FinTsPinTanInitializationProcedureIssue.None;
    public FinTsTanAdvertisement? MatchingAdvertisement { get; }
    /// <summary>A unique returned two-step procedure only when every comparison matches. One-step matching has no two-step procedure.</summary>
    public FinTsTanProcedure? MatchingProcedure { get; }

    public static FinTsPinTanInitializationProcedureEvidence Evaluate(FinTsPinTanResponseBinding binding, FinTsTanParameterSet procedures,
        FinTsPermittedProcedureSet permissions, CancellationToken cancellationToken = default) => EvaluateCore(binding, procedures, permissions, null, cancellationToken);

    internal static FinTsPinTanInitializationProcedureEvidence EvaluateCore(FinTsPinTanResponseBinding binding, FinTsTanParameterSet procedures,
        FinTsPermittedProcedureSet permissions, FinTsPinTanParameterSet? pinTan, CancellationToken cancellationToken, FinTsReadParameterSet? reads = null)
    {
        ArgumentNullException.ThrowIfNull(binding); ArgumentNullException.ThrowIfNull(procedures); ArgumentNullException.ThrowIfNull(permissions);
        cancellationToken.ThrowIfCancellationRequested();
        var initialization = FinTsPinTanInitializationEvidence.EvaluateCore(binding, procedures.Source, procedures, permissions, cancellationToken, pinTan, reads);
        var issues = initialization.HasMatchingEvidence ? FinTsPinTanInitializationProcedureIssue.None : FinTsPinTanInitializationProcedureIssue.InitializationNeedsReview;
        if (!ReferenceEquals(procedures.Source.Source, binding.Response) || !ReferenceEquals(permissions.Source, binding.Response))
        { issues |= FinTsPinTanInitializationProcedureIssue.SourceMismatch; }
        if ((issues & FinTsPinTanInitializationProcedureIssue.SourceMismatch) != 0 || binding.Request.SignatureEvidence.Request.Initialization is null)
        { return new(initialization, procedures, permissions, issues, null, null); }
        var selection = binding.Request.SignatureEvidence.Request.Selection;
        var advertisements = procedures.Advertisements.Where(a => a.Source.Version == selection.TanSegmentVersion).ToArray();
        if (advertisements.Length == 0) { issues |= FinTsPinTanInitializationProcedureIssue.MissingAdvertisement; }
        if (advertisements.Length > 1 || procedures.HasDuplicateAdvertisementVersions) { issues |= FinTsPinTanInitializationProcedureIssue.AmbiguousAdvertisement; }
        FinTsTanAdvertisement? advertisement = null; FinTsTanProcedure? procedure = null;
        if (advertisements.Length == 1)
        {
            advertisement = advertisements[0];
            if (advertisement.MaximumOrders < 1 || advertisement.MinimumSignatures > 1) { issues |= FinTsPinTanInitializationProcedureIssue.AdvertisementRequirementsNeedReview; }
            if (advertisement.HasDuplicateSecurityFunctions) { issues |= FinTsPinTanInitializationProcedureIssue.AmbiguousProcedure; }
            if (selection.ProfileVersion == 1)
            { if (!advertisement.OneStepReportedAllowed) { issues |= FinTsPinTanInitializationProcedureIssue.OneStepNotReportedAllowed; } }
            else
            {
                var candidates = advertisement.Procedures.Where(p => p.SecurityFunction == selection.SecurityFunction).ToArray();
                if (candidates.Length == 0) { issues |= FinTsPinTanInitializationProcedureIssue.SelectionNotReported; }
                if (candidates.Length > 1) { issues |= FinTsPinTanInitializationProcedureIssue.AmbiguousProcedure; }
                if (candidates.Length == 1) { procedure = candidates[0]; }
            }
        }
        if (permissions.Reports.Count == 0) { issues |= FinTsPinTanInitializationProcedureIssue.MissingPermissionReport; }
        if (permissions.IsAmbiguous) { issues |= FinTsPinTanInitializationProcedureIssue.AmbiguousPermissions; }
        if (!permissions.Reports.Any(r => r.SecurityFunctions.Contains(selection.SecurityFunction))) { issues |= FinTsPinTanInitializationProcedureIssue.SelectionNotReported; }
        if (permissions.Reports.Any(r => r.Segment.RequestSegmentNumber != binding.Request.PreparationNumber)) { issues |= FinTsPinTanInitializationProcedureIssue.PermissionScopeMismatch; }
        cancellationToken.ThrowIfCancellationRequested();
        return new(initialization, procedures, permissions, issues, advertisement, procedure);
    }
}
