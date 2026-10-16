using System.Globalization;
using System.Text;
using Broiler.Fond.Kernel.FinTs;

namespace Broiler.Fond.Kernel.Tests;

internal static class StatementCapabilitiesTests
{
    private const string Bank = "HIBPA:3+1+280:10020030+Bank+1+1+300";
    private const string User = "HIUPA:4+PUBLIC-USER+1+1";
    private const string Account = "HIUPD:6+PUBLIC-ACCOUNT::280:10020030+DE89370400440532013000+C+1+EUR+Owner++++HKSAL:2+HKSPA:2+HKKAZ:1+HKCAZ:1";

    internal static void Run(Action<bool, string> check)
    {
        int count = 0;
        void Verify(bool result, string message) { count++; check(result, message); }

        void Reject(params string[] parts)
        {
            try
            {
                _ = Parse(parts);
                Verify(false, "Invalid statement advertisement must be rejected.");
            }
            catch (FinTsFormatException error)
            {
                Verify(error.Error == FinTsSyntaxError.InvalidParameters || error.Error == FinTsSyntaxError.LimitExceeded,
                    "Statement schema failure must report fixed error categories.");
                Verify(!error.ToString().Contains("PUBLIC-", StringComparison.Ordinal),
                    "Statement schema errors must exclude supplied private fields.");
            }
        }

        // 1. MT940 HIKAZS v5, v6, v7
        // v5
        var mt940v5 = Parse(Bank, User, Account, "HIKAZS:5+99+1+0+J");
        var adV5 = mt940v5.Advertisements.Single();
        Verify(adV5.Operation == FinTsStatementOperation.Mt940 && adV5.Version == 5, "HIKAZS v5 must parse operation and version.");
        Verify(adV5.MaximumOrders == 99 && adV5.MinimumSignatures == 1 && adV5.SecurityClass == 0, "HIKAZS v5 common fields must parse.");
        Verify(adV5.TimeRangeAllowed && adV5.AllAccountsAllowed == null && adV5.PaginationTokenAllowed == null, "HIKAZS v5 has only time-range flag.");
        Verify(adV5.SupportedFormats.Count == 0, "HIKAZS v5 has no format descriptors.");

        // v6
        var mt940v6 = Parse(Bank, User, Account, "HIKAZS:6+50+1+2+J:N:J");
        var adV6 = mt940v6.Advertisements.Single();
        Verify(adV6.Operation == FinTsStatementOperation.Mt940 && adV6.Version == 6, "HIKAZS v6 must parse operation and version.");
        Verify(adV6.TimeRangeAllowed && adV6.AllAccountsAllowed == false && adV6.PaginationTokenAllowed == true, "HIKAZS v6 flags must parse.");

        // v7
        var mt940v7 = Parse(Bank, User, Account, "HIKAZS:7+10+1+0+J:J:J:MT940:SWIFT");
        var adV7 = mt940v7.Advertisements.Single();
        Verify(adV7.Operation == FinTsStatementOperation.Mt940 && adV7.Version == 7, "HIKAZS v7 must parse operation and version.");
        Verify(adV7.SupportedFormats.Count == 2 && adV7.SupportedFormats[0] == "MT940" && adV7.SupportedFormats[1] == "SWIFT", "HIKAZS v7 format codes must parse.");

        // 2. CAMT HICAZS v1, v2
        var camtV1 = Parse(Bank, User, Account, "HICAZS:1+20+1+0+J:N:J:camt.052.001.02:camt.053.001.02");
        var adCamt1 = camtV1.Advertisements.Single();
        Verify(adCamt1.Operation == FinTsStatementOperation.Camt && adCamt1.Version == 1, "HICAZS v1 must parse operation and version.");
        Verify(adCamt1.TimeRangeAllowed && adCamt1.AllAccountsAllowed == false && adCamt1.PaginationTokenAllowed == true, "HICAZS v1 flags must parse.");
        Verify(adCamt1.SupportedFormats.Count == 2 && adCamt1.SupportedFormats[1] == "camt.053.001.02", "HICAZS v1 formats must parse.");

        var camtV2 = Parse(Bank, User, Account, "HICAZS:2+30+1+0+J:J:J:urn?:iso?:std?:iso?:20022?:tech?:xsd?:camt.053.001.02:camt.053.001.08");
        var adCamt2 = camtV2.Advertisements.Single();
        Verify(adCamt2.Operation == FinTsStatementOperation.Camt && adCamt2.Version == 2, "HICAZS v2 must parse operation and version.");
        Verify(adCamt2.SupportedFormats.Count == 2 && adCamt2.SupportedFormats[0] == "urn:iso:std:iso:20022:tech:xsd:camt.053.001.02", "HICAZS unescaping of URN colons must succeed.");

        // 3. Rejection of malformed parameter segments
        Reject("HIKAZS:5+1+1"); // missing fields
        Reject("HIKAZS:5+1+1+0+J:N"); // v5 with 2 options
        Reject("HIKAZS:6+1+1+0+J"); // v6 with 1 option
        Reject("HIKAZS:6+1+1+0+J:N:J:EXTRA"); // v6 with 4 options
        Reject("HIKAZS:7+1+1+0+J:N"); // v7 with < 3 options
        Reject("HICAZS:1+1+1+0+J:N"); // camt v1 with < 3 options
        Reject("HICAZS:2+1+1+0+J:N"); // camt v2 with < 3 options
        Reject("HIKAZS:7+1000+1+0+J:J:J"); // number overflow > 3 digits
        Reject("HIKAZS:7+1+4+0+J:J:J"); // signatures > 3
        Reject("HIKAZS:7+1+1+5+J:J:J"); // security class > 4
        Reject("HIKAZS:7+1+1+0+X:J:J"); // invalid flag character
        Reject("HIKAZS:7+1+1+0+J:J:J" + string.Concat(Enumerable.Repeat(":FMT", 100))); // formats > 99
        Reject("HICAZS:1+1+1+0+J:J:J" + string.Concat(Enumerable.Repeat(":camt.053", 100))); // camt formats > 99
        Reject(Enumerable.Repeat("HIKAZS:7+1+1+0+J:J:J", 129).ToArray()); // advertisements > 128

        // Unknown versions preserved in uninterpreted segments
        var future = Parse(Bank, User, Account, "HIKAZS:8+opaque", "HICAZS:3+opaque", "HIKAZS:7+1+1+0+J:J:J");
        Verify(future.Advertisements.Count == 1 && future.UninterpretedSegments.Count == 2, "Unknown statement versions must be preserved as uninterpreted segments.");

        // 4. Statement Capability Evidence Evaluation (MT940)
        var mtEvidence = FinTsStatementCapabilityEvidence.Evaluate(mt940v7, mt940v7.Source.Accounts[0], FinTsStatementOperation.Mt940, 7);
        Verify(mtEvidence.HasMatchingEvidence, "Valid MT940 evidence must match.");
        Verify(mtEvidence.MinimumCustomerSignatures == 1, "Customer signatures must match account permission requirement.");

        // 5. Statement Capability Evidence Evaluation (CAMT)
        var camtEvidence = FinTsStatementCapabilityEvidence.Evaluate(camtV2, camtV2.Source.Accounts[0], FinTsStatementOperation.Camt, 2, "camt.053.001.02");
        Verify(camtEvidence.HasMatchingEvidence, "Valid CAMT evidence with supported format must match.");
        Verify(camtEvidence.RequestedFormat == "camt.053.001.02", "Requested format must be recorded.");

        // CAMT with unsupported format
        var unsupportedFormat = FinTsStatementCapabilityEvidence.Evaluate(camtV2, camtV2.Source.Accounts[0], FinTsStatementOperation.Camt, 2, "camt.054.001.02");
        Verify(!unsupportedFormat.HasMatchingEvidence && unsupportedFormat.Issues.HasFlag(FinTsStatementEvidenceIssue.UnsupportedCamtFormat),
            "Unsupported CAMT format must set UnsupportedCamtFormat issue.");

        // CAMT without IBAN
        var noIbanAccount = Account.Replace("DE89370400440532013000", "", StringComparison.Ordinal);
        var camtNoIban = Parse(Bank, User, noIbanAccount, "HICAZS:2+30+1+0+J:J:J:camt.053.001.02");
        var noIbanEvidence = FinTsStatementCapabilityEvidence.Evaluate(camtNoIban, camtNoIban.Source.Accounts[0], FinTsStatementOperation.Camt, 2, "camt.053.001.02");
        Verify(!noIbanEvidence.HasMatchingEvidence && noIbanEvidence.Issues.HasFlag(FinTsStatementEvidenceIssue.MissingInternationalIdentity),
            "CAMT without IBAN must flag MissingInternationalIdentity.");

        // Diagnostic scenarios for MT940
        (string[] Parts, FinTsStatementEvidenceIssue ExpectedIssue)[] scenarios =
        [
            ([User, Account, "HIKAZS:7+1+1+0+J:J:J"], FinTsStatementEvidenceIssue.MissingBankParameters),
            ([Bank.Replace("+300", "+220", StringComparison.Ordinal), User, Account, "HIKAZS:7+1+1+0+J:J:J"], FinTsStatementEvidenceIssue.Protocol300NotAdvertised),
            ([Bank, Account, "HIKAZS:7+1+1+0+J:J:J"], FinTsStatementEvidenceIssue.MissingUserParameters),
            ([Bank, User, Account.Replace("HKKAZ:1", "HKXYZ:1", StringComparison.Ordinal), "HIKAZS:7+1+1+0+J:J:J"], FinTsStatementEvidenceIssue.PermissionUnknown),
            ([Bank, "HIUPA:4+PUBLIC-USER+1+0", Account.Replace("HKKAZ:1", "HKXYZ:1", StringComparison.Ordinal), "HIKAZS:7+1+1+0+J:J:J"], FinTsStatementEvidenceIssue.PermissionBlocked),
            ([Bank, User, Account + "+HKKAZ:1", "HIKAZS:7+1+1+0+J:J:J"], FinTsStatementEvidenceIssue.PermissionAmbiguous),
            ([Bank, User, Account], FinTsStatementEvidenceIssue.MissingAdvertisement),
            ([Bank, User, Account, "HIKAZS:7+1+1+0+J:J:J", "HIKAZS:7+1+2+0+J:J:J"], FinTsStatementEvidenceIssue.DuplicateAdvertisement),
            ([Bank, User, Account, "HIKAZS:7+1+3+0+J:J:J"], FinTsStatementEvidenceIssue.SignatureConflict),
            ([Bank, User, Account, Account, "HIKAZS:7+1+1+0+J:J:J"], FinTsStatementEvidenceIssue.DuplicateAccountIdentity),
            ([Bank, User, Account, "HIKAZS:7+0+1+0+J:J:J"], FinTsStatementEvidenceIssue.ZeroOrderCapacity),
            ([Bank, User, "HIUPD:6+++C+++++++HKKAZ:1", "HIKAZS:7+1+1+0+J:J:J"], FinTsStatementEvidenceIssue.MissingAccountConnection),
            ([Bank, User, Account.Replace("10020030", "10020031", StringComparison.Ordinal), "HIKAZS:7+1+1+0+J:J:J"], FinTsStatementEvidenceIssue.InstitutionContextMismatch),
        ];

        foreach (var scenario in scenarios)
        {
            var parsed = Parse(scenario.Parts);
            var evidence = FinTsStatementCapabilityEvidence.Evaluate(parsed, parsed.Source.Accounts[0], FinTsStatementOperation.Mt940, 7);
            Verify(!evidence.HasMatchingEvidence && evidence.Issues.HasFlag(scenario.ExpectedIssue),
                $"Evidence issue {scenario.ExpectedIssue} must be correctly flagged.");
        }

        // 6. Format Preference Negotiation
        // Case A: Both CAMT v2 and MT940 v7 advertised and permitted -> CAMT v2 selected, MT940 alternate
        var both = Parse(Bank, User, Account,
            "HICAZS:2+30+1+0+J:J:J:camt.053.001.02",
            "HIKAZS:7+10+1+0+J:J:J");
        var negBoth = FinTsStatementPreferenceNegotiator.Negotiate(both, both.Source.Accounts[0]);
        Verify(negBoth.HasMatchingEvidence, "Negotiation with both formats must find matching evidence.");
        Verify(negBoth.SelectedOperation == FinTsStatementOperation.Camt && negBoth.SelectedVersion == 2, "CAMT v2 must be preferred over MT940.");
        Verify(negBoth.AlternateEvidence is { HasMatchingEvidence: true, Operation: FinTsStatementOperation.Mt940, Version: 7 },
            "MT940 v7 must be recorded as valid alternate evidence.");

        // Case B: CAMT supported but requested format is not advertised -> fallback to MT940
        var negCamtUnsupportedFmt = FinTsStatementPreferenceNegotiator.Negotiate(both, both.Source.Accounts[0], preferredCamtFormat: "camt.054.001.02");
        Verify(negCamtUnsupportedFmt.HasMatchingEvidence, "When CAMT format is unsupported, fallback to MT940 must succeed.");
        Verify(negCamtUnsupportedFmt.SelectedOperation == FinTsStatementOperation.Mt940 && negCamtUnsupportedFmt.SelectedVersion == 7,
            "MT940 must be selected as fallback.");
        Verify(negCamtUnsupportedFmt.AlternateEvidence == null, "No alternate evidence when fallback is MT940.");

        // Case C: Only MT940 advertised
        var onlyMt = Parse(Bank, User, Account, "HIKAZS:7+10+1+0+J:J:J");
        var negOnlyMt = FinTsStatementPreferenceNegotiator.Negotiate(onlyMt, onlyMt.Source.Accounts[0]);
        Verify(negOnlyMt.HasMatchingEvidence && negOnlyMt.SelectedOperation == FinTsStatementOperation.Mt940,
            "When only MT940 is advertised, MT940 must be selected.");
        Verify(negOnlyMt.AlternateEvidence == null, "Alternate evidence is null when only MT940 exists.");

        // Case D: Only CAMT advertised
        var onlyCamt = Parse(Bank, User, Account, "HICAZS:1+20+1+0+J:N:J:camt.053.001.02");
        var negOnlyCamt = FinTsStatementPreferenceNegotiator.Negotiate(onlyCamt, onlyCamt.Source.Accounts[0]);
        Verify(negOnlyCamt.HasMatchingEvidence && negOnlyCamt.SelectedOperation == FinTsStatementOperation.Camt && negOnlyCamt.SelectedVersion == 1,
            "When only CAMT v1 is advertised, CAMT v1 must be selected.");

        // Case E: Neither matches (e.g. no statement advertisements)
        var none = Parse(Bank, User, Account);
        var negNone = FinTsStatementPreferenceNegotiator.Negotiate(none, none.Source.Accounts[0]);
        Verify(!negNone.HasMatchingEvidence, "When no advertisements exist, negotiation must report no match.");
        Verify(negNone.SelectedEvidence != null && negNone.SelectedEvidence.Issues.HasFlag(FinTsStatementEvidenceIssue.MissingAdvertisement),
            "Non-matching negotiation must retain diagnostic evidence.");

        // 7. Cancellation token testing
        try
        {
            _ = FinTsStatementParameterSet.Parse(both.Source, new CancellationToken(true));
            Verify(false, "Cancellation token must cancel parameter parsing.");
        }
        catch (OperationCanceledException)
        {
            Verify(true, "Parameter parsing cancellation token respected.");
        }

        try
        {
            _ = FinTsStatementCapabilityEvidence.Evaluate(both, both.Source.Accounts[0], FinTsStatementOperation.Mt940, 7, cancellationToken: new CancellationToken(true));
            Verify(false, "Cancellation token must cancel capability evaluation.");
        }
        catch (OperationCanceledException)
        {
            Verify(true, "Capability evaluation cancellation token respected.");
        }

        try
        {
            _ = FinTsStatementPreferenceNegotiator.Negotiate(both, both.Source.Accounts[0], cancellationToken: new CancellationToken(true));
            Verify(false, "Cancellation token must cancel negotiation.");
        }
        catch (OperationCanceledException)
        {
            Verify(true, "Negotiation cancellation token respected.");
        }

        // 8. Defensive argument validation
        try
        {
            _ = FinTsStatementCapabilityEvidence.Evaluate(both, both.Source.Accounts[0], (FinTsStatementOperation)99, 7);
            Verify(false, "Invalid operation enum must throw.");
        }
        catch (FinTsFormatException)
        {
            Verify(true, "Invalid operation enum throws safely.");
        }

        try
        {
            _ = FinTsStatementCapabilityEvidence.Evaluate(both, both.Source.Accounts[0], FinTsStatementOperation.Mt940, 0);
            Verify(false, "Invalid version 0 must throw.");
        }
        catch (FinTsFormatException)
        {
            Verify(true, "Invalid version 0 throws safely.");
        }

        // 9. Sentinel secret checking in diagnostics
        Verify(new object[] { both, both.Advertisements[0], mtEvidence, camtEvidence, negBoth }
            .All(o => !o.ToString()!.Contains("PUBLIC-", StringComparison.Ordinal)),
            "Default statement capability diagnostics must exclude private source identifiers.");

        Console.WriteLine($"FinTS statement capabilities and preference negotiation: {count} checks completed.");
    }

    private static FinTsStatementParameterSet Parse(params string[] parts)
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
        return FinTsStatementParameterSet.Parse(FinTsParameterSet.Parse(FinTsResponse.Parse(FinTsMessageFrame.Parse(Encoding.Latin1.GetBytes(wire)))));
    }
}
