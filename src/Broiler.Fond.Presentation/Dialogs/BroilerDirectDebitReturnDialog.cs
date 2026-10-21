namespace Broiler.Fond.Presentation.Dialogs;

using System;
using System.Threading.Tasks;
using Broiler.Fond.Presentation.Sessions;
using Broiler.Fond.Presentation.ViewModels;
using Broiler.Graphics.Geometry;
using Broiler.Graphics.Text;
using Broiler.UI;
using Broiler.UI.Button.Standard;
using Broiler.UI.ComboBox;
using Broiler.UI.ComboBox.Standard;
using Broiler.UI.Dialog;
using Broiler.UI.Dialog.Standard;
using Broiler.UI.Label;
using Broiler.UI.Label.Standard;
using Broiler.UI.Panel;
using Broiler.UI.Panel.Standard;
using Broiler.UI.Window;

/// <summary>
/// Dialog for initiating a formal SEPA direct debit return according to EPC Rulebook standards.
/// </summary>
public sealed class BroilerDirectDebitReturnDialog : IDisposable
{
    private readonly StandardDialog _dialog;
    private readonly StandardLabel _transactionLabel;
    private readonly StandardComboBox _reasonComboBox;
    private readonly StandardLabel _noticeLabel;
    private readonly StandardButton _submitButton;
    private readonly StandardButton _cancelButton;

    public StandardDialog Dialog => _dialog;
    public string Title => _dialog.Title;
    public StandardLabel TransactionLabel => _transactionLabel;
    public StandardComboBox ReasonComboBox => _reasonComboBox;
    public StandardLabel NoticeLabel => _noticeLabel;
    public StandardButton SubmitButton => _submitButton;
    public StandardButton CancelButton => _cancelButton;

    public BroilerDirectDebitReturnDialog(HostSession? session = null)
        : this("Direct Debit - EUR 49.99")
    {
    }

    public BroilerDirectDebitReturnDialog(HostSession? session, DirectDebitReturnViewModel debitVm)
        : this(debitVm != null ? $"{debitVm.CreditorName} - {debitVm.Amount:N2} {debitVm.Currency}" : "Direct Debit")
    {
    }

    public BroilerDirectDebitReturnDialog(string transactionInfo = "Direct Debit - EUR 49.99")
    {
        _dialog = new StandardDialog
        {
            Title = "SEPA Direct Debit Return",
            PreferredSize = new BSize(500, 320)
        };

        var panel = new StandardPanel
        {
            LayoutMode = UiPanelLayoutMode.Stack,
            Spacing = 12
        };

        var content = new StandardPanel
        {
            LayoutMode = UiPanelLayoutMode.Stack,
            Spacing = 10
        };

        _transactionLabel = new StandardLabel
        {
            Text = $"Transaction: {transactionInfo}",
            Wrapping = UiTextWrapping.Wrap,
            AccessibleName = "Transaction Information"
        };
        content.AddChild(_transactionLabel);

        var reasonPrompt = new StandardLabel
        {
            Text = "Select Official SEPA Return Reason:",
            AccessibleName = "SEPA Return Reason Label"
        };
        content.AddChild(reasonPrompt);

        _reasonComboBox = new StandardComboBox();
        _reasonComboBox.SetItems([
            new UiComboBoxItem("MD01", "MD01 - No Valid Mandate"),
            new UiComboBoxItem("MD06", "MD06 - Disputed Authorized Transaction (8-week rule)"),
            new UiComboBoxItem("AM04", "AM04 - Insufficient Funds"),
            new UiComboBoxItem("MS02", "MS02 - Refusal by Debtor"),
            new UiComboBoxItem("MS03", "MS03 - Reason Not Specified"),
            new UiComboBoxItem("SL01", "SL01 - Specific Service Offered by Debtor Bank")
        ]);
        _reasonComboBox.SelectedIndex = 1; // Default: MD06
        content.AddChild(_reasonComboBox);

        _noticeLabel = new StandardLabel
        {
            Text = "Notice: Unconditional refund applies within 8 weeks of debit date pursuant to SEPA Core Rulebook.",
            Wrapping = UiTextWrapping.Wrap,
            AccessibleName = "SEPA Legal Notice"
        };
        content.AddChild(_noticeLabel);

        panel.AddChild(content);

        var buttonBar = new DialogActionPanel
        {
            LayoutMode = UiPanelLayoutMode.Stack,
            Spacing = 12
        };

        _submitButton = new StandardButton
        {
            Text = "Submit Return",
            AccessibleName = "Submit Return Button"
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

    public void Accept() => _dialog.Accept("return");
    public void Cancel() => _dialog.Cancel();

    public string SelectedReasonCode => _reasonComboBox.SelectedItem?.Id ?? "MD06";
    public bool IsConfirmed => _dialog.CompletedResult?.Kind == UiDialogResultKind.Accepted;

    public Task<UiDialogResult> ShowModalAsync(UiWindow owner, BRect? placement = null)
    {
        return DialogLayout.ShowModalAsync(_dialog, owner, placement);
    }

    public void Dispose() => _dialog.Dispose();
}
