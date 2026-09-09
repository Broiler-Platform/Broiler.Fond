using System.Globalization;
using System.Reflection;
using System.Text;
using System.Text.Json;
using Broiler.Fond.Kernel.FinTs;

namespace Broiler.Fond.Kernel.Tests;

internal static class FinTsDialogueEndTests
{
    internal static void Run(Action<bool, string> check)
    {
        int count = 0;
        void Verify(bool condition, string message) { count++; check(condition, message); }
        void Reject(Action action, FinTsSyntaxError expected = FinTsSyntaxError.InvalidDialogueEnd)
        {
            try { action(); Verify(false, "Invalid dialogue-end input must fail."); }
            catch (FinTsFormatException error) { Verify(error.Error == expected && !error.ToString().Contains("PUBLIC-SECRET", StringComparison.Ordinal), "Dialogue-end failures use fixed diagnostics without source text."); }
        }
        void Has(FinTsDialogueEndEvidence evidence, FinTsDialogueEndIssue issue) => Verify(evidence.Issues.HasFlag(issue) && !evidence.HasMatchingEvidence && evidence.Outcome == FinTsDialogueEndOutcome.NeedsReview, "Unresolved close evidence remains for review: " + issue);
        using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream("FinTs.dialogue-end-v1.json")!;
        using var doc = JsonDocument.Parse(stream);
        var vectors = doc.RootElement.GetProperty("vectors").EnumerateArray().ToArray();
        foreach (var v in vectors)
        {
            string dialogue = v.GetProperty("dialogueId").GetString()!;
            byte[] wire = FinTsUnsignedDialogueEndWriter.Encode(dialogue, v.GetProperty("clientMessageNumber").GetInt32());
            Verify(wire.SequenceEqual(Convert.FromBase64String(v.GetProperty("requestBase64").GetString()!)), "Unsigned closing bytes match the independent wire fixture.");
            var frame = FinTsMessageFrame.Parse(wire);
            var request = FinTsUnsignedDialogueEndRequest.Parse(frame, v.GetProperty("expectedBankMessageNumber").GetInt32());
            var response = Response(v); var evidence = FinTsDialogueEndEvidence.Evaluate(request, response);
            var expected = v.GetProperty("issues").EnumerateArray().Aggregate(FinTsDialogueEndIssue.None, (flags, value) => flags | Enum.Parse<FinTsDialogueEndIssue>(value.GetString()!));
            Verify(evidence.Issues == expected && evidence.Outcome.ToString() == v.GetProperty("outcome").GetString(), $"Independent close issues/outcome match {v.GetProperty("name").GetString()}: {evidence.Issues}.");
            Verify(evidence.CloseReported == v.GetProperty("closeReported").GetBoolean() && evidence.AbortReported == v.GetProperty("abortReported").GetBoolean(), "Close and abort code observations remain separate even on review outcomes.");
            Verify(request.Request.DialogueId == dialogue && request.Frame.MessageNumber == v.GetProperty("clientMessageNumber").GetInt32() && request.ExpectedBankMessageNumber == v.GetProperty("expectedBankMessageNumber").GetInt32(), "Exact dialogue text and independent counters survive.");
            Verify(ReferenceEquals(request.Frame, frame) && ReferenceEquals(request.Request.Source, frame.Syntax.Segments[1]) && ReferenceEquals(evidence.Request, request) && ReferenceEquals(evidence.Response, response), "Exact caller-owned source objects are retained.");
            wire[0] = 0; Verify(request.Frame.Syntax.CopyWireBytes()[0] == 'H', "Caller wire mutations cannot change parsed source evidence.");
        }
        Verify(vectors.Length == 10, "All ten independent dialogue-end fixtures ran.");
        var sample = vectors[0]; var requestBase = Request(sample); var responseBase = Response(sample);
        FinTsDialogueEndEvidence Evaluate(Func<string, string>? edit = null) => FinTsDialogueEndEvidence.Evaluate(requestBase, Response(sample, edit));
        foreach (string segment in new[] { "HKEND:2:1'", "HKEND:2:1+'", "HKEND:2:1+S+'", "HKEND:2:1+S:T'", "HKEND:2:1+@1@S'", "HKEND:2:1:1+S'", "HIEND:2:1+S'" })
        { Reject(() => FinTsDialogueEndRequest.Parse(Segment(segment))); }
        Reject(() => FinTsDialogueEndRequest.Parse(Segment("HKEND:2:2+S'")), FinTsSyntaxError.UnsupportedDialogueEndVersion);
        foreach (string dialogue in new[] { "", "0", "unbekannt", " padded", "padded ", "PUBLIC-SECRET\n", "\0", "€", "\ud800", new string('x', 31) })
        { Reject(() => FinTsUnsignedDialogueEndWriter.Encode(dialogue, 2)); }
        foreach (int number in new[] { -1, 0, 1, 10000, int.MaxValue })
        {
            Reject(() => FinTsUnsignedDialogueEndWriter.Encode("SYNTHETIC", number));
            Reject(() => FinTsUnsignedDialogueEndRequest.Parse(requestBase.Frame, number));
        }
        string requestWire = Encoding.Latin1.GetString(requestBase.Frame.Syntax.CopyWireBytes());
        foreach (string changed in new[]
        {
            requestWire.Replace("HKEND:2:1+SYNTHETIC", "HKEND:2:1+OTHER", StringComparison.Ordinal),
            requestWire.Replace("+300+SYNTHETIC+2'", "+300+OTHER+2'", StringComparison.Ordinal),
            requestWire.Replace("+300+SYNTHETIC+2'", "+300+SYNTHETIC+2+SYNTHETIC:1'", StringComparison.Ordinal),
            requestWire.Replace("HNHBS:3:1", "HKSPA:3:1'HNHBS:4:1", StringComparison.Ordinal),
            requestWire.Replace("+2'", "+1'", StringComparison.Ordinal),
        }) { Reject(() => FinTsUnsignedDialogueEndRequest.Parse(Frame(changed), 2)); }
        Reject(() => FinTsUnsignedDialogueEndRequest.Parse(Frame(requestWire.Replace("HKEND:2:1+SYNTHETIC", "HNSHK:2:4+PUBLIC", StringComparison.Ordinal)), 2), FinTsSyntaxError.UnsupportedSecurityWrapper);
        Verify(FinTsUnsignedDialogueEndRequest.Parse(Frame(requestWire.Replace("+300+SYNTHETIC+2'", "+300+SYNTHETIC+2+'", StringComparison.Ordinal)), 2).Request.DialogueId == "SYNTHETIC", "Empty optional outer reference remains an omission.");
        Reject(() => FinTsReadRequestContext.Parse(requestBase.Frame, 2), FinTsSyntaxError.InvalidReadData);
        Reject(() => FinTsUnsignedInitializationRequest.Parse(requestBase.Frame), FinTsSyntaxError.InvalidInitialization);
        Reject(() => FinTsUnsignedSynchronizationRequest.Parse(requestBase.Frame), FinTsSyntaxError.InvalidSynchronization);
        foreach (Func<string, string> edit in new Func<string, string>[]
        {
            s => s.Replace("SYNTHETIC", "OTHER", StringComparison.Ordinal),
            s => s.Replace("+2+SYNTHETIC:2'", "+2'", StringComparison.Ordinal),
            s => s.Replace("+2+SYNTHETIC:2'", "+2+'", StringComparison.Ordinal),
            s => s.Replace("SYNTHETIC:2'", "SYNTHETIC:3'", StringComparison.Ordinal),
            s => s.Replace("+300+SYNTHETIC+2+", "+300+SYNTHETIC+3+", StringComparison.Ordinal).Replace("HNHBS:3:1+2'", "HNHBS:3:1+3'", StringComparison.Ordinal),
        }) { Has(Evaluate(edit), FinTsDialogueEndIssue.MessageMismatch); }
        var scoped = vectors[1];
        Has(FinTsDialogueEndEvidence.Evaluate(Request(scoped), Response(scoped, s => s.Replace("HIRMS:3:2:2", "HIRMS:3:2:1", StringComparison.Ordinal))), FinTsDialogueEndIssue.ReferenceMismatch);
        foreach (string status in new[] { "0010", "0020", "0030", "3040", "9050", "7777" })
        {
            var result = Evaluate(s => s.Replace("0100::PUBLIC-REPLY", status + "::PUBLIC-REPLY", StringComparison.Ordinal));
            Has(result, FinTsDialogueEndIssue.MissingTermination);
            Verify(!result.CloseReported && !result.AbortReported, "Receipt, execution, pending or error status never invents termination.");
        }
        foreach (string status in new[] { "0100::PUBLIC:PUBLIC-SECRET", "0100::PUBLIC+0100::PUBLIC", "9800::PUBLIC+9800::PUBLIC" })
        { Has(Evaluate(s => s.Replace("0100::PUBLIC-REPLY", status, StringComparison.Ordinal)), FinTsDialogueEndIssue.StatusNeedsReview); }
        Has(FinTsDialogueEndEvidence.Evaluate(Request(scoped), Response(scoped, s => s.Replace("0100::PUBLIC-CLOSE-REPLY", "0100:1:PUBLIC-CLOSE-REPLY", StringComparison.Ordinal))), FinTsDialogueEndIssue.StatusNeedsReview);
        Has(FinTsDialogueEndEvidence.Evaluate(Request(scoped), Response(scoped, s => s.Replace("0100::PUBLIC-CLOSE-REPLY", "9800::PUBLIC-CLOSE-REPLY", StringComparison.Ordinal))), FinTsDialogueEndIssue.StatusNeedsReview);
        Verify(Evaluate(s => s.Replace("0100::PUBLIC-REPLY", "0100::PUBLIC-REPLY:", StringComparison.Ordinal)).HasMatchingEvidence, "Empty trailing status parameters remain omissions.");
        var abort = FinTsDialogueEndEvidence.Evaluate(Request(vectors[4]), Response(vectors[4]));
        Verify(abort.HasMatchingEvidence && abort.Outcome == FinTsDialogueEndOutcome.AbortReported && abort.Response.HasErrors && abort.Response.HasUninterpretedCodes, "Bound 9800 remains an abort, with generic reply classification unchanged.");
        Verify(Evaluate().Response.HasUninterpretedCodes, "Scoped 0100 interpretation does not broaden generic reply meanings.");
        Has(FinTsDialogueEndEvidence.Evaluate(requestBase, Wrapped(responseBase)), FinTsDialogueEndIssue.EnvelopeNeedsReview);
        Has(Evaluate(s => s.Replace("HNHBS:3:1", "HIEND:3:1:2+SYNTHETIC'HNHBS:4:1", StringComparison.Ordinal)), FinTsDialogueEndIssue.UnexpectedData);
        Has(Evaluate(s => s.Replace("HNHBS:3:1", "HIBPA:3:3:2+1+280:PUBLIC-BANK+PUBLIC+1+1+300'HNHBS:4:1", StringComparison.Ordinal)), FinTsDialogueEndIssue.UnexpectedData);
        var culture = CultureInfo.CurrentCulture; byte[] canonical = FinTsUnsignedDialogueEndWriter.Encode("SYNTHETIC", 1234);
        try
        {
            foreach (string name in new[] { "de-DE", "ar-SA", "tr-TR" })
            { CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo(name); Verify(FinTsUnsignedDialogueEndWriter.Encode("SYNTHETIC", 1234).SequenceEqual(canonical), "Close encoding is culture invariant."); }
        }
        finally { CultureInfo.CurrentCulture = culture; }
        Verify(Evaluate().HasMatchingEvidence && Evaluate().HasMatchingEvidence, "Pure closing comparisons consume no response or replay state.");
        Verify(new object[] { requestBase, requestBase.Request, Evaluate() }.All(o => !o.ToString()!.Contains("SYNTHETIC", StringComparison.Ordinal)), "Default diagnostics omit dialogue identifiers.");
        using var cancelled = new CancellationTokenSource(); cancelled.Cancel();
        foreach (Action action in new Action[]
        {
            () => FinTsDialogueEndRequest.Parse(requestBase.Request.Source, cancelled.Token),
            () => FinTsUnsignedDialogueEndRequest.Parse(requestBase.Frame, 2, cancelled.Token),
            () => FinTsUnsignedDialogueEndWriter.Encode("SYNTHETIC", 2, cancelled.Token),
            () => FinTsDialogueEndEvidence.Evaluate(requestBase, responseBase, cancelled.Token),
        })
        {
            try { action(); Verify(false, "Cancelled work must return no output."); }
            catch (OperationCanceledException) { Verify(true, "Cancellation propagates."); }
        }
        foreach (Action action in new Action[]
        {
            () => FinTsDialogueEndRequest.Parse(null!), () => FinTsUnsignedDialogueEndRequest.Parse(null!, 2),
            () => FinTsUnsignedDialogueEndWriter.Encode(null!, 2), () => FinTsDialogueEndEvidence.Evaluate(null!, responseBase),
            () => FinTsDialogueEndEvidence.Evaluate(requestBase, null!),
        })
        {
            try { action(); Verify(false, "Null input must fail."); }
            catch (ArgumentNullException) { Verify(true, "Null inputs rejected."); }
        }
        Console.WriteLine($"FinTS dialogue-end schema/encoding verification passed ({vectors.Length} independent vectors, {count} checks).");
    }
    private static FinTsUnsignedDialogueEndRequest Request(JsonElement v) => FinTsUnsignedDialogueEndRequest.Parse(FinTsMessageFrame.Parse(Convert.FromBase64String(v.GetProperty("requestBase64").GetString()!)), v.GetProperty("expectedBankMessageNumber").GetInt32());
    private static FinTsResponse Response(JsonElement v, Func<string, string>? edit = null)
    {
        string wire = Encoding.Latin1.GetString(Convert.FromBase64String(v.GetProperty("responseBase64").GetString()!));
        return FinTsResponse.Parse(Frame(edit is null ? wire : edit(wire)));
    }
    private static FinTsSegment Segment(string text) => FinTsSyntax.ParseSegments(Encoding.Latin1.GetBytes(text)).Segments[0];
    private static FinTsMessageFrame Frame(string wire) => FinTsMessageFrame.Parse(Encoding.Latin1.GetBytes(wire[..10] + Encoding.Latin1.GetByteCount(wire).ToString("D12", CultureInfo.InvariantCulture) + wire[22..]));
    private static FinTsResponse Wrapped(FinTsResponse source)
    {
        string plain = Encoding.Latin1.GetString(source.Frame.Syntax.CopyWireBytes());
        int headerEnd = plain.IndexOf('\'') + 1, trailerStart = plain.LastIndexOf("HNHBS:", StringComparison.Ordinal);
        string body = plain[headerEnd..trailerStart];
        string wrapper = "HNVSK:998:3+PIN:2+998+1+1::PUBLIC-SYSTEM+1:20260908:120000+2:2:13:@8@\0\0\0\0\0\0\0\0:5:1+280:10020030:PUBLIC-KEY-ID:V:0:0+0'";
        string wire = plain[..headerEnd] + wrapper + "HNVSD:999:1+@" + Encoding.Latin1.GetByteCount(body).ToString(CultureInfo.InvariantCulture) + "@" + body + "'" + plain[trailerStart..];
        return FinTsResponse.ParsePinTan(FinTsPinTanEnvelope.Parse(Frame(wire)));
    }
}
