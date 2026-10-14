using System.Globalization;
using System.Reflection;
using System.Text.Json;
using Broiler.Fond.Kernel.FinTs;

namespace Broiler.Fond.Kernel.Tests;

internal static class FinTsPinTanAllAccountResponseTests
{
    internal static void Run(Action<bool, string> check)
    {
        int count = 0;
        void Verify(bool condition, string message) { count++; check(condition, message); }

        using var document = JsonDocument.Parse(Assembly.GetExecutingAssembly().GetManifestResourceStream("FinTs.pin-tan-all-account-response-v1.json")!);
        var vectors = document.RootElement.GetProperty("vectors").EnumerateArray().ToArray();

        foreach (var vector in vectors)
        {
            string name = vector.GetProperty("name").GetString()!;
            var request = Candidate(vector);
            var response = Response(vector);
            byte[] responseBefore = response.Frame.Syntax.CopyWireBytes();
            byte[] requestBefore = request.Context.Signature.Request.Frame.Syntax.CopyWireBytes();

            var binding = FinTsPinTanAllAccountResponseBinding.Evaluate(request, response);
            var expectedBindingIssues = Flags<FinTsPinTanResponseBindingIssue>(vector, "bindingIssues");
            Verify(binding.Issues == expectedBindingIssues && binding.HasMatchingReferences == (expectedBindingIssues == FinTsPinTanResponseBindingIssue.None),
                "Independent all-account binding flags: " + name + " (" + binding.Issues + ").");
            Verify(ReferenceEquals(binding.Request, request) && ReferenceEquals(binding.Response, response),
                "Binding preserves the exact candidate and original parsed response.");
            Verify(response.HasErrors == vector.GetProperty("hasErrors").GetBoolean(),
                "Mapped references retain original error observations, including correctly scoped aborts.");

            var links = vector.GetProperty("references").EnumerateArray().ToArray();
            Verify(binding.References.Count == links.Length, "Each non-message body segment retains a reference observation.");
            for (int i = 0; i < links.Length; i++)
            {
                var actual = binding.References[i];
                var link = links[i];
                int? reference = link.GetProperty("reference").ValueKind == JsonValueKind.Null ? null : link.GetProperty("reference").GetInt32();
                Verify(actual.ResponseSegment.Code == link.GetProperty("code").GetString() && actual.ResponseSegment.Reference == reference &&
                    actual.Target?.Role.ToString() == link.GetProperty("role").GetString(), "Response numbers resolve to independent assembled roles.");
                Verify(response.BodySegments.Any(s => ReferenceEquals(s, actual.ResponseSegment)) &&
                    (actual.Target is null || request.Segments.Any(s => ReferenceEquals(s, actual.Target))),
                    "Reference links preserve actual source and target objects without renumbering.");
            }

            Verify(responseBefore.SequenceEqual(response.Frame.Syntax.CopyWireBytes()) &&
                requestBefore.SequenceEqual(request.Context.Signature.Request.Frame.Syntax.CopyWireBytes()),
                "Reference comparison leaves both response and unsigned request bytes untouched.");
            Verify(FinTsPinTanAllAccountResponseBinding.Evaluate(request, response).Issues == binding.Issues,
                "Pure comparison consumes no response or replay state.");

            if (!vector.GetProperty("testSemantics").GetBoolean())
            {
                if (name == "opaque-payload-needs-schema-validation")
                {
                    try
                    {
                        FinTsReadDataSet.Parse(response);
                        Verify(false, "An opaque HISPA payload must fail business-schema parsing.");
                    }
                    catch (FinTsFormatException)
                    {
                        Verify(true, "Opaque HISPA payload fails business schema parsing.");
                    }
                }
                continue;
            }

            var data = FinTsReadDataSet.Parse(response);
            byte[] dataBefore = response.Frame.Syntax.CopyWireBytes();
            var evidence = FinTsPinTanAllAccountEvidence.Evaluate(binding, data);

            var expectedIssues = Flags<FinTsPinTanAllAccountIssue>(vector, "issues");
            var expectedResponseIssues = Flags<FinTsReadContextIssue>(vector, "responseIssues");
            Verify(evidence.Issues == expectedIssues, "Independent outer all-account issues: " + name + " (" + evidence.Issues + ").");
            Verify(evidence.ResponseIssues == expectedResponseIssues, "Independent semantic response issues: " + name + " (" + evidence.ResponseIssues + ").");
            Verify(evidence.Outcome.ToString() == vector.GetProperty("outcome").GetString() &&
                evidence.HasMatchingEvidence == (evidence.Issues == FinTsPinTanAllAccountIssue.None),
                "Qualified outcomes never escape review issues.");
            Verify(ReferenceEquals(evidence.Binding, binding) && ReferenceEquals(evidence.Request, request) &&
                ReferenceEquals(evidence.Response, response) && ReferenceEquals(evidence.ReadData, data),
                "Semantic evidence retains exact binding, request, response, and schema instances.");
            Verify(evidence.HasMatchingEvidence ? ReferenceEquals(evidence.MatchingReadData, data) : evidence.MatchingReadData is null,
                "Only fully matching evidence exposes qualified read data.");
            Verify(dataBefore.SequenceEqual(binding.Response.Frame.Syntax.CopyWireBytes()) &&
                data.Discovery.Select(r => r.Source).All(s => binding.Response.BodySegments.Any(raw => ReferenceEquals(raw, s))),
                "Typed reports retain original segments and unchanged response wire.");
            Verify(FinTsPinTanAllAccountEvidence.Evaluate(binding, data).Issues == evidence.Issues,
                "Repeated semantic comparison does not consume response.");
        }

        Verify(vectors.Length == 52, "All fifty-two independent all-account response fixtures ran.");

        JsonElement Named(string name) => vectors.Single(v => v.GetProperty("name").GetString() == name);
        FinTsPinTanAllAccountEvidence Evidence(string name)
        {
            var vector = Named(name);
            var req = Candidate(vector);
            var resp = Response(vector);
            var b = FinTsPinTanAllAccountResponseBinding.Evaluate(req, resp);
            var d = FinTsReadDataSet.Parse(resp);
            return FinTsPinTanAllAccountEvidence.Evaluate(b, d);
        }

        // Clean discovery tests
        var clean = Evidence("clean-discovery");
        Verify(clean.HasMatchingEvidence && clean.Outcome == FinTsReadOutcomeObservation.ExecutionReported &&
            clean.Accounts.Count == 1 && clean.Accounts[0].MatchedAccount is not null && clean.UnmatchedUserAccounts.Count == 0,
            "Clean discovery matches single UPD account without issues.");

        var multiple = Evidence("clean-multiple-upd-accounts");
        Verify(multiple.HasMatchingEvidence && multiple.Outcome == FinTsReadOutcomeObservation.ExecutionReported &&
            multiple.Accounts.Count == 3 && multiple.Accounts.All(a => a.MatchedAccount is not null) && multiple.UnmatchedUserAccounts.Count == 0,
            "Clean discovery matches multiple UPD accounts cleanly.");

        var oneStep = Evidence("clean-one-step-profile");
        Verify(oneStep.HasMatchingEvidence && oneStep.Request.ProfileVersion == 1,
            "One-step profile matches cleanly.");

        var tanSix = Evidence("clean-tan-version-six");
        Verify(tanSix.HasMatchingEvidence && tanSix.Request.ProfileVersion == 2,
            "Two-step TAN version 6 profile matches cleanly.");

        var escaped = Evidence("clean-escaped-dialogue");
        Verify(escaped.HasMatchingEvidence && escaped.Request.DialogueId == "D+:'?@ü",
            "Escaped assigned dialogue matches cleanly.");

        // Unavailable reports
        var absent = Evidence("clean-unavailable-absent");
        var empty = Evidence("clean-unavailable-empty");
        Verify(absent.HasMatchingEvidence && empty.HasMatchingEvidence &&
            absent.ReadData.Discovery.Count == 0 && empty.ReadData.Discovery.Single().Accounts.Count == 0 &&
            absent.Outcome == FinTsReadOutcomeObservation.UnavailableReported && empty.Outcome == FinTsReadOutcomeObservation.UnavailableReported,
            "Absent and explicitly empty discovery reports retain different source shapes under unavailable outcome.");

        var unavailExec = Evidence("clean-unavailable-with-execution");
        Verify(unavailExec.HasMatchingEvidence && unavailExec.Outcome == FinTsReadOutcomeObservation.UnavailableReported,
            "Unavailable report with 0020 acknowledgement yields UnavailableReported.");

        var unavailParam = Evidence("clean-unavailable-empty-parameter");
        Verify(unavailParam.HasMatchingEvidence && unavailParam.Outcome == FinTsReadOutcomeObservation.UnavailableReported,
            "Unavailable report with empty trailing parameter is accepted.");

        // Account semantics issues
        var unmatched = Evidence("unmatched-user-accounts");
        Verify(!unmatched.HasMatchingEvidence && unmatched.Issues == FinTsPinTanAllAccountIssue.UnmatchedUserAccounts &&
            unmatched.Accounts.Count == 1 && unmatched.UnmatchedUserAccounts.Count == 2,
            "Missing returned accounts leave UPD entries unmatched.");

        var unknown = Evidence("unknown-returned-account");
        Verify(!unknown.HasMatchingEvidence && unknown.Issues.HasFlag(FinTsPinTanAllAccountIssue.AccountNeedsReview) &&
            unknown.Accounts.Single().Issues.HasFlag(FinTsDiscoveryAccountIssue.UnknownAccount),
            "Unknown returned account flags AccountNeedsReview.");

        var dupId = Evidence("duplicate-returned-identity");
        Verify(!dupId.HasMatchingEvidence && dupId.Issues.HasFlag(FinTsPinTanAllAccountIssue.AccountNeedsReview) &&
            dupId.Accounts.All(a => a.Issues.HasFlag(FinTsDiscoveryAccountIssue.DuplicateReturnedIdentity)),
            "Duplicate returned identity flags AccountNeedsReview.");

        var inst = Evidence("institution-mismatch");
        Verify(!inst.HasMatchingEvidence && inst.Issues.HasFlag(FinTsPinTanAllAccountIssue.AccountNeedsReview) &&
            inst.Accounts.Single().Issues.HasFlag(FinTsDiscoveryAccountIssue.InstitutionMismatch),
            "Institution mismatch flags AccountNeedsReview.");

        var idConf = Evidence("identity-conflict");
        Verify(!idConf.HasMatchingEvidence && idConf.Issues.HasFlag(FinTsPinTanAllAccountIssue.AccountNeedsReview) &&
            idConf.Accounts.Single().Issues.HasFlag(FinTsDiscoveryAccountIssue.IdentityConflict),
            "Conflicting IBAN flags IdentityConflict on AccountNeedsReview.");

        // Envelope identity mismatches
        foreach (string envName in new[] { "foreign-envelope-country", "foreign-envelope-bank", "foreign-envelope-user", "foreign-envelope-system" })
        {
            var envEv = Evidence(envName);
            Verify(!envEv.HasMatchingEvidence && envEv.Issues == FinTsPinTanAllAccountIssue.EnvelopeIdentityMismatch && envEv.MatchingReadData is null,
                "Envelope identity mismatch withholds qualified read data: " + envName);
        }

        // Security execution is not read execution
        var secSuccess = Evidence("security-success-not-read-execution");
        Verify(!secSuccess.HasMatchingEvidence && secSuccess.Binding.HasMatchingReferences &&
            secSuccess.Issues.HasFlag(FinTsPinTanAllAccountIssue.ResponseNeedsReview) && secSuccess.MatchingReadData is null,
            "Security execution report establishes no read execution.");

        // Re-parsed source mismatch (ReadSourceMismatch)
        var reparsedData = FinTsReadDataSet.Parse(Response(Named("clean-discovery")));
        var mismatch = FinTsPinTanAllAccountEvidence.Evaluate(clean.Binding, reparsedData);
        Verify(mismatch.Issues == FinTsPinTanAllAccountIssue.ReadSourceMismatch && !mismatch.HasMatchingEvidence &&
            mismatch.MatchingReadData is null && ReferenceEquals(mismatch.ReadData, reparsedData) &&
            mismatch.Accounts.Count == 0 && mismatch.UnmatchedUserAccounts.Count == 0,
            "Separate parsed response instance fails source identity comparison before semantic interpretation.");

        // Cancellation tests
        using (var cts = new CancellationTokenSource())
        {
            cts.Cancel();
            try
            {
                FinTsPinTanAllAccountResponseBinding.Evaluate(clean.Request, clean.Response, cts.Token);
                Verify(false, "Pre-cancelled token must throw on response binding.");
            }
            catch (OperationCanceledException)
            {
                Verify(true, "Cancelled response binding publishes no result.");
            }

            try
            {
                FinTsPinTanAllAccountEvidence.Evaluate(clean.Binding, clean.ReadData, cts.Token);
                Verify(false, "Pre-cancelled token must throw on evidence evaluation.");
            }
            catch (OperationCanceledException)
            {
                Verify(true, "Cancelled evidence evaluation publishes no result.");
            }
        }

        // Argument null tests
        try
        {
            FinTsPinTanAllAccountResponseBinding.Evaluate(null!, clean.Response);
            Verify(false, "Null request must throw.");
        }
        catch (ArgumentNullException ex)
        {
            Verify(!ex.ToString().Contains("PUBLIC-", StringComparison.Ordinal), "Null request diagnostic preserves privacy.");
        }

        try
        {
            FinTsPinTanAllAccountResponseBinding.Evaluate(clean.Request, null!);
            Verify(false, "Null response must throw.");
        }
        catch (ArgumentNullException ex)
        {
            Verify(!ex.ToString().Contains("PUBLIC-", StringComparison.Ordinal), "Null response diagnostic preserves privacy.");
        }

        try
        {
            FinTsPinTanAllAccountEvidence.Evaluate(null!, clean.ReadData);
            Verify(false, "Null binding must throw.");
        }
        catch (ArgumentNullException ex)
        {
            Verify(!ex.ToString().Contains("PUBLIC-", StringComparison.Ordinal), "Null binding diagnostic preserves privacy.");
        }

        try
        {
            FinTsPinTanAllAccountEvidence.Evaluate(clean.Binding, null!);
            Verify(false, "Null data must throw.");
        }
        catch (ArgumentNullException ex)
        {
            Verify(!ex.ToString().Contains("PUBLIC-", StringComparison.Ordinal), "Null data diagnostic preserves privacy.");
        }

        // Concurrency tests
        var bindingResults = new FinTsPinTanAllAccountResponseBinding[16];
        Parallel.For(0, bindingResults.Length, i => bindingResults[i] = FinTsPinTanAllAccountResponseBinding.Evaluate(clean.Request, clean.Response));
        Verify(bindingResults.All(r => r.HasMatchingReferences && ReferenceEquals(r.Request, clean.Request) && ReferenceEquals(r.Response, clean.Response)),
            "Concurrent response bindings preserve immutable source identity without consumption.");

        var evidenceResults = new FinTsPinTanAllAccountEvidence[16];
        Parallel.For(0, evidenceResults.Length, i => evidenceResults[i] = FinTsPinTanAllAccountEvidence.Evaluate(clean.Binding, clean.ReadData));
        Verify(evidenceResults.All(e => e.HasMatchingEvidence && ReferenceEquals(e.Binding, clean.Binding) && ReferenceEquals(e.ReadData, clean.ReadData)),
            "Concurrent evidence evaluations preserve immutable source identity without consumption.");

        // Culture invariance tests
        var prevCulture = CultureInfo.CurrentCulture;
        try
        {
            foreach (string cultureName in new[] { "de-DE", "ar-SA", "tr-TR" })
            {
                CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo(cultureName);
                var cultBinding = FinTsPinTanAllAccountResponseBinding.Evaluate(clean.Request, clean.Response);
                Verify(cultBinding.HasMatchingReferences, "Binding evaluation is culture invariant in " + cultureName);
                var cultEvidence = FinTsPinTanAllAccountEvidence.Evaluate(clean.Binding, clean.ReadData);
                Verify(cultEvidence.HasMatchingEvidence, "Evidence evaluation is culture invariant in " + cultureName);
            }
        }
        finally
        {
            CultureInfo.CurrentCulture = prevCulture;
        }

        // Reflection assertions
        var bindingType = typeof(FinTsPinTanAllAccountResponseBinding);
        Verify(bindingType.IsSealed && bindingType.IsPublic, "FinTsPinTanAllAccountResponseBinding is public sealed.");
        Verify(bindingType.GetConstructor(BindingFlags.NonPublic | BindingFlags.Instance, null,
            new[] { typeof(FinTsPinTanAllAccountRequestBinding), typeof(FinTsResponse), typeof(List<FinTsPinTanResponseSegmentBinding>), typeof(FinTsPinTanResponseBindingIssue) }, null) is not null,
            "Binding constructor is private.");

        var evidenceType = typeof(FinTsPinTanAllAccountEvidence);
        Verify(evidenceType.IsSealed && evidenceType.IsPublic, "FinTsPinTanAllAccountEvidence is public sealed.");
        Verify(evidenceType.GetConstructor(BindingFlags.NonPublic | BindingFlags.Instance, null,
            new[] { typeof(FinTsPinTanAllAccountResponseBinding), typeof(FinTsReadDataSet), typeof(FinTsPinTanAllAccountIssue), typeof(FinTsReadContextIssue), typeof(FinTsReadOutcomeObservation), typeof(List<FinTsDiscoveryAccountEvidence>), typeof(List<FinTsAccountParameters>) }, null) is not null,
            "Evidence constructor is private.");

        Console.WriteLine($"FinTS PIN/TAN all-account response reference binding and semantics passed ({vectors.Length} independent vectors, {count} checks).");
    }

    private static FinTsPinTanAllAccountRequestBinding Candidate(JsonElement vector)
    {
        var candidateElement = vector.GetProperty("candidate");
        var context = Context(candidateElement);
        return FinTsPinTanAllAccountRequestBinding.ForEnvelopeCandidate(context);
    }

    private static FinTsResponse Response(JsonElement vector)
    {
        var frame = FinTsMessageFrame.Parse(Convert.FromBase64String(vector.GetProperty("responseBase64").GetString()!));
        return vector.GetProperty("wrapped").GetBoolean() ? FinTsResponse.ParsePinTan(FinTsPinTanEnvelope.Parse(frame)) : FinTsResponse.Parse(frame);
    }

    private static FinTsPinTanAllAccountCapabilityContext Context(JsonElement candidateElement)
    {
        var contextElement = candidateElement.GetProperty("context");
        var (binding, tan, permissions, hipins) = FinTsPinTanInitializationRequirementsTests.Inputs(contextElement.GetProperty("initialization"));
        var reads = FinTsReadParameterSet.Parse(hipins.Source);
        var initialization = FinTsPinTanInitializationRequirementsEvidence.EvaluateWithReadParameters(binding, tan, permissions, hipins, reads);
        int expectedBank = contextElement.GetProperty("expectedBankMessageNumber").GetInt32();
        var request = FinTsReadRequestContext.Parse(FinTsMessageFrame.Parse(Convert.FromBase64String(contextElement.GetProperty("requestBase64").GetString()!)), expectedBank);
        var header = FinTsPinTanSignatureHeader.Parse(FinTsSyntax.ParseSegments(Convert.FromBase64String(contextElement.GetProperty("headerBase64").GetString()!)).Segments.Single());
        var selection = binding.Request.SignatureEvidence.Request.Selection;
        string controlReference = candidateElement.GetProperty("controlReference").GetString()!;
        var signature = FinTsPinTanReadSignatureContext.Evaluate(initialization, request, header, selection, controlReference);
        return FinTsPinTanAllAccountCapabilityContext.Evaluate(signature);
    }

    private static TEnum Flags<TEnum>(JsonElement vector, string propertyName) where TEnum : struct, Enum =>
        vector.GetProperty(propertyName).EnumerateArray().Aggregate(default(TEnum),
            (flags, value) => (TEnum)(object)((int)(object)flags | (int)Enum.Parse(typeof(TEnum), value.GetString()!)));
}
