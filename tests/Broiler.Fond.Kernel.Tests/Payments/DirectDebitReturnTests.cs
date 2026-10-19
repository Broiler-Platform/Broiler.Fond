using System.Globalization;
using System.Text;
using Broiler.Fond.Kernel.Domain;
using Broiler.Fond.Kernel.Domain.Payments;
using Broiler.Fond.Kernel.FinTs;

namespace Broiler.Fond.Kernel.Tests.Payments;

public static class DirectDebitReturnTests
{
    public static void RunAll()
    {
        int checkCount = 0;
        Action<bool, string> check = (condition, message) =>
        {
            if (!condition)
            {
                throw new InvalidOperationException($"Direct-debit return assertion failed: {message}");
            }
            checkCount++;
        };

        TestDirectDebitReturnDomainModel(check);
        TestStatutoryDeadlineEnforcement(check);
        TestDirectDebitReturnCapability(check);
        TestFinTsDirectDebitReturnAdvertisement(check);
        TestDirectDebitReturnValidation(check);
        TestMandateRevocationValidation(check);
        TestFinTsDirectDebitReturnWireCodecs(check);
        TestFinTsDirectDebitReturnResponseParser(check);

        Console.WriteLine($"Direct-debit return writes & mandate revocation (M5-04): {checkCount} checks completed successfully.");
    }

    private static void TestDirectDebitReturnDomainModel(Action<bool, string> check)
    {
        // 1. Reason code mapping
        check(DirectDebitReturnReason.DebtorRequestUnconditional.ToReasonCode() == "MS02", "MS02 maps to DebtorRequestUnconditional.");
        check(DirectDebitReturnReason.DebtorRequestGeneral.ToReasonCode() == "MS03", "MS03 maps to DebtorRequestGeneral.");
        check(DirectDebitReturnReason.NoValidMandate.ToReasonCode() == "MD01", "MD01 maps to NoValidMandate.");
        check(DirectDebitReturnReason.DisputedTransaction.ToReasonCode() == "MD06", "MD06 maps to DisputedTransaction.");

        check(DirectDebitReturnReasonExtensions.FromReasonCode("MS02") == DirectDebitReturnReason.DebtorRequestUnconditional, "FromReasonCode parses MS02.");
        check(DirectDebitReturnReasonExtensions.FromReasonCode("ms02") == DirectDebitReturnReason.DebtorRequestUnconditional, "FromReasonCode parses case-insensitive ms02.");
        check(DirectDebitReturnReasonExtensions.FromReasonCode("MD01") == DirectDebitReturnReason.NoValidMandate, "FromReasonCode parses MD01.");
        check(DirectDebitReturnReasonExtensions.FromReasonCode("MD06") == DirectDebitReturnReason.DisputedTransaction, "FromReasonCode parses MD06.");
        check(DirectDebitReturnReasonExtensions.FromReasonCode("MS03") == DirectDebitReturnReason.DebtorRequestGeneral, "FromReasonCode parses MS03.");

        bool threwOnInvalidCode = false;
        try { DirectDebitReturnReasonExtensions.FromReasonCode("INVALID"); }
        catch (ArgumentException) { threwOnInvalidCode = true; }
        check(threwOnInvalidCode, "FromReasonCode throws on invalid code.");

        // 2. IsAuthorizedReturn
        check(DirectDebitReturnReason.DebtorRequestUnconditional.IsAuthorizedReturn(), "MS02 is an authorized return.");
        check(DirectDebitReturnReason.DebtorRequestGeneral.IsAuthorizedReturn(), "MS03 is an authorized return.");
        check(DirectDebitReturnReason.DisputedTransaction.IsAuthorizedReturn(), "MD06 is an authorized return.");
        check(!DirectDebitReturnReason.NoValidMandate.IsAuthorizedReturn(), "MD01 is an unauthorized return (no valid mandate).");

        // 3. Status enum
        check((int)DirectDebitReturnStatus.PendingSubmission == 1, "PendingSubmission status is 1.");
        check((int)DirectDebitReturnStatus.SubmittedAwaitingStatement == 2, "SubmittedAwaitingStatement status is 2.");
        check((int)DirectDebitReturnStatus.Reconciled == 3, "Reconciled status is 3.");
        check((int)DirectDebitReturnStatus.Rejected == 4, "Rejected status is 4.");
        check((int)DirectDebitReturnStatus.Unknown == 5, "Unknown status is 5.");

        // 4. Request record
        DateTimeOffset now = DateTimeOffset.UtcNow;
        DateOnly bookingDate = new(2026, 10, 1);
        DateOnly dueDate = new(2026, 10, 1);

        DirectDebitReturnRequest request = new(
            Id: 801,
            SourceAccountId: 10,
            SourceAccountBindingId: 20,
            SourceAccountIncarnationId: 30,
            OriginalBankReference: "TX-REF-8899",
            CreditorId: "DE98ZZZ09999999999",
            MandateId: "MANDATE-4422",
            CreditorName: "Telecom Corp",
            CreditorIban: "DE12500105170648489890",
            Amount: new Money(69.95m, "EUR"),
            OriginalBookingDate: bookingDate,
            OriginalDueDate: dueDate,
            Reason: DirectDebitReturnReason.DebtorRequestUnconditional,
            Status: DirectDebitReturnStatus.PendingSubmission,
            CreatedAt: now);

        check(request.CanSubmit, "Pending submission request CanSubmit is true.");
        check(!request.IsTerminal, "Pending submission request IsTerminal is false.");

        DirectDebitReturnRequest submitted = request with { Status = DirectDebitReturnStatus.SubmittedAwaitingStatement };
        check(!submitted.CanSubmit, "Submitted request CanSubmit is false.");
        check(!submitted.IsTerminal, "Submitted awaiting statement is not terminal.");

        DirectDebitReturnRequest reconciled = request with { Status = DirectDebitReturnStatus.Reconciled };
        check(reconciled.IsTerminal, "Reconciled request is terminal.");

        DirectDebitReturnRequest rejected = request with { Status = DirectDebitReturnStatus.Rejected };
        check(rejected.IsTerminal, "Rejected request is terminal.");
    }

    private static void TestStatutoryDeadlineEnforcement(Action<bool, string> check)
    {
        DateOnly debitDate = new(2026, 9, 1);

        // 1. Authorized 8-week (56 calendar days) returns: MS02, MS03, MD06
        DateOnly within56Days = debitDate.AddDays(56);
        DateOnly day57 = debitDate.AddDays(57);
        DateOnly futureDate = debitDate.AddDays(-1);

        check(DirectDebitStatutoryDeadline.IsWithinStatutoryDeadline(DirectDebitReturnReason.DebtorRequestUnconditional, debitDate, debitDate),
            "Day 0 is within 8-week deadline.");
        check(DirectDebitStatutoryDeadline.IsWithinStatutoryDeadline(DirectDebitReturnReason.DebtorRequestUnconditional, debitDate, within56Days),
            "Day 56 is within 8-week deadline.");
        check(!DirectDebitStatutoryDeadline.IsWithinStatutoryDeadline(DirectDebitReturnReason.DebtorRequestUnconditional, debitDate, day57),
            "Day 57 exceeds 8-week deadline.");
        check(!DirectDebitStatutoryDeadline.IsWithinStatutoryDeadline(DirectDebitReturnReason.DebtorRequestUnconditional, debitDate, futureDate),
            "Evaluation date before booking date is rejected.");

        check(DirectDebitStatutoryDeadline.GetDeadlineDate(DirectDebitReturnReason.DebtorRequestUnconditional, debitDate) == within56Days,
            "Deadline date for MS02 is exactly debitDate + 56 days.");
        check(DirectDebitStatutoryDeadline.GetAllowedDays(DirectDebitReturnReason.DebtorRequestUnconditional) == 56,
            "Allowed days for MS02 is 56.");

        // 2. Unauthorized 13-month return: MD01
        DateOnly within13Months = debitDate.AddMonths(13);
        DateOnly dayAfter13Months = within13Months.AddDays(1);

        check(DirectDebitStatutoryDeadline.IsWithinStatutoryDeadline(DirectDebitReturnReason.NoValidMandate, debitDate, within56Days),
            "Day 56 is within 13-month deadline.");
        check(DirectDebitStatutoryDeadline.IsWithinStatutoryDeadline(DirectDebitReturnReason.NoValidMandate, debitDate, day57),
            "Day 57 is within 13-month deadline for MD01.");
        check(DirectDebitStatutoryDeadline.IsWithinStatutoryDeadline(DirectDebitReturnReason.NoValidMandate, debitDate, within13Months),
            "Day at 13 months is within deadline for MD01.");
        check(!DirectDebitStatutoryDeadline.IsWithinStatutoryDeadline(DirectDebitReturnReason.NoValidMandate, debitDate, dayAfter13Months),
            "Day after 13 months exceeds deadline for MD01.");

        check(DirectDebitStatutoryDeadline.GetDeadlineDate(DirectDebitReturnReason.NoValidMandate, debitDate) == within13Months,
            "Deadline date for MD01 is debitDate + 13 months.");
        check(DirectDebitStatutoryDeadline.GetAllowedDays(DirectDebitReturnReason.NoValidMandate) == 396,
            "Allowed days for MD01 is 396.");
    }

    private static void TestDirectDebitReturnCapability(Action<bool, string> check)
    {
        DirectDebitReturnCapability def = DirectDebitReturnCapability.StandardDefault;
        check(def.IsSupported, "Default capability is supported.");
        check(def.SupportsMandateRevocation, "Default capability supports mandate revocation.");
        check(def.SupportedReasons.Count == 4, "Default capability supports 4 standard reasons.");
        check(def.IsReasonSupported(DirectDebitReturnReason.DebtorRequestUnconditional), "Supports MS02.");
        check(def.IsReasonSupported(DirectDebitReturnReason.NoValidMandate), "Supports MD01.");

        DirectDebitReturnCapability unsup = DirectDebitReturnCapability.Unsupported;
        check(!unsup.IsSupported, "Unsupported IsSupported is false.");
        check(!unsup.SupportsMandateRevocation, "Unsupported SupportsMandateRevocation is false.");
        check(unsup.SupportedReasons.Count == 0, "Unsupported SupportedReasons is empty.");
        check(!unsup.IsReasonSupported(DirectDebitReturnReason.DebtorRequestUnconditional), "Unsupported returns false for reason.");

        DirectDebitReturnCapability limited = new(
            IsSupported: true,
            SupportedReasons: new HashSet<DirectDebitReturnReason> { DirectDebitReturnReason.DebtorRequestUnconditional },
            SupportsMandateRevocation: false);
        check(limited.IsReasonSupported(DirectDebitReturnReason.DebtorRequestUnconditional), "Limited supports MS02.");
        check(!limited.IsReasonSupported(DirectDebitReturnReason.NoValidMandate), "Limited does not support MD01.");
        check(!limited.SupportsMandateRevocation, "Limited does not support mandate revocation.");
    }

    private static void TestFinTsDirectDebitReturnAdvertisement(Action<bool, string> check)
    {
        // 1. Response with HIDRWS and HIDMBS
        string wireFull =
            "HNHBK:1:3+000000000000+300+DIA-10023+1+DIA-10023:1'" +
            "HIRMG:2:2+0010::Erfolgreich.'" +
            "HIDRWS:3:1:2+1+1+0+MS02:MD01'" +
            "HIDMBS:4:1:2+1+1+0'" +
            "HNHBS:5:1+1'";

        FinTsResponse respFull = FinTsResponse.Parse(Frame(wireFull));
        DirectDebitReturnCapability capFull = FinTsDirectDebitReturnAdvertisement.DiscoverCapability(respFull);

        check(capFull.IsSupported, "Discovered capability is supported.");
        check(capFull.SupportsMandateRevocation, "Discovered capability supports mandate revocation.");
        check(capFull.SupportedReasons.Count == 2, "Discovered capability has 2 reasons from BPD.");
        check(capFull.IsReasonSupported(DirectDebitReturnReason.DebtorRequestUnconditional), "Supports MS02.");
        check(capFull.IsReasonSupported(DirectDebitReturnReason.NoValidMandate), "Supports MD01.");
        check(!capFull.IsReasonSupported(DirectDebitReturnReason.DisputedTransaction), "Does not support MD06.");

        // 2. Response with HIDRWS without explicit reason list (defaults to all)
        string wireNoReasons =
            "HNHBK:1:3+000000000000+300+DIA-10023+1+DIA-10023:1'" +
            "HIRMG:2:2+0010::Erfolgreich.'" +
            "HIDRWS:3:1:2+1+1+0'" +
            "HNHBS:4:1+1'";

        FinTsResponse respNoReasons = FinTsResponse.Parse(Frame(wireNoReasons));
        DirectDebitReturnCapability capNoReasons = FinTsDirectDebitReturnAdvertisement.DiscoverCapability(respNoReasons);
        check(capNoReasons.IsSupported, "Capability with empty reason list is supported.");
        check(capNoReasons.SupportedReasons.Count == 4, "Capability with empty reason list defaults to 4 standard reasons.");
        check(!capNoReasons.SupportsMandateRevocation, "No HIDMBS means no mandate revocation.");

        // 3. Response without HIDRWS
        string wireNone =
            "HNHBK:1:3+000000000000+300+DIA-10023+1+DIA-10023:1'" +
            "HIRMG:2:2+0010::Erfolgreich.'" +
            "HNHBS:3:1+1'";

        FinTsResponse respNone = FinTsResponse.Parse(Frame(wireNone));
        DirectDebitReturnCapability capNone = FinTsDirectDebitReturnAdvertisement.DiscoverCapability(respNone);
        check(!capNone.IsSupported, "Missing HIDRWS results in unsupported capability.");
    }

    private static void TestDirectDebitReturnValidation(Action<bool, string> check)
    {
        DateOnly bookingDate = new(2026, 10, 1);
        DateOnly evalDate = new(2026, 10, 15); // 14 days later, well within 56 days

        DirectDebitReturnRequest valid = new(
            Id: 801,
            SourceAccountId: 10,
            SourceAccountBindingId: 20,
            SourceAccountIncarnationId: 30,
            OriginalBankReference: "TX-REF-9900",
            CreditorId: "DE98ZZZ09999999999",
            MandateId: "MANDATE-4422",
            CreditorName: "Telecom Corp",
            CreditorIban: "DE12500105170648489890",
            Amount: new Money(45.50m, "EUR"),
            OriginalBookingDate: bookingDate,
            OriginalDueDate: bookingDate,
            Reason: DirectDebitReturnReason.DebtorRequestUnconditional,
            Status: DirectDebitReturnStatus.PendingSubmission,
            CreatedAt: DateTimeOffset.UtcNow);

        // 1. Valid request
        var resValid = DirectDebitReturnValidator.Validate(valid, DirectDebitReturnCapability.StandardDefault, evalDate);
        check(resValid.IsValid, "Valid request passes validation.");
        check(resValid.Errors.Count == 0, "No errors on valid request.");

        // 2. Safe mode active
        var resSafe = DirectDebitReturnValidator.Validate(valid, DirectDebitReturnCapability.StandardDefault, evalDate, isSafeModeActive: true);
        check(!resSafe.IsValid, "Safe mode active fails validation.");
        check(resSafe.Errors.Any(e => e.Code == SepaValidationErrorCode.DirectDebitReturnSafeModeActive), "Fails with DirectDebitReturnSafeModeActive.");

        // 3. Unsupported capability
        var resUnsup = DirectDebitReturnValidator.Validate(valid, DirectDebitReturnCapability.Unsupported, evalDate);
        check(!resUnsup.IsValid, "Unsupported capability fails validation.");
        check(resUnsup.Errors.Any(e => e.Code == SepaValidationErrorCode.DirectDebitReturnCapabilityNotSupported), "Fails with DirectDebitReturnCapabilityNotSupported.");

        // 4. Unsupported reason
        var limitedCap = new DirectDebitReturnCapability(
            IsSupported: true,
            SupportedReasons: new HashSet<DirectDebitReturnReason> { DirectDebitReturnReason.NoValidMandate },
            SupportsMandateRevocation: true);
        var resReason = DirectDebitReturnValidator.Validate(valid, limitedCap, evalDate);
        check(!resReason.IsValid, "Unsupported reason fails validation.");
        check(resReason.Errors.Any(e => e.Code == SepaValidationErrorCode.DirectDebitReturnReasonNotSupported), "Fails with DirectDebitReturnReasonNotSupported.");

        // 5. Expired statutory deadline (e.g. 60 days later for MS02)
        DateOnly expiredDate = bookingDate.AddDays(60);
        var resExpired = DirectDebitReturnValidator.Validate(valid, DirectDebitReturnCapability.StandardDefault, expiredDate);
        check(!resExpired.IsValid, "Expired 8-week deadline fails validation.");
        check(resExpired.Errors.Any(e => e.Code == SepaValidationErrorCode.DirectDebitStatutoryDeadlineExceeded), "Fails with DirectDebitStatutoryDeadlineExceeded.");

        // 6. Invalid creditor ID (empty or too short)
        var invalidCreditor = valid with { CreditorId = "ABC" };
        var resCred = DirectDebitReturnValidator.Validate(invalidCreditor, DirectDebitReturnCapability.StandardDefault, evalDate);
        check(!resCred.IsValid, "Short creditor ID fails validation.");
        check(resCred.Errors.Any(e => e.Code == SepaValidationErrorCode.DirectDebitInvalidCreditorId), "Fails with DirectDebitInvalidCreditorId.");

        // 7. Missing mandate ID
        var missingMandate = valid with { MandateId = "  " };
        var resMandate = DirectDebitReturnValidator.Validate(missingMandate, DirectDebitReturnCapability.StandardDefault, evalDate);
        check(!resMandate.IsValid, "Missing mandate ID fails validation.");
        check(resMandate.Errors.Any(e => e.Code == SepaValidationErrorCode.DirectDebitMissingMandateId), "Fails with DirectDebitMissingMandateId.");

        // 8. Invalid currency
        var badCurrency = valid with { Amount = new Money(45.50m, "USD") };
        var resCurr = DirectDebitReturnValidator.Validate(badCurrency, DirectDebitReturnCapability.StandardDefault, evalDate);
        check(!resCurr.IsValid, "Non-EUR currency fails validation.");
        check(resCurr.Errors.Any(e => e.Code == SepaValidationErrorCode.InvalidCurrency), "Fails with InvalidCurrency.");

        // 9. Zero or negative amount
        var zeroAmount = valid with { Amount = new Money(0.00m, "EUR") };
        var resZero = DirectDebitReturnValidator.Validate(zeroAmount, DirectDebitReturnCapability.StandardDefault, evalDate);
        check(!resZero.IsValid, "Zero amount fails validation.");
        check(resZero.Errors.Any(e => e.Code == SepaValidationErrorCode.InvalidAmount), "Fails with InvalidAmount.");

        // 10. Scale > 2
        var badScale = valid with { Amount = new Money(45.505m, "EUR") };
        var resScale = DirectDebitReturnValidator.Validate(badScale, DirectDebitReturnCapability.StandardDefault, evalDate);
        check(!resScale.IsValid, "Amount scale > 2 fails validation.");
        check(resScale.Errors.Any(e => e.Code == SepaValidationErrorCode.AmountExceedsScale), "Fails with AmountExceedsScale.");
    }

    private static void TestMandateRevocationValidation(Action<bool, string> check)
    {
        string creditorId = "DE98ZZZ09999999999";
        string mandateId = "MANDATE-8822";

        // 1. Valid revocation
        var resValid = DirectDebitReturnValidator.ValidateMandateRevocation(creditorId, mandateId, DirectDebitReturnCapability.StandardDefault);
        check(resValid.IsValid, "Valid mandate revocation passes validation.");

        // 2. Safe mode active
        var resSafe = DirectDebitReturnValidator.ValidateMandateRevocation(creditorId, mandateId, DirectDebitReturnCapability.StandardDefault, isSafeModeActive: true);
        check(!resSafe.IsValid, "Safe mode active blocks mandate revocation.");
        check(resSafe.Errors.Any(e => e.Code == SepaValidationErrorCode.DirectDebitReturnSafeModeActive), "Fails with DirectDebitReturnSafeModeActive.");

        // 3. Unsupported capability
        var capNoMandate = new DirectDebitReturnCapability(IsSupported: true, SupportedReasons: new HashSet<DirectDebitReturnReason>(), SupportsMandateRevocation: false);
        var resUnsup = DirectDebitReturnValidator.ValidateMandateRevocation(creditorId, mandateId, capNoMandate);
        check(!resUnsup.IsValid, "Unsupported mandate capability blocks revocation.");
        check(resUnsup.Errors.Any(e => e.Code == SepaValidationErrorCode.DirectDebitMandateRevocationNotSupported), "Fails with DirectDebitMandateRevocationNotSupported.");

        // 4. Invalid creditor ID
        var resCred = DirectDebitReturnValidator.ValidateMandateRevocation("123", mandateId, DirectDebitReturnCapability.StandardDefault);
        check(!resCred.IsValid, "Invalid creditor ID fails mandate revocation validation.");
        check(resCred.Errors.Any(e => e.Code == SepaValidationErrorCode.DirectDebitInvalidCreditorId), "Fails with DirectDebitInvalidCreditorId.");

        // 5. Missing mandate ID
        var resMandate = DirectDebitReturnValidator.ValidateMandateRevocation(creditorId, "", DirectDebitReturnCapability.StandardDefault);
        check(!resMandate.IsValid, "Missing mandate ID fails mandate revocation validation.");
        check(resMandate.Errors.Any(e => e.Code == SepaValidationErrorCode.DirectDebitMissingMandateId), "Fails with DirectDebitMissingMandateId.");
    }

    private static void TestFinTsDirectDebitReturnWireCodecs(Action<bool, string> check)
    {
        string iban = "DE89370400440532013000";
        string bic = "DBBADE33XXX";
        string originalRef = "ORIG-TX-REF-7788";
        Money amount = new(125.75m, "EUR");
        string creditorId = "DE98ZZZ09999999999";
        string mandateId = "MANDATE-5544";
        DateOnly dueDate = new(2026, 10, 15);

        // 1. HKDRW Return Segment
        string hkdrw = FinTsDirectDebitReturnRequestWriter.BuildHkdrwSegment(
            ordinal: 2,
            version: 1,
            originIban: iban,
            originBic: bic,
            originalBankReference: originalRef,
            reason: DirectDebitReturnReason.DebtorRequestUnconditional,
            amount: amount,
            creditorId: creditorId,
            mandateId: mandateId,
            originalDueDate: dueDate);

        check(hkdrw.StartsWith("HKDRW:2:1+", StringComparison.Ordinal), "HKDRW starts with HKDRW:2:1+.");
        check(hkdrw.Contains("DE89370400440532013000:DBBADE33XXX+", StringComparison.Ordinal), "HKDRW contains debtor IBAN:BIC.");
        check(hkdrw.Contains("+ORIG-TX-REF-7788+", StringComparison.Ordinal), "HKDRW contains original bank reference.");
        check(hkdrw.Contains("+MS02+", StringComparison.Ordinal), "HKDRW contains reason code MS02.");
        check(hkdrw.Contains("+125,75:EUR+", StringComparison.Ordinal), "HKDRW contains formatted amount 125,75:EUR.");
        check(hkdrw.Contains("+DE98ZZZ09999999999+", StringComparison.Ordinal), "HKDRW contains creditor ID.");
        check(hkdrw.Contains("+MANDATE-5544+", StringComparison.Ordinal), "HKDRW contains mandate reference.");
        check(hkdrw.Contains("+20261015'", StringComparison.Ordinal), "HKDRW contains original due date.");
        check(hkdrw.EndsWith("'", StringComparison.Ordinal), "HKDRW ends with single quote.");

        // 2. HKDMB Mandate Revocation Segment
        DateOnly effectiveDate = new(2026, 11, 1);
        string hkdmb = FinTsDirectDebitReturnRequestWriter.BuildHkdmbSegment(
            ordinal: 2,
            version: 1,
            originIban: iban,
            originBic: bic,
            creditorId: creditorId,
            mandateId: mandateId,
            effectiveDate: effectiveDate);

        check(hkdmb.StartsWith("HKDMB:2:1+", StringComparison.Ordinal), "HKDMB starts with HKDMB:2:1+.");
        check(hkdmb.Contains("DE89370400440532013000:DBBADE33XXX+", StringComparison.Ordinal), "HKDMB contains debtor IBAN:BIC.");
        check(hkdmb.Contains("+DE98ZZZ09999999999+", StringComparison.Ordinal), "HKDMB contains creditor ID.");
        check(hkdmb.Contains("+MANDATE-5544+", StringComparison.Ordinal), "HKDMB contains mandate reference.");
        check(hkdmb.Contains("+1+", StringComparison.Ordinal), "HKDMB contains action code 1 (revoke).");
        check(hkdmb.Contains("+20261101'", StringComparison.Ordinal), "HKDMB contains effective date.");

        // 3. Complete unsigned return message
        byte[] retMsg = FinTsDirectDebitReturnRequestWriter.EncodeUnsignedReturn(
            dialogueId: "DIA-RET-01",
            messageNumber: 1,
            version: 1,
            originIban: iban,
            originBic: bic,
            originalBankReference: originalRef,
            reason: DirectDebitReturnReason.NoValidMandate,
            amount: amount,
            creditorId: creditorId,
            mandateId: mandateId,
            originalDueDate: dueDate);

        string retMsgText = Encoding.Latin1.GetString(retMsg);
        check(retMsgText.StartsWith("HNHBK:1:3+", StringComparison.Ordinal), "Return message starts with HNHBK.");
        check(retMsgText.Contains("HKDRW:2:1+", StringComparison.Ordinal), "Return message contains HKDRW.");
        check(retMsgText.Contains("+MD01+", StringComparison.Ordinal), "Return message contains MD01 reason.");
        check(retMsgText.EndsWith("HNHBS:3:1+1'", StringComparison.Ordinal), "Return message ends with HNHBS.");

        // 4. Complete unsigned mandate revocation message
        byte[] manMsg = FinTsDirectDebitReturnRequestWriter.EncodeUnsignedMandateRevocation(
            dialogueId: "DIA-MAN-01",
            messageNumber: 1,
            version: 1,
            originIban: iban,
            originBic: bic,
            creditorId: creditorId,
            mandateId: mandateId,
            effectiveDate: effectiveDate);

        string manMsgText = Encoding.Latin1.GetString(manMsg);
        check(manMsgText.StartsWith("HNHBK:1:3+", StringComparison.Ordinal), "Mandate message starts with HNHBK.");
        check(manMsgText.Contains("HKDMB:2:1+", StringComparison.Ordinal), "Mandate message contains HKDMB.");
        check(manMsgText.EndsWith("HNHBS:3:1+1'", StringComparison.Ordinal), "Mandate message ends with HNHBS.");
    }

    private static void TestFinTsDirectDebitReturnResponseParser(Action<bool, string> check)
    {
        // 1. Success response with HIDRW confirmation
        string wireSuccess =
            "HNHBK:1:3+000000000000+300+DIA-10023+1+DIA-10023:1'" +
            "HIRMG:2:2+0010::Nachricht entgegengenommen.'" +
            "HIRMS:3:2:2+0020::Lastschriftrueckbuchung entgegengenommen'" +
            "HIDRW:4:1:2+BANK-RETURN-REF-101'" +
            "HNHBS:5:1+1'";

        FinTsResponse respSuccess = FinTsResponse.Parse(Frame(wireSuccess));
        var resSuccess = FinTsDirectDebitReturnResponseParser.Parse(respSuccess);

        check(resSuccess.IsSuccess, "Success parsed IsSuccess = true.");
        check(!resSuccess.IsScaRequired, "No SCA required.");
        check(!resSuccess.IsRejected, "Not rejected.");
        check(resSuccess.TargetStatus == DirectDebitReturnStatus.SubmittedAwaitingStatement, "TargetStatus is SubmittedAwaitingStatement.");
        check(resSuccess.ConfirmationReference == "BANK-RETURN-REF-101", "Confirmation reference extracted from HIDRW.");

        // 2. SCA Challenge response
        string wireSca =
            "HNHBK:1:3+000000000000+300+DIA-10023+1+DIA-10023:1'" +
            "HIRMG:2:2+0010::Nachricht entgegengenommen.'" +
            "HIRMS:3:2:2+0030::Bitte TAN eingeben'" +
            "HITAN:4:6:2+4+S+CHALLENGE-ID+Geben Sie die TAN fuer Lastschriftrueckbuchung ein:+'" +
            "HNHBS:5:1+1'";

        FinTsResponse respSca = FinTsResponse.Parse(Frame(wireSca));
        var resSca = FinTsDirectDebitReturnResponseParser.Parse(respSca);

        check(!resSca.IsSuccess, "SCA response IsSuccess = false.");
        check(resSca.IsScaRequired, "IsScaRequired = true.");
        check(!resSca.IsRejected, "Not rejected.");
        check(resSca.TargetStatus == DirectDebitReturnStatus.PendingSubmission, "TargetStatus is PendingSubmission.");
        check(resSca.ChallengeText is not null && resSca.ChallengeText.Contains("Geben Sie die TAN"), "Challenge text extracted.");

        // 3. Error / Rejected response
        string wireError =
            "HNHBK:1:3+000000000000+300+DIA-10023+1+DIA-10023:1'" +
            "HIRMG:2:2+9050::Teilweise fehlerhaft.'" +
            "HIRMS:3:2:2+9210::Frist fuer Rueckbuchung abgelaufen'" +
            "HNHBS:4:1+1'";

        FinTsResponse respError = FinTsResponse.Parse(Frame(wireError));
        var resError = FinTsDirectDebitReturnResponseParser.Parse(respError);

        check(!resError.IsSuccess, "Error response IsSuccess = false.");
        check(resError.IsRejected, "IsRejected = true.");
        check(resError.TargetStatus == DirectDebitReturnStatus.Rejected, "TargetStatus is Rejected.");
        check(resError.PrimaryReturnCode == "9210", "PrimaryReturnCode is 9210.");

        // 4. Ambiguous response (e.g. 9000 processing indeterminate)
        string wireAmbiguous =
            "HNHBK:1:3+000000000000+300+DIA-10023+1+DIA-10023:1'" +
            "HIRMG:2:2+3060::Warnung: Verbindung unterbrochen'" +
            "HNHBS:3:1+1'";

        FinTsResponse respAmbiguous = FinTsResponse.Parse(Frame(wireAmbiguous));
        var resAmbiguous = FinTsDirectDebitReturnResponseParser.Parse(respAmbiguous);

        check(!resAmbiguous.IsSuccess, "Ambiguous response IsSuccess is false.");
        check(!resAmbiguous.IsRejected, "Ambiguous response IsRejected is false.");
        check(resAmbiguous.TargetStatus == DirectDebitReturnStatus.Unknown, "TargetStatus is Unknown.");
    }

    private static FinTsMessageFrame Frame(string wire) =>
        FinTsMessageFrame.Parse(Encoding.Latin1.GetBytes(wire[..10] + Encoding.Latin1.GetByteCount(wire).ToString("D12", CultureInfo.InvariantCulture) + wire[22..]));
}
