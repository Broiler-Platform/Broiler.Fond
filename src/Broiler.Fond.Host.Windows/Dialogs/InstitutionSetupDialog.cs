using System.Drawing;
using System.Windows.Forms;
using Broiler.Fond.Host.Windows.Models;

namespace Broiler.Fond.Host.Windows.Dialogs;

/// <summary>
/// Accessible manual financial institution setup dialog fulfilling M1-06 manual entry requirements.
/// </summary>
public sealed class InstitutionSetupDialog : Form
{
    private readonly Label _lblTitle;
    private readonly Label _lblInstitution;
    private readonly TextBox _txtInstitution;
    private readonly Label _lblBankCode;
    private readonly TextBox _txtBankCode;
    private readonly Label _lblEndpoint;
    private readonly TextBox _txtEndpoint;
    private readonly Label _lblUserId;
    private readonly TextBox _txtUserId;
    private readonly Label _lblCustomerId;
    private readonly TextBox _txtCustomerId;
    private readonly Label _lblNotice;
    private readonly Button _btnConfirmEndpoint;
    private readonly Button _btnCancel;

    public string InstitutionName { get; private set; } = string.Empty;
    public string BankCode { get; private set; } = string.Empty;
    public FinTsEndpoint? ConfirmedEndpoint { get; private set; }
    public string UserId { get; private set; } = string.Empty;
    public string? CustomerId { get; private set; }

    public InstitutionSetupDialog()
    {
        Text = "Add Bank Connection - Manual Institution Setup";
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        StartPosition = FormStartPosition.CenterParent;
        ClientSize = new Size(540, 430);
        AutoScaleMode = AutoScaleMode.Dpi;

        AccessibleName = "Manual Bank Institution Setup Dialog";
        AccessibleRole = AccessibleRole.Dialog;
        AccessibleDescription = "Manually enter the bank name, BLZ, FinTS server address, and login ID.";

        _lblTitle = new Label
        {
            Text = "Manual FinTS Connection Configuration",
            Font = new Font(Font.FontFamily, 11, FontStyle.Bold),
            Location = new Point(20, 15),
            Size = new Size(500, 25),
            AccessibleName = "Dialog Title"
        };

        _lblInstitution = new Label
        {
            Text = "Institution &Name (Friendly Label):",
            Location = new Point(20, 48),
            Size = new Size(500, 18),
            AccessibleName = "Institution Name Label"
        };
        _txtInstitution = new TextBox
        {
            Text = "My Bank",
            Location = new Point(20, 68),
            Size = new Size(500, 24),
            TabIndex = 0,
            AccessibleName = "Institution Name TextBox"
        };

        _lblBankCode = new Label
        {
            Text = "&Bank Code / BLZ (8 digits):",
            Location = new Point(20, 100),
            Size = new Size(500, 18),
            AccessibleName = "Bank Code Label"
        };
        _txtBankCode = new TextBox
        {
            MaxLength = 8,
            Location = new Point(20, 120),
            Size = new Size(500, 24),
            TabIndex = 1,
            AccessibleName = "Bank Code TextBox"
        };

        _lblEndpoint = new Label
        {
            Text = "FinTS HTTPS &Server URL (e.g. https://fints.bank.de/fints):",
            Location = new Point(20, 152),
            Size = new Size(500, 18),
            AccessibleName = "FinTS Server URL Label"
        };
        _txtEndpoint = new TextBox
        {
            Text = "https://",
            Location = new Point(20, 172),
            Size = new Size(500, 24),
            TabIndex = 2,
            AccessibleName = "FinTS Server URL TextBox"
        };

        _lblUserId = new Label
        {
            Text = "&User ID / Benutzerkennung / Legitimations-ID:",
            Location = new Point(20, 204),
            Size = new Size(500, 18),
            AccessibleName = "User ID Label"
        };
        _txtUserId = new TextBox
        {
            Location = new Point(20, 224),
            Size = new Size(500, 24),
            TabIndex = 3,
            AccessibleName = "User ID TextBox"
        };

        _lblCustomerId = new Label
        {
            Text = "&Customer ID / Kunden-ID (Optional, leave blank if same as User ID):",
            Location = new Point(20, 256),
            Size = new Size(500, 18),
            AccessibleName = "Customer ID Label"
        };
        _txtCustomerId = new TextBox
        {
            Location = new Point(20, 276),
            Size = new Size(500, 24),
            TabIndex = 4,
            AccessibleName = "Customer ID TextBox"
        };

        _lblNotice = new Label
        {
            Text = "No bank directory is bundled. You must enter the exact server address provided by your bank. A separate confirmation dialog will verify the hostname before connection.",
            ForeColor = Color.DarkSlateGray,
            Location = new Point(20, 310),
            Size = new Size(500, 40),
            AccessibleName = "Privacy and Security Policy Notice"
        };

        _btnConfirmEndpoint = new Button
        {
            Text = "&Review & Confirm...",
            DialogResult = DialogResult.None,
            Location = new Point(250, 365),
            Size = new Size(160, 34),
            TabIndex = 5,
            AccessibleName = "Review and Confirm Endpoint Button"
        };
        _btnConfirmEndpoint.Click += (s, e) => ReviewAndConfirm();

        _btnCancel = new Button
        {
            Text = "&Cancel",
            DialogResult = DialogResult.Cancel,
            Location = new Point(420, 365),
            Size = new Size(100, 34),
            TabIndex = 6,
            AccessibleName = "Cancel Button"
        };

        AcceptButton = _btnConfirmEndpoint;
        CancelButton = _btnCancel;

        Controls.AddRange(new Control[]
        {
            _lblTitle, _lblInstitution, _txtInstitution,
            _lblBankCode, _txtBankCode, _lblEndpoint, _txtEndpoint,
            _lblUserId, _txtUserId, _lblCustomerId, _txtCustomerId,
            _lblNotice, _btnConfirmEndpoint, _btnCancel
        });
    }

    private void ReviewAndConfirm()
    {
        string instName = _txtInstitution.Text.Trim();
        string blz = _txtBankCode.Text.Trim();
        string url = _txtEndpoint.Text.Trim();
        string user = _txtUserId.Text.Trim();
        string? cust = string.IsNullOrWhiteSpace(_txtCustomerId.Text) ? null : _txtCustomerId.Text.Trim();

        if (string.IsNullOrWhiteSpace(instName))
        {
            MessageBox.Show(this, "Please enter an institution name.", "Validation Error", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        if (blz.Length != 8 || !blz.All(char.IsDigit))
        {
            MessageBox.Show(this, "Bank code (BLZ) must be exactly 8 digits.", "Validation Error", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        if (string.IsNullOrWhiteSpace(user))
        {
            MessageBox.Show(this, "Please enter a User ID.", "Validation Error", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        FinTsEndpoint endpoint;
        try
        {
            endpoint = FinTsEndpoint.Create(url);
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, $"Invalid FinTS Endpoint URL: {ex.Message}", "URL Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
            return;
        }

        // Open independent review dialog
        using var reviewDialog = new EndpointConfirmationDialog(endpoint, instName);
        if (reviewDialog.ShowDialog(this) == DialogResult.OK && reviewDialog.IsConfirmed)
        {
            InstitutionName = instName;
            BankCode = blz;
            UserId = user;
            CustomerId = cust;
            ConfirmedEndpoint = endpoint.WithConfirmation(true);

            DialogResult = DialogResult.OK;
            Close();
        }
    }
}
