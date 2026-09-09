namespace Broiler.Fond.Kernel.FinTs;

/// <summary>Explicit caller selection for comparison, not a negotiated or permitted security procedure.</summary>
public sealed class FinTsPinTanSignatureSelection
{
    public FinTsPinTanSignatureSelection(int profileVersion, int securityFunction, int tanSegmentVersion)
    {
        if (profileVersion is not (1 or 2) || (profileVersion == 1 ? securityFunction != 999 : securityFunction is < 900 or > 997) || tanSegmentVersion is not (6 or 7))
        { throw SignatureContextFields.Invalid(); }
        ProfileVersion = profileVersion; SecurityFunction = securityFunction; TanSegmentVersion = tanSegmentVersion;
    }
    public int ProfileVersion { get; }
    public int SecurityFunction { get; }
    public int TanSegmentVersion { get; }
}

/// <summary>Detached header expectation for one unsigned initialization or synchronization request. No signed frame is built.</summary>
public sealed class FinTsPinTanSignatureRequestContext
{
    private FinTsPinTanSignatureRequestContext(FinTsUnsignedInitializationRequest? initialization, FinTsUnsignedSynchronizationRequest? synchronization,
        string user, string control, FinTsPinTanSignatureSelection selection)
    {
        SignatureContextFields.Text(user, 30); SignatureContextFields.Text(control, 14);
        if (control == "0") { throw SignatureContextFields.Invalid(); }
        ArgumentNullException.ThrowIfNull(selection);
        Initialization = initialization; Synchronization = synchronization; ExpectedUserId = user; ControlReference = control; Selection = selection;
    }
    public FinTsUnsignedInitializationRequest? Initialization { get; }
    public FinTsUnsignedSynchronizationRequest? Synchronization { get; }
    public FinTsMessageFrame Frame => Initialization?.Frame ?? Synchronization!.Frame;
    public FinTsInitializationIdentification Identification => Initialization?.Identification ?? Synchronization!.Identification;
    public string ExpectedUserId { get; }
    public string ControlReference { get; }
    public FinTsPinTanSignatureSelection Selection { get; }
    public static FinTsPinTanSignatureRequestContext ForInitialization(FinTsUnsignedInitializationRequest request, string expectedUserId,
        string controlReference, FinTsPinTanSignatureSelection selection)
    { ArgumentNullException.ThrowIfNull(request); return new(request, null, expectedUserId, controlReference, selection); }
    public static FinTsPinTanSignatureRequestContext ForSynchronization(FinTsUnsignedSynchronizationRequest request, string expectedUserId,
        string controlReference, FinTsPinTanSignatureSelection selection)
    { ArgumentNullException.ThrowIfNull(request); return new(null, request, expectedUserId, controlReference, selection); }
}

/// <summary>Explicit origin for parameter/procedure observations. Constructor does not validate or authenticate the association.</summary>
public sealed class FinTsPinTanProcedureContext
{
    public FinTsPinTanProcedureContext(FinTsUnsignedInitializationRequest initialization, FinTsTanParameterSet parameters, FinTsPermittedProcedureSet permissions)
    {
        ArgumentNullException.ThrowIfNull(initialization); ArgumentNullException.ThrowIfNull(parameters); ArgumentNullException.ThrowIfNull(permissions);
        Initialization = initialization; Parameters = parameters; Permissions = permissions;
    }
    public FinTsUnsignedInitializationRequest Initialization { get; }
    public FinTsTanParameterSet Parameters { get; }
    public FinTsPermittedProcedureSet Permissions { get; }
}

[Flags]
public enum FinTsPinTanSignatureIssue
{
    None = 0, RequestNeedsReview = 1, IdentityMismatch = 2, SystemMismatch = 4, SystemNeedsReview = 8,
    SelectionMismatch = 16, ControlMismatch = 32, HeaderRoleNeedsReview = 64, MissingProcedureContext = 128,
    ProcedureScopeMismatch = 256, ProcedureResponseNeedsReview = 512, MissingAdvertisement = 1024,
    AmbiguousAdvertisement = 2048, UninterpretedParameters = 4096, MissingPermissionReport = 8192,
    AmbiguousPermissions = 16384, ProcedureNotListed = 32768, AmbiguousProcedure = 65536, OneStepNotReportedAllowed = 131072,
    AdvertisementRequirementsNeedReview = 262144,
}

/// <summary>Pure comparison of a detached header, unsigned request expectation and explicitly sourced procedure evidence.
/// Matching never authenticates a message, activates a procedure, applies a system ID or authorizes sending.</summary>
public sealed class FinTsPinTanSignatureEvidence
{
    private FinTsPinTanSignatureEvidence(FinTsPinTanSignatureRequestContext request, FinTsPinTanSignatureHeader header,
        FinTsPinTanProcedureContext? procedures, FinTsPinTanSignatureIssue issues, FinTsTanAdvertisement? advertisement, FinTsTanProcedure? procedure)
    { Request = request; Header = header; Procedures = procedures; Issues = issues; MatchingAdvertisement = issues == 0 ? advertisement : null; MatchingProcedure = issues == 0 ? procedure : null; }
    public FinTsPinTanSignatureRequestContext Request { get; }
    public FinTsPinTanSignatureHeader Header { get; }
    public FinTsPinTanProcedureContext? Procedures { get; }
    public FinTsPinTanSignatureIssue Issues { get; }
    public bool HasMatchingEvidence => Issues == FinTsPinTanSignatureIssue.None;
    public FinTsTanAdvertisement? MatchingAdvertisement { get; }
    /// <summary>Only a unique matching two-step procedure; one-step success has no two-step procedure.</summary>
    public FinTsTanProcedure? MatchingProcedure { get; }

    public static FinTsPinTanSignatureEvidence Evaluate(FinTsPinTanSignatureRequestContext request, FinTsPinTanSignatureHeader header,
        FinTsPinTanProcedureContext? procedures = null, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request); ArgumentNullException.ThrowIfNull(header); cancellationToken.ThrowIfCancellationRequested();
        var issues = FinTsPinTanSignatureIssue.None; var identity = request.Identification; var selection = request.Selection;
        if (identity.IsAnonymous || identity.SystemStatus != FinTsCustomerSystemStatus.Required || request.Synchronization?.Synchronization.Mode == FinTsSynchronizationMode.SignatureReferences)
        { issues |= FinTsPinTanSignatureIssue.RequestNeedsReview; }
        if (header.CountryCode != identity.Country || header.InstitutionId != identity.Institution || header.UserId != request.ExpectedUserId)
        { issues |= FinTsPinTanSignatureIssue.IdentityMismatch; }
        if (header.SystemId != identity.SystemId) { issues |= FinTsPinTanSignatureIssue.SystemMismatch; }
        bool assigning = request.Synchronization?.Synchronization.Mode == FinTsSynchronizationMode.SystemId;
        if (identity.SystemId == "unbekannt" || !assigning && identity.SystemId == "0") { issues |= FinTsPinTanSignatureIssue.SystemNeedsReview; }
        if (header.ProfileVersion != selection.ProfileVersion || header.SecurityFunction != selection.SecurityFunction.ToString(System.Globalization.CultureInfo.InvariantCulture))
        { issues |= FinTsPinTanSignatureIssue.SelectionMismatch; }
        if (header.ControlReference != request.ControlReference) { issues |= FinTsPinTanSignatureIssue.ControlMismatch; }
        if (header.Source.Number != 2 || header.SecuritySupplierRole != 1 || header.SecurityParty != 1) { issues |= FinTsPinTanSignatureIssue.HeaderRoleNeedsReview; }
        FinTsTanAdvertisement? advertisement = null; FinTsTanProcedure? procedure = null;
        if (procedures is null) { issues |= FinTsPinTanSignatureIssue.MissingProcedureContext; }
        else
        {
            var parameters = procedures.Parameters; var permissions = procedures.Permissions; var origin = procedures.Initialization;
            var source = parameters.Source.Source;
            if (!ReferenceEquals(source, permissions.Source) || origin.Identification.IsAnonymous ||
                origin.Identification.Country != identity.Country || origin.Identification.Institution != identity.Institution || origin.Identification.CustomerId != identity.CustomerId)
            { issues |= FinTsPinTanSignatureIssue.ProcedureScopeMismatch; }
            var binding = FinTsInitializationEvidence.Evaluate(origin, parameters.Source, request.ExpectedUserId, cancellationToken);
            // Re-evaluate only the HITANS/3920-specific vocabulary below; retain every other initialization binding requirement.
            if ((binding.Issues & ~(FinTsInitializationIssue.StatusNeedsReview | FinTsInitializationIssue.UninterpretedParameters)) != 0)
            { issues |= FinTsPinTanSignatureIssue.ProcedureScopeMismatch; }
            if (parameters.UninterpretedSegments.Count != 0) { issues |= FinTsPinTanSignatureIssue.UninterpretedParameters; }
            if (source.PinTanEnvelope is not null || source.HasErrors || source.HasConflictingClasses || source.HasIndeterminateProcessing)
            { issues |= FinTsPinTanSignatureIssue.ProcedureResponseNeedsReview; }
            bool messageExecution = false, identityExecution = false, preparationExecution = false;
            foreach (var segment in source.ReplySegments)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var seen = new HashSet<string>(StringComparer.Ordinal);
                foreach (var reply in segment.Replies)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    bool permission = reply.Code == "3920" && !segment.IsMessageLevel && segment.RequestSegmentNumber == origin.Preparation.Source.Number;
                    bool supported = permission || (segment.IsMessageLevel ? reply.Code is "0010" or "0020" : reply.Code == "0020");
                    if (!supported || !seen.Add(reply.Code) || !reply.ElementReference.IsEmpty || !permission && reply.Parameters.Any(p => !p.IsEmpty))
                    { issues |= FinTsPinTanSignatureIssue.ProcedureResponseNeedsReview; }
                    if (reply.Code != "0020") { continue; }
                    if (segment.IsMessageLevel) { messageExecution = true; }
                    else if (segment.RequestSegmentNumber == origin.Identification.Source.Number) { identityExecution = true; }
                    else if (segment.RequestSegmentNumber == origin.Preparation.Source.Number) { preparationExecution = true; }
                }
            }
            if (!messageExecution && !(identityExecution && preparationExecution)) { issues |= FinTsPinTanSignatureIssue.ProcedureResponseNeedsReview; }
            var advertisements = parameters.Advertisements.Where(a => a.Source.Version == selection.TanSegmentVersion).ToArray();
            if (advertisements.Length == 0) { issues |= FinTsPinTanSignatureIssue.MissingAdvertisement; }
            if (advertisements.Length > 1 || parameters.HasDuplicateAdvertisementVersions) { issues |= FinTsPinTanSignatureIssue.AmbiguousAdvertisement; }
            if (advertisements.Length == 1)
            {
                advertisement = advertisements[0];
                if (advertisement.MaximumOrders < 1 || advertisement.MinimumSignatures > 1) { issues |= FinTsPinTanSignatureIssue.AdvertisementRequirementsNeedReview; }
                if (advertisement.HasDuplicateSecurityFunctions) { issues |= FinTsPinTanSignatureIssue.AmbiguousProcedure; }
                if (selection.ProfileVersion == 1)
                { if (!advertisement.OneStepReportedAllowed) { issues |= FinTsPinTanSignatureIssue.OneStepNotReportedAllowed; } }
                else
                {
                    var candidates = advertisement.Procedures.Where(p => p.SecurityFunction == selection.SecurityFunction).ToArray();
                    if (candidates.Length == 0) { issues |= FinTsPinTanSignatureIssue.ProcedureNotListed; }
                    if (candidates.Length > 1) { issues |= FinTsPinTanSignatureIssue.AmbiguousProcedure; }
                    if (candidates.Length == 1) { procedure = candidates[0]; }
                }
            }
            if (permissions.Reports.Count == 0) { issues |= FinTsPinTanSignatureIssue.MissingPermissionReport; }
            if (permissions.IsAmbiguous) { issues |= FinTsPinTanSignatureIssue.AmbiguousPermissions; }
            if (!permissions.Reports.Any(r => r.SecurityFunctions.Contains(selection.SecurityFunction))) { issues |= FinTsPinTanSignatureIssue.ProcedureNotListed; }
            if (permissions.Reports.Any(r => r.Segment.RequestSegmentNumber != origin.Preparation.Source.Number)) { issues |= FinTsPinTanSignatureIssue.ProcedureScopeMismatch; }
        }
        cancellationToken.ThrowIfCancellationRequested();
        return new(request, header, procedures, issues, advertisement, procedure);
    }
}

internal static class SignatureContextFields
{
    internal static FinTsFormatException Invalid() => new(FinTsSyntaxError.InvalidSignatureContext);
    internal static void Text(string value, int maximum)
    {
        ArgumentNullException.ThrowIfNull(value);
        if (value.Length is < 1 || value.Length > maximum || value[0] == ' ' || value[^1] == ' ' ||
            value.Any(c => c < 32 || c is >= (char)127 and <= (char)160 || c > 255)) { throw Invalid(); }
    }
}
