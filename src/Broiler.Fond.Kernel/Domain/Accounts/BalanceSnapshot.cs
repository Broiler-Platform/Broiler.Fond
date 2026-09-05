using Broiler.Fond.Kernel.Domain.Identifiers;

namespace Broiler.Fond.Kernel.Domain.Accounts;

/// <summary>
/// Defines a bank-supplied value at a point in time and preserves the binding
/// and account incarnation that produced it. A null value means unavailable
/// and must never be displayed as zero.
/// </summary>
public sealed record BalanceSnapshot(
    AccountId AccountId,
    AccountIncarnationId AccountIncarnationId,
    AccountBindingId AccountBindingId,
    BalanceKind Kind,
    Money? Value,
    DateTimeOffset? BankTimestamp,
    DateTimeOffset RetrievedAt,
    bool IsStale);
