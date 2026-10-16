using System.Drawing;
using System.Text;
using System.Windows.Forms;
using Broiler.Fond.Kernel.Diagnostics;

namespace Broiler.Fond.Host.Windows.Dialogs;

/// <summary>
/// Accessible support preview and export dialog fulfilling M1-08 review and export requirements:
/// Displays SHA-256 preview digest and fully redacted diagnostic payload with explicit user-initiated local save.
/// </summary>
public sealed class SupportExportDialog : Form
{
    private readonly Label _lblTitle;
    private readonly Label _lblSha256;
    private readonly TextBox _txtSha256;
    private readonly Label _lblNotice;
    private readonly TextBox _txtPreview;
    private readonly Button _btnSave;
    private readonly Button _btnClose;
    private readonly DiagnosticSupportPreview _preview;

    public SupportExportDialog(DiagnosticSupportPreview preview)
    {
        ArgumentNullException.ThrowIfNull(preview);
        _preview = preview;

        Text = "Redacted Diagnostic Support Export - Review";
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        StartPosition = FormStartPosition.CenterParent;
        ClientSize = new Size(650, 520);
        AutoScaleMode = AutoScaleMode.Dpi;

        AccessibleName = "Diagnostic Support Export Review Dialog";
        AccessibleRole = AccessibleRole.Dialog;
        AccessibleDescription = "Review the SHA-256 digest and redacted diagnostic data before saving to a file.";

        _lblTitle = new Label
        {
            Text = "Review Support Diagnostics Before Export",
            Font = new Font(Font.FontFamily, 11, FontStyle.Bold),
            Location = new Point(20, 15),
            Size = new Size(610, 24),
            AccessibleName = "Dialog Title"
        };

        _lblNotice = new Label
        {
            Text = "NO TELEMETRY GUARANTEE: Broiler Fond has no telemetry, tracking, or automated reporting server. This diagnostic export is generated on your device and will ONLY be written to a local file that you choose. All PINs, TANs, account numbers, and session secrets are completely redacted.",
            ForeColor = Color.DarkSlateGray,
            Location = new Point(20, 45),
            Size = new Size(610, 48),
            AccessibleName = "Zero Telemetry Guarantee Notice"
        };

        _lblSha256 = new Label
        {
            Text = "Preview SHA-256 Digest (binds exact previewed bytes):",
            Font = new Font(Font.FontFamily, 9, FontStyle.Bold),
            Location = new Point(20, 100),
            Size = new Size(610, 20),
            AccessibleName = "SHA-256 Digest Label"
        };

        _txtSha256 = new TextBox
        {
            Text = preview.PreviewSha256,
            ReadOnly = true,
            Font = new Font("Consolas", 9),
            Location = new Point(20, 122),
            Size = new Size(610, 24),
            TabIndex = 0,
            AccessibleName = "SHA-256 Digest Value TextBox"
        };

        _txtPreview = new TextBox
        {
            Text = preview.Json,
            Multiline = true,
            ReadOnly = true,
            ScrollBars = ScrollBars.Both,
            Font = new Font("Consolas", 8.5f),
            BackColor = SystemColors.Window,
            Location = new Point(20, 160),
            Size = new Size(610, 280),
            TabIndex = 1,
            AccessibleName = "Redacted Diagnostic Content Read-Only Preview"
        };

        _btnSave = new Button
        {
            Text = "&Save Export to File...",
            Location = new Point(330, 460),
            Size = new Size(180, 36),
            TabIndex = 2,
            AccessibleName = "Save Export to Local File Button"
        };
        _btnSave.Click += (s, e) => SaveToFile();

        _btnClose = new Button
        {
            Text = "&Close",
            DialogResult = DialogResult.OK,
            Location = new Point(520, 460),
            Size = new Size(110, 36),
            TabIndex = 3,
            AccessibleName = "Close Dialog Button"
        };
        _btnClose.Click += (s, e) => Close();

        CancelButton = _btnClose;

        Controls.AddRange(new Control[]
        {
            _lblTitle, _lblNotice, _lblSha256, _txtSha256, _txtPreview, _btnSave, _btnClose
        });
    }

    private void SaveToFile()
    {
        using var sfd = new SaveFileDialog
        {
            Title = "Save Redacted Support Diagnostic File",
            Filter = "Log File (*.log)|*.log|Text File (*.txt)|*.txt|All Files (*.*)|*.*",
            FileName = $"broiler-fond-support-{DateTime.UtcNow:yyyyMMdd-HHmmss}.log"
        };

        if (sfd.ShowDialog(this) == DialogResult.OK)
        {
            try
            {
                EncryptedLocalDiagnosticStore.ExportReviewedSupportPreview(_preview, _preview.PreviewSha256, sfd.FileName);
                MessageBox.Show(this, $"Diagnostic export saved successfully to:\n{sfd.FileName}", "Export Complete", MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, $"Failed to save export: {ex.Message}", "Save Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }
    }
}
