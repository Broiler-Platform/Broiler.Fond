using System.Globalization;
using System.Reflection;
using System.Text;
using System.Text.Json;
using Broiler.Fond.Kernel.FinTs;

namespace Broiler.Fond.Kernel.Tests;

internal static class FinTsPinTanSignatureHeaderTests
{
    internal static void Run(Action<bool, string> check)
    {
        int count = 0;
        void Verify(bool condition, string message) { count++; check(condition, message); }
        void Reject(string wire, FinTsSyntaxError expected = FinTsSyntaxError.InvalidSignatureHeader)
        {
            try { Parse(wire); Verify(false, "Invalid signature header must fail."); }
            catch (FinTsFormatException error) { Verify(error.Error == expected && !error.ToString().Contains("PUBLIC", StringComparison.Ordinal), $"Signature-header error must be fixed and expected: {error.Error}."); }
        }
        using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream("FinTs.pin-tan-signature-header-v1.json")!;
        using var doc = JsonDocument.Parse(stream);
        var vectors = doc.RootElement.GetProperty("vectors").EnumerateArray().ToArray();
        foreach (var v in vectors)
        {
            byte[] bytes = Convert.FromBase64String(v.GetProperty("wireBase64").GetString()!);
            var syntax = FinTsSyntax.ParseSegments(bytes); var source = syntax.Segments.Single();
            var header = FinTsPinTanSignatureHeader.Parse(source);
            foreach (var property in typeof(FinTsPinTanSignatureHeader).GetProperties().Where(p => p.Name != "Source"))
            {
                string key = char.ToLowerInvariant(property.Name[0]) + property.Name[1..];
                object? value = property.GetValue(header);
                string? actual = value switch
                {
                    DateOnly date => date.ToString("yyyyMMdd", CultureInfo.InvariantCulture),
                    TimeOnly time => time.ToString("HHmmss", CultureInfo.InvariantCulture),
                    IFormattable formattable => formattable.ToString(null, CultureInfo.InvariantCulture),
                    _ => value?.ToString(),
                };
                var expected = v.GetProperty(key);
                Verify(actual == (expected.ValueKind == JsonValueKind.Null ? null : expected.ToString()), $"Independent signature-header value {key} matches {v.GetProperty("name").GetString()}.");
            }
            Verify(ReferenceEquals(header.Source, source) && source.Number == v.GetProperty("segmentNumber").GetInt32(), "The exact source segment and number remain available without request-position assumptions.");
            bytes[0] = 0;
            Verify(syntax.CopyWireBytes()[0] == 'H' && ReferenceEquals(FinTsPinTanSignatureHeader.Parse(source).Source, source), "Caller byte mutations cannot alter source and repeated parsing consumes no evidence.");
        }
        Verify(vectors.Length == 10, "All ten independent signature-header fixtures ran.");
        string sample = Encoding.Latin1.GetString(Convert.FromBase64String(vectors[0].GetProperty("wireBase64").GetString()!));
        foreach (var pair in new (string Before, string After)[]
        {
            ("HNSHK:2:4", "HNSHA:2:4"), ("HNSHK:2:4", "HNSHK:2:4:1"),
            ("PIN:1", "PIN"), ("PIN:1", "PIN:1:2"), ("PIN:1", "@3@PIN:1"),
            ("+999+", "+998+"), ("+999+", "+900+"), ("+999+", "+0999+"), ("+999+", "+@3@999+"),
            ("+PUBLIC-REF+", "++"), ("PUBLIC-REF", "0"), ("PUBLIC-REF", "PUBLIC-SECRET\n"),
            ("PUBLIC-REF", new string('x', 15)), ("PUBLIC-REF", " padded"), ("PUBLIC-REF", "padded "),
            ("+1+1+1::", "+2+1+1::"), ("+1+1+1::", "+1+2+1::"),
            ("1::PUBLIC-SYSTEM", "3::PUBLIC-SYSTEM"), ("1::PUBLIC-SYSTEM", "1:@1@X:PUBLIC-SYSTEM"),
            ("1::PUBLIC-SYSTEM", "1:@0@:PUBLIC-SYSTEM"), ("1::PUBLIC-SYSTEM", "1:X:PUBLIC-SYSTEM"),
            ("1::PUBLIC-SYSTEM", "1::"), ("PUBLIC-SYSTEM", new string('s', 31)), ("PUBLIC-SYSTEM", "PUBLIC\u0085SYSTEM"),
            ("+1+1:20260908", "+00+1:20260908"), ("+1+1:20260908", "+-1+1:20260908"),
            ("+1+1:20260908", "+99999999999999999+1:20260908"), ("+1+1:20260908", "+@1@1+1:20260908"),
            ("1:20260908:123456", "6:20260908:123456"), ("20260908", "20260229"), ("20260908", "00000000"),
            ("20260908", "2026918"), ("123456", "240000"), ("123456", "120060"), ("123456", "12345"),
            ("1:20260908:123456", "1::123456"), ("1:20260908:123456", "1:20260908:123456:"),
            ("1:20260908:123456", "1:@8@20260908:123456"),
            ("1:999:1", "2:999:1"), ("1:999:1", "1:999:2"), ("1:999:1", "1:999:1:@1@X"),
            ("1:999:1", "1:999:1:@0@"), ("1:999:1", "1:999:1:X"), ("1:999:1", "1:999"),
            ("6:10:16", "1:10:16"), ("6:10:16", "6:ABC:16"), ("6:10:16", "6:1000:16"),
            ("6:10:16", "6::16"), ("6:10:16", "6:10:-1"), ("6:10:16", "6:10:@2@16"),
            ("6:10:16", "6:10:16:"), ("280:PUBLIC-BANK", "28:PUBLIC-BANK"), ("280:PUBLIC-BANK", "28X:PUBLIC-BANK"),
            ("PUBLIC-BANK", new string('b', 31)), ("PUBLIC-USER", ""), ("PUBLIC-USER", new string('u', 31)),
            ("PUBLIC-USER", "@4@USER"), (":S:0:0'", ":S:00:0'"), (":S:0:0'", ":S:1000:0'"),
            (":S:0:0'", ":S:0:-1'"), (":S:0:0'", ":S:0'"), (":S:0:0'", ":S:0:0:X'"),
            (":S:0:0'", ":S:0:0+PUBLIC-CERT'"), (":S:0:0'", ":S:0:0+@0@'"),
            (":S:0:0'", ":S:0:0+:'"), (":S:0:0'", ":S:0:0++'"),
        }) { Reject(sample.Replace(pair.Before, pair.After, StringComparison.Ordinal)); }
        foreach (var pair in new (string Before, string After)[]
        {
            ("HNSHK:2:4", "HNSHK:2:3"), ("HNSHK:2:4", "HNSHK:2:5"),
            ("PIN:1", "RAH:7"), ("PIN:1", "PIN:3"), ("PIN:1", "PIN:01"),
            ("1:999:1", "1:1:1"), ("1:999:1", "1:2:1"), ("1:999:1", "1:777:1"),
            (":S:0:0'", ":D:0:0'"), (":S:0:0'", ":V:0:0'"),
        }) { Reject(sample.Replace(pair.Before, pair.After, StringComparison.Ordinal), FinTsSyntaxError.UnsupportedSignatureHeader); }
        string twoStep = sample.Replace("PIN:1", "PIN:2", StringComparison.Ordinal);
        foreach (string function in new[] { "0", "899", "998", "999", "0900", "9X0" })
        { Reject(twoStep.Replace("+999+", "+" + function + "+", StringComparison.Ordinal)); }
        foreach (string function in new[] { "900", "920", "997" })
        { Verify(Parse(twoStep.Replace("+999+", "+" + function + "+", StringComparison.Ordinal)).SecurityFunction == function, "Two-step procedure codes remain exact observations, not proof of permission."); }
        foreach (string code in new[] { "3", "4", "5", "6", "999" })
        { Verify(Parse(sample.Replace("1:999:1", "1:" + code + ":1", StringComparison.Ordinal)).HashAlgorithmCode == code, "Supported hash codes remain observations with no cryptographic execution."); }
        foreach (string timestamp in new[] { "1", "1:", "1::", "1:20260908", "1:20260908:" })
        { Verify(Parse(sample.Replace("1:20260908:123456", timestamp, StringComparison.Ordinal)).SecurityTime is null, "Missing optional time remains absent with omitted or empty positions."); }
        foreach (string value in new[] { "0", "unbekannt" })
        { Verify(Parse(sample.Replace("PUBLIC-SYSTEM", value, StringComparison.Ordinal)).SystemId == value, "Unassigned system observations require later request context and are not silently activated."); }
        Verify(Parse(sample.Replace("1:999:1", "1:999:1:", StringComparison.Ordinal)).Source.Fields[8].Elements.Count == 4 &&
            Parse(sample[..^1] + "+'").Source.Fields.Count == 12, "Explicitly empty forbidden optional fields remain source omissions.");
        var original = CultureInfo.CurrentCulture;
        try
        {
            foreach (string culture in new[] { "de-DE", "ar-SA", "tr-TR" })
            {
                CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo(culture);
                var h = Parse(sample);
                Verify(h.SecurityDate == new DateOnly(2026, 9, 8) && h.SecurityTime == new TimeOnly(12, 34, 56) && h.SecurityReferenceNumber == 1, "Numeric and calendar parsing is culture-independent.");
            }
        }
        finally { CultureInfo.CurrentCulture = original; }
        var sourceBase = FinTsSyntax.ParseSegments(Encoding.Latin1.GetBytes(sample)).Segments[0];
        using var cancelled = new CancellationTokenSource(); cancelled.Cancel();
        try { FinTsPinTanSignatureHeader.Parse(sourceBase, cancelled.Token); Verify(false, "Cancellation must stop header parsing."); }
        catch (OperationCanceledException) { Verify(true, "Cancellation is observed."); }
        try { FinTsPinTanSignatureHeader.Parse(null!); Verify(false, "Null source must fail."); }
        catch (ArgumentNullException) { Verify(true, "Null source is rejected."); }
        Verify(!Parse(sample).ToString()!.Contains("PUBLIC", StringComparison.Ordinal), "Default header diagnostics contain no private source values.");
        Console.WriteLine($"FinTS PIN/TAN signature-header verification passed ({vectors.Length} independent vectors, {count} checks).");
    }
    private static FinTsPinTanSignatureHeader Parse(string wire) => FinTsPinTanSignatureHeader.Parse(FinTsSyntax.ParseSegments(Encoding.Latin1.GetBytes(wire)).Segments.Single());
}
