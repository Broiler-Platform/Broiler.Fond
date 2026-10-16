using Broiler.Fond.Kernel.Diagnostics;
using Broiler.Fond.Kernel.Storage;

namespace Broiler.Fond.Host.Windows.Models;

/// <summary>
/// Thread-safe host session managing profile store lifecycle, connection states, and truthful account projections.
/// Invariant: Master key and decrypted secrets are held exclusively in volatile memory and wiped upon lock.
/// </summary>
public sealed class HostSession : IDisposable
{
    private readonly object _syncRoot = new();
    private IProfileStore? _profileStore;
    private EncryptedLocalDiagnosticStore? _diagnosticStore;
    private bool _isDiagnosticsEnabled;

    public string? ProfileDirectory { get; private set; }
    public bool IsUnlocked => _profileStore is not null && _profileStore.State == ProfileStoreOpenState.Ready;

    public StoreSnapshot? CurrentSnapshot => _profileStore?.CurrentSnapshot;
    public StoreProfileDocument? Metadata => CurrentSnapshot?.Profile;

    public IReadOnlyList<AccountViewModel> Accounts { get; private set; } = Array.Empty<AccountViewModel>();
    public IReadOnlyList<ConnectionViewModel> Connections { get; private set; } = Array.Empty<ConnectionViewModel>();

    public bool IsDiagnosticsEnabled
    {
        get => _isDiagnosticsEnabled;
        set
        {
            lock (_syncRoot)
            {
                _isDiagnosticsEnabled = value;
                if (!value && _diagnosticStore is not null)
                {
                    _diagnosticStore.PurgeFromDisk();
                    _diagnosticStore = null;
                }
            }
            OnStateChanged();
        }
    }

    public EncryptedLocalDiagnosticStore? DiagnosticStore
    {
        get
        {
            lock (_syncRoot)
            {
                return _diagnosticStore;
            }
        }
    }

    public event EventHandler? StateChanged;

    public void Unlock(string directoryPath, string passphrase)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(directoryPath);
        ArgumentNullException.ThrowIfNull(passphrase);

        lock (_syncRoot)
        {
            LockInternal();

            ProfileStore store = ProfileStore.Open(directoryPath, passphrase);

            if (store.State != ProfileStoreOpenState.Ready)
            {
                store.Dispose();
                throw new InvalidOperationException($"Failed to unlock profile store: state is {store.State}");
            }

            _profileStore = store;
            ProfileDirectory = directoryPath;

            if (_isDiagnosticsEnabled && store.MasterKeySession is not null)
            {
                _diagnosticStore = new EncryptedLocalDiagnosticStore(directoryPath, enabled: true);
                _diagnosticStore.TryLoadFromDisk(store.MasterKeySession);
            }

            ReloadDataInternal();
        }

        OnStateChanged();
    }

    public void CreateNewProfile(string directoryPath, string passphrase, string? label = null, string? note = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(directoryPath);
        ArgumentNullException.ThrowIfNull(passphrase);

        lock (_syncRoot)
        {
            LockInternal();

            string effectiveLabel = string.IsNullOrWhiteSpace(label) ? "Default Profile" : label.Trim();
            string effectiveNote = note ?? string.Empty;

            ProfileStore store = ProfileStore.CreateNew(directoryPath, passphrase, effectiveLabel, effectiveNote);

            _profileStore = store;
            ProfileDirectory = directoryPath;

            if (_isDiagnosticsEnabled && store.MasterKeySession is not null)
            {
                _diagnosticStore = new EncryptedLocalDiagnosticStore(directoryPath, enabled: true);
                _diagnosticStore.SaveToDisk(store.MasterKeySession);
            }

            ReloadDataInternal();
        }

        OnStateChanged();
    }

    public void Lock()
    {
        lock (_syncRoot)
        {
            LockInternal();
        }

        OnStateChanged();
    }

    public void ReloadData()
    {
        lock (_syncRoot)
        {
            if (IsUnlocked)
            {
                ReloadDataInternal();
            }
        }

        OnStateChanged();
    }

    public IProfileStore? GetStore()
    {
        lock (_syncRoot)
        {
            return _profileStore;
        }
    }

    private void ReloadDataInternal()
    {
        if (_profileStore?.CurrentSnapshot is null)
        {
            Accounts = Array.Empty<AccountViewModel>();
            Connections = Array.Empty<ConnectionViewModel>();
            return;
        }

        StoreSnapshot snapshot = _profileStore.CurrentSnapshot;

        // Map institutions
        var instMap = snapshot.Institutions.Institutions.ToDictionary(i => i.Id);

        // Connections view models
        var connList = new List<ConnectionViewModel>();
        foreach (var conn in snapshot.Connections.Connections)
        {
            instMap.TryGetValue(conn.InstitutionId, out var inst);
            connList.Add(new ConnectionViewModel(conn, inst));
        }
        Connections = connList.AsReadOnly();

        // Accounts view models
        var accList = new List<AccountViewModel>();
        var balancesByAccount = snapshot.Balances.Balances
            .GroupBy(b => b.AccountId)
            .ToDictionary(g => g.Key, g => g.ToList());

        foreach (var acc in snapshot.Accounts.Accounts)
        {
            var binding = snapshot.Accounts.Bindings.FirstOrDefault(b => b.AccountId == acc.Id);
            StoreBalanceEntry? booked = null;
            StoreBalanceEntry? available = null;

            if (balancesByAccount.TryGetValue(acc.Id, out var accBalances))
            {
                booked = accBalances
                    .Where(b => string.Equals(b.Kind, "Booked", StringComparison.OrdinalIgnoreCase))
                    .OrderByDescending(b => b.RetrievedAt)
                    .FirstOrDefault();

                available = accBalances
                    .Where(b => string.Equals(b.Kind, "Available", StringComparison.OrdinalIgnoreCase))
                    .OrderByDescending(b => b.RetrievedAt)
                    .FirstOrDefault();
            }

            accList.Add(new AccountViewModel(acc, binding, booked, available));
        }

        Accounts = accList.AsReadOnly();
    }

    private void LockInternal()
    {
        if (_profileStore is not null)
        {
            _profileStore.Dispose();
            _profileStore = null;
        }

        _diagnosticStore = null;
        ProfileDirectory = null;
        Accounts = Array.Empty<AccountViewModel>();
        Connections = Array.Empty<ConnectionViewModel>();
    }

    private void OnStateChanged()
    {
        StateChanged?.Invoke(this, EventArgs.Empty);
    }

    public void Dispose()
    {
        Lock();
    }
}
