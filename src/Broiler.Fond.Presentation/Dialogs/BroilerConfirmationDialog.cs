namespace Broiler.Fond.Presentation.Dialogs;

using System;
using System.Threading.Tasks;
using Broiler.Graphics.Geometry;
using Broiler.Graphics.Text;
using Broiler.UI;
using Broiler.UI.Button.Standard;
using Broiler.UI.Dialog;
using Broiler.UI.Dialog.Standard;
using Broiler.UI.Label;
using Broiler.UI.Label.Standard;
using Broiler.UI.Panel;
using Broiler.UI.Panel.Standard;
using Broiler.UI.Window;

/// <summary>
/// Reusable modal confirmation dialog with Confirm and Cancel actions.
/// </summary>
public sealed class BroilerConfirmationDialog : IDisposable
{
    private readonly StandardDialog _dialog;
    private readonly StandardLabel _messageLabel;
    private readonly StandardButton _confirmButton;
    private readonly StandardButton _cancelButton;

    public StandardDialog Dialog => _dialog;
    public string Title => _dialog.Title;
    public string Message => _messageLabel.Text;
    public StandardButton ConfirmButton => _confirmButton;
    public StandardButton CancelButton => _cancelButton;

    public BroilerConfirmationDialog(
        string title,
        string message,
        string? confirmLabel = null,
        string? cancelLabel = null)
    {
        _dialog = new StandardDialog
        {
            Title = title,
            PreferredSize = new BSize(440, 200)
        };

        var panel = new StandardPanel
        {
            LayoutMode = UiPanelLayoutMode.Stack,
            Spacing = 16
        };

        _messageLabel = new StandardLabel
        {
            Text = message,
            Wrapping = UiTextWrapping.Wrap,
            AccessibleName = "Confirmation Message"
        };
        panel.AddChild(_messageLabel);

        var buttonBar = new DialogActionPanel
        {
            LayoutMode = UiPanelLayoutMode.Stack,
            Spacing = 12
        };

        _confirmButton = new StandardButton
        {
            Text = confirmLabel ?? "Confirm",
            AccessibleName = confirmLabel ?? "Confirm Action"
        };
        _confirmButton.Clicked += (_, _) => _dialog.Accept("confirm");

        _cancelButton = new StandardButton
        {
            Text = cancelLabel ?? "Cancel",
            AccessibleName = cancelLabel ?? "Cancel Action"
        };
        _cancelButton.Clicked += (_, _) => _dialog.Cancel();

        buttonBar.AddChild(_confirmButton);
        buttonBar.AddChild(_cancelButton);

        panel.AddChild(buttonBar);

        DialogLayout.Attach(_dialog, panel);
    }

    public void Accept() => _dialog.Accept("confirm");
    public void Cancel() => _dialog.Cancel();

    public Task<UiDialogResult> ShowModalAsync(UiWindow owner, BRect? placement = null)
    {
        return DialogLayout.ShowModalAsync(_dialog, owner, placement);
    }

    public void Dispose() => _dialog.Dispose();
}
