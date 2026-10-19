using System.Drawing;
using System.Globalization;
using System.Windows.Forms;
using Broiler.Fond.Host.Windows.Models;
using Broiler.Fond.Kernel.Domain.Payments;

namespace Broiler.Fond.Host.Windows.Dialogs;

/// <summary>
/// Accessible review and confirmation dialog for requesting a SEPA direct-debit return (Lastschriftrückgabe)
/// under statutory 8-week unconditional or 13-month unauthorized return rules.
/// </summary>
public sealed class DirectDebitReturnDialog : Form
{
    private readonly HostSession _session;
    private readonly DirectDebitReturnViewModel _debitItem;
    private readonly RadioButton _radReason8Weeks;
    private readonly RadioButton _radReason13Months;
    private readonly CheckBox _chkWarningAck;
    private readonly Button _btnSubmit;
    private readonly Button _btnCancel;

    public string SelectedReasonCode => _radReason13Months.Checked ? "MD01" : "MS02";
    public string SelectedReasonDescription => _radReason13Months.Checked
        ? "Unauthorized direct debit (No valid SEPA mandate)"
        : "Unconditional customer return within statutory 8-week window";

    public DirectDebitReturnDialog(HostSession session, DirectDebitReturnViewModel debitItem)
    {
        _session = session ?? throw new ArgumentNullException(nameof(session));
        _debitItem = debitItem ?? throw new ArgumentNullException(nameof(debitItem));

        Text = "SEPA Direct-Debit Return Request (Lastschrift widersprechen)";
        ClientSize = new Size(580, 530);
        MinimumSize = new Size(520, 480);
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        StartPosition = FormStartPosition.CenterParent;
        AutoScaleMode = AutoScaleMode.Dpi;

        AccessibleRole = AccessibleRole.Dialog;
        AccessibleName = "Direct-Debit Return Request Dialog";
        AccessibleDescription = "Review direct-debit details and statutory deadlines before requesting a reversal.";

        int y = 15;

        // Group 1: Transaction & Mandate Details
        var grpDetails = new GroupBox
        {
            Text = "Direct-Debit Transaction Details",
            Location = new Point(15, y),
            Size = new Size(550, 160),
            Font = new Font(Font, FontStyle.Bold)
        };

        int gy = 22;
        void AddRow(string label, string value, bool isBold = false, Color? color = null)
        {
            var lblL = new Label
            {
                Text = label,
                Location = new Point(15, gy),
                Size = new Size(130, 20),
                Font = new Font(Font.FontFamily, 9, FontStyle.Regular)
            };
            var lblV = new Label
            {
                Text = value,
                Location = new Point(150, gy),
                Size = new Size(385, 20),
                Font = new Font(Font.FontFamily, 9, isBold ? FontStyle.Bold : FontStyle.Regular),
                ForeColor = color ?? SystemColors.ControlText
            };
            grpDetails.Controls.Add(lblL);
            grpDetails.Controls.Add(lblV);
            gy += 24;
        }

        AddRow("Creditor / Payee:", debitItem.CreditorName, isBold: true);
        AddRow("Creditor IBAN:", debitItem.CreditorIban);
        AddRow("Creditor ID:", debitItem.CreditorId ?? "[Not supplied]");
        AddRow("Mandate Reference:", debitItem.MandateId ?? "[Not supplied]");
        AddRow("Amount:", $"{debitItem.AmountDisplay}", isBold: true, color: Color.DarkRed);
        AddRow("Booking Date:", $"{debitItem.BookingDateDisplay} ({debitItem.DaysSinceBooking} days ago)");

        Controls.Add(grpDetails);
        y += 175;

        // Group 2: Reason & Statutory Deadlines
        var grpReason = new GroupBox
        {
            Text = "Statutory Return Basis & Reason",
            Location = new Point(15, y),
            Size = new Size(550, 130),
            Font = new Font(Font, FontStyle.Bold)
        };

        int daysRemaining8Weeks = Math.Max(0, 56 - debitItem.DaysSinceBooking);
        int daysRemaining13Months = Math.Max(0, 396 - debitItem.DaysSinceBooking);

        _radReason8Weeks = new RadioButton
        {
            Text = $"&Unconditional Return (8-week statutory window, {daysRemaining8Weeks} days remaining)",
            Location = new Point(15, 25),
            Size = new Size(520, 24),
            Font = new Font(Font.FontFamily, 9, FontStyle.Regular),
            Enabled = debitItem.IsWithin8Weeks,
            Checked = debitItem.IsWithin8Weeks,
            TabIndex = 0
        };
        _radReason8Weeks.CheckedChanged += (s, e) => UpdateButtons();

        var lblReason8Sub = new Label
        {
            Text = debitItem.IsWithin8Weeks
                ? "[✓ Eligible: 8 weeks / 56 days from booking without giving reasons (MS02/MS03)]"
                : "[✗ Expired: Booking exceeds statutory 8-week unconditional return deadline]",
            Location = new Point(35, 50),
            Size = new Size(500, 18),
            Font = new Font(Font.FontFamily, 8, FontStyle.Regular),
            ForeColor = debitItem.IsWithin8Weeks ? Color.ForestGreen : Color.DarkRed
        };

        _radReason13Months = new RadioButton
        {
            Text = $"&Unauthorized Debit Return (13-month window, {daysRemaining13Months} days remaining)",
            Location = new Point(15, 72),
            Size = new Size(520, 24),
            Font = new Font(Font.FontFamily, 9, FontStyle.Regular),
            Enabled = debitItem.IsWithin13Months,
            Checked = !debitItem.IsWithin8Weeks && debitItem.IsWithin13Months,
            TabIndex = 1
        };
        _radReason13Months.CheckedChanged += (s, e) => UpdateButtons();

        var lblReason13Sub = new Label
        {
            Text = debitItem.IsWithin13Months
                ? "[13 months / 396 days: applicable only if no valid SEPA mandate existed (MD01/MD06)]"
                : "[✗ Expired: Booking exceeds 13-month statutory unauthorized return deadline]",
            Location = new Point(35, 96),
            Size = new Size(500, 18),
            Font = new Font(Font.FontFamily, 8, FontStyle.Regular),
            ForeColor = debitItem.IsWithin13Months ? Color.DarkSlateGray : Color.DarkRed
        };

        grpReason.Controls.Add(_radReason8Weeks);
        grpReason.Controls.Add(lblReason8Sub);
        grpReason.Controls.Add(_radReason13Months);
        grpReason.Controls.Add(lblReason13Sub);

        Controls.Add(grpReason);
        y += 145;

        // Group 3: Warning & Acknowledgment
        var grpWarning = new GroupBox
        {
            Text = "Notice & Legal Warnings",
            Location = new Point(15, y),
            Size = new Size(550, 115),
            Font = new Font(Font, FontStyle.Bold)
        };

        var lblWarningText = new Label
        {
            Location = new Point(15, 20),
            Size = new Size(520, 48),
            Font = new Font(Font.FontFamily, 8, FontStyle.Regular),
            ForeColor = Color.DarkRed,
            Text = "WARNING: Requesting a direct-debit return instructs your bank to reverse this posting. " +
                   "This operation cannot be undone. Your bank will evaluate the mandate status. " +
                   "Do not use this function if you authorized the debit and have no contractual grounds for reversal."
        };
        grpWarning.Controls.Add(lblWarningText);

        _chkWarningAck = new CheckBox
        {
            Text = "I confirm that I have reviewed the return grounds and wish to instruct the bank to return this debit.",
            Location = new Point(15, 72),
            Size = new Size(520, 32),
            Font = new Font(Font.FontFamily, 9, FontStyle.Bold),
            ForeColor = Color.DarkSlateGray,
            TabIndex = 2,
            AccessibleName = "Acknowledge Direct-Debit Return Warning Checkbox"
        };
        _chkWarningAck.CheckedChanged += (s, e) => UpdateButtons();
        grpWarning.Controls.Add(_chkWarningAck);

        Controls.Add(grpWarning);
        y += 130;

        // Buttons
        _btnSubmit = new Button
        {
            Text = "&Submit Return Request...",
            Location = new Point(220, y),
            Size = new Size(230, 34),
            DialogResult = DialogResult.OK,
            Enabled = false,
            TabIndex = 3,
            Font = new Font(Font, FontStyle.Bold),
            AccessibleName = "Submit Direct-Debit Return Button"
        };
        _btnSubmit.Click += OnSubmitClicked;

        _btnCancel = new Button
        {
            Text = "&Cancel",
            Location = new Point(460, y),
            Size = new Size(105, 34),
            DialogResult = DialogResult.Cancel,
            TabIndex = 4,
            AccessibleName = "Cancel Button"
        };

        AcceptButton = _btnSubmit;
        CancelButton = _btnCancel;
        Controls.Add(_btnSubmit);
        Controls.Add(_btnCancel);

        UpdateButtons();
    }

    private void UpdateButtons()
    {
        bool hasValidReason = (_radReason8Weeks.Checked && _debitItem.IsWithin8Weeks)
            || (_radReason13Months.Checked && _debitItem.IsWithin13Months);

        _btnSubmit.Enabled = hasValidReason && _chkWarningAck.Checked && !_session.IsSafeModeActive;
    }

    private void OnSubmitClicked(object? sender, EventArgs e)
    {
        if (_session.IsSafeModeActive)
        {
            MessageBox.Show(
                "Safe mode is currently active. Direct-debit return writes are blocked.",
                "Safe Mode Active",
                MessageBoxButtons.OK,
                MessageBoxIcon.Warning);
            DialogResult = DialogResult.None;
            return;
        }

        _session.RecordDirectDebitReturn(_debitItem.TransactionId, SelectedReasonCode, SelectedReasonDescription);
    }
}
