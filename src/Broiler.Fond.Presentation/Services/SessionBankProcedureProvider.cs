using System.Collections.Concurrent;
using Broiler.Fond.Kernel.FinTs;
using Broiler.Fond.Presentation.Sessions;

namespace Broiler.Fond.Presentation.Services;

/// <summary>Volatile, explicitly selected procedure provenance. Never persists credentials or invents bank parameters.</summary>
internal sealed class SessionBankProcedureProvider : IBankProcedureProvider, IDisposable
{
    private readonly HostSession _session;
    private readonly ConcurrentDictionary<ulong, (ConnectionSetupContext Context, BankProcedureSelection Selection, long Started)> _entries = new();
    internal SessionBankProcedureProvider(HostSession session) { _session = session; session.RefreshContextChanged += Invalidate; }
    internal void Select(ConnectionSetupContext context, BankProcedureSelection selection)
    {
        _ = _session.RequireBatchConnection(context, allowContactChanges: true);
        _ = new FinTsProductionCandidateFactory(BankConnectionSetup.ToDomain(context.Connection, context.Institution),
            _session.ProductConfiguration.Registration ?? throw new InvalidOperationException("Configure the assigned product ID first."), selection.Procedures, selection.Selection);
        _entries[context.Connection.Id] = (context, selection, System.Diagnostics.Stopwatch.GetTimestamp());
        Invalidate();
    }
    public Task<BankProcedureSelection?> GetBankProcedureAsync(ConnectionSetupContext context, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        Invalidate();
        if (_entries.TryGetValue(context.Connection.Id, out var entry) && entry.Context.ProfileId == context.ProfileId &&
            entry.Context.BranchId == context.BranchId && entry.Context.SessionEpoch == context.SessionEpoch &&
            context.Institution == entry.Context.Institution && context.Connection.UserId == entry.Context.Connection.UserId &&
            context.Connection.CustomerId == entry.Context.Connection.CustomerId)
            return Task.FromResult<BankProcedureSelection?>(entry.Selection);
        return Task.FromResult<BankProcedureSelection?>(null);
    }
    private void Invalidate()
    {
        foreach (var entry in _entries)
        {
            try
            {
                var original = entry.Value.Context;
                var current = _session.GetConnectionSetupContext(entry.Key);
                if (current.SessionEpoch != original.SessionEpoch || current.ProfileId != original.ProfileId || current.BranchId != original.BranchId ||
                    current.Institution != original.Institution || current.Connection.UserId != original.Connection.UserId ||
                    current.Connection.CustomerId != original.Connection.CustomerId || current.Connection.State == "Disabled" ||
                    System.Diagnostics.Stopwatch.GetElapsedTime(entry.Value.Started) >= TimeSpan.FromMinutes(10))
                    _entries.TryRemove(entry.Key, out _);
            }
            catch (InvalidOperationException) { _entries.TryRemove(entry.Key, out _); }
        }
    }
    public void Dispose() { _session.RefreshContextChanged -= Invalidate; _entries.Clear(); }
}
