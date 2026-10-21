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
using Broiler.UI.ListView.Standard;
using Broiler.UI.Panel;
using Broiler.UI.Panel.Standard;
using Broiler.UI.Window;

/// <summary>
/// Dialog for inspecting, scheduling, and deleting recurring standing orders.
/// </summary>
public sealed class BroilerStandingOrdersDialog : IDisposable
{
    private readonly StandardDialog _dialog;
    private readonly StandardListView _listView;
    private readonly StandardComboBox _cadenceComboBox;
    private readonly StandardButton _createButton;
    private readonly StandardButton _deleteButton;
    private readonly StandardButton _closeButton;

    public StandardDialog Dialog => _dialog;
    public string Title => _dialog.Title;
    public StandardListView ListView => _listView;
    public StandardComboBox CadenceComboBox => _cadenceComboBox;
    public StandardButton CreateButton => _createButton;
    public StandardButton DeleteButton => _deleteButton;
    public StandardButton CloseButton => _closeButton;

    public BroilerStandingOrdersDialog(HostSession? session = null)
    {
        _dialog = new StandardDialog
        {
            Title = "Standing Orders",
            PreferredSize = new BSize(600, 420)
        };

        var panel = new StandardPanel
        {
            LayoutMode = UiPanelLayoutMode.Stack,
            Spacing = 12
        };

        var header = new StandardLabel
        {
            Text = "Active Standing Orders & Recurring Schedules",
            Wrapping = UiTextWrapping.Wrap,
            AccessibleName = "Standing Orders Header"
        };
        panel.AddChild(header);

        _listView = new StandardListView
        {
            PreferredSize = new BSize(560, 240),
            AccessibleName = "Standing Orders List"
        };
        panel.AddChild(_listView);

        var controlsPanel = new StandardPanel
        {
            LayoutMode = UiPanelLayoutMode.Stack,
            Spacing = 10
        };

        _cadenceComboBox = new StandardComboBox();
        _cadenceComboBox.SetItems([
            new UiComboBoxItem("monthly", "Monthly (1st of month)"),
            new UiComboBoxItem("quarterly", "Quarterly"),
            new UiComboBoxItem("semiannually", "Semi-Annually"),
            new UiComboBoxItem("annually", "Annually")
        ]);
        _cadenceComboBox.SelectedIndex = 0;
        controlsPanel.AddChild(_cadenceComboBox);

        var buttonBar = new DialogActionPanel
        {
            LayoutMode = UiPanelLayoutMode.Stack,
            Spacing = 10
        };

        _createButton = new StandardButton
        {
            Text = "Create Standing Order",
            AccessibleName = "Create Standing Order Button"
        };
        _createButton.Clicked += (_, _) => _dialog.Accept("create");

        _deleteButton = new StandardButton
        {
            Text = "Delete Selected",
            AccessibleName = "Delete Standing Order Button"
        };
        _deleteButton.Clicked += (_, _) => _dialog.Accept("delete");

        _closeButton = new StandardButton
        {
            Text = "Close",
            AccessibleName = "Close Dialog Button"
        };
        _closeButton.Clicked += (_, _) => _dialog.Cancel();

        buttonBar.AddChild(_createButton);
        buttonBar.AddChild(_deleteButton);
        buttonBar.AddChild(_closeButton);

        controlsPanel.AddChild(buttonBar);

        panel.AddChild(controlsPanel);

        DialogLayout.Attach(_dialog, panel);
    }

    public void Close() => _dialog.Cancel();

    public Task<UiDialogResult> ShowModalAsync(UiWindow owner, BRect? placement = null)
    {
        return DialogLayout.ShowModalAsync(_dialog, owner, placement);
    }

    public void Dispose() => _dialog.Dispose();
}
