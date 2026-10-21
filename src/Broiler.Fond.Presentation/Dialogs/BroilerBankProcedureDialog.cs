using Broiler.Fond.Presentation.Services;
using Broiler.Graphics.Geometry;
using Broiler.Graphics.Text;
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

namespace Broiler.Fond.Presentation.Dialogs;

public sealed class BroilerBankProcedureDialog : IDisposable
{
    public StandardDialog Dialog { get; } = new() { Title = "Choose Bank Sign-in Procedure", PreferredSize = new BSize(560, 390) };
    public StandardComboBox ProcedureComboBox { get; } = new() { AccessibleName = "Bank-advertised Sign-in Procedure" };
    public StandardLabel ErrorLabel { get; } = new() { AccessibleName = "Procedure Selection Error", Wrapping = UiTextWrapping.Wrap };
    public StandardButton ChooseButton { get; } = new() { Text = "Check Selected Procedure", AccessibleName = "Check Selected Bank Procedure" };
    private readonly BankProcedureChoiceRequest _request;
    private string? _selected;
    public BroilerBankProcedureDialog(BankProcedureChoiceRequest request)
    {
        _request = request;
        ProcedureComboBox.SetItems(request.Options.Select(o => new UiComboBoxItem(o.Id, o.Name)));
        ProcedureComboBox.SelectedIndex = -1;
        var content = new StandardPanel { LayoutMode = UiPanelLayoutMode.Stack, Spacing = 10 };
        content.AddChild(new StandardLabel { Text = $"{request.BankName}\n{request.Endpoint}\nChoose a bank-advertised procedure. The next exchange sends the configured customer ID to check login-specific parameters, without a PIN. This does not sign you in.", Wrapping = UiTextWrapping.Wrap, AccessibleName = "Procedure Qualification Notice" });
        content.AddChild(ProcedureComboBox);
        content.AddChild(new StandardLabel { Text = string.Join("\n", request.Options.Select(o => $"{o.Name}: {o.Explanation}")), Wrapping = UiTextWrapping.Wrap, AccessibleName = "Bank Procedure Support Details" });
        content.AddChild(ErrorLabel);
        var actions = new DialogActionPanel { Spacing = 10 };
        actions.AddChild(ChooseButton);
        var cancel = new StandardButton { Text = "Cancel", AccessibleName = "Cancel Bank Procedure Selection" };
        cancel.Clicked += (_, _) => Dialog.Cancel();
        actions.AddChild(cancel); content.AddChild(actions);
        ChooseButton.Clicked += (_, _) => Accept();
        DialogLayout.Attach(Dialog, content);
    }
    public void Accept()
    {
        var option = _request.Options.SingleOrDefault(o => o.Id == ProcedureComboBox.SelectedItem?.Id);
        if (option is null) { ErrorLabel.Text = "Choose a procedure explicitly before continuing."; return; }
        if (!option.CanSelect) { ErrorLabel.Text = option.Explanation; return; }
        _selected = option.Id; Dialog.Accept("bank-procedure");
    }
    public string? GetSelection() => Dialog.CompletedResult?.Kind == UiDialogResultKind.Accepted ? _selected : null;
    public Task<UiDialogResult> ShowModalAsync(UiWindow owner) => DialogLayout.ShowModalAsync(Dialog, owner, null);
    public void Dispose() => Dialog.Dispose();
}
