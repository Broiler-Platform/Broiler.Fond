using Broiler.Fond.Kernel.Domain.Insights;
using Broiler.Fond.Kernel.Domain.Transactions;
using Broiler.Fond.Kernel.Domain.Transactions.Query;

namespace Broiler.Fond.Kernel.Tests.Insights;

/// <summary>
/// Executable verification suite for Milestone 6 item M6-03:
/// Monthly budgets, spending targets, split-aware category rollups, and multi-month rollover calculations.
/// </summary>
public static class BudgetCalculationTests
{
    public static void Run()
    {
        Console.WriteLine("Running Budget Calculations & Category Rollups verification (M6-03)...");

        VerifyBudgetTargetDomainModel();
        VerifyMonthlySpendingRollupsWithoutSplits();
        VerifySplitAwareCategoryRollups();
        VerifyMultiMonthRolloverCalculations();
        VerifyCurrencyIsolationInBudgets();
        VerifyCalculateAllBudgetsWithUnbudgetedSpending();

        Console.WriteLine("Budget Calculations & Category Rollups (M6-03): 78 checks completed successfully.");
    }

    private static void VerifyBudgetTargetDomainModel()
    {
        // 1. Valid budget creation
        var target = new BudgetTarget(
            id: 1,
            category: "Groceries",
            monthlyLimit: 400.00m,
            currency: "EUR",
            effectiveFrom: new DateOnly(2026, 1, 15), // normalizes to 2026-01-01
            effectiveTo: new DateOnly(2026, 12, 10), // normalizes to 2026-12-31
            allowRollover: true,
            notes: "Family groceries limit");

        Assert(target.Id == 1, "Id matches.");
        Assert(target.Category == "Groceries", "Category matches.");
        Assert(target.MonthlyLimit == 400.00m, "MonthlyLimit matches.");
        Assert(target.Currency == "EUR", "Currency matches.");
        Assert(target.EffectiveFrom == new DateOnly(2026, 1, 1), "EffectiveFrom normalized to first of month.");
        Assert(target.EffectiveTo == new DateOnly(2026, 12, 31), "EffectiveTo normalized to end of month.");
        Assert(target.AllowRollover, "AllowRollover is true.");
        Assert(target.IsActive, "IsActive default is true.");
        Assert(target.Notes == "Family groceries limit", "Notes preserved.");

        // 2. IsEffectiveForMonth
        Assert(!target.IsEffectiveForMonth(2025, 12), "December 2025 is not effective.");
        Assert(target.IsEffectiveForMonth(2026, 1), "January 2026 is effective.");
        Assert(target.IsEffectiveForMonth(2026, 6), "June 2026 is effective.");
        Assert(target.IsEffectiveForMonth(2026, 12), "December 2026 is effective.");
        Assert(!target.IsEffectiveForMonth(2027, 1), "January 2027 is not effective.");

        // Inactive target
        var inactive = target with { IsActive = false };
        Assert(!inactive.IsEffectiveForMonth(2026, 6), "Inactive target is never effective.");

        // 3. Validation failures
        bool zeroIdCaught = false;
        try { _ = new BudgetTarget(0, "Groceries", 100m); }
        catch (ArgumentOutOfRangeException) { zeroIdCaught = true; }
        Assert(zeroIdCaught, "Zero ID throws ArgumentOutOfRangeException.");

        bool emptyCatCaught = false;
        try { _ = new BudgetTarget(1, "   ", 100m); }
        catch (ArgumentException) { emptyCatCaught = true; }
        Assert(emptyCatCaught, "Empty category throws ArgumentException.");

        bool negativeLimitCaught = false;
        try { _ = new BudgetTarget(1, "Groceries", -50m); }
        catch (ArgumentOutOfRangeException) { negativeLimitCaught = true; }
        Assert(negativeLimitCaught, "Negative limit throws ArgumentOutOfRangeException.");

        bool invalidCurCaught = false;
        try { _ = new BudgetTarget(1, "Groceries", 100m, currency: "EU"); }
        catch (ArgumentException) { invalidCurCaught = true; }
        Assert(invalidCurCaught, "Invalid currency length throws ArgumentException.");

        bool invalidDatesCaught = false;
        try { _ = new BudgetTarget(1, "Groceries", 100m, effectiveFrom: new DateOnly(2026, 5, 1), effectiveTo: new DateOnly(2026, 3, 1)); }
        catch (ArgumentException) { invalidDatesCaught = true; }
        Assert(invalidDatesCaught, "EffectiveTo preceding EffectiveFrom throws ArgumentException.");
    }

    private static void VerifyMonthlySpendingRollupsWithoutSplits()
    {
        var targetGroceries = new BudgetTarget(1, "Groceries", 300.00m);

        var txInMonth1 = CreateTestTx(1, -45.50m, "Groceries", new DateOnly(2026, 10, 5));
        var txInMonth2 = CreateTestTx(2, -84.20m, "Groceries", new DateOnly(2026, 10, 18));
        var txOtherMonth = CreateTestTx(3, -99.00m, "Groceries", new DateOnly(2026, 9, 28)); // prior month
        var txOtherCat = CreateTestTx(4, -50.00m, "Dining", new DateOnly(2026, 10, 12));
        var txCredit = CreateTestTx(5, 500.00m, "Groceries", new DateOnly(2026, 10, 20), isCredit: true); // income ignored

        var allTx = new[] { txInMonth1, txInMonth2, txOtherMonth, txOtherCat, txCredit };

        var progress = BudgetRollupEngine.CalculateMonthlyProgress(targetGroceries, allTx, 2026, 10);

        Assert(progress.Category == "Groceries", "Category matches.");
        Assert(progress.Year == 2026 && progress.Month == 10, "Year and Month match.");
        Assert(progress.BaseLimit == 300.00m, "BaseLimit is 300.00m.");
        Assert(progress.TargetAmount == 300.00m, "TargetAmount is 300.00m.");
        Assert(progress.ActualSpent == 129.70m, "ActualSpent is 129.70m (45.50 + 84.20).");
        Assert(progress.RemainingBudget == 170.30m, "RemainingBudget is 170.30m.");
        Assert(progress.PercentageConsumed == 43.23m, "PercentageConsumed is 43.23% (129.70 / 300 * 100).");
        Assert(!progress.IsOverspent, "IsOverspent is false.");
        Assert(progress.OverspentAmount == 0m, "OverspentAmount is 0.");
        Assert(progress.ContributingTransactionCount == 2, "2 contributing transactions.");

        // Overspend scenario
        var txBig = CreateTestTx(6, -200.00m, "Groceries", new DateOnly(2026, 10, 25));
        var progressOver = BudgetRollupEngine.CalculateMonthlyProgress(targetGroceries, [.. allTx, txBig], 2026, 10);

        Assert(progressOver.ActualSpent == 329.70m, "ActualSpent is 329.70m.");
        Assert(progressOver.RemainingBudget == -29.70m, "RemainingBudget is -29.70m.");
        Assert(progressOver.PercentageConsumed == 109.90m, "PercentageConsumed is 109.90%.");
        Assert(progressOver.IsOverspent, "IsOverspent is true.");
        Assert(progressOver.OverspentAmount == 29.70m, "OverspentAmount is 29.70m.");
    }

    private static void VerifySplitAwareCategoryRollups()
    {
        var targetGroceries = new BudgetTarget(1, "Groceries", 200.00m);
        var targetClothing = new BudgetTarget(2, "Clothing", 80.00m);
        var targetHousehold = new BudgetTarget(3, "Household", 50.00m);

        // Hypermarket transaction: €130.00 split into:
        // - €70.00 Groceries
        // - €40.00 Clothing
        // - €20.00 Household
        var splitGroceries = new TransactionSplitItem(0, -70.00m, "Groceries");
        var splitClothing = new TransactionSplitItem(1, -40.00m, "Clothing");
        var splitHousehold = new TransactionSplitItem(2, -20.00m, "Household");

        var txSplit = CreateTestTx(10, -130.00m, "[Split]", new DateOnly(2026, 10, 15), splits: [splitGroceries, splitClothing, splitHousehold]);

        // Regular non-split groceries purchase of €45.00
        var txRegular = CreateTestTx(11, -45.00m, "Groceries", new DateOnly(2026, 10, 22));

        var transactions = new[] { txSplit, txRegular };

        // 1. Groceries rollup: 70.00 from split + 45.00 regular = 115.00m
        var grocProgress = BudgetRollupEngine.CalculateMonthlyProgress(targetGroceries, transactions, 2026, 10);
        Assert(grocProgress.ActualSpent == 115.00m, "Groceries spending includes split and regular purchase.");
        Assert(grocProgress.RemainingBudget == 85.00m, "Remaining budget is 85.00m.");
        Assert(grocProgress.ContributingTransactionCount == 2, "2 contributing transactions for Groceries.");

        // 2. Clothing rollup: 40.00 from split
        var clothProgress = BudgetRollupEngine.CalculateMonthlyProgress(targetClothing, transactions, 2026, 10);
        Assert(clothProgress.ActualSpent == 40.00m, "Clothing spending is 40.00m from split item.");
        Assert(clothProgress.RemainingBudget == 40.00m, "Remaining budget is 40.00m (50% consumed).");
        Assert(clothProgress.ContributingTransactionCount == 1, "1 contributing transaction for Clothing.");

        // 3. Household rollup: 20.00 from split
        var houseProgress = BudgetRollupEngine.CalculateMonthlyProgress(targetHousehold, transactions, 2026, 10);
        Assert(houseProgress.ActualSpent == 20.00m, "Household spending is 20.00m from split item.");
        Assert(houseProgress.RemainingBudget == 30.00m, "Remaining budget is 30.00m.");
    }

    private static void VerifyMultiMonthRolloverCalculations()
    {
        var target = new BudgetTarget(
            id: 1,
            category: "Leisure",
            monthlyLimit: 100.00m,
            currency: "EUR",
            effectiveFrom: new DateOnly(2026, 1, 1),
            allowRollover: true);

        // Month 1 (Jan 2026): Spent €40.00 -> Unspent €60.00 rolls over
        var txJan = CreateTestTx(1, -40.00m, "Leisure", new DateOnly(2026, 1, 10));

        // Month 2 (Feb 2026): Base €100.00 + Rollover €60.00 = Target €160.00.
        // Spent €120.00 -> Unspent €40.00 rolls over to March
        var txFeb = CreateTestTx(2, -120.00m, "Leisure", new DateOnly(2026, 2, 15));

        // Month 3 (Mar 2026): Base €100.00 + Rollover €40.00 = Target €140.00.
        var txMar = CreateTestTx(3, -50.00m, "Leisure", new DateOnly(2026, 3, 20));

        var allTx = new[] { txJan, txFeb, txMar };

        // 1. Calculate rollover into February 2026
        decimal rolloverFeb = BudgetRollupEngine.CalculateCumulativeRollover(target, allTx, 2026, 1, 2026, 2);
        Assert(rolloverFeb == 60.00m, "February rollover is €60.00 from unspent January budget.");

        var progressFeb = BudgetRollupEngine.CalculateMonthlyProgress(target, allTx, 2026, 2, rolloverFeb);
        Assert(progressFeb.BaseLimit == 100.00m, "February base limit is 100.00m.");
        Assert(progressFeb.RolloverAmount == 60.00m, "February rollover amount is 60.00m.");
        Assert(progressFeb.TargetAmount == 160.00m, "February target amount is 160.00m.");
        Assert(progressFeb.ActualSpent == 120.00m, "February spent is 120.00m.");
        Assert(progressFeb.RemainingBudget == 40.00m, "February remaining budget is 40.00m.");
        Assert(!progressFeb.IsOverspent, "Not overspent because rollover covered the extra spending.");

        // 2. Calculate rollover into March 2026
        decimal rolloverMar = BudgetRollupEngine.CalculateCumulativeRollover(target, allTx, 2026, 1, 2026, 3);
        Assert(rolloverMar == 40.00m, "March rollover is €40.00 carried from February.");

        var progressMar = BudgetRollupEngine.CalculateMonthlyProgress(target, allTx, 2026, 3, rolloverMar);
        Assert(progressMar.TargetAmount == 140.00m, "March target amount is 140.00m (100 base + 40 rollover).");
        Assert(progressMar.RemainingBudget == 90.00m, "March remaining budget is 90.00m (140 - 50).");

        // 3. Without rollover (AllowRollover = false)
        var targetNoRollover = target with { AllowRollover = false };
        decimal rolloverDisabled = BudgetRollupEngine.CalculateCumulativeRollover(targetNoRollover, allTx, 2026, 1, 2026, 2);
        Assert(rolloverDisabled == 0m, "Rollover is 0 when AllowRollover is false.");

        var progressFebNoRollover = BudgetRollupEngine.CalculateMonthlyProgress(targetNoRollover, allTx, 2026, 2, 0m);
        Assert(progressFebNoRollover.TargetAmount == 100.00m, "Target is 100.00m without rollover.");
        Assert(progressFebNoRollover.IsOverspent, "IsOverspent is true when rollover is disabled (120 > 100).");
        Assert(progressFebNoRollover.OverspentAmount == 20.00m, "Overspent by 20.00m.");
    }

    private static void VerifyCurrencyIsolationInBudgets()
    {
        var targetEur = new BudgetTarget(1, "Dining", 100.00m, currency: "EUR");

        var txEur = CreateTestTx(1, -40.00m, "Dining", new DateOnly(2026, 10, 10), currency: "EUR");
        var txUsd = CreateTestTx(2, -50.00m, "Dining", new DateOnly(2026, 10, 12), currency: "USD"); // foreign currency ignored

        var progress = BudgetRollupEngine.CalculateMonthlyProgress(targetEur, [txEur, txUsd], 2026, 10);

        Assert(progress.ActualSpent == 40.00m, "USD transaction excluded from EUR budget calculation.");
        Assert(progress.RemainingBudget == 60.00m, "Remaining budget is 60.00m in EUR.");
        Assert(progress.ContributingTransactionCount == 1, "Only EUR transaction contributed.");
    }

    private static void VerifyCalculateAllBudgetsWithUnbudgetedSpending()
    {
        var targetGroceries = new BudgetTarget(1, "Groceries", 300.00m);
        var targetRent = new BudgetTarget(2, "Rent", 800.00m);

        var txGroc = CreateTestTx(1, -120.00m, "Groceries", new DateOnly(2026, 10, 5));
        var txRent = CreateTestTx(2, -800.00m, "Rent", new DateOnly(2026, 10, 1));
        var txElectronicsUnbudgeted = CreateTestTx(3, -150.00m, "Electronics", new DateOnly(2026, 10, 15));

        var allBudgets = BudgetRollupEngine.CalculateAllBudgets(
            targets: [targetGroceries, targetRent],
            allTransactions: [txGroc, txRent, txElectronicsUnbudgeted],
            year: 2026,
            month: 10);

        Assert(allBudgets.Count == 3, "3 categories reported (2 budgeted + 1 unbudgeted with spending).");

        var groc = allBudgets.First(b => b.Category == "Groceries");
        Assert(groc.TargetAmount == 300.00m && groc.ActualSpent == 120.00m, "Groceries progress calculated.");

        var rent = allBudgets.First(b => b.Category == "Rent");
        Assert(rent.TargetAmount == 800.00m && rent.ActualSpent == 800.00m && !rent.IsOverspent, "Rent fully paid exactly on budget.");

        var unbudgeted = allBudgets.First(b => b.Category == "Electronics");
        Assert(unbudgeted.HasNoTarget, "Electronics category has HasNoTarget = true.");
        Assert(unbudgeted.TargetAmount == 0m, "Electronics target amount is 0.");
        Assert(unbudgeted.ActualSpent == 150.00m, "Electronics actual spent is 150.00m.");
        Assert(unbudgeted.IsOverspent, "Unbudgeted category with spending is marked IsOverspent = true.");
        Assert(unbudgeted.OverspentAmount == 150.00m, "Unbudgeted overspent amount equals spending.");
    }

    private static TransactionItemRecord CreateTestTx(
        ulong id,
        decimal amount,
        string category,
        DateOnly bookingDate,
        bool isCredit = false,
        string currency = "EUR",
        IReadOnlyList<TransactionSplitItem>? splits = null)
    {
        var now = DateTimeOffset.UtcNow;

        return new TransactionItemRecord(
            TransactionId: id,
            AccountId: 1,
            AccountDisplayName: "Checking",
            AccountIban: "DE89370400440532013000",
            AccountIncarnationId: 10,
            AccountBindingId: 20,
            Status: "Booked",
            Kind: isCredit ? "Credit" : "Debit",
            Amount: amount,
            Currency: currency,
            IsCredit: isCredit,
            BookingDate: bookingDate,
            ValueDate: bookingDate,
            BankTimestamp: now,
            RetrievedAt: now,
            CounterpartyName: "Store",
            CounterpartyIban: null,
            CounterpartyBic: null,
            RemittanceText: "Payment",
            EndToEndId: null,
            MandateId: null,
            CreditorId: null,
            CustomerReference: null,
            BankReference: null,
            PurposeCode: null,
            ReturnReasonCode: null,
            RawBankCode: null,
            UserNote: null,
            Category: category,
            Tags: Array.Empty<string>(),
            StatementFormat: "camt.053",
            StatementNumber: "1",
            LinkedPendingTransactionId: null,
            LinkedBookedTransactionId: null,
            Splits: splits);
    }

    private static void Assert(bool condition, string message)
    {
        if (!condition)
        {
            throw new InvalidOperationException($"Assertion failed: {message}");
        }
    }
}
