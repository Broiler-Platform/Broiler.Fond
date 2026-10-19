using System.Globalization;
using System.Text;
using Broiler.Fond.Kernel.Domain;
using Broiler.Fond.Kernel.Domain.Payments;
using Broiler.Fond.Kernel.FinTs;
using Broiler.Fond.Kernel.FinTs.Transport;

namespace Broiler.Fond.Kernel.Tests.Payments;

public static class ScheduledPaymentTests
{
    public static void RunAll()
    {
        int checkCount = 0;
        Action<bool, string> check = (condition, message) =>
        {
            if (!condition)
            {
                throw new InvalidOperationException($"Scheduled payment assertion failed: {message}");
            }
            checkCount++;
        };

        TestScheduledOrderModel(check);
        TestScheduledPaymentCapability(check);
        TestScheduledPaymentAdvertisement(check);
        TestScheduledPaymentValidation(check);
        TestScheduledOrderCancellationValidation(check);
        TestScheduledTransferWireCodecs(check);
        TestScheduledTransferExecutionFlow(check).GetAwaiter().GetResult();

        Console.WriteLine($"Scheduled SEPA credit transfers & bank order lifecycle (M5-02): {checkCount} checks completed successfully.");
    }

    private static void TestScheduledOrderModel(Action<bool, string> check)
    {
        DateTimeOffset now = DateTimeOffset.UtcNow;
        DateOnly futureDate = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(14));

        ScheduledOrder order = new(
            Id: 500,
            SourceAccountId: 10,
            SourceAccountBindingId: 20,
            SourceAccountIncarnationId: 30,
            BankOrderReference: "BANK-SCHED-001",
            EndToEndId: "BF-20261019-SCHED-01",
            Amount: new Money(120.00m, "EUR"),
            Beneficiary: new PaymentBeneficiary("Landlord", "DE89370400440532013000"),
            Remittance: new PaymentRemittance("Rent November 2026"),
            ScheduledExecutionDate: futureDate,
            Status: ScheduledOrderStatus.Pending,
            CreatedAt: now);

        check(order.Id == 500, "Order ID is 500.");
        check(order.Status == ScheduledOrderStatus.Pending, "Initial status is Pending.");
        check(order.CanCancel, "Pending order can be cancelled.");

        ScheduledOrder executedOrder = order with { Status = ScheduledOrderStatus.Executed };
        check(!executedOrder.CanCancel, "Executed order cannot be cancelled.");

        ScheduledOrder cancelledOrder = order with { Status = ScheduledOrderStatus.Cancelled };
        check(!cancelledOrder.CanCancel, "Cancelled order cannot be cancelled.");
    }

    private static void TestScheduledPaymentCapability(Action<bool, string> check)
    {
        ScheduledPaymentCapability def = ScheduledPaymentCapability.StandardDefault;
        check(def.IsSupported, "Default capability is supported.");
        check(def.MinimumLeadDays == 1, "Default minimum lead days is 1.");
        check(def.MaximumLeadDays == 365, "Default maximum lead days is 365.");
        check(def.CanModify, "Default allows modification.");
        check(def.CanCancel, "Default allows cancellation.");
        check(def.SupportedSegmentCode == "HKCME", "Default segment code is HKCME.");

        ScheduledPaymentCapability unsup = ScheduledPaymentCapability.Unsupported;
        check(!unsup.IsSupported, "Unsupported capability IsSupported is false.");
        check(!unsup.CanCancel, "Unsupported capability CanCancel is false.");
    }

    private static void TestScheduledPaymentAdvertisement(Action<bool, string> check)
    {
        // 1. Parameter set with HICMES segment
        string wireWithHicmes =
            "HNHBK:1:3+000000000000+300+DIA-10023+1+DIA-10023:1'" +
            "HIRMG:2:2+0010::Erfolgreich.'" +
            "HIBPA:3:3:2+1+280:10070000+TestBank+1+1+300'" +
            "HICMES:4:1:3+1+0+0+2:180:J:J'" +
            "HNHBS:5:1+1'";

        FinTsResponse resp = FinTsResponse.Parse(Frame(wireWithHicmes));
        FinTsParameterSet paramSet = FinTsParameterSet.Parse(resp);

        IReadOnlyList<FinTsScheduledPaymentAdvertisement> ads = FinTsScheduledPaymentAdvertisement.ParseAll(paramSet);
        check(ads.Count == 1, "Discovered exactly 1 HICMES advertisement.");
        check(ads[0].Version == 1, "HICMES version is 1.");
        check(ads[0].MinimumLeadDays == 2, "HICMES minimum lead days parsed as 2.");
        check(ads[0].MaximumLeadDays == 180, "HICMES maximum lead days parsed as 180.");
        check(ads[0].CanModify, "HICMES can modify is true.");
        check(ads[0].CanCancel, "HICMES can cancel is true.");

        ScheduledPaymentCapability cap = ads[0].ToCapability();
        check(cap.IsSupported, "Capability IsSupported is true.");
        check(cap.MinimumLeadDays == 2, "Capability MinimumLeadDays is 2.");
        check(cap.MaximumLeadDays == 180, "Capability MaximumLeadDays is 180.");

        // 2. Parameter set without HICMES
        string wireNoHicmes =
            "HNHBK:1:3+000000000000+300+DIA-10023+1+DIA-10023:1'" +
            "HIRMG:2:2+0010::Erfolgreich.'" +
            "HIBPA:3:3:2+1+280:10070000+TestBank+1+1+300'" +
            "HNHBS:4:1+1'";

        FinTsResponse respNo = FinTsResponse.Parse(Frame(wireNoHicmes));
        FinTsParameterSet paramSetNo = FinTsParameterSet.Parse(respNo);

        IReadOnlyList<FinTsScheduledPaymentAdvertisement> adsNo = FinTsScheduledPaymentAdvertisement.ParseAll(paramSetNo);
        check(adsNo.Count == 0, "Discovered zero HICMES advertisements when unadvertised.");
    }

    private static void TestScheduledPaymentValidation(Action<bool, string> check)
    {
        DateOnly today = new(2026, 10, 19);
        DateTimeOffset now = DateTimeOffset.UtcNow;
        ScheduledPaymentCapability cap = new(
            IsSupported: true,
            MinimumLeadDays: 2,
            MaximumLeadDays: 90,
            CanModify: true,
            CanCancel: true,
            SupportedSegmentCode: "HKCME",
            SupportedSegmentVersion: 1);

        PaymentDraft draft = new(
            Id: 10,
            SourceAccountId: 1,
            SourceAccountBindingId: 2,
            SourceAccountIncarnationId: 3,
            CurrentRevisionId: 100,
            Status: PaymentDraftStatus.Draft,
            CreatedAt: now,
            UpdatedAt: now);

        // 1. Valid scheduled draft (5 days in the future, within 2..90 window)
        PaymentDraftRevision validRev = new(
            Id: 100,
            DraftId: draft.Id,
            RevisionNumber: 1,
            PredecessorRevisionId: null,
            Timestamp: now,
            SourceAccountId: 1,
            SourceAccountBindingId: 2,
            SourceAccountIncarnationId: 3,
            Beneficiary: new PaymentBeneficiary("Erika Mustermann", "DE89370400440532013000"),
            Amount: new Money(150.00m, "EUR"),
            Remittance: new PaymentRemittance("Future bill"),
            ExecutionMode: PaymentExecutionMode.ScheduledCreditTransfer,
            ExecutionDate: today.AddDays(5));

        SepaValidationResult validResult = ScheduledPaymentValidator.ValidateDraft(validRev, cap, evaluationDate: today);
        check(validResult.IsValid, "Valid scheduled payment within lead window passes validation.");

        // 2. Unsupported capability
        SepaValidationResult unsupResult = ScheduledPaymentValidator.ValidateDraft(validRev, ScheduledPaymentCapability.Unsupported, evaluationDate: today);
        check(!unsupResult.IsValid, "Unsupported capability fails validation.");
        check(unsupResult.Errors.Any(e => e.Code == SepaValidationErrorCode.ScheduledCapabilityNotSupported),
            "Reports ScheduledCapabilityNotSupported error.");

        // 3. Execution date is today
        PaymentDraftRevision todayRev = validRev with { ExecutionDate = today };
        SepaValidationResult todayResult = ScheduledPaymentValidator.ValidateDraft(todayRev, cap, evaluationDate: today);
        check(!todayResult.IsValid, "Today execution date fails scheduled validation.");
        check(todayResult.Errors.Any(e => e.Code == SepaValidationErrorCode.ScheduledDateInPastOrToday),
            "Reports ScheduledDateInPastOrToday error when date is today.");

        // 4. Execution date is in the past
        PaymentDraftRevision pastRev = validRev with { ExecutionDate = today.AddDays(-1) };
        SepaValidationResult pastResult = ScheduledPaymentValidator.ValidateDraft(pastRev, cap, evaluationDate: today);
        check(!pastResult.IsValid, "Past execution date fails scheduled validation.");
        check(pastResult.Errors.Any(e => e.Code == SepaValidationErrorCode.ExecutionDateInPast),
            "Reports ExecutionDateInPast error when date is in the past.");

        // 5. Execution date earlier than minimum lead days (e.g. tomorrow, when min lead is 2 days)
        PaymentDraftRevision tooSoonRev = validRev with { ExecutionDate = today.AddDays(1) };
        SepaValidationResult tooSoonResult = ScheduledPaymentValidator.ValidateDraft(tooSoonRev, cap, evaluationDate: today);
        check(!tooSoonResult.IsValid, "Date earlier than min lead days fails.");
        check(tooSoonResult.Errors.Any(e => e.Code == SepaValidationErrorCode.ScheduledDateOutsideLeadWindow),
            "Reports ScheduledDateOutsideLeadWindow when date < minLeadDays.");

        // 6. Execution date later than maximum lead days (e.g. 100 days, when max lead is 90 days)
        PaymentDraftRevision tooLateRev = validRev with { ExecutionDate = today.AddDays(100) };
        SepaValidationResult tooLateResult = ScheduledPaymentValidator.ValidateDraft(tooLateRev, cap, evaluationDate: today);
        check(!tooLateResult.IsValid, "Date later than max lead days fails.");
        check(tooLateResult.Errors.Any(e => e.Code == SepaValidationErrorCode.ScheduledDateOutsideLeadWindow),
            "Reports ScheduledDateOutsideLeadWindow when date > maxLeadDays.");
    }

    private static void TestScheduledOrderCancellationValidation(Action<bool, string> check)
    {
        DateTimeOffset now = DateTimeOffset.UtcNow;
        DateOnly futureDate = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(10));

        ScheduledOrder pendingOrder = new(
            Id: 601,
            SourceAccountId: 10,
            SourceAccountBindingId: 20,
            SourceAccountIncarnationId: 30,
            BankOrderReference: "BANK-ORD-601",
            EndToEndId: "BF-E2E-601",
            Amount: new Money(80.00m, "EUR"),
            Beneficiary: new PaymentBeneficiary("Supplier", "DE89370400440532013000"),
            Remittance: new PaymentRemittance("Delivery"),
            ScheduledExecutionDate: futureDate,
            Status: ScheduledOrderStatus.Pending,
            CreatedAt: now);

        ScheduledPaymentCapability capCanCancel = ScheduledPaymentCapability.StandardDefault;
        ScheduledPaymentCapability capCannotCancel = capCanCancel with { CanCancel = false };

        // 1. Pending order with CanCancel capability
        SepaValidationResult resultValid = ScheduledPaymentValidator.ValidateCancellation(pendingOrder, capCanCancel);
        check(resultValid.IsValid, "Cancellation of pending order with supported capability passes.");

        // 2. Bank does not support cancellation
        SepaValidationResult resultNoCap = ScheduledPaymentValidator.ValidateCancellation(pendingOrder, capCannotCancel);
        check(!resultNoCap.IsValid, "Cancellation fails when bank does not support cancellation.");
        check(resultNoCap.Errors.Any(e => e.Code == SepaValidationErrorCode.ScheduledCancellationNotPermitted),
            "Reports ScheduledCancellationNotPermitted when CanCancel is false.");

        // 3. Order is already executed
        ScheduledOrder executedOrder = pendingOrder with { Status = ScheduledOrderStatus.Executed };
        SepaValidationResult resultExecuted = ScheduledPaymentValidator.ValidateCancellation(executedOrder, capCanCancel);
        check(!resultExecuted.IsValid, "Cancellation fails when order is already executed.");
        check(resultExecuted.Errors.Any(e => e.Code == SepaValidationErrorCode.ScheduledCancellationNotPermitted),
            "Reports ScheduledCancellationNotPermitted when status is Executed.");
    }

    private static void TestScheduledTransferWireCodecs(Action<bool, string> check)
    {
        string originIban = "DE89370400440532013000";
        string originBic = "DBBADE33XXX";
        byte[] painXmlBytes = Encoding.UTF8.GetBytes("<Document>ScheduledTransferPayload</Document>");

        // 1. BuildHkcmeSegment
        byte[] hkcmeBytes = FinTsScheduledTransferRequestWriter.BuildHkcmeSegment(
            ordinal: 2,
            version: 1,
            originIban: originIban,
            originBic: originBic,
            painXmlBytes: painXmlBytes);

        string hkcmeText = Encoding.Latin1.GetString(hkcmeBytes);
        check(hkcmeText.StartsWith("HKCME:2:1+", StringComparison.Ordinal), "HKCME segment starts with HKCME:2:1+.");
        check(hkcmeText.Contains(originIban, StringComparison.Ordinal), "HKCME segment contains origin IBAN.");
        check(hkcmeText.Contains(originBic, StringComparison.Ordinal), "HKCME segment contains origin BIC.");
        check(hkcmeText.Contains("@" + painXmlBytes.Length.ToString(CultureInfo.InvariantCulture) + "@", StringComparison.Ordinal),
            "HKCME segment contains binary length tag.");
        check(hkcmeText.EndsWith("'", StringComparison.Ordinal), "HKCME segment ends with single quote.");

        // 2. EncodeUnsignedScheduled
        byte[] wireScheduled = FinTsScheduledTransferRequestWriter.EncodeUnsignedScheduled(
            dialogueId: "DIA-SCHED-01",
            messageNumber: 1,
            version: 1,
            originIban: originIban,
            originBic: originBic,
            painXmlBytes: painXmlBytes);

        string wireText = Encoding.Latin1.GetString(wireScheduled);
        check(wireText.StartsWith("HNHBK:1:3+", StringComparison.Ordinal), "Scheduled message starts with HNHBK header.");
        check(wireText.Contains("HKCME:2:1+", StringComparison.Ordinal), "Scheduled message contains HKCME segment.");
        check(wireText.EndsWith("HNHBS:3:1+1'", StringComparison.Ordinal), "Scheduled message ends with HNHBS trailer.");
        check(wireScheduled.Length == int.Parse(wireText.Substring(10, 12), CultureInfo.InvariantCulture),
            "Scheduled wire length matches 12-digit size header.");

        // 3. BuildHkcbdSegment (Cancellation)
        string hkcbd = FinTsScheduledTransferRequestWriter.BuildHkcbdSegment(
            ordinal: 2,
            version: 1,
            originIban: originIban,
            originBic: originBic,
            orderReference: "BANK-SCHED-REF-99");

        check(hkcbd.StartsWith("HKCBD:2:1+", StringComparison.Ordinal), "HKCBD segment starts with HKCBD:2:1+.");
        check(hkcbd.Contains(originIban, StringComparison.Ordinal), "HKCBD segment contains origin IBAN.");
        check(hkcbd.Contains("BANK-SCHED-REF-99", StringComparison.Ordinal), "HKCBD segment contains order reference.");
        check(hkcbd.EndsWith("'", StringComparison.Ordinal), "HKCBD segment ends with single quote.");

        // 4. EncodeUnsignedCancellation
        byte[] wireCancel = FinTsScheduledTransferRequestWriter.EncodeUnsignedCancellation(
            dialogueId: "DIA-CANCEL-01",
            messageNumber: 1,
            version: 1,
            originIban: originIban,
            originBic: originBic,
            orderReference: "BANK-SCHED-REF-99");

        string cancelText = Encoding.Latin1.GetString(wireCancel);
        check(cancelText.StartsWith("HNHBK:1:3+", StringComparison.Ordinal), "Cancellation message starts with HNHBK header.");
        check(cancelText.Contains("HKCBD:2:1+", StringComparison.Ordinal), "Cancellation message contains HKCBD segment.");
        check(cancelText.EndsWith("HNHBS:3:1+1'", StringComparison.Ordinal), "Cancellation message ends with HNHBS trailer.");
    }

    private static async Task TestScheduledTransferExecutionFlow(Action<bool, string> check)
    {
        DateOnly futureDate = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(7));
        DateTimeOffset now = DateTimeOffset.UtcNow;

        PaymentDraft draft = new(
            Id: 50,
            SourceAccountId: 10,
            SourceAccountBindingId: 20,
            SourceAccountIncarnationId: 30,
            CurrentRevisionId: 501,
            Status: PaymentDraftStatus.Draft,
            CreatedAt: now,
            UpdatedAt: now);

        PaymentDraftRevision scheduledDraft = new(
            Id: 501,
            DraftId: draft.Id,
            RevisionNumber: 1,
            PredecessorRevisionId: null,
            Timestamp: now,
            SourceAccountId: 10,
            SourceAccountBindingId: 20,
            SourceAccountIncarnationId: 30,
            Beneficiary: new PaymentBeneficiary("Insurance AG", "DE89370400440532013000"),
            Amount: new Money(200.00m, "EUR"),
            Remittance: new PaymentRemittance("Policy 12345"),
            ExecutionMode: PaymentExecutionMode.ScheduledCreditTransfer,
            ExecutionDate: futureDate);

        PaymentAttempt attempt = PaymentAttempt.CreatePreSendAttempt(60, scheduledDraft, now);

        string rawSuccessResponse =
            "HNHBK:1:3+000000000000+300+D-TEST-99+1+D-TEST-99:1'" +
            "HIRMG:2:2+0010::Nachricht entgegengenommen'" +
            "HIRMS:3:2:2+0020::Terminauftrag entgegengenommen'" +
            "HICME:4:1:2+BANK-SCHED-ORD-88'" +
            "HNHBS:5:1+1'";
        FinTsMessageFrame successFrame = Frame(rawSuccessResponse);

        using var transport = new FinTsSimulatedTransport();
        transport.EnqueueResponse(successFrame.Syntax.CopyWireBytes());

        SepaTransferExecutionResult result = await SepaTransferExecutionCoordinator.InitiateTransferAsync(
            attempt: attempt,
            draftRevision: scheduledDraft,
            debtorName: "Max Mustermann",
            debtorIban: "DE89370400440532013000",
            debtorBic: "DBBADE33XXX",
            endpoint: new Uri("https://bank.example/fints"),
            transport: transport,
            dialogueId: "DIA-SCHED-999",
            messageNumber: 1,
            hkccsVersion: 1,
            transitionIdStart: 1);

        check(result.IsSuccess, "Scheduled payment submission succeeds.");
        check(result.UpdatedAttempt.State == PaymentState.Accepted, "Target state is Accepted.");
        check(result.UpdatedAttempt.BankReference == "BANK-SCHED-ORD-88", "Bank reference captured from HICME segment.");

        check(transport.TransmittedCount == 1, "Transport transmitted 1 message.");
        byte[] sentPayload = transport.RecordedTransmissions.First().Payload;
        string sentText = Encoding.Latin1.GetString(sentPayload);
        check(sentText.Contains("HKCME:2:1+", StringComparison.Ordinal), "Transmitted wire payload used HKCME segment.");
        check(sentText.Contains(futureDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture), StringComparison.Ordinal),
            "Transmitted pain payload contained requested future execution date.");
    }

    private static FinTsMessageFrame Frame(string wire) =>
        FinTsMessageFrame.Parse(Encoding.Latin1.GetBytes(wire[..10] + Encoding.Latin1.GetByteCount(wire).ToString("D12", CultureInfo.InvariantCulture) + wire[22..]));
}
