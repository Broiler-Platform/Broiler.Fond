namespace Broiler.Fond.Presentation.Views;

using System;
using System.Linq;
using Broiler.Fond.Presentation.Dialogs;
using Broiler.Fond.Presentation.Services;
using Broiler.Fond.Presentation.Sessions;
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
using Broiler.UI.Window;

/// <summary>
/// Financial insights view displaying category budgets, cash-flow trends, and net-worth breakdowns.
/// </summary>
public sealed class InsightsView : IDisposable
{
    private readonly HostSession _session;
    private readonly IDialogService _dialogService;
    private readonly StandardPanel _root;
    private readonly StandardToolbar _toolbar;
    private readonly StandardButton _forecastButton;
    private readonly StandardButton _rulesButton;
    private readonly StandardLabel _netWorthLabel;
    private readonly StandardListView _budgetsList;

    public StandardPanel Root => _root;
    public StandardToolbar Toolbar => _toolbar;
    public StandardButton ForecastButton => _forecastButton;
    public StandardButton RulesButton => _rulesButton;
    public StandardLabel NetWorthLabel => _netWorthLabel;
    public StandardListView BudgetsList => _budgetsList;

    public InsightsView(HostSession session, IDialogService? dialogService = null)
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

        _forecastButton = new StandardButton
        {
            Text = "30/60/90-Day Cash Flow Forecast",
            AccessibleName = "Open Cash Flow Forecast Dialog"
        };
        _forecastButton.Clicked += async (_, _) =>
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

            using var dlg = new BroilerCashFlowForecastDialog(_session);
            await dlg.ShowModalAsync(win);
        };
        _toolbar.AddChild(_forecastButton);

        _rulesButton = new StandardButton
        {
            Text = "Categorization Rules",
            AccessibleName = "Open Categorization Rules Dialog"
        };
        _rulesButton.Clicked += async (_, _) =>
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

            using var dlg = new BroilerCategorizationRulesDialog(_session);
            await dlg.ShowModalAsync(win);
            RefreshBudgets();
        };
        _toolbar.AddChild(_rulesButton);

        _netWorthLabel = new StandardLabel
        {
            Text = "Net Worth: EUR 12,450.00",
            AccessibleName = "Net Worth Display"
        };
        _toolbar.AddChild(_netWorthLabel);

        _root.AddChild(_toolbar);
        _root.SetDock(_toolbar, UiDock.Top);

        _budgetsList = new StandardListView
        {
            PreferredSize = new BSize(600, 380),
            AccessibleName = "Category Budgets List"
        };
        _root.AddChild(_budgetsList);
        _root.SetDock(_budgetsList, UiDock.Fill);

        _session.StateChanged += OnStateChanged;
        RefreshBudgets();
    }

    private void OnStateChanged(object? sender, EventArgs e) => RefreshBudgets();

    private UiWindow? GetWindow() => (_dialogService as BroilerDialogService)?.WindowAccessor?.Invoke();

    public void RefreshBudgets()
    {
        if (_budgetsList.IsDisposed) return;

        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var budgetProgress = _session.GetBudgetProgress(today);

        var items = budgetProgress.Select(b => new UiListItem(
            b.Category,
            $"{b.Category}: {b.ActualSpent:N2} of {b.TargetAmount:N2} {b.Currency} ({b.PercentageConsumed:F1}%)",
            $"{b.Category} — {b.PercentageConsumed:F1}% used",
            $"{b.ActualSpent:N2} / {b.TargetAmount:N2} {b.Currency}",
            false,
            b)).ToArray();

        _budgetsList.SetItems(items);
    }

    public void Dispose()
    {
        _session.StateChanged -= OnStateChanged;
        _root.Dispose();
    }
}
