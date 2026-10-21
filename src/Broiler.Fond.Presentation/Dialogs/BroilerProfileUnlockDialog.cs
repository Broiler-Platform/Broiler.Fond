namespace Broiler.Fond.Presentation.Dialogs;

using System;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using Broiler.Fond.Presentation.Accessibility;
using Broiler.Fond.Presentation.Services;
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
/// Profile unlock dialog prompting for master passphrase to decrypt the profile store (store-v1.xsd).
/// Invariant: Memory hygiene is maintained by clearing password fields upon completion or cancellation.
/// </summary>
public sealed class BroilerProfileUnlockDialog : IDisposable
{
    private readonly StandardDialog _dialog;
    private readonly StandardEdit _passphraseEdit;
    private readonly StandardLabel _statusLabel;
    private readonly StandardButton _unlockButton;
    private readonly StandardButton _cancelButton;
    private byte[]? _ephemeralSecret;

    public StandardDialog Dialog => _dialog;
    public string Title => _dialog.Title;
    public StandardEdit PassphraseEdit => _passphraseEdit;
    public StandardLabel StatusLabel => _statusLabel;
    public StandardButton UnlockButton => _unlockButton;
    public StandardButton CancelButton => _cancelButton;
    public SecureInputWrapper PassphraseInput { get; } = new();

    public BroilerProfileUnlockDialog(HostSession? session = null, string profileDirectory = "")
        : this(profileDirectory)
    {
    }

    public BroilerProfileUnlockDialog(string profileDirectory = "")
    {
        _dialog = new StandardDialog
        {
            Title = "Unlock Profile",
            PreferredSize = new BSize(460, 260)
        };

        var panel = new StandardPanel
        {
            LayoutMode = UiPanelLayoutMode.Stack,
            Spacing = 12
        };

        var header = new StandardLabel
        {
            Text = "Enter master passphrase to unlock profile store",
            Wrapping = UiTextWrapping.Wrap,
            AccessibleName = "Unlock Header"
        };
        panel.AddChild(header);

        var form = new StandardPanel
        {
            LayoutMode = UiPanelLayoutMode.Stack,
            Spacing = 10
        };

        _passphraseEdit = new StandardEdit
        {
            IsPassword = true,
            PlaceholderText = "Master Passphrase",
            AccessibleName = "Master Passphrase Field"
        };
        _passphraseEdit.Submitted += (_, _) => Accept();
        form.AddChild(_passphraseEdit);

        _statusLabel = new StandardLabel
        {
            Text = "AES-256-GCM · PBKDF2 calibrated store",
            Wrapping = UiTextWrapping.Wrap,
            AccessibleName = "KDF Status"
        };
        form.AddChild(_statusLabel);

        panel.AddChild(form);

        var buttonBar = new DialogActionPanel
        {
            LayoutMode = UiPanelLayoutMode.Stack,
            Spacing = 12
        };

        _unlockButton = new StandardButton
        {
            Text = "Unlock",
            AccessibleName = "Unlock Profile Button"
        };
        _unlockButton.Clicked += (_, _) => Accept();

        _cancelButton = new StandardButton
        {
            Text = "Cancel",
            AccessibleName = "Cancel Unlock Button"
        };
        _cancelButton.Clicked += (_, _) => Cancel();

        buttonBar.AddChild(_unlockButton);
        buttonBar.AddChild(_cancelButton);

        panel.AddChild(buttonBar);

        DialogLayout.Attach(_dialog, panel);
    }

    public void Accept()
    {
        string text = _passphraseEdit.Text;
        if (string.IsNullOrEmpty(text) && PassphraseInput.Length > 0)
        {
            text = PassphraseInput.GetText();
        }

        if (!string.IsNullOrEmpty(text))
        {
            _ephemeralSecret = Encoding.UTF8.GetBytes(text);
        }
        _passphraseEdit.Text = string.Empty;
        PassphraseInput.Clear();
        _dialog.Accept("unlock");
    }

    public void Cancel()
    {
        WipeSecret();
        PassphraseInput.Clear();
        _passphraseEdit.Text = string.Empty;
        _dialog.Cancel();
    }

    public void Close(BroilerDialogResult result = BroilerDialogResult.Cancel)
    {
        if (result == BroilerDialogResult.OK || result == BroilerDialogResult.Yes)
        {
            Accept();
        }
        else
        {
            Cancel();
        }
    }

    public ProfileUnlockResult GetResult()
    {
        if (_ephemeralSecret != null)
        {
            string passphrase = Encoding.UTF8.GetString(_ephemeralSecret);
            WipeSecret();
            return new ProfileUnlockResult(true, passphrase, false);
        }
        return new ProfileUnlockResult(false, null, true);
    }

    private void WipeSecret()
    {
        if (_ephemeralSecret != null)
        {
            CryptographicOperations.ZeroMemory(_ephemeralSecret);
            _ephemeralSecret = null;
        }
    }

    public Task<UiDialogResult> ShowModalAsync(UiWindow owner, BRect? placement = null)
    {
        return DialogLayout.ShowModalAsync(_dialog, owner, placement);
    }

    public void Dispose()
    {
        WipeSecret();
        PassphraseInput.Dispose();
        if (!_passphraseEdit.IsDisposed)
        {
            _passphraseEdit.Text = string.Empty;
        }
        _dialog.Dispose();
    }
}
