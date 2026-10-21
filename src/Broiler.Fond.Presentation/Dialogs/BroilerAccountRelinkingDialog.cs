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
using Broiler.UI.Label;
using Broiler.UI.Label.Standard;
using Broiler.UI.ListView.Standard;
using Broiler.UI.Panel;
using Broiler.UI.Panel.Standard;
using Broiler.UI.Window;

/// <summary>
/// Account relinking dialog providing side-by-side comparison of rediscovered accounts and purge options.
/// </summary>
public sealed class BroilerAccountRelinkingDialog : IDisposable
{
    private readonly StandardDialog _dialog;
    private readonly StandardLabel _headerLabel;
    private readonly StandardListView _candidateListView;
    private readonly StandardCheckBox _purgeCheckBox;
    private readonly StandardButton _relinkButton;
    private readonly StandardButton _cancelButton;

    public StandardDialog Dialog => _dialog;
    public string Title => _dialog.Title;
    public StandardListView CandidateListView => _candidateListView;
    public StandardCheckBox PurgeCheckBox => _purgeCheckBox;
    public StandardButton RelinkButton => _relinkButton;
    public StandardButton CancelButton => _cancelButton;

    public BroilerAccountRelinkingDialog(HostSession? session = null)
    {
        _dialog = new StandardDialog
        {
            Title = "Relink Accounts & Rediscovery",
            PreferredSize = new BSize(540, 380)
        };

        var panel = new StandardPanel
        {
            LayoutMode = UiPanelLayoutMode.Stack,
            Spacing = 12
        };

        _headerLabel = new StandardLabel
        {
            Text = "Rediscovered account candidates matched by IBAN / account number:",
            Wrapping = UiTextWrapping.Wrap,
            AccessibleName = "Relinking Header Notice"
        };
        panel.AddChild(_headerLabel);

        _candidateListView = new StandardListView
        {
            PreferredSize = new BSize(500, 200),
            AccessibleName = "Account Candidates List"
        };
        panel.AddChild(_candidateListView);

        var bottomPanel = new StandardPanel
        {
            LayoutMode = UiPanelLayoutMode.Stack,
            Spacing = 10
        };

        _purgeCheckBox = new StandardCheckBox
        {
            Text = "Purge unlinked orphan records while preserving ledger provenance",
            AccessibleName = "Purge Orphan Records Checkbox"
        };
        bottomPanel.AddChild(_purgeCheckBox);

        var buttonBar = new DialogActionPanel
        {
            LayoutMode = UiPanelLayoutMode.Stack,
            Spacing = 12
        };

        _relinkButton = new StandardButton
        {
            Text = "Relink Selected Account",
            AccessibleName = "Relink Account Button"
        };
        _relinkButton.Clicked += (_, _) => Accept();

        _cancelButton = new StandardButton
        {
            Text = "Cancel",
            AccessibleName = "Cancel Button"
        };
        _cancelButton.Clicked += (_, _) => Cancel();

        buttonBar.AddChild(_relinkButton);
        buttonBar.AddChild(_cancelButton);
        bottomPanel.AddChild(buttonBar);

        panel.AddChild(bottomPanel);

        DialogLayout.Attach(_dialog, panel);
    }

    public void Accept() => _dialog.Accept("relink");
    public void Cancel() => _dialog.Cancel();

    public bool ShouldPurge => _purgeCheckBox.IsChecked == true;
    public bool IsConfirmed => _dialog.CompletedResult?.Kind == UiDialogResultKind.Accepted;

    public Task<UiDialogResult> ShowModalAsync(UiWindow owner, BRect? placement = null)
    {
        return DialogLayout.ShowModalAsync(_dialog, owner, placement);
    }

    public void Dispose() => _dialog.Dispose();
}
