using System.Drawing;
using System.Windows.Forms;
using Broiler.Fond.Host.Windows.Models;

namespace Broiler.Fond.Host.Windows.Dialogs;

/// <summary>
/// Accessible connection removal and cached-data purge dialog fulfilling M1-08 requirements.
/// </summary>
public sealed class ConnectionRemovalDialog : Form
{
    private readonly Label _lblTitle;
    private readonly Label _lblTarget;
    private readonly RadioButton _rbRemoveConfigOnly;
    private readonly RadioButton _rbRemoveAndPurge;
    private readonly Label _lblPassphrasePrompt;
    private readonly TextBox _txtPassphrase;
    private readonly Label _lblPurgeWarning;
    private readonly Button _btnConfirm;
    private readonly Button _btnCancel;

    public bool PurgeCachedData => _rbRemoveAndPurge.Checked;
    public string Passphrase => _txtPassphrase.Text;

    public ConnectionRemovalDialog(ConnectionViewModel connection)
    {
        ArgumentNullException.ThrowIfNull(connection);

        Text = "Disconnect Institution - Broiler Fond";
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        StartPosition = FormStartPosition.CenterParent;
        ClientSize = new Size(540, 360);
        AutoScaleMode = AutoScaleMode.Dpi;

        AccessibleName = "Disconnect Institution and Purge Dialog";
        AccessibleRole = AccessibleRole.Dialog;
        AccessibleDescription = "Choose whether to remove configuration only or also purge cached financial data.";

        _lblTitle = new Label
        {
            Text = "Disconnect Financial Institution",
            Font = new Font(Font.FontFamily, 11, FontStyle.Bold),
            Location = new Point(20, 15),
            Size = new Size(500, 25),
            AccessibleName = "Dialog Title"
        };

        _lblTarget = new Label
        {
            Text = $"Target Connection: {connection.InstitutionName} (BLZ: {connection.BankCode}, User: {connection.UserId})",
            Font = new Font(Font.FontFamily, 9.5f),
            Location = new Point(20, 45),
            Size = new Size(500, 22),
            AccessibleName = "Target Connection Details"
        };

        _rbRemoveConfigOnly = new RadioButton
        {
            Text = "&Remove connection configuration only (retains historical balance records)",
            Checked = true,
            Location = new Point(20, 80),
            Size = new Size(500, 28),
            TabIndex = 0,
            AccessibleName = "Remove Configuration Only Radio Button"
        };
        _rbRemoveConfigOnly.CheckedChanged += (s, e) => UpdatePurgeState();

        _rbRemoveAndPurge = new RadioButton
        {
            Text = "Remove connection and &purge cached financial data (irreversible privacy purge)",
            ForeColor = Color.DarkRed,
            Font = new Font(Font.FontFamily, 9, FontStyle.Bold),
            Location = new Point(20, 115),
            Size = new Size(500, 28),
            TabIndex = 1,
            AccessibleName = "Remove and Purge Cached Data Radio Button"
        };
        _rbRemoveAndPurge.CheckedChanged += (s, e) => UpdatePurgeState();

        _lblPurgeWarning = new Label
        {
            Text = "CAUTION: Privacy purge permanently deletes all prior generations referencing this connection from the encrypted store archive on disk. This action cannot be undone.",
            ForeColor = Color.DarkRed,
            Location = new Point(38, 148),
            Size = new Size(480, 40),
            Visible = false,
            AccessibleName = "Purge Caution Warning"
        };

        _lblPassphrasePrompt = new Label
        {
            Text = "Enter Profile &Passphrase to Authorize Purge:",
            Location = new Point(38, 195),
            Size = new Size(480, 20),
            Visible = false,
            AccessibleName = "Profile Passphrase Prompt"
        };

        _txtPassphrase = new TextBox
        {
            UseSystemPasswordChar = true,
            Location = new Point(38, 218),
            Size = new Size(480, 24),
            TabIndex = 2,
            Visible = false,
            AccessibleName = "Purge Passphrase Input Box"
        };

        _btnConfirm = new Button
        {
            Text = "&Disconnect",
            DialogResult = DialogResult.OK,
            Location = new Point(290, 295),
            Size = new Size(120, 34),
            TabIndex = 3,
            AccessibleName = "Confirm Disconnect Button"
        };
        _btnConfirm.Click += (s, e) => Submit();

        _btnCancel = new Button
        {
            Text = "&Cancel",
            DialogResult = DialogResult.Cancel,
            Location = new Point(420, 295),
            Size = new Size(100, 34),
            TabIndex = 4,
            AccessibleName = "Cancel Button"
        };

        AcceptButton = _btnConfirm;
        CancelButton = _btnCancel;

        Controls.AddRange(new Control[]
        {
            _lblTitle, _lblTarget, _rbRemoveConfigOnly, _rbRemoveAndPurge,
            _lblPurgeWarning, _lblPassphrasePrompt, _txtPassphrase,
            _btnConfirm, _btnCancel
        });
    }

    private void UpdatePurgeState()
    {
        bool purge = _rbRemoveAndPurge.Checked;
        _lblPurgeWarning.Visible = purge;
        _lblPassphrasePrompt.Visible = purge;
        _txtPassphrase.Visible = purge;
        _btnConfirm.Text = purge ? "&Purge & Delete" : "&Disconnect";
    }

    private void Submit()
    {
        if (_rbRemoveAndPurge.Checked && string.IsNullOrEmpty(_txtPassphrase.Text))
        {
            MessageBox.Show(this, "Profile passphrase is required to authorize privacy purge.", "Passphrase Required", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        DialogResult = DialogResult.OK;
        Close();
    }
}
