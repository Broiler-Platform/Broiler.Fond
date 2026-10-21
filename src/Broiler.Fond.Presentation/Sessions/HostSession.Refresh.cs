using Broiler.Fond.Kernel.Storage;
using Broiler.Fond.Presentation.Services;

namespace Broiler.Fond.Presentation.Sessions;

public partial class HostSession
{
    private long _refreshEpoch;
    private bool _refreshDisposed;
    internal event Action? RefreshContextChanged;
    public IBankRefreshService BankRefresh { get; }
    public IBankBatchRefreshService BankBatchRefresh { get; }
    public IBankProcedureProvider BankProcedures { get; }
    public BankRefreshPresentation RefreshPresentation { get; }
    public BankConnectionSetupCoordinator OnlineSetup { get; }
    public BankParameterAcquisitionService BankParameters { get; }
    private readonly SessionBankProcedureProvider _selectedBankProcedures;

    public BankProductConfiguration ProductConfiguration { get; }
    public HostSession(BankProductConfiguration? productConfiguration = null, IBankProcedureProvider? bankProcedures = null)
        : this(productConfiguration, bankProcedures, null) { }

    internal HostSession(BankProductConfiguration? productConfiguration, IBankProcedureProvider? bankProcedures,
        Func<HostSession, IBankBatchRefreshService>? batchFactory,
        Func<ConnectionSetupContext, Broiler.Fond.Kernel.FinTs.Transport.IFinTsTransport>? setupTransport = null,
        Func<ConnectionSetupContext, Broiler.Fond.Kernel.FinTs.Transport.IFinTsTransport>? parameterTransport = null,
        Action<StoreSnapshot>? synchronizationBeforeSave = null,
        Func<ConnectionSetupContext, Broiler.Fond.Kernel.FinTs.Transport.IFinTsTransport>? discoveryTransport = null)
    {
        ProductConfiguration = productConfiguration ?? BankProductConfiguration.FromSettings(null, null);
        BankRefresh = new BankRefreshService(this);
        BankBatchRefresh = batchFactory?.Invoke(this) ?? new BankBatchRefreshService(this);
        _selectedBankProcedures = new(this);
        BankProcedures = bankProcedures ?? _selectedBankProcedures;
        RefreshPresentation = new(this);
        OnlineSetup = new(this, setupTransport, synchronizationBeforeSave, discoveryTransport);
        BankParameters = new(this, parameterTransport);
    }

    public void SelectBankProcedure(ConnectionSetupContext context, BankProcedureSelection selection)
    {
        if (!ReferenceEquals(BankProcedures, _selectedBankProcedures)) throw new InvalidOperationException("The host owns the configured bank procedure provider.");
        _selectedBankProcedures.Select(context, selection);
    }
    internal bool UsesSessionBankProcedures => ReferenceEquals(BankProcedures, _selectedBankProcedures);

    internal RefreshSessionState CaptureRefreshState()
    {
        lock (_syncRoot)
            return new(_refreshEpoch, _refreshDisposed, _isSafeModeActive, _profileStore?.IsReadOnly ?? false, _profileStore?.CurrentSnapshot);
    }

    internal bool IsRefreshContextCurrent(BalanceRefreshContext context)
    {
        lock (_syncRoot)
        {
            StoreSnapshot? snapshot = _profileStore?.CurrentSnapshot;
            return !_refreshDisposed && !_isSafeModeActive && _profileStore is { IsReadOnly: false } && snapshot is not null &&
                context.SessionEpoch == _refreshEpoch && context.ProfileId == snapshot.Profile.ProfileId && context.BranchId == snapshot.Profile.BranchId &&
                snapshot.Connections.Connections.Any(c => c == context.Connection) &&
                snapshot.Institutions.Institutions.Any(i => i == context.Institution) &&
                snapshot.Accounts.Bindings.Where(b => b.ConnectionId == context.Connection.Id).SequenceEqual(context.Bindings) &&
                snapshot.Accounts.Capabilities.Where(c => c.ConnectionId == context.Connection.Id).SequenceEqual(context.Capabilities);
        }
    }
}

internal sealed record RefreshSessionState(long Epoch, bool Disposed, bool SafeMode, bool ReadOnly, StoreSnapshot? Snapshot);
