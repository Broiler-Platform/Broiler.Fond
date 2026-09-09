using System.Collections.ObjectModel;
using System.Globalization;

namespace Broiler.Fond.Kernel.FinTs;

public enum FinTsSynchronizationMode { SystemId = 0, LastMessageNumber = 1, SignatureReferences = 2 }
public enum FinTsSynchronizationShape { Empty, SystemId, LastMessageNumber, SignatureReferences, Conflicting }

/// <summary>HKSYN-3 observation only. A mode does not authorize synchronization or recovery.</summary>
public sealed class FinTsSynchronizationRequest
{
    private FinTsSynchronizationRequest(FinTsSegment source, FinTsSynchronizationMode mode) { Source = source; Mode = mode; }
    public FinTsSegment Source { get; }
    public FinTsSynchronizationMode Mode { get; }
    public static FinTsSynchronizationRequest Parse(FinTsSegment source, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(source); cancellationToken.ThrowIfCancellationRequested();
        if (source.Code != "HKSYN" || source.Reference is not null) { throw SynchronizationFields.Invalid(); }
        if (source.Version != 3) { throw new FinTsFormatException(FinTsSyntaxError.UnsupportedSynchronizationVersion); }
        if (source.Fields.Count != 1) { throw SynchronizationFields.Invalid(); }
        var mode = SynchronizationFields.Scalar(source.Fields[0], 1) switch
        {
            "0" => FinTsSynchronizationMode.SystemId,
            "1" => FinTsSynchronizationMode.LastMessageNumber,
            "2" => FinTsSynchronizationMode.SignatureReferences,
            _ => throw SynchronizationFields.Invalid(),
        };
        return new(source, mode);
    }
}

/// <summary>Exact HISYN-4 values. Shape describes fields only, without request/profile binding or counter updates.</summary>
public sealed class FinTsSynchronizationReport
{
    private FinTsSynchronizationReport(FinTsSegment source)
    {
        Source = source;
        if (source.Fields.Count > 4) { throw SynchronizationFields.Invalid(); }
        string Value(int index, int maximum) => source.Fields.Count > index ? SynchronizationFields.Scalar(source.Fields[index], maximum, required: false) : "";
        string system = Value(0, 30), message = Value(1, 4), signing = Value(2, 16), digital = Value(3, 16);
        SystemId = system.Length == 0 ? null : system;
        LastMessageNumber = message.Length == 0 ? null : (int)SynchronizationFields.Number(message, positive: true);
        SigningKeySecurityReference = signing.Length == 0 ? null : SynchronizationFields.Number(signing, positive: false);
        DigitalSignatureSecurityReference = digital.Length == 0 ? null : SynchronizationFields.Number(digital, positive: false);
        int groups = (SystemId is not null ? 1 : 0) + (LastMessageNumber.HasValue ? 1 : 0) +
            (SigningKeySecurityReference.HasValue || DigitalSignatureSecurityReference.HasValue ? 1 : 0);
        Shape = groups == 0 ? FinTsSynchronizationShape.Empty : groups > 1 || DigitalSignatureSecurityReference.HasValue && !SigningKeySecurityReference.HasValue
            ? FinTsSynchronizationShape.Conflicting : SystemId is not null ? FinTsSynchronizationShape.SystemId
            : LastMessageNumber.HasValue ? FinTsSynchronizationShape.LastMessageNumber : FinTsSynchronizationShape.SignatureReferences;
    }
    public FinTsSegment Source { get; }
    public int RequestSegmentNumber => Source.Reference!.Value;
    public string? SystemId { get; }
    public int? LastMessageNumber { get; }
    public ulong? SigningKeySecurityReference { get; }
    public ulong? DigitalSignatureSecurityReference { get; }
    public FinTsSynchronizationShape Shape { get; }
    public static FinTsSynchronizationReport Parse(FinTsSegment source, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(source); cancellationToken.ThrowIfCancellationRequested();
        if (source.Code != "HISYN" || source.Reference is null) { throw SynchronizationFields.Invalid(); }
        if (source.Version != 4) { throw new FinTsFormatException(FinTsSyntaxError.UnsupportedSynchronizationVersion); }
        return new(source);
    }
}

/// <summary>Bounded response observations. Missing/duplicate reports and unrelated segments remain visible.</summary>
public sealed class FinTsSynchronizationDataSet
{
    public const int MaximumReports = 128;
    private FinTsSynchronizationDataSet(FinTsResponse source, List<FinTsSynchronizationReport> reports, List<FinTsSegment> unknown)
    { Source = source; Reports = reports.AsReadOnly(); UninterpretedSegments = unknown.AsReadOnly(); }
    public FinTsResponse Source { get; }
    public ReadOnlyCollection<FinTsSynchronizationReport> Reports { get; }
    public ReadOnlyCollection<FinTsSegment> UninterpretedSegments { get; }
    public static FinTsSynchronizationDataSet Parse(FinTsResponse source, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(source); cancellationToken.ThrowIfCancellationRequested();
        List<FinTsSynchronizationReport> reports = []; List<FinTsSegment> unknown = []; int count = 0;
        foreach (var segment in source.UninterpretedSegments)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (segment.Code != "HISYN") { unknown.Add(segment); continue; }
            if (++count > MaximumReports) { throw new FinTsFormatException(FinTsSyntaxError.LimitExceeded); }
            if (segment.Version != 4) { unknown.Add(segment); continue; }
            reports.Add(FinTsSynchronizationReport.Parse(segment, cancellationToken));
        }
        return new(source, reports, unknown);
    }
}

/// <summary>Unsigned five-segment synchronization context. No authenticated dialogue, profile qualification or recovery.</summary>
public sealed class FinTsUnsignedSynchronizationRequest
{
    private FinTsUnsignedSynchronizationRequest(FinTsMessageFrame frame, FinTsInitializationIdentification identification,
        FinTsInitializationPreparation preparation, FinTsSynchronizationRequest synchronization)
    { Frame = frame; Identification = identification; Preparation = preparation; Synchronization = synchronization; }
    public FinTsMessageFrame Frame { get; }
    public FinTsInitializationIdentification Identification { get; }
    public FinTsInitializationPreparation Preparation { get; }
    public FinTsSynchronizationRequest Synchronization { get; }
    public static FinTsUnsignedSynchronizationRequest Parse(FinTsMessageFrame frame, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(frame); cancellationToken.ThrowIfCancellationRequested();
        var segments = frame.Syntax.Segments;
        if (segments.Any(s => s.Code is "HNVSK" or "HNVSD" or "HNSHK" or "HNSHA")) { throw new FinTsFormatException(FinTsSyntaxError.UnsupportedSecurityWrapper); }
        if (segments.Count != 5 || frame.MessageNumber != 1 || segments[0].Fields[2].Elements[0].HeaderText() != "0" ||
            segments[0].Fields.Count == 5 && !ParameterFields.Empty(segments[0].Fields[4])) { throw SynchronizationFields.Invalid(); }
        var identification = FinTsInitializationIdentification.Parse(segments[1], cancellationToken);
        var preparation = FinTsInitializationPreparation.Parse(segments[2], cancellationToken);
        var synchronization = FinTsSynchronizationRequest.Parse(segments[3], cancellationToken);
        if (identification.IsAnonymous || synchronization.Mode == FinTsSynchronizationMode.SystemId &&
            (identification.SystemId != "0" || identification.SystemStatus != FinTsCustomerSystemStatus.Required)) { throw SynchronizationFields.Invalid(); }
        return new(frame, identification, preparation, synchronization);
    }
}

public static class FinTsUnsignedSynchronizationWriter
{
    /// <summary>Local unsigned encoding; supplies no credentials, signature sentinel, transport or recovery action.</summary>
    public static byte[] Encode(FinTsInitializationInput input, FinTsSynchronizationMode mode, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(input); cancellationToken.ThrowIfCancellationRequested();
        if (mode is < FinTsSynchronizationMode.SystemId or > FinTsSynchronizationMode.SignatureReferences) { throw SynchronizationFields.Invalid(); }
        var body = FinTsUnsignedInitializationWriter.CreateBody(input, cancellationToken);
        body.Append("HKSYN:4:3+").Append(((int)mode).ToString(CultureInfo.InvariantCulture)).Append('\'');
        byte[] wire = FinTsUnsignedWireEncoding.Frame("0", 1, body, 5);
        _ = FinTsUnsignedSynchronizationRequest.Parse(FinTsMessageFrame.Parse(wire, cancellationToken), cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();
        return wire;
    }
}

internal static class SynchronizationFields
{
    internal static FinTsFormatException Invalid() => new(FinTsSyntaxError.InvalidSynchronization);
    internal static string Scalar(FinTsField field, int maximum, bool required = true)
    {
        if (field.Elements.Count != 1 || field.Elements[0].IsBinary) { throw Invalid(); }
        string text = field.Elements[0].HeaderText();
        if (text.Length > maximum || required && text.Length == 0 || text.Length != 0 && (text[0] == ' ' || text[^1] == ' ') ||
            text.Any(c => c < 32 || c is >= (char)127 and <= (char)160)) { throw Invalid(); }
        return text;
    }
    internal static ulong Number(string value, bool positive)
    {
        if (value.Any(c => c is < '0' or > '9') || value.Length > 1 && value[0] == '0' || positive && value == "0") { throw Invalid(); }
        return ulong.Parse(value, CultureInfo.InvariantCulture);
    }
}
