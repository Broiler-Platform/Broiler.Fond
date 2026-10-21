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
using Broiler.UI.Panel;
using Broiler.UI.Panel.Standard;
using Broiler.UI.Window;

/// <summary>
/// Dialog for restoring an encrypted backup archive into the active profile.
/// </summary>
public sealed class BroilerRestoreBackupDialog : IDisposable
{
    private readonly StandardDialog _dialog;
    private readonly StandardEdit _backupPathEdit;
    private readonly StandardLabel _summaryLabel;
    private readonly StandardButton _restoreButton;
    private readonly StandardButton _cancelButton;

    public StandardDialog Dialog => _dialog;
    public string Title => _dialog.Title;
    public StandardEdit BackupPathEdit => _backupPathEdit;
    public StandardLabel SummaryLabel => _summaryLabel;
    public StandardButton RestoreButton => _restoreButton;
    public StandardButton CancelButton => _cancelButton;

    public BroilerRestoreBackupDialog(HostSession? session = null, string initialPath = "")
        : this(initialPath)
    {
    }

    public BroilerRestoreBackupDialog(string initialPath = "")
    {
        _dialog = new StandardDialog
        {
            Title = "Restore Profile Backup",
            PreferredSize = new BSize(500, 240)
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

        var label = new StandardLabel
        {
            Text = "Select backup archive file path:",
            AccessibleName = "Backup Path Label"
        };
        content.AddChild(label);

        _backupPathEdit = new StandardEdit
        {
            Text = initialPath,
            PlaceholderText = "/path/to/backup.fond-bak",
            AccessibleName = "Backup File Path"
        };
        content.AddChild(_backupPathEdit);

        _summaryLabel = new StandardLabel
        {
            Text = "Preflight: AES-256-GCM encrypted archive",
            Wrapping = UiTextWrapping.Wrap,
            AccessibleName = "Backup Summary Status"
        };
        content.AddChild(_summaryLabel);

        panel.AddChild(content);

        var buttonBar = new DialogActionPanel
        {
            LayoutMode = UiPanelLayoutMode.Stack,
            Spacing = 12
        };

        _restoreButton = new StandardButton
        {
            Text = "Restore Backup",
            AccessibleName = "Restore Backup Button"
        };
        _restoreButton.Clicked += (_, _) => Accept();

        _cancelButton = new StandardButton
        {
            Text = "Cancel",
            AccessibleName = "Cancel Button"
        };
        _cancelButton.Clicked += (_, _) => Cancel();

        buttonBar.AddChild(_restoreButton);
        buttonBar.AddChild(_cancelButton);

        panel.AddChild(buttonBar);

        DialogLayout.Attach(_dialog, panel);
    }

    public void Accept() => _dialog.Accept("restore");
    public void Cancel() => _dialog.Cancel();

    public string SelectedBackupPath => _backupPathEdit.Text.Trim();
    public bool IsConfirmed => _dialog.CompletedResult?.Kind == UiDialogResultKind.Accepted;

    public Task<UiDialogResult> ShowModalAsync(UiWindow owner, BRect? placement = null)
    {
        return DialogLayout.ShowModalAsync(_dialog, owner, placement);
    }

    public void Dispose() => _dialog.Dispose();
}
