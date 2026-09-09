namespace Broiler.Fond.Kernel.Domain.Identifiers;

/// <summary>
/// In-memory single-writer identity ledger. One instance must own an active store
/// session. Durable entity payloads, file locks and lineage authorization are not
/// implemented here and must wrap this model in the future profile store.
/// </summary>
public sealed class LocalIdentityLedger
{
    private readonly object _gate = new();
    private IdentitySnapshot _snapshot;

    private LocalIdentityLedger(IdentitySnapshot snapshot) => _snapshot = snapshot;

    /// <summary>Invalid startup data returns diagnostics and no writable ledger.</summary>
    public static IdentityValidationResult TryOpen(IdentitySnapshot snapshot, out LocalIdentityLedger? ledger,
        CancellationToken cancellationToken = default)
    {
        IdentityValidationResult result = IdentitySnapshotValidator.Validate(snapshot, cancellationToken);
        ledger = result.IsValid ? new(snapshot) : null;
        return result;
    }

    public IdentitySnapshot Snapshot
    {
        get
        {
            lock (_gate)
            {
                return _snapshot;
            }
        }
    }

    public IdentityAllocationBatch BeginBatch()
    {
        lock (_gate)
        {
            return new(this, _snapshot);
        }
    }

    internal IdentitySnapshot Commit(IdentitySnapshot expected, IdentitySnapshot candidate,
        CancellationToken cancellationToken)
    {
        IdentityValidationResult result = IdentitySnapshotValidator.Validate(candidate, cancellationToken);
        if (!result.IsValid)
        {
            throw new InvalidIdentitySnapshotException(result);
        }

        lock (_gate)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!ReferenceEquals(_snapshot, expected))
            {
                throw new StaleIdentityBatchException();
            }

            _snapshot = candidate;
            return candidate;
        }
    }
}

/// <summary>
/// Stages provisional IDs and their identity records. Only a successful commit
/// publishes them. Failed/abandoned batches publish nothing and must not be used
/// as durable evidence or to send requests to a bank.
/// </summary>
public sealed class IdentityAllocationBatch : IDisposable
{
    private readonly object _gate = new();
    private readonly LocalIdentityLedger _owner;
    private readonly IdentitySnapshot _basis;
    private readonly List<LocalEntityRecord> _pending = [];
    private ulong _nextId;
    private bool _closed;

    internal IdentityAllocationBatch(LocalIdentityLedger owner, IdentitySnapshot basis)
    {
        _owner = owner;
        _basis = basis;
        _nextId = basis.NextId;
    }

    public LocalEntityId Allocate(LocalEntityKind kind, IEnumerable<LocalEntityReference>? references = null,
        LocalEntityId? supersedesId = null)
    {
        if (!Enum.IsDefined(kind))
        {
            throw new ArgumentOutOfRangeException(nameof(kind));
        }

        // Materialize caller-controlled enumeration before taking the batch lock
        // or choosing an ID. Reentrant enumeration cannot duplicate a chosen ID.
        LocalEntityRecord template = new(default, kind, references, supersedesId);
        lock (_gate)
        {
            EnsureOpen();

            // Reserve ulong.MaxValue as the exhausted NextId. Issuing it would
            // make the required NextId > Max(Id) invariant unrepresentable.
            if (_nextId == ulong.MaxValue)
            {
                throw new IdentityAllocationExhaustedException();
            }

            if (_basis.Records.Count + _pending.Count == IdentitySnapshot.MaximumEntities)
            {
                throw new InvalidOperationException("Identity entity limit exceeded.");
            }

            LocalEntityId id = new(_nextId);
            LocalEntityRecord record = template.WithId(id);
            _pending.Add(record);
            _nextId++;
            return id;
        }
    }

    /// <summary>
    /// Publishes a complete identity snapshot or throws without changing the
    /// ledger. Any commit attempt closes this batch, including validation failure,
    /// cancellation and stale-writer rejection.
    /// </summary>
    public IdentitySnapshot Commit(CancellationToken cancellationToken = default)
    {
        lock (_gate)
        {
            EnsureOpen();
            try
            {
                IdentitySnapshot candidate = _pending.Count == 0
                    ? _basis
                    : new(_nextId, _basis.Records.Concat(_pending));
                return _owner.Commit(_basis, candidate, cancellationToken);
            }
            finally
            {
                _closed = true;
                _pending.Clear();
            }
        }
    }

    public void Dispose()
    {
        lock (_gate)
        {
            _closed = true;
            _pending.Clear();
        }
    }

    private void EnsureOpen() => ObjectDisposedException.ThrowIf(_closed, this);
}

public sealed class InvalidIdentitySnapshotException : InvalidOperationException
{
    internal InvalidIdentitySnapshotException(IdentityValidationResult validation)
        : base("Identity snapshot validation failed; no changes were published.") => Validation = validation;

    public IdentityValidationResult Validation { get; }
}

public sealed class StaleIdentityBatchException : InvalidOperationException
{
    internal StaleIdentityBatchException() : base("The identity snapshot changed; start a new batch.") { }
}

public sealed class IdentityAllocationExhaustedException : InvalidOperationException
{
    internal IdentityAllocationExhaustedException() : base("The local identity allocator is exhausted.") { }
}
