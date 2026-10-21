namespace Broiler.Fond.Presentation.Dialogs;

using System;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using Broiler.Fond.Kernel.FinTs;
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
/// Mode of Strong Customer Authentication challenge.
/// </summary>
public enum ScaChallengeMode
{
    PhotoTanMatrix,
    PushTanDecoupled,
    ChipTanFlicker,
    SmsTan,
    AppApproval
}

/// <summary>
/// Strong Customer Authentication (SCA) challenge dialog supporting PhotoTAN, Flicker codes,
/// QR codes, and decoupled push challenge authorization.
/// </summary>
public sealed class BroilerScaChallengeDialog : IDisposable
{
    private readonly StandardDialog _dialog;
    private readonly StandardLabel _modeLabel;
    private readonly StandardLabel _challengePayloadLabel;
    private readonly StandardEdit _tanEdit;
    private readonly StandardButton _submitButton;
    private readonly StandardButton _cancelButton;
    private byte[]? _ephemeralTan;

    public StandardDialog Dialog => _dialog;
    public string Title => _dialog.Title;
    public StandardLabel ModeLabel => _modeLabel;
    public StandardLabel ChallengePayloadLabel => _challengePayloadLabel;
    public StandardEdit TanEdit => _tanEdit;
    public StandardButton SubmitButton => _submitButton;
    public StandardButton CancelButton => _cancelButton;

    public BroilerScaChallengeDialog(
        string procedure,
        string orderId,
        string challengeText,
        int code = 0,
        ScaChallengeMode mode = ScaChallengeMode.PhotoTanMatrix)
        : this(procedure, $"{challengeText} [Order: {orderId}]", true)
    {
    }

    public BroilerScaChallengeDialog(
        string mode = "photoTAN",
        string challengePayload = "Scan matrix with banking app",
        bool requiresTanInput = true)
    {
        _dialog = new StandardDialog
        {
            Title = "Strong Customer Authentication (SCA)",
            PreferredSize = new BSize(500, 360)
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

        _modeLabel = new StandardLabel
        {
            Text = $"SCA Method: {mode}",
            Wrapping = UiTextWrapping.Wrap,
            AccessibleName = "SCA Method Indicator"
        };
        content.AddChild(_modeLabel);

        _challengePayloadLabel = new StandardLabel
        {
            Text = challengePayload,
            Wrapping = UiTextWrapping.Wrap,
            AccessibleName = "SCA Challenge Payload"
        };
        content.AddChild(_challengePayloadLabel);

        _tanEdit = new StandardEdit
        {
            IsPassword = true,
            PlaceholderText = "Enter generated TAN",
            AccessibleName = "TAN Entry Field",
            IsEnabled = requiresTanInput
        };
        _tanEdit.Submitted += (_, _) => Accept();
        content.AddChild(_tanEdit);

        panel.AddChild(content);

        var buttonBar = new DialogActionPanel
        {
            LayoutMode = UiPanelLayoutMode.Stack,
            Spacing = 12
        };

        _submitButton = new StandardButton
        {
            Text = "Authorize",
            AccessibleName = "Authorize Transaction Button"
        };
        _submitButton.Clicked += (_, _) => Accept();

        _cancelButton = new StandardButton
        {
            Text = "Cancel",
            AccessibleName = "Cancel Authorization Button"
        };
        _cancelButton.Clicked += (_, _) => Cancel();

        buttonBar.AddChild(_submitButton);
        buttonBar.AddChild(_cancelButton);

        panel.AddChild(buttonBar);

        DialogLayout.Attach(_dialog, panel);
    }

    public void Accept()
    {
        string text = _tanEdit.Text;
        if (!string.IsNullOrEmpty(text))
        {
            _ephemeralTan = Encoding.UTF8.GetBytes(text);
        }
        _tanEdit.Text = string.Empty;
        _dialog.Accept("authorized");
    }

    public void Cancel()
    {
        WipeSecret();
        _tanEdit.Text = string.Empty;
        _dialog.Cancel();
    }

    public string? GetTanAndWipe()
    {
        if (_ephemeralTan != null)
        {
            string tan = Encoding.UTF8.GetString(_ephemeralTan);
            WipeSecret();
            return tan;
        }
        return null;
    }

    private void WipeSecret()
    {
        if (_ephemeralTan != null)
        {
            CryptographicOperations.ZeroMemory(_ephemeralTan);
            _ephemeralTan = null;
        }
    }

    public Task<UiDialogResult> ShowModalAsync(UiWindow owner, BRect? placement = null)
    {
        return DialogLayout.ShowModalAsync(_dialog, owner, placement);
    }

    public void Dispose()
    {
        WipeSecret();
        if (!_tanEdit.IsDisposed)
        {
            _tanEdit.Text = string.Empty;
        }
        _dialog.Dispose();
    }
}
