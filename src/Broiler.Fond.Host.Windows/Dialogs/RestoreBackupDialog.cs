using System.Drawing;
using System.Security.Cryptography;
using System.Windows.Forms;
using Broiler.Fond.Host.Windows.Models;
using Broiler.Fond.Kernel.Storage;

namespace Broiler.Fond.Host.Windows.Dialogs;

/// <summary>
/// Accessible dialog for restoring and unlocking an encrypted profile backup archive (.bffond / .fondbackup).
/// Provides cryptographic preflight verification and atomic directory restoration.
/// </summary>
public sealed class RestoreBackupDialog : Form
{
    private readonly HostSession _session;
    private readonly TextBox _txtBackupFilePath;
    private readonly TextBox _txtTargetDirectory;
    private readonly TextBox _txtPassphrase;
    private readonly Label _lblStatus;
    private readonly Button _btnRestore;
    private readonly Button _btnCancel;

    public RestoreBackupDialog(HostSession session, string? initialBackupPath = null)
    {
        _session = session ?? throw new ArgumentNullException(nameof(session));

        Text = "Restore Profile from Encrypted Backup";
        ClientSize = new Size(580, 310);
        FormBorderStyle = FormBorderStyle.FixedDialog;
        StartPosition = FormStartPosition.CenterParent;
        MaximizeBox = false;
        MinimizeBox = false;
        ShowInTaskbar = false;
        AutoScaleMode = AutoScaleMode.Dpi;

        AccessibleName = "Restore Profile from Encrypted Backup Dialog";
        AccessibleRole = AccessibleRole.Dialog;
        AccessibleDescription = "Choose an encrypted backup file, target directory, and profile passphrase to restore.";

        var lblIntro = new Label
        {
            Text = "Select an encrypted profile backup archive (.bffond) and enter the profile passphrase:",
            Location = new Point(15, 15),
            Size = new Size(550, 24),
            Font = new Font(Font.FontFamily, 9, FontStyle.Bold),
            AccessibleName = "Restore Backup Introduction Label"
        };

        // Backup file selection
        var lblBackup = new Label
        {
            Text = "Backup File:",
            Location = new Point(15, 50),
            Size = new Size(130, 20),
            AccessibleName = "Backup File Path Label"
        };
        _txtBackupFilePath = new TextBox
        {
            Location = new Point(15, 72),
            Size = new Size(440, 24),
            Text = initialBackupPath ?? string.Empty,
            TabIndex = 0,
            AccessibleName = "Backup File Path Input"
        };
        var btnBrowseBackup = new Button
        {
            Text = "&Browse...",
            Location = new Point(465, 70),
            Size = new Size(95, 28),
            TabIndex = 1,
            AccessibleName = "Browse Backup Archive File Button"
        };
        btnBrowseBackup.Click += (s, e) => BrowseBackupFile();

        // Target directory selection
        var lblTarget = new Label
        {
            Text = "Target Profile Directory:",
            Location = new Point(15, 108),
            Size = new Size(180, 20),
            AccessibleName = "Target Profile Directory Label"
        };
        string defaultTargetDir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "BroilerFond",
            "RestoredProfile");
        _txtTargetDirectory = new TextBox
        {
            Location = new Point(15, 130),
            Size = new Size(440, 24),
            Text = defaultTargetDir,
            TabIndex = 2,
            AccessibleName = "Target Profile Directory Input"
        };
        var btnBrowseTarget = new Button
        {
            Text = "B&rowse...",
            Location = new Point(465, 128),
            Size = new Size(95, 28),
            TabIndex = 3,
            AccessibleName = "Browse Target Directory Button"
        };
        btnBrowseTarget.Click += (s, e) => BrowseTargetDirectory();

        // Passphrase entry
        var lblPassphrase = new Label
        {
            Text = "Profile Passphrase:",
            Location = new Point(15, 168),
            Size = new Size(180, 20),
            AccessibleName = "Profile Passphrase Label"
        };
        _txtPassphrase = new TextBox
        {
            Location = new Point(15, 190),
            Size = new Size(350, 24),
            UseSystemPasswordChar = true,
            TabIndex = 4,
            AccessibleName = "Profile Passphrase Input"
        };

        // Status label with dual visual indicators
        _lblStatus = new Label
        {
            Text = "[✓ Ready to verify backup]",
            Location = new Point(15, 226),
            Size = new Size(550, 22),
            ForeColor = Color.DarkSlateGray,
            Font = new Font(Font.FontFamily, 8.5f),
            AccessibleName = "Restore Verification Status Label"
        };

        // Bottom action buttons
        _btnRestore = new Button
        {
            Text = "&Restore and Unlock",
            Location = new Point(310, 260),
            Size = new Size(150, 32),
            TabIndex = 5,
            AccessibleName = "Restore and Unlock Profile Button",
            AccessibleRole = AccessibleRole.PushButton
        };
        _btnRestore.Click += (s, e) => ExecuteRestore();

        _btnCancel = new Button
        {
            Text = "&Cancel",
            DialogResult = DialogResult.Cancel,
            Location = new Point(470, 260),
            Size = new Size(95, 32),
            TabIndex = 6,
            AccessibleName = "Cancel Restore Dialog Button",
            AccessibleRole = AccessibleRole.PushButton
        };

        Controls.AddRange(new Control[]
        {
            lblIntro,
            lblBackup, _txtBackupFilePath, btnBrowseBackup,
            lblTarget, _txtTargetDirectory, btnBrowseTarget,
            lblPassphrase, _txtPassphrase,
            _lblStatus,
            _btnRestore, _btnCancel
        });

        AcceptButton = _btnRestore;
        CancelButton = _btnCancel;
    }

    private void BrowseBackupFile()
    {
        using var ofd = new OpenFileDialog
        {
            Title = "Select Encrypted Profile Backup Archive",
            Filter = "Profile Backup (*.bffond;*.fondbackup)|*.bffond;*.fondbackup|All Files (*.*)|*.*",
            CheckFileExists = true
        };

        if (ofd.ShowDialog(this) == DialogResult.OK)
        {
            _txtBackupFilePath.Text = ofd.FileName;
        }
    }

    private void BrowseTargetDirectory()
    {
        using var fbd = new FolderBrowserDialog
        {
            Description = "Select Destination Directory for Restored Profile",
            UseDescriptionForTitle = true
        };

        if (fbd.ShowDialog(this) == DialogResult.OK)
        {
            _txtTargetDirectory.Text = fbd.SelectedPath;
        }
    }

    private void ExecuteRestore()
    {
        string backupPath = _txtBackupFilePath.Text.Trim();
        string targetDir = _txtTargetDirectory.Text.Trim();
        string passphrase = _txtPassphrase.Text;

        if (string.IsNullOrWhiteSpace(backupPath) || !File.Exists(backupPath))
        {
            _lblStatus.Text = "[⚠ Error: Backup file not found]";
            _lblStatus.ForeColor = Color.DarkRed;
            MessageBox.Show(this, "Please select an existing profile backup file.", "File Not Found", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        if (string.IsNullOrWhiteSpace(targetDir))
        {
            _lblStatus.Text = "[⚠ Error: Target directory required]";
            _lblStatus.ForeColor = Color.DarkRed;
            MessageBox.Show(this, "Please specify a target directory for the restored profile.", "Target Required", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        if (string.IsNullOrEmpty(passphrase))
        {
            _lblStatus.Text = "[⚠ Error: Passphrase required]";
            _lblStatus.ForeColor = Color.DarkRed;
            MessageBox.Show(this, "Please enter the passphrase that was used to encrypt this backup.", "Passphrase Required", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        try
        {
            _lblStatus.Text = "[Verifying cryptographic envelope and frames...]";
            _lblStatus.ForeColor = Color.Navy;
            Application.DoEvents();

            _session.RestoreFromBackup(backupPath, targetDir, passphrase);

            _lblStatus.Text = "[✓ Profile successfully restored and unlocked]";
            _lblStatus.ForeColor = Color.DarkGreen;
            MessageBox.Show(
                this,
                $"Profile successfully restored and unlocked from backup archive.\n\nRestored Location: {targetDir}",
                "Profile Restored",
                MessageBoxButtons.OK,
                MessageBoxIcon.Information);

            DialogResult = DialogResult.OK;
            Close();
        }
        catch (CryptographicException)
        {
            _lblStatus.Text = "[⚠ Authentication Failed: Incorrect Passphrase]";
            _lblStatus.ForeColor = Color.DarkRed;
            MessageBox.Show(this, "The specified passphrase could not decrypt the backup archive. Please check the passphrase and try again.", "Decryption Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
        catch (Exception ex)
        {
            _lblStatus.Text = $"[⚠ Error: {ex.Message}]";
            _lblStatus.ForeColor = Color.DarkRed;
            MessageBox.Show(this, $"Failed to restore profile backup: {ex.Message}", "Restore Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }
}
