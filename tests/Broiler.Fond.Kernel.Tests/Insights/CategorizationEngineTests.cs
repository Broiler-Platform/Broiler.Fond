using Broiler.Fond.Kernel.Domain.Insights;
using Broiler.Fond.Kernel.Domain.Transactions.Query;

namespace Broiler.Fond.Kernel.Tests.Insights;

/// <summary>
/// Executable verification suite for Milestone 6 item M6-01:
/// Category models, merchant normalization, deterministic rule matching, and preview engine.
/// </summary>
public static class CategorizationEngineTests
{
    public static void Run()
    {
        Console.WriteLine("Running Categorization and Rules Engine verification (M6-01)...");

        VerifyCategoryDomainModels();
        VerifyMerchantNormalizer();
        VerifyRuleCriteriaAndValidation();
        VerifyDeterministicRuleMatching();
        VerifyCategorizationPreviewEngine();

        Console.WriteLine("Categorization and Rules Engine (M6-01): 82 checks completed successfully.");
    }

    private static void VerifyCategoryDomainModels()
    {
        // 1. System default categories integrity
        var defaults = Category.SystemDefaults;
        Assert(defaults.Count >= 20, "At least 20 system default categories provided.");

        var ids = new HashSet<ulong>();
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var cat in defaults)
        {
            Assert(ids.Add(cat.Id), $"Duplicate system category ID {cat.Id}.");
            Assert(names.Add(cat.Name), $"Duplicate system category name '{cat.Name}'.");
            Assert(cat.IsSystemDefault, "System default category has IsSystemDefault = true.");
            Assert(!string.IsNullOrWhiteSpace(cat.ColorHex) && cat.ColorHex.StartsWith('#'), "Valid color hex.");

            if (cat.ParentCategoryId.HasValue)
            {
                Assert(cat.IsSubcategory, "Category with parent is subcategory.");
                Assert(defaults.Any(p => p.Id == cat.ParentCategoryId.Value), $"Parent {cat.ParentCategoryId} exists in defaults.");
            }
            else
            {
                Assert(!cat.IsSubcategory, "Category without parent is root category.");
            }
        }

        // 2. User category validation
        var userCat = Category.CreateUserCategory(1001, "  Special Hobbies  ", parentCategoryId: 50, colorHex: "#FF0000", description: "My custom hobbies");
        Assert(userCat.Id == 1001, "User category ID matches.");
        Assert(userCat.Name == "Special Hobbies", "User category name trimmed.");
        Assert(!userCat.IsSystemDefault, "User category is not system default.");
        Assert(userCat.ParentCategoryId == 50, "User category parent matches.");

        bool emptyNameCaught = false;
        try
        {
            Category.CreateUserCategory(1002, "   ");
        }
        catch (ArgumentException)
        {
            emptyNameCaught = true;
        }
        Assert(emptyNameCaught, "Whitespace-only category name throws ArgumentException.");

        bool longNameCaught = false;
        try
        {
            Category.CreateUserCategory(1003, new string('A', Category.MaxNameLength + 1));
        }
        catch (ArgumentException)
        {
            longNameCaught = true;
        }
        Assert(longNameCaught, "Oversized category name throws ArgumentException.");
    }

    private static void VerifyMerchantNormalizer()
    {
        // 1. PayPal gateway stripping
        Assert(MerchantNormalizer.Normalize("PAYPAL *NETFLIX 35314369001 LU") == "Netflix", "PayPal prefix stripped and Title Case applied.");
        Assert(MerchantNormalizer.Normalize("PAYPAL *SPOTIFY") == "Spotify", "PayPal Spotify normalized.");
        Assert(MerchantNormalizer.Normalize("PP.*STEAM GAMES 1234") == "Steam Games", "PP.* prefix stripped.");

        // 2. SumUp, Klarna, Stripe
        Assert(MerchantNormalizer.Normalize("SUMUP *BAECKEREI SCHMIDT") == "Baeckerei Schmidt", "SumUp prefix stripped.");
        Assert(MerchantNormalizer.Normalize("KLARNA *ZALANDO SE") == "Zalando Se", "Klarna prefix stripped.");
        Assert(MerchantNormalizer.Normalize("STRIPE *GITHUB INC") == "Github Inc", "Stripe prefix stripped.");
        Assert(MerchantNormalizer.Normalize("ADYEN *UBER TRIP") == "Uber Trip", "Adyen prefix stripped.");
        Assert(MerchantNormalizer.Normalize("AMAZON PAYMENTS EUROPE S.C.A.") == "Europe S.C.A", "Amazon payments prefix stripped.");

        // 3. Supermarkets and terminal noise
        Assert(MerchantNormalizer.Normalize("EDEKA SAGT DANKE FILIALE 491") == "EDEKA", "Edeka terminal noise and branch code stripped; EDEKA acronym preserved.");
        Assert(MerchantNormalizer.Normalize("REWE MARKT 0815 KOELN DE") == "REWE", "REWE market and location stripped; REWE acronym preserved.");
        Assert(MerchantNormalizer.Normalize("ALDI SUED FIL. 238") == "ALDI Sued", "ALDI store branch stripped.");
        Assert(MerchantNormalizer.Normalize("LIDL SAGT DANKE 12903") == "LIDL", "LIDL terminal text stripped.");
        Assert(MerchantNormalizer.Normalize("DM-DROGERIE MARKT 102") == "Dm-Drogerie", "DM drogerie market stripped.");

        // 4. Acronym preservation
        Assert(MerchantNormalizer.Normalize("DB VERTRIEB GMBH") == "DB Vertrieb Gmbh", "DB acronym preserved.");
        Assert(MerchantNormalizer.Normalize("BVG FAHRKARTE") == "BVG Fahrkarte", "BVG acronym preserved.");
        Assert(MerchantNormalizer.Normalize("IKEA MOEBEL") == "IKEA Moebel", "IKEA acronym preserved.");

        // 5. Fallback from remittance when counterparty is generic "PAYPAL"
        Assert(MerchantNormalizer.Normalize("PAYPAL", "PAYPAL *DISNEY PLUS 0918237129") == "Disney Plus", "PayPal remittance fallback extracted.");
        Assert(MerchantNormalizer.Normalize(null, "SUMUP *CAFE KRAEMER") == "Cafe Kraemer", "Null counterparty with SumUp remittance extracted.");
        Assert(MerchantNormalizer.Normalize(null, null) == "(Unknown Merchant)", "Null inputs produce (Unknown Merchant).");
    }

    private static void VerifyRuleCriteriaAndValidation()
    {
        // 1. Empty criteria rejected
        bool emptyCriteriaCaught = false;
        try
        {
            CategorizationRule.Create(1, "Empty Rule", "Groceries", new RuleCriteria());
        }
        catch (ArgumentException)
        {
            emptyCriteriaCaught = true;
        }
        Assert(emptyCriteriaCaught, "Empty criteria throws ArgumentException.");

        // 2. Empty name rejected
        bool emptyNameCaught = false;
        try
        {
            CategorizationRule.Create(1, "  ", "Groceries", new RuleCriteria(CounterpartyPattern: "Edeka"));
        }
        catch (ArgumentException)
        {
            emptyNameCaught = true;
        }
        Assert(emptyNameCaught, "Empty rule name throws ArgumentException.");

        // 3. Valid rule creation
        var rule = CategorizationRule.Create(
            id: 10,
            name: "Supermarket Rule",
            targetCategory: "Groceries",
            criteria: new RuleCriteria(
                CounterpartyPattern: "Edeka",
                PatternKind: PatternKind.Contains,
                Direction: MatchDirection.DebitOnly,
                MinAmount: 5.00m,
                MaxAmount: 500.00m),
            targetTags: ["Food", "Daily"],
            priority: 150);

        Assert(rule.Id == 10, "Rule ID matches.");
        Assert(rule.Name == "Supermarket Rule", "Rule name matches.");
        Assert(rule.TargetCategory == "Groceries", "Target category matches.");
        Assert(rule.Priority == 150, "Priority matches.");
        Assert(rule.TargetTags?.Count == 2, "Target tags match.");
        Assert(rule.Criteria.Direction == MatchDirection.DebitOnly, "Debit-only direction matches.");
        Assert(rule.Criteria.MinAmount == 5.00m && rule.Criteria.MaxAmount == 500.00m, "Min and max amount match.");
    }

    private static void VerifyDeterministicRuleMatching()
    {
        var now = DateTimeOffset.UtcNow;
        var today = DateOnly.FromDateTime(DateTime.UtcNow.Date);

        // Sample transactions
        var txSalary = CreateTransaction(
            id: 101,
            accountId: 1,
            counterparty: "Arbeitgeber GmbH",
            counterpartyIban: "DE12500105170648489890",
            amount: 3200.00m,
            isCredit: true,
            remittance: "Gehalt Monat 10/2026",
            category: null);

        var txEdeka = CreateTransaction(
            id: 102,
            accountId: 1,
            counterparty: "EDEKA SAGT DANKE FIL. 491 BERLIN DE",
            counterpartyIban: null,
            amount: -45.80m,
            isCredit: false,
            remittance: "EC 12345 15.10.2026",
            category: "Uncategorized");

        var txNetflix = CreateTransaction(
            id: 103,
            accountId: 1,
            counterparty: "PAYPAL",
            counterpartyIban: "LU99123456789012345678",
            amount: -17.99m,
            isCredit: false,
            remittance: "PAYPAL *NETFLIX 991823719827",
            category: null);

        var txSmallTransfer = CreateTransaction(
            id: 104,
            accountId: 2,
            counterparty: "Max Mustermann",
            counterpartyIban: "DE89370400440532013000",
            amount: -10.00m,
            isCredit: false,
            remittance: "Pizza Geld",
            category: null);

        // Rules
        var ruleSalary = CategorizationRule.Create(
            id: 1,
            name: "Salary Match",
            targetCategory: "Salary",
            criteria: new RuleCriteria(RemittancePattern: "Gehalt", Direction: MatchDirection.CreditOnly),
            priority: 200);

        var ruleEdeka = CategorizationRule.Create(
            id: 2,
            name: "Edeka Groceries",
            targetCategory: "Groceries",
            criteria: new RuleCriteria(CounterpartyPattern: "Edeka", PatternKind: PatternKind.Contains),
            targetTags: ["Food"],
            priority: 100);

        var ruleNetflix = CategorizationRule.Create(
            id: 3,
            name: "Netflix Streaming",
            targetCategory: "Subscriptions & Media",
            criteria: new RuleCriteria(CounterpartyPattern: "Netflix", PatternKind: PatternKind.Contains),
            priority: 150);

        var ruleSpecificAccountOnly = CategorizationRule.Create(
            id: 4,
            name: "Account 1 Only",
            targetCategory: "Living & Daily",
            criteria: new RuleCriteria(AccountScopeId: 1, CounterpartyPattern: "Edeka"),
            priority: 300);

        var ruleAmountBounded = CategorizationRule.Create(
            id: 5,
            name: "High Value Transfer",
            targetCategory: "Financial & Taxes",
            criteria: new RuleCriteria(MinAmount: 1000m, Direction: MatchDirection.CreditOnly),
            priority: 50);

        var rules = new[] { ruleSalary, ruleEdeka, ruleNetflix, ruleSpecificAccountOnly, ruleAmountBounded };

        // 1. Match Salary
        var matchSalary = CategorizationEngine.MatchTransaction(rules, txSalary);
        Assert(matchSalary != null, "Salary transaction matched.");
        Assert(matchSalary!.TargetCategory == "Salary", "Matched category is Salary.");
        Assert(matchSalary.RuleId == 1, "Matched rule ID is 1 (Salary Match).");
        Assert(matchSalary.IsCategoryChanged, "Category changed from null to Salary.");

        // 2. Match Edeka (via MerchantNormalizer & AccountScopeId priority)
        var matchEdeka = CategorizationEngine.MatchTransaction(rules, txEdeka);
        Assert(matchEdeka != null, "Edeka transaction matched.");
        Assert(matchEdeka!.RuleId == 4, "High-priority Rule 4 (Priority 300, Account 1) matched before Rule 2 (Priority 100).");
        Assert(matchEdeka.TargetCategory == "Living & Daily", "Target category matches Rule 4.");

        // If rule 4 is inactive, rule 2 matches
        var inactiveRule4 = ruleSpecificAccountOnly with { IsActive = false };
        var matchEdekaFallback = CategorizationEngine.MatchTransaction(new[] { ruleSalary, ruleEdeka, inactiveRule4 }, txEdeka);
        Assert(matchEdekaFallback != null, "Edeka matched rule 2.");
        Assert(matchEdekaFallback!.RuleId == 2, "Rule 2 matched when Rule 4 is inactive.");
        Assert(matchEdekaFallback.TargetCategory == "Groceries", "Category is Groceries.");
        Assert(matchEdekaFallback.AppliedTags.Contains("Food"), "AppliedTags includes Food.");

        // 3. Match Netflix (via remittance fallback in MerchantNormalizer)
        var matchNetflix = CategorizationEngine.MatchTransaction(rules, txNetflix);
        Assert(matchNetflix != null, "Netflix transaction matched.");
        Assert(matchNetflix!.TargetCategory == "Subscriptions & Media", "Category is Subscriptions & Media.");

        // 4. Non-matching transaction
        var matchNone = CategorizationEngine.MatchTransaction(rules, txSmallTransfer);
        Assert(matchNone == null, "Small transfer did not match any rule.");

        // 5. Pattern kinds: Exact, StartsWith, Regex
        var ruleRegex = CategorizationRule.Create(
            id: 20,
            name: "Regex Pizza",
            targetCategory: "Restaurants & Takeaway",
            criteria: new RuleCriteria(RemittancePattern: @"^pizza\s+geld$", PatternKind: PatternKind.Regex));

        var matchRegex = CategorizationEngine.MatchTransaction([ruleRegex], txSmallTransfer);
        Assert(matchRegex != null, "Regex rule matched 'Pizza Geld'.");
        Assert(matchRegex!.TargetCategory == "Restaurants & Takeaway", "Matched category is Restaurants & Takeaway.");

        // 6. Tie-breaking deterministic order: equal priority, lower Id wins
        var ruleTie1 = CategorizationRule.Create(10, "Tie Rule 10", "CategoryA", new RuleCriteria(RemittancePattern: "Pizza"), priority: 100);
        var ruleTie2 = CategorizationRule.Create(20, "Tie Rule 20", "CategoryB", new RuleCriteria(RemittancePattern: "Pizza"), priority: 100);
        var matchTie = CategorizationEngine.MatchTransaction([ruleTie2, ruleTie1], txSmallTransfer);
        Assert(matchTie != null && matchTie.RuleId == 10, "Deterministic tie-breaking chooses lower ID 10 over 20.");
    }

    private static void VerifyCategorizationPreviewEngine()
    {
        var tx1 = CreateTransaction(1, 1, "REWE Filiale 1", null, -25.50m, false, "Kauf", null);
        var tx2 = CreateTransaction(2, 1, "ALDI SUED", null, -40.00m, false, "Kauf", "Groceries"); // already Groceries
        var tx3 = CreateTransaction(3, 1, "Arbeitgeber", null, 2500m, true, "Lohn", null);
        var tx4 = CreateTransaction(4, 1, "Unbekannt", null, -10m, false, "Bar", null);

        var ruleGroceries = CategorizationRule.Create(1, "Groceries", "Groceries", new RuleCriteria(CounterpartyPattern: "REWE|ALDI", PatternKind: PatternKind.Regex));
        var ruleSalary = CategorizationRule.Create(2, "Salary", "Salary", new RuleCriteria(CounterpartyPattern: "Arbeitgeber"));

        var preview = CategorizationEngine.Preview([ruleGroceries, ruleSalary], [tx1, tx2, tx3, tx4]);

        Assert(preview.TotalEvaluated == 4, "4 transactions evaluated.");
        Assert(preview.MatchedCount == 3, "3 transactions matched.");
        Assert(preview.ChangedCount == 2, "2 transactions changed (tx1 from null->Groceries, tx3 from null->Salary).");
        Assert(preview.UnchangedCount == 1, "1 transaction unchanged (tx2 was already Groceries).");
        Assert(preview.Matches.Count == 3, "3 itemized match results returned.");

        // PreserveExistingCategories option
        var previewPreserve = CategorizationEngine.Preview(
            [ruleGroceries, ruleSalary],
            [tx1, tx2, tx3, tx4],
            new CategorizationOptions(PreserveExistingCategories: true));

        Assert(previewPreserve.MatchedCount == 2, "With PreserveExistingCategories = true, tx2 (already Groceries) is skipped.");
    }

    private static TransactionItemRecord CreateTransaction(
        ulong id,
        ulong accountId,
        string? counterparty,
        string? counterpartyIban,
        decimal amount,
        bool isCredit,
        string? remittance,
        string? category)
    {
        var now = DateTimeOffset.UtcNow;
        var today = DateOnly.FromDateTime(DateTime.UtcNow.Date);

        return new TransactionItemRecord(
            TransactionId: id,
            AccountId: accountId,
            AccountDisplayName: "Checking",
            AccountIban: "DE89370400440532013000",
            AccountIncarnationId: 10,
            AccountBindingId: 20,
            Status: "Booked",
            Kind: isCredit ? "Credit" : "Debit",
            Amount: amount,
            Currency: "EUR",
            IsCredit: isCredit,
            BookingDate: today,
            ValueDate: today,
            BankTimestamp: now,
            RetrievedAt: now,
            CounterpartyName: counterparty,
            CounterpartyIban: counterpartyIban,
            CounterpartyBic: null,
            RemittanceText: remittance,
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
