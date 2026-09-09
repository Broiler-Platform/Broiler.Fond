using System.Globalization;
using System.Reflection;
using System.Text;
using System.Text.Json;
using Broiler.Fond.Kernel.Diagnostics;
using Broiler.Fond.Kernel.FinTs;

namespace Broiler.Fond.Kernel.Tests;

internal static class FinTsResponseTests
{
    private const string Sentinel = "PUBLIC-SECRET-REPLY";

    internal static void Run(Action<bool, string> check)
    {
        int count = 0;
        void Verify(bool condition, string message) { count++; check(condition, message); }
        void Reject(string body, FinTsSyntaxError? expected = null)
        {
            try { _ = Reply(body); Verify(false, "Malformed response schema must be rejected."); }
            catch (FinTsFormatException error)
            {
                Verify(expected is null || error.Error == expected, "Response must report the expected fixed failure category.");
                Verify(!error.ToString().Contains(Sentinel, StringComparison.Ordinal), "Response errors must not echo raw bank text.");
            }
        }

        using Stream vectorStream = Assembly.GetExecutingAssembly().GetManifestResourceStream("FinTs.responses-v1.json")!;
        using JsonDocument corpus = JsonDocument.Parse(vectorStream);
        int vectors = 0;
        foreach (JsonElement vector in corpus.RootElement.GetProperty("vectors").EnumerateArray())
        {
            vectors++;
            byte[] wire = Convert.FromBase64String(vector.GetProperty("wireBase64").GetString()!);
            FinTsResponse response = FinTsResponse.Parse(FinTsMessageFrame.Parse(wire));
            Verify(response.Frame.Syntax.CopyWireBytes().SequenceEqual(wire), "Independent response vector must retain exact wire evidence.");
            Verify(response.HasConflictingClasses == vector.GetProperty("conflicting").GetBoolean(), "Independent fixture must expose contradictory status.");
            Verify(response.ReplySegments.SelectMany(s => s.Replies).Select(r => r.Meaning.ToString()).SequenceEqual(
                vector.GetProperty("meanings").EnumerateArray().Select(v => v.GetString()!)), "Typed meanings must match independently selected fixture expectations.");
            var expectedSegments = vector.GetProperty("segments").EnumerateArray().ToArray();
            foreach (FinTsReplySegment segment in response.ReplySegments)
            {
                JsonElement expected = expectedSegments.Single(s => s.GetProperty("number").GetInt32() == segment.Source.Number);
                int? reference = expected.GetProperty("reference").ValueKind == JsonValueKind.Null ? null : expected.GetProperty("reference").GetInt32();
                Verify(segment.Source.Code == expected.GetProperty("code").GetString() && segment.RequestSegmentNumber == reference,
                    "Independent response scope and request reference must match.");
                JsonElement fields = expected.GetProperty("fields");
                Verify(segment.Replies.Count == fields.GetArrayLength(), "Independent response count must match.");
                for (int index = 0; index < segment.Replies.Count; index++)
                {
                    var elements = segment.Replies[index].Source.Elements;
                    Verify(elements.Count == fields[index].GetArrayLength(), "Independent response component count must match.");
                    for (int component = 0; component < elements.Count; component++)
                    {
                        Verify(elements[component].CopyValueBytes().SequenceEqual(Convert.FromHexString(fields[index][component].GetProperty("hex").GetString()!)),
                            "Independent raw reply text, code, references and parameters must survive unchanged.");
                    }
                }
            }
            Verify(response.UninterpretedSegments.Count == expectedSegments.Count(s => s.GetProperty("code").GetString() is not ("HIRMG" or "HIRMS")),
                "Unknown body segments must stay visible in typed fixture results.");
        }
        Verify(vectors == 5, "All five independent typed response vectors must execute.");

        FinTsResponse receipt = Reply("HIRMG:2:2+0010::" + Sentinel + "'HIRMS:3:2:2+3040:3,4:More?: data:opaque?+cursor::last'");
        FinTsReply messageReply = receipt.ReplySegments[0].Replies.Single();
        FinTsReply segmentReply = receipt.ReplySegments[1].Replies.Single();
        Verify(messageReply.Code == "0010" && messageReply.Class == FinTsReplyClass.Success && messageReply.Meaning == FinTsReplyMeaning.ReceiptReported,
            "0010 must mean receipt reported, not execution completed.");
        Verify(segmentReply.Meaning == FinTsReplyMeaning.MoreInformationAvailable && receipt.ReplySegments[1].RequestSegmentNumber == 2,
            "Pagination evidence must preserve its request scope without starting a new request.");
        Verify(Ascii(segmentReply.ElementReference) == "3,4" && Ascii(segmentReply.Text) == "More: data" &&
            segmentReply.Parameters.Count == 3 && Ascii(segmentReply.Parameters[0]) == "opaque+cursor" && segmentReply.Parameters[1].IsEmpty,
            "Reply field references, escaped text and positional parameters must survive parsing.");
        Verify(!receipt.HasErrors && !receipt.HasConflictingClasses && !receipt.HasUninterpretedCodes && receipt.UninterpretedSegments.Count == 0,
            "Known nonconflicting replies must retain their evidence flags.");
        foreach ((string code, FinTsReplyClass kind, FinTsReplyMeaning meaning) in new[]
        {
            ("0020", FinTsReplyClass.Success, FinTsReplyMeaning.ExecutionReported),
            ("0030", FinTsReplyClass.Success, FinTsReplyMeaning.AuthorizationPending),
            ("9000", FinTsReplyClass.Error, FinTsReplyMeaning.ProcessingIndeterminate),
            ("0957", FinTsReplyClass.Success, FinTsReplyMeaning.Uninterpreted),
            ("3998", FinTsReplyClass.Warning, FinTsReplyMeaning.Uninterpreted),
            ("9010", FinTsReplyClass.Error, FinTsReplyMeaning.Uninterpreted),
            ("7001", FinTsReplyClass.Unknown, FinTsReplyMeaning.Uninterpreted),
        })
        {
            FinTsResponse response = Reply("HIRMG:2:2+" + code + "::" + Sentinel + "'");
            FinTsReply reply = response.ReplySegments.Single().Replies.Single();
            Verify(reply.Code == code && reply.Class == kind && reply.Meaning == meaning &&
                response.HasIndeterminateProcessing == (code == "9000") && response.HasUninterpretedCodes == (meaning == FinTsReplyMeaning.Uninterpreted),
                "Code classification must preserve unknown, pending and indeterminate outcomes without guessing success.");
        }
        Verify(Reply("HIRMG:2:2+3998::note'").ReplySegments[0].Replies[0].Class == FinTsReplyClass.Warning, "Warning-only responses remain warnings.");
        FinTsResponse unknownBody = Reply("HIRMG:2:2+0010::received'ZNEW:3:42:2+PUBLIC-SECRET-REPLY'");
        Verify(unknownBody.UninterpretedSegments.Count == 1 && unknownBody.UninterpretedSegments[0].Version == 42,
            "Unimplemented body segments must remain available and explicitly uninterpreted.");
        foreach (string body in new[]
        {
            "HIRMG:2:2+0010::ok+9000::uncertain'",
            "HIRMG:2:2+0010::ok+0020::done'",
            "HIRMG:2:2+0010::ok'HIRMS:3:2:2+9010::error'",
            "HIRMG:2:2+3998::note'HIRMS:3:2:2+0020::done+9010::error'",
        }) { Verify(Reply(body).HasConflictingClasses, "Contradictory replies must remain visible and cannot be accepted by correlation."); }

        foreach (string body in new[]
        {
            "ZNEW:2:1+data'", "HIRMG:2:2'", "HIRMG:2:2+0010::ok'HIRMG:3:2+0010::ok'",
            "HIRMG:2:2:2+0010::ok'", "HIRMG:2:2+0010:2:ok'", "HIRMG:2:2+0010::ok'HIRMS:3:2+0020::done'",
            "HIRMG:2:2+3998::note'HIRMS:3:2:2+0020::done'HIRMS:4:2:2+0020::done'",
            "HIRMG:2:2+001::" + Sentinel + "'", "HIRMG:2:2+00010::" + Sentinel + "'", "HIRMG:2:2+X010::" + Sentinel + "'",
            "HIRMG:2:2+0010:'", "HIRMG:2:2+0010::'", "HIRMG:2:2+@4@0010::ok'", "HIRMG:2:2+0010:@0@:ok'",
            "HIRMG:2:2+0010::@2@ok'", "HIRMG:2:2+0010::ok:@1@x'", "HIRMG:2:2+0010::bad\rtext'",
            "HIRMG:2:2+0010::bad\ntext'", "HIRMG:2:2+0010::bad\0text'", "HIRMG:2:2+0010::ok:bad\u0080text'",
            "HIRMG:2:2+0010::" + new string('x', 81) + "'",
            "HIRMG:2:2+0010::ok:" + new string('x', 36) + "'",
            "HIRMG:2:2+0010::ok'HIRMS:3:2:2+0020:12345678:done'",
            "HIRMG:2:2+0010::ok" + new string(':', 11) + "'",
            "HIRMG:2:2" + string.Concat(Enumerable.Repeat("+3998::note", 100)) + "'",
        }) { Reject(body); }
        Reject("HIRMG:2:3+0010::ok'", FinTsSyntaxError.UnsupportedResponseVersion);
        Reject("HIRMG:2:2+0010::ok'HIRMS:3:1:2+0020::done'", FinTsSyntaxError.UnsupportedResponseVersion);
        var limits = Reply("HIRMG:2:2+0010::" + new string('x', 80) + string.Concat(Enumerable.Repeat(":" + new string('p', 35), 10)) + "'");
        Verify(limits.ReplySegments[0].Replies[0].Parameters.Count == 10, "Exact reply text/parameter bounds must parse.");
        Verify(Reply("HIRMG:2:2" + string.Concat(Enumerable.Repeat("+3998::note", 99)) + "'").ReplySegments[0].Replies.Count == 99,
            "Exact per-segment reply count must parse.");
        foreach (int size in new[] { FinTsResponse.MaximumReplies, FinTsResponse.MaximumReplies + 1 })
        {
            StringBuilder body = new();
            int remaining = size;
            int segmentNumber = 2;
            while (remaining > 0)
            {
                body.Append(segmentNumber == 2 ? "HIRMG:2:2" : $"HIRMS:{segmentNumber}:2:{segmentNumber}");
                int batch = Math.Min(remaining, 99);
                for (int index = 0; index < batch; index++) { body.Append("+3998::note"); }
                body.Append('\'');
                remaining -= batch;
                segmentNumber++;
            }
            if (size == FinTsResponse.MaximumReplies)
            {
                Verify(Reply(body.ToString()).ReplySegments.Sum(s => s.Replies.Count) == size, "Exact global reply bound must parse.");
            }
            else { Reject(body.ToString(), FinTsSyntaxError.LimitExceeded); }
        }
        FinTsMessageFrame wrapper = Frame("HNHBK:1:3+{size}+300+D+1+D:1'HNVSK:998:3+opaque'HNVSD:999:1+@0@'HNHBS:4:1+1'");
        try { _ = FinTsResponse.Parse(wrapper); Verify(false, "Wrapped responses must await security-profile processing."); }
        catch (FinTsFormatException error) { Verify(error.Error == FinTsSyntaxError.UnsupportedSecurityWrapper, "Wrapped response must return explicit unsupported state."); }
        try { _ = FinTsResponse.Parse(receipt.Frame, new CancellationToken(true)); Verify(false, "Pre-cancelled schema parse must stop."); }
        catch (OperationCanceledException) { Verify(true, "Schema cancellation is supported."); }

        FinTsMessageFrame initialRequest = Request("0", 1);
        FinTsDialogueCorrelation dialogue = new();
        Verify(dialogue.AcceptResponse(receipt) == FinTsCorrelationStatus.NoPendingRequest, "Unsolicited response must not establish a dialogue.");
        Verify(dialogue.BeginRequest(Request("unexpected", 1)) == FinTsCorrelationStatus.RequestDialogMismatch, "New dialogue must require the initial zero ID.");
        Verify(dialogue.BeginRequest(Request("0", 2)) == FinTsCorrelationStatus.RequestMessageMismatch, "New dialogue must require first client message number.");
        Verify(dialogue.BeginRequest(Frame("HNHBK:1:3+{size}+300+0+1+D:1'ZREQ:2:1+read'HNHBS:3:1+1'")) == FinTsCorrelationStatus.UnexpectedRequestReference,
            "A request cannot masquerade as a bank response.");
        Verify(dialogue.BeginRequest(initialRequest) == FinTsCorrelationStatus.RequestRecorded && dialogue.State == FinTsCorrelationState.AwaitingResponse,
            "First request must create one pending correlation expectation.");
        Verify(dialogue.BeginRequest(initialRequest) == FinTsCorrelationStatus.RequestAlreadyPending, "Concurrent second request must be refused.");

        (FinTsResponse Response, FinTsCorrelationStatus Expected)[] rejected =
        [
            (Reply("HIRMG:2:2+0010::ok'", referenceDialog: null), FinTsCorrelationStatus.MissingMessageReference),
            (Reply("HIRMG:2:2+0010::ok'", bankNumber: 2), FinTsCorrelationStatus.BankMessageMismatch),
            (Reply("HIRMG:2:2+0010::ok'", requestNumber: 2), FinTsCorrelationStatus.RequestReferenceMismatch),
            (Reply("HIRMG:2:2+0010::ok'", referenceDialog: "OTHER"), FinTsCorrelationStatus.DialogMismatch),
            (Reply("HIRMG:2:2+0010::ok'", dialog: "0", referenceDialog: "0"), FinTsCorrelationStatus.InvalidAssignedDialog),
            (Reply("HIRMG:2:2+9010::error'", dialog: "unbekannt", referenceDialog: "unbekannt"), FinTsCorrelationStatus.InvalidAssignedDialog),
            (Reply("HIRMG:2:2+0010::ok'HIRMS:3:2:7+0020::done'"), FinTsCorrelationStatus.UnknownSegmentReference),
            (Reply("HIRMG:2:2+0010::ok'ZNEW:3:1:7+data'"), FinTsCorrelationStatus.UnknownSegmentReference),
            (Reply("HIRMG:2:2+0010::ok'HIRMS:3:2:2+9010::error'"), FinTsCorrelationStatus.ConflictingResponse),
        ];
        foreach (var candidate in rejected)
        {
            Verify(dialogue.AcceptResponse(candidate.Response) == candidate.Expected && dialogue.State == FinTsCorrelationState.AwaitingResponse,
                "Rejected candidate must preserve the pending request and counters.");
        }
        Verify(dialogue.AcceptResponse(receipt) == FinTsCorrelationStatus.Matched && dialogue.State == FinTsCorrelationState.Ready,
            "Valid initial response must atomically establish the assigned dialogue.");
        Verify(dialogue.AcceptResponse(receipt) == FinTsCorrelationStatus.NoPendingRequest, "Matched response must not be consumed twice.");
        Verify(dialogue.BeginRequest(Request("0", 2)) == FinTsCorrelationStatus.RequestDialogMismatch, "Established dialogue cannot revert to initial ID.");
        Verify(dialogue.BeginRequest(Request("D", 1)) == FinTsCorrelationStatus.RequestMessageMismatch, "Client request replay must fail.");
        Verify(dialogue.BeginRequest(Request("D", 2)) == FinTsCorrelationStatus.RequestRecorded, "Next client number must be independently validated.");
        Verify(dialogue.AcceptResponse(receipt) == FinTsCorrelationStatus.BankMessageMismatch, "Old response cannot satisfy the next pending request.");
        Verify(dialogue.AcceptResponse(Reply("HIRMG:2:2+0010::ok'", dialog: "d", referenceDialog: "d", bankNumber: 2, requestNumber: 2)) == FinTsCorrelationStatus.DialogMismatch,
            "Dialogue identifiers must be exact byte comparisons, including case.");
        FinTsResponse second = Reply("HIRMG:2:2+9000::" + Sentinel + "'", bankNumber: 2, requestNumber: 2);
        Verify(dialogue.AcceptResponse(second) == FinTsCorrelationStatus.Matched && second.HasIndeterminateProcessing,
            "Mechanical correlation of 9000 must preserve indeterminate processing, never imply successful execution.");
        dialogue.Stop();
        Verify(dialogue.State == FinTsCorrelationState.Stopped && dialogue.BeginRequest(Request("D", 3)) == FinTsCorrelationStatus.Stopped &&
            dialogue.AcceptResponse(second) == FinTsCorrelationStatus.Stopped, "Stopped tracker must be terminal without implicit retry.");

        FinTsDialogueCorrelation concurrent = new();
        FinTsCorrelationStatus[] beginResults = new FinTsCorrelationStatus[16];
        Parallel.For(0, beginResults.Length, index => beginResults[index] = concurrent.BeginRequest(initialRequest));
        Verify(beginResults.Count(r => r == FinTsCorrelationStatus.RequestRecorded) == 1 && beginResults.Count(r => r == FinTsCorrelationStatus.RequestAlreadyPending) == 15,
            "Concurrent callers may publish only one request.");
        FinTsCorrelationStatus[] responseResults = new FinTsCorrelationStatus[16];
        Parallel.For(0, responseResults.Length, index => responseResults[index] = concurrent.AcceptResponse(receipt));
        Verify(responseResults.Count(r => r == FinTsCorrelationStatus.Matched) == 1 && responseResults.Count(r => r == FinTsCorrelationStatus.NoPendingRequest) == 15,
            "Concurrent response handling may advance counters only once.");
        FinTsDialogueCorrelation cancelled = new();
        _ = cancelled.BeginRequest(initialRequest);
        cancelled.Stop();
        Verify(cancelled.AcceptResponse(receipt) == FinTsCorrelationStatus.Stopped, "Late response after cancellation must not revive a dialogue.");
        Verify(new FinTsDialogueCorrelation().BeginRequest(wrapper) == FinTsCorrelationStatus.UnsupportedSecurityWrapper, "Opaque request wrapper cannot supply inner correlation expectations.");

        // Exercise the actual finite wire counters rather than editing private state for the boundary.
        FinTsDialogueCorrelation exhausted = new();
        bool sequenceValid = true;
        for (int number = 1; number <= 9999; number++)
        {
            sequenceValid &= exhausted.BeginRequest(Request(number == 1 ? "0" : "D", number)) == FinTsCorrelationStatus.RequestRecorded;
            sequenceValid &= exhausted.AcceptResponse(Reply("HIRMG:2:2+0010::ok'", bankNumber: number, requestNumber: number)) == FinTsCorrelationStatus.Matched;
        }
        Verify(sequenceValid && exhausted.State == FinTsCorrelationState.Stopped && exhausted.BeginRequest(initialRequest) == FinTsCorrelationStatus.Stopped,
            "Counters must reach 9999 exactly and stop without overflow, wrap or reinitialization.");

        byte[] copiedText = messageReply.Text.CopyValueBytes();
        Array.Clear(copiedText);
        Verify(Ascii(messageReply.Text) == Sentinel, "Reply evidence must resist caller mutations.");
        object[] defaultOutput = [receipt, receipt.ReplySegments[0], messageReply, segmentReply, dialogue];
        Verify(defaultOutput.All(value => !value.ToString()!.Contains(Sentinel, StringComparison.Ordinal)), "Default typed response/correlation diagnostics must be secret-safe.");
        LocalDiagnosticBuffer diagnostics = new(enabled: true);
        diagnostics.Record(LocalDiagnosticCode.ResponseRejected, LocalDiagnosticOutcome.MalformedResponse, DateTimeOffset.UnixEpoch);
        Verify(!diagnostics.CreateSupportPreview(true).Json.Contains(Sentinel, StringComparison.Ordinal), "Diagnostic mapping must take only predefined outcomes.");
        CultureInfo previous = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("ar-SA");
            Verify(Reply("HIRMG:2:2+0010::ok'").ReplySegments[0].Replies[0].Code == "0010", "Reply codes must preserve leading zeros across cultures.");
        }
        finally { CultureInfo.CurrentCulture = previous; }
        Console.WriteLine($"FinTS response schemas and dialogue correlation: {vectors} independent vectors and {count} checks completed.");
    }

    private static string Ascii(FinTsDataElement element) => Encoding.Latin1.GetString(element.CopyValueBytes());
    private static FinTsMessageFrame Request(string dialog, int number) => Frame(
        $"HNHBK:1:3+{{size}}+300+{dialog}+{number.ToString(CultureInfo.InvariantCulture)}'ZREQ:2:1+read'HNHBS:3:1+{number.ToString(CultureInfo.InvariantCulture)}'");

    private static FinTsResponse Reply(string body, string dialog = "D", string? referenceDialog = "D", int bankNumber = 1, int requestNumber = 1)
    {
        // Test body has no binary values containing apostrophes; malformed binary cases are rejected by schema.
        // Count actual parsed segments so escaped apostrophes remain safe in future fixtures.
        int segments = FinTsSyntax.ParseSegments(Encoding.Latin1.GetBytes(body)).Segments.Count;
        string reference = referenceDialog is null ? "" : "+" + referenceDialog + ":" + requestNumber.ToString(CultureInfo.InvariantCulture);
        return FinTsResponse.Parse(Frame($"HNHBK:1:3+{{size}}+300+{dialog}+{bankNumber.ToString(CultureInfo.InvariantCulture)}{reference}'" + body +
            $"HNHBS:{segments + 2}:1+{bankNumber.ToString(CultureInfo.InvariantCulture)}'"));
    }

    private static FinTsMessageFrame Frame(string template)
    {
        string provisional = template.Replace("{size}", "000000000000", StringComparison.Ordinal);
        return FinTsMessageFrame.Parse(Encoding.Latin1.GetBytes(template.Replace("{size}", Encoding.Latin1.GetByteCount(provisional).ToString("D12", CultureInfo.InvariantCulture), StringComparison.Ordinal)));
    }
}
