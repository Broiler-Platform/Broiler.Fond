namespace Broiler.Fond.Presentation.Models;

/// <summary>
/// Matched item within a categorization rule execution preview.
/// </summary>
public sealed record CategorizationPreviewMatchedItem(
    ulong TransactionId,
    DateOnly BookingDate,
    string? CounterpartyName,
    decimal Amount,
    string Currency,
    string? CurrentCategory);

/// <summary>
/// Aggregated preview result of evaluating categorization rules against transaction projections.
/// </summary>
public sealed record CategorizationPreview(
    int TotalEvaluated,
    int MatchCount,
    IReadOnlyList<CategorizationPreviewMatchedItem> MatchedTransactions);
