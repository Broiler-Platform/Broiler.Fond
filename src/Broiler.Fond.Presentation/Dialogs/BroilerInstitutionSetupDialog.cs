namespace Broiler.Fond.Presentation.Dialogs;

using System;
using System.Threading.Tasks;
using Broiler.Fond.Presentation.Services;
using Broiler.Fond.Presentation.Sessions;
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
/// Institution setup dialog allowing interactive configuration of bank FinTS server parameters.
/// Invariant: Validates that endpoint URLs use HTTPS to defend against insecure transports.
/// </summary>
public sealed class BroilerInstitutionSetupDialog : IDisposable
{
    private readonly StandardDialog _dialog;
    private readonly StandardEdit _nameEdit;
    private readonly StandardEdit _bankCodeEdit;
    private readonly StandardEdit _endpointEdit;
    private readonly StandardLabel _errorLabel;
    private readonly StandardButton _saveButton;
    private readonly StandardButton _cancelButton;

    public StandardDialog Dialog => _dialog;
    public string Title => _dialog.Title;
    public StandardEdit NameEdit => _nameEdit;
    public StandardEdit BankCodeEdit => _bankCodeEdit;
    public StandardEdit EndpointEdit => _endpointEdit;
    public StandardEdit UserIdEdit { get; } = new() { AccessibleName = "Bank User ID", PlaceholderText = "Bank login ID, not your profile passphrase" };
    public StandardEdit CustomerIdEdit { get; } = new() { AccessibleName = "Bank Customer ID", PlaceholderText = "Optional; leave blank if your bank does not require it" };
    private InstitutionSetupResult? _accepted;
    public StandardLabel ErrorLabel => _errorLabel;
    public StandardButton SaveButton => _saveButton;
    public StandardButton CancelButton => _cancelButton;

    public BroilerInstitutionSetupDialog()
        : this(new InstitutionSetupRequest())
    {
    }

    public BroilerInstitutionSetupDialog(HostSession? session)
        : this(new InstitutionSetupRequest())
    {
    }

    public BroilerInstitutionSetupDialog(InstitutionSetupRequest? request)
    {
        var req = request ?? new InstitutionSetupRequest();

        _dialog = new StandardDialog
        {
            Title = "Bank Connection Setup",
            PreferredSize = new BSize(500, 320)
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

        var nameLabel = new StandardLabel { Text = "Bank Display Name:", AccessibleName = "Bank Name Label" };
        _nameEdit = new StandardEdit
        {
            Text = req.InitialName,
            PlaceholderText = "e.g. Deutsche Bank",
            AccessibleName = "Bank Display Name"
        };
        form.AddChild(nameLabel);
        form.AddChild(_nameEdit);

        var bankCodeLabel = new StandardLabel { Text = "Bank Code (BLZ):", AccessibleName = "Bank Code Label" };
        _bankCodeEdit = new StandardEdit
        {
            Text = req.InitialBankCode,
            PlaceholderText = "e.g. 50070010",
            AccessibleName = "Bank Identifier Code"
        };
        form.AddChild(bankCodeLabel);
        form.AddChild(_bankCodeEdit);

        var endpointLabel = new StandardLabel { Text = "FinTS HTTPS Endpoint URL:", AccessibleName = "Endpoint URL Label" };
        _endpointEdit = new StandardEdit
        {
            Text = req.InitialEndpoint,
            PlaceholderText = "https://fints.bank.de/fints",
            AccessibleName = "FinTS Endpoint URL"
        };
        form.AddChild(endpointLabel);
        form.AddChild(_endpointEdit);
        UserIdEdit.Text = req.InitialUserId;
        CustomerIdEdit.Text = req.InitialCustomerId ?? string.Empty;
        form.AddChild(new StandardLabel { Text = "Bank User ID:", AccessibleName = "Bank User ID Label" });
        form.AddChild(UserIdEdit);
        form.AddChild(new StandardLabel { Text = "Customer ID (optional):", AccessibleName = "Bank Customer ID Label" });
        form.AddChild(CustomerIdEdit);

        _errorLabel = new StandardLabel
        {
            Text = string.Empty,
            Wrapping = UiTextWrapping.Wrap,
            AccessibleName = "Validation Error Message"
        };
        form.AddChild(_errorLabel);

        panel.AddChild(form);

        var buttonBar = new DialogActionPanel
        {
            LayoutMode = UiPanelLayoutMode.Stack,
            Spacing = 12
        };

        _saveButton = new StandardButton
        {
            Text = "Save Institution",
            AccessibleName = "Save Institution Button"
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

    public void Accept()
    {
        try
        {
            string? customerId = string.IsNullOrEmpty(CustomerIdEdit.Text) ? null : CustomerIdEdit.Text;
            var setup = BankConnectionSetup.Validate(_nameEdit.Text.Trim(), _endpointEdit.Text, _bankCodeEdit.Text.Trim(), UserIdEdit.Text, customerId);
            _accepted = new(true, setup.DisplayName, _bankCodeEdit.Text.Trim(), setup.FinTsEndpoint.AbsoluteUri, false, UserIdEdit.Text, customerId);
            _errorLabel.Text = string.Empty;
            _dialog.Accept("save");
        }
        catch (ArgumentException ex)
        {
            _errorLabel.Text = ex.Message;
        }
    }

    public void Cancel()
    {
        _dialog.Cancel();
    }

    public InstitutionSetupResult GetResult()
    {
        return _dialog.CompletedResult?.Kind == UiDialogResultKind.Accepted && _accepted is not null
            ? _accepted : new(false, "", "", "", true);
    }

    public Task<UiDialogResult> ShowModalAsync(UiWindow owner, BRect? placement = null)
    {
        return DialogLayout.ShowModalAsync(_dialog, owner, placement);
    }

    public void Dispose() => _dialog.Dispose();
}
