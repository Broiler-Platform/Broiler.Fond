using System.Drawing;
using System.Security.Cryptography;
using System.Text;
using System.Windows.Forms;
using Broiler.Fond.Kernel.FinTs;

namespace Broiler.Fond.Host.Windows.Dialogs;

/// <summary>
/// Accessible SCA challenge dialog for two-step TAN authentication and optical/text challenges.
/// </summary>
public sealed class ScaChallengeDialog : Form
{
    private readonly Label _lblTitle;
    private readonly Label _lblProcedure;
    private readonly Label _lblOrderRef;
    private readonly Label _lblChallengeText;
    private readonly TextBox _txtChallengeDisplay;
    private readonly Label _lblTanPrompt;
    private readonly TextBox _txtTanInput;
    private readonly Button _btnSubmit;
    private readonly Button _btnCancel;

    public FinTsSessionCredential? CapturedTan { get; private set; }

    public ScaChallengeDialog(string procedureName, string? orderReference, string? challengeText, int securityFunction = 900)
    {
        Text = "Strong Customer Authentication (SCA) - Confirmation";
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        StartPosition = FormStartPosition.CenterParent;
        ClientSize = new Size(540, 380);
        AutoScaleMode = AutoScaleMode.Dpi;

        AccessibleName = "SCA Challenge Confirmation Dialog";
        AccessibleRole = AccessibleRole.Dialog;
        AccessibleDescription = "Review bank challenge details and enter your TAN to confirm the banking dialogue.";

        _lblTitle = new Label
        {
            Text = "Bank Authentication Challenge Required",
            Font = new Font(Font.FontFamily, 11, FontStyle.Bold),
            Location = new Point(20, 15),
            Size = new Size(500, 24),
            AccessibleName = "Challenge Header"
        };

        string procName = string.IsNullOrWhiteSpace(procedureName) ? $"Procedure {securityFunction}" : procedureName;
        _lblProcedure = new Label
        {
            Text = $"TAN Procedure: {procName} (Code: {securityFunction})",
            Location = new Point(20, 45),
            Size = new Size(500, 20),
            AccessibleName = "Procedure Name"
        };

        string orderRef = string.IsNullOrWhiteSpace(orderReference) ? "[None]" : orderReference;
        _lblOrderRef = new Label
        {
            Text = $"Order Reference: {orderRef}",
            Location = new Point(20, 70),
            Size = new Size(500, 20),
            AccessibleName = "Order Reference"
        };

        _lblChallengeText = new Label
        {
            Text = "Challenge Message from Bank:",
            Font = new Font(Font.FontFamily, 9, FontStyle.Bold),
            Location = new Point(20, 100),
            Size = new Size(500, 20),
            AccessibleName = "Challenge Message Label"
        };

        string challengeMsg = string.IsNullOrWhiteSpace(challengeText) ? "[Please approve via your banking app or token device]" : challengeText;
        _txtChallengeDisplay = new TextBox
        {
            Text = challengeMsg,
            Multiline = true,
            ReadOnly = true,
            ScrollBars = ScrollBars.Vertical,
            BackColor = SystemColors.Info,
            Location = new Point(20, 122),
            Size = new Size(500, 95),
            TabIndex = 0,
            AccessibleName = "Bank Challenge Message Read-Only Text"
        };

        _lblTanPrompt = new Label
        {
            Text = "Enter &TAN:",
            Font = new Font(Font.FontFamily, 9.5f, FontStyle.Bold),
            Location = new Point(20, 235),
            Size = new Size(500, 20),
            AccessibleName = "TAN Prompt Label"
        };

        _txtTanInput = new TextBox
        {
            UseSystemPasswordChar = true,
            Location = new Point(20, 258),
            Size = new Size(500, 26),
            Font = new Font(Font.FontFamily, 11),
            TabIndex = 1,
            AccessibleName = "TAN Input Box"
        };

        _btnSubmit = new Button
        {
            Text = "&Confirm TAN",
            DialogResult = DialogResult.OK,
            Location = new Point(270, 320),
            Size = new Size(130, 34),
            TabIndex = 2,
            AccessibleName = "Confirm TAN Button"
        };
        _btnSubmit.Click += (s, e) => Submit();

        _btnCancel = new Button
        {
            Text = "&Cancel",
            DialogResult = DialogResult.Cancel,
            Location = new Point(410, 320),
            Size = new Size(110, 34),
            TabIndex = 3,
            AccessibleName = "Cancel Challenge Button"
        };
        _btnCancel.Click += (s, e) =>
        {
            CapturedTan = null;
            Close();
        };

        AcceptButton = _btnSubmit;
        CancelButton = _btnCancel;

        Controls.AddRange(new Control[]
        {
            _lblTitle, _lblProcedure, _lblOrderRef, _lblChallengeText,
            _txtChallengeDisplay, _lblTanPrompt, _txtTanInput,
            _btnSubmit, _btnCancel
        });
    }

    public ScaChallengeDialog(FinTsTanChallenge challenge, FinTsTanProcedure procedure)
        : this(
            procedure.Name.HeaderText(),
            challenge.OrderReference?.HeaderText(),
            challenge.Text?.HeaderText(),
            procedure.SecurityFunction)
    {
    }

    private void Submit()
    {
        string text = _txtTanInput.Text.Trim();
        if (string.IsNullOrEmpty(text))
        {
            MessageBox.Show(this, "TAN cannot be empty.", "Input Required", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        byte[] rawBytes = Encoding.Latin1.GetBytes(text);
        try
        {
            CapturedTan = FinTsSessionCredential.CaptureAndClear(rawBytes, FinTsCredentialKind.Tan, TimeSpan.FromMinutes(5));
        }
        finally
        {
            CryptographicOperations.ZeroMemory(rawBytes);
            _txtTanInput.Text = string.Empty;
        }

        Close();
    }
}
