namespace Broiler.Fond.Presentation.Dialogs;

using System;
using System.Threading.Tasks;
using Broiler.Fond.Kernel.FinTs;
using Broiler.Graphics.Geometry;
using Broiler.Graphics.Text;
using Broiler.UI;
using Broiler.UI.Button.Standard;
using Broiler.UI.CheckBox.Standard;
using Broiler.UI.Dialog;
using Broiler.UI.Dialog.Standard;
using Broiler.UI.Label;
using Broiler.UI.Label.Standard;
using Broiler.UI.Panel;
using Broiler.UI.Panel.Standard;
using Broiler.UI.Window;

/// <summary>
/// Confirmation dialog warning against phishing when connecting to a bank host for the first time.
/// </summary>
public sealed class BroilerEndpointConfirmationDialog : IDisposable
{
    private readonly StandardDialog _dialog;
    private readonly StandardLabel _warningLabel;
    private readonly StandardLabel _hostLabel;
    private readonly StandardCheckBox _verifyCheckBox;
    private readonly StandardButton _confirmButton;
    private readonly StandardButton _cancelButton;

    public StandardDialog Dialog => _dialog;
    public string Title => _dialog.Title;
    public StandardLabel HostLabel => _hostLabel;
    public StandardCheckBox VerifyCheckBox => _verifyCheckBox;
    public StandardButton ConfirmButton => _confirmButton;
    public StandardButton CancelButton => _cancelButton;
    private readonly string? _validEndpoint;
    private bool _reviewAccepted;

    public BroilerEndpointConfirmationDialog(Uri? endpoint, string bankName = "")
        : this(endpoint?.ToString() ?? "https://localhost")
    {
    }

    public BroilerEndpointConfirmationDialog(string endpointUri)
    {
        _dialog = new StandardDialog
        {
            Title = "Verify Bank Host Endpoint",
            PreferredSize = new BSize(500, 260)
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

        _warningLabel = new StandardLabel
        {
            Text = "SECURITY WARNING: Connecting to bank servers transmits sensitive authentication credentials. Verify that the bank hostname below matches your financial institution exactly.",
            Wrapping = UiTextWrapping.Wrap,
            AccessibleName = "Phishing Warning Notice"
        };
        content.AddChild(_warningLabel);

        string display = "Invalid HTTPS endpoint; edit the connection before confirming.";
        try
        {
            var configuration = Broiler.Fond.Kernel.Domain.Institutions.ManualInstitutionConfiguration.Create("Bank", endpointUri);
            var parsed = configuration.FinTsEndpoint;
            _validEndpoint = parsed.AbsoluteUri;
            display = $"Endpoint: {parsed.AbsoluteUri}\nScheme: {parsed.Scheme}\nHost: {parsed.IdnHost}\nPort: {parsed.Port}\nPath and query: {parsed.PathAndQuery}";
        }
        catch (ArgumentException) { }

        _hostLabel = new StandardLabel
        {
            Text = display,
            Wrapping = UiTextWrapping.Wrap,
            AccessibleName = "Bank Hostname Badge"
        };
        content.AddChild(_hostLabel);

        _verifyCheckBox = new StandardCheckBox
        {
            Text = "I have independently verified this bank host endpoint.",
            AccessibleName = "Verification Confirmation Checkbox"
        };
        content.AddChild(_verifyCheckBox);

        panel.AddChild(content);

        var buttonBar = new DialogActionPanel
        {
            LayoutMode = UiPanelLayoutMode.Stack,
            Spacing = 12
        };

        _confirmButton = new StandardButton
        {
            Text = "Trust Host & Continue",
            AccessibleName = "Confirm Host Button"
        };
        _confirmButton.Clicked += (_, _) => Accept();

        _cancelButton = new StandardButton
        {
            Text = "Abort Connection",
            AccessibleName = "Cancel Button"
        };
        _cancelButton.Clicked += (_, _) => Cancel();

        buttonBar.AddChild(_confirmButton);
        buttonBar.AddChild(_cancelButton);

        panel.AddChild(buttonBar);

        DialogLayout.Attach(_dialog, panel);
    }

    public void Accept()
    {
        if (_verifyCheckBox.IsChecked == true && _validEndpoint is not null)
        {
            _reviewAccepted = true;
            _dialog.Accept("confirmed");
        }
    }

    public void Cancel() => _dialog.Cancel();

    public bool IsConfirmed => _reviewAccepted && _verifyCheckBox.IsChecked == true && _dialog.CompletedResult?.Kind == UiDialogResultKind.Accepted;

    public Task<UiDialogResult> ShowModalAsync(UiWindow owner, BRect? placement = null)
    {
        return DialogLayout.ShowModalAsync(_dialog, owner, placement);
    }

    public void Dispose() => _dialog.Dispose();
}
