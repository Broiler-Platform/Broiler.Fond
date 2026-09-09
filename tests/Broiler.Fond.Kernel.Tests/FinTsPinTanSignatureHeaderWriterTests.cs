using System.Globalization;
using System.Reflection;
using System.Runtime.ExceptionServices;
using System.Text;
using System.Text.Json;
using Broiler.Fond.Kernel.FinTs;

namespace Broiler.Fond.Kernel.Tests;

internal static class FinTsPinTanSignatureHeaderWriterTests
{
    internal static void Run(Action<bool, string> check)
    {
        int count = 0;
        void Verify(bool condition, string message) { count++; check(condition, message); }
        using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream("FinTs.pin-tan-signature-encoding-v1.json")!;
        using var doc = JsonDocument.Parse(stream);
        var vectors = doc.RootElement.GetProperty("vectors").EnumerateArray().ToArray();
        foreach (var v in vectors)
        {
            var input = Input(v); int number = v.GetProperty("segmentNumber").GetInt32();
            byte[] expected = Convert.FromBase64String(v.GetProperty("wireBase64").GetString()!);
            byte[] wire = FinTsPinTanSignatureHeaderWriter.Encode(input, number);
            Verify(wire.SequenceEqual(expected), $"Independent exact signature-header bytes match {v.GetProperty("name").GetString()}.");
            var syntax = FinTsSyntax.ParseSegments(wire); var header = FinTsPinTanSignatureHeader.Parse(syntax.Segments.Single());
            foreach (var property in typeof(FinTsPinTanSignatureHeaderInput).GetProperties())
            {
                object? observed = typeof(FinTsPinTanSignatureHeader).GetProperty(property.Name)!.GetValue(header);
                Verify(Format(observed) == Format(property.GetValue(input)), "Typed header value survives without normalization or truncation: " + property.Name);
            }
            Verify(header.Source.Number == number && header.Source.Fields.Count == 11 && header.Source.Fields[8].Elements.Count == 3 &&
                header.Source.Fields[7].Elements.Count == (input.SecurityDate is null ? 1 : input.SecurityTime is null ? 2 : 3), "Optional empty certificate/hash/timestamp tails are omitted canonically.");
            wire[0] = 0;
            Verify(syntax.CopyWireBytes().SequenceEqual(expected) && FinTsPinTanSignatureHeaderWriter.Encode(input, number).SequenceEqual(expected), "Returned byte arrays are caller-owned and cannot mutate parsed source or future encoding.");
        }
        Verify(vectors.Length == 10, "All ten independent encoding fixtures ran.");
        var sample = vectors[0]; var basic = Input(sample);
        void Reject(string property, object? value, FinTsSyntaxError error = FinTsSyntaxError.InvalidSignatureHeader)
        {
            try { FinTsPinTanSignatureHeaderWriter.Encode(Input(sample, property, value), 2); Verify(false, "Invalid typed header input must fail."); }
            catch (FinTsFormatException failure) { Verify(failure.Error == error && !failure.ToString().Contains("PUBLIC", StringComparison.Ordinal), "Invalid typed input produces fixed expected diagnostics: " + property); }
        }
        foreach (int number in new[] { -1, 0, 1000, int.MaxValue })
        {
            try { FinTsPinTanSignatureHeaderWriter.Encode(basic, number); Verify(false, "Invalid segment number must fail."); }
            catch (FinTsFormatException error) { Verify(error.Error == FinTsSyntaxError.InvalidSignatureHeader, "Segment-number bounds are checked before encoding."); }
        }
        Verify(FinTsSyntax.ParseSegments(FinTsPinTanSignatureHeaderWriter.Encode(basic, 1)).Segments.Single().Number == 1, "The standalone codec accepts the schema minimum; request context separately requires position 2.");
        foreach (string property in new[] { "profileVersion", "securityFunction", "securitySupplierRole", "securityParty", "keyNumber", "keyVersion" })
            foreach (int value in new[] { -1, 1000, int.MaxValue }) { Reject(property, value); }
        foreach (int value in new[] { 0, 3, 999 }) { Reject("profileVersion", value, FinTsSyntaxError.UnsupportedSignatureHeader); }
        foreach (int value in new[] { 0, 899, 900, 998 }) { Reject("securityFunction", value); }
        foreach (int value in new[] { 0, 2, 5 }) { Reject("securitySupplierRole", value); }
        foreach (int value in new[] { 0, 3 }) { Reject("securityParty", value); }
        Reject("securityReferenceNumber", 10000000000000000UL); Reject("securityReferenceNumber", ulong.MaxValue);
        Reject("securityDate", null); // The supplied time requires a date.
        Reject("securityTime", new TimeOnly(12, 34, 56).Add(TimeSpan.FromTicks(1)));
        Reject("securityTime", new TimeOnly(12, 34, 56, 500));
        foreach (string property in new[] { "controlReference", "systemId", "hashAlgorithmCode", "signatureAlgorithmCode", "operationModeCode", "countryCode", "institutionId", "userId" })
        {
            foreach (string value in new[] { "PUBLIC\n", "€", "\ud800", "\u0085", "\u00a0" }) { Reject(property, value); }
            try { _ = Input(sample, property, null); Verify(false, "Null input field must fail."); }
            catch (ArgumentNullException) { Verify(true, "Null text field is rejected at input construction."); }
        }
        foreach (string property in new[] { "controlReference", "systemId", "signatureAlgorithmCode", "operationModeCode", "countryCode", "userId" }) { Reject(property, ""); }
        Reject("controlReference", "0"); Reject("controlReference", new string('R', 15));
        foreach (string property in new[] { "systemId", "institutionId", "userId" }) { Reject(property, new string('x', 31)); }
        foreach (string property in new[] { "controlReference", "systemId", "institutionId", "userId" })
        {
            Reject(property, " padded"); Reject(property, "padded ");
            var input = Input(sample, property, "X+:'?@ü");
            var header = FinTsPinTanSignatureHeader.Parse(FinTsSyntax.ParseSegments(FinTsPinTanSignatureHeaderWriter.Encode(input, 2)).Segments.Single());
            string name = char.ToUpperInvariant(property[0]) + property[1..];
            Verify((string)typeof(FinTsPinTanSignatureHeader).GetProperty(name)!.GetValue(header)! == "X+:'?@ü", "Escaping preserves delimiters as one field rather than injecting segments.");
        }
        foreach (string code in new[] { "3", "4", "5", "6", "999" })
        {
            byte[] wire = FinTsPinTanSignatureHeaderWriter.Encode(Input(sample, "hashAlgorithmCode", code), 2);
            Verify(FinTsPinTanSignatureHeader.Parse(FinTsSyntax.ParseSegments(wire).Segments.Single()).HashAlgorithmCode == code, "Schema-supported hash observations encode without selecting cryptography.");
        }
        Reject("hashAlgorithmCode", "777", FinTsSyntaxError.UnsupportedSignatureHeader);
        Reject("hashAlgorithmCode", "1", FinTsSyntaxError.UnsupportedSignatureHeader);
        Reject("hashAlgorithmCode", "");
        foreach (string property in new[] { "signatureAlgorithmCode", "operationModeCode" })
        { Reject(property, "ABC"); Reject(property, "1000"); Reject(property, "-1"); }
        Reject("countryCode", "28"); Reject("countryCode", "28X");
        using var cancelled = new CancellationTokenSource(); cancelled.Cancel();
        try { FinTsPinTanSignatureHeaderWriter.Encode(basic, 2, cancelled.Token); Verify(false, "Cancelled encoding must fail."); }
        catch (OperationCanceledException) { Verify(true, "Encoding observes cancellation."); }
        try { FinTsPinTanSignatureHeaderWriter.Encode(null!, 2); Verify(false, "Null encoding input must fail."); }
        catch (ArgumentNullException) { Verify(true, "Null encoding input is rejected."); }
        var culture = CultureInfo.CurrentCulture;
        try
        {
            foreach (string name in new[] { "de-DE", "ar-SA", "tr-TR" })
            {
                CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo(name);
                Verify(FinTsPinTanSignatureHeaderWriter.Encode(basic, 2).SequenceEqual(Convert.FromBase64String(sample.GetProperty("wireBase64").GetString()!)), "Wire bytes are culture-independent.");
            }
        }
        finally { CultureInfo.CurrentCulture = culture; }
        Verify(typeof(FinTsPinTanSignatureHeaderInput).GetProperties().All(p => p.SetMethod is null) && !basic.ToString()!.Contains("PUBLIC", StringComparison.Ordinal), "Input is immutable and default diagnostics exclude identifiers.");
        // Encode the candidate used by the request-context fixture, then compare against the pinned independent origin.
        using var contextStream = Assembly.GetExecutingAssembly().GetManifestResourceStream("FinTs.pin-tan-signature-context-v1.json")!;
        using var contextDoc = JsonDocument.Parse(contextStream);
        var contextFixture = contextDoc.RootElement.GetProperty("vectors")[0];
        var request = FinTsUnsignedInitializationRequest.Parse(Frame(contextFixture, "requestBase64"));
        var context = FinTsPinTanSignatureRequestContext.ForInitialization(request, "PUBLIC-USER", "PUBLIC-REF", new(2, 900, 7));
        var response = FinTsResponse.Parse(Frame(contextFixture, "procedureResponseBase64"));
        var procedures = new FinTsPinTanProcedureContext(FinTsUnsignedInitializationRequest.Parse(Frame(contextFixture, "originRequestBase64")),
            FinTsTanParameterSet.Parse(FinTsParameterSet.Parse(response)), FinTsPermittedProcedureSet.Parse(response));
        var candidate = FinTsPinTanSignatureHeader.Parse(FinTsSyntax.ParseSegments(FinTsPinTanSignatureHeaderWriter.Encode(Input(vectors[1]), 2)).Segments.Single());
        Verify(FinTsPinTanSignatureEvidence.Evaluate(context, candidate, procedures).HasMatchingEvidence, "Encoded candidate integrates with explicit identity and procedure comparison.");
        candidate = FinTsPinTanSignatureHeader.Parse(FinTsSyntax.ParseSegments(FinTsPinTanSignatureHeaderWriter.Encode(Input(vectors[1]), 3)).Segments.Single());
        Verify(FinTsPinTanSignatureEvidence.Evaluate(context, candidate, procedures).Issues.HasFlag(FinTsPinTanSignatureIssue.HeaderRoleNeedsReview), "Schema-valid encoding does not bypass the request comparator's stricter position requirements.");
        Console.WriteLine($"FinTS PIN/TAN signature-header encoding verification passed ({vectors.Length} independent vectors, {count} checks).");
    }
    private static string? Format(object? value) => value switch
    {
        DateOnly date => date.ToString("yyyyMMdd", CultureInfo.InvariantCulture),
        TimeOnly time => time.ToString("HHmmss", CultureInfo.InvariantCulture),
        IFormattable formattable => formattable.ToString(null, CultureInfo.InvariantCulture),
        _ => value?.ToString(),
    };
    private static FinTsPinTanSignatureHeaderInput Input(JsonElement vector, string? changed = null, object? replacement = null)
    {
        var input = vector.GetProperty("input"); var constructor = typeof(FinTsPinTanSignatureHeaderInput).GetConstructors().Single();
        object?[] args = constructor.GetParameters().Select(p =>
        {
            if (p.Name == changed) { return replacement; }
            var value = input.GetProperty(p.Name!);
            if (value.ValueKind == JsonValueKind.Null) { return null; }
            if (p.ParameterType == typeof(int)) { return (object)(value.ValueKind == JsonValueKind.String ? int.Parse(value.GetString()!, CultureInfo.InvariantCulture) : value.GetInt32()); }
            if (p.ParameterType == typeof(ulong)) { return ulong.Parse(value.GetString()!, CultureInfo.InvariantCulture); }
            if (p.ParameterType == typeof(DateOnly?)) { return DateOnly.ParseExact(value.GetString()!, "yyyyMMdd", CultureInfo.InvariantCulture); }
            if (p.ParameterType == typeof(TimeOnly?)) { return TimeOnly.ParseExact(value.GetString()!, "HHmmss", CultureInfo.InvariantCulture); }
            return (object?)value.GetString();
        }).ToArray();
        try { return (FinTsPinTanSignatureHeaderInput)constructor.Invoke(args); }
        catch (TargetInvocationException error) when (error.InnerException is not null) { ExceptionDispatchInfo.Capture(error.InnerException).Throw(); throw; }
    }
    private static FinTsMessageFrame Frame(JsonElement v, string property) => FinTsMessageFrame.Parse(Convert.FromBase64String(v.GetProperty(property).GetString()!));
}
