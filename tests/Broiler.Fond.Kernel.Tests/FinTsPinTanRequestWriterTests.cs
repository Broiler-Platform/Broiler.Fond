using System.Globalization;
using System.Reflection;
using System.Security.Cryptography;
using System.Text.Json;
using Broiler.Fond.Kernel.FinTs;

namespace Broiler.Fond.Kernel.Tests;

internal static class FinTsPinTanRequestWriterTests
{
    internal static void Run(Action<bool, string> check)
    {
        int count = 0;
        void Verify(bool condition, string message) { count++; check(condition, message); }
        using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream("FinTs.pin-tan-request-assembly-v1.json")!;
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
                var result = FinTsPinTanRequestWriter.TryEncode(evidence, pin, tan, output, out int written);
                byte[] expected = vector.GetProperty("wireBase64").GetString() is { } wire ? Convert.FromBase64String(wire) : [];
                Verify(result.ToString() == vector.GetProperty("result").GetString(), "Assembly matches independent outcome: " + vector.GetProperty("name").GetString());
                Verify(written == expected.Length && output.AsSpan(0, written).SequenceEqual(expected) && output.Skip(written).All(b => b == 0xCC), "Exact framed bytes, reported byte length and untouched tail match independent fixtures.");
                Verify(original.SequenceEqual(evidence.Request.Frame.Syntax.CopyWireBytes()), "Assembly leaves the original unsigned request and evidence intact.");
                Verify(pin.GetSnapshot().CopiesCompleted == vector.GetProperty("pinCopies").GetInt32(), "Preflight failures avoid PIN copy-out.");
                if (tan is not null)
                {
                    bool consumed = vector.GetProperty("tanConsumed").GetBoolean();
                    Verify(tan.GetSnapshot().State == (consumed ? FinTsCredentialState.Consumed : FinTsCredentialState.Available) && (!consumed || tanStorage!.All(b => b == 0)), "Assembly preserves TAN consumption and actual owned-buffer erasure.");
                }
                if (result == FinTsPinTanRequestWriteResult.Written)
                {
                    // Public synthetic bytes only. Production must not parse secret-bearing assembled output.
                    var frame = FinTsMessageFrame.Parse(output.AsSpan(0, written));
                    var segments = frame.Syntax.Segments;
                    bool sync = evidence.Request.Synchronization is not null;
                    Verify(segments.Select(s => s.Code).SequenceEqual(sync ? new[] { "HNHBK", "HNSHK", "HKIDN", "HKVVB", "HKSYN", "HNSHA", "HNHBS" } : new[] { "HNHBK", "HNSHK", "HKIDN", "HKVVB", "HNSHA", "HNHBS" }) && segments.Select(s => s.Number).SequenceEqual(Enumerable.Range(1, segments.Count)), "Security and business segments have exact contiguous initialization/synchronization positions.");
                    Verify(frame.MessageNumber == 1 && segments[0].Fields[2].Elements[0].CopyValueBytes().SequenceEqual("0"u8.ToArray()), "First-dialogue identity and message counter remain zero/one.");
                    for (int i = 1; i < evidence.Request.Frame.Syntax.Segments.Count - 1; i++)
                    { Verify(SameFields(evidence.Request.Frame.Syntax.Segments[i], segments[i + 1]), "Renumbering preserves every business field and component."); }
                    Verify(SameFields(evidence.Header.Source, segments[1]) && segments[1].Fields[2].Elements[0].CopyValueBytes().SequenceEqual(segments[^2].Fields[0].Elements[0].CopyValueBytes()), "Header source fields and matching header/trailer control are retained exactly.");
                }
            }
            finally { CryptographicOperations.ZeroMemory(output); }
        }
        Verify(vectors.Length == 14, "All fourteen independent assembly fixtures ran.");
        var sample = vectors[1]; var matched = FinTsPinTanSignatureTrailerTests.Evidence(sample.GetProperty("context"));
        int expectedLength = Convert.FromBase64String(sample.GetProperty("wireBase64").GetString()!).Length;
        FinTsSessionCredential Pin(HookClock? clock = null) => Owner("PUBLIC-PIN"u8.ToArray(), FinTsCredentialKind.Pin, clock);
        FinTsSessionCredential Tan() => Owner("PUBLIC-TAN"u8.ToArray(), FinTsCredentialKind.Tan);
        foreach (int capacity in new[] { 0, 1, expectedLength, FinTsPinTanRequestWriter.MaximumEncodedLength - 1 })
        {
            using var pin = Pin(); using var tan = Tan(); byte[] output = Enumerable.Repeat((byte)0xCC, capacity).ToArray();
            Verify(FinTsPinTanRequestWriter.TryEncode(matched, pin, tan, output, out int written) == FinTsPinTanRequestWriteResult.DestinationTooSmall && written == 0 && output.All(b => b == 0xCC) && pin.GetSnapshot().CopiesCompleted == 0 && tan.GetSnapshot().CopiesCompleted == 0, "The whole-message reserve is required before credentials are copied or consumed.");
        }
        // Trailer checks the PIN five times; whole-message handoff adds two checks. Exercise both boundaries.
        foreach (int stage in new[] { 3, 4, 5, 6, 7 })
            foreach (bool cancel in new[] { false, true })
            {
                var clock = new HookClock(); using var pin = Pin(clock); using var tan = Tan(); using var cancellation = new CancellationTokenSource();
                byte[] ownedPin = Storage(pin)!, output = Output(); int written = -1;
                clock.AfterReads = stage; clock.Action = () => { if (cancel) { cancellation.Cancel(); } else { clock.Milliseconds = 10000; } };
                try
                {
                    var result = FinTsPinTanRequestWriter.TryEncode(matched, pin, tan, output, out written, cancellation.Token);
                    Verify(!cancel && result == FinTsPinTanRequestWriteResult.CredentialUnavailable, "Expiry rejects a staged or published request.");
                }
                catch (OperationCanceledException) { Verify(cancel, "Cancellation rejects a staged or published request."); }
                Verify(written == 0 && (stage == 7 ? output.Take(expectedLength).All(b => b == 0) && output.Skip(expectedLength).All(b => b == 0xCC) : output.All(b => b == 0xCC)), "Failure clears an entire published message; before publication the destination remains untouched.");
                Verify(ownedPin.All(b => b == 0) && tan.GetSnapshot().CopiesCompleted == (stage >= 4 ? 1 : 0) && (stage < 4 || tan.GetSnapshot().State == FinTsCredentialState.Consumed), "Cancellation/expiry erases PIN storage and never restores a copied TAN.");
            }
        using (var pin = Pin())
        using (var tan = Tan())
        using (var cancellation = new CancellationTokenSource())
        {
            byte[] ownedPin = Storage(pin)!, ownedTan = Storage(tan)!, output = Output(); cancellation.Cancel();
            try { FinTsPinTanRequestWriter.TryEncode(matched, pin, tan, output, out _, cancellation.Token); Verify(false, "Pre-cancellation must throw."); }
            catch (OperationCanceledException)
            { Verify(output.All(b => b == 0xCC) && ownedPin.All(b => b == 0) && ownedTan.All(b => b == 0), "Pre-cancellation erases both owners without publishing any request."); }
        }
        foreach (bool wrongKind in new[] { false, true })
        {
            using var pin = wrongKind ? Tan() : Pin(); using var tan = Tan(); if (!wrongKind) { pin.Dispose(); }
            byte[] output = Output();
            Verify(FinTsPinTanRequestWriter.TryEncode(matched, pin, tan, output, out int written) == (wrongKind ? FinTsPinTanRequestWriteResult.ContextNeedsReview : FinTsPinTanRequestWriteResult.CredentialUnavailable) && written == 0 && output.All(b => b == 0xCC) && tan.GetSnapshot().CopiesCompleted == 0, "Wrong-kind or unavailable PIN leaves output untouched and TAN unconsumed.");
        }
        var culture = CultureInfo.CurrentCulture;
        try
        {
            foreach (string name in new[] { "de-DE", "ar-SA", "tr-TR" })
            {
                CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo(name); using var pin = Pin(); using var tan = Tan(); byte[] output = Output();
                try { Verify(FinTsPinTanRequestWriter.TryEncode(matched, pin, tan, output, out int written) == FinTsPinTanRequestWriteResult.Written && output.AsSpan(0, written).SequenceEqual(Convert.FromBase64String(sample.GetProperty("wireBase64").GetString()!)), "Whole-message size, counters and text are culture-independent."); }
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
                try { outcomes[i] = FinTsPinTanRequestWriter.TryEncode(matched, pin, tan, output, out _); if (outcomes[i] == FinTsPinTanRequestWriteResult.Written) { Interlocked.Increment(ref successes); } }
                finally { CryptographicOperations.ZeroMemory(output); }
            });
            Verify(successes == 1 && outcomes.All(r => r is FinTsPinTanRequestWriteResult.Written or FinTsPinTanRequestWriteResult.CredentialUnavailable), "Only one concurrent assembled message can consume a shared TAN owner.");
        }
        Console.WriteLine($"FinTS PIN/TAN request assembly verification passed ({vectors.Length} independent vectors, {count} checks).");
    }

    private static bool SameFields(FinTsSegment first, FinTsSegment second) => first.Fields.Count == second.Fields.Count && first.Fields.Zip(second.Fields).All(pair =>
        pair.First.Elements.Count == pair.Second.Elements.Count && pair.First.Elements.Zip(pair.Second.Elements).All(element => element.First.CopyValueBytes().SequenceEqual(element.Second.CopyValueBytes())));
    private static byte[] Output() => Enumerable.Repeat((byte)0xCC, FinTsPinTanRequestWriter.MaximumEncodedLength).ToArray();
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
