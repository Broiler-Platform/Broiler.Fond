using System.Globalization;
using System.Reflection;
using System.Security.Cryptography;
using System.Text.Json;
using Broiler.Fond.Kernel.FinTs;

namespace Broiler.Fond.Kernel.Tests;

internal static class FinTsPinTanDialogueEndTests
{
    internal static void Run(Action<bool, string> check)
    {
        int count = 0;
        void Verify(bool condition, string message) { count++; check(condition, message); }
        using var document = JsonDocument.Parse(Assembly.GetExecutingAssembly().GetManifestResourceStream("FinTs.pin-tan-dialogue-end-v1.json")!);
        var vectors = document.RootElement.GetProperty("vectors").EnumerateArray().ToArray();
        foreach (var vector in vectors)
        {
            var context = Context(vector);
            var expectedIssues = vector.GetProperty("issues").EnumerateArray().Aggregate(FinTsPinTanDialogueEndContextIssue.None,
                (flags, issue) => flags | Enum.Parse<FinTsPinTanDialogueEndContextIssue>(issue.GetString()!));
            Verify(context.Issues == expectedIssues, "Independent closing context issues match: " + vector.GetProperty("name").GetString());
            byte[] original = context.Request.Frame.Syntax.CopyWireBytes();
            using var pin = Owner(Convert.FromBase64String(vector.GetProperty("pinBase64").GetString()!));
            byte[] output = Output();
            try
            {
                var result = FinTsPinTanDialogueEndWriter.TryEncode(context, pin, output, out int written);
                byte[] expected = vector.GetProperty("wireBase64").GetString() is { } encoded ? Convert.FromBase64String(encoded) : [];
                Verify(result == (expectedIssues == 0 ? FinTsPinTanRequestWriteResult.Written : FinTsPinTanRequestWriteResult.ContextNeedsReview) && written == expected.Length, "Independent outcome and actual message length match.");
                Verify(output.AsSpan(0, written).SequenceEqual(expected) && output.Skip(written).All(b => b == 0xCC), "Exact independent closing envelope bytes match and unused output remains untouched.");
                Verify(pin.GetSnapshot().CopiesCompleted == (expectedIssues == 0 ? 1 : 0) && original.SequenceEqual(context.Request.Frame.Syntax.CopyWireBytes()), "Review precedes credential copy-out and encoding preserves source bytes.");
                if (result != FinTsPinTanRequestWriteResult.Written) { continue; }
                var frame = FinTsMessageFrame.Parse(output.AsSpan(0, written)); var outer = frame.Syntax.Segments;
                byte[] payload = outer[2].Fields[0].Elements[0].CopyValueBytes();
                Verify(frame.MessageNumber == 2 && outer.Select(s => s.Number).SequenceEqual(new[] { 1, 998, 999, 5 }) && outer.Select(s => s.Code).SequenceEqual(new[] { "HNHBK", "HNVSK", "HNVSD", "HNHBS" }), "Closing uses message two with reserved wrapper numbers and logical trailer five.");
                Verify(payload.SequenceEqual(Convert.FromBase64String(vector.GetProperty("payloadBase64").GetString()!)), "The HNVSD binary payload matches independent bytes including escapes.");
                var inner = FinTsSyntax.ParseSegments(payload).Segments;
                Verify(inner.Select(s => s.Code).SequenceEqual(new[] { "HNSHK", "HKEND", "HNSHA" }) && inner.Select(s => s.Number).SequenceEqual(new[] { 2, 3, 4 }) && inner[2].Fields[2].Elements.Count == 1, "Closing contains only signature header, HKEND and PIN-only signature trailer.");
                Verify(FinTsDialogueEndRequest.Parse(inner[1]).DialogueId == context.Synchronization.ReportedDialogueId && FinTsPinTanSignatureHeader.Parse(inner[0]).SystemId == context.Header.SystemId, "Dialogue and system metadata remain bound through encoding.");
            }
            finally { CryptographicOperations.ZeroMemory(output); }
        }
        Verify(vectors.Length == 20, "All twenty independent closing vectors ran.");
        var sample = vectors[0]; var matched = Context(sample);
        int expectedLength = Convert.FromBase64String(sample.GetProperty("wireBase64").GetString()!).Length;
        var repeated = FinTsPinTanDialogueEndContext.Evaluate(matched.Synchronization, matched.Request, matched.Header);
        Verify(repeated.HasMatchingEvidence && ReferenceEquals(repeated.Synchronization, matched.Synchronization) && ReferenceEquals(repeated.Request, matched.Request) && ReferenceEquals(repeated.Header, matched.Header), "Context comparison is repeatable and preserves exact source objects without consuming a handoff.");
        foreach (int capacity in new[] { 0, expectedLength, FinTsPinTanDialogueEndWriter.MaximumEncodedLength - 1 })
        {
            using var pin = Pin(); byte[] output = Output(capacity);
            Verify(FinTsPinTanDialogueEndWriter.TryEncode(matched, pin, output, out int written) == FinTsPinTanRequestWriteResult.DestinationTooSmall && written == 0 && output.All(b => b == 0xCC) && pin.GetSnapshot().CopiesCompleted == 0, "Full reserve is required before credential access, even if actual wire would fit.");
        }
        // Five observations in credential/trailer processing and two around final envelope publication.
        foreach (int stage in Enumerable.Range(1, 7))
            foreach (string failure in new[] { "expiry", "cancel", "clock" })
            {
                var clock = new HookClock(); using var pin = Pin(clock); using var cancellation = new CancellationTokenSource();
                byte[] storage = Storage(pin)!, output = Output(); int written = -1;
                clock.AfterReads = stage; clock.Action = () =>
                {
                    if (failure == "expiry") { clock.Milliseconds = 10000; }
                    else if (failure == "cancel") { cancellation.Cancel(); }
                    else { throw new InvalidOperationException("PUBLIC-CLOCK-DETAIL"); }
                };
                try
                {
                    var result = FinTsPinTanDialogueEndWriter.TryEncode(matched, pin, output, out written, cancellation.Token);
                    Verify(failure != "cancel" && result == FinTsPinTanRequestWriteResult.CredentialUnavailable, "Expiry and clock failure prevent publication at each credential boundary.");
                }
                catch (OperationCanceledException) { Verify(failure == "cancel", "Cancellation propagates without returning credential output."); }
                Verify(written == 0 && storage.All(b => b == 0) && pin.GetSnapshot().State != FinTsCredentialState.Available, "Failed encoding clears owned PIN storage and reports no bytes.");
                Verify(stage == 7 ? output.Take(expectedLength).All(b => b == 0) && output.Skip(expectedLength).All(b => b == 0xCC) : output.All(b => b == 0xCC), "Late failed publication clears the whole envelope prefix; earlier failure touches no caller output.");
                CryptographicOperations.ZeroMemory(output);
            }
        foreach (byte[] invalid in new[] { new byte[] { 10 }, new byte[] { 127 }, new byte[] { 160 } })
        {
            using var pin = Owner(invalid); byte[] output = Output();
            Verify(FinTsPinTanDialogueEndWriter.TryEncode(matched, pin, output, out int written) == FinTsPinTanRequestWriteResult.InvalidCredentialText && written == 0 && output.All(b => b == 0xCC), "Nonprintable PIN text cannot become wire output.");
        }
        using (var wrongKind = Owner("PUBLIC-TAN"u8.ToArray(), kind: FinTsCredentialKind.Tan))
        {
            byte[] output = Output();
            Verify(FinTsPinTanDialogueEndWriter.TryEncode(matched, wrongKind, output, out int written) == FinTsPinTanRequestWriteResult.ContextNeedsReview && written == 0 && wrongKind.GetSnapshot().CopiesCompleted == 0 && output.All(b => b == 0xCC), "A TAN owner cannot be passed as the PIN and remains unconsumed.");
        }
        using (var pin = Pin())
        {
            pin.Dispose(); byte[] output = Output();
            Verify(FinTsPinTanDialogueEndWriter.TryEncode(matched, pin, output, out int written) == FinTsPinTanRequestWriteResult.CredentialUnavailable && written == 0 && output.All(b => b == 0xCC), "Disposed credentials cannot publish closing bytes.");
        }
        var culture = CultureInfo.CurrentCulture;
        try
        {
            foreach (string name in new[] { "de-DE", "ar-SA", "tr-TR" })
            {
                CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo(name); using var pin = Pin(); byte[] output = Output();
                Verify(FinTsPinTanDialogueEndWriter.TryEncode(matched, pin, output, out int written) == FinTsPinTanRequestWriteResult.Written && output.AsSpan(0, written).SequenceEqual(Convert.FromBase64String(sample.GetProperty("wireBase64").GetString()!)), "Closing encoding is culture independent.");
                CryptographicOperations.ZeroMemory(output);
            }
        }
        finally { CultureInfo.CurrentCulture = culture; }
        using (var cancellation = new CancellationTokenSource())
        using (var pin = Pin())
        {
            cancellation.Cancel(); byte[] output = Output();
            try { FinTsPinTanDialogueEndWriter.TryEncode(matched, pin, output, out _, cancellation.Token); Verify(false, "Pre-cancellation must throw."); }
            catch (OperationCanceledException) { Verify(pin.GetSnapshot().State == FinTsCredentialState.Cancelled && output.All(b => b == 0xCC), "Pre-cancelled encoding cancels the PIN without output."); }
            try { FinTsPinTanDialogueEndContext.Evaluate(matched.Synchronization, matched.Request, matched.Header, cancellation.Token); Verify(false, "Context cancellation must throw."); }
            catch (OperationCanceledException) { Verify(true, "Context comparison observes cancellation."); }
        }
        foreach (int index in Enumerable.Range(0, 3))
        {
            try { FinTsPinTanDialogueEndContext.Evaluate(index == 0 ? null! : matched.Synchronization, index == 1 ? null! : matched.Request, index == 2 ? null! : matched.Header); Verify(false, "Null context inputs must fail."); }
            catch (ArgumentNullException) { Verify(true, "Required context inputs are checked."); }
        }
        foreach (bool nullContext in new[] { false, true })
        {
            using var pin = Pin(); byte[] output = Output();
            try { FinTsPinTanDialogueEndWriter.TryEncode(nullContext ? null! : matched, nullContext ? pin : null!, output, out _); Verify(false, "Null encoding inputs must fail."); }
            catch (ArgumentNullException) { Verify(output.All(b => b == 0xCC), "Null encoding arguments publish no output."); }
        }
        Console.WriteLine($"FinTS PIN/TAN dialogue-end context/encoding verification passed ({vectors.Length} independent vectors, {count} checks).");
    }
    private static byte[] Output(int capacity = FinTsPinTanDialogueEndWriter.MaximumEncodedLength) => Enumerable.Repeat((byte)0xCC, capacity).ToArray();
    private static FinTsSessionCredential Pin(HookClock? clock = null) => Owner("PUBLIC-PIN"u8.ToArray(), clock);
    private static FinTsSessionCredential Owner(byte[] input, HookClock? clock = null, FinTsCredentialKind kind = FinTsCredentialKind.Pin) => FinTsSessionCredential.CaptureAndClear(input, kind, TimeSpan.FromSeconds(10), clock ?? new HookClock());
    private static byte[]? Storage(FinTsSessionCredential owner) => (byte[]?)typeof(FinTsSessionCredential).GetField("_bytes", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(owner);
    internal static FinTsPinTanDialogueEndContext Context(JsonElement vector)
    {
        var sync = vector.GetProperty("synchronization");
        var candidate = FinTsPinTanRequestBinding.ForEnvelopeCandidate(FinTsPinTanSignatureTrailerTests.Evidence(sync.GetProperty("context")));
        var responseFrame = FinTsMessageFrame.Parse(Convert.FromBase64String(sync.GetProperty("responseBase64").GetString()!));
        var response = sync.GetProperty("wrapped").GetBoolean() ? FinTsResponse.ParsePinTan(FinTsPinTanEnvelope.Parse(responseFrame)) : FinTsResponse.Parse(responseFrame);
        var recovery = sync.GetProperty("previousDialogueId").ValueKind == JsonValueKind.Null ? null : new FinTsSynchronizationRecoveryContext(sync.GetProperty("previousDialogueId").GetString()!, sync.GetProperty("lastSubmittedMessageNumber").GetInt32());
        var evidence = FinTsPinTanSynchronizationEvidence.Evaluate(FinTsPinTanResponseBinding.Evaluate(candidate, response), FinTsSynchronizationDataSet.Parse(response), recovery);
        var request = FinTsUnsignedDialogueEndRequest.Parse(FinTsMessageFrame.Parse(Convert.FromBase64String(vector.GetProperty("requestBase64").GetString()!)), vector.GetProperty("expectedBankMessageNumber").GetInt32());
        var header = FinTsPinTanSignatureHeader.Parse(FinTsSyntax.ParseSegments(Convert.FromBase64String(vector.GetProperty("headerBase64").GetString()!)).Segments.Single());
        return FinTsPinTanDialogueEndContext.Evaluate(evidence, request, header);
    }
    private sealed class HookClock : TimeProvider
    {
        internal long Milliseconds { get; set; }
        internal int AfterReads { get; set; } = int.MaxValue;
        internal Action? Action { get; set; }
        public override long TimestampFrequency => 1000;
        public override long GetTimestamp() { if (--AfterReads == 0) { Action?.Invoke(); } return Milliseconds; }
    }
}
