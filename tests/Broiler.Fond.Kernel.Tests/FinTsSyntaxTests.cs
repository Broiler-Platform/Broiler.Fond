using System.Globalization;
using System.Reflection;
using System.Text;
using System.Text.Json;
using Broiler.Fond.Kernel.FinTs;

namespace Broiler.Fond.Kernel.Tests;

internal static class FinTsSyntaxTests
{
    internal static void Run(Action<bool, string> check)
    {
        int count = 0;
        void Verify(bool condition, string message) { count++; check(condition, message); }
        void Reject(byte[] wire, bool frame = false, FinTsSyntaxError? expected = null)
        {
            try
            {
                if (frame) { _ = FinTsMessageFrame.Parse(wire); }
                else { _ = FinTsSyntax.ParseSegments(wire); }
                Verify(false, "Malformed or over-limit wire input must be rejected.");
            }
            catch (FinTsFormatException error)
            {
                Verify(expected is null || expected == error.Error, "Wire failure must use the expected safe error category.");
                Verify(!error.ToString().Contains("PUBLIC-SECRET", StringComparison.Ordinal), "Wire exceptions must never echo input.");
            }
        }

        using Stream stream = Assembly.GetExecutingAssembly().GetManifestResourceStream("FinTs.syntax-v1.json")!;
        using JsonDocument corpus = JsonDocument.Parse(stream);
        int vectors = 0;
        foreach (JsonElement vector in corpus.RootElement.GetProperty("vectors").EnumerateArray())
        {
            vectors++;
            byte[] wire = Convert.FromBase64String(vector.GetProperty("wireBase64").GetString()!);
            bool framed = vector.GetProperty("framed").GetBoolean();
            FinTsSyntaxDocument parsed = framed ? FinTsMessageFrame.Parse(wire).Syntax : FinTsSyntax.ParseSegments(wire);
            JsonElement expected = vector.GetProperty("segments");
            Verify(parsed.ByteLength == wire.Length && parsed.CopyWireBytes().SequenceEqual(wire), "Golden vector must retain every original byte.");
            Verify(parsed.Segments.Count == expected.GetArrayLength(), "Golden segment count must agree with independent producer.");
            for (int index = 0; index < parsed.Segments.Count; index++)
            {
                FinTsSegment segment = parsed.Segments[index];
                JsonElement model = expected[index];
                int? reference = model.GetProperty("reference").ValueKind == JsonValueKind.Null ? null : model.GetProperty("reference").GetInt32();
                Verify(segment.Code == model.GetProperty("code").GetString() && segment.Number == model.GetProperty("number").GetInt32() &&
                    segment.Version == model.GetProperty("version").GetInt32() && segment.Reference == reference, "Golden header must retain code, numbering, unknown version and reference.");
                JsonElement fields = model.GetProperty("fields");
                Verify(segment.Fields.Count == fields.GetArrayLength(), "Golden field count must preserve omissions.");
                for (int fieldIndex = 0; fieldIndex < fields.GetArrayLength(); fieldIndex++)
                {
                    var elements = segment.Fields[fieldIndex].Elements;
                    JsonElement field = fields[fieldIndex];
                    Verify(elements.Count == field.GetArrayLength(), "Golden component count must preserve trailing empty positions.");
                    for (int elementIndex = 0; elementIndex < elements.Count; elementIndex++)
                    {
                        byte[] value = Convert.FromHexString(field[elementIndex].GetProperty("hex").GetString()!);
                        Verify(elements[elementIndex].IsBinary == field[elementIndex].GetProperty("binary").GetBoolean() &&
                            elements[elementIndex].CopyValueBytes().SequenceEqual(value), "Golden scalar must preserve binary kind and exact decoded bytes.");
                    }
                }
            }

            byte[] original = parsed.CopyWireBytes();
            Array.Fill(wire, (byte)'x');
            byte[] exported = parsed.CopyWireBytes();
            Array.Fill(exported, (byte)'y');
            Verify(parsed.CopyWireBytes().SequenceEqual(original), "Caller/input/export mutations must not change parsed evidence.");
            foreach (var element in parsed.Segments.SelectMany(s => s.Fields).SelectMany(f => f.Elements))
            {
                byte[] value = element.CopyValueBytes();
                byte[] copy = (byte[])value.Clone();
                Array.Fill(value, (byte)0);
                Verify(element.CopyValueBytes().SequenceEqual(copy), "Scalar mutation must not alter the document.");
            }

            // Every prefix of a complete frame must fail, including prefixes at valid segment boundaries.
            if (framed)
            {
                for (int length = 0; length < original.Length; length++) { Reject(original[..length], frame: true); }
                Reject([.. original, (byte)'\n'], frame: true);
                Reject([.. original, .. original], frame: true);
            }
        }

        Verify(vectors == 6, "All six independent vectors must execute.");
        Verify(FinTsWireEncoding.EncodeText(Bytes("+:'?@")).SequenceEqual(Bytes("?+?:?'???@")), "All five text syntax characters must be escaped exactly.");
        Verify(FinTsWireEncoding.EncodeBinary([0, 255, (byte)'@']).SequenceEqual(new byte[] { 64, 51, 64, 0, 255, 64 }), "Binary prefix length counts octets and payload is unchanged.");
        Verify(FinTsWireEncoding.EncodeBinary([]).SequenceEqual(Bytes("@0@")), "Empty binary must stay distinct from missing text.");
        Verify(FinTsSyntax.ParseSegments(Bytes("Z:1:1:+@0001@x'")).Segments[0].Fields[0].Elements[0].CopyValueBytes().SequenceEqual(Bytes("x")),
            "Leading-zero binary length spelling and omitted header reference must parse without losing original evidence.");

        foreach (string malformed in new[]
        {
            "", "'", "+Z:1:1'", "Z:1'", "Z:1:1:1:1'", "z:1:1'", "TOOLONG:1:1'", "Z:0:1'", "Z:01:1'", "Z:1000:1'",
            "Z:1:01'", "Z:1:1000'", "Z:1:1:0'", "Z:1:1:@0@'", "@1@Z:1:1'", "Z:1:1+PUBLIC-SECRET?", "Z:1:1+PUBLIC-SECRET?x'",
            "Z:1:1+abc@1@x'", "Z:1:1+@@'", "Z:1:1+@-1@x'", "Z:1:1+@x@x'", "Z:1:1+@1x@x'", "Z:1:1+@2@x",
            "Z:1:1+@1@xy'", "Z:1:1+@1@x?+'", "Z:1:1+@0000000000@'", "Z:1:1+@999999999999999999999@'",
            "Z:1:1+abc", "Z:1:1+abc?'", "Z:1:1+@0@", "Z:1:1''", "Z:1:1'\n",
        }) { Reject(Bytes(malformed)); }

        // Recompute lengths on mutations so each frame test reaches semantic checks.
        foreach (string malformed in new[]
        {
            "HNHBK:1:2+{size}+300+0+1'HNHBS:2:1+1'",
            "HNHBK:2:3+{size}+300+0+1'HNHBS:3:1+1'",
            "HNHBK:1:3:1+{size}+300+0+1'HNHBS:2:1+1'",
            "HNHBK:1:3+{size}+310+0+1'HNHBS:2:1+1'",
            "HNHBK:1:3+{size}+300++1'HNHBS:2:1+1'",
            "HNHBK:1:3+{size}+300+@1@0+1'HNHBS:2:1+1'",
            "HNHBK:1:3+{size}+300+0+0'HNHBS:2:1+0'",
            "HNHBK:1:3+{size}+300+0+01'HNHBS:2:1+1'",
            "HNHBK:1:3+{size}+300+0+1'HNHBS:2:1+2'",
            "HNHBK:1:3+{size}+300+0+1'HNHBS:2:2+1'",
            "HNHBK:1:3+{size}+300+0+1'HNHBS:2:1:1+1'",
            "HNHBK:1:3+{size}+300+0+1+ref'HNHBS:2:1+1'",
            "HNHBK:1:3+{size}+300+0+1+ref:0'HNHBS:2:1+1'",
            "HNHBK:1:3+{size}+300+0+1+ref:1:x'HNHBS:2:1+1'",
            "HNHBK:1:3+{size}+300+0+1'HNHBS:3:1+1'",
            "HNHBK:1:3+{size}+300+0+1'Z:1:1'HNHBS:3:1+1'",
            "HNHBK:1:3+{size}+300+0+1'HNHBS:2:1+1'HNHBS:3:1+1'",
            "HNHBK:1:3+{size}+300+0+1'HNHBK:2:3+1'HNHBS:3:1+1'",
            "HNHBK:1:3+{size}+300+0+1'HNVSK:2:3+opaque'HNHBS:3:1+1'",
            "HNHBK:1:3+{size}+300+0+1'HNVSK:998:3+opaque'HNVSD:999:1+@0@'HNHBS:998:1+1'",
        }) { Reject(Frame(malformed), frame: true); }
        byte[] validFrame = Frame("HNHBK:1:3+{size}+300+0+1+'HNHBS:2:1+1'");
        Verify(FinTsMessageFrame.Parse(validFrame).MessageNumber == 1, "Empty trailing optional frame reference must be accepted.");
        foreach (string dialog in new[] { new string('D', 31), "bad\rID", "bad\nID", "bad\0ID", "bad\u0080ID" })
        {
            Reject(Frame("HNHBK:1:3+{size}+300+" + dialog + "+1'HNHBS:2:1+1'"), frame: true);
            Reject(Frame("HNHBK:1:3+{size}+300+0+1+" + dialog + ":1'HNHBS:2:1+1'"), frame: true);
        }
        Verify(FinTsMessageFrame.Parse(Frame("HNHBK:1:3+{size}+300+" + new string('D', 30) + "+9999'HNHBS:2:1+9999'")).MessageNumber == 9999,
            "Exact dialog and message-number boundaries must parse.");
        byte[] wrongSize = (byte[])validFrame.Clone();
        wrongSize[10] = (byte)'1';
        Reject(wrongSize, frame: true);
        Reject(Bytes(Encoding.ASCII.GetString(validFrame).Replace("0000000000", "000000000", StringComparison.Ordinal)), frame: true);

        // Deterministic hostile-byte corpus exercises delimiter combinations without external fuzzing packages.
        Random random = new(20260905);
        for (int iteration = 0; iteration < 256; iteration++)
        {
            byte[] value = new byte[iteration];
            random.NextBytes(value);
            foreach (bool binary in new[] { false, true })
            {
                byte[] encoded = binary ? FinTsWireEncoding.EncodeBinary(value) : FinTsWireEncoding.EncodeText(value);
                byte[] wire = [.. Bytes("Z:1:1+"), .. encoded, .. Bytes("+tail'")];
                FinTsSegment segment = FinTsSyntax.ParseSegments(wire).Segments[0];
                Verify(segment.Fields.Count == 2 && segment.Fields[0].Elements[0].IsBinary == binary &&
                    segment.Fields[0].Elements[0].CopyValueBytes().SequenceEqual(value) && segment.Fields[1].Elements[0].CopyValueBytes().SequenceEqual(Bytes("tail")),
                    "Generated octet cases must not split or consume adjacent fields.");
            }
        }

        string manyFields = "Z:1:1" + string.Concat(Enumerable.Repeat("+", FinTsSyntax.MaximumFieldsPerSegment - 1)) + "'";
        Verify(FinTsSyntax.ParseSegments(Bytes(manyFields)).Segments[0].Fields.Count == FinTsSyntax.MaximumFieldsPerSegment - 1, "Exact field bound must parse.");
        Reject(Bytes(manyFields.Insert(manyFields.Length - 1, "+")), expected: FinTsSyntaxError.LimitExceeded);
        string manyComponents = "Z:1:1+" + new string(':', FinTsSyntax.MaximumComponentsPerField - 1) + "'";
        Verify(FinTsSyntax.ParseSegments(Bytes(manyComponents)).Segments[0].Fields[0].Elements.Count == FinTsSyntax.MaximumComponentsPerField, "Exact component bound must parse.");
        Reject(Bytes(manyComponents.Insert(manyComponents.Length - 1, ":")), expected: FinTsSyntaxError.LimitExceeded);
        byte[] manySegments = Bytes(string.Concat(Enumerable.Repeat("Z:1:1'", FinTsSyntax.MaximumSegments)));
        Verify(FinTsSyntax.ParseSegments(manySegments).Segments.Count == FinTsSyntax.MaximumSegments, "Syntax layer must preserve standalone segment sequences up to its bound.");
        Reject([.. manySegments, .. Bytes("Z:1:1'")], expected: FinTsSyntaxError.LimitExceeded);
        // 3 header elements + 127*256 + 253 = 32768 total elements.
        string atElements = "Z:1:1" + string.Concat(Enumerable.Repeat("+" + new string(':', 255), 127)) + "+" + new string(':', 252) + "'";
        Verify(FinTsSyntax.ParseSegments(Bytes(atElements)).Segments.Count == 1, "Exact global element bound must parse.");
        Reject(Bytes(atElements.Insert(atElements.Length - 1, ":")), expected: FinTsSyntaxError.LimitExceeded);
        byte[] atWireLimit = [.. Bytes("Z:1:1+"), .. new byte[FinTsSyntax.MaximumWireBytes - 7], (byte)'\''];
        Verify(FinTsSyntax.ParseSegments(atWireLimit).ByteLength == FinTsSyntax.MaximumWireBytes, "Exact byte limit must parse without recursive traversal.");
        Reject([.. atWireLimit, (byte)'x'], expected: FinTsSyntaxError.LimitExceeded);
        Reject(Bytes("Z:1:1+@1048577@'"), expected: FinTsSyntaxError.LimitExceeded);
        foreach (bool binary in new[] { false, true })
        {
            try
            {
                byte[] oversized = Enumerable.Repeat((byte)'?', FinTsSyntax.MaximumWireBytes).ToArray();
                _ = binary ? FinTsWireEncoding.EncodeBinary(oversized) : FinTsWireEncoding.EncodeText(oversized);
                Verify(false, "Encoder must bound encoded output before allocation.");
            }
            catch (FinTsFormatException error) { Verify(error.Error == FinTsSyntaxError.LimitExceeded, "Encoding overflow must return safe limit error."); }
        }
        try { _ = FinTsSyntax.ParseSegments(validFrame, new CancellationToken(true)); Verify(false, "Pre-cancelled parse must stop."); }
        catch (OperationCanceledException) { Verify(true, "Pre-cancelled parse stopped."); }

        var secretDocument = FinTsSyntax.ParseSegments(Bytes("Z:1:1+PUBLIC-SECRET+@13@PUBLIC-SECRET'"));
        object[] printable = [secretDocument, secretDocument.Segments[0], secretDocument.Segments[0].Fields[0], secretDocument.Segments[0].Fields[0].Elements[0]];
        Verify(printable.All(value => !value.ToString()!.Contains("PUBLIC-SECRET", StringComparison.Ordinal)), "Default syntax object formatting must exclude raw data.");
        CultureInfo previous = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("ar-SA");
            Verify(FinTsMessageFrame.Parse(validFrame).MessageNumber == 1 && FinTsWireEncoding.EncodeBinary(Bytes("x")).SequenceEqual(Bytes("@1@x")),
                "Framing numbers and binary lengths must be culture independent.");
        }
        finally { CultureInfo.CurrentCulture = previous; }
        Console.WriteLine($"FinTS byte syntax and framing: {vectors} independent vectors and {count} checks completed.");
    }

    private static byte[] Bytes(string value) => Encoding.Latin1.GetBytes(value);
    private static byte[] Frame(string template)
    {
        string provisional = template.Replace("{size}", "000000000000", StringComparison.Ordinal);
        return Bytes(template.Replace("{size}", Bytes(provisional).Length.ToString("D12", CultureInfo.InvariantCulture), StringComparison.Ordinal));
    }
}
