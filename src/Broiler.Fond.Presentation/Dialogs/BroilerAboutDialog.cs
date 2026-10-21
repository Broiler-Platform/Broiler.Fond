namespace Broiler.Fond.Presentation.Dialogs;

using System;
using System.Threading.Tasks;
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
/// About dialog displaying product identity, legal classification, zero-telemetry disclosure, and license.
/// </summary>
public sealed class BroilerAboutDialog : IDisposable
{
    private readonly StandardDialog _dialog;
    private readonly StandardLabel _productLabel;
    private readonly StandardLabel _versionLabel;
    private readonly StandardLabel _disclosureLabel;
    private readonly StandardLabel _licenseLabel;
    private readonly StandardButton _closeButton;

    public StandardDialog Dialog => _dialog;
    public StandardLabel ProductLabel => _productLabel;
    public StandardLabel VersionLabel => _versionLabel;
    public StandardLabel DisclosureLabel => _disclosureLabel;
    public StandardLabel LicenseLabel => _licenseLabel;
    public StandardButton CloseButton => _closeButton;

    public BroilerAboutDialog()
    {
        _dialog = new StandardDialog
        {
            Title = "About Broiler Fond",
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

        _productLabel = new StandardLabel
        {
            Text = "Broiler Fond — Finance on Demand",
            Wrapping = UiTextWrapping.Wrap,
            AccessibleName = "Product Title"
        };
        content.AddChild(_productLabel);

        _versionLabel = new StandardLabel
        {
            Text = "Version 1.0.0 (net10.0 cross-platform desktop)",
            Wrapping = UiTextWrapping.Wrap,
            AccessibleName = "Product Version"
        };
        content.AddChild(_versionLabel);

        _disclosureLabel = new StandardLabel
        {
            Text = "Privacy: Zero Telemetry · Zero Cloud Analytics · Local Cryptographic Storage (AES-256-GCM / PBKDF2).",
            Wrapping = UiTextWrapping.Wrap,
            AccessibleName = "Privacy Disclosure"
        };
        content.AddChild(_disclosureLabel);

        _licenseLabel = new StandardLabel
        {
            Text = "Compliance: FinTS 3.0/4.1 & EBICS · ISO 20022 SEPA · European Accessibility Act (BFSG / EN 301 549).",
            Wrapping = UiTextWrapping.Wrap,
            AccessibleName = "Legal & Compliance Notice"
        };
        content.AddChild(_licenseLabel);

        panel.AddChild(content);

        _closeButton = new StandardButton
        {
            Text = "Close",
            AccessibleName = "Close Dialog Button"
        };
        _closeButton.Clicked += (_, _) => _dialog.Cancel();

        panel.AddChild(_closeButton);

        DialogLayout.Attach(_dialog, panel);
    }

    public Task<UiDialogResult> ShowModalAsync(UiWindow owner, BRect? placement = null)
    {
        return DialogLayout.ShowModalAsync(_dialog, owner, placement);
    }

    public void Dispose() => _dialog.Dispose();
}
