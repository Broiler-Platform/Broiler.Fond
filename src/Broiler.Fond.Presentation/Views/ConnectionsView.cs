namespace Broiler.Fond.Presentation.Views;

using System;
using System.Linq;
using Broiler.Fond.Presentation.Dialogs;
using Broiler.Fond.Presentation.Services;
using Broiler.Fond.Presentation.Sessions;
using Broiler.Fond.Presentation.ViewModels;
using Broiler.Graphics.Geometry;
using Broiler.Graphics.Text;
using Broiler.UI;
using Broiler.UI.Button.Standard;
using Broiler.UI.Dialog;
using Broiler.UI.Label;
using Broiler.UI.Label.Standard;
using Broiler.UI.ListView;
using Broiler.UI.ListView.Standard;
using Broiler.UI.Panel;
using Broiler.UI.Panel.Standard;
using Broiler.UI.Toolbar;
using Broiler.UI.Toolbar.Standard;
using Broiler.UI.Window;

/// <summary>
/// Connections view displaying bank institution connections, operational sync state, and management actions.
/// </summary>
public sealed class ConnectionsView : IDisposable
{
    private readonly HostSession _session;
    private readonly IDialogService _dialogService;
    private readonly StandardPanel _root;
    private readonly StandardToolbar _toolbar;
    private readonly StandardButton _addConnectionButton;
    private readonly StandardButton _relinkButton;
    private readonly StandardButton _removeButton;
    private readonly StandardListView _connectionsList;
    private bool _setupBusy;
    private bool _ownsRefresh;
    private bool _disposed;
    public StandardButton RefreshBalancesButton { get; } = new() { Text = "Refresh Balances", AccessibleName = "Refresh Balances for Selected Bank Connection" };
    public StandardButton CancelRefreshButton { get; } = new() { Text = "Cancel Refresh", IsEnabled = false };
    public StandardButton DiscoverAccountsButton { get; } = new() { Text = "Discover Accounts", AccessibleName = "Discover and Review Bank Accounts" };
    public StandardButton ConnectButton { get; } = new() { Text = "Connect", AccessibleName = "Connect and Set Up Online Bank Access" };
    public StandardButton CancelConnectButton { get; } = new() { Text = "Cancel Setup", AccessibleName = "Cancel Online Bank Setup", IsEnabled = false };
    public StandardLabel OnlineSetupStatus { get; } = new() { Text = "Online setup: Ready", AccessibleName = "Online Bank Setup Progress", Wrapping = UiTextWrapping.Wrap };
    public StandardButton EditConnectionButton { get; } = new() { Text = "Edit Connection", AccessibleName = "Edit Bank Login and Endpoint" };
    public StandardButton ConfirmEndpointButton { get; } = new() { Text = "Confirm Endpoint", AccessibleName = "Review and Confirm Bank Endpoint" };

    public StandardPanel Root => _root;
    public StandardToolbar Toolbar => _toolbar;
    public StandardButton AddConnectionButton => _addConnectionButton;
    public StandardButton RelinkButton => _relinkButton;
    public StandardButton RemoveButton => _removeButton;
    public StandardListView ConnectionsList => _connectionsList;

    public ConnectionsView(HostSession session, IDialogService? dialogService = null)
    {
        _session = session ?? throw new ArgumentNullException(nameof(session));
        _dialogService = dialogService ?? new BroilerDialogService();

        _root = new StandardPanel
        {
            LayoutMode = UiPanelLayoutMode.Dock,
            Spacing = 12
        };

        _toolbar = new StandardToolbar
        {
            Overflow = UiToolbarOverflow.Wrap,
            Spacing = 8,
            PreferredSize = new BSize(0, 38)
        };

        _connectionsList = new StandardListView
        {
            PreferredSize = new BSize(600, 380),
            AccessibleName = "Configured Bank Connections List"
        };

        _addConnectionButton = new StandardButton
        {
            Text = "Add Connection",
            AccessibleName = "Setup New Bank Connection"
        };
        _addConnectionButton.Clicked += async (_, _) => await ConfigureConnectionAsync();
        _toolbar.AddChild(_addConnectionButton);
        EditConnectionButton.Clicked += async (_, _) => await ConfigureConnectionAsync(edit: true);
        ConfirmEndpointButton.Clicked += async (_, _) => await ConfirmSelectedEndpointAsync();
        _toolbar.AddChild(EditConnectionButton);
        _toolbar.AddChild(ConfirmEndpointButton);
        ConnectButton.Clicked += async (_, _) => await ConnectSelectedAsync();
        CancelConnectButton.Clicked += (_, _) => _session.OnlineSetup.Cancel();
        _toolbar.AddChild(ConnectButton);
        DiscoverAccountsButton.Clicked += async (_, _) => await DiscoverSelectedAsync();
        _toolbar.AddChild(DiscoverAccountsButton);
        RefreshBalancesButton.Clicked += async (_, _) => await RefreshSelectedAsync();
        CancelRefreshButton.Clicked += (_, _) => _session.RefreshPresentation.Cancel();
        _toolbar.AddChild(RefreshBalancesButton);
        _toolbar.AddChild(CancelRefreshButton);
        _toolbar.AddChild(CancelConnectButton);
        _toolbar.AddChild(OnlineSetupStatus);
        _session.OnlineSetup.Changed += OnOnlineSetupChanged;
        _session.RefreshPresentation.Changed += OnOnlineSetupChanged;

        _relinkButton = new StandardButton
        {
            Text = "Relink Accounts",
            AccessibleName = "Relink Rediscovered Accounts"
        };
        _relinkButton.Clicked += async (_, _) =>
        {
            if (!await _session.EnsureUnlockedAsync(_dialogService))
            {
                return;
            }

            var win = GetWindow();
            if (win == null)
            {
                return;
            }

            using var dlg = new BroilerAccountRelinkingDialog(_session);
            await dlg.ShowModalAsync(win);
            RefreshConnections();
        };
        _toolbar.AddChild(_relinkButton);

        _removeButton = new StandardButton
        {
            Text = "Remove Connection",
            AccessibleName = "Remove Selected Bank Connection"
        };
        _removeButton.Clicked += async (_, _) =>
        {
            if (!await _session.EnsureUnlockedAsync(_dialogService))
            {
                return;
            }

            if (string.IsNullOrEmpty(_connectionsList.SelectedItemId) ||
                !ulong.TryParse(_connectionsList.SelectedItemId, out ulong connId))
            {
                await _dialogService.ShowAlertAsync("Remove Connection", "Please select a connection to remove.");
                return;
            }

            var connVm = _session.Connections.FirstOrDefault(c => c.ConnectionId == connId);
            var win = GetWindow();
            if (win == null)
            {
                return;
            }

            using var dlg = new BroilerConnectionRemovalDialog(connVm);
            var res = await dlg.ShowModalAsync(win);
            if (res.Kind == UiDialogResultKind.Accepted)
            {
                _session.RemoveConnection(connId, dlg.ShouldPurge);
                RefreshConnections();
            }
        };
        _toolbar.AddChild(_removeButton);

        _root.AddChild(_toolbar);
        _root.SetDock(_toolbar, UiDock.Top);

        // Dock layout follows child order: reserve the toolbar before filling the list.
        _root.AddChild(_connectionsList);
        _root.SetDock(_connectionsList, UiDock.Fill);

        _session.StateChanged += OnStateChanged;
        _session.SyncStateChanged += OnSyncStateChanged;
        OnOnlineSetupChanged(this, EventArgs.Empty);
        RefreshConnections();
    }

    private void OnStateChanged(object? sender, EventArgs e) => OnUi(RefreshConnections);
    private void OnSyncStateChanged(object? sender, SyncStateChangedEventArgs e) => OnUi(RefreshConnections);
    private void OnUi(Action action)
    {
        void Apply() { if (!_disposed) action(); }
        var dispatcher = _root.Session?.Dispatcher;
        if (dispatcher is not null && !dispatcher.CheckAccess()) dispatcher.Post(Apply); else Apply();
    }
    private void OnOnlineSetupChanged(object? sender, EventArgs e) => OnUi(() =>
    {
        bool busy = _session.OnlineSetup.IsRunning || _session.RefreshPresentation.IsRunning;
        ConnectButton.IsEnabled = !busy;
        DiscoverAccountsButton.IsEnabled = !busy;
        RefreshBalancesButton.IsEnabled = !busy;
        CancelRefreshButton.IsEnabled = _session.RefreshPresentation.IsRunning;
        CancelConnectButton.IsEnabled = _session.OnlineSetup.IsRunning;
        OnlineSetupStatus.Text = _session.RefreshPresentation.IsRunning || ReferenceEquals(sender, _session.RefreshPresentation)
            ? _session.RefreshPresentation.ProgressText : _session.OnlineSetup.ProgressText;
    });

    public Task ConnectSelectedAsync() => RefreshUiContext.RunAsync(_root.Session?.Dispatcher, async () =>
    {
        if (_disposed || _setupBusy || _session.OnlineSetup.IsRunning || _session.RefreshPresentation.IsRunning) return;
        _setupBusy = true;
        try
        {
            if (!await _session.EnsureUnlockedAsync(_dialogService) || _disposed) return;
            if (!ulong.TryParse(_connectionsList.SelectedItemId, out ulong id))
            { await _dialogService.ShowAlertAsync("Connect Bank", "Select a bank connection first."); return; }
            if (_dialogService is not IBankCredentialPrompt prompt)
            { await _dialogService.ShowAlertAsync("Connect Bank", "This host does not provide a banking PIN prompt."); return; }
            var result = await _session.OnlineSetup.ConnectAsync(id, prompt);
            if (!_disposed) await _dialogService.ShowAlertAsync("Bank Online Setup", result.Message);
        }
        catch (Exception)
        {
            if (!_disposed)
            { try { await _dialogService.ShowAlertAsync("Bank Online Setup", "Online setup could not complete. Review profile access and bank configuration before retrying."); } catch (Exception) { } }
        }
        finally { _setupBusy = false; }
    });

    public Task DiscoverSelectedAsync() => RefreshUiContext.RunAsync(_root.Session?.Dispatcher, async () =>
    {
        if (_disposed || _setupBusy || _session.OnlineSetup.IsRunning || _session.RefreshPresentation.IsRunning) return;
        _setupBusy = true;
        try
        {
            if (!await _session.EnsureUnlockedAsync(_dialogService) || _disposed) return;
            if (!ulong.TryParse(_connectionsList.SelectedItemId, out ulong id))
            { await _dialogService.ShowAlertAsync("Discover Accounts", "Select a bank connection first."); return; }
            if (_dialogService is not IBankCredentialPrompt prompt || _dialogService is not IBankAccountDiscoveryReview reviewer)
            { await _dialogService.ShowAlertAsync("Discover Accounts", "This host must provide a banking PIN prompt and explicit account review."); return; }
            var result = await _session.OnlineSetup.DiscoverAsync(id, prompt, reviewer);
            if (!_disposed) await _dialogService.ShowAlertAsync("Discover Accounts", result.Explanation);
        }
        catch (Exception)
        {
            if (!_disposed)
            { try { await _dialogService.ShowAlertAsync("Discover Accounts", "Account discovery could not complete. Review profile access and bank setup before retrying."); } catch (Exception) { } }
        }
        finally { _setupBusy = false; }
    });

    public Task RefreshSelectedAsync() => RefreshUiContext.RunAsync(_root.Session?.Dispatcher, async () =>
    {
        if (_disposed || _setupBusy || _session.OnlineSetup.IsRunning || _session.RefreshPresentation.IsRunning) return;
        _setupBusy = true;
        try
        {
            if (!await _session.EnsureUnlockedAsync(_dialogService) || _disposed) return;
            if (!ulong.TryParse(_connectionsList.SelectedItemId, out ulong id))
            { await _dialogService.ShowAlertAsync("Refresh Balances", "Select a bank connection first."); return; }
            var context = _session.GetConnectionSetupContext(id);
            var state = _session.CaptureRefreshState();
            if (state.SafeMode || state.ReadOnly || !_session.ProductConfiguration.IsConfigured ||
                context.Institution.VerificationState != "Confirmed" || !context.Connection.IsSynchronized || context.Connection.State != "Connected")
            { await _dialogService.ShowAlertAsync("Refresh Balances", "Unlock a writable profile outside Safe Mode, configure the application product ID, and use Connect to confirm and synchronize bank access first."); return; }
            bool eligible = state.Snapshot!.Accounts.Bindings.Where(b => b.ConnectionId == id).Select(b => b.AccountId).Distinct().Any(account =>
            {
                try { return _session.GetBankBalanceContext(account).Connection.Connection.Id == id; }
                catch (InvalidOperationException) { return false; }
            });
            if (!eligible)
            { await _dialogService.ShowAlertAsync("Refresh Balances", "Use Discover Accounts and explicitly review bank identities and supported balance capabilities before refreshing. Manual account entry is optional."); return; }
            if (_dialogService is not IBankCredentialPrompt prompt)
            { await _dialogService.ShowAlertAsync("Refresh Balances", "This host does not provide a banking PIN prompt."); return; }
            var selected = await _session.BankProcedures.GetBankProcedureAsync(context);
            if (selected is null)
            {
                var qualified = await _session.OnlineSetup.ConnectAsync(id, prompt);
                if (qualified.Outcome is not (BankConnectOutcome.AlreadySynchronized or BankConnectOutcome.Synchronized))
                { if (!_disposed) await _dialogService.ShowAlertAsync("Refresh Setup", qualified.Message); return; }
            }
            if (_disposed) return;
            _ = _session.RequireBatchConnection(context);
            _ownsRefresh = true;
            try { await _session.RefreshPresentation.RunAsync(_dialogService, new(ConnectionIds: [id])); }
            finally { _ownsRefresh = false; }
        }
        catch (Exception)
        {
            if (!_disposed)
            { try { await _dialogService.ShowAlertAsync("Refresh Balances", "Refresh setup could not complete or its profile changed. Review bank setup and try again. Previous saved balances remain authoritative."); } catch (Exception) { } }
        }
        finally { _setupBusy = false; }
    });

    private UiWindow? GetWindow() => (_dialogService as BroilerDialogService)?.WindowAccessor?.Invoke();

    public async Task<bool> ConfigureConnectionAsync(bool edit = false)
    {
        if (_setupBusy) return false;
        _setupBusy = true;
        try
        {
            if (!await _session.EnsureUnlockedAsync(_dialogService)) return false;
            var state = _session.CaptureRefreshState();
            if (state.SafeMode || state.ReadOnly) throw new InvalidOperationException("Open a writable profile outside Safe Mode to configure a bank connection.");
            ConnectionSetupContext? context = edit ? SelectedContext() : null;
            InstitutionSetupRequest request = context is null ? new() : new(context.Institution.DisplayName,
                context.Institution.BankCode ?? "", context.Institution.Endpoint, context.Connection.UserId, context.Connection.CustomerId);
            var result = await _dialogService.ShowInstitutionSetupAsync(request);
            if (!result.Succeeded || result.Cancelled) return false;
            if (_session.CaptureRefreshState().Epoch != state.Epoch)
                throw new InvalidOperationException("The profile changed during setup. Open setup again in the intended profile.");
            _ = BankConnectionSetup.Validate(result.DisplayName, result.EndpointUri, result.BankCode, result.UserId, result.CustomerId);
            ulong id;
            if (context is null)
                id = _session.AddInstitutionAndConnection(result.DisplayName, result.BankCode, result.EndpointUri, result.UserId, result.CustomerId).ConnectionId;
            else
            {
                _session.UpdateConnectionSetup(context, result);
                id = context.Connection.Id;
            }
            _connectionsList.SelectedItemId = id.ToString();
            var current = _session.GetConnectionSetupContext(id);
            if (current.Institution.VerificationState != "Confirmed") await ReviewEndpointAsync(current);
            return true;
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException or System.IO.IOException or UnauthorizedAccessException)
        {
            await _dialogService.ShowAlertAsync("Bank Connection Setup", ex.Message);
            return false;
        }
        finally { _setupBusy = false; }
    }

    public async Task<bool> ConfirmSelectedEndpointAsync()
    {
        if (_setupBusy) return false;
        _setupBusy = true;
        try
        {
            if (!await _session.EnsureUnlockedAsync(_dialogService)) return false;
            return await ReviewEndpointAsync(SelectedContext());
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException or System.IO.IOException or UnauthorizedAccessException)
        {
            await _dialogService.ShowAlertAsync("Confirm Bank Endpoint", ex.Message);
            return false;
        }
        finally { _setupBusy = false; }
    }

    private ConnectionSetupContext SelectedContext()
    {
        if (!ulong.TryParse(_connectionsList.SelectedItemId, out ulong id))
            throw new InvalidOperationException("Select a bank connection first.");
        return _session.GetConnectionSetupContext(id);
    }

    private async Task<bool> ReviewEndpointAsync(ConnectionSetupContext context)
    {
        _ = BankConnectionSetup.Validate(context.Institution.DisplayName, context.Institution.Endpoint, context.Institution.BankCode,
            context.Connection.UserId, context.Connection.CustomerId);
        if (!await _dialogService.ShowEndpointConfirmationAsync(context.Institution.Endpoint)) return false;
        _session.ConfirmConnectionEndpoint(context, context.Institution.Endpoint);
        return true;
    }

    public void RefreshConnections()
    {
        if (_connectionsList.IsDisposed) return;

        var items = _session.Connections.Select(c => new UiListItem(
            c.ConnectionId.ToString(),
            $"{c.InstitutionName} (BLZ {c.BankCode}) · Host: {c.EndpointHost} · State: {c.StateDisplay}",
            $"{c.InstitutionName} (BLZ {c.BankCode})",
            $"Host: {c.EndpointHost} · {c.StateDisplay}",
            false,
            c)).ToArray();

        _connectionsList.SetItems(items);
    }

    public void Dispose()
    {
        _disposed = true;
        _session.OnlineSetup.Cancel();
        if (_ownsRefresh) _session.RefreshPresentation.CancelForWindowClose();
        _session.OnlineSetup.Changed -= OnOnlineSetupChanged;
        _session.RefreshPresentation.Changed -= OnOnlineSetupChanged;
        _session.StateChanged -= OnStateChanged;
        _session.SyncStateChanged -= OnSyncStateChanged;
        _root.Dispose();
    }
}
