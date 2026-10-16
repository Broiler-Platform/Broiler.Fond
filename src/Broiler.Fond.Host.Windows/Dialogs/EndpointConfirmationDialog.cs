using System.Drawing;
using System.Windows.Forms;
using Broiler.Fond.Host.Windows.Models;

namespace Broiler.Fond.Host.Windows.Dialogs;

/// <summary>
/// Independent endpoint verification dialog fulfilling M1-06:
/// Mandates explicit confirmation of the exact HTTPS hostname and discloses phishing / incorrect endpoint risk.
/// </summary>
public sealed class EndpointConfirmationDialog : Form
{
    private readonly Label _lblTitle;
    private readonly Label _lblWarning;
    private readonly GroupBox _grpDetails;
    private readonly Label _lblScheme;
    private readonly Label _lblHost;
    private readonly Label _lblPort;
    private readonly Label _lblPath;
    private readonly Label _lblInstitution;
    private readonly CheckBox _chkVerified;
    private readonly Button _btnConfirm;
    private readonly Button _btnCancel;

    public bool IsConfirmed { get; private set; }

    public EndpointConfirmationDialog(FinTsEndpoint endpoint, string institutionName)
    {
        ArgumentNullException.ThrowIfNull(endpoint);

        Text = "Confirm Bank Endpoint Hostname - Security Review";
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        StartPosition = FormStartPosition.CenterParent;
        ClientSize = new Size(580, 420);
        AutoScaleMode = AutoScaleMode.Dpi;

        AccessibleName = "Endpoint Confirmation Security Review Dialog";
        AccessibleRole = AccessibleRole.Dialog;
        AccessibleDescription = "Security dialog requiring independent confirmation of the exact bank endpoint host.";

        _lblTitle = new Label
        {
            Text = "Review Financial Institution Endpoint",
            Font = new Font(Font.FontFamily, 12, FontStyle.Bold),
            Location = new Point(20, 15),
            Size = new Size(540, 28),
            AccessibleName = "Title",
            AccessibleRole = AccessibleRole.StaticText
        };

        _lblWarning = new Label
        {
            Text = "IMPORTANT: The institution name below is a local label entered by you. All banking communications, credentials, and financial messages will be transmitted directly to the host specified below. Verify that the hostname matches the official FinTS server published by your bank.",
            ForeColor = Color.DarkRed,
            Location = new Point(20, 50),
            Size = new Size(540, 60),
            AccessibleName = "Security Warning Notice",
            AccessibleRole = AccessibleRole.Alert
        };

        _grpDetails = new GroupBox
        {
            Text = "Endpoint Destination Details",
            Location = new Point(20, 120),
            Size = new Size(540, 190),
            TabIndex = 0,
            AccessibleName = "Endpoint Destination Details Group",
            AccessibleRole = AccessibleRole.Grouping
        };

        _lblInstitution = new Label
        {
            Text = $"Configured Name: {institutionName}",
            Location = new Point(15, 25),
            Size = new Size(510, 22),
            AccessibleName = "Configured Institution Name",
            AccessibleRole = AccessibleRole.StaticText
        };

        _lblScheme = new Label
        {
            Text = $"Security Protocol: {endpoint.Uri.Scheme.ToUpperInvariant()} (TLS Encrypted)",
            Location = new Point(15, 52),
            Size = new Size(510, 22),
            AccessibleName = "Protocol Scheme",
            AccessibleRole = AccessibleRole.StaticText
        };

        _lblHost = new Label
        {
            Text = $"Exact Hostname: {endpoint.IdnHost}",
            Font = new Font(Font.FontFamily, 10, FontStyle.Bold),
            ForeColor = Color.DarkBlue,
            Location = new Point(15, 80),
            Size = new Size(510, 24),
            AccessibleName = "Target Hostname",
            AccessibleRole = AccessibleRole.StaticText
        };

        _lblPort = new Label
        {
            Text = $"Port: {endpoint.Uri.Port} {(endpoint.Uri.IsDefaultPort ? "(Default HTTPS)" : "(Custom)")}",
            Location = new Point(15, 110),
            Size = new Size(510, 22),
            AccessibleName = "Target Port",
            AccessibleRole = AccessibleRole.StaticText
        };

        _lblPath = new Label
        {
            Text = $"Service Path: {endpoint.Uri.AbsolutePath}",
            Location = new Point(15, 138),
            Size = new Size(510, 22),
            AccessibleName = "Service Path",
            AccessibleRole = AccessibleRole.StaticText
        };

        _grpDetails.Controls.AddRange(new Control[]
        {
            _lblInstitution, _lblScheme, _lblHost, _lblPort, _lblPath
        });

        _chkVerified = new CheckBox
        {
            Text = "&I have independently verified this exact hostname against official bank documentation.",
            Location = new Point(20, 325),
            Size = new Size(540, 26),
            TabIndex = 1,
            AccessibleName = "Verification Confirmation Checkbox",
            AccessibleRole = AccessibleRole.CheckButton,
            AccessibleDescription = "Check to confirm you have verified the hostname independently."
        };

        _btnConfirm = new Button
        {
            Text = "&Confirm Hostname",
            DialogResult = DialogResult.OK,
            Location = new Point(280, 370),
            Size = new Size(160, 32),
            Enabled = false,
            TabIndex = 2,
            AccessibleName = "Confirm Hostname Button",
            AccessibleRole = AccessibleRole.PushButton
        };
        _chkVerified.CheckedChanged += (s, e) => _btnConfirm.Enabled = _chkVerified.Checked;
        _btnConfirm.Click += (s, e) =>
        {
            IsConfirmed = true;
            Close();
        };

        _btnCancel = new Button
        {
            Text = "&Cancel",
            DialogResult = DialogResult.Cancel,
            Location = new Point(450, 370),
            Size = new Size(110, 32),
            TabIndex = 3,
            AccessibleName = "Cancel Button",
            AccessibleRole = AccessibleRole.PushButton
        };
        _btnCancel.Click += (s, e) =>
        {
            IsConfirmed = false;
            Close();
        };

        AcceptButton = _btnConfirm;
        CancelButton = _btnCancel;

        Controls.AddRange(new Control[]
        {
            _lblTitle, _lblWarning, _grpDetails, _chkVerified, _btnConfirm, _btnCancel
        });
    }
}
