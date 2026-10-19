using System.Globalization;
using System.Text;
using Broiler.Fond.Kernel.Domain;
using Broiler.Fond.Kernel.Domain.Payments;
using Broiler.Fond.Kernel.FinTs;
using Broiler.Fond.Kernel.FinTs.Transport;

namespace Broiler.Fond.Kernel.Tests.Payments;

public static class InstantPaymentTests
{
    public static void RunAll()
    {
        int checkCount = 0;
        Action<bool, string> check = (condition, message) =>
        {
            if (!condition)
            {
                throw new InvalidOperationException($"Instant payment assertion failed: {message}");
            }
            checkCount++;
        };

        TestExecutionModeEnum(check);
        TestOwnAccountTransferValidator(check);
        TestInstantPaymentDraftValidation(check);
        TestAntiDowngradeGuard(check);
        TestPainDocumentWriterInstantMode(check);
        TestFinTsInstantTransferWireCodecs(check);
        TestFinTsInstantPaymentAdvertisement(check);
        TestSepaTransferExecutionCoordinatorInstantFlow(check).GetAwaiter().GetResult();

        Console.WriteLine($"Own-account and instant SEPA credit transfers (M5-01): {checkCount} checks completed successfully.");
    }

    private static void TestExecutionModeEnum(Action<bool, string> check)
    {
        check((int)PaymentExecutionMode.StandardCreditTransfer == 1, "StandardCreditTransfer enum value is 1.");
        check((int)PaymentExecutionMode.InstantCreditTransfer == 2, "InstantCreditTransfer enum value is 2.");
        check((int)PaymentExecutionMode.OwnAccountTransfer == 3, "OwnAccountTransfer enum value is 3.");
    }

    private static void TestOwnAccountTransferValidator(Action<bool, string> check)
    {
        string sourceIban = "DE89370400440532013000";
        string destIban = "DE12500105170648489890";
        Money validAmount = new(250.00m, "EUR");

        // 1. Valid own-account transfer
        SepaValidationResult validResult = OwnAccountTransferValidator.Validate(
            sourceAccountId: 10,
            destinationAccountId: 20,
            sourceIban: sourceIban,
            destinationIban: destIban,
            amount: validAmount,
            sourceBankCode: "37040044",
            destinationBankCode: "50010517");
        check(validResult.IsValid, "Distinct accounts and valid IBANs pass validation.");
        check(validResult.Errors.Count == 0, "No errors for valid own-account transfer.");

        // 2. Same account ID
        SepaValidationResult sameIdResult = OwnAccountTransferValidator.Validate(
            sourceAccountId: 10,
            destinationAccountId: 10,
            sourceIban: sourceIban,
            destinationIban: destIban,
            amount: validAmount);
        check(!sameIdResult.IsValid, "Same source and destination account ID fails validation.");
        check(sameIdResult.Errors.Any(e => e.Code == SepaValidationErrorCode.OwnAccountSourceEqualsDestination),
            "Reports OwnAccountSourceEqualsDestination error for same ID.");

        // 3. Same IBAN
        SepaValidationResult sameIbanResult = OwnAccountTransferValidator.Validate(
            sourceAccountId: 10,
            destinationAccountId: 20,
            sourceIban: sourceIban,
            destinationIban: sourceIban,
            amount: validAmount);
        check(!sameIbanResult.IsValid, "Same source and destination IBAN fails validation.");
        check(sameIbanResult.Errors.Any(e => e.Code == SepaValidationErrorCode.OwnAccountSourceEqualsDestination),
            "Reports OwnAccountSourceEqualsDestination error for same IBAN.");

        // 4. Invalid IBANs
        SepaValidationResult invalidIbanResult = OwnAccountTransferValidator.Validate(
            sourceAccountId: 10,
            destinationAccountId: 20,
            sourceIban: "INVALID_IBAN",
            destinationIban: destIban,
            amount: validAmount);
        check(!invalidIbanResult.IsValid, "Invalid source IBAN fails validation.");
        check(invalidIbanResult.Errors.Any(e => e.Code == SepaValidationErrorCode.InvalidIban),
            "Reports InvalidIban error for malformed source IBAN.");

        // 5. Invalid Currency
        SepaValidationResult nonEurResult = OwnAccountTransferValidator.Validate(
            sourceAccountId: 10,
            destinationAccountId: 20,
            sourceIban: sourceIban,
            destinationIban: destIban,
            amount: new Money(100.00m, "USD"));
        check(!nonEurResult.IsValid, "Non-EUR currency fails validation.");
        check(nonEurResult.Errors.Any(e => e.Code == SepaValidationErrorCode.InvalidCurrency),
            "Reports InvalidCurrency error.");

        // 6. Zero or negative amount
        SepaValidationResult zeroResult = OwnAccountTransferValidator.Validate(
            sourceAccountId: 10,
            destinationAccountId: 20,
            sourceIban: sourceIban,
            destinationIban: destIban,
            amount: new Money(0.00m, "EUR"));
        check(!zeroResult.IsValid, "Zero amount fails validation.");
        check(zeroResult.Errors.Any(e => e.Code == SepaValidationErrorCode.InvalidAmount),
            "Reports InvalidAmount error.");

        // 7. Amount with > 2 decimal places
        SepaValidationResult fractionalResult = OwnAccountTransferValidator.Validate(
            sourceAccountId: 10,
            destinationAccountId: 20,
            sourceIban: sourceIban,
            destinationIban: destIban,
            amount: new Money(10.123m, "EUR"));
        check(!fractionalResult.IsValid, "Fractional cents fail validation.");
        check(fractionalResult.Errors.Any(e => e.Code == SepaValidationErrorCode.AmountExceedsScale),
            "Reports AmountExceedsScale error.");

        // 8. Internal transfer check
        check(OwnAccountTransferValidator.IsInternalTransfer("37040044", "37040044"), "Matching bank codes detected as internal.");
        check(!OwnAccountTransferValidator.IsInternalTransfer("37040044", "10077777"), "Differing bank codes detected as external.");
        check(!OwnAccountTransferValidator.IsInternalTransfer(null, "10077777"), "Null bank code detected as external.");
    }

    private static void TestInstantPaymentDraftValidation(Action<bool, string> check)
    {
        DateOnly today = new(2026, 10, 19);
        DateOnly future = new(2026, 10, 25);
        DateTimeOffset now = DateTimeOffset.UtcNow;

        PaymentDraftRevision instantTodayDraft = new(
            Id: 2,
            DraftId: 1,
            RevisionNumber: 1,
            PredecessorRevisionId: null,
            Timestamp: now,
            SourceAccountId: 10,
            SourceAccountBindingId: 20,
            SourceAccountIncarnationId: 30,
            Beneficiary: new PaymentBeneficiary("Erika Mustermann", "DE89370400440532013000", "DBBADE33XXX"),
            Amount: new Money(500.00m, "EUR"),
            Remittance: new PaymentRemittance("Dinner share"),
            ExecutionMode: PaymentExecutionMode.InstantCreditTransfer,
            ExecutionDate: today);

        // 1. Instant payment with supported capability and within limit
        InstantPaymentCapability standardCapability = InstantPaymentCapability.StandardEpcDefault;
        SepaValidationResult resultValid = SepaPaymentDraftValidator.Validate(
            instantTodayDraft,
            standardCapability,
            evaluationDate: today);
        check(resultValid.IsValid, "Instant payment with supported capability and today execution date passes.");

        // 2. Instant payment without capability (null)
        SepaValidationResult resultNullCap = SepaPaymentDraftValidator.Validate(
            instantTodayDraft,
            instantCapability: null,
            evaluationDate: today);
        check(!resultNullCap.IsValid, "Instant payment without capability fails validation.");
        check(resultNullCap.Errors.Any(e => e.Code == SepaValidationErrorCode.InstantCapabilityNotSupported),
            "Reports InstantCapabilityNotSupported when capability is null.");

        // 3. Instant payment with unsupported capability
        SepaValidationResult resultUnsupCap = SepaPaymentDraftValidator.Validate(
            instantTodayDraft,
            InstantPaymentCapability.Unsupported,
            evaluationDate: today);
        check(!resultUnsupCap.IsValid, "Instant payment with unsupported capability fails.");
        check(resultUnsupCap.Errors.Any(e => e.Code == SepaValidationErrorCode.InstantCapabilityNotSupported),
            "Reports InstantCapabilityNotSupported when IsSupported is false.");

        // 4. Instant payment exceeding bank limit
        InstantPaymentCapability lowLimitCap = new(
            IsSupported: true,
            MaximumAmount: 200.00m,
            EstimatedExecutionSeconds: 10,
            FeeNotice: "0.25 EUR fee",
            SupportedSegmentCode: "HKIPB",
            SupportedSegmentVersion: 1);
        SepaValidationResult resultExceeds = SepaPaymentDraftValidator.Validate(
            instantTodayDraft,
            lowLimitCap,
            evaluationDate: today);
        check(!resultExceeds.IsValid, "Instant payment exceeding bank limit fails.");
        check(resultExceeds.Errors.Any(e => e.Code == SepaValidationErrorCode.InstantAmountExceedsLimit),
            "Reports InstantAmountExceedsLimit when amount > max limit.");

        // 5. Instant payment future-dated
        PaymentDraftRevision instantFutureDraft = new(
            Id: 3,
            DraftId: 1,
            RevisionNumber: 2,
            PredecessorRevisionId: 2,
            Timestamp: now,
            SourceAccountId: 10,
            SourceAccountBindingId: 20,
            SourceAccountIncarnationId: 30,
            Beneficiary: new PaymentBeneficiary("Erika Mustermann", "DE89370400440532013000", "DBBADE33XXX"),
            Amount: new Money(100.00m, "EUR"),
            Remittance: new PaymentRemittance("Dinner share"),
            ExecutionMode: PaymentExecutionMode.InstantCreditTransfer,
            ExecutionDate: future);

        SepaValidationResult resultFuture = SepaPaymentDraftValidator.Validate(
            instantFutureDraft,
            standardCapability,
            evaluationDate: today);
        check(!resultFuture.IsValid, "Future-dated instant payment fails validation.");
        check(resultFuture.Errors.Any(e => e.Code == SepaValidationErrorCode.InstantExecutionDateNotToday),
            "Reports InstantExecutionDateNotToday when execution date != today.");

        // 6. Standard credit transfer with future date passes
        PaymentDraftRevision standardFutureDraft = new(
            Id: 4,
            DraftId: 1,
            RevisionNumber: 3,
            PredecessorRevisionId: 2,
            Timestamp: now,
            SourceAccountId: 10,
            SourceAccountBindingId: 20,
            SourceAccountIncarnationId: 30,
            Beneficiary: new PaymentBeneficiary("Erika Mustermann", "DE89370400440532013000", "DBBADE33XXX"),
            Amount: new Money(100.00m, "EUR"),
            Remittance: new PaymentRemittance("Dinner share"),
            ExecutionMode: PaymentExecutionMode.StandardCreditTransfer,
            ExecutionDate: future);

        SepaValidationResult resultStdFuture = SepaPaymentDraftValidator.Validate(
            standardFutureDraft,
            instantCapability: null,
            evaluationDate: today);
        check(resultStdFuture.IsValid, "Standard credit transfer with future execution date passes.");
    }

    private static void TestAntiDowngradeGuard(Action<bool, string> check)
    {
        DateOnly today = new(2026, 10, 19);
        DateTimeOffset now = DateTimeOffset.UtcNow;

        PaymentDraftRevision instantDraft = new(
            Id: 2,
            DraftId: 1,
            RevisionNumber: 1,
            PredecessorRevisionId: null,
            Timestamp: now,
            SourceAccountId: 10,
            SourceAccountBindingId: 20,
            SourceAccountIncarnationId: 30,
            Beneficiary: new PaymentBeneficiary("Erika Mustermann", "DE89370400440532013000", "DBBADE33XXX"),
            Amount: new Money(500.00m, "EUR"),
            Remittance: new PaymentRemittance("Dinner"),
            ExecutionMode: PaymentExecutionMode.InstantCreditTransfer,
            ExecutionDate: today);

        PaymentDraftRevision standardDraft = new(
            Id: 3,
            DraftId: 1,
            RevisionNumber: 2,
            PredecessorRevisionId: 2,
            Timestamp: now,
            SourceAccountId: 10,
            SourceAccountBindingId: 20,
            SourceAccountIncarnationId: 30,
            Beneficiary: new PaymentBeneficiary("Erika Mustermann", "DE89370400440532013000", "DBBADE33XXX"),
            Amount: new Money(500.00m, "EUR"),
            Remittance: new PaymentRemittance("Dinner"),
            ExecutionMode: PaymentExecutionMode.StandardCreditTransfer,
            ExecutionDate: today);

        // 1. Same mode: no throw
        SepaTransferExecutionCoordinator.AssertNoInstantDowngrade(instantDraft, PaymentExecutionMode.InstantCreditTransfer);
        SepaTransferExecutionCoordinator.AssertNoInstantDowngrade(standardDraft, PaymentExecutionMode.StandardCreditTransfer);
        check(true, "Compatible execution modes pass anti-downgrade check.");

        // 2. Instant -> Standard downgrade must throw InstantPaymentDowngradeException
        bool threwDowngrade = false;
        try
        {
            SepaTransferExecutionCoordinator.AssertNoInstantDowngrade(instantDraft, PaymentExecutionMode.StandardCreditTransfer);
        }
        catch (InstantPaymentDowngradeException)
        {
            threwDowngrade = true;
        }
        check(threwDowngrade, "Downgrading InstantCreditTransfer to StandardCreditTransfer throws InstantPaymentDowngradeException.");

        // 3. Instant -> OwnAccount downgrade must throw InstantPaymentDowngradeException
        bool threwOwnAccountDowngrade = false;
        try
        {
            SepaTransferExecutionCoordinator.AssertNoInstantDowngrade(instantDraft, PaymentExecutionMode.OwnAccountTransfer);
        }
        catch (InstantPaymentDowngradeException)
        {
            threwOwnAccountDowngrade = true;
        }
        check(threwOwnAccountDowngrade, "Downgrading InstantCreditTransfer to OwnAccountTransfer throws InstantPaymentDowngradeException.");
    }

    private static void TestPainDocumentWriterInstantMode(Action<bool, string> check)
    {
        DateTimeOffset now = new(2026, 10, 19, 11, 0, 0, TimeSpan.Zero);
        DateOnly execDate = new(2026, 10, 19);
        string endToEndId = "BF-20261019-INSTANT-001";

        // 1. Instant execution mode
        string instantXml = SepaPainDocumentWriter.GenerateXml(
            messageId: endToEndId,
            initiatingPartyName: "Max Mustermann",
            debtorName: "Max Mustermann",
            debtorIban: "DE89370400440532013000",
            debtorBic: "DBBADE33XXX",
            paymentInfoId: endToEndId,
            executionDate: execDate,
            endToEndId: endToEndId,
            amount: 75.50m,
            creditorName: "Erika Mustermann",
            creditorIban: "DE12500105170648489890",
            creditorBic: "BELADEB1XXX",
            remittance: new PaymentRemittance("Urgent transfer"),
            creationDateTime: now,
            executionMode: PaymentExecutionMode.InstantCreditTransfer);

        check(instantXml.Contains("<Cd>SEPA</Cd>", StringComparison.Ordinal), "Instant XML contains SEPA service level.");
        check(instantXml.Contains("<LclInstrm><Cd>INST</Cd></LclInstrm>", StringComparison.Ordinal),
            "Instant XML contains EPC <LclInstrm><Cd>INST</Cd></LclInstrm> local instrument indicator.");

        // 2. Standard execution mode
        string standardXml = SepaPainDocumentWriter.GenerateXml(
            messageId: endToEndId,
            initiatingPartyName: "Max Mustermann",
            debtorName: "Max Mustermann",
            debtorIban: "DE89370400440532013000",
            debtorBic: "DBBADE33XXX",
            paymentInfoId: endToEndId,
            executionDate: execDate,
            endToEndId: endToEndId,
            amount: 75.50m,
            creditorName: "Erika Mustermann",
            creditorIban: "DE12500105170648489890",
            creditorBic: "BELADEB1XXX",
            remittance: new PaymentRemittance("Regular transfer"),
            creationDateTime: now,
            executionMode: PaymentExecutionMode.StandardCreditTransfer);

        check(standardXml.Contains("<Cd>SEPA</Cd>", StringComparison.Ordinal), "Standard XML contains SEPA service level.");
        check(!standardXml.Contains("<LclInstrm>", StringComparison.Ordinal),
            "Standard XML does not contain <LclInstrm> tag.");
        check(!standardXml.Contains("<Cd>INST</Cd>", StringComparison.Ordinal),
            "Standard XML does not contain <Cd>INST</Cd> indicator.");
    }

    private static void TestFinTsInstantTransferWireCodecs(Action<bool, string> check)
    {
        string iban = "DE89370400440532013000";
        string bic = "DBBADE33XXX";
        byte[] painBytes = Encoding.UTF8.GetBytes("<Document>TestPayload</Document>");

        // 1. BuildHkipbSegment
        byte[] segmentBytes = FinTsSepaTransferRequestWriter.BuildHkipbSegment(
            ordinal: 2,
            version: 1,
            originIban: iban,
            originBic: bic,
            painXmlBytes: painBytes,
            sepaDescriptor: SepaPainDocumentWriter.PainNamespace);

        string segmentText = Encoding.Latin1.GetString(segmentBytes);
        check(segmentText.StartsWith("HKIPB:2:1+", StringComparison.Ordinal), "HKIPB segment starts with HKIPB:2:1+.");
        check(segmentText.Contains(iban, StringComparison.Ordinal), "HKIPB segment contains origin IBAN.");
        check(segmentText.Contains(bic, StringComparison.Ordinal), "HKIPB segment contains origin BIC.");
        check(segmentText.Contains("@" + painBytes.Length.ToString(CultureInfo.InvariantCulture) + "@", StringComparison.Ordinal),
            "HKIPB segment contains bin length tag.");
        check(segmentText.EndsWith("'", StringComparison.Ordinal), "HKIPB segment ends with FinTS terminator (').");

        // 2. EncodeUnsignedInstant
        byte[] wireBytes = FinTsSepaTransferRequestWriter.EncodeUnsignedInstant(
            dialogueId: "DIA-12345",
            messageNumber: 1,
            version: 1,
            originIban: iban,
            originBic: bic,
            painXmlBytes: painBytes);

        string wireText = Encoding.Latin1.GetString(wireBytes);
        check(wireText.StartsWith("HNHBK:1:3+", StringComparison.Ordinal), "Envelope starts with HNHBK:1:3.");
        check(wireText.Contains("HKIPB:2:1+", StringComparison.Ordinal), "Envelope contains HKIPB segment.");
        check(wireText.EndsWith("HNHBS:3:1+1'", StringComparison.Ordinal), "Envelope ends with HNHBS trailer.");
        check(wireBytes.Length == int.Parse(wireText.Substring(10, 12), CultureInfo.InvariantCulture),
            "Envelope size header matches exact wire byte count.");
    }

    private static void TestFinTsInstantPaymentAdvertisement(Action<bool, string> check)
    {
        // 1. Parameter set with HIPBS advertisement
        string wireBpdWithInstant = "HNHBK:1:3+000000000000+300+DIA-10023+1+DIA-10023:1'HIRMG:2:2+0010::Erfolgreich.'HIBPA:3:3:2+1+280:10070000+TestBank+1+1+300'HIPBS:4:1:3+1+0+0+100000.00'HNHBS:5:1+1'";
        var response = FinTsResponse.Parse(Frame(wireBpdWithInstant));
        var paramSet = FinTsParameterSet.Parse(response);

        var instantAds = FinTsInstantPaymentAdvertisement.ParseAll(paramSet);
        check(instantAds.Count == 1, "One HIPBS advertisement found in parameter set.");
        check(instantAds[0].Version == 1, "HIPBS version is 1.");
        check(instantAds[0].MaximumOrders == 1, "Maximum orders is 1.");
        check(instantAds[0].MinimumSignatures == 0, "Minimum signatures is 0.");
        check(instantAds[0].SecurityClass == 0, "Security class is 0.");
        check(instantAds[0].MaximumAmount == 100000.00m, "Maximum amount is 100,000.00 EUR.");

        InstantPaymentCapability cap = instantAds[0].ToCapability();
        check(cap.IsSupported, "Capability IsSupported is true.");
        check(cap.MaximumAmount == 100000.00m, "Capability MaximumAmount is 100,000.00 EUR.");
        check(cap.EstimatedExecutionSeconds == 10, "Capability EstimatedExecutionSeconds is 10.");
        check(cap.SupportedSegmentCode == "HKIPB", "Capability SupportedSegmentCode is HKIPB.");
        check(cap.SupportedSegmentVersion == 1, "Capability SupportedSegmentVersion is 1.");

        // 2. Parameter set without HIPBS
        string wireBpdNoInstant = "HNHBK:1:3+000000000000+300+DIA-10023+1+DIA-10023:1'HIRMG:2:2+0010::Erfolgreich.'HIBPA:3:3:2+1+280:10070000+TestBank+1+1+300'HNHBS:4:1+1'";
        var respNoInstant = FinTsResponse.Parse(Frame(wireBpdNoInstant));
        var paramSetNoInstant = FinTsParameterSet.Parse(respNoInstant);

        var noAds = FinTsInstantPaymentAdvertisement.ParseAll(paramSetNoInstant);
        check(noAds.Count == 0, "Zero HIPBS advertisements when unadvertised.");
    }

    private static async Task TestSepaTransferExecutionCoordinatorInstantFlow(Action<bool, string> check)
    {
        DateOnly today = new(2026, 10, 19);
        DateTimeOffset now = DateTimeOffset.UtcNow;

        PaymentDraftRevision instantDraft = new(
            Id: 2,
            DraftId: 1,
            RevisionNumber: 1,
            PredecessorRevisionId: null,
            Timestamp: now,
            SourceAccountId: 10,
            SourceAccountBindingId: 20,
            SourceAccountIncarnationId: 30,
            Beneficiary: new PaymentBeneficiary("Erika Mustermann", "DE89370400440532013000", "DBBADE33XXX"),
            Amount: new Money(350.00m, "EUR"),
            Remittance: new PaymentRemittance("Instant gift"),
            ExecutionMode: PaymentExecutionMode.InstantCreditTransfer,
            ExecutionDate: today);

        PaymentAttempt attempt = PaymentAttempt.CreatePreSendAttempt(50, instantDraft, now);

        string rawSuccessResponse =
            "HNHBK:1:3+000000000000+300+D-TEST-99+1+D-TEST-99:1'" +
            "HIRMG:2:2+0010::Nachricht entgegengenommen'" +
            "HIRMS:3:2:2+0020::Instant-Ausfuehrung gebucht'" +
            "HIIPB:4:1:2+ORD-INST-77'" +
            "HNHBS:5:1+1'";
        FinTsMessageFrame successFrame = Frame(rawSuccessResponse);

        using var transport = new FinTsSimulatedTransport();
        transport.EnqueueResponse(successFrame.Syntax.CopyWireBytes());

        SepaTransferExecutionResult result = await SepaTransferExecutionCoordinator.InitiateTransferAsync(
            attempt: attempt,
            draftRevision: instantDraft,
            debtorName: "Max Mustermann",
            debtorIban: "DE89370400440532013000",
            debtorBic: "DBBADE33XXX",
            endpoint: new Uri("https://bank.example/fints"),
            transport: transport,
            dialogueId: "DIA-999",
            messageNumber: 1,
            hkccsVersion: 1,
            transitionIdStart: 1);

        check(result.IsSuccess, "Instant payment submission completes successfully.");
        check(result.UpdatedAttempt.State == PaymentState.Accepted, "Target state is Accepted.");
        check(result.Transitions.Count == 2, "Records exactly 2 transitions (Submitting pre-send commit + Accepted).");
        check(result.Transitions[0].ToState == PaymentState.Submitting, "Pre-send transition to Submitting recorded.");
        check(result.Transitions[1].ToState == PaymentState.Accepted, "Final transition to Accepted recorded.");

        check(transport.TransmittedCount == 1, "Transport transmitted exactly 1 message.");
        byte[] sentPayload = transport.RecordedTransmissions.First().Payload;
        string sentText = Encoding.Latin1.GetString(sentPayload);
        check(sentText.Contains("HKIPB:2:1+", StringComparison.Ordinal), "Sent wire payload used HKIPB segment.");
        check(sentText.Contains("<LclInstrm><Cd>INST</Cd></LclInstrm>", StringComparison.Ordinal),
            "Sent pain XML embedded within wire bytes included <LclInstrm><Cd>INST</Cd></LclInstrm>.");
    }

    private static FinTsMessageFrame Frame(string wire) =>
        FinTsMessageFrame.Parse(Encoding.Latin1.GetBytes(wire[..10] + Encoding.Latin1.GetByteCount(wire).ToString("D12", CultureInfo.InvariantCulture) + wire[22..]));
}
