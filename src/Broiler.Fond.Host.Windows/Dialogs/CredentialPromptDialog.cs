using System.Drawing;
using System.Security.Cryptography;
using System.Text;
using System.Windows.Forms;
using Broiler.Fond.Kernel.FinTs;

namespace Broiler.Fond.Host.Windows.Dialogs;

/// <summary>
/// Session credential dialog for capturing online banking PINs or TANs into volatile memory buffers.
/// Invariant: Credentials are wiped with CryptographicOperations.ZeroMemory and never persisted or logged.
/// </summary>
public sealed class CredentialPromptDialog : Form
{
    private readonly Label _lblPrompt;
    private readonly Label _lblNotice;
    private readonly TextBox _txtCredential;
    private readonly Button _btnSubmit;
    private readonly Button _btnCancel;
    private readonly FinTsCredentialKind _kind;

    public FinTsSessionCredential? CapturedCredential { get; private set; }

    public CredentialPromptDialog(
        string institutionName,
        string userId,
        FinTsCredentialKind kind = FinTsCredentialKind.Pin,
        string? customPrompt = null)
    {
        _kind = kind;
        string kindName = kind == FinTsCredentialKind.Pin ? "Online Banking PIN" : "TAN Confirmation";

        Text = $"{kindName} Authentication - {institutionName}";
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        StartPosition = FormStartPosition.CenterParent;
        ClientSize = new Size(500, 240);
        AutoScaleMode = AutoScaleMode.Dpi;

        AccessibleName = $"{kindName} Entry Dialog";
        AccessibleRole = AccessibleRole.Dialog;
        AccessibleDescription = $"Enter your {kindName} for user {userId} at {institutionName}.";

        _lblPrompt = new Label
        {
            Text = customPrompt ?? $"Please enter the {kindName} for User ID '{userId}' at {institutionName}:",
            Font = new Font(Font.FontFamily, 9.5f, FontStyle.Bold),
            Location = new Point(20, 15),
            Size = new Size(460, 40),
            AccessibleName = "Prompt Description",
            AccessibleRole = AccessibleRole.StaticText
        };

        _lblNotice = new Label
        {
            Text = "NOTICE: Your credential is held exclusively in transient RAM during this dialogue and wiped immediately with zeroization. It is never logged or saved to disk.",
            ForeColor = Color.DarkSlateGray,
            Location = new Point(20, 60),
            Size = new Size(460, 35),
            AccessibleName = "Ephemeral Credential Guarantee",
            AccessibleRole = AccessibleRole.StaticText
        };

        _txtCredential = new TextBox
        {
            UseSystemPasswordChar = true,
            Location = new Point(20, 105),
            Size = new Size(460, 28),
            Font = new Font(Font.FontFamily, 11),
            TabIndex = 0,
            AccessibleName = $"{kindName} Input Box",
            AccessibleRole = AccessibleRole.Text
        };

        _btnSubmit = new Button
        {
            Text = "&Submit",
            DialogResult = DialogResult.OK,
            Location = new Point(260, 180),
            Size = new Size(110, 34),
            TabIndex = 1,
            AccessibleName = "Submit Credential Button",
            AccessibleRole = AccessibleRole.PushButton
        };
        _btnSubmit.Click += (s, e) => Submit();

        _btnCancel = new Button
        {
            Text = "&Cancel",
            DialogResult = DialogResult.Cancel,
            Location = new Point(380, 180),
            Size = new Size(100, 34),
            TabIndex = 2,
            AccessibleName = "Cancel Button",
            AccessibleRole = AccessibleRole.PushButton
        };
        _btnCancel.Click += (s, e) =>
        {
            CapturedCredential = null;
            Close();
        };

        AcceptButton = _btnSubmit;
        CancelButton = _btnCancel;

        Controls.AddRange(new Control[]
        {
            _lblPrompt, _lblNotice, _txtCredential, _btnSubmit, _btnCancel
        });
    }

    private void Submit()
    {
        string text = _txtCredential.Text;
        if (string.IsNullOrEmpty(text))
        {
            MessageBox.Show(this, "Credential cannot be empty.", "Input Required", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        byte[] rawBytes = Encoding.Latin1.GetBytes(text);
        try
        {
            CapturedCredential = FinTsSessionCredential.CaptureAndClear(rawBytes, _kind, TimeSpan.FromMinutes(5));
        }
        finally
        {
            CryptographicOperations.ZeroMemory(rawBytes);
            _txtCredential.Text = string.Empty;
        }

        Close();
    }
}
