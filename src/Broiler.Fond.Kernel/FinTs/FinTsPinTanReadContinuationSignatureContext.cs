using System.Globalization;

namespace Broiler.Fond.Kernel.FinTs;

[Flags]
public enum FinTsPinTanReadContinuationSignatureIssue
{
    None = 0, ContinuationNeedsReview = 1, IdentityMismatch = 2, SystemMismatch = 4,
    SelectionMismatch = 8, ControlMismatch = 16, HeaderRoleNeedsReview = 32,
}

/// <summary>Detached PIN/TAN header context for a qualified second balance page. No credential validation,
/// message encoding, freshness, authentication, SCA completion or continuation submission.</summary>
public sealed class FinTsPinTanReadContinuationSignatureContext
{
    private FinTsPinTanReadContinuationSignatureContext(FinTsPinTanReadContinuationContext continuation,
        FinTsPinTanSignatureHeader header, FinTsPinTanSignatureSelection selection, string control, FinTsPinTanReadContinuationSignatureIssue issues)
    { Continuation = continuation; Header = header; Selection = selection; ControlReference = control; Issues = issues; }
    public FinTsPinTanReadContinuationContext Continuation { get; }
    public FinTsReadRequestContext Request => Continuation.Request;
    public FinTsPinTanSignatureHeader Header { get; }
    public FinTsPinTanSignatureSelection Selection { get; }
    public string ControlReference { get; }
    public FinTsPinTanReadContinuationSignatureIssue Issues { get; }
    public bool HasMatchingEvidence => Issues == FinTsPinTanReadContinuationSignatureIssue.None;
    public FinTsPinTanReadCapabilityContext? MatchingOrigin => HasMatchingEvidence ? Continuation.Origin : null;
    public FinTsTanAdvertisement? MatchingAdvertisement => HasMatchingEvidence ? Continuation.Origin.Signature.MatchingAdvertisement : null;
    public FinTsTanProcedure? MatchingProcedure => HasMatchingEvidence ? Continuation.Origin.Signature.MatchingProcedure : null;
    public FinTsPinTanAdvertisement? MatchingRequirements => HasMatchingEvidence ? Continuation.Origin.Signature.MatchingRequirements : null;
    /// <summary>Qualified original HIPINS observation only. A reported N flag does not waive SCA.</summary>
    public FinTsPinTanOperationEvidence OperationEvidence => HasMatchingEvidence ? Continuation.Origin.Signature.OperationEvidence : FinTsPinTanOperationEvidence.Unknown;

    public static FinTsPinTanReadContinuationSignatureContext Evaluate(FinTsPinTanReadContinuationContext continuation,
        FinTsPinTanSignatureHeader header, FinTsPinTanSignatureSelection selection, string controlReference,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(continuation); ArgumentNullException.ThrowIfNull(header); ArgumentNullException.ThrowIfNull(selection);
        cancellationToken.ThrowIfCancellationRequested();
        SignatureContextFields.Text(controlReference, 14);
        if (controlReference == "0") { throw SignatureContextFields.Invalid(); }
        var issues = continuation.HasMatchingEvidence ? FinTsPinTanReadContinuationSignatureIssue.None : FinTsPinTanReadContinuationSignatureIssue.ContinuationNeedsReview;
        var original = continuation.Origin.Signature; var expected = original.Header;
        if (header.CountryCode != expected.CountryCode || header.InstitutionId != expected.InstitutionId || header.UserId != expected.UserId)
        { issues |= FinTsPinTanReadContinuationSignatureIssue.IdentityMismatch; }
        if (header.SystemId != expected.SystemId || header.SystemId is "0" or "unbekannt")
        { issues |= FinTsPinTanReadContinuationSignatureIssue.SystemMismatch; }
        var pinned = original.Selection;
        if (selection.ProfileVersion != pinned.ProfileVersion || selection.SecurityFunction != pinned.SecurityFunction || selection.TanSegmentVersion != pinned.TanSegmentVersion ||
            header.ProfileVersion != selection.ProfileVersion || header.SecurityFunction != selection.SecurityFunction.ToString(CultureInfo.InvariantCulture))
        { issues |= FinTsPinTanReadContinuationSignatureIssue.SelectionMismatch; }
        if (header.ControlReference != controlReference) { issues |= FinTsPinTanReadContinuationSignatureIssue.ControlMismatch; }
        if (header.Source.Number != 2 || header.SecuritySupplierRole != 1 || header.SecurityParty != 1)
        { issues |= FinTsPinTanReadContinuationSignatureIssue.HeaderRoleNeedsReview; }
        cancellationToken.ThrowIfCancellationRequested();
        return new(continuation, header, selection, controlReference, issues);
    }
}
