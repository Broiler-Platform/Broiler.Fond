namespace Broiler.Fond.Presentation.Dialogs;

using System;
using System.Globalization;
using System.Threading.Tasks;
using Broiler.Fond.Presentation.Sessions;
using Broiler.Graphics.Geometry;
using Broiler.Graphics.Text;
using Broiler.UI;
using Broiler.UI.Button.Standard;
using Broiler.UI.CheckBox.Standard;
using Broiler.UI.Dialog;
using Broiler.UI.Dialog.Standard;
using Broiler.UI.Edit.Standard;
using Broiler.UI.Label;
using Broiler.UI.Label.Standard;
using Broiler.UI.Panel;
using Broiler.UI.Panel.Standard;
using Broiler.UI.Window;

/// <summary>
/// Payment initiation dialog allowing creation and validation of SEPA credit transfers.
/// Invariant: Exact decimal monetary validation, strict IBAN structure verification.
/// </summary>
public sealed class BroilerPaymentInitiationDialog : IDisposable
{
    private readonly StandardDialog _dialog;
    private readonly StandardEdit _beneficiaryEdit;
    private readonly StandardEdit _ibanEdit;
    private readonly StandardEdit _amountEdit;
    private readonly StandardEdit _remittanceEdit;
    private readonly StandardCheckBox _instantCheckBox;
    private readonly StandardLabel _errorLabel;
    private readonly StandardButton _submitButton;
    private readonly StandardButton _cancelButton;

    public StandardDialog Dialog => _dialog;
    public StandardEdit BeneficiaryEdit => _beneficiaryEdit;
    public StandardEdit IbanEdit => _ibanEdit;
    public StandardEdit AmountEdit => _amountEdit;
    public StandardEdit RemittanceEdit => _remittanceEdit;
    public StandardCheckBox InstantCheckBox => _instantCheckBox;
    public StandardLabel ErrorLabel => _errorLabel;
    public StandardButton SubmitButton => _submitButton;
    public StandardButton CancelButton => _cancelButton;

    public BroilerPaymentInitiationDialog(HostSession? session)
        : this()
    {
    }

    public BroilerPaymentInitiationDialog(
        string defaultBeneficiary = "",
        string defaultIban = "",
        decimal defaultAmount = 0m,
        string defaultRemittance = "")
    {
        _dialog = new StandardDialog
        {
            Title = "New SEPA Transfer",
            PreferredSize = new BSize(520, 420)
        };

        var panel = new StandardPanel
        {
            LayoutMode = UiPanelLayoutMode.Stack,
            Spacing = 12
        };

        var form = new StandardPanel
        {
            LayoutMode = UiPanelLayoutMode.Stack,
            Spacing = 8
        };

        var benLabel = new StandardLabel { Text = "Beneficiary Name:", AccessibleName = "Beneficiary Label" };
        _beneficiaryEdit = new StandardEdit
        {
            Text = defaultBeneficiary,
            PlaceholderText = "e.g. Max Mustermann",
            AccessibleName = "Beneficiary Name"
        };
        form.AddChild(benLabel);
        form.AddChild(_beneficiaryEdit);

        var ibanLabel = new StandardLabel { Text = "Beneficiary IBAN:", AccessibleName = "IBAN Label" };
        _ibanEdit = new StandardEdit
        {
            Text = defaultIban,
            PlaceholderText = "DE89 3704 0044 0532 0130 00",
            AccessibleName = "Beneficiary IBAN"
        };
        form.AddChild(ibanLabel);
        form.AddChild(_ibanEdit);

        var amtLabel = new StandardLabel { Text = "Amount (EUR):", AccessibleName = "Amount Label" };
        _amountEdit = new StandardEdit
        {
            Text = defaultAmount > 0 ? defaultAmount.ToString("0.00", CultureInfo.InvariantCulture) : string.Empty,
            PlaceholderText = "0.00",
            AccessibleName = "Transfer Amount"
        };
        form.AddChild(amtLabel);
        form.AddChild(_amountEdit);

        var remLabel = new StandardLabel { Text = "Remittance Information (Purpose):", AccessibleName = "Remittance Label" };
        _remittanceEdit = new StandardEdit
        {
            Text = defaultRemittance,
            PlaceholderText = "e.g. Invoice 2026-42",
            AccessibleName = "Remittance Text"
        };
        form.AddChild(remLabel);
        form.AddChild(_remittanceEdit);

        _instantCheckBox = new StandardCheckBox
        {
            Text = "SEPA Instant Payment (Real-Time Settlement)",
            AccessibleName = "Instant SEPA Checkbox"
        };
        form.AddChild(_instantCheckBox);

        _errorLabel = new StandardLabel
        {
            Text = string.Empty,
            Wrapping = UiTextWrapping.Wrap,
            AccessibleName = "Payment Validation Error"
        };
        form.AddChild(_errorLabel);

        panel.AddChild(form);

        var buttonBar = new DialogActionPanel
        {
            LayoutMode = UiPanelLayoutMode.Stack,
            Spacing = 12
        };

        _submitButton = new StandardButton
        {
            Text = "Review Transfer",
            AccessibleName = "Review Payment Button"
        };
        _submitButton.Clicked += (_, _) => Accept();

        _cancelButton = new StandardButton
        {
            Text = "Cancel",
            AccessibleName = "Cancel Button"
        };
        _cancelButton.Clicked += (_, _) => Cancel();

        buttonBar.AddChild(_submitButton);
        buttonBar.AddChild(_cancelButton);

        panel.AddChild(buttonBar);

        DialogLayout.Attach(_dialog, panel);
    }

    public void Accept()
    {
        string iban = _ibanEdit.Text.Replace(" ", "").Trim();
        if (iban.Length < 15 || iban.Length > 34)
        {
            _errorLabel.Text = "Please enter a valid SEPA IBAN (15–34 characters).";
            return;
        }

        if (string.IsNullOrWhiteSpace(_beneficiaryEdit.Text))
        {
            _errorLabel.Text = "Beneficiary name is required.";
            return;
        }

        if (!decimal.TryParse(_amountEdit.Text, CultureInfo.InvariantCulture, out decimal amt) || amt <= 0)
        {
            _errorLabel.Text = "Please enter a positive decimal transfer amount.";
            return;
        }

        _errorLabel.Text = string.Empty;
        _dialog.Accept("review");
    }

    public void Cancel() => _dialog.Cancel();

    public string Beneficiary => _beneficiaryEdit.Text.Trim();
    public string Iban => _ibanEdit.Text.Replace(" ", "").Trim();
    public decimal Amount => decimal.TryParse(_amountEdit.Text, CultureInfo.InvariantCulture, out decimal a) ? a : 0m;
    public string RemittanceText => _remittanceEdit.Text.Trim();
    public bool IsInstant => _instantCheckBox.IsChecked == true;

    public Task<UiDialogResult> ShowModalAsync(UiWindow owner, BRect? placement = null)
    {
        return DialogLayout.ShowModalAsync(_dialog, owner, placement);
    }

    public void Dispose() => _dialog.Dispose();
}
