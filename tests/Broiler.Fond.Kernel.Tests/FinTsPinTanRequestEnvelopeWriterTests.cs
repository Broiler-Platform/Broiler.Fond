using System.Globalization;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Broiler.Fond.Kernel.FinTs;

namespace Broiler.Fond.Kernel.Tests;

internal static class FinTsPinTanRequestEnvelopeWriterTests
{
    internal static void Run(Action<bool, string> check)
    {
        int count = 0;
        void Verify(bool condition, string message) { count++; check(condition, message); }
        using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream("FinTs.pin-tan-request-envelope-v1.json")!;
        using var document = JsonDocument.Parse(stream);
        var vectors = document.RootElement.GetProperty("vectors").EnumerateArray().ToArray();
        foreach (var vector in vectors)
        {
            var evidence = FinTsPinTanSignatureTrailerTests.Evidence(vector.GetProperty("context"));
            byte[] original = evidence.Request.Frame.Syntax.CopyWireBytes();
            using var pin = Owner(Convert.FromBase64String(vector.GetProperty("pinBase64").GetString()!), FinTsCredentialKind.Pin);
            using var tan = vector.GetProperty("tanBase64").GetString() is { } encoded ? Owner(Convert.FromBase64String(encoded), FinTsCredentialKind.Tan) : null;
            byte[]? tanStorage = tan is null ? null : Storage(tan);
            byte[] output = Output();
            try
            {
                var result = FinTsPinTanRequestEnvelopeWriter.TryEncode(evidence, pin, tan, output, out int written);
                byte[] expected = vector.GetProperty("wireBase64").GetString() is { } wire ? Convert.FromBase64String(wire) : [];
                Verify(result.ToString() == vector.GetProperty("result").GetString(), "Envelope matches independent outcome: " + vector.GetProperty("name").GetString());
                Verify(written == expected.Length && output.AsSpan(0, written).SequenceEqual(expected) && output.Skip(written).All(b => b == 0xCC), "Exact envelope, total byte count and untouched tail match independent fixtures.");
                Verify(original.SequenceEqual(evidence.Request.Frame.Syntax.CopyWireBytes()) && pin.GetSnapshot().CopiesCompleted == vector.GetProperty("pinCopies").GetInt32(), "Envelope assembly preserves source observations and expected credential access.");
                if (tan is not null)
                {
                    bool consumed = vector.GetProperty("tanConsumed").GetBoolean();
                    Verify(tan.GetSnapshot().State == (consumed ? FinTsCredentialState.Consumed : FinTsCredentialState.Available) && (!consumed || tanStorage!.All(b => b == 0)), "Wrapped output preserves TAN consumption and actual private-buffer erasure.");
                }
                if (result != FinTsPinTanRequestWriteResult.Written) { continue; }
                // Public synthetic bytes only; neither production writer parses secret-bearing request buffers.
                var frame = FinTsMessageFrame.Parse(output.AsSpan(0, written)); var outer = frame.Syntax.Segments;
                Verify(outer.Select(s => s.Code).SequenceEqual(new[] { "HNHBK", "HNVSK", "HNVSD", "HNHBS" }) && outer[1].Number == 998 && outer[1].Version == 3 && outer[2].Number == 999 && outer[2].Version == 1, "Reserved envelope numbers and versions occupy exactly four outer segments.");
                byte[] payload = outer[2].Fields[0].Elements[0].CopyValueBytes();
                Verify(outer[2].Fields.Count == 1 && outer[2].Fields[0].Elements.Count == 1 && outer[2].Fields[0].Elements[0].IsBinary && payload.SequenceEqual(Convert.FromBase64String(vector.GetProperty("payloadBase64").GetString()!)), "HNVSD binary length encloses the exact signed body, preserving all escapes without double encoding.");
                var inner = FinTsSyntax.ParseSegments(payload).Segments;
                Verify(inner[0].Code == "HNSHK" && inner[^1].Code == "HNSHA" && inner.Select(s => s.Number).SequenceEqual(Enumerable.Range(2, inner.Count)) && outer[^1].Number == inner.Count + 2 && inner.All(s => s.Code is not ("HNHBK" or "HNHBS" or "HNVSK" or "HNVSD")), "Logical inner numbering and outer trailer count exclude nested framing/wrappers.");
                var security = outer[1].Fields;
                string Value(FinTsDataElement element) => Encoding.Latin1.GetString(element.CopyValueBytes());
                Verify(security.Count == 8 && Value(security[0].Elements[0]) == "PIN" && Value(security[0].Elements[1]) == evidence.Header.ProfileVersion.ToString(CultureInfo.InvariantCulture) && Value(security[1].Elements[0]) == "998" && Value(security[7].Elements[0]) == "0", "Profile is bound; security function is plaintext and compression is disabled with no certificate field.");
                Verify(Value(security[3].Elements[2]) == evidence.Header.SystemId && security[3].Elements[1].IsEmpty && Value(security[6].Elements[0]) == evidence.Header.CountryCode && Value(security[6].Elements[1]) == evidence.Header.InstitutionId && Value(security[6].Elements[2]) == evidence.Header.UserId, "Exact escaped country, bank, user and system identity are bound to the signature context.");
                Verify(security[5].Elements.Count == 6 && security[5].Elements[3].IsBinary && security[5].Elements[3].CopyValueBytes().SequenceEqual(new byte[8]) && Value(security[5].Elements[4]) == "5" && Value(security[6].Elements[3]) == "V" && Value(security[6].Elements[4]) == "0" && Value(security[6].Elements[5]) == "0", "Canonical public binary/key fillers remain distinct from credential bytes and cryptographic keys.");
                try { FinTsPinTanEnvelope.Parse(frame); Verify(false, "The response-only parser must reject a wrapped client request."); }
                catch (FinTsFormatException) { Verify(true, "Response-only envelope parsing remains restricted to bank-side bodies."); }
            }
            finally { CryptographicOperations.ZeroMemory(output); }
        }
        Verify(vectors.Length == 17, "All seventeen independent envelope fixtures ran.");
        var sample = vectors[1]; var matched = FinTsPinTanSignatureTrailerTests.Evidence(sample.GetProperty("context"));
        int expectedLength = Convert.FromBase64String(sample.GetProperty("wireBase64").GetString()!).Length;
        FinTsSessionCredential Pin(HookClock? clock = null) => Owner("PUBLIC-PIN"u8.ToArray(), FinTsCredentialKind.Pin, clock);
        FinTsSessionCredential Tan() => Owner("PUBLIC-TAN"u8.ToArray(), FinTsCredentialKind.Tan);
        foreach (int capacity in new[] { 0, expectedLength, FinTsPinTanRequestWriter.MaximumEncodedLength, FinTsPinTanRequestEnvelopeWriter.MaximumEncodedLength - 1 })
        {
            using var pin = Pin(); using var tan = Tan(); byte[] output = Enumerable.Repeat((byte)0xCC, capacity).ToArray();
            Verify(FinTsPinTanRequestEnvelopeWriter.TryEncode(matched, pin, tan, output, out int written) == FinTsPinTanRequestWriteResult.DestinationTooSmall && written == 0 && output.All(b => b == 0xCC) && pin.GetSnapshot().CopiesCompleted == 0 && tan.GetSnapshot().CopiesCompleted == 0, "The complete-envelope reserve precedes all credential copy-out.");
        }
        // Trailer checks 1..5; plain assembler checks 6..7; envelope handoff checks 8..9.
        foreach (int stage in new[] { 3, 4, 5, 6, 7, 8, 9 })
            foreach (bool cancel in new[] { false, true })
            {
                var clock = new HookClock(); using var pin = Pin(clock); using var tan = Tan(); using var cancellation = new CancellationTokenSource();
                byte[] ownedPin = Storage(pin)!, output = Output(); int written = -1;
                clock.AfterReads = stage; clock.Action = () => { if (cancel) { cancellation.Cancel(); } else { clock.Milliseconds = 10000; } };
                try
                {
                    var result = FinTsPinTanRequestEnvelopeWriter.TryEncode(matched, pin, tan, output, out written, cancellation.Token);
                    Verify(!cancel && result == FinTsPinTanRequestWriteResult.CredentialUnavailable, "Expiry withholds wrapped-request success.");
                }
                catch (OperationCanceledException) { Verify(cancel, "Cancellation withholds wrapped-request success."); }
                Verify(written == 0 && (stage == 9 ? output.Take(expectedLength).All(b => b == 0) && output.Skip(expectedLength).All(b => b == 0xCC) : output.All(b => b == 0xCC)), "Every failed boundary clears published bytes or leaves the whole destination untouched.");
                Verify(ownedPin.All(b => b == 0) && tan.GetSnapshot().CopiesCompleted == (stage >= 4 ? 1 : 0) && (stage < 4 || tan.GetSnapshot().State == FinTsCredentialState.Consumed), "Failure erases owned PIN bytes and never restores a copied TAN.");
            }
        var faultyClock = new HookClock();
        using (var pin = Pin(faultyClock))
        using (var tan = Tan())
        {
            byte[] output = Output(); faultyClock.AfterReads = 9; faultyClock.Action = () => throw new InvalidOperationException("PUBLIC-CLOCK-DETAIL");
            Verify(FinTsPinTanRequestEnvelopeWriter.TryEncode(matched, pin, tan, output, out int written) == FinTsPinTanRequestWriteResult.CredentialUnavailable && written == 0 && pin.GetSnapshot().State == FinTsCredentialState.ClockInvalid && output.Take(expectedLength).All(b => b == 0) && output.Skip(expectedLength).All(b => b == 0xCC), "A clock failure after envelope publication erases output and exposes only a fixed outcome.");
        }
        using (var pin = Pin())
        using (var tan = Tan())
        using (var cancellation = new CancellationTokenSource())
        {
            byte[] ownedPin = Storage(pin)!, ownedTan = Storage(tan)!, output = Output(); cancellation.Cancel();
            try { FinTsPinTanRequestEnvelopeWriter.TryEncode(matched, pin, tan, output, out _, cancellation.Token); Verify(false, "Pre-cancellation must throw."); }
            catch (OperationCanceledException) { Verify(output.All(b => b == 0xCC) && ownedPin.All(b => b == 0) && ownedTan.All(b => b == 0), "Pre-cancellation clears both owners without output."); }
        }
        foreach (bool wrongKind in new[] { false, true })
        {
            using var pin = wrongKind ? Tan() : Pin(); using var tan = Tan(); if (!wrongKind) { pin.Dispose(); }
            byte[] output = Output();
            Verify(FinTsPinTanRequestEnvelopeWriter.TryEncode(matched, pin, tan, output, out int written) == (wrongKind ? FinTsPinTanRequestWriteResult.ContextNeedsReview : FinTsPinTanRequestWriteResult.CredentialUnavailable) && written == 0 && output.All(b => b == 0xCC) && tan.GetSnapshot().CopiesCompleted == 0, "Wrong-kind or unavailable PIN never emits an envelope or consumes TAN.");
        }
        var culture = CultureInfo.CurrentCulture;
        try
        {
            foreach (string name in new[] { "de-DE", "ar-SA", "tr-TR" })
            {
                CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo(name); using var pin = Pin(); using var tan = Tan(); byte[] output = Output();
                try { Verify(FinTsPinTanRequestEnvelopeWriter.TryEncode(matched, pin, tan, output, out int written) == FinTsPinTanRequestWriteResult.Written && output.AsSpan(0, written).SequenceEqual(Convert.FromBase64String(sample.GetProperty("wireBase64").GetString()!)), "Binary lengths, total size, dates and profile fields are culture-independent."); }
                finally { CryptographicOperations.ZeroMemory(output); }
            }
        }
        finally { CultureInfo.CurrentCulture = culture; }
        using (var pin = Pin())
        using (var tan = Tan())
        {
            int successes = 0; var outcomes = new FinTsPinTanRequestWriteResult[16];
            Parallel.For(0, outcomes.Length, i =>
            {
                byte[] output = Output();
                try { outcomes[i] = FinTsPinTanRequestEnvelopeWriter.TryEncode(matched, pin, tan, output, out _); if (outcomes[i] == FinTsPinTanRequestWriteResult.Written) { Interlocked.Increment(ref successes); } }
                finally { CryptographicOperations.ZeroMemory(output); }
            });
            Verify(successes == 1 && outcomes.All(r => r is FinTsPinTanRequestWriteResult.Written or FinTsPinTanRequestWriteResult.CredentialUnavailable), "Only one concurrent envelope can consume the shared TAN owner.");
        }
        foreach (bool nullContext in new[] { false, true })
        {
            using var pin = Pin(); byte[] output = Output();
            try { FinTsPinTanRequestEnvelopeWriter.TryEncode(nullContext ? null! : matched, nullContext ? pin : null!, null, output, out _); Verify(false, "Required null inputs must fail."); }
            catch (ArgumentNullException error) { Verify(output.All(b => b == 0xCC) && !error.ToString().Contains("PUBLIC-PIN", StringComparison.Ordinal), "Null inputs leave output untouched with fixed diagnostics."); }
        }
        Console.WriteLine($"FinTS PIN/TAN request-envelope verification passed ({vectors.Length} independent vectors, {count} checks).");
    }
    private static byte[] Output() => Enumerable.Repeat((byte)0xCC, FinTsPinTanRequestEnvelopeWriter.MaximumEncodedLength).ToArray();
    private static byte[]? Storage(FinTsSessionCredential owner) => (byte[]?)typeof(FinTsSessionCredential).GetField("_bytes", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(owner);
    private static FinTsSessionCredential Owner(byte[] bytes, FinTsCredentialKind kind, HookClock? clock = null) => FinTsSessionCredential.CaptureAndClear(bytes, kind, TimeSpan.FromSeconds(10), clock ?? new HookClock());
    private sealed class HookClock : TimeProvider
    {
        internal long Milliseconds { get; set; }
        internal int AfterReads { get; set; } = int.MaxValue;
        internal Action? Action { get; set; }
        public override long TimestampFrequency => 1000;
        public override long GetTimestamp() { if (--AfterReads == 0) { Action?.Invoke(); } return Milliseconds; }
    }
}
