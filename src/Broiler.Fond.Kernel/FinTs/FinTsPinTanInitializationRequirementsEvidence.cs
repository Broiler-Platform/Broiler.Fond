namespace Broiler.Fond.Kernel.FinTs;

[Flags]
public enum FinTsPinTanInitializationRequirementsIssue
{
    None = 0, ProceduresNeedReview = 1, SourceMismatch = 2, MissingAdvertisement = 4, AmbiguousAdvertisement = 8,
    ConflictingPinBounds = 16, UnsupportedRequirements = 32, AmbiguousOperations = 64, UnsupportedVersion = 128,
}

/// <summary>Combined initialization, procedure and HIPINS observations. No credential validation, capability activation or SCA exemption.</summary>
public sealed class FinTsPinTanInitializationRequirementsEvidence
{
    private FinTsPinTanInitializationRequirementsEvidence(FinTsPinTanInitializationProcedureEvidence procedures, FinTsPinTanParameterSet pinTan,
        FinTsPinTanInitializationRequirementsIssue issues, FinTsPinTanAdvertisement? advertisement, FinTsReadParameterSet? reads = null)
    { Procedures = procedures; PinTan = pinTan; Issues = issues; MatchingAdvertisement = issues == 0 ? advertisement : null; ReadParameters = reads; }
    public FinTsPinTanInitializationProcedureEvidence Procedures { get; }
    public FinTsPinTanParameterSet PinTan { get; }
    /// <summary>Explicitly supplied read schemas, not an account capability decision. Null on the original path.</summary>
    public FinTsReadParameterSet? ReadParameters { get; }
    public FinTsPinTanInitializationRequirementsIssue Issues { get; }
    public bool HasMatchingEvidence => Issues == FinTsPinTanInitializationRequirementsIssue.None;
    /// <summary>Unique HIPINS source only when the entire combined comparison matches. Optional bounds remain nullable.</summary>
    public FinTsPinTanAdvertisement? MatchingAdvertisement { get; }

    /// <summary>Qualified reported operation flag only. N does not waive SCA or grant bank/user permission.
    /// Any unresolved combined evidence yields Unknown, even if raw HIPINS contains a flag.</summary>
    public FinTsPinTanOperationEvidence GetOperationEvidence(string operation)
    {
        var reported = PinTan.GetOperationEvidence(operation);
        return HasMatchingEvidence ? reported : FinTsPinTanOperationEvidence.Unknown;
    }

    public static FinTsPinTanInitializationRequirementsEvidence Evaluate(FinTsPinTanResponseBinding binding, FinTsTanParameterSet procedures,
        FinTsPermittedProcedureSet permissions, FinTsPinTanParameterSet pinTan, CancellationToken cancellationToken = default)
        => EvaluateCore(binding, procedures, permissions, pinTan, null, cancellationToken);

    public static FinTsPinTanInitializationRequirementsEvidence EvaluateWithReadParameters(FinTsPinTanResponseBinding binding, FinTsTanParameterSet procedures,
        FinTsPermittedProcedureSet permissions, FinTsPinTanParameterSet pinTan, FinTsReadParameterSet reads, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(reads);
        return EvaluateCore(binding, procedures, permissions, pinTan, reads, cancellationToken);
    }

    internal static FinTsPinTanInitializationRequirementsEvidence EvaluateCore(FinTsPinTanResponseBinding binding, FinTsTanParameterSet procedures,
        FinTsPermittedProcedureSet permissions, FinTsPinTanParameterSet pinTan, FinTsReadParameterSet? reads, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(binding); ArgumentNullException.ThrowIfNull(procedures);
        ArgumentNullException.ThrowIfNull(permissions); ArgumentNullException.ThrowIfNull(pinTan); cancellationToken.ThrowIfCancellationRequested();
        bool sameSource = ReferenceEquals(pinTan.Source, procedures.Source) && ReferenceEquals(pinTan.Source.Source, binding.Response) &&
            ReferenceEquals(permissions.Source, binding.Response) && (reads is null || ReferenceEquals(reads.Source, pinTan.Source));
        var combined = FinTsPinTanInitializationProcedureEvidence.EvaluateCore(binding, procedures, permissions, sameSource ? pinTan : null, cancellationToken, sameSource ? reads : null);
        var issues = combined.HasMatchingEvidence ? FinTsPinTanInitializationRequirementsIssue.None : FinTsPinTanInitializationRequirementsIssue.ProceduresNeedReview;
        if (!sameSource) { issues |= FinTsPinTanInitializationRequirementsIssue.SourceMismatch; }
        if (!sameSource || binding.Request.SignatureEvidence.Request.Initialization is null) { return new(combined, pinTan, issues, null, reads); }
        if (pinTan.Advertisements.Count == 0) { issues |= FinTsPinTanInitializationRequirementsIssue.MissingAdvertisement; }
        if (pinTan.Advertisements.Count > 1) { issues |= FinTsPinTanInitializationRequirementsIssue.AmbiguousAdvertisement; }
        if (pinTan.UninterpretedSegments.Any(s => s.Code == "HIPINS")) { issues |= FinTsPinTanInitializationRequirementsIssue.UnsupportedVersion; }
        FinTsPinTanAdvertisement? advertisement = pinTan.Advertisements.Count == 1 ? pinTan.Advertisements[0] : null;
        if (advertisement is not null)
        {
            if (advertisement.HasConflictingPinLengthBounds) { issues |= FinTsPinTanInitializationRequirementsIssue.ConflictingPinBounds; }
            // Explicit zero is retained by parsing, but has no usable credential-length meaning in this local path.
            if (advertisement.MaximumOrders < 1 || advertisement.MinimumSignatures > 1 || advertisement.MinimumPinLength == 0 ||
                advertisement.MaximumPinLength == 0 || advertisement.MaximumTanLength == 0)
            { issues |= FinTsPinTanInitializationRequirementsIssue.UnsupportedRequirements; }
            if (advertisement.Operations.GroupBy(o => o.Operation, StringComparer.Ordinal).Any(g => g.Count() > 1))
            { issues |= FinTsPinTanInitializationRequirementsIssue.AmbiguousOperations; }
        }
        cancellationToken.ThrowIfCancellationRequested();
        return new(combined, pinTan, issues, advertisement, reads);
    }
}
