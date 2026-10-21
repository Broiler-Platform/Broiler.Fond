namespace Broiler.Fond.Presentation.Dialogs;

using System;
using System.Globalization;
using System.Threading.Tasks;
using Broiler.Fond.Kernel.Storage;
using Broiler.Fond.Presentation.Services;
using Broiler.Graphics.Geometry;
using Broiler.Graphics.Text;
using Broiler.UI;
using Broiler.UI.Button.Standard;
using Broiler.UI.Dialog;
using Broiler.UI.Dialog.Standard;
using Broiler.UI.Label;
using Broiler.UI.Label.Standard;
using Broiler.UI.Panel;
using Broiler.UI.Panel.Standard;
using Broiler.UI.Window;

/// <summary>
/// Payment review dialog displaying Verification of Payee (VoP) status, bank fee warnings,
/// and requiring explicit user authorization before transmission.
/// </summary>
public sealed class BroilerPaymentReviewDialog : IDisposable
{
    private readonly StandardDialog _dialog;
    private readonly StandardLabel _recipientLabel;
    private readonly StandardLabel _ibanLabel;
    private readonly StandardLabel _amountLabel;
    private readonly StandardLabel _sourceLabel;
    private readonly StandardLabel _vopBadgeLabel;
    private readonly StandardLabel _warningLabel;
    private readonly StandardButton _confirmButton;
    private readonly StandardButton _cancelButton;

    public StandardDialog Dialog => _dialog;
    public string Title => _dialog.Title;
    public StandardLabel RecipientLabel => _recipientLabel;
    public StandardLabel IbanLabel => _ibanLabel;
    public StandardLabel AmountLabel => _amountLabel;
    public StandardLabel VoPBadgeLabel => _vopBadgeLabel;
    public StandardLabel WarningLabel => _warningLabel;
    public StandardButton ConfirmButton => _confirmButton;
    public StandardButton CancelButton => _cancelButton;

    public BroilerPaymentReviewDialog(
        string sourceAccountName,
        string beneficiaryName,
        string beneficiaryIban,
        string beneficiaryBic,
        decimal amount,
        string currency,
        string remittance,
        DateOnly executionDate,
        StorePaymentVoPResultEntry? vopResult = null,
        string? bankWarning = null)
        : this(new PaymentReviewRequest(
            0,
            beneficiaryName,
            beneficiaryIban,
            amount,
            currency,
            sourceAccountName,
            "Standard",
            remittance,
            vopResult?.Status ?? "Match",
            bankWarning))
    {
    }

    public BroilerPaymentReviewDialog(PaymentReviewRequest request)
    {
        _dialog = new StandardDialog
        {
            Title = "Review Payment Before Submission",
            PreferredSize = new BSize(500, 360)
        };

        var panel = new StandardPanel
        {
            LayoutMode = UiPanelLayoutMode.Stack,
            Spacing = 12
        };

        var content = new StandardPanel
        {
            LayoutMode = UiPanelLayoutMode.Stack,
            Spacing = 8
        };

        _recipientLabel = new StandardLabel
        {
            Text = $"Payee: {request.BeneficiaryName}",
            Wrapping = UiTextWrapping.Wrap,
            AccessibleName = "Beneficiary Payee Name"
        };
        content.AddChild(_recipientLabel);

        _ibanLabel = new StandardLabel
        {
            Text = $"IBAN: {request.BeneficiaryIban}",
            Wrapping = UiTextWrapping.Wrap,
            AccessibleName = "Beneficiary IBAN"
        };
        content.AddChild(_ibanLabel);

        _amountLabel = new StandardLabel
        {
            Text = $"Amount: {request.Amount.ToString("0.00", CultureInfo.InvariantCulture)} {request.Currency} ({request.ExecutionMode})",
            Wrapping = UiTextWrapping.Wrap,
            AccessibleName = "Payment Total Amount"
        };
        content.AddChild(_amountLabel);

        _sourceLabel = new StandardLabel
        {
            Text = $"From: {request.SourceAccountDisplay}",
            Wrapping = UiTextWrapping.Wrap,
            AccessibleName = "Source Account"
        };
        content.AddChild(_sourceLabel);

        string vopText = string.IsNullOrWhiteSpace(request.VoPStatus) ? "VoP Status: Verified Payee Match" : $"VoP Status: {request.VoPStatus}";
        _vopBadgeLabel = new StandardLabel
        {
            Text = vopText,
            Wrapping = UiTextWrapping.Wrap,
            AccessibleName = "Verification of Payee Status"
        };
        content.AddChild(_vopBadgeLabel);

        if (!string.IsNullOrWhiteSpace(request.BankWarning))
        {
            _warningLabel = new StandardLabel
            {
                Text = $"Bank Advisory: {request.BankWarning}",
                Wrapping = UiTextWrapping.Wrap,
                AccessibleName = "Bank Warning Notice"
            };
            content.AddChild(_warningLabel);
        }
        else
        {
            _warningLabel = new StandardLabel
            {
                Text = "Payment ready for FinTS authorization.",
                Wrapping = UiTextWrapping.Wrap,
                AccessibleName = "Authorization Status"
            };
            content.AddChild(_warningLabel);
        }

        panel.AddChild(content);

        var buttonBar = new DialogActionPanel
        {
            LayoutMode = UiPanelLayoutMode.Stack,
            Spacing = 12
        };

        _confirmButton = new StandardButton
        {
            Text = "Authorize & Submit",
            AccessibleName = "Authorize Payment Button"
        };
        _confirmButton.Clicked += (_, _) => Accept();

        _cancelButton = new StandardButton
        {
            Text = "Reject Draft",
            AccessibleName = "Reject Payment Button"
        };
        _cancelButton.Clicked += (_, _) => Cancel();

        buttonBar.AddChild(_confirmButton);
        buttonBar.AddChild(_cancelButton);

        panel.AddChild(buttonBar);

        DialogLayout.Attach(_dialog, panel);
    }

    public void Accept() => _dialog.Accept("authorized");
    public void Cancel() => _dialog.Cancel();

    public PaymentReviewResult GetResult()
    {
        bool confirmed = _dialog.CompletedResult?.Kind == UiDialogResultKind.Accepted;
        return new PaymentReviewResult(confirmed, !confirmed);
    }

    public Task<UiDialogResult> ShowModalAsync(UiWindow owner, BRect? placement = null)
    {
        return DialogLayout.ShowModalAsync(_dialog, owner, placement);
    }

    public void Dispose() => _dialog.Dispose();
}
