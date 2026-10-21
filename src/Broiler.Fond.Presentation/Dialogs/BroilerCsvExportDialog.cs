namespace Broiler.Fond.Presentation.Dialogs;

using System;
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
/// Dialog configuring CSV transaction history export.
/// </summary>
public sealed class BroilerCsvExportDialog : IDisposable
{
    private readonly StandardDialog _dialog;
    private readonly StandardEdit _startDateEdit;
    private readonly StandardEdit _endDateEdit;
    private readonly StandardCheckBox _includeIbanCheckBox;
    private readonly StandardCheckBox _semicolonSeparatorCheckBox;
    private readonly StandardButton _exportButton;
    private readonly StandardButton _cancelButton;

    public StandardDialog Dialog => _dialog;
    public string Title => _dialog.Title;
    public StandardEdit StartDateEdit => _startDateEdit;
    public StandardEdit EndDateEdit => _endDateEdit;
    public StandardCheckBox IncludeIbanCheckBox => _includeIbanCheckBox;
    public StandardCheckBox SemicolonSeparatorCheckBox => _semicolonSeparatorCheckBox;
    public StandardButton ExportButton => _exportButton;
    public StandardButton CancelButton => _cancelButton;

    public BroilerCsvExportDialog(HostSession? session = null, object? filter = null)
        : this()
    {
    }

    public BroilerCsvExportDialog()
    {
        _dialog = new StandardDialog
        {
            Title = "Export Transactions to CSV",
            PreferredSize = new BSize(480, 320)
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

        var startLabel = new StandardLabel { Text = "Start Date (YYYY-MM-DD):", AccessibleName = "Start Date Label" };
        _startDateEdit = new StandardEdit
        {
            Text = DateTime.UtcNow.AddMonths(-1).ToString("yyyy-MM-dd"),
            PlaceholderText = "YYYY-MM-DD",
            AccessibleName = "Export Start Date"
        };
        content.AddChild(startLabel);
        content.AddChild(_startDateEdit);

        var endLabel = new StandardLabel { Text = "End Date (YYYY-MM-DD):", AccessibleName = "End Date Label" };
        _endDateEdit = new StandardEdit
        {
            Text = DateTime.UtcNow.ToString("yyyy-MM-dd"),
            PlaceholderText = "YYYY-MM-DD",
            AccessibleName = "Export End Date"
        };
        content.AddChild(endLabel);
        content.AddChild(_endDateEdit);

        _includeIbanCheckBox = new StandardCheckBox
        {
            Text = "Include Counterparty IBAN in export",
            AccessibleName = "Include IBAN Checkbox"
        };
        content.AddChild(_includeIbanCheckBox);

        _semicolonSeparatorCheckBox = new StandardCheckBox
        {
            Text = "Use semicolon ';' delimiter (German Excel formatting)",
            AccessibleName = "Semicolon Delimiter Checkbox"
        };
        content.AddChild(_semicolonSeparatorCheckBox);

        panel.AddChild(content);

        var buttonBar = new DialogActionPanel
        {
            LayoutMode = UiPanelLayoutMode.Stack,
            Spacing = 12
        };

        _exportButton = new StandardButton
        {
            Text = "Export CSV",
            AccessibleName = "Export CSV Button"
        };
        _exportButton.Clicked += (_, _) => Accept();

        _cancelButton = new StandardButton
        {
            Text = "Cancel",
            AccessibleName = "Cancel Button"
        };
        _cancelButton.Clicked += (_, _) => Cancel();

        buttonBar.AddChild(_exportButton);
        buttonBar.AddChild(_cancelButton);

        panel.AddChild(buttonBar);

        DialogLayout.Attach(_dialog, panel);
    }

    public void Accept() => _dialog.Accept("export");
    public void Cancel() => _dialog.Cancel();

    public string StartDate => _startDateEdit.Text.Trim();
    public string EndDate => _endDateEdit.Text.Trim();
    public bool IncludeIban => _includeIbanCheckBox.IsChecked == true;
    public bool UseSemicolon => _semicolonSeparatorCheckBox.IsChecked == true;
    public bool IsConfirmed => _dialog.CompletedResult?.Kind == UiDialogResultKind.Accepted;

    public Task<UiDialogResult> ShowModalAsync(UiWindow owner, BRect? placement = null)
    {
        return DialogLayout.ShowModalAsync(_dialog, owner, placement);
    }

    public void Dispose() => _dialog.Dispose();
}
