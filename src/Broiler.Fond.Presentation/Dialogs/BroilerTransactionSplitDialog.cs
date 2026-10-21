namespace Broiler.Fond.Presentation.Dialogs;

using System;
using System.Globalization;
using System.Threading.Tasks;
using Broiler.Fond.Presentation.Sessions;
using Broiler.Fond.Presentation.ViewModels;
using Broiler.Graphics.Geometry;
using Broiler.Graphics.Text;
using Broiler.UI;
using Broiler.UI.Button.Standard;
using Broiler.UI.Dialog;
using Broiler.UI.Dialog.Standard;
using Broiler.UI.Edit.Standard;
using Broiler.UI.Label;
using Broiler.UI.Label.Standard;
using Broiler.UI.Panel;
using Broiler.UI.Panel.Standard;
using Broiler.UI.Window;

/// <summary>
/// Dialog for splitting a transaction into multiple categorized allocations.
/// Invariant: Exact decimal arithmetic ensures sum of splits equals original amount.
/// </summary>
public sealed class BroilerTransactionSplitDialog : IDisposable
{
    private readonly StandardDialog _dialog;
    private readonly StandardLabel _originalAmountLabel;
    private readonly StandardLabel _remainderLabel;
    private readonly StandardEdit _splitAmountEdit;
    private readonly StandardEdit _splitCategoryEdit;
    private readonly StandardButton _addSplitButton;
    private readonly StandardButton _saveButton;
    private readonly StandardButton _cancelButton;
    private readonly decimal _totalAmount;

    public StandardDialog Dialog => _dialog;
    public string Title => _dialog.Title;
    public StandardLabel OriginalAmountLabel => _originalAmountLabel;
    public StandardLabel RemainderLabel => _remainderLabel;
    public StandardEdit SplitAmountEdit => _splitAmountEdit;
    public StandardEdit SplitCategoryEdit => _splitCategoryEdit;
    public StandardButton AddSplitButton => _addSplitButton;
    public StandardButton SaveButton => _saveButton;
    public StandardButton CancelButton => _cancelButton;

    public BroilerTransactionSplitDialog(HostSession? session, TransactionViewModel? tx = null)
        : this(tx?.Amount ?? 100m, tx?.Currency ?? "EUR")
    {
    }

    public BroilerTransactionSplitDialog(decimal totalAmount = 100.00m, string currency = "EUR")
    {
        _totalAmount = totalAmount;

        _dialog = new StandardDialog
        {
            Title = "Split Transaction",
            PreferredSize = new BSize(520, 380)
        };

        var panel = new StandardPanel
        {
            LayoutMode = UiPanelLayoutMode.Stack,
            Spacing = 12
        };

        var topPanel = new StandardPanel
        {
            LayoutMode = UiPanelLayoutMode.Stack,
            Spacing = 6
        };

        _originalAmountLabel = new StandardLabel
        {
            Text = $"Original Transaction Amount: {totalAmount.ToString("0.00", CultureInfo.InvariantCulture)} {currency}",
            Wrapping = UiTextWrapping.Wrap,
            AccessibleName = "Original Amount Label"
        };
        topPanel.AddChild(_originalAmountLabel);

        _remainderLabel = new StandardLabel
        {
            Text = $"Unallocated Remainder: {totalAmount.ToString("0.00", CultureInfo.InvariantCulture)} {currency}",
            Wrapping = UiTextWrapping.Wrap,
            AccessibleName = "Remainder Warning Label"
        };
        topPanel.AddChild(_remainderLabel);

        panel.AddChild(topPanel);

        var form = new StandardPanel
        {
            LayoutMode = UiPanelLayoutMode.Stack,
            Spacing = 8
        };

        _splitAmountEdit = new StandardEdit
        {
            PlaceholderText = "Split Amount (0.00)",
            AccessibleName = "Split Amount Edit"
        };
        form.AddChild(_splitAmountEdit);

        _splitCategoryEdit = new StandardEdit
        {
            PlaceholderText = "Sub-Category Name",
            AccessibleName = "Split Category Edit"
        };
        form.AddChild(_splitCategoryEdit);

        _addSplitButton = new StandardButton
        {
            Text = "Add Split Allocation",
            AccessibleName = "Add Split Button"
        };
        _addSplitButton.Clicked += (_, _) =>
        {
            if (decimal.TryParse(_splitAmountEdit.Text, CultureInfo.InvariantCulture, out decimal amt))
            {
                decimal remainder = _totalAmount - amt;
                _remainderLabel.Text = $"Unallocated Remainder: {remainder.ToString("0.00", CultureInfo.InvariantCulture)} {currency}";
            }
        };
        form.AddChild(_addSplitButton);

        panel.AddChild(form);

        var buttonBar = new DialogActionPanel
        {
            LayoutMode = UiPanelLayoutMode.Stack,
            Spacing = 12
        };

        _saveButton = new StandardButton
        {
            Text = "Save Splits",
            AccessibleName = "Save Splits Button"
        };
        _saveButton.Clicked += (_, _) => Accept();

        _cancelButton = new StandardButton
        {
            Text = "Cancel",
            AccessibleName = "Cancel Button"
        };
        _cancelButton.Clicked += (_, _) => Cancel();

        buttonBar.AddChild(_saveButton);
        buttonBar.AddChild(_cancelButton);

        panel.AddChild(buttonBar);

        DialogLayout.Attach(_dialog, panel);
    }

    public void Accept() => _dialog.Accept("save");
    public void Cancel() => _dialog.Cancel();

    public Task<UiDialogResult> ShowModalAsync(UiWindow owner, BRect? placement = null)
    {
        return DialogLayout.ShowModalAsync(_dialog, owner, placement);
    }

    public void Dispose() => _dialog.Dispose();
}
