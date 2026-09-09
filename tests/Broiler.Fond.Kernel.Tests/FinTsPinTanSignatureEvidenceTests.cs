using System.Globalization;
using System.Reflection;
using System.Text;
using System.Text.Json;
using Broiler.Fond.Kernel.FinTs;

namespace Broiler.Fond.Kernel.Tests;

internal static class FinTsPinTanSignatureEvidenceTests
{
    internal static void Run(Action<bool, string> check)
    {
        int count = 0;
        void Verify(bool condition, string message) { count++; check(condition, message); }
        void Has(FinTsPinTanSignatureEvidence evidence, FinTsPinTanSignatureIssue issue) => Verify(evidence.Issues.HasFlag(issue) && !evidence.HasMatchingEvidence && evidence.MatchingAdvertisement is null && evidence.MatchingProcedure is null, "Unresolved signature evidence selects no advertisement or procedure: " + issue);
        using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream("FinTs.pin-tan-signature-context-v1.json")!;
        using var doc = JsonDocument.Parse(stream);
        var vectors = doc.RootElement.GetProperty("vectors").EnumerateArray().ToArray();
        foreach (var v in vectors)
        {
            var request = Context(v); var header = Header(v); var procedures = v.GetProperty("missingProcedureContext").GetBoolean() ? null : Procedures(v);
            var evidence = FinTsPinTanSignatureEvidence.Evaluate(request, header, procedures);
            var expected = v.GetProperty("issues").EnumerateArray().Aggregate(FinTsPinTanSignatureIssue.None, (a, e) => a | Enum.Parse<FinTsPinTanSignatureIssue>(e.GetString()!));
            Verify(evidence.Issues == expected, $"Independent signature issues match {v.GetProperty("name").GetString()}: {evidence.Issues}.");
            Verify(evidence.HasMatchingEvidence == (expected == 0) && (evidence.MatchingAdvertisement is not null) == (expected == 0), "Only fully matching evidence exposes an advertisement.");
            Verify((evidence.MatchingProcedure is not null) == (expected == 0 && request.Selection.ProfileVersion == 2), "One-step comparison never invents a two-step procedure.");
            Verify(ReferenceEquals(evidence.Request, request) && ReferenceEquals(evidence.Header, header) && ReferenceEquals(evidence.Procedures, procedures), "Exact caller-owned request, header and procedure contexts remain available.");
        }
        Verify(vectors.Length == 18, "All eighteen independent signature-context vectors ran.");
        var sample = vectors[0]; var requestBase = Context(sample); var headerBase = Header(sample); var proceduresBase = Procedures(sample);
        FinTsPinTanSignatureEvidence Evaluate(FinTsPinTanSignatureRequestContext? request = null, FinTsPinTanSignatureHeader? header = null, FinTsPinTanProcedureContext? procedures = null) => FinTsPinTanSignatureEvidence.Evaluate(request ?? requestBase, header ?? headerBase, procedures ?? proceduresBase);
        var matched = Evaluate();
        Verify(ReferenceEquals(matched.MatchingAdvertisement, proceduresBase.Parameters.Advertisements[0]) && ReferenceEquals(matched.MatchingProcedure, proceduresBase.Parameters.Advertisements[0].Procedures[0]), "Selected observations belong to the exact supplied parameter source.");
        var reparsedPermissions = FinTsPermittedProcedureSet.Parse(Response(sample));
        Has(Evaluate(procedures: new(proceduresBase.Initialization, proceduresBase.Parameters, reparsedPermissions)), FinTsPinTanSignatureIssue.ProcedureScopeMismatch);
        Verify(Evaluate().HasMatchingEvidence && Evaluate().HasMatchingEvidence, "Pure repeated comparisons consume no evidence or replay state.");
        foreach (var edit in new Func<string, string>[]
        {
            s => s.Replace("280:PUBLIC-BANK", "281:PUBLIC-BANK", StringComparison.Ordinal),
            s => s.Replace("PUBLIC-BANK", "OTHER-BANK", StringComparison.Ordinal),
            s => s.Replace("PUBLIC-USER", "public-user", StringComparison.Ordinal),
        }) { Has(Evaluate(header: Header(sample, edit)), FinTsPinTanSignatureIssue.IdentityMismatch); }
        foreach (var edit in new Func<string, string>[]
        {
            s => s.Replace("HNSHK:2:4", "HNSHK:3:4", StringComparison.Ordinal),
            s => s.Replace("+1+1+1::", "+1+3+1::", StringComparison.Ordinal),
            s => s.Replace("+1+1+1::", "+1+1+2::", StringComparison.Ordinal),
        }) { Has(Evaluate(header: Header(sample, edit)), FinTsPinTanSignatureIssue.HeaderRoleNeedsReview); }
        var initialization = requestBase.Initialization!;
        foreach (string system in new[] { "0", "unbekannt" })
        {
            var changed = FinTsUnsignedInitializationRequest.Parse(Frame(sample.GetProperty("requestBase64").GetString()!, s => s.Replace("PUBLIC-SYSTEM", system, StringComparison.Ordinal)));
            var context = FinTsPinTanSignatureRequestContext.ForInitialization(changed, "PUBLIC-USER", "PUBLIC-REF", requestBase.Selection);
            Has(Evaluate(context, Header(sample, s => s.Replace("PUBLIC-SYSTEM", system, StringComparison.Ordinal))), FinTsPinTanSignatureIssue.SystemNeedsReview);
        }
        var anonymous = FinTsUnsignedInitializationRequest.Parse(FinTsMessageFrame.Parse(FinTsUnsignedInitializationWriter.EncodeAnonymous("280", "PUBLIC-BANK", "PUBLIC-PRODUCT", "1")));
        Has(Evaluate(FinTsPinTanSignatureRequestContext.ForInitialization(anonymous, "PUBLIC-USER", "PUBLIC-REF", requestBase.Selection)), FinTsPinTanSignatureIssue.RequestNeedsReview);
        var wrongStatus = FinTsUnsignedInitializationRequest.Parse(Frame(sample.GetProperty("requestBase64").GetString()!, s => s.Replace("PUBLIC-SYSTEM+1'", "PUBLIC-SYSTEM+0'", StringComparison.Ordinal)));
        Has(Evaluate(FinTsPinTanSignatureRequestContext.ForInitialization(wrongStatus, "PUBLIC-USER", "PUBLIC-REF", requestBase.Selection)), FinTsPinTanSignatureIssue.RequestNeedsReview);
        var syncSample = vectors[2];
        var signatureSync = FinTsUnsignedSynchronizationRequest.Parse(Frame(syncSample.GetProperty("requestBase64").GetString()!, s => s.Replace("HKSYN:4:3+0", "HKSYN:4:3+2", StringComparison.Ordinal).Replace("PUBLIC-CUSTOMER+0+1'", "PUBLIC-CUSTOMER+PUBLIC-SYSTEM+1'", StringComparison.Ordinal)));
        Has(Evaluate(FinTsPinTanSignatureRequestContext.ForSynchronization(signatureSync, "PUBLIC-USER", "PUBLIC-REF", requestBase.Selection)), FinTsPinTanSignatureIssue.RequestNeedsReview);
        foreach (var edit in new Func<string, string>[]
        {
            s => s.Replace("SYNTHETIC:1'", "SYNTHETIC:2'", StringComparison.Ordinal),
            s => s.Replace("+1+SYNTHETIC:1'", "+1'", StringComparison.Ordinal),
            s => s.Replace("HITANS:7:7:3", "HITANS:7:7:2", StringComparison.Ordinal),
            s => s.Replace("280:PUBLIC-BANK", "280:OTHER-BANK", StringComparison.Ordinal),
            s => s.Replace("+300'", "+301'", StringComparison.Ordinal),
        }) { Has(Evaluate(procedures: Procedures(sample, edit)), FinTsPinTanSignatureIssue.ProcedureScopeMismatch); }
        var wrongOrigin = FinTsUnsignedInitializationRequest.Parse(Frame(sample.GetProperty("originRequestBase64").GetString()!, s => s.Replace("PUBLIC-CUSTOMER", "OTHER-CUSTOMER", StringComparison.Ordinal)));
        Has(Evaluate(procedures: new(wrongOrigin, proceduresBase.Parameters, proceduresBase.Permissions)), FinTsPinTanSignatureIssue.ProcedureScopeMismatch);
        foreach (var edit in new Func<string, string>[]
        {
            s => s.Replace("0010::PUBLIC-REPLY", "7777::PUBLIC-REPLY", StringComparison.Ordinal),
            s => s.Replace("0020::PUBLIC-ID-REPLY", "0010::PUBLIC-ID-REPLY", StringComparison.Ordinal),
            s => s.Replace("0020::PUBLIC-PREP-REPLY", "0030::PUBLIC-PREP-REPLY", StringComparison.Ordinal),
            s => s.Replace("0020::PUBLIC-ID-REPLY", "0020::PUBLIC-ID-REPLY:PUBLIC-EXTRA", StringComparison.Ordinal),
            s => s.Replace("0020::PUBLIC-ID-REPLY", "0020:1:PUBLIC-ID-REPLY", StringComparison.Ordinal),
        }) { Has(Evaluate(procedures: Procedures(sample, edit)), FinTsPinTanSignatureIssue.ProcedureResponseNeedsReview); }
        Has(Evaluate(procedures: Procedures(sample, s => s.Replace("3920::PUBLIC-PERMISSIONS:900:999", "3920::PUBLIC-PERMISSIONS:900:999+3920::PUBLIC-OTHER:900", StringComparison.Ordinal))), FinTsPinTanSignatureIssue.AmbiguousPermissions);
        Has(Evaluate(procedures: Procedures(sample, s => s.Replace("HITANS:7:7:3", "HITANS:7:99:3", StringComparison.Ordinal))), FinTsPinTanSignatureIssue.UninterpretedParameters);
        foreach (var edit in new Func<string, string>[]
        {
            s => s.Replace("HIBPA:5:3:3", "ZBANK:5:3:3", StringComparison.Ordinal),
            s => s.Replace("HIUPA:6:4:3", "ZUSER:6:4:3", StringComparison.Ordinal),
        }) { Has(Evaluate(procedures: Procedures(sample, edit)), FinTsPinTanSignatureIssue.ProcedureScopeMismatch); }
        foreach (string prefix in new[] { "+0+1+0+", "+1+2+0+", "+1+3+0+" })
        { Has(Evaluate(procedures: Procedures(sample, s => s.Replace("HITANS:7:7:3+1+1+0+", "HITANS:7:7:3" + prefix, StringComparison.Ordinal))), FinTsPinTanSignatureIssue.AdvertisementRequirementsNeedReview); }
        Verify(Evaluate(procedures: Procedures(sample, s => s.Replace("HITANS:7:7:3+1+1+0+", "HITANS:7:7:3+999+0+0+", StringComparison.Ordinal))).HasMatchingEvidence, "A zero minimum signature count and maximum order bound do not prohibit the supplied single header.");
        var version6 = FinTsPinTanSignatureRequestContext.ForInitialization(initialization, "PUBLIC-USER", "PUBLIC-REF", new(2, 900, 6));
        Has(Evaluate(version6), FinTsPinTanSignatureIssue.MissingAdvertisement);
        var notAdvertised = FinTsPinTanSignatureRequestContext.ForInitialization(initialization, "PUBLIC-USER", "PUBLIC-REF", new(2, 920, 7));
        Has(Evaluate(notAdvertised, Header(sample, s => s.Replace("+900+", "+920+", StringComparison.Ordinal))), FinTsPinTanSignatureIssue.ProcedureNotListed);
        Verify(Evaluate(header: Header(sample, s => s.Replace("+1+1:20260908:123456", "+9999999999999999+1::", StringComparison.Ordinal).Replace("6:10:16", "6:001:000", StringComparison.Ordinal))).HasMatchingEvidence, "Timestamps, reference numbers and fillers are not reinterpreted as cryptographic requirements or freshness.");
        var mutation = Convert.FromBase64String(sample.GetProperty("headerBase64").GetString()!);
        var isolated = FinTsPinTanSignatureHeader.Parse(FinTsSyntax.ParseSegments(mutation).Segments[0]); mutation[0] = 0;
        Verify(Evaluate(header: isolated).HasMatchingEvidence, "External byte mutations cannot change header evidence.");
        foreach (var values in new[] { (0, 999, 7), (1, 900, 7), (2, 999, 7), (2, 898, 7), (2, 900, 8) })
        {
            try { _ = new FinTsPinTanSignatureSelection(values.Item1, values.Item2, values.Item3); Verify(false, "Malformed selection must fail."); }
            catch (FinTsFormatException error) { Verify(error.Error == FinTsSyntaxError.InvalidSignatureContext, "Invalid caller selection fails with fixed diagnostics."); }
        }
        foreach (string value in new[] { "", " padded", "padded ", "PUBLIC-SECRET\n", "€", new string('x', 31) })
        {
            try { _ = FinTsPinTanSignatureRequestContext.ForInitialization(initialization, value, "PUBLIC-REF", requestBase.Selection); Verify(false, "Malformed user context must fail."); }
            catch (FinTsFormatException error) { Verify(error.Error == FinTsSyntaxError.InvalidSignatureContext && !error.ToString().Contains("PUBLIC-SECRET", StringComparison.Ordinal), "Malformed caller identity uses fixed diagnostics."); }
        }
        foreach (string value in new[] { "", "0", new string('x', 15) })
        {
            try { _ = FinTsPinTanSignatureRequestContext.ForInitialization(initialization, "PUBLIC-USER", value, requestBase.Selection); Verify(false, "Malformed control expectation must fail."); }
            catch (FinTsFormatException error) { Verify(error.Error == FinTsSyntaxError.InvalidSignatureContext, "Malformed control expectation fails."); }
        }
        foreach (Action action in new Action[]
        {
            () => FinTsPinTanSignatureRequestContext.ForInitialization(null!, "U", "R", requestBase.Selection),
            () => FinTsPinTanSignatureRequestContext.ForSynchronization(null!, "U", "R", requestBase.Selection),
            () => FinTsPinTanSignatureRequestContext.ForInitialization(initialization, null!, "R", requestBase.Selection),
            () => FinTsPinTanSignatureRequestContext.ForInitialization(initialization, "U", null!, requestBase.Selection),
            () => FinTsPinTanSignatureRequestContext.ForInitialization(initialization, "U", "R", null!),
            () => new FinTsPinTanProcedureContext(null!, proceduresBase.Parameters, proceduresBase.Permissions),
            () => new FinTsPinTanProcedureContext(initialization, null!, proceduresBase.Permissions),
            () => new FinTsPinTanProcedureContext(initialization, proceduresBase.Parameters, null!),
            () => FinTsPinTanSignatureEvidence.Evaluate(null!, headerBase), () => FinTsPinTanSignatureEvidence.Evaluate(requestBase, null!),
        })
        {
            try { action(); Verify(false, "Null input must fail."); }
            catch (ArgumentNullException) { Verify(true, "Null API input is rejected."); }
        }
        using var cancelled = new CancellationTokenSource(); cancelled.Cancel();
        try { FinTsPinTanSignatureEvidence.Evaluate(requestBase, headerBase, proceduresBase, cancelled.Token); Verify(false, "Cancellation must stop comparison."); }
        catch (OperationCanceledException) { Verify(true, "Comparison observes cancellation."); }
        var culture = CultureInfo.CurrentCulture;
        try
        {
            foreach (string name in new[] { "de-DE", "ar-SA", "tr-TR" })
            { CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo(name); Verify(Evaluate().HasMatchingEvidence, "Comparison is culture-independent."); }
        }
        finally { CultureInfo.CurrentCulture = culture; }
        Verify(new object[] { matched, requestBase, requestBase.Selection, proceduresBase }.All(o => !o.ToString()!.Contains("PUBLIC", StringComparison.Ordinal)), "Default context diagnostics exclude identities and source text.");
        Console.WriteLine($"FinTS PIN/TAN signature-context verification passed ({vectors.Length} independent vectors, {count} checks).");
    }
    private static FinTsPinTanSignatureRequestContext Context(JsonElement v)
    {
        var frame = Frame(v.GetProperty("requestBase64").GetString()!);
        var selection = new FinTsPinTanSignatureSelection(v.GetProperty("profileVersion").GetInt32(), v.GetProperty("securityFunction").GetInt32(), v.GetProperty("tanSegmentVersion").GetInt32());
        string user = v.GetProperty("expectedUserId").GetString()!, control = v.GetProperty("controlReference").GetString()!;
        return v.GetProperty("requestKind").GetString() == "initialization" ? FinTsPinTanSignatureRequestContext.ForInitialization(FinTsUnsignedInitializationRequest.Parse(frame), user, control, selection)
            : FinTsPinTanSignatureRequestContext.ForSynchronization(FinTsUnsignedSynchronizationRequest.Parse(frame), user, control, selection);
    }
    private static FinTsPinTanProcedureContext Procedures(JsonElement v, Func<string, string>? edit = null)
    {
        var response = Response(v, edit);
        return new(FinTsUnsignedInitializationRequest.Parse(Frame(v.GetProperty("originRequestBase64").GetString()!)),
            FinTsTanParameterSet.Parse(FinTsParameterSet.Parse(response)), FinTsPermittedProcedureSet.Parse(response));
    }
    private static FinTsResponse Response(JsonElement v, Func<string, string>? edit = null) => FinTsResponse.Parse(Frame(v.GetProperty("procedureResponseBase64").GetString()!, edit));
    private static FinTsPinTanSignatureHeader Header(JsonElement v, Func<string, string>? edit = null)
    {
        string text = Encoding.Latin1.GetString(Convert.FromBase64String(v.GetProperty("headerBase64").GetString()!));
        return FinTsPinTanSignatureHeader.Parse(FinTsSyntax.ParseSegments(Encoding.Latin1.GetBytes(edit is null ? text : edit(text))).Segments.Single());
    }
    private static FinTsMessageFrame Frame(string bytes, Func<string, string>? edit = null)
    {
        string wire = Encoding.Latin1.GetString(Convert.FromBase64String(bytes)); if (edit is not null) { wire = edit(wire); }
        wire = wire[..10] + Encoding.Latin1.GetByteCount(wire).ToString("D12", CultureInfo.InvariantCulture) + wire[22..];
        return FinTsMessageFrame.Parse(Encoding.Latin1.GetBytes(wire));
    }
}
