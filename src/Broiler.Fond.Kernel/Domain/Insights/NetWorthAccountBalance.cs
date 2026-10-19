using Broiler.Fond.Kernel.Domain.Identifiers;

namespace Broiler.Fond.Kernel.Domain.Insights;

/// <summary>
/// Constituent account balance record contributing to a net-worth snapshot at a point in time.
/// </summary>
public sealed record NetWorthAccountBalance(
    AccountId AccountId,
    string AccountDisplayName,
    AccountProductCategory ProductCategory,
    AccountClassificationKind Classification,
    Money Balance,
    Money? CreditLine,
    BalanceSnapshotId? SourceSnapshotId,
    DateTimeOffset EffectiveDate,
    bool IsOfflineOrManual);
