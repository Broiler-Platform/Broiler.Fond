using System.Globalization;
using System.Reflection;
using System.Security.Cryptography;
using System.Text.Json;
using Broiler.Fond.Kernel.FinTs;

namespace Broiler.Fond.Kernel.Tests;

internal static class FinTsPinTanReadContinuationPinTrailerTests
{
    internal static void Run(Action<bool, string> check)
    {
        int count = 0;
        void Verify(bool condition, string message) { count++; check(condition, message); }
        using var document = JsonDocument.Parse(Assembly.GetExecutingAssembly().GetManifestResourceStream("FinTs.pin-tan-read-continuation-pin-trailer-v1.json")!);
        var vectors = document.RootElement.GetProperty("vectors").EnumerateArray().ToArray();
        foreach (var vector in vectors)
        {
            var context = Context(vector);
            byte[] requestBefore = context.Request.Frame.Syntax.CopyWireBytes();
            byte[] previousBefore = context.Continuation.Previous!.Response.Frame.Syntax.CopyWireBytes();
            byte[] input = Convert.FromBase64String(vector.GetProperty("pinBase64").GetString()!);
            var clock = new Clock(); using var pin = Owner(input, clock); int readsBefore = clock.Reads;
            byte[] retained = Buffer(pin)!; byte[] output = Output();
            byte[] expected = vector.GetProperty("wireBase64").GetString() is { } wire ? Convert.FromBase64String(wire) : [];
            try
            {
                Verify(input.All(b => b == 0), "PIN capture clears transferred fixture input.");
                var result = FinTsPinTanSignatureTrailerWriter.TryEncodeReadContinuationPinOnly(context, pin, output, out int written);
                Verify(vector.GetProperty("pinCopies").GetInt32() != 0 || clock.Reads == readsBefore, "All unresolved continuation/signature or missing-bound cases fail before even querying the PIN owner.");
                Verify(requestBefore.SequenceEqual(context.Request.Frame.Syntax.CopyWireBytes()) && previousBefore.SequenceEqual(context.Continuation.Previous!.Response.Frame.Syntax.CopyWireBytes()), "Encoding preserves the original continuation request and partial-response bytes.");
                Verify(result.ToString() == vector.GetProperty("result").GetString(), "Independent continuation PIN trailer result: " + vector.GetProperty("name").GetString());
                Verify(written == expected.Length && output.AsSpan(0, written).SequenceEqual(expected) && output.Skip(written).All(b => b == 0xCC), "Independent exact bytes and unchanged output tail match.");
                Verify(pin.GetSnapshot().CopiesCompleted == vector.GetProperty("pinCopies").GetInt32() && pin.GetSnapshot().State == FinTsCredentialState.Available,
                    "Missing bounds fail before copy; value checks apply after one actual PIN copy without consuming its owner.");
                if (result == FinTsSignatureTrailerWriteResult.Written)
                {
                    // Test-only parsing of public synthetic credential bytes; production writes directly from bounded staging.
                    var trailer = FinTsSyntax.ParseSegments(expected).Segments.Single();
                    Verify(trailer.Code == "HNSHA" && trailer.Number == 4 && trailer.Version == 2 && trailer.Fields.Count == 3 &&
                        trailer.Fields[1].Elements.Single().IsEmpty && trailer.Fields[2].Elements.Count == 1,
                        "The continuation component uses HNSHA segment 4, empty validation result and no TAN component.");
                    byte[] retry = Output();
                    try { Verify(FinTsPinTanSignatureTrailerWriter.TryEncodeReadContinuationPinOnly(context, pin, retry, out int again) == result && retry.AsSpan(0, again).SequenceEqual(expected), "A reusable PIN owner can encode again without request or replay state."); }
                    finally { CryptographicOperations.ZeroMemory(retry); }
                }
                pin.Dispose();
                Verify(retained.All(b => b == 0) && Buffer(pin) is null, "Disposal erases the actual retained PIN storage after success or review.");
            }
            finally { CryptographicOperations.ZeroMemory(output); CryptographicOperations.ZeroMemory(expected); }
        }
        Verify(vectors.Length == 82, "All eighty-two independently generated PIN trailer fixtures ran.");
        var basis = Context(vectors[0]);
        int expectedLength = Convert.FromBase64String(vectors[0].GetProperty("wireBase64").GetString()!).Length;
        foreach (int capacity in new[] { 0, 1, 20, FinTsPinTanSignatureTrailerWriter.MaximumEncodedLength - 1 })
        {
            using var pin = Owner("12345"u8.ToArray()); byte[] output = Enumerable.Repeat((byte)0xCC, capacity).ToArray();
            Verify(FinTsPinTanSignatureTrailerWriter.TryEncodeReadContinuationPinOnly(basis, pin, output, out int written) == FinTsSignatureTrailerWriteResult.DestinationTooSmall &&
                written == 0 && output.All(b => b == 0xCC) && pin.GetSnapshot().CopiesCompleted == 0, "Worst-case output reserve is required before credential access.");
        }
        var untouchedClock = new Clock(); using (var pin = Owner("12345"u8.ToArray(), untouchedClock))
        {
            int before = untouchedClock.Reads; byte[] output = Output();
            Verify(FinTsPinTanSignatureTrailerWriter.TryEncodeReadContinuationPinOnly(Context(vectors.Single(v => v.GetProperty("name").GetString() == "missing-pin-bounds-retained")), pin, output, out _) == FinTsSignatureTrailerWriteResult.CredentialRequirementsNeedReview && untouchedClock.Reads == before,
                "Known missing PIN bounds do not even query the credential owner.");
            Verify(FinTsPinTanSignatureTrailerWriter.TryEncodeReadContinuationPinOnly(Context(vectors[13]), pin, output, out _) == FinTsSignatureTrailerWriteResult.ContextNeedsReview && untouchedClock.Reads == before,
                "An unresolved continuation context does not query the credential owner.");
        }
        using (var tan = Owner("12345"u8.ToArray(), kind: FinTsCredentialKind.Tan))
        {
            byte[] output = Output();
            Verify(FinTsPinTanSignatureTrailerWriter.TryEncodeReadContinuationPinOnly(basis, tan, output, out int written) == FinTsSignatureTrailerWriteResult.ContextNeedsReview && written == 0 &&
                output.All(b => b == 0xCC) && tan.GetSnapshot().State == FinTsCredentialState.Available && tan.GetSnapshot().CopiesCompleted == 0,
                "A TAN owner passed as PIN is rejected without consumption.");
        }
        foreach (string unavailable in new[] { "cancel", "dispose", "expiry", "clock" })
        {
            var clock = new Clock(); using var pin = Owner("12345"u8.ToArray(), clock); byte[] retained = Buffer(pin)!; byte[] output = Output();
            if (unavailable == "cancel") { pin.Cancel(); }
            else if (unavailable == "dispose") { pin.Dispose(); }
            else if (unavailable == "expiry") { clock.Milliseconds = 10000; }
            else { clock.AfterReads = 1; clock.Action = () => throw new InvalidOperationException("PUBLIC-CLOCK"); }
            Verify(FinTsPinTanSignatureTrailerWriter.TryEncodeReadContinuationPinOnly(basis, pin, output, out int written) == FinTsSignatureTrailerWriteResult.CredentialUnavailable &&
                written == 0 && output.All(b => b == 0xCC) && retained.All(b => b == 0) && Buffer(pin) is null, "Unavailable PINs yield no output and release their owned storage.");
        }
        // Kind check, before/after copy, before/after publishing. Inject at each observable ownership boundary.
        foreach (int stage in new[] { 1, 2, 3, 4, 5 })
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
                    var result = FinTsPinTanSignatureTrailerWriter.TryEncodeReadContinuationPinOnly(basis, pin, output, out written, cancellation.Token);
                    Verify(failure != "token" && result == FinTsSignatureTrailerWriteResult.CredentialUnavailable, "Late ownership failure withholds success.");
                }
                catch (OperationCanceledException) { Verify(failure == "token", "Late token cancellation withholds success."); }
                Verify(written == 0 && (stage == 5 ? output.Take(expectedLength).All(b => b == 0) && output.Skip(expectedLength).All(b => b == 0xCC) : output.All(b => b == 0xCC)),
                    "Late failures clear exactly the published secret prefix; failures before publication leave the destination untouched.");
                Verify(retained.All(b => b == 0) && Buffer(pin) is null && pin.GetSnapshot().CopiesCompleted == (stage >= 4 ? 1 : 0),
                    "Every terminal ownership path clears the actual PIN array and preserves completed-copy accounting.");
                CryptographicOperations.ZeroMemory(output);
            }
        using (var cancellation = new CancellationTokenSource())
        {
            using var pin = Owner("12345"u8.ToArray()); byte[] retained = Buffer(pin)!; byte[] output = Output(); cancellation.Cancel();
            try { FinTsPinTanSignatureTrailerWriter.TryEncodeReadContinuationPinOnly(basis, pin, output, out _, cancellation.Token); Verify(false, "Pre-cancellation must throw."); }
            catch (OperationCanceledException) { Verify(pin.GetSnapshot().State == FinTsCredentialState.Cancelled && retained.All(b => b == 0) && output.All(b => b == 0xCC), "Pre-cancellation clears the owner and emits no bytes."); }
        }
        var culture = CultureInfo.CurrentCulture;
        try
        {
            foreach (string name in new[] { "de-DE", "ar-SA", "tr-TR" })
            {
                CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo(name); using var pin = Owner("12345"u8.ToArray()); byte[] output = Output();
                try
                {
                    Verify(FinTsPinTanSignatureTrailerWriter.TryEncodeReadContinuationPinOnly(basis, pin, output, out int written) == FinTsSignatureTrailerWriteResult.Written &&
                    output.AsSpan(0, written).SequenceEqual("HNSHA:4:2+PAGE2-REF++12345'"u8), "Read trailer bytes are culture independent.");
                }
                finally { CryptographicOperations.ZeroMemory(output); }
            }
        }
        finally { CultureInfo.CurrentCulture = culture; }
        using (var pin = Owner("12345"u8.ToArray()))
        {
            var results = new bool[16];
            Parallel.For(0, results.Length, i =>
            {
                byte[] output = Output();
                try { results[i] = FinTsPinTanSignatureTrailerWriter.TryEncodeReadContinuationPinOnly(basis, pin, output, out int written) == FinTsSignatureTrailerWriteResult.Written && output.AsSpan(0, written).SequenceEqual("HNSHA:4:2+PAGE2-REF++12345'"u8); }
                finally { CryptographicOperations.ZeroMemory(output); }
            });
            Verify(results.All(r => r) && pin.GetSnapshot().CopiesCompleted == 16 && pin.GetSnapshot().State == FinTsCredentialState.Available,
                "Concurrent outputs use independent staging and the shared reusable PIN owner accounts for every copy.");
        }
        foreach (bool missingContext in new[] { false, true })
        {
            using var pin = Owner("12345"u8.ToArray()); byte[] output = Output(); int written = -1;
            try { FinTsPinTanSignatureTrailerWriter.TryEncodeReadContinuationPinOnly(missingContext ? null! : basis, missingContext ? pin : null!, output, out written); Verify(false, "Null required input must fail."); }
            catch (ArgumentNullException error) { Verify(written == 0 && output.All(b => b == 0xCC) && pin.GetSnapshot().CopiesCompleted == 0 && !error.ToString().Contains("12345", StringComparison.Ordinal), "Null inputs fail with fixed diagnostics and no secret side effects."); }
        }
        Console.WriteLine($"FinTS continuation PIN-only trailer encoding passed ({vectors.Length} independent vectors, {count} checks).");
    }
    internal static FinTsPinTanReadContinuationSignatureContext Context(JsonElement vector)
    {
        var (continuation, header, selection, control) = FinTsPinTanReadContinuationSignatureTests.Inputs(vector.GetProperty("context"));
        return FinTsPinTanReadContinuationSignatureContext.Evaluate(continuation, header, selection, control);
    }
    private static FinTsSessionCredential Owner(byte[] value, Clock? clock = null, FinTsCredentialKind kind = FinTsCredentialKind.Pin) => FinTsSessionCredential.CaptureAndClear(value, kind, TimeSpan.FromSeconds(10), clock ?? new Clock());
    private static byte[] Output() => Enumerable.Repeat((byte)0xCC, FinTsPinTanSignatureTrailerWriter.MaximumEncodedLength).ToArray();
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
