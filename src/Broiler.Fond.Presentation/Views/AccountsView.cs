namespace Broiler.Fond.Presentation.Views;

using System;
using System.Linq;
using Broiler.Fond.Presentation.Services;
using Broiler.Fond.Presentation.Sessions;
using Broiler.Fond.Presentation.ViewModels;
using Broiler.Graphics.Geometry;
using Broiler.Graphics.Text;
using Broiler.UI;
using Broiler.UI.Button.Standard;
using Broiler.UI.Label;
using Broiler.UI.Label.Standard;
using Broiler.UI.ListView;
using Broiler.UI.ListView.Standard;
using Broiler.UI.Panel;
using Broiler.UI.Panel.Standard;
using Broiler.UI.Toolbar;
using Broiler.UI.Toolbar.Standard;

/// <summary>
/// Accounts view displaying multi-currency partitioned balance cards, freshness badges, and account actions.
/// </summary>
public sealed class AccountsView : IDisposable
{
    private readonly HostSession _session;
    private readonly IDialogService _dialogService;
    private readonly StandardPanel _root;
    private readonly StandardToolbar _toolbar;
    private readonly StandardListView _accountsList;
    private readonly StandardLabel _freshnessLabel;
    private readonly StandardButton _syncButton;
    private readonly StandardButton _cancelRefreshButton;
    private readonly StandardLabel _refreshStatusLabel;
    private readonly StandardButton _detailsButton;
    private readonly StandardButton _addAccountButton;
    private bool _isAddingAccount;

    public StandardPanel Root => _root;
    public StandardToolbar Toolbar => _toolbar;
    public StandardListView AccountsList => _accountsList;
    public StandardLabel FreshnessLabel => _freshnessLabel;
    public StandardButton SyncButton => _syncButton;
    public StandardButton CancelRefreshButton => _cancelRefreshButton;
    public StandardLabel RefreshStatusLabel => _refreshStatusLabel;
    public StandardButton DetailsButton => _detailsButton;
    public StandardButton AddAccountButton => _addAccountButton;

    public AccountsView(HostSession session, IDialogService? dialogService = null)
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

        _accountsList = new StandardListView
        {
            PreferredSize = new BSize(600, 360),
            AccessibleName = "Accounts and Balances List"
        };

        _addAccountButton = new StandardButton { Text = "Add Account", AccessibleName = "Add Account Manually" };
        _addAccountButton.Clicked += async (_, _) => await AddAccountAsync();
        _toolbar.AddChild(_addAccountButton);

        _syncButton = new StandardButton
        {
            Text = "Refresh Balances",
            AccessibleName = "Refresh All Account Balances"
        };
        _syncButton.Clicked += async (_, _) =>
        {
            await BankRefreshFeedback.ShowAsync(_session, _dialogService, _root.Session?.Dispatcher);
        };
        _toolbar.AddChild(_syncButton);
        _cancelRefreshButton = new StandardButton { Text = "Cancel Refresh", AccessibleName = "Cancel Bank Balance Refresh", IsEnabled = false };
        _cancelRefreshButton.Clicked += (_, _) => _session.RefreshPresentation.Cancel();
        _toolbar.AddChild(_cancelRefreshButton);
        _refreshStatusLabel = new StandardLabel { Text = "Bank refresh: Ready", Wrapping = UiTextWrapping.Wrap, AccessibleName = "Bank Refresh Progress" };
        _toolbar.AddChild(_refreshStatusLabel);

        _detailsButton = new StandardButton
        {
            Text = "Account Details",
            AccessibleName = "View Selected Account Details"
        };
        _detailsButton.Clicked += async (_, _) =>
        {
            if (!await _session.EnsureUnlockedAsync(_dialogService))
            {
                return;
            }

            if (string.IsNullOrEmpty(_accountsList.SelectedItemId))
            {
                await _dialogService.ShowAlertAsync("Account Details", "Please select an account from the list to view its details.");
                return;
            }

            var acc = _session.Accounts.FirstOrDefault(a => a.AccountId.ToString() == _accountsList.SelectedItemId);
            if (acc != null)
            {
                await _dialogService.ShowAlertAsync(
                    "Account Details",
                    $"Account: {acc.AccountName}\nHolder: {acc.AccountHolder}\nLocator: {acc.LocatorDisplay}\nBooked: {acc.BookedBalanceDisplay}\nAvailable: {acc.AvailableBalanceDisplay}\nFreshness: {acc.FreshnessDisplay}");
            }
        };
        _toolbar.AddChild(_detailsButton);

        _freshnessLabel = new StandardLabel
        {
            Text = "Balances: No bank data retrieved",
            AccessibleName = "Balance Freshness Status Badge"
        };
        _toolbar.AddChild(_freshnessLabel);

        _root.AddChild(_toolbar);
        _root.SetDock(_toolbar, UiDock.Top);

        // Dock layout follows child order: reserve the toolbar before filling the list.
        _root.AddChild(_accountsList);
        _root.SetDock(_accountsList, UiDock.Fill);

        _session.AccountListChanged += OnAccountListChanged;
        _session.RefreshPresentation.Changed += OnRefreshChanged;
        UpdateRefreshStatus();
        RefreshList();
    }

    private void OnAccountListChanged(object? sender, EventArgs e) => OnUi(RefreshList);
    private void OnRefreshChanged(object? sender, EventArgs e) => OnUi(UpdateRefreshStatus);
    private void OnUi(Action action)
    {
        var dispatcher = _root.Session?.Dispatcher;
        if (dispatcher is not null && !dispatcher.CheckAccess()) dispatcher.Post(() => { if (!_root.IsDisposed) action(); });
        else if (!_root.IsDisposed) action();
    }
    private void UpdateRefreshStatus()
    {
        if (_root.IsDisposed) return;
        _syncButton.IsEnabled = !_session.RefreshPresentation.IsRunning;
        _cancelRefreshButton.IsEnabled = _session.RefreshPresentation.IsRunning;
        _refreshStatusLabel.Text = _session.RefreshPresentation.ProgressText;
    }

    public async Task<bool> AddAccountAsync()
    {
        if (_isAddingAccount)
            return false;
        _isAddingAccount = true;
        _addAccountButton.IsEnabled = false;
        try
        {
            if (!await _session.EnsureUnlockedAsync(_dialogService))
                return false;
            if (_session.IsSafeModeActive)
            {
                await _dialogService.ShowAlertAsync("Add Account", "Accounts cannot be added while Safe Mode is active.");
                return false;
            }
            if (_session.Connections.Count == 0)
            {
                await _dialogService.ShowAlertAsync("Add Account", "Create a bank connection using Connections → Add Connection, then add its account here.");
                return false;
            }
            var request = new AccountSetupRequest(_session.Connections.Select(c =>
                new AccountConnectionOption(c.ConnectionId, $"{c.InstitutionName} (Connection {c.ConnectionId})")).ToArray());
            AccountSetupResult result = await _dialogService.ShowAccountSetupAsync(request);
            if (!result.Succeeded || result.Cancelled)
                return false;
            AccountViewModel account = _session.AddManualAccount(result.ConnectionId, result.DisplayName, result.Iban, result.Currency);
            _accountsList.SelectedItemId = account.AccountId.ToString();
            return true;
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException or System.IO.IOException or UnauthorizedAccessException)
        {
            await _dialogService.ShowAlertAsync("Add Account", ex.Message);
            return false;
        }
        finally
        {
            _isAddingAccount = false;
            if (!_addAccountButton.IsDisposed)
                _addAccountButton.IsEnabled = true;
        }
    }

    public void RefreshList()
    {
        if (_accountsList.IsDisposed) return;

        var recorded = _session.Accounts.Where(a => a.BookedAmount.HasValue || a.AvailableAmount.HasValue).ToArray();
        var oldest = recorded.Where(a => a.RetrievedAtUtc.HasValue).Select(a => a.RetrievedAtUtc!.Value).OrderBy(t => t).FirstOrDefault();
        _freshnessLabel.Text = recorded.Length == 0
            ? "Balances: No bank data retrieved"
            : $"Cached balances: {recorded.Length}/{_session.Accounts.Count} accounts" +
              (oldest == default ? " · Retrieval time unknown" : $" · Oldest retrieval: {oldest.UtcDateTime:yyyy-MM-dd HH:mm} UTC");

        var items = _session.Accounts.Select(acc => new UiListItem(
            acc.AccountId.ToString(),
            $"{acc.AccountHolder} - {acc.Iban} - {acc.BalanceDisplay}",
            $"{acc.DisplayName} ({acc.Iban})",
            acc.BalanceDisplay,
            false,
            acc)).ToArray();

        _accountsList.SetItems(items);
    }

    public void Dispose()
    {
        _session.AccountListChanged -= OnAccountListChanged;
        _session.RefreshPresentation.Changed -= OnRefreshChanged;
        _root.Dispose();
    }
}
