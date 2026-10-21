namespace Broiler.Fond.Host.Desktop.Windowing.Windows;

using System;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using Broiler.Graphics;
using Broiler.Graphics.Color;
using Broiler.Graphics.Geometry;
using Broiler.Graphics.Imaging;
using Broiler.Graphics.Rendering;
using Broiler.Graphics.RenderList;
using Broiler.Graphics.Resources;
using Broiler.Graphics.Windowing;
using Broiler.Graphics.Windows;
using Broiler.Input.Keyboard;
using Broiler.UI;
using Broiler.UI.Standard;

/// <summary>
/// Native Direct2D secondary window hosting modal banking dialogs that break out
/// of the main window, matching WriterHostWindow in Broiler Writer.
/// </summary>
[SupportedOSPlatform("windows7.0")]
public sealed class FondHostWindow : Direct2DWindow, IUiHostWindow, IUiWindowChromeHost, IUiClipboardHost
{
    private readonly FondWindowsUiHost _mainHost;
    private UiSession? _session;

#pragma warning disable CS0618
    private readonly StandardLegacyGraphicsInputAdapter _legacyInput = new("broiler-fond-dialog");
#pragma warning restore CS0618

    public FondHostWindow(
        UiHostWindowRequest request,
        BWindow? owner,
        FondWindowsUiHost mainHost)
        : base(new BWindowOptions
        {
            Title = string.IsNullOrWhiteSpace(request.Title) ? "Broiler Fond" : request.Title,
            ClientWidth = ToClientExtent(request.Placement.Width, 640),
            ClientHeight = ToClientExtent(request.Placement.Height, 420),
            Left = request.Placement.IsEmpty ? null : request.Placement.X,
            Top = request.Placement.IsEmpty ? null : request.Placement.Y,
            ClearColor = new BColor(245, 247, 250),
            RenderOptions = new BRenderOptions(Antialias: true, VSync: true, SubpixelText: true),
            OwnsMessageLoop = false,
            Chrome = request.Chrome == UiHostWindowChrome.Owner ? BWindowChrome.Owner : BWindowChrome.System,
            Resizable = request.Resizable,
            Owner = request.IsModal ? owner : null,
        })
    {
        _mainHost = mainHost;
        StateChanged += (_, _) => WindowStateChanged?.Invoke(this, EventArgs.Empty);
    }

    // IUiHost implementation
    BSize IUiHost.ViewportSize => ClientSize;
    double IUiHost.Scale => DpiScale;
    BRenderList IUiHost.CreateRenderList(int capacity) => new(capacity);
    void IUiHost.Invalidate(UiInvalidation invalidation) => InvalidateIfAlive();
    void IUiHost.Present(BRenderList renderList) { }

    // IUiClipboardHost
    public bool TryGetText(out string text) => _mainHost.TryGetText(out text);
    public void SetText(string text) => _mainHost.SetText(text);

    // IUiHostWindow
    public void Bind(UiSession session)
    {
        _session = session ?? throw new ArgumentNullException(nameof(session));
        InvalidateIfAlive();
    }

    public void Activate()
    {
        if (!IsDisposed && NativeHandle != IntPtr.Zero)
        {
            SetForegroundWindow(NativeHandle);
            InvalidateIfAlive();
        }
    }

    // IUiWindowChromeHost
    public event EventHandler? WindowStateChanged;
    UiHostWindowChrome IUiWindowChromeHost.Chrome =>
        Options.Chrome == BWindowChrome.Owner ? UiHostWindowChrome.Owner : UiHostWindowChrome.System;
    bool IUiWindowChromeHost.IsResizable => Options.Resizable;
    UiHostWindowState IUiWindowChromeHost.WindowState => ToHostState(WindowState);
    void IUiWindowChromeHost.SetWindowState(UiHostWindowState state) => SetWindowState(state switch
    {
        UiHostWindowState.Minimized => BWindowState.Minimized,
        UiHostWindowState.Maximized => BWindowState.Maximized,
        _ => BWindowState.Normal,
    });
    void IUiWindowChromeHost.SetIcon(BPixelBuffer? icon) => SetIcon(icon);
    void IUiWindowChromeHost.RequestClose() => Close();
    void IUiWindowChromeHost.BeginMoveDrag() => BeginMoveDrag();
    void IUiWindowChromeHost.BeginResizeDrag(UiWindowEdge edge) => BeginResizeDrag(ToWindowEdge(edge));

    protected override BRenderList? BuildRenderList(BSize clientSize) =>
        _session is { IsDisposed: false } session ? session.RenderFrame() : null;

    protected override void OnResized(BSize clientSize, double dpiScale) => InvalidateIfAlive();
    protected override void OnPointerDown(BPointerEventArgs e) => Dispatch(_legacyInput.FromPointerButton(e));
    protected override void OnPointerMove(BPointerEventArgs e) => Dispatch(_legacyInput.FromPointerMove(e));
    protected override void OnPointerUp(BPointerEventArgs e) => Dispatch(_legacyInput.FromPointerButton(e));
    protected override void OnMouseWheel(BMouseWheelEventArgs e) => Dispatch(_legacyInput.FromMouseWheel(e));
    protected override void OnKeyDown(BKeyEventArgs e) => Dispatch(_legacyInput.FromKey(e, KeyboardKeyTransition.Down));
    protected override void OnKeyUp(BKeyEventArgs e) => Dispatch(_legacyInput.FromKey(e, KeyboardKeyTransition.Up));
    protected override void OnTextInput(BTextInputEventArgs e) => Dispatch(_legacyInput.FromText(e));

    protected override void Dispose(bool disposing)
    {
        if (disposing && !IsDisposed)
        {
            Close();
        }
        base.Dispose(disposing);
    }

    private static UiHostWindowState ToHostState(BWindowState state) => state switch
    {
        BWindowState.Minimized => UiHostWindowState.Minimized,
        BWindowState.Maximized => UiHostWindowState.Maximized,
        _ => UiHostWindowState.Normal,
    };

    private static BWindowEdge ToWindowEdge(UiWindowEdge edge) => edge switch
    {
        UiWindowEdge.Left => BWindowEdge.Left,
        UiWindowEdge.Top => BWindowEdge.Top,
        UiWindowEdge.Right => BWindowEdge.Right,
        UiWindowEdge.Bottom => BWindowEdge.Bottom,
        UiWindowEdge.TopLeft => BWindowEdge.TopLeft,
        UiWindowEdge.TopRight => BWindowEdge.TopRight,
        UiWindowEdge.BottomLeft => BWindowEdge.BottomLeft,
        UiWindowEdge.BottomRight => BWindowEdge.BottomRight,
        _ => BWindowEdge.None,
    };

    private void Dispatch(UiInputEvent input)
    {
        if (_session is null || _session.IsDisposed)
        {
            return;
        }

        if (_session.DispatchInput(input))
        {
            InvalidateIfAlive();
        }
    }

    private void InvalidateIfAlive()
    {
        if (!IsDisposed && NativeHandle != IntPtr.Zero)
        {
            Invalidate();
        }
    }

    private static int ToClientExtent(double requested, int fallback) =>
        requested > 1 ? (int)Math.Round(requested) : fallback;

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetForegroundWindow(IntPtr hwnd);
}
