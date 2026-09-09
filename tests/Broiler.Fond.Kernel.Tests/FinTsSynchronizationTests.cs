using System.Globalization;
using System.Reflection;
using System.Text;
using System.Text.Json;
using Broiler.Fond.Kernel.FinTs;

namespace Broiler.Fond.Kernel.Tests;

internal static class FinTsSynchronizationTests
{
    internal static void Run(Action<bool, string> check)
    {
        int count = 0;
        void Verify(bool condition, string message) { count++; check(condition, message); }
        void Reject(Action action, FinTsSyntaxError expected = FinTsSyntaxError.InvalidSynchronization)
        {
            try { action(); Verify(false, "Invalid synchronization input must fail."); }
            catch (FinTsFormatException error)
            { Verify(error.Error == expected && !error.ToString().Contains("PUBLIC-SECRET", StringComparison.Ordinal), "Synchronization errors have fixed categories without source data."); }
        }
        using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream("FinTs.synchronization-v1.json")!;
        using var doc = JsonDocument.Parse(stream);
        var vectors = doc.RootElement.GetProperty("vectors").EnumerateArray().ToArray();
        foreach (var vector in vectors)
        {
            byte[] wire = Convert.FromBase64String(vector.GetProperty("requestBase64").GetString()!);
            var mode = (FinTsSynchronizationMode)vector.GetProperty("mode").GetInt32();
            Verify(FinTsUnsignedSynchronizationWriter.Encode(Input(vector), mode).SequenceEqual(wire), "Unsigned synchronization bytes match the independent producer.");
            var frame = FinTsMessageFrame.Parse(wire); var request = FinTsUnsignedSynchronizationRequest.Parse(frame);
            Verify(request.Synchronization.Mode == mode && frame.MessageNumber == 1 && frame.Syntax.Segments.Count == 5, "The exact mode and restricted first-message layout survive parsing.");
            Verify(ReferenceEquals(frame, request.Frame) && ReferenceEquals(request.Synchronization.Source, frame.Syntax.Segments[3]) && ReferenceEquals(request.Identification.Source, frame.Syntax.Segments[1]), "Request observations preserve source identity.");
            var response = FinTsResponse.Parse(FinTsMessageFrame.Parse(Convert.FromBase64String(vector.GetProperty("responseBase64").GetString()!)));
            var data = FinTsSynchronizationDataSet.Parse(response);
            var expected = vector.GetProperty("reports");
            Verify(ReferenceEquals(data.Source, response) && data.Reports.Count == expected.GetArrayLength() && data.UninterpretedSegments.Count == vector.GetProperty("uninterpreted").GetInt32(), "Missing, duplicate and unknown response shapes remain explicit.");
            for (int i = 0; i < data.Reports.Count; i++)
            {
                var actual = data.Reports[i]; var model = expected[i];
                int? message = model.GetProperty("messageNumber").ValueKind == JsonValueKind.Null ? null : model.GetProperty("messageNumber").GetInt32();
                ulong? Number(string name) => model.GetProperty(name).GetString() is { } value ? ulong.Parse(value, CultureInfo.InvariantCulture) : null;
                Verify(actual.Shape.ToString() == model.GetProperty("shape").GetString() && actual.SystemId == model.GetProperty("systemId").GetString() && actual.LastMessageNumber == message &&
                    actual.SigningKeySecurityReference == Number("signingReference") && actual.DigitalSignatureSecurityReference == Number("digitalReference"), "Every returned field matches independently chosen exact values and shape.");
                Verify(actual.RequestSegmentNumber == 4 && ReferenceEquals(actual.Source, response.UninterpretedSegments[i]), "Reports retain their request reference and source order.");
            }
            Verify(response.Frame.Syntax.CopyWireBytes().SequenceEqual(Convert.FromBase64String(vector.GetProperty("responseBase64").GetString()!)), "Response spelling and omissions remain byte-exact.");
            Verify(new object[] { request, request.Synchronization, data }.Concat(data.Reports).All(o => !o.ToString()!.Contains("PUBLIC", StringComparison.Ordinal)), "Default schema diagnostics exclude identifiers and counters.");
            wire[0] = 0; Verify(request.Frame.Syntax.CopyWireBytes()[0] == 'H', "Parsed source buffers remain isolated from caller mutations.");
        }
        Verify(vectors.Length == 10, "All ten synchronization fixtures ran.");
        foreach (string segment in new[] { "HKSYN:4:3'", "HKSYN:4:3+'", "HKSYN:4:3+3'", "HKSYN:4:3+00'", "HKSYN:4:3+0+'", "HKSYN:4:3+0:1'", "HKSYN:4:3+@1@0'", "HKSYN:4:3:2+0'", "HISYN:4:3+0'" })
        { Reject(() => FinTsSynchronizationRequest.Parse(Segment(segment))); }
        Reject(() => FinTsSynchronizationRequest.Parse(Segment("HKSYN:4:2+0'")), FinTsSyntaxError.UnsupportedSynchronizationVersion);
        Reject(() => FinTsSynchronizationReport.Parse(Segment("HISYN:4:3:4+PUBLIC-SECRET'")), FinTsSyntaxError.UnsupportedSynchronizationVersion);
        foreach (string segment in new[]
        {
            "HISYN:4:4+PUBLIC-SECRET'", "HKSYN:4:4:4+PUBLIC-SECRET'", "HISYN:4:4:4+PUBLIC-SECRET++++'",
            "HISYN:4:4:4+PUBLIC-SECRET:extra'", "HISYN:4:4:4+@0@'", "HISYN:4:4:4++0'", "HISYN:4:4:4++10000'",
            "HISYN:4:4:4++01'", "HISYN:4:4:4++-1'", "HISYN:4:4:4++1,0'", "HISYN:4:4:4+++01'",
            "HISYN:4:4:4+++10000000000000000'", "HISYN:4:4:4++++10000000000000000'",
            "HISYN:4:4:4+++1e3'", "HISYN:4:4:4++++-1'", "HISYN:4:4:4+++@1@1'", "HISYN:4:4:4++++@1@1'",
            "HISYN:4:4:4+ PUBLIC-SECRET'", "HISYN:4:4:4+PUBLIC-SECRET '", "HISYN:4:4:4+PUBLIC-SECRET\n'",
            "HISYN:4:4:4+" + new string('s', 31) + "'",
        }) { Reject(() => FinTsSynchronizationReport.Parse(Segment(segment))); }
        var zeroSystem = FinTsSynchronizationReport.Parse(Segment("HISYN:4:4:4+0'"));
        Verify(zeroSystem.SystemId == "0" && zeroSystem.Shape == FinTsSynchronizationShape.SystemId, "A reported zero system ID remains untrusted text for later assignment checks.");
        Verify(FinTsSynchronizationReport.Parse(Segment("HISYN:4:4:4+" + new string('s', 30) + "'")).SystemId!.Length == 30, "The system-ID field limit is inclusive.");
        Verify(FinTsSynchronizationReport.Parse(Segment("HISYN:4:4:4+++0+0'")).Shape == FinTsSynchronizationShape.SignatureReferences, "Zero signature references remain distinct from absent fields.");
        foreach (string wire in new[] { "HISYN:4:4:4'", "HISYN:4:4:4+'", "HISYN:4:4:4++++'" })
        { Verify(FinTsSynchronizationReport.Parse(Segment(wire)).Shape == FinTsSynchronizationShape.Empty, "Omitted and explicit empty reports retain empty shape without inventing values."); }
        foreach (string wire in new[] { "HISYN:4:4:4+S++1'", "HISYN:4:4:4++1+1'", "HISYN:4:4:4+S+1+1+1'" })
        { Verify(FinTsSynchronizationReport.Parse(Segment(wire)).Shape == FinTsSynchronizationShape.Conflicting, "Cross-mode field groups are preserved as conflicting evidence."); }
        string baseWire = Encoding.Latin1.GetString(Convert.FromBase64String(vectors[0].GetProperty("requestBase64").GetString()!));
        foreach (Func<string, string> edit in new Func<string, string>[]
        {
            s => s.Replace("+300+0+1'", "+300+ESTABLISHED+1'", StringComparison.Ordinal),
            s => s.Replace("+300+0+1'", "+300+0+2'", StringComparison.Ordinal).Replace("HNHBS:5:1+1'", "HNHBS:5:1+2'", StringComparison.Ordinal),
            s => s.Replace("+300+0+1'", "+300+0+1+0:1'", StringComparison.Ordinal),
            s => s.Replace("PUBLIC-CUSTOMER+0+1'", "9999999999+0+0'", StringComparison.Ordinal),
            s => s.Replace("PUBLIC-CUSTOMER+0+1'", "PUBLIC-CUSTOMER+PUBLIC-SYSTEM+1'", StringComparison.Ordinal),
            s => s.Replace("PUBLIC-CUSTOMER+0+1'", "PUBLIC-CUSTOMER+0+0'", StringComparison.Ordinal),
            s => s.Replace("HNHBS:5:1", "HKSPA:5:1'HNHBS:6:1", StringComparison.Ordinal),
        }) { Reject(() => FinTsUnsignedSynchronizationRequest.Parse(Frame(edit(baseWire)))); }
        Reject(() => FinTsUnsignedSynchronizationRequest.Parse(Frame(baseWire.Replace("HKIDN:2:2", "HNSHK:2:4", StringComparison.Ordinal))), FinTsSyntaxError.UnsupportedSecurityWrapper);
        Verify(FinTsUnsignedSynchronizationRequest.Parse(Frame(baseWire.Replace("+300+0+1'", "+300+0+1+'", StringComparison.Ordinal))).Frame.Syntax.Segments[0].Fields.Count == 5, "Empty optional outer reference remains an omission.");
        Reject(() => FinTsUnsignedInitializationRequest.Parse(Frame(baseWire)), FinTsSyntaxError.InvalidInitialization);
        Reject(() => FinTsReadRequestContext.Parse(Frame(baseWire), 1), FinTsSyntaxError.InvalidReadData);
        var input = Input(vectors[0]);
        foreach (int mode in new[] { -1, 3, int.MaxValue }) { Reject(() => FinTsUnsignedSynchronizationWriter.Encode(input, (FinTsSynchronizationMode)mode)); }
        Reject(() => FinTsUnsignedSynchronizationWriter.Encode(Input(vectors[1]), FinTsSynchronizationMode.SystemId));
        var anon = new FinTsInitializationInput("280", "PUBLIC-BANK", "9999999999", "0", FinTsCustomerSystemStatus.NotRequired, 0, 0, FinTsDialogueLanguage.Standard, "PUBLIC-PRODUCT", "1.0");
        foreach (var mode in Enum.GetValues<FinTsSynchronizationMode>()) { Reject(() => FinTsUnsignedSynchronizationWriter.Encode(anon, mode)); }
        // Unknown HISYN versions count toward the same resource budget and stay opaque.
        foreach (int version in new[] { 4, 5 })
        {
            var maximum = FinTsSynchronizationDataSet.Parse(Many(128, version));
            Verify(maximum.Reports.Count + maximum.UninterpretedSegments.Count == 128 && (version == 4 ? maximum.Reports.Count : maximum.UninterpretedSegments.Count) == 128, "The report budget includes supported and unknown versions.");
            Reject(() => FinTsSynchronizationDataSet.Parse(Many(129, version)), FinTsSyntaxError.LimitExceeded);
        }
        var culture = CultureInfo.CurrentCulture; byte[] canonical = FinTsUnsignedSynchronizationWriter.Encode(input, FinTsSynchronizationMode.SystemId);
        try
        {
            foreach (string name in new[] { "de-DE", "ar-SA", "tr-TR" })
            { CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo(name); Verify(FinTsUnsignedSynchronizationWriter.Encode(input, FinTsSynchronizationMode.SystemId).SequenceEqual(canonical), "Synchronization encoding is culture invariant."); }
        }
        finally { CultureInfo.CurrentCulture = culture; }
        using var cancelled = new CancellationTokenSource(); cancelled.Cancel();
        foreach (Action action in new Action[]
        {
            () => FinTsSynchronizationRequest.Parse(Segment("HKSYN:4:3+0'"), cancelled.Token),
            () => FinTsSynchronizationReport.Parse(Segment("HISYN:4:4:4+S'"), cancelled.Token),
            () => FinTsSynchronizationDataSet.Parse(Many(1, 4), cancelled.Token),
            () => FinTsUnsignedSynchronizationRequest.Parse(Frame(baseWire), cancelled.Token),
            () => FinTsUnsignedSynchronizationWriter.Encode(input, FinTsSynchronizationMode.SystemId, cancelled.Token),
        })
        {
            try { action(); Verify(false, "Cancelled schema/encoding work must return no output."); }
            catch (OperationCanceledException) { Verify(true, "Cancellation is propagated."); }
        }
        foreach (Action action in new Action[]
        {
            () => FinTsSynchronizationRequest.Parse(null!), () => FinTsSynchronizationReport.Parse(null!),
            () => FinTsSynchronizationDataSet.Parse(null!), () => FinTsUnsignedSynchronizationRequest.Parse(null!),
            () => FinTsUnsignedSynchronizationWriter.Encode(null!, FinTsSynchronizationMode.SystemId),
        })
        {
            try { action(); Verify(false, "Null synchronization source must fail."); }
            catch (ArgumentNullException) { Verify(true, "Null input rejected."); }
        }
        Console.WriteLine($"FinTS synchronization schema/encoding verification passed ({vectors.Length} independent vectors, {count} checks).");
    }
    private static FinTsInitializationInput Input(JsonElement vector)
    {
        var v = vector.GetProperty("input");
        return new(v.GetProperty("country").GetString()!, v.GetProperty("institution").GetString()!, v.GetProperty("customerId").GetString()!, v.GetProperty("systemId").GetString()!,
            (FinTsCustomerSystemStatus)v.GetProperty("systemStatus").GetInt32(), v.GetProperty("bankParameterVersion").GetInt32(), v.GetProperty("userParameterVersion").GetInt32(),
            (FinTsDialogueLanguage)v.GetProperty("language").GetInt32(), v.GetProperty("productIdentifier").GetString()!, v.GetProperty("productVersion").GetString()!);
    }
    private static FinTsSegment Segment(string text) => FinTsSyntax.ParseSegments(Encoding.Latin1.GetBytes(text)).Segments[0];
    private static FinTsMessageFrame Frame(string wire) => FinTsMessageFrame.Parse(Encoding.Latin1.GetBytes(wire[..10] + Encoding.Latin1.GetByteCount(wire).ToString("D12", CultureInfo.InvariantCulture) + wire[22..]));
    private static FinTsResponse Many(int count, int version)
    {
        string body = "HIRMG:2:2+0010::PUBLIC'" + string.Concat(Enumerable.Range(3, count).Select(n => $"HISYN:{n.ToString(CultureInfo.InvariantCulture)}:{version.ToString(CultureInfo.InvariantCulture)}:4+PUBLIC-SYSTEM'"));
        return FinTsResponse.Parse(Frame("HNHBK:1:3+000000000000+300+SYNTHETIC+1+SYNTHETIC:1'" + body + "HNHBS:" + (count + 3).ToString(CultureInfo.InvariantCulture) + ":1+1'"));
    }
}
