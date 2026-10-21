using Broiler.Fond.Kernel.Domain.Accounts;
using Broiler.Fond.Kernel.Domain.Insights;
using Broiler.Fond.Kernel.Domain.Payments;
using Broiler.Fond.Kernel.Domain.Transactions.Query;
using Broiler.Fond.Kernel.Storage;
using Broiler.Fond.Presentation.Models;
using Broiler.Fond.Presentation.ViewModels;

namespace Broiler.Fond.Presentation.Tests;

internal static class ViewModelTests
{
    public static void Run(Action<bool, string> check)
    {
        VerifyAccountViewModel(check);
        VerifyConnectionViewModel(check);
        VerifyTransactionViewModel(check);
        VerifyPaymentViewModels(check);
        VerifyInsightsViewModels(check);
        VerifyFinTsEndpoint(check);
    }

    private static void VerifyAccountViewModel(Action<bool, string> check)
    {
        var now = DateTimeOffset.UtcNow;
        var acc = new StoreAccountEntry(100, "Girokonto Primary", "EUR");
        var binding = new StoreBindingEntry(
            200, 100, 150, 10, 1, "Active", "FinTs", "3.0", "https://localhost",
            "DE89370400440532013000", null, null, null, "EUR", Array.Empty<(string, string, string, string)>());

        var bookedBalance = new StoreBalanceEntry(
            300, 1, 100, 150, 200,
            "Booked", "1520.50", "EUR", now.AddMinutes(-15),
            now.AddMinutes(-15), false, "HISAL");

        var availBalance = new StoreBalanceEntry(
            301, 1, 100, 150, 200,
            "Available", "1520.50", "EUR", now.AddMinutes(-15),
            now.AddMinutes(-15), false, "HISAL");

        var vm = new AccountViewModel(acc, binding, bookedBalance, availBalance, now, accountType: "Payment", accountHolder: "Erika Mustermann");

        check(vm.AccountId == 100, "Account ID preserved.");
        check(vm.AccountName == "Girokonto Primary", "Account display name preserved.");
        check(vm.AccountHolder == "Erika Mustermann", "Account holder preserved.");
        check(vm.IsSupportedPaymentAccount, "Payment account type recognized as supported.");
        check(vm.AccountTypeDisplay == "Payment / Current (Girokonto)", "Payment account type display formatted correctly.");
        check(vm.LocatorDisplay.Contains("DE89370400440532013000", StringComparison.Ordinal), "IBAN displayed in locator.");
        check(vm.BindingStateDisplay == "[✓ Active]", "Active binding state formatted with non-color indicator.");
        check(vm.BookedAmount == 1520.50m, "Booked amount parsed correctly.");
        check(vm.BookedCurrency == "EUR", "Booked currency matches.");
        check(vm.BookedBalanceDisplay.Contains("1.520,50", StringComparison.Ordinal) || vm.BookedBalanceDisplay.Contains("1,520.50", StringComparison.Ordinal), "Booked balance display formatted.");
        check(vm.AvailableAmount == 1520.50m, "Available amount parsed correctly.");
        check(vm.FreshnessDisplay.Contains("Fresh", StringComparison.Ordinal), "Freshness indicator shows fresh (< 1h).");
        check(!vm.IsStale, "Fresh account is not stale.");

        // Stale balance (> 24 hours)
        var staleBooked = new StoreBalanceEntry(
            302, 2, 100, 150, 200,
            "Booked", "500.00", "EUR", now.AddDays(-3),
            now.AddDays(-3), true, "HISAL");
        var staleVm = new AccountViewModel(acc, binding, staleBooked, null, now, accountType: "Payment");
        check(staleVm.IsStale, "Balance older than 24h is marked stale.");
        check(staleVm.FreshnessDisplay.Contains("Stale", StringComparison.Ordinal), "Freshness display indicates stale.");
        check(staleVm.AvailableBalanceDisplay == "[Unavailable]", "Missing available balance displayed as [Unavailable].");

        // Multi-currency isolation
        var accUsd = new StoreAccountEntry(101, "USD Savings", "USD");
        var bindingUsd = new StoreBindingEntry(
            201, 101, 150, 10, 1, "Active", "FinTs", "3.0", "https://localhost",
            null, "12345678", "US123456", null, "USD", Array.Empty<(string, string, string, string)>());
        var bookedUsd = new StoreBalanceEntry(
            303, 1, 101, 150, 201,
            "Booked", "2500.00", "USD", now, now, false, "HISAL");
        var vmUsd = new AccountViewModel(accUsd, bindingUsd, bookedUsd, null, now, accountType: "Savings");

        var totals = AccountViewModel.ComputeMultiCurrencyTotals(new[] { vm, vmUsd });
        check(totals.Count == 2, "Multi-currency totals partitioned into exactly 2 currencies.");
        check(totals["EUR"] == 1520.50m, "EUR total is isolated.");
        check(totals["USD"] == 2500.00m, "USD total is isolated.");
    }

    private static void VerifyConnectionViewModel(Action<bool, string> check)
    {
        var conn = new StoreConnectionEntry(10, 5, "USER01", "CUST01", "Connected", "SYS1", "Active", 1, true, null, null, "3.0");
        var inst = new StoreInstitutionEntry(5, "Sparkasse Test", "https://fints.sparkasse-test.de/hbci", "12345678", null, "Confirmed");

        var vm = new ConnectionViewModel(conn, inst);
        check(vm.ConnectionId == 10, "Connection ID preserved.");
        check(vm.InstitutionName == "Sparkasse Test", "Institution name preserved.");
        check(vm.BankCode == "12345678", "Bank code preserved.");
        check(vm.EndpointHost == "fints.sparkasse-test.de", "Host normalized from endpoint URI.");
        check(vm.IsEndpointConfirmed, "Endpoint confirmed flag true.");
        check(vm.StateDisplay == "[✓ Connected]", "Connected state display formatted with checkmark.");
        check(vm.CanRefresh, "CanRefresh true for confirmed connected connection.");
        check(!vm.RequiresReconfirmation, "RequiresReconfirmation false for confirmed endpoint.");

        // Unconfirmed quarantined connection
        var unconfInst = new StoreInstitutionEntry(6, "Unconfirmed Bank", "https://suspicious.bank.de", "87654321", null, "Unconfirmed");
        var unconfConn = new StoreConnectionEntry(11, 6, "USER02", null, "Quarantined", "SYS2", "Pending", 1, false, null, null, "3.0");
        var unconfVm = new ConnectionViewModel(unconfConn, unconfInst);
        check(!unconfVm.IsEndpointConfirmed, "Unconfirmed endpoint flagged false.");
        check(unconfVm.RequiresReconfirmation, "Quarantined connection requires reconfirmation.");
        check(!unconfVm.CanRefresh, "Quarantined connection cannot refresh.");
        check(unconfVm.StateDisplay.Contains("Quarantined", StringComparison.Ordinal), "Quarantined state display indicates warning.");
    }

    private static void VerifyTransactionViewModel(Action<bool, string> check)
    {
        var now = DateTimeOffset.UtcNow;
        var creditRecord = new TransactionItemRecord(
            TransactionId: 1001,
            AccountId: 100,
            AccountDisplayName: "Checking",
            AccountIban: "DE89370400440532013000",
            AccountIncarnationId: 150,
            AccountBindingId: 200,
            Status: "Booked",
            Kind: "CreditTransfer",
            Amount: 250.00m,
            Currency: "EUR",
            IsCredit: true,
            BookingDate: new DateOnly(2026, 10, 1),
            ValueDate: new DateOnly(2026, 10, 1),
            BankTimestamp: now,
            RetrievedAt: now,
            CounterpartyName: "Employer GmbH",
            CounterpartyIban: "DE12345678901234567890",
            CounterpartyBic: "TESTDEFFXXX",
            RemittanceText: "Monthly Salary October 2026",
            EndToEndId: "E2E-12345",
            MandateId: null,
            CreditorId: null,
            CustomerReference: null,
            BankReference: null,
            PurposeCode: "SALA",
            ReturnReasonCode: null,
            RawBankCode: null,
            UserNote: "Salary",
            Category: "Income",
            Tags: new[] { "salary", "work" },
            StatementFormat: "CAMT053",
            StatementNumber: "2026/1",
            LinkedPendingTransactionId: null,
            LinkedBookedTransactionId: null,
            Splits: null);

        var creditVm = new TransactionViewModel(creditRecord);
        check(creditVm.TransactionId == 1001, "Transaction ID preserved.");
        check(creditVm.IsBooked, "Booked status recognized.");
        check(creditVm.StatusDisplay == "[✓ Booked]", "Booked status display formatted.");
        check(creditVm.IsCredit, "Credit recognized.");
        check(creditVm.SemanticStyle == TransactionSemanticStyle.Credit, "Semantic style is Credit.");
        check(creditVm.AmountColorHex == "#006400", "AmountColorHex is #006400 for credit.");
        check(creditVm.AmountDisplay.Contains("[+] +", StringComparison.Ordinal), "Credit amount display contains [+] + sign.");
        check(creditVm.CounterpartyDisplay == "Employer GmbH", "Counterparty name displayed.");
        check(creditVm.CategoryDisplay == "Income", "Category display matches.");
        check(creditVm.TagsDisplay == "salary, work", "Tags formatted as comma separated list.");

        // Debit pending transaction
        var debitRecord = creditRecord with
        {
            TransactionId = 1002,
            Status = "Pending",
            Amount = 49.99m,
            IsCredit = false,
            CounterpartyName = "Supermarket",
            RemittanceText = "Grocery Shopping with very long description that exceeds 60 characters and needs truncation for summary preview"
        };
        var debitVm = new TransactionViewModel(debitRecord);
        check(debitVm.IsPending, "Pending status recognized.");
        check(debitVm.StatusDisplay == "[⏳ Pending]", "Pending status display formatted.");
        check(!debitVm.IsCredit, "Debit recognized.");
        check(debitVm.SemanticStyle == TransactionSemanticStyle.Debit, "Semantic style is Debit.");
        check(debitVm.AmountColorHex == "#8B0000", "AmountColorHex is #8B0000 for debit.");
        check(debitVm.AmountDisplay.Contains("[-] -", StringComparison.Ordinal), "Debit amount display contains [-] - sign.");
        check(debitVm.RemittanceSummary.EndsWith("...", StringComparison.Ordinal), "Long remittance truncated with ellipsis.");
    }

    private static void VerifyPaymentViewModels(Action<bool, string> check)
    {
        var now = DateTimeOffset.UtcNow;
        var draft = new StorePaymentDraftEntry(501, 1, 100, 200, 150, now, now);
        var rev = new StorePaymentDraftRevisionEntry(
            Id: 601,
            DraftId: 501,
            RevisionNumber: 1,
            PredecessorRevisionId: null,
            Timestamp: now,
            SourceAccountId: 100,
            SourceAccountBindingId: 200,
            SourceAccountIncarnationId: 150,
            BeneficiaryName: "Alice Supplier",
            BeneficiaryIban: "DE02123456789012345678",
            BeneficiaryBic: "TESTDEFFXXX",
            Amount: 75.50m,
            Currency: "EUR",
            RemittanceUnstructured: "Invoice #9876",
            RemittanceCreditorReference: null,
            ExecutionMode: "StandardCreditTransfer",
            ExecutionDate: new DateOnly(2026, 10, 20));

        var draftVm = new PaymentDraftViewModel(draft, rev, "Main Account");
        check(draftVm.DraftId == 501, "Draft ID preserved.");
        check(draftVm.BeneficiaryName == "Alice Supplier", "Beneficiary name preserved.");
        check(draftVm.Amount == 75.50m, "Amount preserved.");
        check(draftVm.BeneficiaryDisplay.Contains("Alice Supplier", StringComparison.Ordinal), "Beneficiary display formatted.");

        var vopMatch = new StorePaymentVoPResultEntry(
            Status: nameof(VerificationOfPayeeStatus.Match),
            CheckedAt: now,
            MatchedName: "Alice Supplier",
            ReasonCode: null,
            WarningMessage: null,
            UserOverride: false);

        var attempt = new StorePaymentAttemptEntry(
            Id: 601,
            DraftId: 501,
            DraftRevisionId: 601,
            SourceAccountId: 100,
            SourceAccountBindingId: 200,
            SourceAccountIncarnationId: 150,
            EndToEndId: "E2E-777",
            State: nameof(PaymentState.Accepted),
            VoPResult: vopMatch,
            BankReference: "BANK-REF-99",
            ResponseCode: "0010",
            StatusDescription: "Transaction executed successfully",
            CreatedAt: now,
            UpdatedAt: now);

        var attemptVm = new PaymentAttemptViewModel(attempt, draftVm, Array.Empty<StorePaymentStateTransitionEntry>());
        check(attemptVm.AttemptId == 601, "Attempt ID preserved.");
        check(attemptVm.IsAccepted, "Accepted state recognized.");
        check(attemptVm.StateDisplay == "[✓ Accepted by Bank]", "State display formatted.");
        check(attemptVm.VoPStatusDisplay == "[✓ Match]", "VoP match status formatted.");
        check(attemptVm.CanReconcile, "Accepted attempt can be reconciled.");
        check(!attemptVm.CanRetry, "Accepted attempt cannot be retried.");

        // Direct debit return deadline calculation
        var ddVm = new DirectDebitReturnViewModel(
            transactionId: 1005,
            creditorName: "Gym Club",
            creditorIban: "DE99888877776666555544",
            creditorId: "DE12ZZZ00000000001",
            mandateId: "MANDATE-01",
            amount: -29.90m,
            currency: "EUR",
            bookingDate: new DateOnly(2026, 9, 1),
            referenceDate: new DateOnly(2026, 10, 1));

        check(ddVm.Amount == 29.90m, "Direct debit return amount is absolute positive.");
        check(ddVm.DaysSinceBooking == 30, "30 days since booking.");
        check(ddVm.IsWithin8Weeks, "30 days is within 8-week statutory refund window (<= 56 days).");
        check(ddVm.IsWithin13Months, "30 days is within 13-month unauthorized debit window.");
    }

    private static void VerifyInsightsViewModels(Action<bool, string> check)
    {
        var budgetProgress = new CategoryBudgetProgress(
            Category: "Groceries",
            Currency: "EUR",
            Year: 2026,
            Month: 10,
            BaseLimit: 400.00m,
            RolloverAmount: 0m,
            TargetAmount: 400.00m,
            ActualSpent: 350.00m,
            RemainingBudget: 50.00m,
            PercentageConsumed: 87.5m,
            IsOverspent: false,
            OverspentAmount: 0m,
            ContributingTransactionCount: 8,
            ContributingTransactions: Array.Empty<TransactionItemRecord>());

        var budgetVm = new CategoryBudgetViewModel(budgetProgress);
        check(budgetVm.Category == "Groceries", "Budget category preserved.");
        check(budgetVm.TargetAmount == 400.00m, "Target amount preserved.");
        check(budgetVm.ActualSpent == 350.00m, "Spent amount preserved.");
        check(budgetVm.PercentageConsumed == 87.5m, "Percentage consumed preserved.");
        check(budgetVm.IsNearLimit, "87.5% consumed recognized as near limit (>= 80%).");
        check(!budgetVm.IsOverBudget, "Not over budget.");
        check(budgetVm.StatusBadge == "[⚡ Approaching Limit]", "Status badge shows approaching limit.");

        var forecastItem = new ForecastItem(
            obligationId: 1,
            accountId: 100,
            expectedDate: new DateOnly(2026, 11, 1),
            amount: -850.00m,
            currency: "EUR",
            counterpartyName: "Landlord Rent",
            category: "Housing");

        var forecastVm = new CashFlowForecastItemViewModel(forecastItem);
        check(forecastVm.CounterpartyDisplay == "Landlord Rent", "Counterparty preserved.");
        check(!forecastVm.IsCredit, "Recognized as debit.");
        check(forecastVm.AmountDisplay.Contains("[-] -", StringComparison.Ordinal), "Debit forecast item formatted.");
    }

    private static void VerifyFinTsEndpoint(Action<bool, string> check)
    {
        var ep = FinTsEndpoint.Create("https://fints.bank.de/portal/fints");
        check(ep.IdnHost == "fints.bank.de", "IdnHost correctly extracted.");
        check(!ep.IsConfirmed, "Initially unconfirmed.");

        var confirmed = ep.WithConfirmation(true);
        check(confirmed.IsConfirmed, "WithConfirmation updates confirmed state.");
        check(!ep.IsConfirmed, "Original record was immutable.");

        bool rejectedHttp = false;
        try
        {
            FinTsEndpoint.Create("http://insecure.bank.de");
        }
        catch (ArgumentException)
        {
            rejectedHttp = true;
        }
        check(rejectedHttp, "HTTP endpoint rejected; HTTPS required.");

        bool rejectedUserinfo = false;
        try
        {
            FinTsEndpoint.Create("https://user:pass@bank.de/fints");
        }
        catch (ArgumentException)
        {
            rejectedUserinfo = true;
        }
        check(rejectedUserinfo, "Userinfo in endpoint rejected.");
    }
}
