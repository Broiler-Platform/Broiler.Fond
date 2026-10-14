using System.Globalization;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Broiler.Fond.Kernel.FinTs;

namespace Broiler.Fond.Kernel.Tests;

internal static class FinTsPinTanReadRequestEnvelopeTests
{
    internal static void Run(Action<bool, string> check)
    {
        int count = 0;
        void Verify(bool condition, string message) { count++; check(condition, message); }
        using var document = JsonDocument.Parse(Assembly.GetExecutingAssembly().GetManifestResourceStream("FinTs.pin-tan-read-request-envelope-v1.json")!);
        var vectors = document.RootElement.GetProperty("vectors").EnumerateArray().ToArray();
        foreach (var vector in vectors)
        {
            var context = FinTsPinTanReadPinTrailerTests.Context(vector);
            if (!vector.GetProperty("candidateMatches").GetBoolean())
            {
                try { FinTsPinTanReadRequestBinding.ForEnvelopeCandidate(context); Verify(false, "Unqualified read context cannot construct a candidate."); }
                catch (FinTsFormatException error) { Verify(!error.ToString().Contains("PUBLIC-", StringComparison.Ordinal), "Candidate rejection uses fixed diagnostics before credential access."); }
                continue;
            }
            var candidate = FinTsPinTanReadRequestBinding.ForEnvelopeCandidate(context);
            Verify(ReferenceEquals(candidate.Context, context) && candidate.MessageNumber == 2 && candidate.ExpectedBankMessageNumber == 2 && candidate.ReadNumber == 3 &&
                candidate.ProfileVersion == vector.GetProperty("profileVersion").GetInt32() && candidate.ExpectedUserId == "PUBLIC-USER", "Candidate preserves exact context and explicit message/profile/read metadata.");
            Verify(candidate.Segments.Select(s => s.Number).SequenceEqual(new[] { 1, 998, 999, 2, 3, 4, 5 }) &&
                candidate.Segments.Select(s => s.Role).SequenceEqual(new[] { FinTsPinTanRequestSegmentRole.MessageHeader, FinTsPinTanRequestSegmentRole.EnvelopeHeader,
                    FinTsPinTanRequestSegmentRole.EnvelopeData, FinTsPinTanRequestSegmentRole.SignatureHeader, FinTsPinTanRequestSegmentRole.ReadOperation,
                    FinTsPinTanRequestSegmentRole.SignatureTrailer, FinTsPinTanRequestSegmentRole.MessageTrailer }) &&
                candidate.Segments[4].Code == vector.GetProperty("readCode").GetString() && candidate.Segments[4].Version == vector.GetProperty("readVersion").GetInt32(),
                "The credential-free map preserves reserved wrapper numbers and exact read operation/version.");
            byte[] original = context.Signature.Request.Frame.Syntax.CopyWireBytes();
            using var pin = Owner(Convert.FromBase64String(vector.GetProperty("pinBase64").GetString()!)); byte[] retained = Buffer(pin)!; byte[] output = Output();
            byte[] expected = vector.GetProperty("wireBase64").GetString() is { } wire ? Convert.FromBase64String(wire) : [];
            try
            {
                var result = FinTsPinTanReadRequestEnvelopeWriter.TryEncodePinOnly(candidate, pin, output, out int written);
                Verify(result.ToString() == vector.GetProperty("result").GetString(), "Independent read envelope result: " + vector.GetProperty("name").GetString());
                Verify(written == expected.Length && output.AsSpan(0, written).SequenceEqual(expected) && output.Skip(written).All(b => b == 0xCC), "Complete envelope bytes, actual length and untouched output tail match independent fixtures.");
                Verify(original.SequenceEqual(context.Signature.Request.Frame.Syntax.CopyWireBytes()) && pin.GetSnapshot().CopiesCompleted == vector.GetProperty("pinCopies").GetInt32(), "Envelope preserves the source request and performs only the expected PIN copy.");
                if (result == FinTsPinTanRequestWriteResult.Written)
                {
                    // Parse only public synthetic credential-bearing fixtures. Production selects its own assembler payload by known framing lengths.
                    var frame = FinTsMessageFrame.Parse(output.AsSpan(0, written)); var outer = frame.Syntax.Segments;
                    Verify(frame.MessageNumber == candidate.MessageNumber && outer.Select(s => s.Code).SequenceEqual(new[] { "HNHBK", "HNVSK", "HNVSD", "HNHBS" }) &&
                        outer.Select(s => s.Number).SequenceEqual(new[] { 1, 998, 999, 5 }) && Value(outer[0].Fields[2].Elements[0]) == candidate.DialogueId,
                        "Exact framing retains the assigned dialogue and four reserved/logical outer segments.");
                    byte[] payload = outer[2].Fields[0].Elements.Single().CopyValueBytes();
                    byte[] expectedPayload = Convert.FromBase64String(vector.GetProperty("payloadBase64").GetString()!);
                    try
                    {
                        Verify(outer[2].Fields[0].Elements.Single().IsBinary && payload.SequenceEqual(expectedPayload), "HNVSD contains the exact inner bytes without double escaping or nested framing.");
                        var inner = FinTsSyntax.ParseSegments(payload).Segments;
                        var actual = new[] { outer[0], outer[1], outer[2], inner[0], inner[1], inner[2], outer[3] };
                        Verify(actual.Zip(candidate.Segments).All(pair => pair.First.Code == pair.Second.Code && pair.First.Number == pair.Second.Number && pair.First.Version == pair.Second.Version) &&
                            inner[2].Fields[2].Elements.Count == 1, "Every candidate map entry matches an actual wrapper/inner segment and the security component remains PIN-only.");
                    }
                    finally { CryptographicOperations.ZeroMemory(payload); CryptographicOperations.ZeroMemory(expectedPayload); }
                    var security = outer[1].Fields;
                    Verify(security.Count == 8 && Value(security[1].Elements[0]) == "998" && Value(security[3].Elements[2]) == context.Signature.Header.SystemId &&
                        Value(security[6].Elements[2]) == candidate.ExpectedUserId && Value(security[7].Elements[0]) == "0" &&
                        security[5].Elements[3].IsBinary && security[5].Elements[3].CopyValueBytes().SequenceEqual(new byte[8]),
                        "The wrapper retains bound identities, plaintext function, no compression and canonical public binary filler.");
                    try { FinTsPinTanEnvelope.Parse(frame); Verify(false, "The bank-response parser must reject client signature bodies."); }
                    catch (FinTsFormatException) { Verify(true, "Response-envelope parsing remains restricted to bank replies."); }
                }
                pin.Dispose();
                Verify(retained.All(b => b == 0) && Buffer(pin) is null && ReferenceEquals(candidate.Context, context), "PIN disposal clears storage while independent credential-free metadata remains usable.");
            }
            finally { CryptographicOperations.ZeroMemory(output); CryptographicOperations.ZeroMemory(expected); }
        }
        Verify(vectors.Length == 33, "All thirty-three independent read-envelope fixtures ran.");
        var basis = FinTsPinTanReadRequestBinding.ForEnvelopeCandidate(FinTsPinTanReadPinTrailerTests.Context(vectors[0]));
        byte[] expectedBasis = Convert.FromBase64String(vectors[0].GetProperty("wireBase64").GetString()!);
        try
        {
            foreach (int capacity in new[] { 0, expectedBasis.Length, FinTsPinTanReadRequestWriter.MaximumEncodedLength, FinTsPinTanReadRequestEnvelopeWriter.MaximumEncodedLength - 1 })
            {
                var clock = new Clock(); using var pin = Owner("12345"u8.ToArray(), clock); int before = clock.Reads;
                byte[] output = Enumerable.Repeat((byte)0xCC, capacity).ToArray();
                Verify(FinTsPinTanReadRequestEnvelopeWriter.TryEncodePinOnly(basis, pin, output, out int written) == FinTsPinTanRequestWriteResult.DestinationTooSmall && written == 0 &&
                    output.All(b => b == 0xCC) && clock.Reads == before, "The envelope reserve is required before owner access or inner assembly.");
            }
            foreach (int stage in Enumerable.Range(1, 9))
                foreach (string failure in new[] { "token", "cancel", "dispose", "expiry", "clock", "regression" })
                {
                    var clock = new Clock(); using var pin = Owner("12345"u8.ToArray(), clock); using var cancellation = new CancellationTokenSource();
                    byte[] retained = Buffer(pin)!; byte[] output = Output(); int written = -1;
                    clock.AfterReads = stage; clock.Action = () =>
                    {
                        if (failure == "token") { cancellation.Cancel(); }
                        else if (failure == "cancel") { pin.Cancel(); }
                        else if (failure == "dispose") { pin.Dispose(); }
                        else if (failure == "expiry") { clock.Milliseconds = 10000; }
                        else if (failure == "regression") { clock.Milliseconds = -1; }
                        else { throw new InvalidOperationException("PUBLIC-CLOCK"); }
                    };
                    try
                    {
                        var result = FinTsPinTanReadRequestEnvelopeWriter.TryEncodePinOnly(basis, pin, output, out written, cancellation.Token);
                        Verify(failure != "token" && result == FinTsPinTanRequestWriteResult.CredentialUnavailable, "Late ownership failure withholds the envelope.");
                    }
                    catch (OperationCanceledException) { Verify(failure == "token", "Late token cancellation withholds the envelope."); }
                    Verify(written == 0 && (stage == 9 ? output.Take(expectedBasis.Length).All(b => b == 0) && output.Skip(expectedBasis.Length).All(b => b == 0xCC) : output.All(b => b == 0xCC)),
                        "Final failure erases the complete envelope prefix; every nested failure leaves caller output untouched.");
                    Verify(retained.All(b => b == 0) && Buffer(pin) is null && pin.GetSnapshot().CopiesCompleted == (stage >= 4 ? 1 : 0), "All ownership failure stages erase the actual PIN storage with exact copy accounting.");
                    CryptographicOperations.ZeroMemory(output);
                }
            using (var tan = Owner("12345"u8.ToArray(), kind: FinTsCredentialKind.Tan))
            {
                byte[] output = Output();
                Verify(FinTsPinTanReadRequestEnvelopeWriter.TryEncodePinOnly(basis, tan, output, out int written) == FinTsPinTanRequestWriteResult.ContextNeedsReview && written == 0 &&
                    output.All(b => b == 0xCC) && tan.GetSnapshot().CopiesCompleted == 0 && tan.GetSnapshot().State == FinTsCredentialState.Available, "The wrapper cannot consume a TAN supplied as PIN.");
            }
            using (var pin = Owner("12345"u8.ToArray()))
            {
                var results = new bool[16];
                Parallel.For(0, results.Length, i =>
                {
                    byte[] output = Output();
                    try { results[i] = FinTsPinTanReadRequestEnvelopeWriter.TryEncodePinOnly(basis, pin, output, out int written) == FinTsPinTanRequestWriteResult.Written && output.AsSpan(0, written).SequenceEqual(expectedBasis); }
                    finally { CryptographicOperations.ZeroMemory(output); }
                });
                Verify(results.All(r => r) && pin.GetSnapshot().CopiesCompleted == 16, "Concurrent envelope writes preserve immutable candidate metadata and use independent secret staging.");
            }
            var culture = CultureInfo.CurrentCulture;
            try
            {
                foreach (string name in new[] { "de-DE", "ar-SA", "tr-TR" })
                {
                    CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo(name); using var pin = Owner("12345"u8.ToArray()); byte[] output = Output();
                    try { Verify(FinTsPinTanReadRequestEnvelopeWriter.TryEncodePinOnly(basis, pin, output, out int written) == FinTsPinTanRequestWriteResult.Written && output.AsSpan(0, written).SequenceEqual(expectedBasis), "Envelope length and timestamp formatting are culture independent."); }
                    finally { CryptographicOperations.ZeroMemory(output); }
                }
            }
            finally { CultureInfo.CurrentCulture = culture; }
            using (var cancellation = new CancellationTokenSource())
            {
                using var pin = Owner("12345"u8.ToArray()); byte[] retained = Buffer(pin)!; byte[] output = Output(); cancellation.Cancel();
                try { FinTsPinTanReadRequestEnvelopeWriter.TryEncodePinOnly(basis, pin, output, out _, cancellation.Token); Verify(false, "Pre-cancellation must throw."); }
                catch (OperationCanceledException) { Verify(retained.All(b => b == 0) && pin.GetSnapshot().State == FinTsCredentialState.Cancelled && output.All(b => b == 0xCC), "Pre-cancellation clears PIN ownership before output."); }
                try { FinTsPinTanReadRequestBinding.ForEnvelopeCandidate(basis.Context, cancellation.Token); Verify(false, "Candidate pre-cancellation must throw."); }
                catch (OperationCanceledException) { Verify(true, "Metadata construction observes cancellation without credential access."); }
            }
            foreach (bool missingCandidate in new[] { false, true })
            {
                using var pin = Owner("12345"u8.ToArray()); byte[] output = Output(); int written = -1;
                try { FinTsPinTanReadRequestEnvelopeWriter.TryEncodePinOnly(missingCandidate ? null! : basis, missingCandidate ? pin : null!, output, out written); Verify(false, "Null input must fail."); }
                catch (ArgumentNullException error) { Verify(written == 0 && output.All(b => b == 0xCC) && pin.GetSnapshot().CopiesCompleted == 0 && !error.ToString().Contains("12345", StringComparison.Ordinal), "Null input has fixed diagnostics without publication or credential access."); }
            }
            try { FinTsPinTanReadRequestBinding.ForEnvelopeCandidate(null!); Verify(false, "Null metadata context must fail."); }
            catch (ArgumentNullException) { Verify(true, "Candidate construction requires a context."); }
            Verify(((ICollection<FinTsPinTanRequestSegmentBinding>)basis.Segments).IsReadOnly && ReferenceEquals(FinTsPinTanReadRequestBinding.ForEnvelopeCandidate(basis.Context).Context, basis.Context),
                "Candidate maps are read-only and construction consumes no context or replay state.");
        }
        finally { CryptographicOperations.ZeroMemory(expectedBasis); }
        Console.WriteLine($"FinTS first-read PIN-only request envelope passed ({vectors.Length} independent vectors, {count} checks).");
    }
    private static string Value(FinTsDataElement element) => Encoding.Latin1.GetString(element.CopyValueBytes());
    private static FinTsSessionCredential Owner(byte[] value, Clock? clock = null, FinTsCredentialKind kind = FinTsCredentialKind.Pin) => FinTsSessionCredential.CaptureAndClear(value, kind, TimeSpan.FromSeconds(10), clock ?? new Clock());
    private static byte[] Output() => Enumerable.Repeat((byte)0xCC, FinTsPinTanReadRequestEnvelopeWriter.MaximumEncodedLength).ToArray();
    private static byte[]? Buffer(FinTsSessionCredential owner) => (byte[]?)typeof(FinTsSessionCredential).GetField("_bytes", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(owner);
    private sealed class Clock : TimeProvider
    {
        internal long Milliseconds { get; set; }
        internal int Reads { get; private set; }
        internal int AfterReads { get; set; } = int.MaxValue;
        internal Action? Action { get; set; }
        public override long TimestampFrequency => 1000;
        public override long GetTimestamp() { Reads++; if (--AfterReads == 0) { Action?.Invoke(); } return Milliseconds; }
    }
}
