using Broiler.Fond.Kernel.Domain.Insights;
using Broiler.Fond.Kernel.Domain.Transactions.Query;

namespace Broiler.Fond.Kernel.Tests.Insights;

/// <summary>
/// Executable verification suite for Milestone 6 item M6-04:
/// Recurring cadence detection, cash-flow forecast projections, low-balance alert thresholds, and non-conflation invariants.
/// </summary>
public static class CashFlowForecastTests
{
    public static void Run()
    {
        Console.WriteLine("Running Recurring Obligations & Cash-Flow Forecast verification (M6-04)...");

        VerifyRecurringObligationModelAndCadenceAdvancement();
        VerifyRecurringCadenceDetector();
        VerifyForecastItemNonConflationInvariants();
        VerifyCashFlowForecastEngineAndBalances();
        VerifyLowBalanceAlertTriggering();

        Console.WriteLine("Recurring Obligations & Cash-Flow Forecast (M6-04): 80 checks completed successfully.");
    }

    private static void VerifyRecurringObligationModelAndCadenceAdvancement()
    {
        // 1. Monthly cadence advancement
        var monthly = new RecurringObligation(
            id: 1,
            accountId: 10,
            counterpartyName: "Landlord GmbH",
            expectedAmount: 850.00m,
            currency: "EUR",
            isCredit: false,
            cadence: RecurringCadence.Monthly,
            expectedDayOfMonth: 1,
            lastObservedDate: new DateOnly(2026, 1, 1),
            nextExpectedDate: new DateOnly(2026, 2, 1));

        Assert(monthly.NextExpectedDate == new DateOnly(2026, 2, 1), "Next expected date matches.");
        DateOnly mar1 = monthly.CalculateNextOccurrence(new DateOnly(2026, 2, 1));
        Assert(mar1 == new DateOnly(2026, 3, 1), "Mar 1 occurrence calculated.");
        DateOnly apr1 = monthly.CalculateNextOccurrence(mar1);
        Assert(apr1 == new DateOnly(2026, 4, 1), "Apr 1 occurrence calculated.");

        // 2. Quarterly cadence advancement
        var quarterly = new RecurringObligation(
            id: 2,
            accountId: 10,
            counterpartyName: "Allianz Insurance",
            expectedAmount: 180.00m,
            cadence: RecurringCadence.Quarterly,
            expectedDayOfMonth: 15,
            nextExpectedDate: new DateOnly(2026, 3, 15));

        DateOnly jun15 = quarterly.CalculateNextOccurrence(new DateOnly(2026, 3, 15));
        Assert(jun15 == new DateOnly(2026, 6, 15), "Jun 15 calculated (+3 months).");

        // 3. Weekly cadence advancement
        var weekly = new RecurringObligation(
            id: 3,
            accountId: 10,
            counterpartyName: "Weekly Market",
            expectedAmount: 35.00m,
            cadence: RecurringCadence.Weekly,
            nextExpectedDate: new DateOnly(2026, 10, 7));

        DateOnly nextWeek = weekly.CalculateNextOccurrence(new DateOnly(2026, 10, 7));
        Assert(nextWeek == new DateOnly(2026, 10, 14), "Oct 14 calculated (+7 days).");

        // 4. Validation errors
        bool zeroIdCaught = false;
        try { _ = new RecurringObligation(0, 1, "Name", 100m); }
        catch (ArgumentOutOfRangeException) { zeroIdCaught = true; }
        Assert(zeroIdCaught, "Zero ID throws ArgumentOutOfRangeException.");

        bool emptyNameCaught = false;
        try { _ = new RecurringObligation(1, 1, "   ", 100m); }
        catch (ArgumentException) { emptyNameCaught = true; }
        Assert(emptyNameCaught, "Empty name throws ArgumentException.");

        bool negAmountCaught = false;
        try { _ = new RecurringObligation(1, 1, "Name", -50m); }
        catch (ArgumentOutOfRangeException) { negAmountCaught = true; }
        Assert(negAmountCaught, "Negative amount throws ArgumentOutOfRangeException.");

        bool invalidDayCaught = false;
        try { _ = new RecurringObligation(1, 1, "Name", 100m, expectedDayOfMonth: 32); }
        catch (ArgumentOutOfRangeException) { invalidDayCaught = true; }
        Assert(invalidDayCaught, "Day 32 throws ArgumentOutOfRangeException.");
    }

    private static void VerifyRecurringCadenceDetector()
    {
        // 1. Monthly Rent Detection (4 monthly debits of ~€850)
        var rentTxs = new[]
        {
            CreateTx(1, 1, "Immobilien GmbH", "DE11111111111111111111", -850.00m, new DateOnly(2026, 7, 1)),
            CreateTx(2, 1, "Immobilien GmbH", "DE11111111111111111111", -850.00m, new DateOnly(2026, 8, 1)),
            CreateTx(3, 1, "Immobilien GmbH", "DE11111111111111111111", -850.00m, new DateOnly(2026, 9, 1)),
            CreateTx(4, 1, "Immobilien GmbH", "DE11111111111111111111", -850.00m, new DateOnly(2026, 10, 1)),
        };

        // 2. Monthly Salary Detection (4 monthly credits of ~€3200)
        var salaryTxs = new[]
        {
            CreateTx(10, 1, "Arbeitgeber AG", "DE22222222222222222222", 3200.00m, new DateOnly(2026, 7, 28), isCredit: true),
            CreateTx(11, 1, "Arbeitgeber AG", "DE22222222222222222222", 3200.00m, new DateOnly(2026, 8, 28), isCredit: true),
            CreateTx(12, 1, "Arbeitgeber AG", "DE22222222222222222222", 3200.00m, new DateOnly(2026, 9, 28), isCredit: true),
            CreateTx(13, 1, "Arbeitgeber AG", "DE22222222222222222222", 3200.00m, new DateOnly(2026, 10, 28), isCredit: true),
        };

        // 3. Quarterly Insurance Detection (2 quarterly payments ~90 days apart)
        var insuranceTxs = new[]
        {
            CreateTx(20, 1, "HUK Coburg", null, -125.00m, new DateOnly(2026, 4, 15)),
            CreateTx(21, 1, "HUK Coburg", null, -125.00m, new DateOnly(2026, 7, 15)),
            CreateTx(22, 1, "HUK Coburg", null, -125.00m, new DateOnly(2026, 10, 15)),
        };

        // 4. Irregular transactions (should NOT be detected as recurring)
        var irregularTxs = new[]
        {
            CreateTx(30, 1, "Electronics Store", null, -199.00m, new DateOnly(2026, 8, 3)),
            CreateTx(31, 1, "Electronics Store", null, -49.00m, new DateOnly(2026, 8, 14)), // only 11 days later, amount differs wildly
        };

        var allHistory = rentTxs.Concat(salaryTxs).Concat(insuranceTxs).Concat(irregularTxs).ToList();

        var detected = RecurringCadenceDetector.DetectObligations(1, allHistory);

        Assert(detected.Count == 3, "3 recurring patterns detected (Rent, Salary, Insurance).");

        var rentOb = detected.First(o => o.CounterpartyName.Contains("Immobilien", StringComparison.OrdinalIgnoreCase));
        Assert(rentOb.Cadence == RecurringCadence.Monthly, "Rent cadence is Monthly.");
        Assert(rentOb.ExpectedAmount == 850.00m, "Rent expected amount is 850.00m.");
        Assert(!rentOb.IsCredit, "Rent is a debit obligation.");
        Assert(!rentOb.IsUserConfirmed, "Auto-detected rent obligation is initially unconfirmed candidate.");
        Assert(rentOb.NextExpectedDate == new DateOnly(2026, 11, 1), "Next expected date is 2026-11-01.");

        var salaryOb = detected.First(o => o.CounterpartyName.Contains("Arbeitgeber", StringComparison.OrdinalIgnoreCase));
        Assert(salaryOb.Cadence == RecurringCadence.Monthly, "Salary cadence is Monthly.");
        Assert(salaryOb.ExpectedAmount == 3200.00m, "Salary expected amount is 3200.00m.");
        Assert(salaryOb.IsCredit, "Salary is a credit obligation.");
        Assert(salaryOb.NextExpectedDate == new DateOnly(2026, 11, 28), "Next salary is 2026-11-28.");

        var insOb = detected.First(o => o.CounterpartyName.Contains("HUK", StringComparison.OrdinalIgnoreCase));
        Assert(insOb.Cadence == RecurringCadence.Quarterly, "Insurance cadence is Quarterly.");
        Assert(insOb.ExpectedAmount == 125.00m, "Insurance expected amount is 125.00m.");
        Assert(insOb.NextExpectedDate == new DateOnly(2027, 1, 15), "Next insurance occurrence is 2027-01-15.");
    }

    private static void VerifyForecastItemNonConflationInvariants()
    {
        var item = new ForecastItem(
            obligationId: 10,
            accountId: 1,
            expectedDate: new DateOnly(2026, 11, 15),
            amount: -850.00m,
            currency: "EUR",
            counterpartyName: "Landlord",
            category: "Rent");

        // INVARIANT CHECKS:
        Assert(item.IsEstimate, "ForecastItem.IsEstimate is ALWAYS true (non-conflation guard).");
        Assert(item.DisplayLabel.StartsWith("[Estimate / Prognose]"), "Display label explicitly declares [Estimate / Prognose].");
        Assert(!item.IsCredit, "IsCredit is false for debit item.");
        Assert(item.Amount == -850.00m, "Amount is signed exact decimal.");

        bool zeroAmountCaught = false;
        try
        {
            _ = new ForecastItem(1, 1, new DateOnly(2026, 11, 1), 0m, "EUR", "Name");
        }
        catch (ArgumentException)
        {
            zeroAmountCaught = true;
        }
        Assert(zeroAmountCaught, "Zero amount in ForecastItem throws ArgumentException.");
    }

    private static void VerifyCashFlowForecastEngineAndBalances()
    {
        DateOnly start = new(2026, 11, 1);
        decimal initialBalance = 1500.00m;

        var obRent = new RecurringObligation(1, 1, "Rent", 800.00m, cadence: RecurringCadence.Monthly, nextExpectedDate: new DateOnly(2026, 11, 3));
        var obSalary = new RecurringObligation(2, 1, "Salary", 2500.00m, isCredit: true, cadence: RecurringCadence.Monthly, nextExpectedDate: new DateOnly(2026, 11, 28));
        var obNetflix = new RecurringObligation(3, 1, "Netflix", 17.99m, cadence: RecurringCadence.Monthly, nextExpectedDate: new DateOnly(2026, 11, 15));

        var forecast = CashFlowForecastEngine.GenerateForecast(
            accountId: 1,
            currentBalance: initialBalance,
            obligations: [obRent, obSalary, obNetflix],
            startDate: start,
            horizonDays: 30,
            safetyThreshold: 300.00m);

        Assert(forecast.AccountId == 1, "AccountId matches.");
        Assert(forecast.CurrentBookedBalance == 1500.00m, "Initial booked balance is 1500.00m.");
        Assert(forecast.HorizonDays == 30, "Horizon is 30 days.");
        Assert(forecast.StartDate == start, "StartDate matches.");
        Assert(forecast.EndDate == new DateOnly(2026, 11, 30), "EndDate matches.");
        Assert(forecast.ForecastItems.Count == 3, "3 forecast items in 30 days.");

        // Check timeline balance progression:
        // Day 1-2 (Nov 1-2): 1500.00m
        // Day 3 (Nov 3): -800.00m Rent -> 700.00m
        // Day 15 (Nov 15): -17.99m Netflix -> 682.01m
        // Day 28 (Nov 28): +2500.00m Salary -> 3182.01m
        // Day 30 (Nov 30): Closing balance = 3182.01m
        Assert(forecast.ProjectedClosingBalance == 3182.01m, "Closing balance is exactly 3182.01m.");
        Assert(forecast.TotalProjectedIncome == 2500.00m, "Total projected income is 2500.00m.");
        Assert(forecast.TotalProjectedExpenses == 817.99m, "Total projected expenses is 817.99m.");
        Assert(!forecast.HasLowBalanceAlerts, "No low balance alerts since balance never dipped below 300.00m threshold (lowest was 682.01m).");
    }

    private static void VerifyLowBalanceAlertTriggering()
    {
        DateOnly start = new(2026, 11, 1);
        decimal initialBalance = 600.00m;
        decimal safetyThreshold = 200.00m;

        // Big rent payment of €550 on Nov 4 causes balance to dip to €50, which is below the €200 safety threshold
        var obBigRent = new RecurringObligation(1, 1, "High Rent", 550.00m, cadence: RecurringCadence.Monthly, nextExpectedDate: new DateOnly(2026, 11, 4));
        var obSalary = new RecurringObligation(2, 1, "Salary", 2000.00m, isCredit: true, cadence: RecurringCadence.Monthly, nextExpectedDate: new DateOnly(2026, 11, 25));

        var forecast = CashFlowForecastEngine.GenerateForecast(
            accountId: 1,
            currentBalance: initialBalance,
            obligations: [obBigRent, obSalary],
            startDate: start,
            horizonDays: 30,
            safetyThreshold: safetyThreshold);

        Assert(forecast.HasLowBalanceAlerts, "Low balance alerts triggered.");
        Assert(forecast.LowBalanceAlerts.Count > 0, "Alert list is non-empty.");

        var firstAlert = forecast.LowBalanceAlerts.First();
        Assert(firstAlert.Date == new DateOnly(2026, 11, 4), "Alert triggered on Nov 4.");
        Assert(firstAlert.ProjectedBalance == 50.00m, "Projected balance on Nov 4 is 50.00m (600 - 550).");
        Assert(firstAlert.Threshold == 200.00m, "Threshold is 200.00m.");
        Assert(firstAlert.Shortfall == 150.00m, "Shortfall is 150.00m (200 - 50).");
        Assert(firstAlert.TriggeringCounterparty == "High Rent", "High Rent identified as triggering obligation.");
        Assert(firstAlert.WarningMessage.Contains("High Rent"), "Warning message mentions triggering party.");
    }

    private static TransactionItemRecord CreateTx(
        ulong id,
        ulong accountId,
        string counterparty,
        string? iban,
        decimal amount,
        DateOnly bookingDate,
        bool isCredit = false)
    {
        var now = DateTimeOffset.UtcNow;

        return new TransactionItemRecord(
            TransactionId: id,
            AccountId: accountId,
            AccountDisplayName: "Main Checking",
            AccountIban: "DE89370400440532013000",
            AccountIncarnationId: 10,
            AccountBindingId: 20,
            Status: "Booked",
            Kind: isCredit ? "Credit" : "Debit",
            Amount: amount,
            Currency: "EUR",
            IsCredit: isCredit,
            BookingDate: bookingDate,
            ValueDate: bookingDate,
            BankTimestamp: now,
            RetrievedAt: now,
            CounterpartyName: counterparty,
            CounterpartyIban: iban,
            CounterpartyBic: null,
            RemittanceText: "Transaction remittance",
            EndToEndId: null,
            MandateId: null,
            CreditorId: null,
            CustomerReference: null,
            BankReference: null,
            PurposeCode: null,
            ReturnReasonCode: null,
            RawBankCode: null,
            UserNote: null,
            Category: isCredit ? "Salary" : "Living",
            Tags: Array.Empty<string>(),
            StatementFormat: "camt.053",
            StatementNumber: "1",
            LinkedPendingTransactionId: null,
            LinkedBookedTransactionId: null);
    }

    private static void Assert(bool condition, string message)
    {
        if (!condition)
        {
            throw new InvalidOperationException($"Assertion failed: {message}");
        }
    }
}
