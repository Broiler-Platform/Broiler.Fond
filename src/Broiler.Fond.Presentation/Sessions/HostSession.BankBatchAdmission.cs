using System.Collections.Concurrent;
using Broiler.Fond.Kernel.Domain.Connections.Orchestration;
using Broiler.Fond.Kernel.Storage;
using Broiler.Fond.Presentation.Services;

namespace Broiler.Fond.Presentation.Sessions;

internal sealed class BankConnectionBatchLease(HostSession owner, long epoch, ulong connection) : IDisposable
{
    internal bool Active { get; private set; } = true;
    internal bool Matches(HostSession session, long currentEpoch, ulong id) => Active && ReferenceEquals(session, owner) && epoch == currentEpoch && connection == id;
    public void Dispose() { if (Active) { Active = false; owner.ReleaseBankOperation(epoch, connection); } }
}

public partial class HostSession
{
    private readonly ConcurrentDictionary<ulong, SemaphoreSlim> _bankInstitutionSlots = new();
    private long _backoffEpoch = -1;
    private ConnectionBackoffTracker _batchBackoff = new();
    internal SemaphoreSlim BankInstitutionGate(ulong institution) => _bankInstitutionSlots.GetOrAdd(institution, _ => new(1));
    internal ConnectionBackoffTracker GetBatchBackoff()
    {
        lock (_syncRoot)
        {
            if (_backoffEpoch != _refreshEpoch) { _backoffEpoch = _refreshEpoch; _batchBackoff = new(); }
            return _batchBackoff;
        }
    }

    internal StoreSnapshot RequireBatchConnection(ConnectionSetupContext context, bool allowContactChanges = false)
    {
        lock (_syncRoot)
        {
            var snapshot = RequireSetupSnapshot();
            var current = snapshot.Connections.Connections.SingleOrDefault(c => c.Id == context.Connection.Id);
            if (allowContactChanges && current is not null)
                current = current with { LastSuccessfulContact = context.Connection.LastSuccessfulContact, LastAttemptedContact = context.Connection.LastAttemptedContact };
            if (context.SessionEpoch != _refreshEpoch || snapshot.Profile.ProfileId != context.ProfileId || snapshot.Profile.BranchId != context.BranchId ||
                current != context.Connection || !snapshot.Institutions.Institutions.Contains(context.Institution))
                throw new InvalidOperationException("The batch bank context changed.");
            return snapshot;
        }
    }
}
