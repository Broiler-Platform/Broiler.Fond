using System.Globalization;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Broiler.Fond.Kernel.FinTs;

namespace Broiler.Fond.Kernel.Tests;

internal static class FinTsPinTanSignatureTrailerTests
{
    private static readonly FieldInfo BufferField = typeof(FinTsSessionCredential).GetField("_bytes", BindingFlags.Instance | BindingFlags.NonPublic)!;
    internal static void Run(Action<bool, string> check)
    {
        int count = 0;
        void Verify(bool condition, string message) { count++; check(condition, message); }
        using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream("FinTs.pin-tan-signature-trailer-v1.json")!;
        using var doc = JsonDocument.Parse(stream);
        var vectors = doc.RootElement.GetProperty("vectors").EnumerateArray().ToArray();
        foreach (var v in vectors)
        {
            var context = Evidence(v.GetProperty("context"));
            using var pin = Owner(Convert.FromBase64String(v.GetProperty("pinBase64").GetString()!), FinTsCredentialKind.Pin);
            using var tan = v.GetProperty("tanBase64").GetString() is { } encodedTan ? Owner(Convert.FromBase64String(encodedTan), FinTsCredentialKind.Tan) : null;
            byte[]? retainedTan = tan is null ? null : Buffer(tan);
            byte[] destination = Output();
            var result = FinTsPinTanSignatureTrailerWriter.TryEncode(context, pin, tan, v.GetProperty("segmentNumber").GetInt32(), destination, out int written);
            Verify(result.ToString() == v.GetProperty("result").GetString(), $"Independent trailer result matches {v.GetProperty("name").GetString()}: {result}.");
            byte[] expected = v.GetProperty("wireBase64").GetString() is { } wire ? Convert.FromBase64String(wire) : [];
            Verify(written == expected.Length && destination.AsSpan(0, written).SequenceEqual(expected) && destination.Skip(written).All(b => b == 0xCC), "Independent exact trailer bytes and untouched output tail match.");
            Verify(pin.GetSnapshot().CopiesCompleted == v.GetProperty("pinCopies").GetInt32() && pin.GetSnapshot().State == FinTsCredentialState.Available, "PIN copy counts follow the independent preflight/encoding expectations.");
            if (tan is not null)
            {
                bool consumed = v.GetProperty("tanConsumed").GetBoolean();
                Verify(tan.GetSnapshot().State == (consumed ? FinTsCredentialState.Consumed : FinTsCredentialState.Available) &&
                    (consumed ? retainedTan!.All(b => b == 0) && Buffer(tan) is null : Buffer(tan) is not null), "TAN ownership is consumed and actual storage cleared at the expected stage.");
            }
            if (result == FinTsSignatureTrailerWriteResult.Written)
            {
                // General parsing here is test-only, using public fixture bytes. Production writer must not retain parsed credentials.
                var parsed = FinTsSyntax.ParseSegments(expected).Segments.Single();
                Verify(parsed.Code == "HNSHA" && parsed.Version == 2 && parsed.Reference is null && parsed.Fields.Count == 3 && parsed.Fields[1].Elements.Single().IsEmpty, "The fixture has HNSHA-2 with empty validation result and no extra fields.");
                Verify(parsed.Fields[0].Elements.Single().CopyValueBytes().SequenceEqual(Encoding.Latin1.GetBytes(context.Header.ControlReference)) &&
                    parsed.Fields[2].Elements[0].CopyValueBytes().SequenceEqual(Convert.FromBase64String(v.GetProperty("pinBase64").GetString()!)), "Control reference and credential delimiters decode exactly in synthetic fixtures.");
                if (tan is not null)
                { Verify(FinTsPinTanSignatureTrailerWriter.TryEncode(context, pin, tan, v.GetProperty("segmentNumber").GetInt32(), new byte[440], out _) == FinTsSignatureTrailerWriteResult.CredentialUnavailable, "A consumed TAN cannot be reused by a second encoding."); }
            }
            CryptographicOperations.ZeroMemory(destination);
        }
        Verify(vectors.Length == 12, "All twelve independent signature-trailer fixtures ran.");
        var sample = vectors[1]; var matched = Evidence(sample.GetProperty("context"));
        FinTsSessionCredential Pin(TimeProvider? clock = null) => Owner("PUBLIC-PIN"u8.ToArray(), FinTsCredentialKind.Pin, clock);
        FinTsSessionCredential Tan(TimeProvider? clock = null) => Owner("PUBLIC-TAN"u8.ToArray(), FinTsCredentialKind.Tan, clock);
        foreach (int capacity in new[] { 0, 1, 40, 439 })
        {
            using var pin = Pin(); using var tan = Tan(); byte[] output = Enumerable.Repeat((byte)0xCC, capacity).ToArray();
            Verify(FinTsPinTanSignatureTrailerWriter.TryEncode(matched, pin, tan, 5, output, out int written) == FinTsSignatureTrailerWriteResult.DestinationTooSmall && written == 0 && output.All(b => b == 0xCC) && pin.GetSnapshot().CopiesCompleted == 0 && tan.GetSnapshot().State == FinTsCredentialState.Available, "Insufficient worst-case reserve fails before any credential copy or consumption.");
            byte[] full = Output(); Verify(FinTsPinTanSignatureTrailerWriter.TryEncode(matched, pin, tan, 5, full, out _) == FinTsSignatureTrailerWriteResult.Written, "Corrected destination can use the unconsumed TAN."); CryptographicOperations.ZeroMemory(full);
        }
        foreach (int number in new[] { -1, 0, 2, 4, 6, 999, int.MaxValue })
        {
            using var pin = Pin(); using var tan = Tan();
            Verify(FinTsPinTanSignatureTrailerWriter.TryEncode(matched, pin, tan, number, Output(), out _) == FinTsSignatureTrailerWriteResult.ContextNeedsReview && pin.GetSnapshot().CopiesCompleted == 0 && tan.GetSnapshot().CopiesCompleted == 0, "Only the derived initialization trailer position is accepted before credential access.");
        }
        foreach (bool wrongPin in new[] { false, true })
        {
            using var pin = wrongPin ? Tan() : Pin(); using var tan = wrongPin ? Tan() : Pin();
            Verify(FinTsPinTanSignatureTrailerWriter.TryEncode(matched, pin, tan, 5, Output(), out _) == FinTsSignatureTrailerWriteResult.ContextNeedsReview && pin.GetSnapshot().CopiesCompleted == 0 && tan.GetSnapshot().CopiesCompleted == 0, "Credential kinds cannot be swapped or consumed on invalid context.");
        }
        using (var pin = Pin())
        { Verify(FinTsPinTanSignatureTrailerWriter.TryEncode(matched, pin, pin, 5, Output(), out _) == FinTsSignatureTrailerWriteResult.ContextNeedsReview && pin.GetSnapshot().CopiesCompleted == 0, "The same PIN owner cannot also supply TAN material."); }
        foreach (bool unavailablePin in new[] { false, true })
        {
            using var pin = Pin(); using var tan = Tan(); if (unavailablePin) { pin.Dispose(); } else { tan.Cancel(); }
            byte[] output = Output();
            Verify(FinTsPinTanSignatureTrailerWriter.TryEncode(matched, pin, tan, 5, output, out int written) == FinTsSignatureTrailerWriteResult.CredentialUnavailable && written == 0 && output.All(b => b == 0xCC) && tan.GetSnapshot().CopiesCompleted == 0, "Unavailable owners produce no output and never consume a pending TAN after a PIN failure.");
        }
        foreach (byte bad in new byte[] { 0, 10, 31, 127, 128, 160 })
        {
            using var pin = Owner(new byte[] { bad }, FinTsCredentialKind.Pin); using var tan = Tan(); byte[] output = Output();
            Verify(FinTsPinTanSignatureTrailerWriter.TryEncode(matched, pin, tan, 5, output, out _) == FinTsSignatureTrailerWriteResult.InvalidCredentialText && output.All(b => b == 0xCC) && tan.GetSnapshot().State == FinTsCredentialState.Available, "Invalid PIN octets are rejected before TAN consumption without emitting bytes.");
        }
        using (var pin = Owner(new byte[] { 161, 255 }, FinTsCredentialKind.Pin))
        {
            byte[] output = Output();
            Verify(FinTsPinTanSignatureTrailerWriter.TryEncode(matched, pin, null, 5, output, out int written) == FinTsSignatureTrailerWriteResult.Written && output.AsSpan(0, written).Contains((byte)255), "Printable high Latin-1 octets are preserved without transcoding."); CryptographicOperations.ZeroMemory(output);
        }
        using (var cancellation = new CancellationTokenSource())
        {
            using var pin = Pin(); using var tan = Tan(); byte[] ownedPin = Buffer(pin)!, ownedTan = Buffer(tan)!, output = Output(); cancellation.Cancel();
            try { FinTsPinTanSignatureTrailerWriter.TryEncode(matched, pin, tan, 5, output, out _, cancellation.Token); Verify(false, "Cancellation must throw."); }
            catch (OperationCanceledException)
            { Verify(pin.GetSnapshot().State == FinTsCredentialState.Cancelled && tan.GetSnapshot().State == FinTsCredentialState.Cancelled && ownedPin.All(b => b == 0) && ownedTan.All(b => b == 0) && output.All(b => b == 0xCC), "Pre-cancelled encoding clears both owners and emits no output."); }
        }
        // Five PIN clock reads occur after capture: kind check, before/after copying, before/after publishing.
        foreach (bool cancel in new[] { false, true })
        {
            using var cancellation = new CancellationTokenSource(); var clock = new HookClock(); using var pin = Pin(clock); using var tan = Tan();
            byte[] ownedPin = Buffer(pin)!, ownedTan = Buffer(tan)!, output = Output();
            clock.AfterReads = 5; clock.Action = () => { if (cancel) { cancellation.Cancel(); } else { clock.Milliseconds = 10000; } };
            int written = -1;
            try
            {
                var result = FinTsPinTanSignatureTrailerWriter.TryEncode(matched, pin, tan, 5, output, out written, cancellation.Token);
                Verify(!cancel && result == FinTsSignatureTrailerWriteResult.CredentialUnavailable, "Post-publication expiry withholds success.");
            }
            catch (OperationCanceledException) { Verify(cancel, "Post-publication cancellation withholds success."); }
            int expectedLength = Convert.FromBase64String(sample.GetProperty("wireBase64").GetString()!).Length;
            Verify(written == 0 && output.Take(expectedLength).All(b => b == 0) && output.Skip(expectedLength).All(b => b == 0xCC) && ownedPin.All(b => b == 0) && ownedTan.All(b => b == 0) && tan.GetSnapshot().State == FinTsCredentialState.Consumed, "Failed publication zeroes the entire secret output prefix and never restores the consumed TAN.");
        }
        var tanClock = new HookClock();
        using (var pin = Pin())
        using (var tan = Tan(tanClock))
        {
            tanClock.AfterReads = 3; tanClock.Action = pin.Cancel; byte[] output = Output();
            Verify(FinTsPinTanSignatureTrailerWriter.TryEncode(matched, pin, tan, 5, output, out _) == FinTsSignatureTrailerWriteResult.CredentialUnavailable && output.All(b => b == 0xCC) && tan.GetSnapshot().State == FinTsCredentialState.Consumed, "A PIN cancelled during TAN consumption prevents output while the TAN remains consumed.");
        }
        var culture = CultureInfo.CurrentCulture;
        try
        {
            foreach (string name in new[] { "de-DE", "ar-SA", "tr-TR" })
            {
                CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo(name); using var pin = Pin(); using var tan = Tan(); byte[] output = Output();
                Verify(FinTsPinTanSignatureTrailerWriter.TryEncode(matched, pin, tan, 5, output, out int written) == FinTsSignatureTrailerWriteResult.Written && output.AsSpan(0, written).SequenceEqual(Convert.FromBase64String(sample.GetProperty("wireBase64").GetString()!)), "Trailer bytes are culture-independent."); CryptographicOperations.ZeroMemory(output);
            }
        }
        finally { CultureInfo.CurrentCulture = culture; }
        using (var pin = Pin())
        using (var tan = Tan())
        {
            int successes = 0; var outcomes = new FinTsSignatureTrailerWriteResult[16];
            Parallel.For(0, outcomes.Length, i =>
            {
                byte[] output = Output();
                try { outcomes[i] = FinTsPinTanSignatureTrailerWriter.TryEncode(matched, pin, tan, 5, output, out _); if (outcomes[i] == FinTsSignatureTrailerWriteResult.Written) { Interlocked.Increment(ref successes); } }
                finally { CryptographicOperations.ZeroMemory(output); }
            });
            Verify(successes == 1 && outcomes.All(r => r is FinTsSignatureTrailerWriteResult.Written or FinTsSignatureTrailerWriteResult.CredentialUnavailable) && tan.GetSnapshot().CopiesCompleted == 1, "Concurrent trailer attempts can publish only one copy of a shared TAN.");
        }
        foreach (bool nullContext in new[] { false, true })
        {
            using var pin = Pin(); byte[] output = Output();
            try { FinTsPinTanSignatureTrailerWriter.TryEncode(nullContext ? null! : matched, nullContext ? pin : null!, null, 5, output, out _); Verify(false, "Null required input must fail."); }
            catch (ArgumentNullException error) { Verify(output.All(b => b == 0xCC) && !error.ToString().Contains("PUBLIC-PIN", StringComparison.Ordinal), "Null arguments emit no bytes and use fixed diagnostics."); }
        }
        Verify(Enum.GetNames<FinTsSignatureTrailerWriteResult>().All(n => !n.Contains("PUBLIC", StringComparison.Ordinal)), "Results expose only fixed outcome codes.");
        Console.WriteLine($"FinTS PIN/TAN signature-trailer encoding verification passed ({vectors.Length} independent vectors, {count} checks).");
    }
    private static byte[] Output() => Enumerable.Repeat((byte)0xCC, FinTsPinTanSignatureTrailerWriter.MaximumEncodedLength).ToArray();
    private static byte[]? Buffer(FinTsSessionCredential owner) => (byte[]?)BufferField.GetValue(owner);
    private static FinTsSessionCredential Owner(byte[] bytes, FinTsCredentialKind kind, TimeProvider? clock = null) => FinTsSessionCredential.CaptureAndClear(bytes, kind, TimeSpan.FromSeconds(10), clock ?? new HookClock());
    private sealed class HookClock : TimeProvider
    {
        internal long Milliseconds { get; set; }
        internal int AfterReads { get; set; } = int.MaxValue;
        internal Action? Action { get; set; }
        public override long TimestampFrequency => 1000;
        public override long GetTimestamp() { if (--AfterReads == 0) { Action?.Invoke(); } return Milliseconds; }
    }
    internal static FinTsPinTanSignatureEvidence Evidence(JsonElement v)
    {
        FinTsMessageFrame Frame(string property) => FinTsMessageFrame.Parse(Convert.FromBase64String(v.GetProperty(property).GetString()!));
        var selection = new FinTsPinTanSignatureSelection(v.GetProperty("profileVersion").GetInt32(), v.GetProperty("securityFunction").GetInt32(), v.GetProperty("tanSegmentVersion").GetInt32());
        string user = v.GetProperty("expectedUserId").GetString()!, control = v.GetProperty("controlReference").GetString()!;
        var request = v.GetProperty("requestKind").GetString() == "initialization" ? FinTsPinTanSignatureRequestContext.ForInitialization(FinTsUnsignedInitializationRequest.Parse(Frame("requestBase64")), user, control, selection)
            : FinTsPinTanSignatureRequestContext.ForSynchronization(FinTsUnsignedSynchronizationRequest.Parse(Frame("requestBase64")), user, control, selection);
        var header = FinTsPinTanSignatureHeader.Parse(FinTsSyntax.ParseSegments(Convert.FromBase64String(v.GetProperty("headerBase64").GetString()!)).Segments.Single());
        var response = FinTsResponse.Parse(Frame("procedureResponseBase64"));
        var procedures = v.GetProperty("missingProcedureContext").GetBoolean() ? null : new FinTsPinTanProcedureContext(FinTsUnsignedInitializationRequest.Parse(Frame("originRequestBase64")),
            FinTsTanParameterSet.Parse(FinTsParameterSet.Parse(response)), FinTsPermittedProcedureSet.Parse(response));
        return FinTsPinTanSignatureEvidence.Evaluate(request, header, procedures);
    }
}
