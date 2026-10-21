using System.Globalization;
using Broiler.Fond.Presentation.Services;
using Broiler.Graphics.Geometry;
using Broiler.Graphics.Text;
using Broiler.UI.Button.Standard;
using Broiler.UI.ComboBox;
using Broiler.UI.ComboBox.Standard;
using Broiler.UI.Dialog;
using Broiler.UI.Dialog.Standard;
using Broiler.UI.Edit.Standard;
using Broiler.UI.Label;
using Broiler.UI.Label.Standard;
using Broiler.UI.Panel;
using Broiler.UI.Panel.Standard;
using Broiler.UI.Window;

namespace Broiler.Fond.Presentation.Dialogs;

/// <summary>Manual account details; no network access or bank verification is implied.</summary>
public sealed class BroilerAccountSetupDialog : IDisposable
{
    public StandardDialog Dialog { get; } = new() { Title = "Add Account", PreferredSize = new BSize(520, 430) };
    public StandardComboBox ConnectionComboBox { get; } = new() { AccessibleName = "Bank Connection" };
    public StandardEdit NameEdit { get; } = new() { AccessibleName = "Account Name", PlaceholderText = "e.g. Household Current Account" };
    public StandardEdit IbanEdit { get; } = new() { AccessibleName = "Account IBAN", PlaceholderText = "e.g. DE89 3704 0044 0532 0130 00" };
    public StandardEdit CurrencyEdit { get; } = new() { AccessibleName = "Account Currency", Text = "EUR" };
    public StandardLabel ErrorLabel { get; } = new() { AccessibleName = "Account Validation Error", Wrapping = UiTextWrapping.Wrap };
    public StandardButton SaveButton { get; } = new() { Text = "Add Account", AccessibleName = "Save Manual Account" };
    public StandardButton CancelButton { get; } = new() { Text = "Cancel", AccessibleName = "Cancel Account Setup" };
    private readonly AccountSetupRequest _request;
    private AccountSetupResult _result = new(false, 0, string.Empty, string.Empty, string.Empty, true);

    public BroilerAccountSetupDialog(AccountSetupRequest request)
    {
        _request = request ?? throw new ArgumentNullException(nameof(request));
        ConnectionComboBox.SetItems(request.Connections.Select(c => new UiComboBoxItem(c.ConnectionId.ToString(CultureInfo.InvariantCulture), c.DisplayName)));
        if (request.Connections.Count > 0)
            ConnectionComboBox.SelectedIndex = 0;

        var content = new StandardPanel { LayoutMode = UiPanelLayoutMode.Stack, Spacing = 8 };
        content.AddChild(new StandardLabel
        {
            Text = "Enter account details manually. Balances remain unavailable until retrieved from your bank.",
            Wrapping = UiTextWrapping.Wrap,
            AccessibleName = "Manual Account Notice"
        });
        AddField("Bank Connection:", ConnectionComboBox);
        AddField("Account Name:", NameEdit);
        AddField("IBAN:", IbanEdit);
        AddField("Currency (e.g. EUR):", CurrencyEdit);
        content.AddChild(ErrorLabel);
        var actions = new DialogActionPanel { Spacing = 12 };
        actions.AddChild(SaveButton);
        actions.AddChild(CancelButton);
        content.AddChild(actions);
        SaveButton.Clicked += (_, _) => Accept();
        NameEdit.Submitted += (_, _) => Accept();
        IbanEdit.Submitted += (_, _) => Accept();
        CurrencyEdit.Submitted += (_, _) => Accept();
        CancelButton.Clicked += (_, _) => Dialog.Cancel();
        DialogLayout.Attach(Dialog, content);

        void AddField(string label, Broiler.UI.UiElement control)
        {
            content.AddChild(new StandardLabel { Text = label, AccessibleName = label });
            content.AddChild(control);
        }
    }

    public void Accept()
    {
        try
        {
            if (!ulong.TryParse(ConnectionComboBox.SelectedItem?.Id, NumberStyles.None, CultureInfo.InvariantCulture, out ulong connectionId) ||
                !_request.Connections.Any(c => c.ConnectionId == connectionId))
                throw new ArgumentException("Choose a bank connection. Create one in Connections first if necessary.");
            ManualAccountInput input = ManualAccountInput.Validate(NameEdit.Text, IbanEdit.Text, CurrencyEdit.Text);
            _result = new(true, connectionId, input.DisplayName, input.Iban, input.Currency, false);
            ErrorLabel.Text = string.Empty;
            Dialog.Accept("add-account");
        }
        catch (ArgumentException ex)
        {
            ErrorLabel.Text = ex.Message;
        }
    }

    public AccountSetupResult GetResult() => Dialog.CompletedResult?.Kind == UiDialogResultKind.Accepted
        ? _result : new(false, 0, string.Empty, string.Empty, string.Empty, true);
    public Task<UiDialogResult> ShowModalAsync(UiWindow owner, BRect? placement = null) => DialogLayout.ShowModalAsync(Dialog, owner, placement);
    public void Dispose() => Dialog.Dispose();
}
