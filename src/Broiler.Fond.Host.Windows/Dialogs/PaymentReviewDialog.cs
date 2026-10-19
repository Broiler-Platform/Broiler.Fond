using System.Drawing;
using System.Globalization;
using System.Windows.Forms;
using Broiler.Fond.Kernel.Domain.Payments;
using Broiler.Fond.Kernel.Storage;

namespace Broiler.Fond.Host.Windows.Dialogs;

/// <summary>
/// Accessible review and payee verification dialog displaying exact payment details,
/// Verification of Payee (VoP) outcomes per Regulation (EU) 2024/886, and explicit warning acknowledgment.
/// </summary>
public sealed class PaymentReviewDialog : Form
{
    private readonly CheckBox _chkWarningAck;
    private readonly Button _btnAuthorize;
    private readonly Button _btnCancel;

    public bool WasWarningAcknowledged => _chkWarningAck.Checked;
    public bool IsAuthorizeEnabled => _btnAuthorize.Enabled;

    public PaymentReviewDialog(
        string sourceAccountName,
        string beneficiaryName,
        string beneficiaryIban,
        string? beneficiaryBic,
        decimal amount,
        string currency,
        string? remittance,
        DateOnly executionDate,
        StorePaymentVoPResultEntry? vopResult)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sourceAccountName);
        ArgumentException.ThrowIfNullOrWhiteSpace(beneficiaryName);
        ArgumentException.ThrowIfNullOrWhiteSpace(beneficiaryIban);

        Text = "Review SEPA Payment & Payee Verification";
        ClientSize = new Size(580, 520);
        MinimumSize = new Size(520, 480);
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        StartPosition = FormStartPosition.CenterParent;
        AutoScaleMode = AutoScaleMode.Dpi;

        AccessibleRole = AccessibleRole.Dialog;
        AccessibleName = "Payment Review and Payee Verification Dialog";
        AccessibleDescription = "Review exact SEPA transfer parameters and payee verification result before bank authorization.";

        int y = 20;

        // Group 1: Transfer Details
        var grpTransfer = new GroupBox
        {
            Text = "Payment Order Details",
            Location = new Point(15, y),
            Size = new Size(550, 220),
            Font = new Font(Font, FontStyle.Bold)
        };

        int gy = 25;
        void AddRow(string label, string value, bool isBold = false, Color? color = null)
        {
            var lblL = new Label
            {
                Text = label,
                Location = new Point(15, gy),
                Size = new Size(140, 20),
                Font = new Font(Font.FontFamily, 9, FontStyle.Regular)
            };
            var lblV = new Label
            {
                Text = value,
                Location = new Point(160, gy),
                Size = new Size(370, 20),
                Font = new Font(Font.FontFamily, 9, isBold ? FontStyle.Bold : FontStyle.Regular),
                ForeColor = color ?? SystemColors.ControlText
            };
            grpTransfer.Controls.Add(lblL);
            grpTransfer.Controls.Add(lblV);
            gy += 26;
        }

        AddRow("Debit Account:", sourceAccountName, isBold: true);
        AddRow("Recipient Name:", beneficiaryName, isBold: true);
        AddRow("Recipient IBAN:", beneficiaryIban, isBold: true);
        if (!string.IsNullOrWhiteSpace(beneficiaryBic))
        {
            AddRow("Recipient BIC:", beneficiaryBic);
        }
        AddRow("Amount:", $"{amount.ToString("N2", CultureInfo.CurrentCulture)} {currency}", isBold: true, color: Color.Navy);
        AddRow("Remittance:", string.IsNullOrWhiteSpace(remittance) ? "[None]" : remittance);
        AddRow("Execution Date:", executionDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));

        Controls.Add(grpTransfer);

        // Group 2: Verification of Payee (VoP)
        y += 235;
        var grpVoP = new GroupBox
        {
            Text = "Verification of Payee (VoP) — Regulation (EU) 2024/886",
            Location = new Point(15, y),
            Size = new Size(550, 180),
            Font = new Font(Font, FontStyle.Bold)
        };

        var lblVoPStatus = new Label
        {
            Location = new Point(15, 25),
            Size = new Size(520, 24),
            Font = new Font(Font.FontFamily, 10, FontStyle.Bold)
        };

        var lblVoPDetails = new Label
        {
            Location = new Point(15, 52),
            Size = new Size(520, 48),
            Font = new Font(Font.FontFamily, 9, FontStyle.Regular)
        };

        bool requiresAcknowledgment = false;
        string status = vopResult?.Status ?? nameof(VerificationOfPayeeStatus.NotApplicable);

        if (string.Equals(status, nameof(VerificationOfPayeeStatus.Match), StringComparison.OrdinalIgnoreCase))
        {
            lblVoPStatus.Text = "[✓ Recipient Match]";
            lblVoPStatus.ForeColor = Color.ForestGreen;
            lblVoPDetails.Text = $"The beneficiary name '{beneficiaryName}' matches the bank account records exactly.";
        }
        else if (string.Equals(status, nameof(VerificationOfPayeeStatus.CloseMatch), StringComparison.OrdinalIgnoreCase))
        {
            lblVoPStatus.Text = "[~ Close Match Detected]";
            lblVoPStatus.ForeColor = Color.DarkGoldenrod;
            string suggested = vopResult?.MatchedName ?? "different name";
            lblVoPDetails.Text = $"The bank reports a close match. Official account holder name: '{suggested}'.\nReason: {vopResult?.WarningMessage ?? "Minor spelling discrepancy."}";
            requiresAcknowledgment = true;
        }
        else if (string.Equals(status, nameof(VerificationOfPayeeStatus.NoMatch), StringComparison.OrdinalIgnoreCase))
        {
            lblVoPStatus.Text = "[⚠ Mismatch Warning]";
            lblVoPStatus.ForeColor = Color.DarkRed;
            lblVoPDetails.Text = $"WARNING: The bank reports that '{beneficiaryName}' does NOT match the account holder for this IBAN.\nFunds transferred may not reach your intended recipient.";
            requiresAcknowledgment = true;
        }
        else if (string.Equals(status, nameof(VerificationOfPayeeStatus.Unavailable), StringComparison.OrdinalIgnoreCase))
        {
            lblVoPStatus.Text = "[? Verification Unavailable]";
            lblVoPStatus.ForeColor = Color.DarkOrange;
            lblVoPDetails.Text = "The recipient bank's verification service is temporarily unavailable.\nStatutory consumer warning applies: verify details carefully before authorization.";
            requiresAcknowledgment = true;
        }
        else
        {
            lblVoPStatus.Text = "[- Payee Check Not Applicable]";
            lblVoPStatus.ForeColor = Color.DarkSlateGray;
            lblVoPDetails.Text = "Verification of payee is not applicable for this transfer category or source account.";
        }

        grpVoP.Controls.Add(lblVoPStatus);
        grpVoP.Controls.Add(lblVoPDetails);

        _chkWarningAck = new CheckBox
        {
            Text = "I have reviewed this payee warning and confirm I wish to proceed with the transfer.",
            Location = new Point(15, 105),
            Size = new Size(520, 30),
            Font = new Font(Font.FontFamily, 9, FontStyle.Bold),
            ForeColor = Color.DarkRed,
            Visible = requiresAcknowledgment,
            TabIndex = 0,
            AccessibleName = "Acknowledge Payee Verification Warning Checkbox"
        };
        _chkWarningAck.CheckedChanged += (s, e) => UpdateButtons();
        grpVoP.Controls.Add(_chkWarningAck);

        Controls.Add(grpVoP);

        // Buttons
        y += 195;
        _btnAuthorize = new Button
        {
            Text = "&Authorize Payment...",
            Location = new Point(230, y),
            Size = new Size(220, 34),
            DialogResult = DialogResult.OK,
            Enabled = !requiresAcknowledgment,
            TabIndex = 1,
            Font = new Font(Font, FontStyle.Bold),
            AccessibleName = "Authorize Payment Button"
        };

        _btnCancel = new Button
        {
            Text = "&Cancel",
            Location = new Point(460, y),
            Size = new Size(105, 34),
            DialogResult = DialogResult.Cancel,
            TabIndex = 2,
            AccessibleName = "Cancel Button"
        };

        AcceptButton = _btnAuthorize;
        CancelButton = _btnCancel;
        Controls.Add(_btnAuthorize);
        Controls.Add(_btnCancel);
    }

    private void UpdateButtons()
    {
        _btnAuthorize.Enabled = !_chkWarningAck.Visible || _chkWarningAck.Checked;
    }
}
