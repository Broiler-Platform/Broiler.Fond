using System.Text;
using Broiler.Fond.Kernel.Domain.Payments;
using Broiler.Fond.Kernel.FinTs;

namespace Broiler.Fond.Kernel.Tests.Payments;

public static class VerificationOfPayeeTests
{
    public static void RunAll()
    {
        int checkCount = 0;
        Action<bool, string> check = (condition, message) =>
        {
            if (!condition)
            {
                throw new InvalidOperationException($"VoP verification assertion failed: {message}");
            }
            checkCount++;
        };

        TestVerificationOfPayeeDomainStates(check);
        TestFinTsVopRequestWriter(check);
        TestFinTsVopResponseParser(check);
        TestFinTsVopAdvertisement(check);

        Console.WriteLine($"Verification of Payee (VoP) workflow (M4-02): {checkCount} checks completed successfully.");
    }

    private static void TestVerificationOfPayeeDomainStates(Action<bool, string> check)
    {
        var now = DateTimeOffset.UtcNow;

        // 1. Match
        var match = VerificationOfPayeeResult.CreateMatch("Erika Mustermann", now);
        check(match.Status == VerificationOfPayeeStatus.Match, "Match status matches.");
        check(match.SuppliedName == "Erika Mustermann", "Supplied name preserved.");
        check(match.CanProceed, "Match outcome can proceed to authorization.");
        check(!match.RequiresExplicitAcknowledgment, "Match does not require explicit warning acknowledgment.");
        check(match.CorrectedName is null, "Match has no corrected name.");

        // 2. Close Match
        var closeMatch = VerificationOfPayeeResult.CreateCloseMatch("Erika Mustermann", "Erika Musterfrau", null, now);
        check(closeMatch.Status == VerificationOfPayeeStatus.CloseMatch, "CloseMatch status matches.");
        check(closeMatch.SuppliedName == "Erika Mustermann", "Supplied name preserved.");
        check(closeMatch.CorrectedName == "Erika Musterfrau", "Corrected name preserved.");
        check(closeMatch.RequiresExplicitAcknowledgment, "CloseMatch requires explicit user review/acknowledgment.");
        check(closeMatch.CanProceed, "CloseMatch can proceed with user acknowledgment.");
        check(!string.IsNullOrWhiteSpace(closeMatch.WarningMessage), "CloseMatch produces descriptive warning.");

        // 3. No Match
        var noMatch = VerificationOfPayeeResult.CreateNoMatch("John Doe", "Account holder differs.", now);
        check(noMatch.Status == VerificationOfPayeeStatus.NoMatch, "NoMatch status matches.");
        check(noMatch.RequiresExplicitAcknowledgment, "NoMatch requires explicit user review/acknowledgment.");
        check(noMatch.CanProceed, "NoMatch can proceed with user acknowledgment (no hard block under Reg 2024/886).");
        check(noMatch.WarningMessage == "Account holder differs.", "NoMatch warning message retained.");

        // 4. Unavailable
        var unavail = VerificationOfPayeeResult.CreateUnavailable("Jane Doe", "Directory service down.", now);
        check(unavail.Status == VerificationOfPayeeStatus.Unavailable, "Unavailable status matches.");
        check(unavail.RequiresExplicitAcknowledgment, "Unavailable requires user acknowledgment.");
        check(unavail.CanProceed, "Unavailable can proceed with user acknowledgment.");
        check(unavail.WarningMessage == "Directory service down.", "Unavailable warning message retained.");

        // 5. Not Applicable
        var notApp = VerificationOfPayeeResult.CreateNotApplicable("Internal Account", "Own-account transfer", now);
        check(notApp.Status == VerificationOfPayeeStatus.NotApplicable, "NotApplicable status matches.");
        check(!notApp.RequiresExplicitAcknowledgment, "NotApplicable does not require warning acknowledgment.");
        check(notApp.CanProceed, "NotApplicable can proceed.");

        // 6. Pending
        var pending = new VerificationOfPayeeResult(VerificationOfPayeeStatus.Pending, "Max Mustermann", checkedAt: now);
        check(pending.Status == VerificationOfPayeeStatus.Pending, "Pending status matches.");
        check(!pending.CanProceed, "Pending check cannot proceed to authorization yet.");
    }

    private static void TestFinTsVopRequestWriter(Action<bool, string> check)
    {
        string originIban = "DE89370400440532013000";
        string originBic = "HELAADEF100";
        string payeeName = "Musterfirma GmbH";
        string payeeIban = "AT611904300234573201";
        string? payeeBic = null;
        decimal amount = 125.50m;
        string currency = "EUR";

        // 1. BuildSegment happy path
        string segment = FinTsVopRequestWriter.BuildSegment(
            ordinal: 2,
            version: 1,
            originIban: originIban,
            originBic: originBic,
            payeeName: payeeName,
            payeeIban: payeeIban,
            payeeBic: payeeBic,
            amount: amount,
            currency: currency);

        check(segment.StartsWith("HKVOP:2:1+", StringComparison.Ordinal), "Segment starts with HKVOP header.");
        check(segment.Contains("DE89370400440532013000:HELAADEF100+", StringComparison.Ordinal), "Origin account encoded properly.");
        check(segment.Contains("Musterfirma GmbH:AT611904300234573201:+", StringComparison.Ordinal), "Payee details encoded properly.");
        check(segment.EndsWith("125,50:EUR'", StringComparison.Ordinal), "Amount formatted with comma and EUR currency.");

        // 2. Encode complete 3-segment message
        byte[] wire = FinTsVopRequestWriter.Encode(
            dialogueId: "DIA-10023",
            messageNumber: 1,
            version: 1,
            originIban: originIban,
            originBic: originBic,
            payeeName: payeeName,
            payeeIban: payeeIban,
            payeeBic: payeeBic,
            amount: amount,
            currency: currency);

        check(wire.Length > 0, "Framed wire bytes produced.");
        string wireText = Encoding.Latin1.GetString(wire);
        check(wireText.Contains("HNHBK:1:3+"), "HNHBK header present in message frame.");
        check(wireText.Contains("HKVOP:2:1+"), "HKVOP segment present in message frame.");
        check(wireText.Contains("HNHBS:3:1+1'"), "HNHBS trailer present in message frame.");

        // 3. Validation rejection branches
        try
        {
            FinTsVopRequestWriter.BuildSegment(2, 3, originIban, originBic, payeeName, payeeIban, payeeBic, amount, currency);
            check(false, "Unsupported version should throw.");
        }
        catch (FinTsFormatException)
        {
            check(true, "Unsupported version rejected with FinTsFormatException.");
        }

        try
        {
            FinTsVopRequestWriter.BuildSegment(2, 1, "DE88370400440532013000", originBic, payeeName, payeeIban, payeeBic, amount, currency);
            check(false, "Invalid origin IBAN should throw.");
        }
        catch (ArgumentException)
        {
            check(true, "Invalid origin IBAN rejected.");
        }

        try
        {
            FinTsVopRequestWriter.BuildSegment(2, 1, originIban, originBic, payeeName, "AT611904300234573200", payeeBic, amount, currency);
            check(false, "Invalid payee IBAN should throw.");
        }
        catch (ArgumentException)
        {
            check(true, "Invalid payee IBAN rejected.");
        }

        try
        {
            FinTsVopRequestWriter.BuildSegment(2, 1, originIban, originBic, "  ", payeeIban, payeeBic, amount, currency);
            check(false, "Empty payee name should throw.");
        }
        catch (ArgumentException)
        {
            check(true, "Empty payee name rejected.");
        }

        try
        {
            FinTsVopRequestWriter.BuildSegment(2, 1, originIban, originBic, payeeName, payeeIban, payeeBic, -10m, currency);
            check(false, "Negative amount should throw.");
        }
        catch (ArgumentException)
        {
            check(true, "Negative amount rejected.");
        }

        try
        {
            FinTsVopRequestWriter.BuildSegment(2, 1, originIban, originBic, payeeName, payeeIban, payeeBic, amount, "USD");
            check(false, "Non-EUR currency should throw.");
        }
        catch (ArgumentException)
        {
            check(true, "Non-EUR currency rejected.");
        }
    }

    private static FinTsMessageFrame Frame(string wire) =>
        FinTsMessageFrame.Parse(Encoding.Latin1.GetBytes(wire[..10] + Encoding.Latin1.GetByteCount(wire).ToString("D12", System.Globalization.CultureInfo.InvariantCulture) + wire[22..]));

    private static void TestFinTsVopResponseParser(Action<bool, string> check)
    {
        // 1. HIVOP dedicated segment with code "1" (Match)
        string wireMatch = "HNHBK:1:3+000000000000+300+DIA-10023+1+DIA-10023:1'HIRMG:2:2+0010::Nachricht entgegengenommen.'HIVOP:3:1:2+DE89370400440532013000:HELAADEF100+1:::REF-1234'HNHBS:4:1+1'";
        var respMatch = FinTsResponse.Parse(Frame(wireMatch));
        var resultMatch = FinTsVopResponseParser.Parse(respMatch, "Alice Smith");
        check(resultMatch.Status == VerificationOfPayeeStatus.Match, "HIVOP code 1 decoded as Match.");
        check(resultMatch.CanProceed, "Match can proceed.");
        check(!resultMatch.RequiresExplicitAcknowledgment, "Match does not require acknowledgment.");

        // 2. HIVOP dedicated segment with code "2" (Close Match) and suggested name
        string wireClose = "HNHBK:1:3+000000000000+300+DIA-10023+1+DIA-10023:1'HIRMG:2:2+0010::Nachricht entgegengenommen.'HIVOP:3:1:2+DE89370400440532013000:HELAADEF100+2:Alice Schmitt:Namensabweichung:REF-1234'HNHBS:4:1+1'";
        var respClose = FinTsResponse.Parse(Frame(wireClose));
        var resultClose = FinTsVopResponseParser.Parse(respClose, "Alice Smith");
        check(resultClose.Status == VerificationOfPayeeStatus.CloseMatch, "HIVOP code 2 decoded as CloseMatch.");
        check(resultClose.CorrectedName == "Alice Schmitt", "Suggested corrected name extracted.");
        check(resultClose.RequiresExplicitAcknowledgment, "CloseMatch requires acknowledgment.");
        check(resultClose.CanProceed, "CloseMatch can proceed with acknowledgment.");

        // 3. HIVOP dedicated segment with code "3" (No Match)
        string wireNoMatch = "HNHBK:1:3+000000000000+300+DIA-10023+1+DIA-10023:1'HIRMG:2:2+0010::Nachricht entgegengenommen.'HIVOP:3:1:2+DE89370400440532013000:HELAADEF100+3::Empfaenger stimmt nicht ueberein:REF-1234'HNHBS:4:1+1'";
        var respNoMatch = FinTsResponse.Parse(Frame(wireNoMatch));
        var resultNoMatch = FinTsVopResponseParser.Parse(respNoMatch, "Alice Smith");
        check(resultNoMatch.Status == VerificationOfPayeeStatus.NoMatch, "HIVOP code 3 decoded as NoMatch.");
        check(resultNoMatch.RequiresExplicitAcknowledgment, "NoMatch requires acknowledgment.");
        check(resultNoMatch.CanProceed, "NoMatch can proceed with user acknowledgment.");

        // 4. HIVOP dedicated segment with code "4" (Unavailable)
        string wireUnavail = "HNHBK:1:3+000000000000+300+DIA-10023+1+DIA-10023:1'HIRMG:2:2+0010::Nachricht entgegengenommen.'HIVOP:3:1:2+DE89370400440532013000:HELAADEF100+4::Dienst temporaer nicht verfuegbar:REF-1234'HNHBS:4:1+1'";
        var respUnavail = FinTsResponse.Parse(Frame(wireUnavail));
        var resultUnavail = FinTsVopResponseParser.Parse(respUnavail, "Alice Smith");
        check(resultUnavail.Status == VerificationOfPayeeStatus.Unavailable, "HIVOP code 4 decoded as Unavailable.");
        check(resultUnavail.RequiresExplicitAcknowledgment, "Unavailable requires acknowledgment.");

        // 5. Correlated HIRMS reply codes (when no HIVOP segment present)
        // Code 3070 -> Match
        string wireHirms3070 = "HNHBK:1:3+000000000000+300+DIA-10023+1+DIA-10023:1'HIRMG:2:2+0010::Nachricht entgegengenommen.'HIRMS:3:2:2+3070::Empfaengerpruefung erfolgreich uebereinstimmend.'HNHBS:4:1+1'";
        var respHirms3070 = FinTsResponse.Parse(Frame(wireHirms3070));
        var result3070 = FinTsVopResponseParser.Parse(respHirms3070, "Alice Smith", requestSegmentOrdinal: 2);
        check(result3070.Status == VerificationOfPayeeStatus.Match, "HIRMS 3070 decoded as Match.");

        // Code 3071 -> Close Match
        string wireHirms3071 = "HNHBK:1:3+000000000000+300+DIA-10023+1+DIA-10023:1'HIRMG:2:2+0010::Nachricht entgegengenommen.'HIRMS:3:2:2+3071::Name weicht geringfuegig ab:Alice Schmitt'HNHBS:4:1+1'";
        var respHirms3071 = FinTsResponse.Parse(Frame(wireHirms3071));
        var result3071 = FinTsVopResponseParser.Parse(respHirms3071, "Alice Smith", requestSegmentOrdinal: 2);
        check(result3071.Status == VerificationOfPayeeStatus.CloseMatch, "HIRMS 3071 decoded as CloseMatch.");
        check(result3071.CorrectedName == "Alice Schmitt", "HIRMS 3071 parameter captured as suggested name.");

        // Code 3072 -> No Match
        string wireHirms3072 = "HNHBK:1:3+000000000000+300+DIA-10023+1+DIA-10023:1'HIRMG:2:2+0010::Nachricht entgegengenommen.'HIRMS:3:2:2+3072::Name weicht vom Kontoinhaber ab.'HNHBS:4:1+1'";
        var respHirms3072 = FinTsResponse.Parse(Frame(wireHirms3072));
        var result3072 = FinTsVopResponseParser.Parse(respHirms3072, "Alice Smith", requestSegmentOrdinal: 2);
        check(result3072.Status == VerificationOfPayeeStatus.NoMatch, "HIRMS 3072 decoded as NoMatch.");

        // Code 3073 -> Unavailable
        string wireHirms3073 = "HNHBK:1:3+000000000000+300+DIA-10023+1+DIA-10023:1'HIRMG:2:2+0010::Nachricht entgegengenommen.'HIRMS:3:2:2+3073::Empfaengerpruefung nicht verfuegbar.'HNHBS:4:1+1'";
        var respHirms3073 = FinTsResponse.Parse(Frame(wireHirms3073));
        var result3073 = FinTsVopResponseParser.Parse(respHirms3073, "Alice Smith", requestSegmentOrdinal: 2);
        check(result3073.Status == VerificationOfPayeeStatus.Unavailable, "HIRMS 3073 decoded as Unavailable.");
    }

    private static void TestFinTsVopAdvertisement(Action<bool, string> check)
    {
        // 1. Parameter set with HIVOPS advertisement
        string wireBpdWithVop = "HNHBK:1:3+000000000000+300+DIA-10023+1+DIA-10023:1'HIRMG:2:2+0010::Erfolgreich.'HIBPA:3:3:2+1+280:10070000+TestBank+1+1+300'HIVOPS:4:1:3+1+0+0'HNHBS:5:1+1'";
        var response = FinTsResponse.Parse(Frame(wireBpdWithVop));
        var paramSet = FinTsParameterSet.Parse(response);

        var vopAds = FinTsVopAdvertisement.ParseAll(paramSet);
        check(vopAds.Count == 1, "One HIVOPS advertisement found in parameter set.");
        check(vopAds[0].Version == 1, "HIVOPS version is 1.");
        check(vopAds[0].MaximumOrders == 1, "Maximum orders is 1.");
        check(vopAds[0].MinimumSignatures == 0, "Minimum signatures is 0.");

        // 2. Parameter set without HIVOPS
        string wireBpdNoVop = "HNHBK:1:3+000000000000+300+DIA-10023+1+DIA-10023:1'HIRMG:2:2+0010::Erfolgreich.'HIBPA:3:3:2+1+280:10070000+TestBank+1+1+300'HNHBS:4:1+1'";
        var respNoVop = FinTsResponse.Parse(Frame(wireBpdNoVop));
        var paramSetNoVop = FinTsParameterSet.Parse(respNoVop);

        var noAds = FinTsVopAdvertisement.ParseAll(paramSetNoVop);
        check(noAds.Count == 0, "Zero HIVOPS advertisements when unadvertised.");
    }
}
