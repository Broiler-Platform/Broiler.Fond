namespace Broiler.Fond.Presentation.Dialogs;

using System;
using System.Threading.Tasks;
using Broiler.Fond.Presentation.Sessions;
using Broiler.Graphics.Geometry;
using Broiler.Graphics.Text;
using Broiler.UI;
using Broiler.UI.Button.Standard;
using Broiler.UI.Dialog;
using Broiler.UI.Dialog.Standard;
using Broiler.UI.Edit.Standard;
using Broiler.UI.Label;
using Broiler.UI.Label.Standard;
using Broiler.UI.ListView.Standard;
using Broiler.UI.Panel;
using Broiler.UI.Panel.Standard;
using Broiler.UI.Window;

/// <summary>
/// Dialog for managing deterministic transaction categorization rules.
/// </summary>
public sealed class BroilerCategorizationRulesDialog : IDisposable
{
    private readonly StandardDialog _dialog;
    private readonly StandardListView _rulesList;
    private readonly StandardEdit _patternEdit;
    private readonly StandardEdit _categoryEdit;
    private readonly StandardButton _addButton;
    private readonly StandardButton _removeButton;
    private readonly StandardButton _closeButton;

    public StandardDialog Dialog => _dialog;
    public string Title => _dialog.Title;
    public StandardListView RulesList => _rulesList;
    public StandardEdit PatternEdit => _patternEdit;
    public StandardEdit CategoryEdit => _categoryEdit;
    public StandardButton AddButton => _addButton;
    public StandardButton RemoveButton => _removeButton;
    public StandardButton CloseButton => _closeButton;

    public BroilerCategorizationRulesDialog(HostSession? session = null)
    {
        _dialog = new StandardDialog
        {
            Title = "Transaction Categorization Rules",
            PreferredSize = new BSize(560, 420)
        };

        var panel = new StandardPanel
        {
            LayoutMode = UiPanelLayoutMode.Stack,
            Spacing = 12
        };

        var header = new StandardLabel
        {
            Text = "Configured Exact Match & Pattern Categorization Rules:",
            Wrapping = UiTextWrapping.Wrap,
            AccessibleName = "Rules Header"
        };
        panel.AddChild(header);

        _rulesList = new StandardListView
        {
            PreferredSize = new BSize(520, 220),
            AccessibleName = "Categorization Rules List"
        };
        panel.AddChild(_rulesList);

        var bottomPanel = new StandardPanel
        {
            LayoutMode = UiPanelLayoutMode.Stack,
            Spacing = 8
        };

        var inputs = new StandardPanel
        {
            LayoutMode = UiPanelLayoutMode.Stack,
            Spacing = 8
        };

        _patternEdit = new StandardEdit
        {
            PlaceholderText = "Match Pattern (e.g. REWE, Spotify)",
            AccessibleName = "Rule Pattern Edit"
        };
        inputs.AddChild(_patternEdit);

        _categoryEdit = new StandardEdit
        {
            PlaceholderText = "Target Category (e.g. Groceries)",
            AccessibleName = "Rule Category Edit"
        };
        inputs.AddChild(_categoryEdit);

        bottomPanel.AddChild(inputs);

        var buttonBar = new DialogActionPanel
        {
            LayoutMode = UiPanelLayoutMode.Stack,
            Spacing = 12
        };

        _addButton = new StandardButton
        {
            Text = "Add Rule",
            AccessibleName = "Add Rule Button"
        };
        _addButton.Clicked += (_, _) => _dialog.Accept("add");

        _removeButton = new StandardButton
        {
            Text = "Remove Selected",
            AccessibleName = "Remove Rule Button"
        };
        _removeButton.Clicked += (_, _) => _dialog.Accept("remove");

        _closeButton = new StandardButton
        {
            Text = "Close",
            AccessibleName = "Close Dialog Button"
        };
        _closeButton.Clicked += (_, _) => _dialog.Cancel();

        buttonBar.AddChild(_addButton);
        buttonBar.AddChild(_removeButton);
        buttonBar.AddChild(_closeButton);
        bottomPanel.AddChild(buttonBar);

        panel.AddChild(bottomPanel);

        DialogLayout.Attach(_dialog, panel);
    }

    public Task<UiDialogResult> ShowModalAsync(UiWindow owner, BRect? placement = null)
    {
        return DialogLayout.ShowModalAsync(_dialog, owner, placement);
    }

    public void Dispose() => _dialog.Dispose();
}
