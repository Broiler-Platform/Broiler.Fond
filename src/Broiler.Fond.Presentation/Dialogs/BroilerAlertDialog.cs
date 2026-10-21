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
/// Reusable modal alert dialog presenting informational or error notifications with an OK action.
/// </summary>
public sealed class BroilerAlertDialog : IDisposable
{
    private readonly StandardDialog _dialog;
    private readonly StandardLabel _messageLabel;
    private readonly StandardButton _okButton;

    public StandardDialog Dialog => _dialog;
    public string Title => _dialog.Title;
    public string Message => _messageLabel.Text;
    public StandardButton OkButton => _okButton;

    public BroilerAlertDialog(string title, string message)
    {
        _dialog = new StandardDialog
        {
            Title = title,
            PreferredSize = new BSize(420, 180)
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
            AccessibleName = "Alert Message"
        };
        panel.AddChild(_messageLabel);

        _okButton = new StandardButton
        {
            Text = "OK",
            AccessibleName = "Acknowledge Alert"
        };
        _okButton.Clicked += (_, _) => _dialog.Accept("ok");

        panel.AddChild(_okButton);

        DialogLayout.Attach(_dialog, panel);
    }

    public void Accept() => _dialog.Accept("ok");
    public void Cancel() => _dialog.Cancel();

    public Task<UiDialogResult> ShowModalAsync(UiWindow owner, BRect? placement = null)
    {
        return DialogLayout.ShowModalAsync(_dialog, owner, placement);
    }

    public void Dispose() => _dialog.Dispose();
}
