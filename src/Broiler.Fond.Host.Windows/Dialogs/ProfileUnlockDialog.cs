using System.Drawing;
using System.Windows.Forms;
using Broiler.Fond.Host.Windows.Models;

namespace Broiler.Fond.Host.Windows.Dialogs;

/// <summary>
/// Accessible profile unlock and initialization dialog fulfilling M1-06 application lock requirements.
/// </summary>
public sealed class ProfileUnlockDialog : Form
{
    private readonly RadioButton _rbOpenExisting;
    private readonly RadioButton _rbCreateNew;
    private readonly Label _lblDirectory;
    private readonly TextBox _txtDirectory;
    private readonly Button _btnBrowse;
    private readonly Label _lblPassphrase;
    private readonly TextBox _txtPassphrase;
    private readonly Label _lblConfirmPassphrase;
    private readonly TextBox _txtConfirmPassphrase;
    private readonly Label _lblProfileName;
    private readonly TextBox _txtProfileName;
    private readonly Label _lblError;
    private readonly Button _btnSubmit;
    private readonly Button _btnCancel;
    private readonly HostSession _session;

    public ProfileUnlockDialog(HostSession session)
    {
        _session = session ?? throw new ArgumentNullException(nameof(session));

        Text = "Unlock Profile - Broiler Fond";
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        StartPosition = FormStartPosition.CenterParent;
        ClientSize = new Size(540, 420);
        AutoScaleMode = AutoScaleMode.Dpi;

        AccessibleName = "Unlock or Create Profile Dialog";
        AccessibleRole = AccessibleRole.Dialog;
        AccessibleDescription = "Enter your profile storage folder and passphrase to unlock your financial data.";

        _rbOpenExisting = new RadioButton
        {
            Text = "&Open Existing Profile",
            Checked = true,
            Location = new Point(25, 20),
            Size = new Size(200, 24),
            TabIndex = 0,
            AccessibleName = "Open Existing Profile Radio Button"
        };
        _rbOpenExisting.CheckedChanged += (s, e) => UpdateMode();

        _rbCreateNew = new RadioButton
        {
            Text = "&Create New Profile",
            Location = new Point(240, 20),
            Size = new Size(200, 24),
            TabIndex = 1,
            AccessibleName = "Create New Profile Radio Button"
        };
        _rbCreateNew.CheckedChanged += (s, e) => UpdateMode();

        _lblDirectory = new Label
        {
            Text = "Profile &Folder Path:",
            Location = new Point(25, 60),
            Size = new Size(490, 20),
            AccessibleName = "Profile Folder Label"
        };

        string defaultPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Broiler.Fond", "Profiles", "default");

        _txtDirectory = new TextBox
        {
            Text = defaultPath,
            Location = new Point(25, 82),
            Size = new Size(380, 26),
            TabIndex = 2,
            AccessibleName = "Profile Folder Path TextBox"
        };

        _btnBrowse = new Button
        {
            Text = "&Browse...",
            Location = new Point(415, 80),
            Size = new Size(100, 30),
            TabIndex = 3,
            AccessibleName = "Browse Folder Button"
        };
        _btnBrowse.Click += (s, e) =>
        {
            using var fbd = new FolderBrowserDialog
            {
                Description = "Select Profile Storage Directory",
                SelectedPath = Directory.Exists(_txtDirectory.Text) ? _txtDirectory.Text : Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData)
            };
            if (fbd.ShowDialog(this) == DialogResult.OK)
            {
                _txtDirectory.Text = fbd.SelectedPath;
            }
        };

        _lblPassphrase = new Label
        {
            Text = "Profile &Passphrase:",
            Location = new Point(25, 125),
            Size = new Size(490, 20),
            AccessibleName = "Profile Passphrase Label"
        };

        _txtPassphrase = new TextBox
        {
            UseSystemPasswordChar = true,
            Location = new Point(25, 147),
            Size = new Size(490, 26),
            TabIndex = 4,
            AccessibleName = "Profile Passphrase TextBox"
        };

        _lblConfirmPassphrase = new Label
        {
            Text = "Confirm Pass&phrase:",
            Location = new Point(25, 185),
            Size = new Size(490, 20),
            Visible = false,
            AccessibleName = "Confirm Passphrase Label"
        };

        _txtConfirmPassphrase = new TextBox
        {
            UseSystemPasswordChar = true,
            Location = new Point(25, 207),
            Size = new Size(490, 26),
            TabIndex = 5,
            Visible = false,
            AccessibleName = "Confirm Passphrase TextBox"
        };

        _lblProfileName = new Label
        {
            Text = "Profile &Label (Optional):",
            Location = new Point(25, 245),
            Size = new Size(490, 20),
            Visible = false,
            AccessibleName = "Profile Label"
        };

        _txtProfileName = new TextBox
        {
            Text = "Personal Banking",
            Location = new Point(25, 267),
            Size = new Size(490, 26),
            TabIndex = 6,
            Visible = false,
            AccessibleName = "Profile Label TextBox"
        };

        _lblError = new Label
        {
            ForeColor = Color.Red,
            Location = new Point(25, 305),
            Size = new Size(490, 45),
            AccessibleName = "Error Message Display",
            AccessibleRole = AccessibleRole.Alert
        };

        _btnSubmit = new Button
        {
            Text = "&Unlock Profile",
            DialogResult = DialogResult.None,
            Location = new Point(255, 360),
            Size = new Size(150, 34),
            TabIndex = 7,
            AccessibleName = "Unlock or Create Submit Button"
        };
        _btnSubmit.Click += (s, e) => Submit();

        _btnCancel = new Button
        {
            Text = "&Cancel",
            DialogResult = DialogResult.Cancel,
            Location = new Point(415, 360),
            Size = new Size(100, 34),
            TabIndex = 8,
            AccessibleName = "Cancel Button"
        };

        AcceptButton = _btnSubmit;
        CancelButton = _btnCancel;

        Controls.AddRange(new Control[]
        {
            _rbOpenExisting, _rbCreateNew,
            _lblDirectory, _txtDirectory, _btnBrowse,
            _lblPassphrase, _txtPassphrase,
            _lblConfirmPassphrase, _txtConfirmPassphrase,
            _lblProfileName, _txtProfileName,
            _lblError, _btnSubmit, _btnCancel
        });
    }

    private void UpdateMode()
    {
        bool isCreate = _rbCreateNew.Checked;
        _lblConfirmPassphrase.Visible = isCreate;
        _txtConfirmPassphrase.Visible = isCreate;
        _lblProfileName.Visible = isCreate;
        _txtProfileName.Visible = isCreate;
        _btnSubmit.Text = isCreate ? "&Create Profile" : "&Unlock Profile";
        _lblError.Text = string.Empty;
    }

    private void Submit()
    {
        _lblError.Text = string.Empty;

        string directory = _txtDirectory.Text.Trim();
        string passphrase = _txtPassphrase.Text;

        if (string.IsNullOrWhiteSpace(directory))
        {
            _lblError.Text = "Please specify a profile storage folder path.";
            return;
        }

        if (string.IsNullOrEmpty(passphrase))
        {
            _lblError.Text = "Please enter a profile passphrase.";
            return;
        }

        try
        {
            if (_rbCreateNew.Checked)
            {
                if (passphrase != _txtConfirmPassphrase.Text)
                {
                    _lblError.Text = "Passphrases do not match. Please re-enter.";
                    return;
                }

                _session.CreateNewProfile(directory, passphrase, _txtProfileName.Text.Trim(), null);
            }
            else
            {
                _session.Unlock(directory, passphrase);
            }

            DialogResult = DialogResult.OK;
            Close();
        }
        catch (Exception ex)
        {
            _lblError.Text = $"Error: {ex.Message}";
        }
    }
}
