namespace Broiler.Fond.Presentation.Dialogs;

using System;
using System.Threading.Tasks;
using Broiler.Fond.Presentation.Sessions;
using Broiler.Graphics.Geometry;
using Broiler.Graphics.Text;
using Broiler.UI;
using Broiler.UI.Button.Standard;
using Broiler.UI.ComboBox;
using Broiler.UI.ComboBox.Standard;
using Broiler.UI.Dialog;
using Broiler.UI.Dialog.Standard;
using Broiler.UI.Label;
using Broiler.UI.Label.Standard;
using Broiler.UI.Panel;
using Broiler.UI.Panel.Standard;
using Broiler.UI.Window;

/// <summary>
/// Dialog projecting 30/60/90-day cash flow balances and recurring liquidity demands.
/// </summary>
public sealed class BroilerCashFlowForecastDialog : IDisposable
{
    private readonly StandardDialog _dialog;
    private readonly StandardComboBox _horizonComboBox;
    private readonly StandardLabel _projectedBalanceLabel;
    private readonly StandardLabel _inflowLabel;
    private readonly StandardLabel _outflowLabel;
    private readonly StandardLabel _warningBanner;
    private readonly StandardButton _closeButton;

    public StandardDialog Dialog => _dialog;
    public string Title => _dialog.Title;
    public StandardComboBox HorizonComboBox => _horizonComboBox;
    public StandardLabel ProjectedBalanceLabel => _projectedBalanceLabel;
    public StandardLabel InflowLabel => _inflowLabel;
    public StandardLabel OutflowLabel => _outflowLabel;
    public StandardLabel WarningBanner => _warningBanner;
    public StandardButton CloseButton => _closeButton;

    public BroilerCashFlowForecastDialog(HostSession? session = null)
        : this()
    {
    }

    public BroilerCashFlowForecastDialog()
    {
        _dialog = new StandardDialog
        {
            Title = "Cash Flow & Liquidity Projection",
            PreferredSize = new BSize(560, 360)
        };

        var panel = new StandardPanel
        {
            LayoutMode = UiPanelLayoutMode.Stack,
            Spacing = 12
        };

        var topPanel = new StandardPanel
        {
            LayoutMode = UiPanelLayoutMode.Stack,
            Spacing = 8
        };

        var horizonLabel = new StandardLabel
        {
            Text = "Forecast Time Horizon:",
            AccessibleName = "Horizon Label"
        };
        topPanel.AddChild(horizonLabel);

        _horizonComboBox = new StandardComboBox();
        _horizonComboBox.SetItems([
            new UiComboBoxItem("30", "30 Days Forward Projection"),
            new UiComboBoxItem("60", "60 Days Forward Projection"),
            new UiComboBoxItem("90", "90 Days Forward Projection")
        ]);
        _horizonComboBox.SelectedIndex = 0;
        topPanel.AddChild(_horizonComboBox);

        panel.AddChild(topPanel);

        var content = new StandardPanel
        {
            LayoutMode = UiPanelLayoutMode.Stack,
            Spacing = 10
        };

        _projectedBalanceLabel = new StandardLabel
        {
            Text = "Projected End Balance: EUR 4,820.50",
            Wrapping = UiTextWrapping.Wrap,
            AccessibleName = "Projected Balance"
        };
        content.AddChild(_projectedBalanceLabel);

        _inflowLabel = new StandardLabel
        {
            Text = "Expected Inflows: +EUR 3,200.00 (Salary & recurring receipts)",
            Wrapping = UiTextWrapping.Wrap,
            AccessibleName = "Expected Inflow"
        };
        content.AddChild(_inflowLabel);

        _outflowLabel = new StandardLabel
        {
            Text = "Scheduled Outflows: -EUR 1,450.00 (Rent, standing orders, direct debits)",
            Wrapping = UiTextWrapping.Wrap,
            AccessibleName = "Scheduled Outflow"
        };
        content.AddChild(_outflowLabel);

        _warningBanner = new StandardLabel
        {
            Text = "Liquidity Status: Healthy · No overdraft risk projected.",
            Wrapping = UiTextWrapping.Wrap,
            AccessibleName = "Liquidity Status Banner"
        };
        content.AddChild(_warningBanner);

        panel.AddChild(content);

        _closeButton = new StandardButton
        {
            Text = "Close",
            AccessibleName = "Close Dialog Button"
        };
        _closeButton.Clicked += (_, _) => _dialog.Cancel();

        panel.AddChild(_closeButton);

        DialogLayout.Attach(_dialog, panel);
    }

    public Task<UiDialogResult> ShowModalAsync(UiWindow owner, BRect? placement = null)
    {
        return DialogLayout.ShowModalAsync(_dialog, owner, placement);
    }

    public void Dispose() => _dialog.Dispose();
}
