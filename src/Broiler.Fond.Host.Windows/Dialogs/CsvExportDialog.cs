using System.Drawing;
using System.Windows.Forms;
using Broiler.Fond.Host.Windows.Models;
using Broiler.Fond.Kernel.Domain.Transactions.Query;

namespace Broiler.Fond.Host.Windows.Dialogs;

/// <summary>
/// Accessible, privacy-conscious dialog for previewing and exporting filtered transactions to CSV.
/// Provides keyboard navigation, screen-reader labels, live row previews, and zero-telemetry disclosure.
/// </summary>
public sealed class CsvExportDialog : Form
{
    private readonly HostSession _session;
    private readonly TransactionQueryFilter? _filter;
    private readonly TextBox _txtDestination;
    private readonly Button _btnBrowse;
    private readonly Button _btnExport;
    private readonly Button _btnCancel;
    private readonly TextBox _txtPreview;
    private readonly Label _lblSummary;

    public string DestinationPath => _txtDestination.Text.Trim();

    public CsvExportDialog(HostSession session, TransactionQueryFilter? filter = null)
    {
        _session = session ?? throw new ArgumentNullException(nameof(session));
        _filter = filter;

        Text = "Export Transactions to CSV";
        ClientSize = new Size(680, 500);
        MinimumSize = new Size(540, 400);
        StartPosition = FormStartPosition.CenterParent;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        ShowInTaskbar = false;
        AutoScaleMode = AutoScaleMode.Dpi;

        AccessibleRole = AccessibleRole.Dialog;
        AccessibleName = "Export Transactions to CSV Dialog";
        AccessibleDescription = "Preview and export filtered transactions to an RFC 4180 CSV file on local storage.";

        (string previewText, int totalCount) = _session.GenerateCsvPreview(_filter, maxRows: 8);

        var lblHeader = new Label
        {
            Location = new Point(20, 15),
            Size = new Size(640, 24),
            Font = new Font(Font.FontFamily, 11, FontStyle.Bold),
            Text = "Privacy-Conscious Transaction CSV Export",
            AccessibleName = "Export Dialog Header"
        };

        _lblSummary = new Label
        {
            Location = new Point(20, 42),
            Size = new Size(640, 20),
            Text = $"Matching transactions to export: {totalCount} record(s).",
            Font = new Font(Font.FontFamily, 9, FontStyle.Bold),
            AccessibleName = "Matching Records Summary"
        };

        var lblPreviewTitle = new Label
        {
            Location = new Point(20, 70),
            Size = new Size(640, 18),
            Text = "Preview (First 8 rows in RFC 4180 format):",
            AccessibleName = "CSV Preview Title"
        };

        _txtPreview = new TextBox
        {
            Location = new Point(20, 90),
            Size = new Size(640, 230),
            Multiline = true,
            ReadOnly = true,
            ScrollBars = ScrollBars.Both,
            WordWrap = false,
            Font = new Font("Consolas", 8.5f),
            BackColor = Color.White,
            Text = previewText,
            TabIndex = 0,
            AccessibleName = "CSV Preview Content",
            AccessibleRole = AccessibleRole.Text
        };

        var grpDestination = new GroupBox
        {
            Location = new Point(20, 330),
            Size = new Size(640, 75),
            Text = "Export Destination (Local Storage Only)",
            TabIndex = 1,
            AccessibleName = "Export Destination Group"
        };

        _txtDestination = new TextBox
        {
            Location = new Point(15, 30),
            Size = new Size(495, 26),
            ReadOnly = false,
            TabIndex = 0,
            AccessibleName = "Destination File Path TextBox"
        };
        _txtDestination.TextChanged += (s, e) => UpdateExportButtonState();

        _btnBrowse = new Button
        {
            Location = new Point(520, 28),
            Size = new Size(105, 30),
            Text = "&Browse...",
            TabIndex = 1,
            AccessibleName = "Browse Destination File Button"
        };
        _btnBrowse.Click += (s, e) => BrowseDestination();

        grpDestination.Controls.AddRange(new Control[] { _txtDestination, _btnBrowse });

        var lblPrivacyNotice = new Label
        {
            Location = new Point(20, 415),
            Size = new Size(400, 35),
            ForeColor = Color.DarkSlateGray,
            Font = new Font(Font.FontFamily, 8),
            Text = "Privacy Guarantee: Transactions are written directly to your local device. Broiler Fond operates zero telemetry, tracking, or background reporting."
        };

        _btnExport = new Button
        {
            Location = new Point(440, 415),
            Size = new Size(110, 34),
            Text = "&Export",
            Enabled = false,
            DialogResult = DialogResult.OK,
            TabIndex = 2,
            AccessibleName = "Confirm Export Button"
        };
        _btnExport.Click += (s, e) => ExecuteExport();

        _btnCancel = new Button
        {
            Location = new Point(560, 415),
            Size = new Size(100, 34),
            Text = "Cancel",
            DialogResult = DialogResult.Cancel,
            TabIndex = 3,
            AccessibleName = "Cancel Export Button"
        };

        AcceptButton = _btnExport;
        CancelButton = _btnCancel;

        Controls.AddRange(new Control[]
        {
            lblHeader, _lblSummary, lblPreviewTitle, _txtPreview,
            grpDestination, lblPrivacyNotice, _btnExport, _btnCancel
        });
    }

    private void BrowseDestination()
    {
        using SaveFileDialog sfd = new()
        {
            Title = "Select Destination for Transactions CSV",
            Filter = "CSV Files (*.csv)|*.csv|All Files (*.*)|*.*",
            DefaultExt = "csv",
            FileName = $"transactions-export-{DateTime.UtcNow:yyyyMMdd-HHmmss}.csv"
        };

        if (sfd.ShowDialog(this) == DialogResult.OK)
        {
            _txtDestination.Text = sfd.FileName;
        }
    }

    private void UpdateExportButtonState()
    {
        _btnExport.Enabled = !string.IsNullOrWhiteSpace(_txtDestination.Text);
    }

    private void ExecuteExport()
    {
        string dest = DestinationPath;
        if (string.IsNullOrWhiteSpace(dest))
        {
            return;
        }

        try
        {
            int count = _session.ExportTransactionsToCsv(_filter, dest);
            MessageBox.Show(
                this,
                $"Successfully exported {count} transaction(s) to:\n{dest}",
                "Export Complete",
                MessageBoxButtons.OK,
                MessageBoxIcon.Information);

            DialogResult = DialogResult.OK;
            Close();
        }
        catch (Exception ex)
        {
            MessageBox.Show(
                this,
                $"Failed to export transactions:\n{ex.Message}",
                "Export Failed",
                MessageBoxButtons.OK,
                MessageBoxIcon.Error);
        }
    }
}
