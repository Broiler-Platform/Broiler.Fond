namespace Broiler.Fond.Presentation.Hosting;

using System;
using Broiler.Graphics.Geometry;
using Broiler.Graphics.RenderList;
using Broiler.UI;

/// <summary>
/// Platform-neutral headless UI host for Broiler Fond presentation sessions and automated verification.
/// Invariant: Pure BCL implementation with zero third-party dependencies or platform locks.
/// </summary>
public sealed class PresentationUiHost : IUiHost, IUiClipboardHost
{
    private string _clipboardText = string.Empty;

    public BSize ViewportSize { get; set; } = new(1280, 800);
    public double Scale { get; set; } = 1.0;

    public BRenderList CreateRenderList(int capacity = 0) => new(capacity);

    public void Invalidate(UiInvalidation invalidation)
    {
    }

    public void Present(BRenderList renderList)
    {
    }

    public bool TryGetText(out string text)
    {
        text = _clipboardText;
        return text.Length > 0;
    }

    public void SetText(string text)
    {
        _clipboardText = text ?? string.Empty;
    }

    public void ClearClipboard()
    {
        _clipboardText = string.Empty;
    }
}
