namespace Broiler.Fond.Kernel.Domain.Insights;

/// <summary>
/// Transaction direction filter for categorization rules.
/// </summary>
public enum MatchDirection
{
    All = 0,
    CreditOnly = 1,
    DebitOnly = 2
}

/// <summary>
/// Text pattern matching style for rule evaluation.
/// </summary>
public enum PatternKind
{
    Contains = 0,
    Exact = 1,
    StartsWith = 2,
    Regex = 3
}

/// <summary>
/// Criteria evaluated against a transaction to determine rule applicability.
/// </summary>
public sealed record RuleCriteria(
    string? CounterpartyPattern = null,
    string? CounterpartyIban = null,
    string? RemittancePattern = null,
    PatternKind PatternKind = PatternKind.Contains,
    MatchDirection Direction = MatchDirection.All,
    decimal? MinAmount = null,
    decimal? MaxAmount = null,
    ulong? AccountScopeId = null)
{
    public bool IsEmpty =>
        string.IsNullOrWhiteSpace(CounterpartyPattern) &&
        string.IsNullOrWhiteSpace(CounterpartyIban) &&
        string.IsNullOrWhiteSpace(RemittancePattern) &&
        !MinAmount.HasValue &&
        !MaxAmount.HasValue &&
        !AccountScopeId.HasValue &&
        Direction == MatchDirection.All;
}

/// <summary>
/// Deterministic rule that maps matching transactions to a target category and optional tags.
/// </summary>
public sealed record CategorizationRule(
    ulong Id,
    string Name,
    string TargetCategory,
    IReadOnlyList<string>? TargetTags = null,
    int Priority = 100,
    RuleCriteria Criteria = default!,
    bool IsActive = true,
    DateTimeOffset CreatedAt = default,
    DateTimeOffset? UpdatedAt = null)
{
    public const int MaxNameLength = 100;

    public static CategorizationRule Create(
        ulong id,
        string name,
        string targetCategory,
        RuleCriteria criteria,
        IReadOnlyList<string>? targetTags = null,
        int priority = 100,
        bool isActive = true,
        DateTimeOffset? createdAt = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentException.ThrowIfNullOrWhiteSpace(targetCategory);
        ArgumentNullException.ThrowIfNull(criteria);

        if (criteria.IsEmpty)
        {
            throw new ArgumentException("Categorization rule criteria cannot be empty.", nameof(criteria));
        }

        return new CategorizationRule(
            Id: id,
            Name: name.Trim(),
            TargetCategory: targetCategory.Trim(),
            TargetTags: targetTags ?? Array.Empty<string>(),
            Priority: priority,
            Criteria: criteria,
            IsActive: isActive,
            CreatedAt: createdAt ?? DateTimeOffset.UtcNow,
            UpdatedAt: null);
    }
}
