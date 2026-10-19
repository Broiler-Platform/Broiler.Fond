using Broiler.Fond.Kernel.Domain;
using Broiler.Fond.Kernel.Domain.Accounts;
using Broiler.Fond.Kernel.Domain.Identifiers;
using Broiler.Fond.Kernel.Domain.Insights;
using Broiler.Fond.Kernel.Domain.Transactions.Query;

namespace Broiler.Fond.Kernel.Tests.Insights;

/// <summary>
/// Executable verification suite for Milestone 6 item M6-05:
/// Multi-currency net-worth history, assets vs liabilities breakdown, and extended read-only product presentation.
/// </summary>
public static class NetWorthHistoryTests
{
    private static int _checkCount;

    public static void Run()
    {
        Console.WriteLine("Running Net-Worth History & Extended Products verification (M6-05)...");
        _checkCount = 0;

        VerifyExtendedAccountProductValidationAndGuards();
        VerifyAccountClassificationAndAssetLiabilityBreakdown();
        VerifyCurrencyNetWorthSummaryMath();
        VerifyMultiCurrencyIsolationInSnapshot();
        VerifyHistoricalSnapshotCalculationWithSnapshotsAndTransactions();
        VerifyMonthlyNetWorthTrendCalculation();

        Console.WriteLine($"Net-Worth History & Extended Products (M6-05): {_checkCount} checks completed successfully.");
    }

    private static void VerifyExtendedAccountProductValidationAndGuards()
    {
        // 1. Valid current account with payment capability
        var currentAcc = new ExtendedAccountProduct(
            id: new AccountId(101),
            displayName: "Main Checking",
            currency: "EUR",
            productCategory: AccountProductCategory.Current,
            isBankSynced: true,
            isReadOnly: false,
            canInitiatePayments: true,
            bookedBalance: new Money(1500.50m, "EUR"),
            availableBalance: new Money(2000.00m, "EUR"),
            creditLine: new Money(500.00m, "EUR"));

        Assert(currentAcc.Id.Value == 101, "Account ID matches.");
        Assert(currentAcc.ProductCategory == AccountProductCategory.Current, "Current product category.");
        Assert(currentAcc.CanInitiatePayments, "Current account can initiate payments.");
        Assert(!currentAcc.IsReadOnly, "Current account is not read-only.");
        Assert(!currentAcc.IsOfflineManual, "Current account is bank-synced.");
        Assert(currentAcc.Classification == AccountClassificationKind.Asset, "Positive balance classified as Asset.");

        // 2. Non-conflation guard: Savings account CANNOT initiate payments
        bool threwSavings = false;
        try
        {
            _ = new ExtendedAccountProduct(
                id: new AccountId(102),
                displayName: "Tagesgeld",
                currency: "EUR",
                productCategory: AccountProductCategory.Savings,
                canInitiatePayments: true);
        }
        catch (InvalidOperationException)
        {
            threwSavings = true;
        }
        Assert(threwSavings, "Non-conflation guard: Savings cannot initiate payments.");

        // 3. Non-conflation guard: Loan account CANNOT initiate payments
        bool threwLoan = false;
        try
        {
            _ = new ExtendedAccountProduct(
                id: new AccountId(103),
                displayName: "Car Loan",
                currency: "EUR",
                productCategory: AccountProductCategory.Loan,
                canInitiatePayments: true);
        }
        catch (InvalidOperationException)
        {
            threwLoan = true;
        }
        Assert(threwLoan, "Non-conflation guard: Loan cannot initiate payments.");

        // 4. Non-conflation guard: Manual Cash CANNOT initiate payments
        bool threwCashPayment = false;
        try
        {
            _ = new ExtendedAccountProduct(
                id: new AccountId(104),
                displayName: "Wallet Cash",
                currency: "EUR",
                productCategory: AccountProductCategory.ManualCash,
                isBankSynced: false,
                canInitiatePayments: true);
        }
        catch (InvalidOperationException)
        {
            threwCashPayment = true;
        }
        Assert(threwCashPayment, "Non-conflation guard: Manual cash cannot initiate payments.");

        // 5. Offline guard: Manual Cash cannot be bank-synced
        bool threwCashSync = false;
        try
        {
            _ = new ExtendedAccountProduct(
                id: new AccountId(105),
                displayName: "Physical Safe",
                currency: "EUR",
                productCategory: AccountProductCategory.ManualCash,
                isBankSynced: true);
        }
        catch (InvalidOperationException)
        {
            threwCashSync = true;
        }
        Assert(threwCashSync, "Offline guard: Manual cash cannot be bank-synced.");

        // 6. Savings account is automatically read-only
        var savings = new ExtendedAccountProduct(
            id: new AccountId(106),
            displayName: "Festgeld 12M",
            currency: "EUR",
            productCategory: AccountProductCategory.Savings,
            isReadOnly: false); // passes false, but must be forced to true
        Assert(savings.IsReadOnly, "Savings is forced to read-only.");

        // 7. Currency mismatch guard
        bool threwCurrMismatch = false;
        try
        {
            _ = new ExtendedAccountProduct(
                id: new AccountId(107),
                displayName: "Dollar Savings",
                currency: "USD",
                productCategory: AccountProductCategory.Savings,
                bookedBalance: new Money(100m, "EUR"));
        }
        catch (ArgumentException)
        {
            threwCurrMismatch = true;
        }
        Assert(threwCurrMismatch, "Currency mismatch between account and booked balance fails.");
    }

    private static void VerifyAccountClassificationAndAssetLiabilityBreakdown()
    {
        // 1. Current account with negative balance is a Liability (Overdraft)
        var overdrawnCurrent = new ExtendedAccountProduct(
            id: new AccountId(201),
            displayName: "Overdrawn Checking",
            currency: "EUR",
            productCategory: AccountProductCategory.Current,
            bookedBalance: new Money(-250.00m, "EUR"),
            creditLine: new Money(1000.00m, "EUR"));
        Assert(overdrawnCurrent.Classification == AccountClassificationKind.Liability, "Overdrawn current is Liability.");

        // 2. Loan account is always Liability
        var loanAcc = new ExtendedAccountProduct(
            id: new AccountId(202),
            displayName: "Baukredit",
            currency: "EUR",
            productCategory: AccountProductCategory.Loan,
            bookedBalance: new Money(120000.00m, "EUR"));
        Assert(loanAcc.Classification == AccountClassificationKind.Liability, "Loan is always Liability.");

        // 3. Credit Card with drawn balance is Liability
        var drawnCreditCard = new ExtendedAccountProduct(
            id: new AccountId(203),
            displayName: "Visa Platinum",
            currency: "EUR",
            productCategory: AccountProductCategory.CreditCard,
            bookedBalance: new Money(-850.25m, "EUR"));
        Assert(drawnCreditCard.Classification == AccountClassificationKind.Liability, "Drawn credit card is Liability.");

        // 4. Credit Card with positive credit balance is Asset
        var creditCardPositive = new ExtendedAccountProduct(
            id: new AccountId(204),
            displayName: "Mastercard Prepaid",
            currency: "EUR",
            productCategory: AccountProductCategory.CreditCard,
            bookedBalance: new Money(50.00m, "EUR"));
        Assert(creditCardPositive.Classification == AccountClassificationKind.Asset, "Credit card in credit is Asset.");

        // 5. Manual cash is always Asset
        var manualCash = new ExtendedAccountProduct(
            id: new AccountId(205),
            displayName: "Cash Wallet",
            currency: "EUR",
            productCategory: AccountProductCategory.ManualCash,
            isBankSynced: false,
            bookedBalance: new Money(180.00m, "EUR"));
        Assert(manualCash.Classification == AccountClassificationKind.Asset, "Manual cash is Asset.");
        Assert(manualCash.IsOfflineManual, "Manual cash is marked offline/manual.");
    }

    private static void VerifyCurrencyNetWorthSummaryMath()
    {
        DateTimeOffset now = new(2026, 10, 19, 12, 0, 0, TimeSpan.Zero);

        List<NetWorthAccountBalance> balances =
        [
            // Current checking: +2,500.00 EUR (Asset)
            new NetWorthAccountBalance(new AccountId(301), "Checking", AccountProductCategory.Current,
                AccountClassificationKind.Asset, new Money(2500.00m, "EUR"), null, null, now, false),

            // Savings: +10,000.00 EUR (Asset)
            new NetWorthAccountBalance(new AccountId(302), "Tagesgeld", AccountProductCategory.Savings,
                AccountClassificationKind.Asset, new Money(10000.00m, "EUR"), null, null, now, false),

            // Manual cash: +350.00 EUR (Asset)
            new NetWorthAccountBalance(new AccountId(303), "Wallet", AccountProductCategory.ManualCash,
                AccountClassificationKind.Asset, new Money(350.00m, "EUR"), null, null, now, true),

            // Credit card: -450.00 EUR (Liability: 450.00 EUR)
            new NetWorthAccountBalance(new AccountId(304), "Visa", AccountProductCategory.CreditCard,
                AccountClassificationKind.Liability, new Money(-450.00m, "EUR"), new Money(2000m, "EUR"), null, now, false),

            // Installment loan: 5,000.00 EUR (Liability: 5,000.00 EUR)
            new NetWorthAccountBalance(new AccountId(305), "Auto Loan", AccountProductCategory.Loan,
                AccountClassificationKind.Liability, new Money(5000.00m, "EUR"), null, null, now, false)
        ];

        var summary = new CurrencyNetWorthSummary("EUR", balances);

        // Assets = 2,500 + 10,000 + 350 = 12,850.00 EUR
        Assert(summary.TotalAssets.Amount == 12850.00m, "Total assets exact decimal calculation.");
        Assert(summary.TotalAssets.Currency == "EUR", "Total assets currency is EUR.");

        // Liabilities = 450 + 5,000 = 5,450.00 EUR
        Assert(summary.TotalLiabilities.Amount == 5450.00m, "Total liabilities exact decimal calculation.");
        Assert(summary.TotalLiabilities.Currency == "EUR", "Total liabilities currency is EUR.");

        // Net worth = 12,850.00 - 5,450.00 = 7,400.00 EUR
        Assert(summary.NetWorth.Amount == 7400.00m, "Net worth = assets - liabilities.");
        Assert(summary.NetWorth.Currency == "EUR", "Net worth currency is EUR.");

        // Category breakdown
        Assert(summary.GetCategoryTotal(AccountProductCategory.Current).Amount == 2500.00m, "Current category total.");
        Assert(summary.GetCategoryTotal(AccountProductCategory.Savings).Amount == 10000.00m, "Savings category total.");
        Assert(summary.GetCategoryTotal(AccountProductCategory.ManualCash).Amount == 350.00m, "ManualCash category total.");
        Assert(summary.GetCategoryTotal(AccountProductCategory.CreditCard).Amount == -450.00m, "CreditCard category total.");
        Assert(summary.GetCategoryTotal(AccountProductCategory.Loan).Amount == 5000.00m, "Loan category total.");
        Assert(summary.GetCategoryTotal(AccountProductCategory.Depot).Amount == 0m, "Empty category total defaults to zero.");

        // Cross-currency contamination guard
        bool threwContamination = false;
        try
        {
            List<NetWorthAccountBalance> contaminated = [.. balances, new NetWorthAccountBalance(
                new AccountId(399), "US Brokerage", AccountProductCategory.Depot, AccountClassificationKind.Asset,
                new Money(1000m, "USD"), null, null, now, false)];
            _ = new CurrencyNetWorthSummary("EUR", contaminated);
        }
        catch (ArgumentException)
        {
            threwContamination = true;
        }
        Assert(threwContamination, "Summary rejects foreign currency balances.");
    }

    private static void VerifyMultiCurrencyIsolationInSnapshot()
    {
        DateTimeOffset pointInTime = new(2026, 10, 19, 12, 0, 0, TimeSpan.Zero);

        // Accounts in EUR and USD
        List<ExtendedAccountProduct> accounts =
        [
            new ExtendedAccountProduct(new AccountId(401), "EUR Checking", "EUR", AccountProductCategory.Current, bookedBalance: new Money(3000m, "EUR")),
            new ExtendedAccountProduct(new AccountId(402), "EUR Savings", "EUR", AccountProductCategory.Savings, bookedBalance: new Money(7000m, "EUR")),
            new ExtendedAccountProduct(new AccountId(403), "USD Checking", "USD", AccountProductCategory.Current, bookedBalance: new Money(1200m, "USD")),
            new ExtendedAccountProduct(new AccountId(404), "USD Credit Card", "USD", AccountProductCategory.CreditCard, bookedBalance: new Money(-200m, "USD"))
        ];

        NetWorthSnapshot snapshot = NetWorthHistoryCalculator.CalculateSnapshot(pointInTime, accounts);

        Assert(snapshot.SnapshotDate == pointInTime, "Snapshot date matches.");
        Assert(snapshot.Currencies.Count == 2, "Contains exactly 2 currencies.");
        Assert(snapshot.Currencies[0] == "EUR" && snapshot.Currencies[1] == "USD", "Ordered currencies.");

        // EUR summary
        CurrencyNetWorthSummary? eurSummary = snapshot.GetSummary("EUR");
        Assert(eurSummary is not null, "EUR summary exists.");
        Assert(eurSummary!.TotalAssets.Amount == 10000m, "EUR Total assets = 10,000.");
        Assert(eurSummary.TotalLiabilities.Amount == 0m, "EUR Total liabilities = 0.");
        Assert(eurSummary.NetWorth.Amount == 10000m, "EUR Net worth = 10,000.");
        Assert(eurSummary.Accounts.Count == 2, "EUR accounts count = 2.");

        // USD summary
        CurrencyNetWorthSummary? usdSummary = snapshot.GetSummary("USD");
        Assert(usdSummary is not null, "USD summary exists.");
        Assert(usdSummary!.TotalAssets.Amount == 1200m, "USD Total assets = 1,200.");
        Assert(usdSummary.TotalLiabilities.Amount == 200m, "USD Total liabilities = 200.");
        Assert(usdSummary.NetWorth.Amount == 1000m, "USD Net worth = 1,000.");
        Assert(usdSummary.Accounts.Count == 2, "USD accounts count = 2.");

        // Zero silent conversion: snapshot does not merge EUR and USD into a synthetic number
        Assert(snapshot.GetSummary("GBP") is null, "Unrepresented currency returns null.");
    }

    private static void VerifyHistoricalSnapshotCalculationWithSnapshotsAndTransactions()
    {
        var accId = new AccountId(501);
        var account = new ExtendedAccountProduct(
            accId, "Primary Giro", "EUR", AccountProductCategory.Current, isBankSynced: true);

        // Snapshot 1: on Jan 1 with 1,000.00 EUR
        DateTimeOffset jan1 = new(2026, 1, 1, 10, 0, 0, TimeSpan.Zero);
        var snap1 = new BalanceSnapshot(
            id: new BalanceSnapshotId(1),
            observationId: new ObservationId(10),
            accountId: accId,
            accountIncarnationId: new AccountIncarnationId(100),
            accountBindingId: new AccountBindingId(1000),
            kind: BalanceKind.Booked,
            value: new Money(1000.00m, "EUR"),
            bankTimestamp: jan1,
            retrievedAt: jan1);

        // Intermediate transactions:
        // Jan 5: Salary credit +500.00 EUR
        // Jan 10: Grocery debit -120.00 EUR
        // Jan 20: Rent debit -600.00 EUR
        List<TransactionItemRecord> txs =
        [
            CreateTestTransaction(1, accId.Value, 500.00m, "EUR", isCredit: true, new DateOnly(2026, 1, 5)),
            CreateTestTransaction(2, accId.Value, 120.00m, "EUR", isCredit: false, new DateOnly(2026, 1, 10)),
            CreateTestTransaction(3, accId.Value, 600.00m, "EUR", isCredit: false, new DateOnly(2026, 1, 20))
        ];

        // Evaluation at Jan 1: exactly snapshot 1
        NetWorthSnapshot atJan1 = NetWorthHistoryCalculator.CalculateSnapshot(jan1, [account], [snap1], txs);
        var eurJan1 = atJan1.GetSummary("EUR");
        Assert(eurJan1 is not null, "Jan 1 summary exists.");
        Assert(eurJan1!.NetWorth.Amount == 1000.00m, "Jan 1 net worth matches snap1 (1,000.00 EUR).");
        Assert(eurJan1.Accounts[0].SourceSnapshotId?.Value == 1, "Provenance links to snap1.");

        // Evaluation at Jan 15 (includes Salary +500 and Grocery -120, excludes Rent -600)
        // Expected: 1,000 + 500 - 120 = 1,380.00 EUR
        DateTimeOffset jan15 = new(2026, 1, 15, 23, 59, 59, TimeSpan.Zero);
        NetWorthSnapshot atJan15 = NetWorthHistoryCalculator.CalculateSnapshot(jan15, [account], [snap1], txs);
        var eurJan15 = atJan15.GetSummary("EUR");
        Assert(eurJan15!.NetWorth.Amount == 1380.00m, "Jan 15 net worth adjusted by transactions (1,380.00 EUR).");

        // Evaluation at Jan 25 (includes Rent -600)
        // Expected: 1,380 - 600 = 780.00 EUR
        DateTimeOffset jan25 = new(2026, 1, 25, 23, 59, 59, TimeSpan.Zero);
        NetWorthSnapshot atJan25 = NetWorthHistoryCalculator.CalculateSnapshot(jan25, [account], [snap1], txs);
        var eurJan25 = atJan25.GetSummary("EUR");
        Assert(eurJan25!.NetWorth.Amount == 780.00m, "Jan 25 net worth includes rent debit (780.00 EUR).");

        // Snapshot 2 on Feb 1: 800.00 EUR
        DateTimeOffset feb1 = new(2026, 2, 1, 8, 0, 0, TimeSpan.Zero);
        var snap2 = new BalanceSnapshot(
            id: new BalanceSnapshotId(2),
            observationId: new ObservationId(11),
            accountId: accId,
            accountIncarnationId: new AccountIncarnationId(100),
            accountBindingId: new AccountBindingId(1000),
            kind: BalanceKind.Booked,
            value: new Money(800.00m, "EUR"),
            bankTimestamp: feb1,
            retrievedAt: feb1);

        NetWorthSnapshot atFeb1 = NetWorthHistoryCalculator.CalculateSnapshot(feb1, [account], [snap1, snap2], txs);
        var eurFeb1 = atFeb1.GetSummary("EUR");
        Assert(eurFeb1!.NetWorth.Amount == 800.00m, "Feb 1 net worth binds to snap2 (800.00 EUR).");
        Assert(eurFeb1.Accounts[0].SourceSnapshotId?.Value == 2, "Provenance links to snap2.");
    }

    private static void VerifyMonthlyNetWorthTrendCalculation()
    {
        var giroId = new AccountId(601);
        var giro = new ExtendedAccountProduct(giroId, "Girokonto", "EUR", AccountProductCategory.Current, isBankSynced: true);

        var cashId = new AccountId(602);
        var cash = new ExtendedAccountProduct(cashId, "Bargeld", "EUR", AccountProductCategory.ManualCash, isBankSynced: false);

        // Snapshots for Giro across 3 months
        DateTimeOffset jan31 = new(2026, 1, 31, 23, 59, 59, TimeSpan.Zero);
        DateTimeOffset feb28 = new(2026, 2, 28, 23, 59, 59, TimeSpan.Zero);
        DateTimeOffset mar31 = new(2026, 3, 31, 23, 59, 59, TimeSpan.Zero);

        var incId = new AccountIncarnationId(701);
        var bindId = new AccountBindingId(801);

        List<BalanceSnapshot> snapshots =
        [
            new BalanceSnapshot(new BalanceSnapshotId(101), new ObservationId(201), giroId, incId, bindId,
                BalanceKind.Booked, new Money(2000m, "EUR"), jan31, jan31),
            new BalanceSnapshot(new BalanceSnapshotId(102), new ObservationId(202), giroId, incId, bindId,
                BalanceKind.Booked, new Money(2400m, "EUR"), feb28, feb28),
            new BalanceSnapshot(new BalanceSnapshotId(103), new ObservationId(203), giroId, incId, bindId,
                BalanceKind.Booked, new Money(2100m, "EUR"), mar31, mar31)
        ];

        // Cash transactions: +100 in Jan, +50 in Feb
        List<TransactionItemRecord> cashTxs =
        [
            CreateTestTransaction(11, cashId.Value, 100m, "EUR", isCredit: true, new DateOnly(2026, 1, 15)),
            CreateTestTransaction(12, cashId.Value, 50m, "EUR", isCredit: true, new DateOnly(2026, 2, 10))
        ];

        var history = NetWorthHistoryCalculator.CalculateMonthlyHistory(
            startMonth: new DateOnly(2026, 1, 1),
            endMonth: new DateOnly(2026, 3, 1),
            accounts: [giro, cash],
            snapshots: snapshots,
            transactions: cashTxs);

        Assert(history.Count == 3, "History spans exactly 3 monthly snapshots.");

        // Jan 31: Giro 2,000 + Cash 100 = 2,100 EUR
        var janSummary = history[0].GetSummary("EUR");
        Assert(janSummary is not null, "Jan summary present.");
        Assert(janSummary!.NetWorth.Amount == 2100m, "Jan net worth = 2,100 EUR.");

        // Feb 28: Giro 2,400 + Cash (100 + 50) = 2,550 EUR
        var febSummary = history[1].GetSummary("EUR");
        Assert(febSummary is not null, "Feb summary present.");
        Assert(febSummary!.NetWorth.Amount == 2550m, "Feb net worth = 2,550 EUR.");

        // Mar 31: Giro 2,100 + Cash 150 = 2,250 EUR
        var marSummary = history[2].GetSummary("EUR");
        Assert(marSummary is not null, "Mar summary present.");
        Assert(marSummary!.NetWorth.Amount == 2250m, "Mar net worth = 2,250 EUR.");
    }

    private static TransactionItemRecord CreateTestTransaction(
        ulong id, ulong accountId, decimal amount, string currency, bool isCredit, DateOnly bookingDate)
    {
        return new TransactionItemRecord(
            TransactionId: id,
            AccountId: accountId,
            AccountDisplayName: "Test Account",
            AccountIban: null,
            AccountIncarnationId: 1,
            AccountBindingId: 1,
            Status: "Booked",
            Kind: "Standard",
            Amount: amount,
            Currency: currency,
            IsCredit: isCredit,
            BookingDate: bookingDate,
            ValueDate: bookingDate,
            BankTimestamp: new DateTimeOffset(bookingDate.ToDateTime(new TimeOnly(12, 0, 0)), TimeSpan.Zero),
            RetrievedAt: DateTimeOffset.UtcNow,
            CounterpartyName: "Test Party",
            CounterpartyIban: null,
            CounterpartyBic: null,
            RemittanceText: "Test Remittance",
            EndToEndId: null,
            MandateId: null,
            CreditorId: null,
            CustomerReference: null,
            BankReference: null,
            PurposeCode: null,
            ReturnReasonCode: null,
            RawBankCode: null,
            UserNote: null,
            Category: null,
            Tags: [],
            StatementFormat: null,
            StatementNumber: null,
            LinkedPendingTransactionId: null,
            LinkedBookedTransactionId: null);
    }

    private static void Assert(bool condition, string message)
    {
        _checkCount++;
        if (!condition)
        {
            throw new InvalidOperationException($"Assertion failed: {message}");
        }
    }
}
