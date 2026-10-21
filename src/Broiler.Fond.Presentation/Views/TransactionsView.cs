namespace Broiler.Fond.Presentation.Views;

using System;
using System.Collections.Generic;
using System.Linq;
using Broiler.Fond.Presentation.Dialogs;
using Broiler.Fond.Presentation.Services;
using Broiler.Fond.Presentation.Sessions;
using Broiler.Fond.Presentation.ViewModels;
using Broiler.Graphics.Geometry;
using Broiler.Graphics.Text;
using Broiler.UI;
using Broiler.UI.Button.Standard;
using Broiler.UI.Edit.Standard;
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
/// Transactions view providing high-performance transaction list presentation, search filtering, and actions.
/// </summary>
public sealed class TransactionsView : IDisposable
{
    private readonly HostSession _session;
    private readonly IDialogService _dialogService;
    private readonly StandardPanel _root;
    private readonly StandardToolbar _toolbar;
    private readonly StandardEdit _searchEdit;
    private readonly StandardButton _splitButton;
    private readonly StandardButton _csvExportButton;
    private readonly StandardLabel _countLabel;
    private readonly StandardListView _transactionsList;

    public StandardPanel Root => _root;
    public StandardToolbar Toolbar => _toolbar;
    public StandardEdit SearchEdit => _searchEdit;
    public StandardButton SplitButton => _splitButton;
    public StandardButton CsvExportButton => _csvExportButton;
    public StandardLabel CountLabel => _countLabel;
    public StandardListView TransactionsList => _transactionsList;

    public TransactionsView(HostSession session, IDialogService? dialogService = null)
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

        _searchEdit = new StandardEdit
        {
            PlaceholderText = "Search payee, purpose, IBAN...",
            PreferredSize = new BSize(280, 32),
            AccessibleName = "Transaction Search Filter"
        };
        _searchEdit.Submitted += (_, _) => FilterList();
        _toolbar.AddChild(_searchEdit);

        _transactionsList = new StandardListView
        {
            PreferredSize = new BSize(600, 400),
            AccessibleName = "Transaction History List"
        };

        _splitButton = new StandardButton
        {
            Text = "Split Transaction",
            AccessibleName = "Split Selected Transaction"
        };
        _splitButton.Clicked += async (_, _) =>
        {
            if (!await _session.EnsureUnlockedAsync(_dialogService))
            {
                return;
            }

            if (string.IsNullOrEmpty(_transactionsList.SelectedItemId))
            {
                await _dialogService.ShowAlertAsync("Split Transaction", "Please select a transaction to split.");
                return;
            }

            var tx = _session.Transactions.FirstOrDefault(t => t.TransactionId.ToString() == _transactionsList.SelectedItemId);
            if (tx != null)
            {
                var win = GetWindow();
                if (win == null)
                {
                    return;
                }

                using var dlg = new BroilerTransactionSplitDialog(_session, tx);
                await dlg.ShowModalAsync(win);
                FilterList();
            }
        };
        _toolbar.AddChild(_splitButton);

        _csvExportButton = new StandardButton
        {
            Text = "Export to CSV",
            AccessibleName = "Export Transactions to CSV"
        };
        _csvExportButton.Clicked += async (_, _) =>
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

            using var dlg = new BroilerCsvExportDialog(_session);
            await dlg.ShowModalAsync(win);
        };
        _toolbar.AddChild(_csvExportButton);

        _countLabel = new StandardLabel
        {
            Text = "0 transactions",
            AccessibleName = "Transaction Record Count"
        };
        _toolbar.AddChild(_countLabel);

        _root.AddChild(_toolbar);
        _root.SetDock(_toolbar, UiDock.Top);

        // Dock layout follows child order: reserve the toolbar before filling the list.
        _root.AddChild(_transactionsList);
        _root.SetDock(_transactionsList, UiDock.Fill);

        _session.StateChanged += OnStateChanged;
        FilterList();
    }

    private void OnStateChanged(object? sender, EventArgs e) => FilterList();

    private UiWindow? GetWindow() => (_dialogService as BroilerDialogService)?.WindowAccessor?.Invoke();

    public void FilterList()
    {
        if (_transactionsList.IsDisposed) return;

        string query = _searchEdit.Text.Trim();
        IEnumerable<TransactionViewModel> txs = _session.Transactions;

        if (!string.IsNullOrEmpty(query))
        {
            txs = txs.Where(t =>
                (t.Record.CounterpartyName != null && t.Record.CounterpartyName.Contains(query, StringComparison.OrdinalIgnoreCase)) ||
                (t.Record.RemittanceText != null && t.Record.RemittanceText.Contains(query, StringComparison.OrdinalIgnoreCase)) ||
                (t.AccountIban != null && t.AccountIban.Contains(query, StringComparison.OrdinalIgnoreCase)));
        }

        var list = txs.ToArray();
        _countLabel.Text = $"{list.Length} transactions";

        var items = list.Select(tx => new UiListItem(
            tx.TransactionId.ToString(),
            $"{tx.BookingDateDisplay} · {tx.CounterpartyDisplay} · {tx.AmountDisplay}",
            $"{tx.CounterpartyDisplay} — {tx.RemittanceSummary}",
            $"{tx.BookingDateDisplay} · {tx.AmountDisplay}",
            false,
            tx)).ToArray();

        _transactionsList.SetItems(items);
    }

    public void Dispose()
    {
        _session.StateChanged -= OnStateChanged;
        _root.Dispose();
    }
}
