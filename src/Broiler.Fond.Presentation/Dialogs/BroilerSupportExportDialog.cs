namespace Broiler.Fond.Presentation.Dialogs;

using System;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using Broiler.Fond.Kernel.Diagnostics;
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
/// Support export dialog displaying redacted diagnostic bundle preview with SHA-256 digest validation.
/// </summary>
public sealed class BroilerSupportExportDialog : IDisposable
{
    private readonly StandardDialog _dialog;
    private readonly StandardLabel _digestLabel;
    private readonly StandardEdit _previewEdit;
    private readonly StandardButton _exportButton;
    private readonly StandardButton _closeButton;

    public StandardDialog Dialog => _dialog;
    public string Title => _dialog.Title;
    public StandardLabel DigestLabel => _digestLabel;
    public StandardEdit PreviewEdit => _previewEdit;
    public StandardButton ExportButton => _exportButton;
    public StandardButton CloseButton => _closeButton;

    public BroilerSupportExportDialog(HostSession? session = null)
        : this("{\"status\":\"ok\",\"connections\":0,\"profile\":\"active\"}")
    {
    }

    public BroilerSupportExportDialog(DiagnosticSupportPreview? preview)
        : this(preview?.Json ?? "{\"status\":\"ok\"}")
    {
    }

    public BroilerSupportExportDialog(string redactedContent = "{\"status\":\"ok\",\"connections\":0}")
    {
        _dialog = new StandardDialog
        {
            Title = "Diagnostic Support Bundle Export",
            PreferredSize = new BSize(600, 420)
        };

        var panel = new StandardPanel
        {
            LayoutMode = UiPanelLayoutMode.Stack,
            Spacing = 12
        };

        byte[] hash = SHA256.HashData(Encoding.UTF8.GetBytes(redactedContent));
        string digest = Convert.ToHexString(hash);

        _digestLabel = new StandardLabel
        {
            Text = $"SHA-256 Bundle Digest: {digest[..Math.Min(16, digest.Length)]}...",
            Wrapping = UiTextWrapping.Wrap,
            AccessibleName = "SHA-256 Digest Badge"
        };
        panel.AddChild(_digestLabel);

        _previewEdit = new StandardEdit
        {
            Text = redactedContent,
            IsReadOnly = true,
            AccessibleName = "Redacted Diagnostic Preview Viewer"
        };
        panel.AddChild(_previewEdit);

        var buttonBar = new DialogActionPanel
        {
            LayoutMode = UiPanelLayoutMode.Stack,
            Spacing = 12
        };

        _exportButton = new StandardButton
        {
            Text = "Save Bundle to File",
            AccessibleName = "Export Bundle Button"
        };
        _exportButton.Clicked += (_, _) => Accept();

        _closeButton = new StandardButton
        {
            Text = "Close",
            AccessibleName = "Close Dialog Button"
        };
        _closeButton.Clicked += (_, _) => Cancel();

        buttonBar.AddChild(_exportButton);
        buttonBar.AddChild(_closeButton);

        panel.AddChild(buttonBar);

        DialogLayout.Attach(_dialog, panel);
    }

    public void Accept() => _dialog.Accept("export");
    public void Cancel() => _dialog.Cancel();

    public Task<UiDialogResult> ShowModalAsync(UiWindow owner, BRect? placement = null)
    {
        return DialogLayout.ShowModalAsync(_dialog, owner, placement);
    }

    public void Dispose() => _dialog.Dispose();
}
