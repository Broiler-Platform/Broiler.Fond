using Broiler.Fond.Kernel.Domain.Identifiers;

namespace Broiler.Fond.Kernel.Domain.Accounts;

/// <summary>
/// Defines a bank-supplied value at a point in time and preserves the binding
/// and account incarnation that produced it. A null value means unavailable
/// and must never be displayed as zero.
/// </summary>
public sealed class BalanceSnapshot
{
    public BalanceSnapshot(BalanceSnapshotId id, ObservationId observationId,
        AccountId accountId, AccountIncarnationId accountIncarnationId, AccountBindingId accountBindingId,
        BalanceKind kind, Money? value, DateTimeOffset? bankTimestamp, DateTimeOffset retrievedAt,
        bool isStale = false, string? sourceLabel = null)
    {
        ulong[] ids = [id.Value, observationId.Value, accountId.Value, accountIncarnationId.Value, accountBindingId.Value];
        if (ids.Contains(0UL) || ids.Distinct().Count() != ids.Length || !Enum.IsDefined(kind))
        {
            throw new ArgumentException("Distinct nonzero balance identities and a known balance kind are required.");
        }

        if ((kind is BalanceKind.Other or BalanceKind.Unknown) && string.IsNullOrWhiteSpace(sourceLabel))
        {
            throw new ArgumentException("Unmapped balance kinds require their source label.", nameof(sourceLabel));
        }

        if (sourceLabel?.Length > 1024)
        {
            throw new ArgumentException("Balance source label limit exceeded.", nameof(sourceLabel));
        }

        Id = id;
        ObservationId = observationId;
        AccountId = accountId;
        AccountIncarnationId = accountIncarnationId;
        AccountBindingId = accountBindingId;
        Kind = kind;
        Value = value;
        BankTimestamp = bankTimestamp;
        RetrievedAt = retrievedAt;
        IsStale = isStale;
        SourceLabel = sourceLabel;
    }

    public BalanceSnapshotId Id { get; }
    public ObservationId ObservationId { get; }
    public AccountId AccountId { get; }
    public AccountIncarnationId AccountIncarnationId { get; }
    public AccountBindingId AccountBindingId { get; }
    public BalanceKind Kind { get; }
    public Money? Value { get; }
    public DateTimeOffset? BankTimestamp { get; }
    public DateTimeOffset RetrievedAt { get; }
    public bool IsStale { get; }
    public string? SourceLabel { get; }
}
