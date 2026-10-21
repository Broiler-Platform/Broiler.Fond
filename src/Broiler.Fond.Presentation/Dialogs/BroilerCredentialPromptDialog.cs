namespace Broiler.Fond.Presentation.Dialogs;

using System;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using Broiler.Fond.Kernel.FinTs;
using Broiler.Fond.Presentation.Services;
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
/// Credential prompt dialog for ephemeral banking PIN or TAN entry.
/// Owned buffers are cleared on transfer/disposal. The edit control still uses managed strings.
/// </summary>
public sealed class BroilerCredentialPromptDialog : IDisposable
{
    private readonly StandardDialog _dialog;
    private readonly StandardLabel _messageLabel;
    private readonly StandardEdit _credentialEdit;
    private readonly StandardButton _submitButton;
    private readonly StandardButton _cancelButton;
    private readonly FinTsCredentialKind _kind;

    public StandardDialog Dialog => _dialog;
    public string Title => _dialog.Title;
    public StandardLabel MessageLabel => _messageLabel;
    public StandardEdit CredentialEdit => _credentialEdit;
    public StandardButton SubmitButton => _submitButton;
    public StandardButton CancelButton => _cancelButton;
    public SecureInputWrapper CredentialInput { get; } = new();
    public FinTsSessionCredential? CapturedCredential { get; private set; }

    public BroilerCredentialPromptDialog(string connectionName, string userId, FinTsCredentialKind kind = FinTsCredentialKind.Pin)
        : this(new CredentialPromptRequest(
            kind == FinTsCredentialKind.Pin ? "PIN Authentication" : "TAN Authentication",
            $"Enter {kind} for {connectionName} ({userId}):",
            connectionName,
            kind == FinTsCredentialKind.Tan))
    {
        _kind = kind;
    }

    public BroilerCredentialPromptDialog(CredentialPromptRequest request)
    {
        _kind = request.IsTan ? FinTsCredentialKind.Tan : FinTsCredentialKind.Pin;

        _dialog = new StandardDialog
        {
            Title = string.IsNullOrWhiteSpace(request.Title) ? (request.IsTan ? "TAN Authentication" : "PIN Authentication") : request.Title,
            PreferredSize = new BSize(460, 240)
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

        string promptText = string.IsNullOrWhiteSpace(request.PromptMessage)
            ? $"Enter {(request.IsTan ? "TAN" : "PIN")} for {request.ConnectionName}:"
            : request.PromptMessage;

        _messageLabel = new StandardLabel
        {
            Text = promptText,
            Wrapping = UiTextWrapping.Wrap,
            AccessibleName = "Credential Prompt Message"
        };
        content.AddChild(_messageLabel);

        if (request.IsTan && !string.IsNullOrWhiteSpace(request.TanProcedureName))
        {
            var procedureLabel = new StandardLabel
            {
                Text = $"TAN Procedure: {request.TanProcedureName}",
                AccessibleName = "TAN Procedure Name"
            };
            content.AddChild(procedureLabel);
        }

        _credentialEdit = new StandardEdit
        {
            IsPassword = true,
            PlaceholderText = request.IsTan ? "Enter TAN" : "Enter PIN",
            AccessibleName = request.IsTan ? "TAN Input Field" : "PIN Input Field"
        };
        _credentialEdit.Submitted += (_, _) => Accept();
        content.AddChild(_credentialEdit);

        panel.AddChild(content);

        var buttonBar = new DialogActionPanel
        {
            LayoutMode = UiPanelLayoutMode.Stack,
            Spacing = 12
        };

        _submitButton = new StandardButton
        {
            Text = "Submit",
            AccessibleName = "Submit Credential Button"
        };
        _submitButton.Clicked += (_, _) => Submit();

        _cancelButton = new StandardButton
        {
            Text = "Cancel",
            AccessibleName = "Cancel Button"
        };
        _cancelButton.Clicked += (_, _) => Cancel();

        buttonBar.AddChild(_submitButton);
        buttonBar.AddChild(_cancelButton);

        panel.AddChild(buttonBar);

        DialogLayout.Attach(_dialog, panel);
    }

    public void Submit() => Accept();

    public void Accept()
    {
        CapturedCredential?.Dispose();
        CapturedCredential = null;
        byte[]? bytes = null;
        try
        {
            bytes = !string.IsNullOrEmpty(_credentialEdit.Text)
                ? Encoding.UTF8.GetBytes(_credentialEdit.Text)
                : CredentialInput.GetBytesAndClear();
            if (bytes is null || bytes.Length is < 1 or > FinTsSessionCredential.MaximumLength) return;
            CapturedCredential = FinTsSessionCredential.CaptureAndClear(bytes, _kind, TimeSpan.FromMinutes(5));
        }
        finally
        {
            if (bytes is not null) CryptographicOperations.ZeroMemory(bytes);
            _credentialEdit.Text = string.Empty;
            CredentialInput.Clear();
        }
        _dialog.Accept("submit");
    }

    public void Cancel()
    {
        CapturedCredential?.Dispose();
        CapturedCredential = null;
        CredentialInput.Clear();
        if (!_credentialEdit.IsDisposed) _credentialEdit.Text = string.Empty;
        if (!_dialog.IsDisposed) _dialog.Cancel();
    }

    /// <summary>Transfers ownership exactly once. The caller must dispose the result.</summary>
    public FinTsSessionCredential? TakeCredential()
    {
        var credential = CapturedCredential;
        CapturedCredential = null;
        return credential;
    }

    /// <summary>Legacy string contract. New banking workflows use TakeCredential instead.</summary>
    public CredentialPromptResult GetResult()
    {
        using var credential = TakeCredential();
        if (credential is null) return new(false, null, true);
        byte[] buffer = new byte[FinTsSessionCredential.MaximumLength];
        try
        {
            return credential.TryCopyTo(buffer, out int written) == FinTsCredentialCopyResult.Copied
                ? new(true, Encoding.UTF8.GetString(buffer, 0, written), false)
                : new(false, null, true);
        }
        finally { CryptographicOperations.ZeroMemory(buffer); }
    }

    public Task<UiDialogResult> ShowModalAsync(UiWindow owner, BRect? placement = null)
    {
        return DialogLayout.ShowModalAsync(_dialog, owner, placement);
    }

    public void Dispose()
    {
        CapturedCredential?.Dispose();
        CapturedCredential = null;
        CredentialInput.Dispose();
        if (!_credentialEdit.IsDisposed)
        {
            _credentialEdit.Text = string.Empty;
        }
        _dialog.Dispose();
    }
}
