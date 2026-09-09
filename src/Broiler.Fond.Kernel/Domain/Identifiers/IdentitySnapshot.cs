using System.Collections.ObjectModel;

namespace Broiler.Fond.Kernel.Domain.Identifiers;

/// <summary>
/// One identifier in a store-wide namespace. Zero is invalid; raw values remain
/// representable so startup validation can report corrupt data without repairing it.
/// </summary>
public readonly record struct LocalEntityId(ulong Value);

/// <summary>Identity categories, including reserved revision categories for later milestones.</summary>
public enum LocalEntityKind
{
    Institution = 1,
    Connection,
    Account,
    AccountIncarnation,
    AccountBinding,
    Observation,
    BalanceSnapshot,
    Transaction,
    TransactionRevision,
    AnnotationRevision,
}

/// <summary>A local reference and the kind its target must have.</summary>
public readonly record struct LocalEntityReference(LocalEntityId TargetId, LocalEntityKind ExpectedKind);

/// <summary>
/// Immutable identity metadata for an entity. Full domain payloads and their
/// semantic validation belong to the later snapshot schema integration.
/// </summary>
public sealed class LocalEntityRecord
{
    public const int MaximumReferences = 64;

    public LocalEntityRecord(LocalEntityId id, LocalEntityKind kind,
        IEnumerable<LocalEntityReference>? references = null, LocalEntityId? supersedesId = null)
    {
        Id = id;
        Kind = kind;
        SupersedesId = supersedesId;
        List<LocalEntityReference> copy = [];
        if (references is not null)
        {
            foreach (LocalEntityReference reference in references)
            {
                if (copy.Count == MaximumReferences)
                {
                    throw new ArgumentException("Identity reference limit exceeded.", nameof(references));
                }

                copy.Add(reference);
            }
        }

        References = copy.AsReadOnly();
    }

    private LocalEntityRecord(LocalEntityId id, LocalEntityRecord source)
    {
        Id = id;
        Kind = source.Kind;
        References = source.References;
        SupersedesId = source.SupersedesId;
    }

    internal LocalEntityRecord WithId(LocalEntityId id) => new(id, this);

    public LocalEntityId Id { get; }
    public LocalEntityKind Kind { get; }
    public ReadOnlyCollection<LocalEntityReference> References { get; }
    public LocalEntityId? SupersedesId { get; }
}

/// <summary>
/// Detached, bounded identity inventory from one snapshot. Construction preserves
/// invalid values for diagnosis; only validation can make it writable.
/// </summary>
public sealed class IdentitySnapshot
{
    public const int MaximumEntities = 1_000_000;
    public const int MaximumTotalReferences = 4_000_000;

    public IdentitySnapshot(ulong nextId, IEnumerable<LocalEntityRecord> records)
    {
        ArgumentNullException.ThrowIfNull(records);
        NextId = nextId;
        List<LocalEntityRecord> copy = [];
        int referenceCount = 0;
        foreach (LocalEntityRecord record in records)
        {
            ArgumentNullException.ThrowIfNull(record);
            if (copy.Count == MaximumEntities)
            {
                throw new ArgumentException("Identity entity limit exceeded.", nameof(records));
            }

            referenceCount += record.References.Count + (record.SupersedesId.HasValue ? 1 : 0);
            if (referenceCount > MaximumTotalReferences)
            {
                throw new ArgumentException("Total identity reference limit exceeded.", nameof(records));
            }

            copy.Add(record);
        }

        Records = copy.AsReadOnly();
    }

    public ulong NextId { get; }
    public ReadOnlyCollection<LocalEntityRecord> Records { get; }

    /// <summary>Creates an empty identity inventory. This does not create a profile or lineage.</summary>
    public static IdentitySnapshot Empty() => new(1, []);
}
