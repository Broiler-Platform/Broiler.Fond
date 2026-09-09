using System.Globalization;
using System.Reflection;
using System.Text;
using System.Text.Json;
using Broiler.Fond.Kernel.FinTs;

namespace Broiler.Fond.Kernel.Tests;

internal static class FinTsReadAvailabilityTests
{
    internal static void Run(Action<bool, string> check)
    {
        int count = 0;
        void Verify(bool value, string message) { count++; check(value, message); }
        using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream("FinTs.read-availability-v1.json")!;
        using var document = JsonDocument.Parse(stream);
        var vectors = document.RootElement.GetProperty("vectors").EnumerateArray().ToArray();
        foreach (var vector in vectors)
        {
            var request = Request(vector);
            var capability = Capability(vector, request);
            var data = Data(vector);
            var evidence = FinTsReadContextEvidence.Evaluate(request, data, capability);
            var issues = vector.GetProperty("issues").EnumerateArray().Aggregate(FinTsReadContextIssue.None, (sum, item) => sum | Enum.Parse<FinTsReadContextIssue>(item.GetString()!));
            Verify(evidence.Issues == issues && evidence.Outcome.ToString() == vector.GetProperty("outcome").GetString(), $"Independent availability expectation differs for {vector.GetProperty("name").GetString()}: {evidence.Issues} / {evidence.Outcome}.");
            Verify(data.Discovery.Count == vector.GetProperty("discoveryReports").GetInt32() && data.Balances.Count == vector.GetProperty("balanceReports").GetInt32(), "Empty, omitted and populated source reports remain distinct.");
            Verify(ReferenceEquals(evidence.Response, data) && ReferenceEquals(evidence.Request, request), "Availability observations retain exact source/request provenance.");
            var attempt = new FinTsReadRefreshAttempt(TimeSpan.FromMinutes(1));
            Verify(attempt.Start(request, capability) == FinTsReadRefreshTransition.RequestRecorded, "Availability fixtures start with matching request context.");
            var result = attempt.AcceptResponse(data);
            Verify(result == (issues == FinTsReadContextIssue.None ? FinTsReadRefreshTransition.UnavailableReported : FinTsReadRefreshTransition.RejectedForReview) && attempt.GetSnapshot().PagesAccepted == (issues == FinTsReadContextIssue.None ? 1 : 0), "Unavailable outcomes terminate separately from review failures.");
            Verify(attempt.ContinuationEvidence is null && Released(attempt) && attempt.AcceptResponse(data) == FinTsReadRefreshTransition.Terminal, "Terminal availability paths release raw references and cannot consume the response twice.");
        }
        Verify(vectors.Length == 8, "All eight independent availability fixtures ran.");
        var sample = vectors[2]; var initial = Request(sample); var cap = Capability(sample, initial); var unavailable = Data(sample);
        FinTsReadContextEvidence Evaluate(FinTsReadDataSet data) => FinTsReadContextEvidence.Evaluate(initial, data, cap);
        var zero = Data(vectors[3], s => s.Replace("3010::Synthetic", "0020::Synthetic", StringComparison.Ordinal));
        Verify(Evaluate(zero).Outcome == FinTsReadOutcomeObservation.ExecutionReported && zero.Balances.Single().Booked.SignedValue == 0m, "A reported zero balance remains a real amount, distinct from an unavailable response.");
        Verify(unavailable.Source.HasUninterpretedCodes && unavailable.Source.ReplySegments.SelectMany(s => s.Replies).Single(r => r.Code == "3010").Meaning == FinTsReplyMeaning.Uninterpreted, "Generic 3010 decoding remains uninterpreted outside the scoped read comparator.");
        foreach (string text in new[] { "Account closed", "No entries", "Invalid account", "Zero balance", "PUBLIC <b>untrusted</b>" })
        {
            var evidence = Evaluate(Data(sample, s => s.Replace("3010::Synthetic", "3010::" + text, StringComparison.Ordinal)));
            Verify(evidence.Outcome == FinTsReadOutcomeObservation.UnavailableReported && evidence.Response.Balances.Count == 0, "Free-form bank text cannot imply closure, emptiness, zero or another typed outcome.");
        }
        foreach (string status in new[] { "0020::Synthetic+3010::Synthetic", "3010::Synthetic:", "3010::Synthetic::" })
        { Verify(Evaluate(Data(sample, s => s.Replace("3010::Synthetic", status, StringComparison.Ordinal))).Outcome == FinTsReadOutcomeObservation.UnavailableReported, "Compatible execution acknowledgement and empty optional parameters preserve unavailable evidence."); }
        foreach (string status in new[] { "3010:1:Synthetic", "3010::Synthetic:PUBLIC", "3010::Synthetic::PUBLIC", "3010::Synthetic+3010::Synthetic", "3010::Synthetic+0030::Synthetic", "3010::Synthetic+3998::Synthetic", "3010::Synthetic+9210::Synthetic", "0010::Synthetic", "9210::No balance because depot" })
        {
            var evidence = Evaluate(Data(sample, s => s.Replace("3010::Synthetic", status, StringComparison.Ordinal)));
            Verify(evidence.Outcome == FinTsReadOutcomeObservation.NeedsReview && evidence.Issues != FinTsReadContextIssue.None && evidence.Response.Balances.Count == 0, "Ambiguous, element-scoped, parameterized, pending and error replies cannot yield clean unavailable evidence.");
        }
        foreach (var data in new[]
        {
            Data(sample, s => s.Replace("HIRMS:3:2:2", "HIRMS:3:2:1", StringComparison.Ordinal)),
            Data(sample, s => s.Replace("0010::Synthetic", "3010::Synthetic", StringComparison.Ordinal).Replace("HIRMS:3:2:2+3010", "HIRMS:3:2:2+0020", StringComparison.Ordinal)),
            Data(sample, s => s.Replace("+SYNTHETIC:2", "+OTHER:2", StringComparison.Ordinal)),
        })
        { Verify(Evaluate(data).Outcome == FinTsReadOutcomeObservation.NeedsReview, "Wrong segment/message scope cannot declare the selected account unavailable."); }
        var wrongReference = new FinTsReadRefreshAttempt(TimeSpan.FromMinutes(1)); wrongReference.Start(initial, cap);
        Verify(wrongReference.AcceptResponse(Data(sample, s => s.Replace("HIRMS:3:2:2", "HIRMS:3:2:1", StringComparison.Ordinal))) == FinTsReadRefreshTransition.ContextMismatch && wrongReference.AcceptResponse(unavailable) == FinTsReadRefreshTransition.UnavailableReported, "Unrelated availability candidates leave the pending request available for its matching response.");
        var discovery = vectors[0]; var discoveryRequest = Request(discovery); var discoveryCap = Capability(discovery, discoveryRequest);
        foreach (string change in new[] { "HISPA:4:1:2+J:PUBLIC-IBAN:PUBLIC-BIC:PUBLIC-001:00:280:PUBLIC-BANK", "HISPA:4:2:2", "HISPA:4:1:1" })
        {
            var evidence = FinTsReadContextEvidence.Evaluate(discoveryRequest, Data(discovery, s => s.Replace("HISPA:4:1:2", change, StringComparison.Ordinal)), discoveryCap);
            Verify(!evidence.HasMatchingEvidence, "Unavailable discovery cannot coexist with data, unsupported versions or wrong references.");
        }
        var duplicateEmpty = Data(discovery, s => s.Replace("HNHBS:5:1+2'", "HISPA:5:1:2'HNHBS:6:1+2'", StringComparison.Ordinal));
        Verify(FinTsReadContextEvidence.Evaluate(discoveryRequest, duplicateEmpty, discoveryCap).Issues.HasFlag(FinTsReadContextIssue.DuplicateReport), "An unavailable status does not hide duplicate empty reports.");
        foreach (int version in new[] { 6, 7 })
        {
            var request = Request(sample, s => s.Replace("HKSAL:2:8", $"HKSAL:2:{version}", StringComparison.Ordinal).Replace("PUBLIC-IBAN:PUBLIC-BIC", version == 6 ? "PUBLIC-001:00:280:PUBLIC-BANK" : "PUBLIC-IBAN:PUBLIC-BIC", StringComparison.Ordinal));
            var capability = Capability(sample, request, s => s.Replace("HISALS:6:8+1+1+0+J", $"HISALS:6:{version}+1+1+0", StringComparison.Ordinal));
            Verify(FinTsReadContextEvidence.Evaluate(request, unavailable, capability).Outcome == FinTsReadOutcomeObservation.UnavailableReported, "Unavailable observations retain supported explicit request/capability version matching.");
        }
        using var refreshStream = Assembly.GetExecutingAssembly().GetManifestResourceStream("FinTs.read-refresh-v1.json")!;
        using var refreshDoc = JsonDocument.Parse(refreshStream);
        var trace = refreshDoc.RootElement.GetProperty("vectors")[1];
        var nextFrame = Frame(trace.GetProperty("requests")[1].GetString()!);
        var nextRequest = FinTsReadRequestContext.Parse(nextFrame, 3);
        var partial = FinTsReadDataSet.Parse(FinTsResponse.Parse(Frame(trace.GetProperty("responses")[0].GetString()!)));
        var prior = FinTsReadContextEvidence.Evaluate(initial, partial, cap);
        var nextUnavailable = Data(sample, s => s.Replace("+SYNTHETIC+2", "+SYNTHETIC+3", StringComparison.Ordinal).Replace("+SYNTHETIC:2", "+SYNTHETIC:3", StringComparison.Ordinal).Replace("HNHBS:4:1+2", "HNHBS:4:1+3", StringComparison.Ordinal));
        var nextEvidence = FinTsReadContextEvidence.Evaluate(nextRequest, nextUnavailable, cap, previousPage: prior);
        Verify(nextEvidence.Outcome == FinTsReadOutcomeObservation.UnavailableReported && nextEvidence.PageNumber == 2 && prior.Response.Balances.Count == 1 && nextEvidence.Response.Balances.Count == 0, "A later page's unavailable report does not erase or reinterpret the earlier data page.");
        Verify(FinTsReadContextEvidence.Evaluate(nextRequest, nextUnavailable, cap).Issues.HasFlag(FinTsReadContextIssue.ContinuationScopeMismatch), "Unavailable status cannot bypass continuation provenance.");
        var attemptWithPrior = new FinTsReadRefreshAttempt(TimeSpan.FromMinutes(1), maximumPages: 2);
        attemptWithPrior.Start(initial, cap); attemptWithPrior.AcceptResponse(partial); attemptWithPrior.RecordContinuation(nextRequest);
        Verify(attemptWithPrior.AcceptResponse(nextUnavailable) == FinTsReadRefreshTransition.UnavailableReported && attemptWithPrior.GetSnapshot().PagesAccepted == 2 && Released(attemptWithPrior), "Unavailable final page terminates at the page budget without aggregation or deletion.");
        var racing = new FinTsReadRefreshAttempt(TimeSpan.FromMinutes(1)); racing.Start(initial, cap);
        var results = new FinTsReadRefreshTransition[32];
        Parallel.For(0, results.Length, i => results[i] = racing.AcceptResponse(unavailable));
        Verify(results.Count(r => r == FinTsReadRefreshTransition.UnavailableReported) == 1 && results.Count(r => r == FinTsReadRefreshTransition.Terminal) == 31 && racing.GetSnapshot().PagesAccepted == 1, "Concurrent unavailable reports terminate once.");
        racing.Cancel(); racing.Stop();
        Verify(racing.GetSnapshot().State == FinTsReadRefreshState.UnavailableReported && racing.Start(initial, cap) == FinTsReadRefreshTransition.Terminal && Released(racing), "Terminal unavailable outcome cannot be restarted or replaced.");
        var clock = new Clock(); var timed = new FinTsReadRefreshAttempt(TimeSpan.FromSeconds(1), clock: clock); timed.Start(initial, cap); clock.Expired = true;
        Verify(timed.AcceptResponse(unavailable) == FinTsReadRefreshTransition.Terminal && timed.GetSnapshot().State == FinTsReadRefreshState.TimedOut && timed.GetSnapshot().PagesAccepted == 0, "Unavailable reports do not bypass expiry.");
        Verify(!nextEvidence.ToString()!.Contains("PUBLIC", StringComparison.Ordinal) && !racing.GetSnapshot().ToString()!.Contains("PUBLIC", StringComparison.Ordinal), "Availability diagnostics exclude raw source text and identifiers.");
        Console.WriteLine($"FinTS read-availability verification passed ({count} checks).");
    }
    private sealed class Clock : TimeProvider
    {
        internal bool Expired { get; set; }
        public override long TimestampFrequency => 1000;
        public override long GetTimestamp() => Expired ? 1000 : 0;
    }
    private static bool Released(FinTsReadRefreshAttempt attempt) => typeof(FinTsReadRefreshAttempt).GetFields(BindingFlags.Instance | BindingFlags.NonPublic)
        .Where(f => !f.IsInitOnly && !f.FieldType.IsValueType).All(f => f.GetValue(attempt) is null);
    private static FinTsReadRequestContext Request(JsonElement vector, Func<string, string>? transform = null) => FinTsReadRequestContext.Parse(Frame(vector.GetProperty("requestBase64").GetString()!, transform), 2);
    private static FinTsReadDataSet Data(JsonElement vector, Func<string, string>? transform = null) => FinTsReadDataSet.Parse(FinTsResponse.Parse(Frame(vector.GetProperty("responseBase64").GetString()!, transform)));
    private static FinTsReadCapabilityEvidence Capability(JsonElement vector, FinTsReadRequestContext request, Func<string, string>? transform = null)
    {
        var parameters = FinTsReadParameterSet.Parse(FinTsParameterSet.Parse(FinTsResponse.Parse(Frame(vector.GetProperty("parametersBase64").GetString()!, transform))));
        return FinTsReadCapabilityEvidence.Evaluate(parameters, parameters.Source.Accounts[0], request.Request.Source.Code == "HKSPA" ? FinTsReadOperation.SepaAccountDetails : FinTsReadOperation.Balance, request.Request.Source.Version);
    }
    private static FinTsMessageFrame Frame(string base64, Func<string, string>? transform = null)
    {
        string wire = Encoding.Latin1.GetString(Convert.FromBase64String(base64));
        if (transform is not null) { wire = transform(wire); }
        int start = wire.IndexOf('+') + 1;
        wire = wire[..start] + Encoding.Latin1.GetByteCount(wire).ToString("D12", CultureInfo.InvariantCulture) + wire[(start + 12)..];
        return FinTsMessageFrame.Parse(Encoding.Latin1.GetBytes(wire));
    }
}
