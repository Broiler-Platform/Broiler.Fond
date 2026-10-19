using System.Drawing;
using System.Globalization;
using System.Windows.Forms;
using Broiler.Fond.Host.Windows.Models;
using Broiler.Fond.Kernel.Domain.Payments;

namespace Broiler.Fond.Host.Windows.Dialogs;

/// <summary>
/// Accessible payment initiation dialog providing real-time SEPA IBAN validation,
/// exact decimal EUR money input, structured/unstructured remittance, duplicate detection,
/// beneficiary template selection & saving, instant SEPA credit transfers, and scheduled date bounds.
/// </summary>
public sealed class PaymentInitiationDialog : Form
{
    private readonly HostSession _session;
    private readonly ComboBox _cmbSourceAccount;
    private readonly ComboBox _cmbTemplates;
    private readonly TextBox _txtBeneficiaryName;
    private readonly TextBox _txtBeneficiaryIban;
    private readonly Label _lblIbanFeedback;
    private readonly TextBox _txtBeneficiaryBic;
    private readonly CheckBox _chkSaveTemplate;
    private readonly TextBox _txtTemplateLabel;
    private readonly TextBox _txtAmount;
    private readonly Label _lblAmountFeedback;
    private readonly RadioButton _radRemittanceUnstructured;
    private readonly RadioButton _radRemittanceCreditorRef;
    private readonly TextBox _txtRemittance;
    private readonly RadioButton _radModeStandard;
    private readonly RadioButton _radModeInstant;
    private readonly Label _lblInstantFeedback;
    private readonly DateTimePicker _dtpExecutionDate;
    private readonly Label _lblDateFeedback;
    private readonly Label _lblDuplicateWarning;
    private readonly Button _btnContinue;
    private readonly Button _btnCancel;

    public ulong SelectedAccountId { get; private set; }
    public string BeneficiaryName { get; private set; } = string.Empty;
    public string BeneficiaryIban { get; private set; } = string.Empty;
    public string? BeneficiaryBic { get; private set; }
    public decimal Amount { get; private set; }
    public string Currency => "EUR";
    public string? RemittanceUnstructured { get; private set; }
    public string? RemittanceCreditorReference { get; private set; }
    public string ExecutionMode => _radModeInstant.Checked ? "InstantCreditTransfer" : "StandardCreditTransfer";
    public DateOnly ExecutionDate { get; private set; }
    public bool SaveAsTemplate => _chkSaveTemplate.Checked;
    public string TemplateLabel => _txtTemplateLabel.Text.Trim();

    public PaymentInitiationDialog(HostSession session, ulong? preselectedAccountId = null)
    {
        _session = session ?? throw new ArgumentNullException(nameof(session));

        Text = "New SEPA Credit Transfer (Überweisung)";
        ClientSize = new Size(620, 680);
        MinimumSize = new Size(580, 620);
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        StartPosition = FormStartPosition.CenterParent;
        AutoScaleMode = AutoScaleMode.Dpi;

        AccessibleRole = AccessibleRole.Dialog;
        AccessibleName = "Payment Initiation Dialog";
        AccessibleDescription = "Initiate a single or instant SEPA EUR credit transfer with real-time validation and template support.";

        int labelX = 20;
        int inputX = 190;
        int inputWidth = 390;
        int y = 15;

        // 1. Source Account
        var lblSource = new Label
        {
            Text = "Debit Account:",
            Location = new Point(labelX, y + 4),
            Size = new Size(160, 20),
            Font = new Font(Font, FontStyle.Bold)
        };
        _cmbSourceAccount = new ComboBox
        {
            Location = new Point(inputX, y),
            Size = new Size(inputWidth, 26),
            DropDownStyle = ComboBoxStyle.DropDownList,
            TabIndex = 0,
            AccessibleName = "Debit Source Account Selector"
        };
        _cmbSourceAccount.SelectedIndexChanged += (s, e) =>
        {
            UpdateInstantCapabilityUI();
            ValidateAllInputs();
        };
        Controls.Add(lblSource);
        Controls.Add(_cmbSourceAccount);

        // 2. Beneficiary Templates Picker
        y += 34;
        var lblTemplate = new Label
        {
            Text = "Saved Recipient / Vorlage:",
            Location = new Point(labelX, y + 4),
            Size = new Size(160, 20)
        };
        _cmbTemplates = new ComboBox
        {
            Location = new Point(inputX, y),
            Size = new Size(inputWidth, 26),
            DropDownStyle = ComboBoxStyle.DropDownList,
            TabIndex = 1,
            AccessibleName = "Saved Beneficiary Template Selector"
        };
        _cmbTemplates.SelectedIndexChanged += OnTemplateSelected;
        Controls.Add(lblTemplate);
        Controls.Add(_cmbTemplates);

        // 3. Beneficiary Name
        y += 34;
        var lblName = new Label
        {
            Text = "Recipient Name:",
            Location = new Point(labelX, y + 4),
            Size = new Size(160, 20),
            Font = new Font(Font, FontStyle.Bold)
        };
        _txtBeneficiaryName = new TextBox
        {
            Location = new Point(inputX, y),
            Size = new Size(inputWidth, 24),
            MaxLength = SepaPaymentDraftValidator.MaxBeneficiaryNameLength,
            TabIndex = 2,
            AccessibleName = "Recipient Name Input"
        };
        _txtBeneficiaryName.TextChanged += (s, e) => ValidateAllInputs();
        Controls.Add(lblName);
        Controls.Add(_txtBeneficiaryName);

        // 4. Beneficiary IBAN
        y += 30;
        var lblIban = new Label
        {
            Text = "Recipient IBAN:",
            Location = new Point(labelX, y + 4),
            Size = new Size(160, 20),
            Font = new Font(Font, FontStyle.Bold)
        };
        _txtBeneficiaryIban = new TextBox
        {
            Location = new Point(inputX, y),
            Size = new Size(inputWidth, 24),
            MaxLength = 34,
            TabIndex = 3,
            AccessibleName = "Recipient IBAN Input"
        };
        _txtBeneficiaryIban.TextChanged += (s, e) => ValidateAllInputs();
        Controls.Add(lblIban);
        Controls.Add(_txtBeneficiaryIban);

        y += 26;
        _lblIbanFeedback = new Label
        {
            Location = new Point(inputX, y),
            Size = new Size(inputWidth, 18),
            Font = new Font(Font, FontStyle.Bold),
            ForeColor = Color.DarkGray,
            Text = "[Enter IBAN]"
        };
        Controls.Add(_lblIbanFeedback);

        // 5. Beneficiary BIC (optional)
        y += 24;
        var lblBic = new Label
        {
            Text = "Recipient BIC (optional):",
            Location = new Point(labelX, y + 4),
            Size = new Size(160, 20)
        };
        _txtBeneficiaryBic = new TextBox
        {
            Location = new Point(inputX, y),
            Size = new Size(inputWidth, 24),
            MaxLength = 11,
            TabIndex = 4,
            AccessibleName = "Recipient BIC Input"
        };
        _txtBeneficiaryBic.TextChanged += (s, e) => ValidateAllInputs();
        Controls.Add(lblBic);
        Controls.Add(_txtBeneficiaryBic);

        // 6. Save as Template Checkbox
        y += 28;
        _chkSaveTemplate = new CheckBox
        {
            Text = "Save recipient to address book as:",
            Location = new Point(inputX, y),
            Size = new Size(210, 22),
            TabIndex = 5,
            AccessibleName = "Save Recipient to Address Book Checkbox"
        };
        _txtTemplateLabel = new TextBox
        {
            Location = new Point(inputX + 215, y),
            Size = new Size(inputWidth - 215, 24),
            MaxLength = BeneficiaryTemplate.MaxLabelLength,
            Enabled = false,
            TabIndex = 6,
            AccessibleName = "Template Label Input"
        };
        _txtTemplateLabel.TextChanged += (s, e) => ValidateAllInputs();
        _chkSaveTemplate.CheckedChanged += (s, e) =>
        {
            _txtTemplateLabel.Enabled = _chkSaveTemplate.Checked;
            if (_chkSaveTemplate.Checked && string.IsNullOrWhiteSpace(_txtTemplateLabel.Text))
            {
                _txtTemplateLabel.Text = _txtBeneficiaryName.Text.Trim();
            }
            ValidateAllInputs();
        };
        Controls.Add(_chkSaveTemplate);
        Controls.Add(_txtTemplateLabel);

        // 7. Amount in EUR
        y += 32;
        var lblAmount = new Label
        {
            Text = "Amount (EUR):",
            Location = new Point(labelX, y + 4),
            Size = new Size(160, 20),
            Font = new Font(Font, FontStyle.Bold)
        };
        _txtAmount = new TextBox
        {
            Location = new Point(inputX, y),
            Size = new Size(160, 24),
            TabIndex = 7,
            AccessibleName = "Payment Amount EUR Input"
        };
        _txtAmount.TextChanged += (s, e) => ValidateAllInputs();
        var lblEur = new Label
        {
            Text = "EUR (Exact decimal)",
            Location = new Point(inputX + 170, y + 4),
            Size = new Size(130, 20),
            Font = new Font(Font, FontStyle.Italic)
        };
        Controls.Add(lblAmount);
        Controls.Add(_txtAmount);
        Controls.Add(lblEur);

        y += 26;
        _lblAmountFeedback = new Label
        {
            Location = new Point(inputX, y),
            Size = new Size(inputWidth, 18),
            Font = new Font(Font, FontStyle.Bold),
            ForeColor = Color.DarkGray,
            Text = "[Enter positive EUR amount]"
        };
        Controls.Add(_lblAmountFeedback);

        // 8. Remittance Information
        y += 24;
        var lblRemittance = new Label
        {
            Text = "Remittance:",
            Location = new Point(labelX, y + 4),
            Size = new Size(160, 20),
            Font = new Font(Font, FontStyle.Bold)
        };
        _radRemittanceUnstructured = new RadioButton
        {
            Text = "&Purpose (max 140)",
            Location = new Point(inputX, y),
            Size = new Size(150, 22),
            Checked = true,
            TabIndex = 8
        };
        _radRemittanceCreditorRef = new RadioButton
        {
            Text = "&Creditor Ref (RF)",
            Location = new Point(inputX + 160, y),
            Size = new Size(150, 22),
            TabIndex = 9
        };
        _radRemittanceUnstructured.CheckedChanged += (s, e) => ValidateAllInputs();
        _radRemittanceCreditorRef.CheckedChanged += (s, e) => ValidateAllInputs();
        Controls.Add(lblRemittance);
        Controls.Add(_radRemittanceUnstructured);
        Controls.Add(_radRemittanceCreditorRef);

        y += 24;
        _txtRemittance = new TextBox
        {
            Location = new Point(inputX, y),
            Size = new Size(inputWidth, 38),
            Multiline = true,
            MaxLength = SepaPaymentDraftValidator.MaxRemittanceLength,
            TabIndex = 10,
            AccessibleName = "Remittance Information Text"
        };
        _txtRemittance.TextChanged += (s, e) => ValidateAllInputs();
        Controls.Add(_txtRemittance);

        // 9. Execution Mode (Standard vs Instant)
        y += 46;
        var lblMode = new Label
        {
            Text = "Transfer Mode:",
            Location = new Point(labelX, y + 4),
            Size = new Size(160, 20),
            Font = new Font(Font, FontStyle.Bold)
        };
        _radModeStandard = new RadioButton
        {
            Text = "&Standard Transfer (1 business day)",
            Location = new Point(inputX, y),
            Size = new Size(230, 22),
            Checked = true,
            TabIndex = 11
        };
        _radModeInstant = new RadioButton
        {
            Text = "&SEPA Instant (<= 10 seconds)",
            Location = new Point(inputX + 235, y),
            Size = new Size(160, 22),
            TabIndex = 12
        };
        _radModeStandard.CheckedChanged += (s, e) => OnExecutionModeChanged();
        _radModeInstant.CheckedChanged += (s, e) => OnExecutionModeChanged();
        Controls.Add(lblMode);
        Controls.Add(_radModeStandard);
        Controls.Add(_radModeInstant);

        y += 24;
        _lblInstantFeedback = new Label
        {
            Location = new Point(inputX, y),
            Size = new Size(inputWidth, 18),
            Font = new Font(Font, FontStyle.Regular),
            ForeColor = Color.DarkSlateGray,
            Text = "Standard SEPA clearing cycle."
        };
        Controls.Add(_lblInstantFeedback);

        // 10. Execution Date
        y += 24;
        var lblDate = new Label
        {
            Text = "Execution Date:",
            Location = new Point(labelX, y + 4),
            Size = new Size(160, 20),
            Font = new Font(Font, FontStyle.Bold)
        };
        _dtpExecutionDate = new DateTimePicker
        {
            Location = new Point(inputX, y),
            Size = new Size(180, 24),
            Format = DateTimePickerFormat.Short,
            MinDate = DateTime.Today,
            TabIndex = 13,
            AccessibleName = "Requested Execution Date"
        };
        _dtpExecutionDate.ValueChanged += (s, e) => ValidateAllInputs();
        Controls.Add(lblDate);
        Controls.Add(_dtpExecutionDate);

        _lblDateFeedback = new Label
        {
            Location = new Point(inputX + 190, y + 4),
            Size = new Size(inputWidth - 190, 20),
            Font = new Font(Font, FontStyle.Regular),
            ForeColor = Color.DarkSlateGray,
            Text = "Execution today."
        };
        Controls.Add(_lblDateFeedback);

        // 11. Duplicate Warning Label
        y += 32;
        _lblDuplicateWarning = new Label
        {
            Location = new Point(labelX, y),
            Size = new Size(inputWidth + (inputX - labelX), 32),
            ForeColor = Color.DarkGoldenrod,
            Font = new Font(Font, FontStyle.Bold),
            Visible = false,
            Text = "⚠ Warning: A matching transfer to this recipient with the same amount exists recently."
        };
        Controls.Add(_lblDuplicateWarning);

        // 12. Buttons
        y += 38;
        _btnContinue = new Button
        {
            Text = "&Review & Payee Verification...",
            Location = new Point(230, y),
            Size = new Size(230, 34),
            DialogResult = DialogResult.OK,
            Enabled = false,
            TabIndex = 14,
            Font = new Font(Font, FontStyle.Bold),
            AccessibleName = "Continue to Payee Verification Button"
        };
        _btnContinue.Click += (s, e) => OnContinueClicked();

        _btnCancel = new Button
        {
            Text = "&Cancel",
            Location = new Point(470, y),
            Size = new Size(110, 34),
            DialogResult = DialogResult.Cancel,
            TabIndex = 15,
            AccessibleName = "Cancel Button"
        };

        AcceptButton = _btnContinue;
        CancelButton = _btnCancel;
        Controls.Add(_btnContinue);
        Controls.Add(_btnCancel);

        PopulateSourceAccounts(preselectedAccountId);
        PopulateTemplates();
        UpdateInstantCapabilityUI();
        ValidateAllInputs();
    }

    private void PopulateSourceAccounts(ulong? preselectedAccountId)
    {
        _cmbSourceAccount.Items.Clear();
        int selectedIndex = -1;

        foreach (var acc in _session.Accounts)
        {
            if (acc.IsSupportedPaymentAccount && acc.BindingId.HasValue)
            {
                int idx = _cmbSourceAccount.Items.Add(new SourceAccountItem(acc.AccountId, $"{acc.AccountName} ({acc.LocatorDisplay})"));
                if (preselectedAccountId.HasValue && acc.AccountId == preselectedAccountId.Value)
                {
                    selectedIndex = idx;
                }
            }
        }

        if (selectedIndex >= 0)
        {
            _cmbSourceAccount.SelectedIndex = selectedIndex;
        }
        else if (_cmbSourceAccount.Items.Count > 0)
        {
            _cmbSourceAccount.SelectedIndex = 0;
        }
    }

    private void PopulateTemplates()
    {
        _cmbTemplates.Items.Clear();
        _cmbTemplates.Items.Add(new TemplateComboItem(null, "-- Manual Entry / Keiner --"));

        foreach (var tmpl in _session.BeneficiaryTemplates)
        {
            _cmbTemplates.Items.Add(new TemplateComboItem(tmpl, tmpl.DisplayName));
        }

        _cmbTemplates.SelectedIndex = 0;
    }

    private void OnTemplateSelected(object? sender, EventArgs e)
    {
        if (_cmbTemplates.SelectedItem is TemplateComboItem item && item.Template != null)
        {
            var tmpl = item.Template;
            _txtBeneficiaryName.Text = tmpl.BeneficiaryName;
            _txtBeneficiaryIban.Text = tmpl.BeneficiaryIban;
            _txtBeneficiaryBic.Text = tmpl.BeneficiaryBic ?? string.Empty;

            if (!string.IsNullOrWhiteSpace(tmpl.DefaultRemittanceUnstructured))
            {
                _radRemittanceUnstructured.Checked = true;
                _txtRemittance.Text = tmpl.DefaultRemittanceUnstructured;
            }

            if (tmpl.DefaultAmount.HasValue)
            {
                _txtAmount.Text = tmpl.DefaultAmount.Value.ToString("F2", CultureInfo.InvariantCulture);
            }

            if (tmpl.DefaultExecutionMode == "InstantCreditTransfer" && _radModeInstant.Enabled)
            {
                _radModeInstant.Checked = true;
            }
            else
            {
                _radModeStandard.Checked = true;
            }

            _chkSaveTemplate.Checked = false;
        }

        ValidateAllInputs();
    }

    private void UpdateInstantCapabilityUI()
    {
        if (_cmbSourceAccount.SelectedItem is not SourceAccountItem selectedAcc)
        {
            _radModeInstant.Enabled = false;
            _radModeStandard.Checked = true;
            _lblInstantFeedback.Text = "Select debit account to verify instant transfer capability.";
            return;
        }

        InstantPaymentCapability? instantCap = _session.GetInstantPaymentCapability(selectedAcc.AccountId);
        if (instantCap != null && instantCap.IsSupported)
        {
            _radModeInstant.Enabled = true;
            if (_radModeInstant.Checked)
            {
                _lblInstantFeedback.Text = $"[⚡ Instant SCT Inst] Max limit: {instantCap.MaximumAmount:N2} EUR | Execution <= {instantCap.EstimatedExecutionSeconds}s | Fee: {instantCap.FeeNotice ?? "Included"}";
                _lblInstantFeedback.ForeColor = Color.Navy;
            }
            else
            {
                _lblInstantFeedback.Text = "Standard SEPA clearing cycle (1 business day).";
                _lblInstantFeedback.ForeColor = Color.DarkSlateGray;
            }
        }
        else
        {
            _radModeInstant.Enabled = false;
            _radModeStandard.Checked = true;
            _lblInstantFeedback.Text = "Instant SEPA (SCT Inst) is not advertised by the bank for this account.";
            _lblInstantFeedback.ForeColor = Color.DarkSlateGray;
        }
    }

    private void OnExecutionModeChanged()
    {
        if (_radModeInstant.Checked)
        {
            _dtpExecutionDate.Value = DateTime.Today;
            _dtpExecutionDate.Enabled = false;
            _lblDateFeedback.Text = "Instant transfers execute immediately (today).";
        }
        else
        {
            _dtpExecutionDate.Enabled = true;
            _lblDateFeedback.Text = _dtpExecutionDate.Value.Date == DateTime.Today ? "Execution today." : $"Scheduled for {_dtpExecutionDate.Value:yyyy-MM-dd}.";
        }

        UpdateInstantCapabilityUI();
        ValidateAllInputs();
    }

    private void ValidateAllInputs()
    {
        bool isValid = true;

        // 1. Source Account
        if (_cmbSourceAccount.SelectedItem is not SourceAccountItem selectedAcc)
        {
            isValid = false;
        }

        // 2. Beneficiary Name
        string name = _txtBeneficiaryName.Text.Trim();
        if (string.IsNullOrWhiteSpace(name))
        {
            isValid = false;
        }

        // 3. IBAN
        string ibanText = _txtBeneficiaryIban.Text.Trim().Replace(" ", string.Empty, StringComparison.Ordinal);
        if (string.IsNullOrWhiteSpace(ibanText))
        {
            _lblIbanFeedback.Text = "[Enter IBAN]";
            _lblIbanFeedback.ForeColor = Color.DarkGray;
            isValid = false;
        }
        else
        {
            if (SepaIbanValidator.Validate(ibanText, out _))
            {
                _lblIbanFeedback.Text = "[✓ Valid SEPA IBAN]";
                _lblIbanFeedback.ForeColor = Color.ForestGreen;
            }
            else
            {
                _lblIbanFeedback.Text = "[✗ Invalid IBAN: Checksum or length mismatch]";
                _lblIbanFeedback.ForeColor = Color.DarkRed;
                isValid = false;
            }
        }

        // 4. BIC (optional)
        string bic = _txtBeneficiaryBic.Text.Trim();
        if (!string.IsNullOrEmpty(bic) && (bic.Length is not (8 or 11) || !bic.All(char.IsLetterOrDigit)))
        {
            isValid = false;
        }

        // 5. Amount
        string amountText = _txtAmount.Text.Trim();
        decimal parsedAmount = 0m;
        if (string.IsNullOrWhiteSpace(amountText))
        {
            _lblAmountFeedback.Text = "[Enter positive EUR amount]";
            _lblAmountFeedback.ForeColor = Color.DarkGray;
            isValid = false;
        }
        else
        {
            bool parsed = decimal.TryParse(amountText, NumberStyles.Number, CultureInfo.CurrentCulture, out parsedAmount)
                || decimal.TryParse(amountText, NumberStyles.Number, CultureInfo.InvariantCulture, out parsedAmount);

            if (!parsed || parsedAmount <= 0.00m)
            {
                _lblAmountFeedback.Text = "[✗ Invalid amount: must be > 0.00 EUR]";
                _lblAmountFeedback.ForeColor = Color.DarkRed;
                isValid = false;
            }
            else if (decimal.Round(parsedAmount, 2) != parsedAmount)
            {
                _lblAmountFeedback.Text = "[✗ Invalid scale: maximum 2 decimal places]";
                _lblAmountFeedback.ForeColor = Color.DarkRed;
                isValid = false;
            }
            else
            {
                _lblAmountFeedback.Text = $"[✓ {parsedAmount:N2} EUR]";
                _lblAmountFeedback.ForeColor = Color.ForestGreen;
            }
        }

        // 6. Instant Transfer amount limit check
        if (_radModeInstant.Checked && _cmbSourceAccount.SelectedItem is SourceAccountItem instantAcc)
        {
            InstantPaymentCapability? cap = _session.GetInstantPaymentCapability(instantAcc.AccountId);
            if (cap != null && parsedAmount > cap.MaximumAmount)
            {
                _lblAmountFeedback.Text = $"[✗ Exceeds Instant Limit: max {cap.MaximumAmount:N2} EUR]";
                _lblAmountFeedback.ForeColor = Color.DarkRed;
                isValid = false;
            }
        }

        // 7. Scheduled payment date bounds
        if (!_radModeInstant.Checked && _dtpExecutionDate.Value.Date > DateTime.Today && _cmbSourceAccount.SelectedItem is SourceAccountItem schedAcc)
        {
            ScheduledPaymentCapability? schedCap = _session.GetScheduledPaymentCapability(schedAcc.AccountId);
            int advanceDays = (_dtpExecutionDate.Value.Date - DateTime.Today).Days;
            if (schedCap != null)
            {
                if (advanceDays > schedCap.MaximumLeadDays)
                {
                    _lblDateFeedback.Text = $"[✗ Date exceeds maximum {schedCap.MaximumLeadDays} advance days]";
                    _lblDateFeedback.ForeColor = Color.DarkRed;
                    isValid = false;
                }
                else
                {
                    _lblDateFeedback.Text = $"[✓ Scheduled in {advanceDays} days]";
                    _lblDateFeedback.ForeColor = Color.ForestGreen;
                }
            }
        }

        // 8. Remittance
        string remittance = _txtRemittance.Text.Trim();
        if (_radRemittanceCreditorRef.Checked)
        {
            if (!string.IsNullOrEmpty(remittance) && !remittance.StartsWith("RF", StringComparison.OrdinalIgnoreCase))
            {
                isValid = false;
            }
        }

        // 9. Template label validation if save checked
        if (_chkSaveTemplate.Checked && string.IsNullOrWhiteSpace(_txtTemplateLabel.Text))
        {
            isValid = false;
        }

        // 10. Duplicate Detection
        if (isValid && parsedAmount > 0m)
        {
            bool duplicateDetected = _session.PaymentDrafts.Any(d =>
                string.Equals(d.BeneficiaryIban.Replace(" ", string.Empty, StringComparison.Ordinal), ibanText, StringComparison.OrdinalIgnoreCase)
                && d.Amount == parsedAmount
                && (DateTimeOffset.UtcNow - d.CreatedAt).TotalHours < 24);

            _lblDuplicateWarning.Visible = duplicateDetected;
        }
        else
        {
            _lblDuplicateWarning.Visible = false;
        }

        _btnContinue.Enabled = isValid && !_session.IsSafeModeActive;
    }

    private void OnContinueClicked()
    {
        if (_cmbSourceAccount.SelectedItem is not SourceAccountItem selectedAcc)
        {
            return;
        }

        SelectedAccountId = selectedAcc.AccountId;
        BeneficiaryName = _txtBeneficiaryName.Text.Trim();
        BeneficiaryIban = _txtBeneficiaryIban.Text.Trim().Replace(" ", string.Empty, StringComparison.Ordinal).ToUpperInvariant();
        BeneficiaryBic = string.IsNullOrWhiteSpace(_txtBeneficiaryBic.Text) ? null : _txtBeneficiaryBic.Text.Trim().ToUpperInvariant();

        string amountStr = _txtAmount.Text.Trim();
        if (!decimal.TryParse(amountStr, NumberStyles.Number, CultureInfo.CurrentCulture, out decimal amt) &&
            !decimal.TryParse(amountStr, NumberStyles.Number, CultureInfo.InvariantCulture, out amt))
        {
            return;
        }
        Amount = decimal.Round(amt, 2);

        string rem = _txtRemittance.Text.Trim();
        if (_radRemittanceCreditorRef.Checked)
        {
            RemittanceCreditorReference = string.IsNullOrWhiteSpace(rem) ? null : rem;
            RemittanceUnstructured = null;
        }
        else
        {
            RemittanceUnstructured = string.IsNullOrWhiteSpace(rem) ? null : rem;
            RemittanceCreditorReference = null;
        }

        ExecutionDate = DateOnly.FromDateTime(_dtpExecutionDate.Value.Date);
    }

    private sealed record SourceAccountItem(ulong AccountId, string DisplayText)
    {
        public override string ToString() => DisplayText;
    }

    private sealed record TemplateComboItem(BeneficiaryTemplateViewModel? Template, string DisplayText)
    {
        public override string ToString() => DisplayText;
    }
}
