namespace Broiler.Fond.Host.Desktop.Windowing.Windows;

using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using Broiler.Graphics;
using Broiler.Graphics.Geometry;
using Broiler.Graphics.RenderList;
using Broiler.Graphics.Windows;
using Broiler.UI;

/// <summary>
/// Direct2D Windows UI host providing IUiHost, IUiClipboardHost, and IUiWindowHost
/// capabilities, matching WriterWindowsUiHost in Broiler Writer.
/// </summary>
[SupportedOSPlatform("windows7.0")]
public sealed class FondWindowsUiHost : IUiHost, IUiClipboardHost, IUiWindowHost, IDisposable
{
    private readonly Direct2DWindow _ownerWindow;
    private readonly Func<BSize> _getViewportSize;
    private readonly Func<double> _getScale;
    private readonly Action _invalidate;
    private readonly Func<string?>? _getClipboardText;
    private readonly Action<string>? _setClipboardText;
    private readonly List<FondHostWindow> _hostWindows = [];

    public BSize ViewportSize => _getViewportSize();
    public double Scale => _getScale();

    public FondWindowsUiHost(
        Direct2DWindow ownerWindow,
        Func<BSize> getViewportSize,
        Func<double> getScale,
        Action invalidate,
        Func<string?>? getClipboardText = null,
        Action<string>? setClipboardText = null)
    {
        _ownerWindow = ownerWindow ?? throw new ArgumentNullException(nameof(ownerWindow));
        _getViewportSize = getViewportSize ?? throw new ArgumentNullException(nameof(getViewportSize));
        _getScale = getScale ?? throw new ArgumentNullException(nameof(getScale));
        _invalidate = invalidate ?? throw new ArgumentNullException(nameof(invalidate));
        _getClipboardText = getClipboardText;
        _setClipboardText = setClipboardText;
    }

    public BRenderList CreateRenderList(int capacity = 0) => new(capacity);

    public void Invalidate(UiInvalidation invalidation) => _invalidate();

    public void Present(BRenderList renderList)
    {
    }

    public bool TryGetText(out string text)
    {
        text = _getClipboardText?.Invoke() ?? string.Empty;
        return text.Length > 0;
    }

    public void SetText(string text) => _setClipboardText?.Invoke(text ?? string.Empty);

    public IUiHostWindow CreateHostWindow(UiHostWindowRequest request)
    {
        var hostWindow = new FondHostWindow(ToScreenPlacement(request), _ownerWindow, this);
        _hostWindows.Add(hostWindow);
        hostWindow.Closed += (_, _) => _hostWindows.Remove(hostWindow);
        hostWindow.Show();
        return hostWindow;
    }

    private UiHostWindowRequest ToScreenPlacement(UiHostWindowRequest request)
    {
        if (request.Placement.IsEmpty)
        {
            return request;
        }

        IntPtr ownerHandle = _ownerWindow.NativeHandle;
        if (ownerHandle == IntPtr.Zero)
        {
            return request;
        }

        var origin = default(NativePoint);
        if (!ClientToScreen(ownerHandle, ref origin))
        {
            return request;
        }

        double scale = Scale > 0 ? Scale : 1;
        BRect placement = request.Placement;
        return request with
        {
            Placement = new BRect(
                (origin.X / scale) + placement.X,
                (origin.Y / scale) + placement.Y,
                placement.Width,
                placement.Height),
        };
    }

    public void Dispose()
    {
        foreach (var window in _hostWindows.ToArray())
        {
            try { window.Dispose(); } catch { }
        }
        _hostWindows.Clear();
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct NativePoint
    {
        public int X;
        public int Y;
    }

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool ClientToScreen(IntPtr hWnd, ref NativePoint lpPoint);
}
