using System.Text.RegularExpressions;
using Broiler.Fond.Kernel.Domain.Transactions.Query;

namespace Broiler.Fond.Kernel.Domain.Insights;

/// <summary>
/// Result of evaluating a transaction against a categorization rule.
/// </summary>
public sealed record CategorizationMatch(
    ulong TransactionId,
    ulong RuleId,
    string RuleName,
    string TargetCategory,
    string? PreviousCategory,
    IReadOnlyList<string> AppliedTags,
    bool IsCategoryChanged)
{
    public bool IsNewAssignment => string.IsNullOrWhiteSpace(PreviousCategory) || PreviousCategory.Equals("Uncategorized", StringComparison.OrdinalIgnoreCase);
}

/// <summary>
/// Summary and itemized diagnostics of prospective rule evaluations across a transaction corpus.
/// </summary>
public sealed record CategorizationPreviewResult(
    int TotalEvaluated,
    int MatchedCount,
    int ChangedCount,
    int UnchangedCount,
    IReadOnlyList<CategorizationMatch> Matches);

/// <summary>
/// Execution options controlling rule matching and overwriting behavior.
/// </summary>
public sealed record CategorizationOptions(
    bool PreserveExistingCategories = false,
    bool PreserveManualCategories = true);

/// <summary>
/// Deterministic categorization engine evaluating rules against transactions in strict priority order.
/// </summary>
public sealed class CategorizationEngine
{
    private static readonly TimeSpan RegexTimeout = TimeSpan.FromMilliseconds(200);

    /// <summary>
    /// Matches a single transaction against an ordered rule set. Returns the first matching rule, or null.
    /// </summary>
    public static CategorizationMatch? MatchTransaction(
        IReadOnlyList<CategorizationRule> rules,
        TransactionItemRecord transaction,
        CategorizationOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(rules);
        ArgumentNullException.ThrowIfNull(transaction);

        options ??= new CategorizationOptions();

        if (options.PreserveExistingCategories && !string.IsNullOrWhiteSpace(transaction.Category) && !transaction.Category.Equals("Uncategorized", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        var sortedRules = rules
            .Where(r => r.IsActive)
            .OrderByDescending(r => r.Priority)
            .ThenBy(r => r.Id);

        string normalizedMerchant = MerchantNormalizer.Normalize(transaction.CounterpartyName, transaction.RemittanceText);

        foreach (var rule in sortedRules)
        {
            if (EvaluateRule(rule, transaction, normalizedMerchant))
            {
                bool isChanged = !string.Equals(transaction.Category, rule.TargetCategory, StringComparison.OrdinalIgnoreCase);
                return new CategorizationMatch(
                    TransactionId: transaction.TransactionId,
                    RuleId: rule.Id,
                    RuleName: rule.Name,
                    TargetCategory: rule.TargetCategory,
                    PreviousCategory: transaction.Category,
                    AppliedTags: rule.TargetTags ?? Array.Empty<string>(),
                    IsCategoryChanged: isChanged);
            }
        }

        return null;
    }

    /// <summary>
    /// Previews the prospective application of a rule set over a transaction corpus without modifying state.
    /// </summary>
    public static CategorizationPreviewResult Preview(
        IReadOnlyList<CategorizationRule> rules,
        IReadOnlyList<TransactionItemRecord> transactions,
        CategorizationOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(rules);
        ArgumentNullException.ThrowIfNull(transactions);

        options ??= new CategorizationOptions();
        List<CategorizationMatch> matches = new();
        int changedCount = 0;
        int unchangedCount = 0;

        foreach (var tx in transactions)
        {
            var match = MatchTransaction(rules, tx, options);
            if (match != null)
            {
                matches.Add(match);
                if (match.IsCategoryChanged)
                {
                    changedCount++;
                }
                else
                {
                    unchangedCount++;
                }
            }
        }

        return new CategorizationPreviewResult(
            TotalEvaluated: transactions.Count,
            MatchedCount: matches.Count,
            ChangedCount: changedCount,
            UnchangedCount: unchangedCount,
            Matches: matches.AsReadOnly());
    }

    private static bool EvaluateRule(CategorizationRule rule, TransactionItemRecord tx, string normalizedMerchant)
    {
        var criteria = rule.Criteria;

        // 1. Direction check
        if (criteria.Direction == MatchDirection.CreditOnly && !tx.IsCredit) return false;
        if (criteria.Direction == MatchDirection.DebitOnly && tx.IsCredit) return false;

        // 2. Account scope check
        if (criteria.AccountScopeId.HasValue && tx.AccountId != criteria.AccountScopeId.Value) return false;

        // 3. Amount checks
        decimal absAmount = Math.Abs(tx.Amount);
        if (criteria.MinAmount.HasValue && absAmount < criteria.MinAmount.Value) return false;
        if (criteria.MaxAmount.HasValue && absAmount > criteria.MaxAmount.Value) return false;

        // 4. Counterparty IBAN check
        if (!string.IsNullOrWhiteSpace(criteria.CounterpartyIban))
        {
            string expectedIban = criteria.CounterpartyIban.Replace(" ", string.Empty, StringComparison.Ordinal);
            string actualIban = (tx.CounterpartyIban ?? string.Empty).Replace(" ", string.Empty, StringComparison.Ordinal);
            if (!string.Equals(expectedIban, actualIban, StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }
        }

        // 5. Counterparty Pattern check
        if (!string.IsNullOrWhiteSpace(criteria.CounterpartyPattern))
        {
            bool matchRaw = MatchesPattern(tx.CounterpartyName, criteria.CounterpartyPattern, criteria.PatternKind);
            bool matchNorm = MatchesPattern(normalizedMerchant, criteria.CounterpartyPattern, criteria.PatternKind);
            if (!matchRaw && !matchNorm)
            {
                return false;
            }
        }

        // 6. Remittance Pattern check
        if (!string.IsNullOrWhiteSpace(criteria.RemittancePattern))
        {
            if (!MatchesPattern(tx.RemittanceText, criteria.RemittancePattern, criteria.PatternKind))
            {
                return false;
            }
        }

        return true;
    }

    private static bool MatchesPattern(string? text, string pattern, PatternKind kind)
    {
        if (string.IsNullOrWhiteSpace(text)) return false;

        return kind switch
        {
            PatternKind.Contains => text.Contains(pattern, StringComparison.OrdinalIgnoreCase),
            PatternKind.Exact => string.Equals(text.Trim(), pattern.Trim(), StringComparison.OrdinalIgnoreCase),
            PatternKind.StartsWith => text.StartsWith(pattern, StringComparison.OrdinalIgnoreCase),
            PatternKind.Regex => EvaluateRegex(text, pattern),
            _ => text.Contains(pattern, StringComparison.OrdinalIgnoreCase)
        };
    }

    private static bool EvaluateRegex(string text, string pattern)
    {
        try
        {
            return Regex.IsMatch(text, pattern, RegexOptions.IgnoreCase, RegexTimeout);
        }
        catch (RegexMatchTimeoutException)
        {
            return false;
        }
        catch (ArgumentException)
        {
            return false;
        }
    }
}
