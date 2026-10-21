namespace Broiler.Fond.Presentation.Dialogs;

using System;
using System.Threading.Tasks;
using Broiler.Fond.Presentation.ViewModels;
using Broiler.Graphics.Geometry;
using Broiler.Graphics.Text;
using Broiler.UI;
using Broiler.UI.Button.Standard;
using Broiler.UI.CheckBox.Standard;
using Broiler.UI.Dialog;
using Broiler.UI.Dialog.Standard;
using Broiler.UI.Label;
using Broiler.UI.Label.Standard;
using Broiler.UI.Panel;
using Broiler.UI.Panel.Standard;
using Broiler.UI.Window;

/// <summary>
/// High-impact modal dialog for removing a bank connection with optional cached data purging.
/// </summary>
public sealed class BroilerConnectionRemovalDialog : IDisposable
{
    private readonly StandardDialog _dialog;
    private readonly StandardLabel _warningLabel;
    private readonly StandardCheckBox _purgeCheckBox;
    private readonly StandardButton _deleteButton;
    private readonly StandardButton _cancelButton;

    public StandardDialog Dialog => _dialog;
    public string Title => _dialog.Title;
    public StandardLabel WarningLabel => _warningLabel;
    public StandardCheckBox PurgeCheckBox => _purgeCheckBox;
    public StandardButton DeleteButton => _deleteButton;
    public StandardButton CancelButton => _cancelButton;

    public BroilerConnectionRemovalDialog(ConnectionViewModel? connection)
        : this(connection?.InstitutionName ?? "Bank Connection")
    {
    }

    public BroilerConnectionRemovalDialog(string connectionName = "Bank Connection")
    {
        _dialog = new StandardDialog
        {
            Title = "Remove Bank Connection",
            PreferredSize = new BSize(480, 260)
        };

        var panel = new StandardPanel
        {
            LayoutMode = UiPanelLayoutMode.Stack,
            Spacing = 12
        };

        var content = new StandardPanel
        {
            LayoutMode = UiPanelLayoutMode.Stack,
            Spacing = 10
        };

        _warningLabel = new StandardLabel
        {
            Text = $"WARNING: Removing connection '{connectionName}' terminates FinTS synchronization for all associated accounts.",
            Wrapping = UiTextWrapping.Wrap,
            AccessibleName = "Removal Warning Message"
        };
        content.AddChild(_warningLabel);

        _purgeCheckBox = new StandardCheckBox
        {
            Text = "Purge cached transactions, balances, and statement history",
            AccessibleName = "Purge Cached Data Checkbox"
        };
        content.AddChild(_purgeCheckBox);

        panel.AddChild(content);

        var buttonBar = new DialogActionPanel
        {
            LayoutMode = UiPanelLayoutMode.Stack,
            Spacing = 12
        };

        _deleteButton = new StandardButton
        {
            Text = "Delete Connection",
            AccessibleName = "Confirm Delete Connection Button"
        };
        _deleteButton.Clicked += (_, _) => Accept();

        _cancelButton = new StandardButton
        {
            Text = "Cancel",
            AccessibleName = "Cancel Button"
        };
        _cancelButton.Clicked += (_, _) => Cancel();

        buttonBar.AddChild(_deleteButton);
        buttonBar.AddChild(_cancelButton);

        panel.AddChild(buttonBar);

        DialogLayout.Attach(_dialog, panel);
    }

    public void Accept() => _dialog.Accept("delete");
    public void Cancel() => _dialog.Cancel();

    public bool ShouldPurge => _purgeCheckBox.IsChecked == true;
    public bool PurgeCachedData => ShouldPurge;
    public bool IsConfirmed => _dialog.CompletedResult?.Kind == UiDialogResultKind.Accepted;

    public Task<UiDialogResult> ShowModalAsync(UiWindow owner, BRect? placement = null)
    {
        return DialogLayout.ShowModalAsync(_dialog, owner, placement);
    }

    public void Dispose() => _dialog.Dispose();
}
