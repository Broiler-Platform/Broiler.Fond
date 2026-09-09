using System.Globalization;
using System.Reflection;
using System.Text;
using System.Text.Json;
using Broiler.Fond.Kernel.FinTs;

namespace Broiler.Fond.Kernel.Tests;

internal static class FinTsReadCapabilityTests
{
    private const string Bank = "HIBPA:3+1+280:10020030+Bank+1+1+300";
    private const string User = "HIUPA:4+PUBLIC-USER+1+1";
    private const string Account = "HIUPD:6+PUBLIC-ACCOUNT::280:10020030+SYNTHETIC-IBAN+C+1+EUR+Owner++++HKSAL:2+HKSPA:2";
    internal static void Run(Action<bool, string> check)
    {
        int count = 0;
        void Verify(bool result, string message) { count++; check(result, message); }
        void Reject(params string[] parts)
        {
            try { _ = Parse(parts); Verify(false, "Invalid supported read advertisement must fail."); }
            catch (FinTsFormatException error)
            {
                Verify(error.Error == FinTsSyntaxError.InvalidParameters || error.Error == FinTsSyntaxError.LimitExceeded, "Read schema failure must use fixed error categories.");
                Verify(!error.ToString().Contains("PUBLIC-", StringComparison.Ordinal), "Read schema errors must exclude supplied private fields.");
            }
        }
        FinTsReadCapabilityEvidence Evaluate(FinTsReadParameterSet input, FinTsReadOperation operation = FinTsReadOperation.Balance, int version = 7) =>
            FinTsReadCapabilityEvidence.Evaluate(input, input.Source.Accounts[0], operation, version);
        foreach (int version in new[] { 6, 7, 8 })
        {
            var input = Parse(Bank, User, Account, $"HISALS:{version}+9+1+4" + (version == 8 ? "+J" : ""));
            var advertisement = input.Advertisements.Single();
            var evidence = Evaluate(input, version: version);
            Verify(advertisement.MaximumOrders == 9 && advertisement.MinimumSignatures == 1 && advertisement.SecurityClass == 4,
                "Common bank read constraints must survive without interpreting security class as a TAN procedure.");
            Verify(advertisement.EntryCountInputAllowed == (version == 8 ? true : null), "Balance parameter options are version-specific.");
            Verify(evidence.HasMatchingEvidence && evidence.MinimumCustomerSignatures == 2 && evidence.Candidates.Count == 1,
                "Unique compatible evidence must retain the stronger UPD customer-signature requirement.");
        }
        foreach (int version in new[] { 1, 2, 3 })
        {
            string options = "J:N:J" + (version >= 2 ? ":N" : "") + (version == 3 ? ":12" : "") + ":urn?:synthetic:urn?:synthetic:";
            var input = Parse(Bank, User, Account, $"HISPAS:{version}+1+1+0+{options}");
            var advertisement = input.Advertisements.Single();
            Verify(advertisement.SingleAccountRequestAllowed == true && advertisement.NationalAccountConnectionAllowed == false && advertisement.StructuredRemittanceAllowed == true,
                "SEPA query flags must retain independent values.");
            Verify(advertisement.EntryCountInputAllowed == (version >= 2 ? false : null) && advertisement.ReservedRemittancePositions == (version == 3 ? 12 : null),
                "SEPA query flags and reserved positions must match their segment version.");
            Verify(advertisement.SepaFormats.Count == 3 && Text(advertisement.SepaFormats[0]) == "urn:synthetic" && advertisement.SepaFormats[2].IsEmpty,
                "Opaque schema identifiers, duplicates and empty positions must remain unchanged and unfetched.");
            Verify(Evaluate(input, FinTsReadOperation.SepaAccountDetails, version).HasMatchingEvidence, "Matching explicit SEPA query version must produce evidence only.");
        }
        (string[] Parts, FinTsReadEvidenceIssue Issue)[] scenarios =
        [
            ([User, Account, "HISALS:7+1+1+0"], FinTsReadEvidenceIssue.MissingBankParameters),
            ([Bank.Replace("+300", "+220", StringComparison.Ordinal), User, Account, "HISALS:7+1+1+0"], FinTsReadEvidenceIssue.Protocol300NotAdvertised),
            ([Bank, Account, "HISALS:7+1+1+0"], FinTsReadEvidenceIssue.MissingUserParameters),
            ([Bank, User, Account.Replace("HKSAL:2", "HKXYZ:2", StringComparison.Ordinal), "HISALS:7+1+1+0"], FinTsReadEvidenceIssue.PermissionUnknown),
            ([Bank, "HIUPA:4+PUBLIC-USER+1+0", Account.Replace("HKSAL:2", "HKXYZ:2", StringComparison.Ordinal), "HISALS:7+1+1+0"], FinTsReadEvidenceIssue.PermissionBlocked),
            ([Bank, User, Account + "+HKSAL:2", "HISALS:7+1+1+0"], FinTsReadEvidenceIssue.PermissionAmbiguous),
            ([Bank, User, Account], FinTsReadEvidenceIssue.MissingAdvertisement),
            ([Bank, User, Account, "HISALS:7+1+1+0", "HISALS:7+1+2+0"], FinTsReadEvidenceIssue.DuplicateAdvertisement),
            ([Bank, User, Account, "HISALS:7+1+3+0"], FinTsReadEvidenceIssue.SignatureConflict),
            ([Bank, User, Account, Account, "HISALS:7+1+1+0"], FinTsReadEvidenceIssue.DuplicateAccountIdentity),
            ([Bank, User, Account.Replace("SYNTHETIC-IBAN", "", StringComparison.Ordinal), "HISALS:7+1+1+0"], FinTsReadEvidenceIssue.MissingInternationalIdentity),
            ([Bank, User, Account, "HISALS:7+0+1+0"], FinTsReadEvidenceIssue.ZeroOrderCapacity),
            ([Bank, User, "HIUPD:6+++C+++++++HKSAL:1", "HISALS:7+1+1+0"], FinTsReadEvidenceIssue.MissingAccountConnection),
            ([Bank, User, Account.Replace("10020030", "10020031", StringComparison.Ordinal), "HISALS:7+1+1+0"], FinTsReadEvidenceIssue.InstitutionContextMismatch),
            ([Bank, User, Account.Replace("280", "040", StringComparison.Ordinal), "HISALS:7+1+1+0"], FinTsReadEvidenceIssue.InstitutionContextMismatch),
        ];
        foreach (var scenario in scenarios)
        {
            var evidence = Evaluate(Parse(scenario.Parts));
            Verify(!evidence.HasMatchingEvidence && evidence.Issues.HasFlag(scenario.Issue), "Missing/ambiguous/conflicting evidence must remain explicit and cannot match.");
        }
        var future = Parse(Bank, User, Account, "HISALS:9+opaque", "HISALS:7+1+1+0", "HIKOM:4+PUBLIC-ENDPOINT");
        Verify(future.UninterpretedSegments.Count == 2 && Evaluate(future, version: 9).Issues.HasFlag(FinTsReadEvidenceIssue.UnsupportedVersion) &&
            Evaluate(future, version: 9).Candidates.Count == 0, "Future versions must remain opaque with no fallback to a different version.");
        Verify(Evaluate(future).HasMatchingEvidence, "An explicitly selected understood version can be assessed independently of opaque advertisements.");
        var blockedSingle = Parse(Bank, User, Account, "HISPAS:1+1+1+0+N:J:N");
        Verify(Evaluate(blockedSingle, FinTsReadOperation.SepaAccountDetails, 1).Issues.HasFlag(FinTsReadEvidenceIssue.SingleAccountRequestNotAdvertised),
            "Single-account matching must not reinterpret an all-accounts-only advertisement.");
        var anonymousMinimum = Parse(Bank, User, Account.Replace("HKSAL:2", "HKSAL:0", StringComparison.Ordinal), "HISALS:7+1+0+0");
        Verify(Evaluate(anonymousMinimum).MinimumCustomerSignatures == 1, "Anonymous bank minimum cannot reduce a customer's signature lower bound to zero.");
        foreach (string reply in new[] { "9000", "0030", "3040", "7001", "3998" })
        {
            var input = Parse(Bank, User, Account, "HISALS:7+1+1+0");
            string wire = Encoding.Latin1.GetString(input.Source.Source.Frame.Syntax.CopyWireBytes()).Replace("0010::received", reply + "::received", StringComparison.Ordinal);
            var altered = FinTsReadParameterSet.Parse(FinTsParameterSet.Parse(FinTsResponse.Parse(FinTsMessageFrame.Parse(Encoding.Latin1.GetBytes(wire)))));
            Verify(Evaluate(altered).Issues.HasFlag(FinTsReadEvidenceIssue.ResponseNeedsReview), "Errors, pending, partial and unknown replies must block matching evidence.");
        }
        foreach (string bad in new[]
        {
            "HISALS:7+1+1", "HISALS:7+1+1+0+J", "HISALS:8+1+1+0", "HISALS:8+1+1+0+X", "HISALS:8+1+1+0+J:N",
            "HISALS:7+1000+1+0", "HISALS:7+01+1+0", "HISALS:7+1+4+0", "HISALS:7+1+1+5", "HISALS:7+@1@1+1+0",
            "HISPAS:1+1+1+0+J:N", "HISPAS:2+1+1+0+J:N:J", "HISPAS:3+1+1+0+J:N:J:N",
            "HISPAS:3+1+1+0+J:N:J:N:100", "HISPAS:3+1+1+0+J:N:J:N:01", "HISPAS:1+1+1+0+J:N:X", "HISPAS:1+1+1+0+J:N:J:@0@",
            "HISPAS:1+1+1+0+J:N:J:PUBLIC-SECRET\r", "HISPAS:1+1+1+0+J:N:J:" + new string('x', 257),
            "HISPAS:1+1+1+0+J:N:J" + new string(':', 100),
        }) { Reject(bad); }
        Verify(Parse("HISPAS:3+999+3+4+J:N:J:N:99" + string.Concat(Enumerable.Repeat(":" + new string('x', 256), 99))).Advertisements[0].SepaFormats.Count == 99,
            "Exact format count/text/common numeric boundaries must parse.");
        Verify(Parse(Enumerable.Repeat("HISALS:7+1+1+0", 128).ToArray()).Advertisements.Count == 128, "Exact advertisement bound must preserve duplicates.");
        Reject(Enumerable.Repeat("HISALS:7+1+1+0", 129).ToArray());
        Reject(Enumerable.Repeat("HISALS:999+opaque", 129).ToArray());
        try { _ = FinTsReadParameterSet.Parse(future.Source, new CancellationToken(true)); Verify(false, "Read schema cancellation must stop."); }
        catch (OperationCanceledException) { Verify(true, "Read schema cancellation works."); }
        try { _ = FinTsReadCapabilityEvidence.Evaluate(future, future.Source.Accounts[0], FinTsReadOperation.Balance, 7, new CancellationToken(true)); Verify(false, "Matching cancellation must stop."); }
        catch (OperationCanceledException) { Verify(true, "Matching cancellation works."); }
        foreach (var action in new Action[]
        {
            () => FinTsReadCapabilityEvidence.Evaluate(future, anonymousMinimum.Source.Accounts[0], FinTsReadOperation.Balance, 7),
            () => FinTsReadCapabilityEvidence.Evaluate(future, future.Source.Accounts[0], (FinTsReadOperation)99, 7),
            () => FinTsReadCapabilityEvidence.Evaluate(future, future.Source.Accounts[0], FinTsReadOperation.Balance, 0),
        })
        {
            try { action(); Verify(false, "Foreign scope, unsupported operation enum and invalid version must fail."); }
            catch (FinTsFormatException) { Verify(true, "Invalid matching inputs fail safely."); }
        }
        Verify(new object[] { future, future.Advertisements[0], Evaluate(future) }.All(o => !o.ToString()!.Contains("PUBLIC-", StringComparison.Ordinal)),
            "Default capability diagnostics must exclude private source identifiers.");

        using Stream stream = Assembly.GetExecutingAssembly().GetManifestResourceStream("FinTs.read-capabilities-v1.json")!;
        using JsonDocument corpus = JsonDocument.Parse(stream);
        int vectors = 0;
        foreach (JsonElement vector in corpus.RootElement.GetProperty("vectors").EnumerateArray())
        {
            vectors++;
            byte[] wire = Convert.FromBase64String(vector.GetProperty("wireBase64").GetString()!);
            var input = FinTsReadParameterSet.Parse(FinTsParameterSet.Parse(FinTsResponse.Parse(FinTsMessageFrame.Parse(wire))));
            var evidence = Evaluate(input, Enum.Parse<FinTsReadOperation>(vector.GetProperty("operation").GetString()!), vector.GetProperty("version").GetInt32());
            var expected = vector.GetProperty("issues").EnumerateArray().Select(e => Enum.Parse<FinTsReadEvidenceIssue>(e.GetString()!)).Aggregate(FinTsReadEvidenceIssue.None, (a, b) => a | b);
            Verify(evidence.Issues == expected, "Independent fixtures must produce the exact conservative issue set.");
            Verify(input.Source.Source.Frame.Syntax.CopyWireBytes().SequenceEqual(wire), "Independent capability wire evidence must remain byte-identical.");
        }
        Verify(vectors == 5, "All five independent capability fixtures must execute.");
        Console.WriteLine($"FinTS read schemas/capability matching: {vectors} independent vectors and {count} checks completed.");
    }
    private static string Text(FinTsDataElement element) => Encoding.Latin1.GetString(element.CopyValueBytes());
    private static FinTsReadParameterSet Parse(params string[] parts)
    {
        StringBuilder body = new("HIRMG:2:2+0010::received'");
        int number = 3;
        foreach (string part in parts)
        {
            int colon = part.IndexOf(':');
            body.Append(part.AsSpan(0, colon)).Append(':').Append((number++).ToString(CultureInfo.InvariantCulture)).Append(part.AsSpan(colon)).Append('\'');
        }
        string wire = "HNHBK:1:3+000000000000+300+D+1+D:1'" + body + $"HNHBS:{number}:1+1'";
        wire = wire.Replace("000000000000", Encoding.Latin1.GetByteCount(wire).ToString("D12", CultureInfo.InvariantCulture), StringComparison.Ordinal);
        return FinTsReadParameterSet.Parse(FinTsParameterSet.Parse(FinTsResponse.Parse(FinTsMessageFrame.Parse(Encoding.Latin1.GetBytes(wire)))));
    }
}
