using System.Globalization;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Broiler.Fond.Kernel.FinTs;

namespace Broiler.Fond.Kernel.Tests;

internal static class FinTsPinTanReadRequestWriterTests
{
    internal static void Run(Action<bool, string> check)
    {
        int count = 0;
        void Verify(bool condition, string message) { count++; check(condition, message); }
        using var document = JsonDocument.Parse(Assembly.GetExecutingAssembly().GetManifestResourceStream("FinTs.pin-tan-read-request-assembly-v1.json")!);
        var vectors = document.RootElement.GetProperty("vectors").EnumerateArray().ToArray();
        foreach (var vector in vectors)
        {
            var context = FinTsPinTanReadPinTrailerTests.Context(vector);
            byte[] originalRequest = context.Signature.Request.Frame.Syntax.CopyWireBytes();
            byte[] input = Convert.FromBase64String(vector.GetProperty("pinBase64").GetString()!);
            using var pin = Owner(input); byte[] retained = Buffer(pin)!; byte[] output = Output();
            byte[] expected = vector.GetProperty("wireBase64").GetString() is { } wire ? Convert.FromBase64String(wire) : [];
            try
            {
                var result = FinTsPinTanReadRequestWriter.TryEncodePinOnly(context, pin, output, out int written);
                Verify(result.ToString() == vector.GetProperty("result").GetString(), "Independent read assembly result: " + vector.GetProperty("name").GetString());
                Verify(written == expected.Length && output.AsSpan(0, written).SequenceEqual(expected) && output.Skip(written).All(b => b == 0xCC), "Exact independent full-message bytes and untouched destination tail match.");
                Verify(pin.GetSnapshot().CopiesCompleted == vector.GetProperty("pinCopies").GetInt32() && pin.GetSnapshot().State == FinTsCredentialState.Available && input.All(b => b == 0),
                    "Assembly performs only the expected owned PIN copy and retains capture cleanup.");
                Verify(originalRequest.SequenceEqual(context.Signature.Request.Frame.Syntax.CopyWireBytes()) && context.Signature.Request.Request.Source.Number == 2,
                    "Assembly preserves the original unsigned request and its segment numbering.");
                if (result == FinTsPinTanRequestWriteResult.Written)
                {
                    // Public synthetic credentials only. Production never parses the resulting secret-bearing message.
                    var frame = FinTsMessageFrame.Parse(output.AsSpan(0, written));
                    var segments = frame.Syntax.Segments;
                    Verify(frame.MessageNumber == 2 && segments.Count == 5 && segments.Select(s => s.Number).SequenceEqual(new[] { 1, 2, 3, 4, 5 }) &&
                        segments.Select(s => s.Code).SequenceEqual(new[] { "HNHBK", "HNSHK", context.Signature.Request.Request.Source.Code, "HNSHA", "HNHBS" }),
                        "Framing validates the exact byte size and five logical segment roles.");
                    Verify(segments[0].Fields[2].Elements.Single().CopyValueBytes().SequenceEqual(context.Signature.Request.Frame.Syntax.Segments[0].Fields[2].Elements.Single().CopyValueBytes()) &&
                        segments[2].Version == context.Signature.Request.Request.Source.Version && segments[2].Fields.Count == context.Signature.Request.Request.Source.Fields.Count &&
                        segments[3].Fields[0].Elements.Single().CopyValueBytes().SequenceEqual(Encoding.Latin1.GetBytes(context.Signature.ControlReference)) && segments[3].Fields[2].Elements.Count == 1,
                        "Assigned dialogue, explicit read version/options and bound PIN-only control linkage are preserved.");
                }
                pin.Dispose();
                Verify(retained.All(b => b == 0) && Buffer(pin) is null, "The retained PIN array is erased on disposal after all assembly outcomes.");
            }
            finally { CryptographicOperations.ZeroMemory(output); CryptographicOperations.ZeroMemory(expected); }
        }
        Verify(vectors.Length == 31, "All thirty-one independent read assembly vectors ran.");
        var basis = FinTsPinTanReadPinTrailerTests.Context(vectors[0]);
        byte[] expectedBasis = Convert.FromBase64String(vectors[0].GetProperty("wireBase64").GetString()!);
        try
        {
            foreach (int capacity in new[] { 0, 1, 440, FinTsPinTanReadRequestWriter.MaximumEncodedLength - 1 })
            {
                var clock = new Clock(); using var pin = Owner("12345"u8.ToArray(), clock); int before = clock.Reads;
                byte[] output = Enumerable.Repeat((byte)0xCC, capacity).ToArray();
                Verify(FinTsPinTanReadRequestWriter.TryEncodePinOnly(basis, pin, output, out int written) == FinTsPinTanRequestWriteResult.DestinationTooSmall && written == 0 &&
                    output.All(b => b == 0xCC) && clock.Reads == before, "A complete worst-case reserve is required before any owner access.");
            }
            foreach (int source in new[] { 5, 13 })
            {
                var clock = new Clock(); using var pin = Owner("12345"u8.ToArray(), clock); int before = clock.Reads; byte[] output = Output();
                var result = FinTsPinTanReadRequestWriter.TryEncodePinOnly(FinTsPinTanReadPinTrailerTests.Context(vectors[source]), pin, output, out int written);
                Verify(result == (source == 5 ? FinTsPinTanRequestWriteResult.CredentialRequirementsNeedReview : FinTsPinTanRequestWriteResult.ContextNeedsReview) && written == 0 &&
                    output.All(b => b == 0xCC) && clock.Reads == before, "Missing bounds or unresolved context cannot access the owner or publish partial public framing.");
            }
            using (var tan = Owner("12345"u8.ToArray(), kind: FinTsCredentialKind.Tan))
            {
                byte[] output = Output();
                Verify(FinTsPinTanReadRequestWriter.TryEncodePinOnly(basis, tan, output, out int written) == FinTsPinTanRequestWriteResult.ContextNeedsReview && written == 0 &&
                    output.All(b => b == 0xCC) && tan.GetSnapshot().CopiesCompleted == 0 && tan.GetSnapshot().State == FinTsCredentialState.Available,
                    "A TAN owner cannot substitute for PIN or be consumed by this assembly API.");
            }
            // Five trailer ownership observations followed by before/after whole-message publication.
            foreach (int stage in Enumerable.Range(1, 7))
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
                        var result = FinTsPinTanReadRequestWriter.TryEncodePinOnly(basis, pin, output, out written, cancellation.Token);
                        Verify(failure != "token" && result == FinTsPinTanRequestWriteResult.CredentialUnavailable, "Late ownership failure prevents successful assembly.");
                    }
                    catch (OperationCanceledException) { Verify(failure == "token", "Late token cancellation prevents successful assembly."); }
                    Verify(written == 0 && (stage == 7 ? output.Take(expectedBasis.Length).All(b => b == 0) && output.Skip(expectedBasis.Length).All(b => b == 0xCC) : output.All(b => b == 0xCC)),
                        "Failure at final handoff erases the whole message; earlier nested-trailer failures leave caller output untouched.");
                    Verify(retained.All(b => b == 0) && Buffer(pin) is null && pin.GetSnapshot().CopiesCompleted == (stage >= 4 ? 1 : 0),
                        "Actual owned storage is cleared and successful-copy accounting remains exact.");
                    CryptographicOperations.ZeroMemory(output);
                }
            using (var cancellation = new CancellationTokenSource())
            {
                using var pin = Owner("12345"u8.ToArray()); byte[] retained = Buffer(pin)!; byte[] output = Output(); cancellation.Cancel();
                try { FinTsPinTanReadRequestWriter.TryEncodePinOnly(basis, pin, output, out _, cancellation.Token); Verify(false, "Pre-cancellation must throw."); }
                catch (OperationCanceledException) { Verify(retained.All(b => b == 0) && pin.GetSnapshot().State == FinTsCredentialState.Cancelled && output.All(b => b == 0xCC), "Pre-cancellation cancels the PIN and emits no message."); }
            }
            using (var pin = Owner("12345"u8.ToArray()))
            {
                var matches = new bool[16];
                Parallel.For(0, matches.Length, i =>
                {
                    byte[] output = Output();
                    try { matches[i] = FinTsPinTanReadRequestWriter.TryEncodePinOnly(basis, pin, output, out int written) == FinTsPinTanRequestWriteResult.Written && output.AsSpan(0, written).SequenceEqual(expectedBasis); }
                    finally { CryptographicOperations.ZeroMemory(output); }
                });
                Verify(matches.All(m => m) && pin.GetSnapshot().CopiesCompleted == 16 && pin.GetSnapshot().State == FinTsCredentialState.Available,
                    "Concurrent assemblies have separate staging and a synchronized reusable PIN owner; no request is consumed.");
            }
            var culture = CultureInfo.CurrentCulture;
            try
            {
                foreach (string name in new[] { "de-DE", "ar-SA", "tr-TR" })
                {
                    CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo(name); using var pin = Owner("12345"u8.ToArray()); byte[] output = Output();
                    try { Verify(FinTsPinTanReadRequestWriter.TryEncodePinOnly(basis, pin, output, out int written) == FinTsPinTanRequestWriteResult.Written && output.AsSpan(0, written).SequenceEqual(expectedBasis), "Read framing and byte-size calculation are culture independent."); }
                    finally { CryptographicOperations.ZeroMemory(output); }
                }
            }
            finally { CultureInfo.CurrentCulture = culture; }
            foreach (bool missingContext in new[] { false, true })
            {
                using var pin = Owner("12345"u8.ToArray()); byte[] output = Output(); int written = -1;
                try { FinTsPinTanReadRequestWriter.TryEncodePinOnly(missingContext ? null! : basis, missingContext ? pin : null!, output, out written); Verify(false, "Null required inputs must fail."); }
                catch (ArgumentNullException error) { Verify(written == 0 && output.All(b => b == 0xCC) && pin.GetSnapshot().CopiesCompleted == 0 && !error.ToString().Contains("12345", StringComparison.Ordinal), "Null arguments expose fixed diagnostics without output or credential access."); }
            }
        }
        finally { CryptographicOperations.ZeroMemory(expectedBasis); }
        Console.WriteLine($"FinTS first-read PIN-only request assembly passed ({vectors.Length} independent vectors, {count} checks).");
    }
    private static FinTsSessionCredential Owner(byte[] value, Clock? clock = null, FinTsCredentialKind kind = FinTsCredentialKind.Pin) => FinTsSessionCredential.CaptureAndClear(value, kind, TimeSpan.FromSeconds(10), clock ?? new Clock());
    private static byte[] Output() => Enumerable.Repeat((byte)0xCC, FinTsPinTanReadRequestWriter.MaximumEncodedLength).ToArray();
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
