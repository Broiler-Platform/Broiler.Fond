using System.Globalization;
using System.Reflection;
using System.Text;
using System.Text.Json;
using Broiler.Fond.Kernel.FinTs;

namespace Broiler.Fond.Kernel.Tests;

internal static class FinTsInitializationTests
{
    internal static void Run(Action<bool, string> check)
    {
        int count = 0;
        void Verify(bool condition, string message) { count++; check(condition, message); }
        void Reject(Action action, FinTsSyntaxError expected = FinTsSyntaxError.InvalidInitialization)
        {
            try { action(); Verify(false, "Invalid initialization must not return an observation or wire bytes."); }
            catch (FinTsFormatException error)
            {
                Verify(error.Error == expected, "Initialization fails with the expected fixed category.");
                Verify(!error.ToString().Contains("PUBLIC-SECRET", StringComparison.Ordinal), "Initialization errors do not echo source data.");
            }
        }
        using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream("FinTs.initialization-v1.json")!;
        using var doc = JsonDocument.Parse(stream);
        var vectors = doc.RootElement.GetProperty("vectors").EnumerateArray().ToArray();
        foreach (var vector in vectors)
        {
            var input = Input(vector);
            byte[] expected = Convert.FromBase64String(vector.GetProperty("wireBase64").GetString()!);
            byte[] actual = FinTsUnsignedInitializationWriter.Encode(input);
            Verify(actual.SequenceEqual(expected), $"Initialization wire matches independent fixture {vector.GetProperty("name").GetString()}.");
            var frame = FinTsMessageFrame.Parse(expected);
            var request = FinTsUnsignedInitializationRequest.Parse(frame);
            var id = request.Identification; var preparation = request.Preparation;
            Verify(ReferenceEquals(request.Frame, frame) && ReferenceEquals(id.Source, frame.Syntax.Segments[1]) && ReferenceEquals(preparation.Source, frame.Syntax.Segments[2]), "Schema observations retain exact source object identity.");
            Verify(frame.MessageNumber == 1 && frame.Syntax.Segments.Count == 4 && Encoding.Latin1.GetString(frame.Syntax.Segments[0].Fields[2].Elements[0].CopyValueBytes()) == "0", "Initialization starts with fixed dialogue zero and message one.");
            Verify(id.Country == input.Country && id.Institution == input.Institution && id.CustomerId == input.CustomerId && id.SystemId == input.SystemId && id.SystemStatus == input.SystemStatus, "Identification strings and system-status codes survive exactly.");
            Verify(preparation.BankParameterVersion == input.BankParameterVersion && preparation.UserParameterVersion == input.UserParameterVersion && preparation.Language == input.Language && preparation.ProductIdentifier == input.ProductIdentifier && preparation.ProductVersion == input.ProductVersion, "Preparation versions, language and product fields survive exactly.");
            Verify(id.IsAnonymous == vector.GetProperty("anonymous").GetBoolean(), "Anonymous marker is explicit without interpreting other identities.");
            if (id.IsAnonymous)
            {
                Verify(FinTsUnsignedInitializationWriter.EncodeAnonymous(input.Country, input.Institution, input.ProductIdentifier, input.ProductVersion, input.BankParameterVersion, input.UserParameterVersion, input.Language).SequenceEqual(expected), "Anonymous helper emits the prescribed identity tuple.");
            }
            actual[0] = 0; expected[0] = 0;
            Verify(frame.Syntax.CopyWireBytes()[0] == 'H', "Caller mutations cannot change retained source bytes.");
            Verify(new object[] { input, request, id, preparation }.All(o => !o.ToString()!.Contains("PUBLIC", StringComparison.Ordinal)), "Default diagnostics omit explicit identifiers and product data.");
        }
        Verify(vectors.Length == 8 && FinTsInitializationIdentification.AnonymousCustomerId == "9999999999", "Eight fixtures ran and the PDF footnote is excluded from the ten-digit anonymous marker.");
        var basis = Input(vectors[2]);
        string wire = Encoding.Latin1.GetString(Convert.FromBase64String(vectors[2].GetProperty("wireBase64").GetString()!));
        FinTsUnsignedInitializationRequest Parse(string value) => FinTsUnsignedInitializationRequest.Parse(Frame(value));
        foreach (string changed in new[]
        {
            wire.Replace("+300+0+1'", "+300+ESTABLISHED+1'", StringComparison.Ordinal),
            wire.Replace("+300+0+1'", "+300+0+2'", StringComparison.Ordinal).Replace("HNHBS:4:1+1'", "HNHBS:4:1+2'", StringComparison.Ordinal),
            wire.Replace("+300+0+1'", "+300+0+1+0:1'", StringComparison.Ordinal),
            wire.Replace("HKIDN:2:2", "HKIDN:2:2:1", StringComparison.Ordinal),
            wire.Replace("HKVVB:3:3", "HKVVB:3:3:2", StringComparison.Ordinal),
            wire.Replace("HKIDN:2:2", "ZID:2:2", StringComparison.Ordinal),
            wire.Replace("HKVVB:3:3", "ZPREP:3:3", StringComparison.Ordinal),
            wire.Replace("HNHBS:4:1", "HKSPA:4:1'HNHBS:5:1", StringComparison.Ordinal),
        }) { Reject(() => Parse(changed)); }
        foreach (string changed in new[] { wire.Replace("HKIDN:2:2", "HKIDN:2:1", StringComparison.Ordinal), wire.Replace("HKVVB:3:3", "HKVVB:3:4", StringComparison.Ordinal) })
        { Reject(() => Parse(changed), FinTsSyntaxError.UnsupportedInitializationVersion); }
        Reject(() => Parse(wire.Replace("HKIDN:2:2", "HNSHK:2:4", StringComparison.Ordinal)), FinTsSyntaxError.UnsupportedSecurityWrapper);
        Verify(Parse(wire.Replace("+300+0+1'", "+300+0+1+'", StringComparison.Ordinal)).Frame.Syntax.Segments[0].Fields.Count == 5, "An empty optional outer reference remains an omission.");
        var unsignedRead = FinTsMessageFrame.Parse(FinTsUnsignedReadRequestWriter.EncodeDiscovery("SYNTHETIC", 1, []));
        Reject(() => FinTsUnsignedInitializationRequest.Parse(unsignedRead));
        Reject(() => FinTsReadRequestContext.Parse(Frame(wire), 1), FinTsSyntaxError.InvalidReadData);
        foreach (string segment in new[]
        {
            "HKIDN:2:2+280:PUBLIC-BANK+PUBLIC-SECRET+PUBLIC-SYSTEM'",
            "HKIDN:2:2+280:PUBLIC-BANK+PUBLIC-SECRET+PUBLIC-SYSTEM+1+'",
            "HKIDN:2:2+280+PUBLIC-SECRET+PUBLIC-SYSTEM+1'",
            "HKIDN:2:2+280:PUBLIC-BANK:extra+PUBLIC-SECRET+PUBLIC-SYSTEM+1'",
            "HKIDN:2:2+280:+PUBLIC-SECRET+PUBLIC-SYSTEM+1'",
            "HKIDN:2:2+28A:PUBLIC-BANK+PUBLIC-SECRET+PUBLIC-SYSTEM+1'",
            "HKIDN:2:2+280:PUBLIC-BANK+PUBLIC-SECRET+PUBLIC-SYSTEM+2'",
            "HKIDN:2:2+280:PUBLIC-BANK+PUBLIC-SECRET+PUBLIC-SYSTEM+01'",
            "HKIDN:2:2+280:PUBLIC-BANK+PUBLIC-SECRET+PUBLIC-SYSTEM+1:extra'",
            "HKIDN:2:2+280:PUBLIC-BANK+9999999999+PUBLIC-SYSTEM+0'",
            "HKIDN:2:2+280:PUBLIC-BANK+9999999999+0+1'",
        }) { Reject(() => FinTsInitializationIdentification.Parse(Segment(segment))); }
        foreach (string segment in new[]
        {
            "HKVVB:3:3+1+2+0+PUBLIC-PRODUCT'", "HKVVB:3:3+1+2+0+PUBLIC-PRODUCT+1.0+'",
            "HKVVB:3:3+01+2+0+PUBLIC-PRODUCT+1.0'", "HKVVB:3:3+1+02+0+PUBLIC-PRODUCT+1.0'",
            "HKVVB:3:3+-1+2+0+PUBLIC-PRODUCT+1.0'", "HKVVB:3:3+1000+2+0+PUBLIC-PRODUCT+1.0'",
            "HKVVB:3:3+1+2+4+PUBLIC-PRODUCT+1.0'", "HKVVB:3:3+1+2+00+PUBLIC-PRODUCT+1.0'",
            "HKVVB:3:3+1:extra+2+0+PUBLIC-PRODUCT+1.0'", "HKVVB:3:3+1+2+0++1.0'",
        }) { Reject(() => FinTsInitializationPreparation.Parse(Segment(segment))); }
        // Every scalar slot rejects binary substitution, even if its decoded bytes look valid.
        foreach (string value in new[] { "280", "PUBLIC-BANK", "PUBLIC-CUSTOMER", "PUBLIC-SYSTEM" })
        { Reject(() => Parse(wire.Replace(value, $"@{value.Length}@{value}", StringComparison.Ordinal))); }
        foreach (string value in new[] { "1", "2", "0", "PUBLIC-PRODUCT", "1.0" })
        {
            string[] fields = ["1", "2", "0", "PUBLIC-PRODUCT", "1.0"];
            int index = Array.IndexOf(fields, value); fields[index] = $"@{value.Length}@{value}";
            Reject(() => FinTsInitializationPreparation.Parse(Segment("HKVVB:3:3+" + string.Join('+', fields) + "'")));
        }
        for (int field = 0; field < 6; field++)
        {
            int maximum = new[] { 3, 30, 30, 30, 25, 5 }[field];
            foreach (string invalid in new[] { "", " ", " PUBLIC-SECRET", "PUBLIC-SECRET ", "\n", "\0", "€", "\ud800", new string('X', maximum + 1) })
            { int selected = field; Reject(() => FinTsUnsignedInitializationWriter.Encode(Change(basis, selected, invalid))); }
        }
        foreach (int invalid in new[] { -1, 1000, int.MaxValue })
        {
            Reject(() => FinTsUnsignedInitializationWriter.Encode(Input(vectors[2], bpd: invalid)));
            Reject(() => FinTsUnsignedInitializationWriter.Encode(Input(vectors[2], upd: invalid)));
        }
        Reject(() => FinTsUnsignedInitializationWriter.Encode(Input(vectors[2], status: 2)));
        Reject(() => FinTsUnsignedInitializationWriter.Encode(Input(vectors[2], status: -1)));
        Reject(() => FinTsUnsignedInitializationWriter.Encode(Input(vectors[2], language: 4)));
        Reject(() => FinTsUnsignedInitializationWriter.Encode(Input(vectors[2], language: -1)));
        var culture = CultureInfo.CurrentCulture;
        byte[] canonical = FinTsUnsignedInitializationWriter.Encode(basis);
        try
        {
            foreach (string name in new[] { "de-DE", "ar-SA", "tr-TR" })
            {
                CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo(name);
                Verify(FinTsUnsignedInitializationWriter.Encode(basis).SequenceEqual(canonical), "Initialization output is culture invariant.");
            }
        }
        finally { CultureInfo.CurrentCulture = culture; }
        using var cancelled = new CancellationTokenSource(); cancelled.Cancel();
        foreach (Action action in new Action[]
        {
            () => FinTsUnsignedInitializationWriter.Encode(basis, cancelled.Token),
            () => FinTsUnsignedInitializationWriter.EncodeAnonymous("280", "PUBLIC-BANK", "PUBLIC-PRODUCT", "1.0", cancellationToken: cancelled.Token),
            () => FinTsUnsignedInitializationRequest.Parse(Frame(wire), cancelled.Token),
            () => FinTsInitializationIdentification.Parse(Frame(wire).Syntax.Segments[1], cancelled.Token),
            () => FinTsInitializationPreparation.Parse(Frame(wire).Syntax.Segments[2], cancelled.Token),
        })
        {
            try { action(); Verify(false, "Cancelled initialization work must not return data."); }
            catch (OperationCanceledException) { Verify(true, "Cancellation propagates."); }
        }
        foreach (Action action in new Action[]
        {
            () => FinTsUnsignedInitializationWriter.Encode(null!), () => FinTsUnsignedInitializationRequest.Parse(null!),
            () => FinTsInitializationIdentification.Parse(null!), () => FinTsInitializationPreparation.Parse(null!),
            () => FinTsUnsignedInitializationWriter.EncodeAnonymous("280", "PUBLIC-BANK", null!, "1.0"),
        })
        {
            try { action(); Verify(false, "Null initialization input must fail."); }
            catch (ArgumentNullException) { Verify(true, "Null input is explicitly rejected."); }
        }
        Console.WriteLine($"FinTS initialization schema/encoding verification passed ({vectors.Length} independent vectors, {count} checks).");
    }
    private static FinTsInitializationInput Input(JsonElement v, int? bpd = null, int? upd = null, int? status = null, int? language = null) => new(
        v.GetProperty("country").GetString()!, v.GetProperty("institution").GetString()!, v.GetProperty("customerId").GetString()!, v.GetProperty("systemId").GetString()!,
        (FinTsCustomerSystemStatus)(status ?? v.GetProperty("systemStatus").GetInt32()), bpd ?? v.GetProperty("bankParameterVersion").GetInt32(), upd ?? v.GetProperty("userParameterVersion").GetInt32(),
        (FinTsDialogueLanguage)(language ?? v.GetProperty("language").GetInt32()), v.GetProperty("productIdentifier").GetString()!, v.GetProperty("productVersion").GetString()!);
    private static FinTsInitializationInput Change(FinTsInitializationInput input, int field, string text)
    {
        string[] fields = [input.Country, input.Institution, input.CustomerId, input.SystemId, input.ProductIdentifier, input.ProductVersion]; fields[field] = text;
        return new(fields[0], fields[1], fields[2], fields[3], input.SystemStatus, input.BankParameterVersion, input.UserParameterVersion, input.Language, fields[4], fields[5]);
    }
    private static FinTsSegment Segment(string value) => FinTsSyntax.ParseSegments(Encoding.Latin1.GetBytes(value)).Segments[0];
    private static FinTsMessageFrame Frame(string value)
    {
        string size = Encoding.Latin1.GetByteCount(value).ToString("D12", CultureInfo.InvariantCulture);
        return FinTsMessageFrame.Parse(Encoding.Latin1.GetBytes(value[..10] + size + value[22..]));
    }
}
