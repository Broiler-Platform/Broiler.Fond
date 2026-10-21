namespace Broiler.Fond.Presentation.Views;

using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
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
using Broiler.UI.Panel;
using Broiler.UI.Panel.Standard;
using Broiler.UI.Standard;
using Broiler.UI.TabView.Standard;
using Broiler.UI.Toolbar;
using Broiler.UI.Toolbar.Standard;
using Broiler.UI.Window.Standard;

/// <summary>
/// Unified main banking workspace window shell hosting navigation tabs, top toolbar, and persistent status strip.
/// Invariant: Pure BCL implementation with zero third-party packages or telemetry.
/// </summary>
public sealed class BroilerMainWindow : IDisposable
{
    private readonly HostSession _session;
    private readonly IDialogService _dialogService;
    private readonly StandardWindow _window;
    private readonly StandardPanel _root;
    private readonly StandardToolbar _topBar;
    private readonly StandardButton _syncAllButton;
    private readonly StandardButton _cancelRefreshButton;
    private readonly StandardLabel _refreshStatusLabel;
    private readonly StandardButton _newTransferButton;
    private readonly StandardButton _lockProfileButton;
    private readonly StandardButton _supportExportButton;
    private readonly StandardLabel _safeModeBadge;
    private readonly StandardTabView _tabView;
    private readonly StandardPanel _statusBar;
    private readonly StandardLabel _statusLineageLabel;
    private readonly StandardLabel _statusCurrencyTotalsLabel;

    public StandardWindow Window => _window;
    public StandardPanel Root => _root;
    public StandardToolbar TopBar => _topBar;
    public StandardButton SyncAllButton => _syncAllButton;
    public StandardButton CancelRefreshButton => _cancelRefreshButton;
    public StandardLabel RefreshStatusLabel => _refreshStatusLabel;
    public StandardButton NewTransferButton => _newTransferButton;
    public StandardButton LockProfileButton => _lockProfileButton;
    public StandardButton SupportExportButton => _supportExportButton;
    public StandardLabel SafeModeBadge => _safeModeBadge;
    public StandardTabView TabView => _tabView;
    public StandardLabel StatusLineageLabel => _statusLineageLabel;
    public StandardLabel StatusCurrencyTotalsLabel => _statusCurrencyTotalsLabel;

    public AccountsView Accounts { get; }
    public TransactionsView Transactions { get; }
    public InsightsView Insights { get; }
    public ConnectionsView Connections { get; }
    public PaymentsView Payments { get; }
    public SecurityAuditView Security { get; }

    public BroilerMainWindow(HostSession session, IDialogService? dialogService = null)
    {
        _session = session ?? throw new ArgumentNullException(nameof(session));
        _dialogService = dialogService ?? new BroilerDialogService(() => _window);

        _window = new StandardWindow
        {
            Title = "Broiler Fond — Finance on Demand"
        };
        _window.ApplyTheme(StandardControlPaint.Theme);

        if (_dialogService is BroilerDialogService concrete)
        {
            concrete.WindowAccessor = () => _window;
        }

        _root = new StandardPanel
        {
            LayoutMode = UiPanelLayoutMode.Dock,
            Spacing = 4
        };

        // 1. Top Action Bar
        _topBar = new StandardToolbar
        {
            Overflow = UiToolbarOverflow.Wrap,
            Spacing = 8,
            PreferredSize = new BSize(0, 42)
        };

        _syncAllButton = new StandardButton
        {
            Text = "Sync All",
            AccessibleName = "Synchronize All Banking Connections"
        };
        _syncAllButton.Clicked += async (_, _) =>
        {
            await BankRefreshFeedback.ShowAsync(_session, _dialogService, _window.Session?.Dispatcher);
        };
        _topBar.AddChild(_syncAllButton);
        _cancelRefreshButton = new StandardButton { Text = "Cancel Refresh", AccessibleName = "Cancel All Bank Refresh Work", IsEnabled = false };
        _cancelRefreshButton.Clicked += (_, _) => _session.RefreshPresentation.Cancel();
        _topBar.AddChild(_cancelRefreshButton);
        _refreshStatusLabel = new StandardLabel { Text = "Bank refresh: Ready", Wrapping = UiTextWrapping.Wrap, AccessibleName = "All Banks Refresh Progress" };
        _topBar.AddChild(_refreshStatusLabel);

        _newTransferButton = new StandardButton
        {
            Text = "New Transfer",
            AccessibleName = "Initiate New Transfer"
        };
        _newTransferButton.Clicked += async (_, _) =>
        {
            if (!await ShowUnlockDialogAsync())
            {
                return;
            }

            if (_session.IsSafeModeActive)
            {
                await _dialogService.ShowAlertAsync("Safe Mode Active", "Payment transfers are disabled while in Read-Only Safe Mode.");
                return;
            }

            using var dlg = new BroilerPaymentInitiationDialog(_session);
            var res = await dlg.ShowModalAsync(_window);
            if (res.Kind == UiDialogResultKind.Accepted)
            {
                var sourceAcc = _session.Accounts.FirstOrDefault();
                if (sourceAcc != null)
                {
                    _session.CreatePaymentDraft(
                        sourceAcc.AccountId,
                        dlg.Beneficiary,
                        dlg.Iban,
                        null,
                        dlg.Amount,
                        dlg.RemittanceText,
                        null,
                        dlg.IsInstant ? "Instant" : "Standard",
                        DateOnly.FromDateTime(DateTime.UtcNow));
                    Payments?.RefreshDrafts();
                }
                else
                {
                    await _dialogService.ShowAlertAsync("No Source Account", "Please set up a bank connection first.");
                }
            }
        };
        _topBar.AddChild(_newTransferButton);

        _safeModeBadge = new StandardLabel
        {
            Text = _session.IsSafeModeActive ? "Safe Mode: Active (Read-Only)" : "Operational: Secure",
            AccessibleName = "Safe Mode Indicator"
        };
        _topBar.AddChild(_safeModeBadge);

        _lockProfileButton = new StandardButton
        {
            Text = _session.IsUnlocked ? "Lock Profile" : "Unlock Profile",
            AccessibleName = "Lock or Unlock Profile"
        };
        _lockProfileButton.Clicked += async (_, _) =>
        {
            if (_session.IsUnlocked)
            {
                _session.Lock();
            }
            else
            {
                await ShowUnlockDialogAsync();
            }
        };
        _topBar.AddChild(_lockProfileButton);

        _supportExportButton = new StandardButton
        {
            Text = "Support Export",
            AccessibleName = "Export Redacted Diagnostics"
        };
        _supportExportButton.Clicked += async (_, _) =>
        {
            using var dlg = new BroilerSupportExportDialog(_session.RefreshPresentation.GetSupportPreviewJson());
            await dlg.ShowModalAsync(_window);
        };
        _topBar.AddChild(_supportExportButton);

        _root.AddChild(_topBar);
        _root.SetDock(_topBar, UiDock.Top);

        // 2. Persistent Status Strip
        _statusBar = new StandardPanel
        {
            LayoutMode = UiPanelLayoutMode.Dock,
            Spacing = 12
        };

        _statusLineageLabel = new StandardLabel
        {
            Text = "Profile: Qualified · Lineage: Verified",
            AccessibleName = "Profile Lineage Status"
        };
        _statusBar.AddChild(_statusLineageLabel);
        _statusBar.SetDock(_statusLineageLabel, UiDock.Left);

        _statusCurrencyTotalsLabel = new StandardLabel
        {
            Text = "Total: EUR 0.00 | USD 0.00",
            AccessibleName = "Multi-Currency Balance Summary"
        };
        _statusBar.AddChild(_statusCurrencyTotalsLabel);
        _statusBar.SetDock(_statusCurrencyTotalsLabel, UiDock.Right);

        _root.AddChild(_statusBar);
        _root.SetDock(_statusBar, UiDock.Bottom);

        // 3. Primary Tab View Strip
        _tabView = new StandardTabView();

        Accounts = new AccountsView(_session, _dialogService);
        Transactions = new TransactionsView(_session, _dialogService);
        Insights = new InsightsView(_session, _dialogService);
        Connections = new ConnectionsView(_session, _dialogService);
        Payments = new PaymentsView(_session, _dialogService);
        Security = new SecurityAuditView(_session, _dialogService);

        _tabView.AddTab("accounts", "Accounts", Accounts.Root);
        _tabView.AddTab("transactions", "Transactions", Transactions.Root);
        _tabView.AddTab("insights", "Insights & Budgets", Insights.Root);
        _tabView.AddTab("connections", "Connections", Connections.Root);
        _tabView.AddTab("payments", "Payments", Payments.Root);
        _tabView.AddTab("security", "Security & Diagnostics", Security.Root);

        _root.AddChild(_tabView);
        _root.SetDock(_tabView, UiDock.Fill);

        _window.AddChild(_root);

        _session.StateChanged += OnSessionStateChanged;
        _session.SafeModeTriggered += OnSessionSafeModeTriggered;
        _session.AccountListChanged += OnSessionAccountListChanged;
        _session.RefreshPresentation.Changed += OnRefreshChanged;
        UpdateRefreshStatus();
        UpdateHeaderAndStatus();
    }

    private void OnSessionStateChanged(object? sender, EventArgs e) => OnUi(UpdateHeaderAndStatus);
    private void OnSessionSafeModeTriggered(object? sender, SafeModeTriggeredEventArgs e) => OnUi(UpdateHeaderAndStatus);
    private void OnSessionAccountListChanged(object? sender, AccountListChangedEventArgs e) => OnUi(UpdateHeaderAndStatus);
    private void OnRefreshChanged(object? sender, EventArgs e) => OnUi(UpdateRefreshStatus);
    private void OnUi(Action action)
    {
        var dispatcher = _window.Session?.Dispatcher;
        if (dispatcher is not null && !dispatcher.CheckAccess()) dispatcher.Post(() => { if (!_window.IsDisposed) action(); });
        else if (!_window.IsDisposed) action();
    }
    private void UpdateRefreshStatus()
    {
        if (_window.IsDisposed) return;
        _syncAllButton.IsEnabled = !_session.RefreshPresentation.IsRunning;
        _cancelRefreshButton.IsEnabled = _session.RefreshPresentation.IsRunning;
        _refreshStatusLabel.Text = _session.RefreshPresentation.ProgressText;
    }

    private void UpdateHeaderAndStatus()
    {
        if (_window.IsDisposed || _safeModeBadge.IsDisposed || _lockProfileButton.IsDisposed)
        {
            return;
        }

        _safeModeBadge.Text = _session.IsSafeModeActive ? "Safe Mode: Active (Read-Only)" : "Operational: Secure";
        _lockProfileButton.Text = _session.IsUnlocked ? "Lock Profile" : "Unlock Profile";

        if (_session.IsUnlocked && _session.Metadata != null)
        {
            _statusLineageLabel.Text = $"Profile: Verified · {_session.Metadata.Label} (Gen {_session.Metadata.Generation})";
        }
        else
        {
            _statusLineageLabel.Text = "Profile: [🔒 Locked - Passphrase Required]";
        }

        var totals = AccountViewModel.ComputeMultiCurrencyTotals(_session.Accounts);
        if (totals.Count > 0)
        {
            int missing = _session.Accounts.Count(a => !a.BookedAmount.HasValue);
            _statusCurrencyTotalsLabel.Text = "Cached totals: " + string.Join(" | ", totals.OrderBy(t => t.Key).Select(t => $"{t.Key} {t.Value:N2}")) +
                (missing > 0 ? $" · {missing} account(s) without balances" : string.Empty);
        }
        else
        {
            _statusCurrencyTotalsLabel.Text = "Balances: Unavailable";
        }
    }

    public async Task<bool> ShowUnlockDialogAsync()
    {
        return await _session.EnsureUnlockedAsync(_dialogService);
    }

    /// <summary>
    /// Attaches the main window visual tree as root to the specified UI session.
    /// </summary>
    public void Attach(UiSession session)
    {
        ArgumentNullException.ThrowIfNull(session);
        session.AddRoot(_window);
    }

    public void Dispose()
    {
        _session.StateChanged -= OnSessionStateChanged;
        _session.SafeModeTriggered -= OnSessionSafeModeTriggered;
        _session.AccountListChanged -= OnSessionAccountListChanged;
        _session.RefreshPresentation.Changed -= OnRefreshChanged;
        _session.RefreshPresentation.CancelForWindowClose();
        Accounts.Dispose();
        Transactions.Dispose();
        Insights.Dispose();
        Connections.Dispose();
        Payments.Dispose();
        Security.Dispose();
        _window.Dispose();
    }
}
