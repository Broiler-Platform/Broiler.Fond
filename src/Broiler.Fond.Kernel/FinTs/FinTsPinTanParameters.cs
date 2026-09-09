using System.Collections.ObjectModel;

namespace Broiler.Fond.Kernel.FinTs;

public enum FinTsPinTanOperationEvidence { Unknown, Unlisted, TanReportedRequired, TanReportedNotRequired, Ambiguous }

/// <summary>Reported flag only. A false flag does not waive SCA or establish permission.</summary>
public sealed class FinTsPinTanOperation
{
    internal FinTsPinTanOperation(FinTsDataElement operation, FinTsDataElement flag)
    {
        SourceOperation = operation;
        SourceFlag = flag;
        Operation = operation.HeaderText();
        TanRequired = FinTsReadAdvertisement.Flag(flag);
    }
    public FinTsDataElement SourceOperation { get; }
    public FinTsDataElement SourceFlag { get; }
    public string Operation { get; }
    public bool TanRequired { get; }
}

public sealed class FinTsPinTanAdvertisement
{
    internal FinTsPinTanAdvertisement(FinTsSegment source, List<FinTsPinTanOperation> operations)
    {
        Source = source;
        Operations = operations.AsReadOnly();
        var options = source.Fields[3].Elements;
        MinimumPinLength = OptionalNumber(options, 0);
        MaximumPinLength = OptionalNumber(options, 1);
        MaximumTanLength = OptionalNumber(options, 2);
        UserIdLabel = options.Count > 3 ? options[3] : null;
        CustomerIdLabel = options.Count > 4 ? options[4] : null;
    }
    public FinTsSegment Source { get; }
    public int MaximumOrders => ParameterFields.Number(Source.Fields[0], 3)!.Value;
    public int MinimumSignatures => ParameterFields.Number(Source.Fields[1], 1)!.Value;
    /// <summary>Raw filler; security classes have no processing meaning for PIN/TAN.</summary>
    public int SecurityClass => ParameterFields.Number(Source.Fields[2], 1)!.Value;
    public int? MinimumPinLength { get; }
    public int? MaximumPinLength { get; }
    public int? MaximumTanLength { get; }
    public FinTsDataElement? UserIdLabel { get; }
    public FinTsDataElement? CustomerIdLabel { get; }
    public bool HasConflictingPinLengthBounds => MinimumPinLength > MaximumPinLength;
    public ReadOnlyCollection<FinTsPinTanOperation> Operations { get; }
    internal static int? OptionalNumber(IReadOnlyList<FinTsDataElement> elements, int index)
    {
        if (index >= elements.Count) { return null; }
        ParameterFields.Text(elements[index], 2, false);
        return elements[index].IsEmpty ? null : ParameterFields.Number(elements[index], 2);
    }
}

/// <summary>Bounded HIPINS version 1 evidence; never selects a TAN procedure or enables an operation.</summary>
public sealed class FinTsPinTanParameterSet
{
    public const int MaximumAdvertisements = 128;
    // The protocol allows 999 repetitions; the existing 256-component syntax bound permits 125 pairs.
    public const int MaximumOperationsPerAdvertisement = 125;
    private FinTsPinTanParameterSet(FinTsParameterSet source, List<FinTsPinTanAdvertisement> advertisements, List<FinTsSegment> unknown)
    {
        Source = source;
        Advertisements = advertisements.AsReadOnly();
        UninterpretedSegments = unknown.AsReadOnly();
    }
    public FinTsParameterSet Source { get; }
    public ReadOnlyCollection<FinTsPinTanAdvertisement> Advertisements { get; }
    public ReadOnlyCollection<FinTsSegment> UninterpretedSegments { get; }

    public static FinTsPinTanParameterSet Parse(FinTsParameterSet source, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(source);
        cancellationToken.ThrowIfCancellationRequested();
        List<FinTsPinTanAdvertisement> advertisements = [];
        List<FinTsSegment> unknown = [];
        int count = 0;
        foreach (var segment in source.UninterpretedSegments)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (segment.Code != "HIPINS") { unknown.Add(segment); continue; }
            if (++count > MaximumAdvertisements) { throw new FinTsFormatException(FinTsSyntaxError.LimitExceeded); }
            if (segment.Version != 1) { unknown.Add(segment); continue; }
            if (segment.Fields.Count != 4) { throw ParameterFields.Invalid(); }
            _ = ParameterFields.Number(segment.Fields[0], 3);
            if (ParameterFields.Number(segment.Fields[1], 1) > 3 || ParameterFields.Number(segment.Fields[2], 1) > 4) { throw ParameterFields.Invalid(); }
            var options = segment.Fields[3].Elements;
            for (int index = 0; index < 3; index++) { _ = FinTsPinTanAdvertisement.OptionalNumber(options, index); }
            for (int index = 3; index < Math.Min(5, options.Count); index++) { ParameterFields.Text(options[index], 30, false); }
            if (options.Count > 5 && (options.Count - 5) % 2 != 0) { throw ParameterFields.Invalid(); }
            List<FinTsPinTanOperation> operations = [];
            for (int index = 5; index < options.Count; index += 2)
            {
                cancellationToken.ThrowIfCancellationRequested();
                ParameterFields.Text(options[index], 6, true);
                if (!ParameterFields.IsOperation(options[index].HeaderText())) { throw ParameterFields.Invalid(); }
                if (operations.Count == MaximumOperationsPerAdvertisement) { throw new FinTsFormatException(FinTsSyntaxError.LimitExceeded); }
                operations.Add(new(options[index], options[index + 1]));
            }
            advertisements.Add(new(segment, operations));
        }
        return new(source, advertisements, unknown);
    }

    public FinTsPinTanOperationEvidence GetOperationEvidence(string operation)
    {
        if (!ParameterFields.IsOperation(operation)) { throw ParameterFields.Invalid(); }
        int unknownVersions = UninterpretedSegments.Count(s => s.Code == "HIPINS");
        if (Advertisements.Count == 0) { return FinTsPinTanOperationEvidence.Unknown; }
        if (Advertisements.Count != 1 || unknownVersions != 0 || Advertisements[0].HasConflictingPinLengthBounds) { return FinTsPinTanOperationEvidence.Ambiguous; }
        var entries = Advertisements[0].Operations.Where(o => o.Operation == operation).ToArray();
        return entries.Length switch
        {
            0 => FinTsPinTanOperationEvidence.Unlisted,
            1 => entries[0].TanRequired ? FinTsPinTanOperationEvidence.TanReportedRequired : FinTsPinTanOperationEvidence.TanReportedNotRequired,
            _ => FinTsPinTanOperationEvidence.Ambiguous,
        };
    }
}
