using System.Globalization;
using System.Text;
using Broiler.Fond.Kernel.Domain;
using Broiler.Fond.Kernel.Domain.Payments;
using Broiler.Fond.Kernel.FinTs;

namespace Broiler.Fond.Kernel.Tests.Payments;

public static class StandingOrderTests
{
    public static void RunAll()
    {
        int checkCount = 0;
        Action<bool, string> check = (condition, message) =>
        {
            if (!condition)
            {
                throw new InvalidOperationException($"Standing order assertion failed: {message}");
            }
            checkCount++;
        };

        TestStandingOrderDomainModel(check);
        TestStandingOrderCapability(check);
        TestStandingOrderAdvertisement(check);
        TestStandingOrderScheduleCalculator(check);
        TestStandingOrderValidation(check);
        TestStandingOrderModificationAndDeletionValidation(check);
        TestStandingOrderWireCodecs(check);
        TestStandingOrderResponseParser(check);

        Console.WriteLine($"Standing orders management (M5-03): {checkCount} checks completed successfully.");
    }

    private static void TestStandingOrderDomainModel(Action<bool, string> check)
    {
        check((int)StandingOrderFrequency.Monthly == 1, "Monthly frequency is 1.");
        check((int)StandingOrderFrequency.TwoMonthly == 2, "TwoMonthly frequency is 2.");
        check((int)StandingOrderFrequency.Quarterly == 3, "Quarterly frequency is 3.");
        check((int)StandingOrderFrequency.SemiAnnually == 6, "SemiAnnually frequency is 6.");
        check((int)StandingOrderFrequency.Annually == 12, "Annually frequency is 12.");

        check((int)StandingOrderStatus.Active == 1, "Active status is 1.");
        check((int)StandingOrderStatus.ModificationPending == 2, "ModificationPending status is 2.");
        check((int)StandingOrderStatus.CancellationPending == 3, "CancellationPending status is 3.");
        check((int)StandingOrderStatus.Cancelled == 4, "Cancelled status is 4.");

        DateTimeOffset now = DateTimeOffset.UtcNow;
        DateOnly startDate = new(2026, 11, 1);
        DateOnly endDate = new(2027, 11, 1);

        StandingOrder order = new(
            Id: 701,
            SourceAccountId: 10,
            SourceAccountBindingId: 20,
            SourceAccountIncarnationId: 30,
            BankOrderReference: "BANK-DA-701",
            Amount: new Money(45.00m, "EUR"),
            Beneficiary: new PaymentBeneficiary("Streaming Service", "DE89370400440532013000"),
            Remittance: new PaymentRemittance("Sub 9988"),
            Frequency: StandingOrderFrequency.Monthly,
            ExecutionDay: 1,
            FirstExecutionDate: startDate,
            LastExecutionDate: endDate,
            Status: StandingOrderStatus.Active,
            CreatedAt: now);

        check(order.CanModify, "Active standing order can be modified.");
        check(order.CanCancel, "Active standing order can be cancelled.");

        StandingOrder modPending = order with { Status = StandingOrderStatus.ModificationPending };
        check(!modPending.CanModify, "ModificationPending order cannot be modified again.");

        StandingOrder cancelled = order with { Status = StandingOrderStatus.Cancelled };
        check(!cancelled.CanModify, "Cancelled order cannot be modified.");
        check(!cancelled.CanCancel, "Cancelled order cannot be cancelled.");
    }

    private static void TestStandingOrderCapability(Action<bool, string> check)
    {
        StandingOrderCapability def = StandingOrderCapability.StandardDefault;
        check(def.IsSupported, "Default capability is supported.");
        check(def.CanCreate, "Default capability can create.");
        check(def.CanModify, "Default capability can modify.");
        check(def.CanDelete, "Default capability can delete.");
        check(def.SupportedFrequencies.Count == 5, "Default capability supports 5 standard frequencies.");
        check(def.MinimumLeadDays == 1, "Default lead days is 1.");

        StandingOrderCapability unsup = StandingOrderCapability.Unsupported;
        check(!unsup.IsSupported, "Unsupported IsSupported is false.");
        check(!unsup.CanCreate, "Unsupported CanCreate is false.");
        check(unsup.SupportedFrequencies.Count == 0, "Unsupported has 0 frequencies.");
    }

    private static void TestStandingOrderAdvertisement(Action<bool, string> check)
    {
        // 1. Parameter set with HICDBS, HICDES, HICDAS, HICDLS
        string wireWithAll =
            "HNHBK:1:3+000000000000+300+DIA-10023+1+DIA-10023:1'" +
            "HIRMG:2:2+0010::Erfolgreich.'" +
            "HIBPA:3:3:2+1+280:10070000+TestBank+1+1+300'" +
            "HICDBS:4:1:3+1+0+0'" +
            "HICDES:5:1:3+1+0+0+3+1:3:12'" +
            "HICDAS:6:1:3+1+0+0'" +
            "HICDLS:7:1:3+1+0+0'" +
            "HNHBS:8:1+1'";

        FinTsResponse resp = FinTsResponse.Parse(Frame(wireWithAll));
        FinTsParameterSet paramSet = FinTsParameterSet.Parse(resp);

        StandingOrderCapability cap = FinTsStandingOrderAdvertisement.DiscoverCapability(paramSet);
        check(cap.IsSupported, "Discovered capability is supported.");
        check(cap.CanCreate, "Discovered capability can create.");
        check(cap.CanModify, "Discovered capability can modify.");
        check(cap.CanDelete, "Discovered capability can delete.");
        check(cap.MinimumLeadDays == 3, "Minimum lead days parsed as 3.");
        check(cap.SupportedFrequencies.SequenceEqual([StandingOrderFrequency.Monthly, StandingOrderFrequency.Quarterly, StandingOrderFrequency.Annually]),
            "Supported frequencies parsed correctly from BPD.");

        // 2. Parameter set without standing order segments
        string wireNone =
            "HNHBK:1:3+000000000000+300+DIA-10023+1+DIA-10023:1'" +
            "HIRMG:2:2+0010::Erfolgreich.'" +
            "HIBPA:3:3:2+1+280:10070000+TestBank+1+1+300'" +
            "HNHBS:4:1+1'";

        FinTsResponse respNone = FinTsResponse.Parse(Frame(wireNone));
        FinTsParameterSet paramSetNone = FinTsParameterSet.Parse(respNone);

        StandingOrderCapability capNone = FinTsStandingOrderAdvertisement.DiscoverCapability(paramSetNone);
        check(!capNone.IsSupported, "Unadvertised parameter set discovers unsupported capability.");
    }

    private static void TestStandingOrderScheduleCalculator(Action<bool, string> check)
    {
        DateOnly start = new(2026, 1, 15);

        // 1. Monthly cadence on the 15th
        DateOnly refJan10 = new(2026, 1, 10);
        DateOnly? next1 = StandingOrderScheduleCalculator.CalculateNextExecutionDate(
            StandingOrderFrequency.Monthly,
            executionDay: 15,
            firstExecutionDate: start,
            lastExecutionDate: null,
            referenceDate: refJan10);
        check(next1 == new DateOnly(2026, 1, 15), "Next date before start date resolves to first run on 2026-01-15.");

        DateOnly refJan15 = new(2026, 1, 15);
        DateOnly? next2 = StandingOrderScheduleCalculator.CalculateNextExecutionDate(
            StandingOrderFrequency.Monthly,
            executionDay: 15,
            firstExecutionDate: start,
            lastExecutionDate: null,
            referenceDate: refJan15);
        check(next2 == new DateOnly(2026, 2, 15), "Next date after Jan 15 resolves to Feb 15.");

        // 2. Month-end clamping (Day 31 in February and April)
        DateOnly start31 = new(2026, 1, 31);
        DateOnly? febClamped = StandingOrderScheduleCalculator.CalculateNextExecutionDate(
            StandingOrderFrequency.Monthly,
            executionDay: 31,
            firstExecutionDate: start31,
            lastExecutionDate: null,
            referenceDate: new DateOnly(2026, 1, 31));
        check(febClamped == new DateOnly(2026, 2, 28), "Day 31 clamped to Feb 28 in non-leap year 2026.");

        DateOnly? aprClamped = StandingOrderScheduleCalculator.CalculateNextExecutionDate(
            StandingOrderFrequency.Monthly,
            executionDay: 31,
            firstExecutionDate: start31,
            lastExecutionDate: null,
            referenceDate: new DateOnly(2026, 3, 31));
        check(aprClamped == new DateOnly(2026, 4, 30), "Day 31 clamped to Apr 30.");

        // 3. Quarterly cadence (every 3 months)
        DateOnly? qNext = StandingOrderScheduleCalculator.CalculateNextExecutionDate(
            StandingOrderFrequency.Quarterly,
            executionDay: 1,
            firstExecutionDate: new DateOnly(2026, 1, 1),
            lastExecutionDate: null,
            referenceDate: new DateOnly(2026, 1, 1));
        check(qNext == new DateOnly(2026, 4, 1), "Quarterly cadence moves from Jan 1 to Apr 1.");

        // 4. Expiration date handling (LastExecutionDate)
        DateOnly lastDate = new(2026, 3, 31);
        DateOnly? expired = StandingOrderScheduleCalculator.CalculateNextExecutionDate(
            StandingOrderFrequency.Monthly,
            executionDay: 15,
            firstExecutionDate: start,
            lastExecutionDate: lastDate,
            referenceDate: new DateOnly(2026, 3, 15));
        check(expired is null, "Returns null once calculated next date (April 15) exceeds LastExecutionDate (March 31).");

        // 5. CalculateUpcomingDates
        StandingOrder order = new(
            Id: 1,
            SourceAccountId: 10,
            SourceAccountBindingId: 20,
            SourceAccountIncarnationId: 30,
            BankOrderReference: "ORD-1",
            Amount: new Money(100m, "EUR"),
            Beneficiary: new PaymentBeneficiary("Club", "DE89370400440532013000"),
            Remittance: new PaymentRemittance("Fee"),
            Frequency: StandingOrderFrequency.Monthly,
            ExecutionDay: 10,
            FirstExecutionDate: new DateOnly(2026, 1, 10),
            LastExecutionDate: new DateOnly(2026, 4, 10),
            Status: StandingOrderStatus.Active,
            CreatedAt: DateTimeOffset.UtcNow);

        var upcoming = StandingOrderScheduleCalculator.CalculateUpcomingDates(order, new DateOnly(2026, 1, 1), 5);
        check(upcoming.Count == 4, "Generates exactly 4 dates before expiring (Jan, Feb, Mar, Apr 10).");
        check(upcoming[0] == new DateOnly(2026, 1, 10), "First upcoming date is Jan 10.");
        check(upcoming[3] == new DateOnly(2026, 4, 10), "Last upcoming date is Apr 10.");
    }

    private static void TestStandingOrderValidation(Action<bool, string> check)
    {
        DateOnly today = new(2026, 10, 19);
        DateOnly validStart = today.AddDays(5);
        StandingOrderCapability cap = StandingOrderCapability.StandardDefault;
        PaymentBeneficiary validBeneficiary = new("Landlord", "DE89370400440532013000");

        // 1. Valid setup passes
        SepaValidationResult validResult = StandingOrderValidator.ValidateSetup(
            amount: new Money(500.00m, "EUR"),
            beneficiary: validBeneficiary,
            remittance: new PaymentRemittance("Rent"),
            frequency: StandingOrderFrequency.Monthly,
            executionDay: 1,
            firstExecutionDate: validStart,
            lastExecutionDate: validStart.AddYears(1),
            capability: cap,
            evaluationDate: today);
        check(validResult.IsValid, "Valid standing order setup passes validation.");

        // 2. Unsupported capability
        SepaValidationResult unsupResult = StandingOrderValidator.ValidateSetup(
            amount: new Money(500.00m, "EUR"),
            beneficiary: validBeneficiary,
            remittance: new PaymentRemittance("Rent"),
            frequency: StandingOrderFrequency.Monthly,
            executionDay: 1,
            firstExecutionDate: validStart,
            lastExecutionDate: null,
            capability: StandingOrderCapability.Unsupported,
            evaluationDate: today);
        check(!unsupResult.IsValid, "Unsupported capability fails validation.");
        check(unsupResult.Errors.Any(e => e.Code == SepaValidationErrorCode.StandingOrderCapabilityNotSupported),
            "Reports StandingOrderCapabilityNotSupported error.");

        // 3. Unsupported frequency
        StandingOrderCapability limitedCap = cap with { SupportedFrequencies = [StandingOrderFrequency.Monthly] };
        SepaValidationResult freqResult = StandingOrderValidator.ValidateSetup(
            amount: new Money(500.00m, "EUR"),
            beneficiary: validBeneficiary,
            remittance: new PaymentRemittance("Rent"),
            frequency: StandingOrderFrequency.Quarterly,
            executionDay: 1,
            firstExecutionDate: validStart,
            lastExecutionDate: null,
            capability: limitedCap,
            evaluationDate: today);
        check(!freqResult.IsValid, "Unsupported frequency fails validation.");
        check(freqResult.Errors.Any(e => e.Code == SepaValidationErrorCode.StandingOrderFrequencyNotSupported),
            "Reports StandingOrderFrequencyNotSupported error.");

        // 4. Invalid execution day (e.g. 0 or 32)
        SepaValidationResult day0Result = StandingOrderValidator.ValidateSetup(
            amount: new Money(500.00m, "EUR"),
            beneficiary: validBeneficiary,
            remittance: new PaymentRemittance("Rent"),
            frequency: StandingOrderFrequency.Monthly,
            executionDay: 0,
            firstExecutionDate: validStart,
            lastExecutionDate: null,
            capability: cap,
            evaluationDate: today);
        check(!day0Result.IsValid, "Execution day 0 fails validation.");
        check(day0Result.Errors.Any(e => e.Code == SepaValidationErrorCode.StandingOrderInvalidExecutionDay),
            "Reports StandingOrderInvalidExecutionDay error.");

        SepaValidationResult day32Result = StandingOrderValidator.ValidateSetup(
            amount: new Money(500.00m, "EUR"),
            beneficiary: validBeneficiary,
            remittance: new PaymentRemittance("Rent"),
            frequency: StandingOrderFrequency.Monthly,
            executionDay: 32,
            firstExecutionDate: validStart,
            lastExecutionDate: null,
            capability: cap,
            evaluationDate: today);
        check(!day32Result.IsValid, "Execution day 32 fails validation.");
        check(day32Result.Errors.Any(e => e.Code == SepaValidationErrorCode.StandingOrderInvalidExecutionDay),
            "Reports StandingOrderInvalidExecutionDay error.");

        // 5. First execution date in the past
        SepaValidationResult pastResult = StandingOrderValidator.ValidateSetup(
            amount: new Money(500.00m, "EUR"),
            beneficiary: validBeneficiary,
            remittance: new PaymentRemittance("Rent"),
            frequency: StandingOrderFrequency.Monthly,
            executionDay: 1,
            firstExecutionDate: today.AddDays(-1),
            lastExecutionDate: null,
            capability: cap,
            evaluationDate: today);
        check(!pastResult.IsValid, "Past first execution date fails validation.");
        check(pastResult.Errors.Any(e => e.Code == SepaValidationErrorCode.StandingOrderFirstExecutionDateInPast),
            "Reports StandingOrderFirstExecutionDateInPast error.");

        // 6. Last execution date before or equal to first execution date
        SepaValidationResult lastBeforeFirst = StandingOrderValidator.ValidateSetup(
            amount: new Money(500.00m, "EUR"),
            beneficiary: validBeneficiary,
            remittance: new PaymentRemittance("Rent"),
            frequency: StandingOrderFrequency.Monthly,
            executionDay: 1,
            firstExecutionDate: validStart,
            lastExecutionDate: validStart,
            capability: cap,
            evaluationDate: today);
        check(!lastBeforeFirst.IsValid, "Last execution date equal to first fails.");
        check(lastBeforeFirst.Errors.Any(e => e.Code == SepaValidationErrorCode.StandingOrderLastExecutionDateBeforeFirst),
            "Reports StandingOrderLastExecutionDateBeforeFirst error.");

        // 7. Non-EUR currency or negative amount
        SepaValidationResult nonEurResult = StandingOrderValidator.ValidateSetup(
            amount: new Money(500.00m, "USD"),
            beneficiary: validBeneficiary,
            remittance: new PaymentRemittance("Rent"),
            frequency: StandingOrderFrequency.Monthly,
            executionDay: 1,
            firstExecutionDate: validStart,
            lastExecutionDate: null,
            capability: cap,
            evaluationDate: today);
        check(!nonEurResult.IsValid, "Non-EUR currency fails validation.");
        check(nonEurResult.Errors.Any(e => e.Code == SepaValidationErrorCode.InvalidCurrency),
            "Reports InvalidCurrency error.");
    }

    private static void TestStandingOrderModificationAndDeletionValidation(Action<bool, string> check)
    {
        DateOnly start = new(2026, 11, 1);
        StandingOrderCapability cap = StandingOrderCapability.StandardDefault;

        StandingOrder activeOrder = new(
            Id: 801,
            SourceAccountId: 10,
            SourceAccountBindingId: 20,
            SourceAccountIncarnationId: 30,
            BankOrderReference: "BANK-DA-801",
            Amount: new Money(60.00m, "EUR"),
            Beneficiary: new PaymentBeneficiary("Gym", "DE89370400440532013000"),
            Remittance: new PaymentRemittance("Membership"),
            Frequency: StandingOrderFrequency.Monthly,
            ExecutionDay: 1,
            FirstExecutionDate: start,
            LastExecutionDate: null,
            Status: StandingOrderStatus.Active,
            CreatedAt: DateTimeOffset.UtcNow);

        // 1. Valid modification
        SepaValidationResult modValid = StandingOrderValidator.ValidateModification(
            activeOrder,
            newAmount: new Money(65.00m, "EUR"),
            newFrequency: StandingOrderFrequency.Monthly,
            newExecutionDay: 15,
            newLastExecutionDate: start.AddYears(2),
            capability: cap);
        check(modValid.IsValid, "Valid modification passes.");

        // 2. Modify cancelled order fails
        StandingOrder cancelledOrder = activeOrder with { Status = StandingOrderStatus.Cancelled };
        SepaValidationResult modCancelled = StandingOrderValidator.ValidateModification(
            cancelledOrder,
            newAmount: new Money(65.00m, "EUR"),
            newFrequency: StandingOrderFrequency.Monthly,
            newExecutionDay: 15,
            newLastExecutionDate: null,
            capability: cap);
        check(!modCancelled.IsValid, "Modifying cancelled order fails.");
        check(modCancelled.Errors.Any(e => e.Code == SepaValidationErrorCode.StandingOrderActionNotPermitted),
            "Reports StandingOrderActionNotPermitted for cancelled order.");

        // 3. Valid deletion
        SepaValidationResult delValid = StandingOrderValidator.ValidateDeletion(activeOrder, cap);
        check(delValid.IsValid, "Valid deletion passes.");

        // 4. Delete when capability disabled
        StandingOrderCapability noDelCap = cap with { CanDelete = false };
        SepaValidationResult delNoCap = StandingOrderValidator.ValidateDeletion(activeOrder, noDelCap);
        check(!delNoCap.IsValid, "Deletion fails when bank does not support online deletion.");
        check(delNoCap.Errors.Any(e => e.Code == SepaValidationErrorCode.StandingOrderCapabilityNotSupported),
            "Reports StandingOrderCapabilityNotSupported when CanDelete is false.");
    }

    private static void TestStandingOrderWireCodecs(Action<bool, string> check)
    {
        string iban = "DE89370400440532013000";
        string bic = "DBBADE33XXX";
        byte[] painBytes = Encoding.UTF8.GetBytes("<Document>StandingOrderPainPayload</Document>");

        // 1. HKCDB List Query
        string hkcdb = FinTsStandingOrderRequestWriter.BuildHkcdbSegment(2, 1, iban, bic);
        check(hkcdb.StartsWith("HKCDB:2:1+", StringComparison.Ordinal), "HKCDB starts with HKCDB:2:1+.");
        check(hkcdb.Contains(iban, StringComparison.Ordinal), "HKCDB contains IBAN.");
        check(hkcdb.EndsWith("'", StringComparison.Ordinal), "HKCDB ends with single quote.");

        byte[] listMsg = FinTsStandingOrderRequestWriter.EncodeUnsignedListQuery("DIA-DA-01", 1, 1, iban, bic);
        string listMsgText = Encoding.Latin1.GetString(listMsg);
        check(listMsgText.StartsWith("HNHBK:1:3+", StringComparison.Ordinal), "List query starts with HNHBK header.");
        check(listMsgText.Contains("HKCDB:2:1+", StringComparison.Ordinal), "List query contains HKCDB segment.");
        check(listMsgText.EndsWith("HNHBS:3:1+1'", StringComparison.Ordinal), "List query ends with HNHBS trailer.");

        // 2. HKCDE Setup
        byte[] hkcde = FinTsStandingOrderRequestWriter.BuildHkcdeSegment(
            ordinal: 2,
            version: 1,
            originIban: iban,
            originBic: bic,
            frequency: StandingOrderFrequency.Monthly,
            executionDay: 1,
            firstExecutionDate: new DateOnly(2026, 11, 1),
            lastExecutionDate: new DateOnly(2027, 11, 1),
            painXmlBytes: painBytes);

        string hkcdeText = Encoding.Latin1.GetString(hkcde);
        check(hkcdeText.StartsWith("HKCDE:2:1+", StringComparison.Ordinal), "HKCDE starts with HKCDE:2:1+.");
        check(hkcdeText.Contains("+1+1+20261101+20271101+", StringComparison.Ordinal), "HKCDE contains frequency, day, start and end dates.");
        check(hkcdeText.Contains("@" + painBytes.Length.ToString(CultureInfo.InvariantCulture) + "@", StringComparison.Ordinal),
            "HKCDE contains binary length tag.");

        byte[] setupMsg = FinTsStandingOrderRequestWriter.EncodeUnsignedSetup(
            dialogueId: "DIA-DA-02",
            messageNumber: 1,
            version: 1,
            originIban: iban,
            originBic: bic,
            frequency: StandingOrderFrequency.Monthly,
            executionDay: 1,
            firstExecutionDate: new DateOnly(2026, 11, 1),
            lastExecutionDate: null,
            painXmlBytes: painBytes);

        string setupMsgText = Encoding.Latin1.GetString(setupMsg);
        check(setupMsgText.Contains("HKCDE:2:1+", StringComparison.Ordinal), "Setup message contains HKCDE segment.");

        // 3. HKCDA Modify
        byte[] hkcda = FinTsStandingOrderRequestWriter.BuildHkcdaSegment(
            ordinal: 2,
            version: 1,
            originIban: iban,
            originBic: bic,
            orderReference: "BANK-DA-REF-77",
            frequency: StandingOrderFrequency.Quarterly,
            executionDay: 15,
            firstExecutionDate: new DateOnly(2026, 11, 15),
            lastExecutionDate: null,
            painXmlBytes: painBytes);

        string hkcdaText = Encoding.Latin1.GetString(hkcda);
        check(hkcdaText.StartsWith("HKCDA:2:1+", StringComparison.Ordinal), "HKCDA starts with HKCDA:2:1+.");
        check(hkcdaText.Contains("BANK-DA-REF-77", StringComparison.Ordinal), "HKCDA contains order reference.");
        check(hkcdaText.Contains("+3+15+20261115++", StringComparison.Ordinal), "HKCDA contains quarterly frequency, day 15, and start date.");

        // 4. HKCDL Delete
        string hkcdl = FinTsStandingOrderRequestWriter.BuildHkcdlSegment(2, 1, iban, bic, "BANK-DA-REF-77");
        check(hkcdl.StartsWith("HKCDL:2:1+", StringComparison.Ordinal), "HKCDL starts with HKCDL:2:1+.");
        check(hkcdl.Contains("BANK-DA-REF-77", StringComparison.Ordinal), "HKCDL contains order reference.");
        check(hkcdl.EndsWith("'", StringComparison.Ordinal), "HKCDL ends with single quote.");

        byte[] delMsg = FinTsStandingOrderRequestWriter.EncodeUnsignedDelete("DIA-DA-03", 1, 1, iban, bic, "BANK-DA-REF-77");
        string delMsgText = Encoding.Latin1.GetString(delMsg);
        check(delMsgText.Contains("HKCDL:2:1+", StringComparison.Ordinal), "Delete message contains HKCDL segment.");
    }

    private static void TestStandingOrderResponseParser(Action<bool, string> check)
    {
        // 1. Parse HICDB list response
        string wireList =
            "HNHBK:1:3+000000000000+300+DIA-10023+1+DIA-10023:1'" +
            "HIRMG:2:2+0010::Erfolgreich.'" +
            "HICDB:3:1:2+DE89370400440532013000:DBBADE33XXX+REF-DA-01+1+1+20261101+20271101+450,00:EUR+Housing GmbH+DE12500105170648489890+Apartment rent'" +
            "HICDB:4:1:2+DE89370400440532013000:DBBADE33XXX+REF-DA-02+3+15+20261215++89,90:EUR+Insurance AG+DE12500105170648489890+Quarterly fee'" +
            "HNHBS:5:1+1'";

        FinTsResponse resp = FinTsResponse.Parse(Frame(wireList));
        var orders = FinTsStandingOrderResponseParser.ParseDiscoveredOrders(resp);

        check(orders.Count == 2, "Discovered exactly 2 standing orders from HICDB response.");

        // First order
        check(orders[0].BankOrderReference == "REF-DA-01", "Order 1 reference matches.");
        check(orders[0].Frequency == StandingOrderFrequency.Monthly, "Order 1 frequency is Monthly.");
        check(orders[0].ExecutionDay == 1, "Order 1 execution day is 1.");
        check(orders[0].FirstExecutionDate == new DateOnly(2026, 11, 1), "Order 1 first date is 2026-11-01.");
        check(orders[0].LastExecutionDate == new DateOnly(2027, 11, 1), "Order 1 last date is 2027-11-01.");
        check(orders[0].Amount == 450.00m, "Order 1 amount is 450.00 EUR.");
        check(orders[0].CreditorName == "Housing GmbH", "Order 1 creditor name parsed.");
        check(orders[0].RemittanceText == "Apartment rent", "Order 1 remittance parsed.");

        // Second order
        check(orders[1].BankOrderReference == "REF-DA-02", "Order 2 reference matches.");
        check(orders[1].Frequency == StandingOrderFrequency.Quarterly, "Order 2 frequency is Quarterly.");
        check(orders[1].ExecutionDay == 15, "Order 2 execution day is 15.");
        check(orders[1].LastExecutionDate is null, "Order 2 last execution date is null.");
        check(orders[1].Amount == 89.90m, "Order 2 amount is 89.90 EUR.");

        // 2. Extract confirmation reference from HICDE
        string wireConfirm =
            "HNHBK:1:3+000000000000+300+DIA-10023+1+DIA-10023:1'" +
            "HIRMG:2:2+0010::Nachricht entgegengenommen.'" +
            "HIRMS:3:2:2+0020::Dauerauftrag eingerichtet'" +
            "HICDE:4:1:2+BANK-NEW-DA-999'" +
            "HNHBS:5:1+1'";

        FinTsResponse respConfirm = FinTsResponse.Parse(Frame(wireConfirm));
        string? refConfirm = FinTsStandingOrderResponseParser.ExtractOrderReference(respConfirm);
        check(refConfirm == "BANK-NEW-DA-999", "Order reference extracted from HICDE segment.");
    }

    private static FinTsMessageFrame Frame(string wire) =>
        FinTsMessageFrame.Parse(Encoding.Latin1.GetBytes(wire[..10] + Encoding.Latin1.GetByteCount(wire).ToString("D12", CultureInfo.InvariantCulture) + wire[22..]));
}
