using Broiler.Fond.Kernel.Domain;
using Broiler.Fond.Kernel.Domain.Payments;

namespace Broiler.Fond.Kernel.Tests.Payments;

public static class PaymentDraftValidationTests
{
    public static void RunAll()
    {
        int checkCount = 0;
        Action<bool, string> check = (condition, message) =>
        {
            if (!condition)
            {
                throw new InvalidOperationException($"Payment draft validation assertion failed: {message}");
            }
            checkCount++;
        };

        TestSepaIbanValidation(check);
        TestBeneficiaryAndRemittance(check);
        TestPaymentDraftAndRevisionImmutability(check);
        TestSepaPaymentDraftValidator(check);

        Console.WriteLine($"Payment domain and SEPA validation (M4-01): {checkCount} checks completed successfully.");
    }

    private static void TestSepaIbanValidation(Action<bool, string> check)
    {
        // 1. Valid German IBAN
        check(SepaIbanValidator.Validate("DE89370400440532013000", out string deNorm), "Valid German IBAN passes validation.");
        check(deNorm == "DE89370400440532013000", "Normalized German IBAN matches expected string.");

        // With spaces and lowercase
        check(SepaIbanValidator.Validate(" de89 3704 0044 0532 0130 00 ", out string deNormSpaces), "German IBAN with spaces and lowercase passes.");
        check(deNormSpaces == "DE89370400440532013000", "Whitespace stripped and uppercased properly.");

        // Spacing format
        string formatted = SepaIbanValidator.FormatWithSpaces(deNorm);
        check(formatted == "DE89 3704 0044 0532 0130 00", "FormatWithSpaces groups into 4-character blocks.");

        // 2. Valid Austrian IBAN (length 20)
        check(SepaIbanValidator.Validate("AT611904300234573201", out string atNorm), "Valid Austrian IBAN passes validation.");
        check(atNorm == "AT611904300234573201", "Normalized Austrian IBAN matches.");

        // 3. Valid French IBAN with alphanumeric BBAN (length 27)
        check(SepaIbanValidator.Validate("FR1420041010050500013M02606", out string frNorm), "Valid French alphanumeric IBAN passes validation.");
        check(frNorm == "FR1420041010050500013M02606", "Normalized French IBAN matches.");

        // 4. Valid Dutch IBAN (length 18)
        check(SepaIbanValidator.Validate("NL91ABNA0417164300", out string nlNorm), "Valid Dutch IBAN passes validation.");
        check(nlNorm == "NL91ABNA0417164300", "Normalized Dutch IBAN matches.");

        // 5. Valid Italian IBAN (length 27)
        check(SepaIbanValidator.Validate("IT60X0542811101000000123456", out string itNorm), "Valid Italian IBAN passes validation.");
        check(itNorm == "IT60X0542811101000000123456", "Normalized Italian IBAN matches.");

        // 6. Invalid checksum (single digit alteration in check digits or BBAN)
        check(!SepaIbanValidator.Validate("DE88370400440532013000", out _), "Single-digit alteration in check digits fails checksum.");
        check(!SepaIbanValidator.Validate("DE89370400440532013001", out _), "Single-digit alteration in account digits fails checksum.");
        check(!SepaIbanValidator.Validate("DE89370400440532013009", out _), "Single-digit alteration fails checksum.");

        // 7. Invalid lengths
        check(!SepaIbanValidator.Validate("DE8937040044053201300", out _), "German IBAN with 21 chars fails (too short).");
        check(!SepaIbanValidator.Validate("DE893704004405320130001", out _), "German IBAN with 23 chars fails (too long).");
        check(!SepaIbanValidator.Validate("AT61190430023457320", out _), "Austrian IBAN with 19 chars fails.");

        // 8. Invalid characters
        check(!SepaIbanValidator.Validate("DE89-3704-0044-0532-0130-00", out _), "IBAN with hyphens rejected.");
        check(!SepaIbanValidator.Validate("DE89#70400440532013000", out _), "IBAN with special characters rejected.");
        check(!SepaIbanValidator.Validate("", out _), "Empty string rejected.");
        check(!SepaIbanValidator.Validate(null, out _), "Null string rejected.");
        check(!SepaIbanValidator.Validate("12345678901234567890", out _), "IBAN without country code letters rejected.");
    }

    private static void TestBeneficiaryAndRemittance(Action<bool, string> check)
    {
        var ben = new PaymentBeneficiary("  Musterfirma GmbH  ", " de89370400440532013000 ", " heLaAdef100 ");
        check(ben.Name == "Musterfirma GmbH", "Beneficiary name is trimmed.");
        check(ben.Iban == "DE89370400440532013000", "Beneficiary IBAN is uppercased and trimmed.");
        check(ben.Bic == "HELAADEF100", "Beneficiary BIC is uppercased and trimmed.");

        var benNoBic = new PaymentBeneficiary("Erika Mustermann", "DE89370400440532013000", null);
        check(benNoBic.Bic is null, "Beneficiary with null BIC retains null.");

        var remText = new PaymentRemittance("Rechnung 2026-10492");
        check(remText.UnstructuredText == "Rechnung 2026-10492", "Unstructured remittance text matches.");
        check(remText.DisplayText == "Rechnung 2026-10492", "DisplayText returns unstructured text.");

        var remStruct = new PaymentRemittance(null, "RF18539007547034");
        check(remStruct.StructuredReference == "RF18539007547034", "Structured reference preserved.");
        check(remStruct.DisplayText == "RF18539007547034", "DisplayText returns structured reference when unstructured is null.");

        var remEmpty = new PaymentRemittance(null, null);
        check(remEmpty.DisplayText == "(No Remittance Specified)", "DisplayText handles empty remittance.");
    }

    private static void TestPaymentDraftAndRevisionImmutability(Action<bool, string> check)
    {
        var now = DateTimeOffset.UtcNow;
        var today = DateOnly.FromDateTime(DateTime.UtcNow);

        var draft = new PaymentDraft(
            Id: 100,
            SourceAccountId: 10,
            SourceAccountBindingId: 20,
            SourceAccountIncarnationId: 30,
            CurrentRevisionId: 101,
            Status: PaymentDraftStatus.Draft,
            CreatedAt: now,
            UpdatedAt: now);

        check(draft.Id == 100, "Draft ID matches.");
        check(draft.Status == PaymentDraftStatus.Draft, "Draft initial status is Draft.");
        check(draft.CurrentRevisionId == 101, "Draft points to revision 101.");

        var ben = new PaymentBeneficiary("Max Mustermann", "DE89370400440532013000");
        var money = new Money(50.00m, "EUR");
        var remittance = new PaymentRemittance("Dinner share");

        var rev1 = new PaymentDraftRevision(
            Id: 101,
            DraftId: 100,
            RevisionNumber: 1,
            PredecessorRevisionId: null,
            Timestamp: now,
            SourceAccountId: 10,
            SourceAccountBindingId: 20,
            SourceAccountIncarnationId: 30,
            Beneficiary: ben,
            Amount: money,
            Remittance: remittance,
            ExecutionMode: PaymentExecutionMode.StandardCreditTransfer,
            ExecutionDate: today,
            CustomerReference: "REF-001");

        check(rev1.Id == 101, "Revision ID is 101.");
        check(rev1.RevisionNumber == 1, "Revision number is 1.");
        check(rev1.PredecessorRevisionId is null, "First revision has null predecessor.");
        check(rev1.Amount.Amount == 50.00m, "Amount is 50.00 EUR.");

        // Edit draft: creates revision 2 linked to revision 1
        var moneyUpdated = new Money(75.50m, "EUR");
        var rev2 = new PaymentDraftRevision(
            Id: 102,
            DraftId: 100,
            RevisionNumber: 2,
            PredecessorRevisionId: 101,
            Timestamp: now.AddMinutes(2),
            SourceAccountId: 10,
            SourceAccountBindingId: 20,
            SourceAccountIncarnationId: 30,
            Beneficiary: ben,
            Amount: moneyUpdated,
            Remittance: remittance,
            ExecutionMode: PaymentExecutionMode.StandardCreditTransfer,
            ExecutionDate: today,
            CustomerReference: "REF-001");

        check(rev2.Id == 102, "Revision 2 ID is 102.");
        check(rev2.RevisionNumber == 2, "Revision number incremented to 2.");
        check(rev2.PredecessorRevisionId == 101, "Predecessor points to revision 101.");
        check(rev2.Amount.Amount == 75.50m, "Updated amount is 75.50 EUR.");
        check(rev1.Amount.Amount == 50.00m, "Revision 1 remains completely immutable.");
    }

    private static void TestSepaPaymentDraftValidator(Action<bool, string> check)
    {
        var now = DateTimeOffset.UtcNow;
        var today = new DateOnly(2026, 10, 18);

        var validBen = new PaymentBeneficiary("Alice Schmidt", "DE89370400440532013000");
        var validMoney = new Money(120.00m, "EUR");
        var validRemittance = new PaymentRemittance("Invoice 2026-441");

        var validRev = new PaymentDraftRevision(
            Id: 1,
            DraftId: 10,
            RevisionNumber: 1,
            PredecessorRevisionId: null,
            Timestamp: now,
            SourceAccountId: 100,
            SourceAccountBindingId: 200,
            SourceAccountIncarnationId: 300,
            Beneficiary: validBen,
            Amount: validMoney,
            Remittance: validRemittance,
            ExecutionMode: PaymentExecutionMode.StandardCreditTransfer,
            ExecutionDate: today);

        // 1. Happy path
        var resultValid = SepaPaymentDraftValidator.Validate(validRev, today);
        check(resultValid.IsValid, "Valid draft revision passes validation.");
        check(resultValid.Errors.Count == 0, "Valid draft produces zero errors.");

        // 2. Non-EUR currency
        var badCurrencyRev = validRev with { Amount = new Money(120.00m, "USD") };
        var resCurrency = SepaPaymentDraftValidator.Validate(badCurrencyRev, today);
        check(!resCurrency.IsValid, "Non-EUR currency fails validation.");
        check(resCurrency.Errors.Any(e => e.Code == SepaValidationErrorCode.InvalidCurrency), "InvalidCurrency error code present.");

        // 3. Zero or negative amount
        var zeroAmountRev = validRev with { Amount = new Money(0.00m, "EUR") };
        var resZero = SepaPaymentDraftValidator.Validate(zeroAmountRev, today);
        check(!resZero.IsValid, "Zero amount fails validation.");
        check(resZero.Errors.Any(e => e.Code == SepaValidationErrorCode.InvalidAmount), "InvalidAmount error code on zero.");

        var negAmountRev = validRev with { Amount = new Money(-25.00m, "EUR") };
        var resNeg = SepaPaymentDraftValidator.Validate(negAmountRev, today);
        check(!resNeg.IsValid, "Negative amount fails validation.");
        check(resNeg.Errors.Any(e => e.Code == SepaValidationErrorCode.InvalidAmount), "InvalidAmount error code on negative.");

        // 4. Amount with more than 2 decimal places
        var fracAmountRev = validRev with { Amount = new Money(10.555m, "EUR") };
        var resFrac = SepaPaymentDraftValidator.Validate(fracAmountRev, today);
        check(!resFrac.IsValid, "Fractional cents fail validation.");
        check(resFrac.Errors.Any(e => e.Code == SepaValidationErrorCode.AmountExceedsScale), "AmountExceedsScale error code present.");

        // 5. Beneficiary name issues
        var emptyNameBen = new PaymentBeneficiary("   ", "DE89370400440532013000");
        var emptyNameRev = validRev with { Beneficiary = emptyNameBen };
        var resEmptyName = SepaPaymentDraftValidator.Validate(emptyNameRev, today);
        check(!resEmptyName.IsValid, "Whitespace beneficiary name fails validation.");
        check(resEmptyName.Errors.Any(e => e.Code == SepaValidationErrorCode.MissingBeneficiaryName), "MissingBeneficiaryName error code present.");

        string longName = new('A', 71);
        var longNameBen = new PaymentBeneficiary(longName, "DE89370400440532013000");
        var longNameRev = validRev with { Beneficiary = longNameBen };
        var resLongName = SepaPaymentDraftValidator.Validate(longNameRev, today);
        check(!resLongName.IsValid, "Beneficiary name > 70 chars fails validation.");
        check(resLongName.Errors.Any(e => e.Code == SepaValidationErrorCode.BeneficiaryNameTooLong), "BeneficiaryNameTooLong error code present.");

        // Exactly 70 chars is valid
        string exact70Name = new('A', 70);
        var exact70Ben = new PaymentBeneficiary(exact70Name, "DE89370400440532013000");
        var exact70Rev = validRev with { Beneficiary = exact70Ben };
        check(SepaPaymentDraftValidator.Validate(exact70Rev, today).IsValid, "Beneficiary name with exactly 70 chars is valid.");

        // 6. Invalid IBAN
        var badIbanBen = new PaymentBeneficiary("Bob Smith", "DE88370400440532013000"); // bad checksum
        var badIbanRev = validRev with { Beneficiary = badIbanBen };
        var resBadIban = SepaPaymentDraftValidator.Validate(badIbanRev, today);
        check(!resBadIban.IsValid, "Invalid IBAN fails validation.");
        check(resBadIban.Errors.Any(e => e.Code == SepaValidationErrorCode.InvalidIban), "InvalidIban error code present.");

        // 7. Remittance length > 140
        string longRemittance = new('X', 141);
        var longRemRev = validRev with { Remittance = new PaymentRemittance(longRemittance) };
        var resLongRem = SepaPaymentDraftValidator.Validate(longRemRev, today);
        check(!resLongRem.IsValid, "Remittance > 140 chars fails validation.");
        check(resLongRem.Errors.Any(e => e.Code == SepaValidationErrorCode.RemittanceTooLong), "RemittanceTooLong error code present.");

        string exact140Rem = new('X', 140);
        var exact140Rev = validRev with { Remittance = new PaymentRemittance(exact140Rem) };
        check(SepaPaymentDraftValidator.Validate(exact140Rev, today).IsValid, "Remittance with exactly 140 chars is valid.");

        // 8. Execution date in the past
        var pastDateRev = validRev with { ExecutionDate = today.AddDays(-1) };
        var resPastDate = SepaPaymentDraftValidator.Validate(pastDateRev, today);
        check(!resPastDate.IsValid, "Execution date in the past fails validation.");
        check(resPastDate.Errors.Any(e => e.Code == SepaValidationErrorCode.ExecutionDateInPast), "ExecutionDateInPast error code present.");

        // Future execution date is valid
        var futureDateRev = validRev with { ExecutionDate = today.AddDays(7) };
        check(SepaPaymentDraftValidator.Validate(futureDateRev, today).IsValid, "Future execution date is valid.");

        // 9. Concurrent validation errors
        var multipleErrorsRev = validRev with
        {
            Amount = new Money(-10.00m, "USD"),
            Beneficiary = new PaymentBeneficiary("   ", "DE00000000000000000000"),
            Remittance = new PaymentRemittance(longRemittance),
            ExecutionDate = today.AddDays(-5),
        };
        var resMulti = SepaPaymentDraftValidator.Validate(multipleErrorsRev, today);
        check(!resMulti.IsValid, "Multiple invalid fields fail validation.");
        check(resMulti.Errors.Count >= 5, "Multiple distinct validation errors collected concurrently.");
    }
}
