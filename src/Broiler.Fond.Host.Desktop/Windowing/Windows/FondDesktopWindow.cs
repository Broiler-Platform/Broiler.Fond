namespace Broiler.Fond.Host.Desktop.Windowing.Windows;

using System;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using Broiler.Fond.Presentation.Services;
using Broiler.Fond.Presentation.Sessions;
using Broiler.Fond.Presentation.Views;
using Broiler.Graphics;
using Broiler.Graphics.Color;
using Broiler.Graphics.Geometry;
using Broiler.Graphics.Rendering;
using Broiler.Graphics.RenderList;
using Broiler.Graphics.Windowing;
using Broiler.Graphics.Windows;
using Broiler.Hosting.Windows;
using Broiler.Input.Keyboard;
using Broiler.UI;
using Broiler.UI.Standard;

/// <summary>
/// Primary Direct2D/Win32 application window for Broiler Fond, matching WriterWindow in Broiler Writer.
/// Coordinates GPU-accelerated rendering, input routing, modal dialog host windows, and Win32 message loop.
/// </summary>
[SupportedOSPlatform("windows7.0")]
public sealed class FondDesktopWindow : Direct2DWindow
{
    private readonly FondWindowsUiHost _host;
    private readonly UiSession _session;
    private readonly StandardQueuedUiDispatcher _dispatcher;
    private bool _uiDisposed;
    private readonly HostSession _bankingSession;
    private readonly BroilerDialogService _dialogService;
    private readonly BroilerMainWindow _workspace;
    private WindowsClipboard? _clipboard;
    private FondWindowCloseGuard? _closeGuard;

#pragma warning disable CS0618
    private readonly StandardLegacyGraphicsInputAdapter _legacyInput = new("broiler-fond");
#pragma warning restore CS0618

    public UiSession Session => _session;
    public HostSession BankingSession => _bankingSession;
    public BroilerMainWindow Workspace => _workspace;

    public FondDesktopWindow(HostSession? session = null, BroilerDialogService? dialogService = null)
        : base(new BWindowOptions
        {
            Title = "Broiler Fond - Finance on Demand",
            ClientWidth = 1280,
            ClientHeight = 800,
            ClearColor = new BColor(245, 247, 250),
            RenderOptions = new BRenderOptions(Antialias: true, VSync: true, SubpixelText: true),
        })
    {
        _bankingSession = session ?? new HostSession(BankProductConfiguration.FromSettings(
            Environment.GetEnvironmentVariable("BROILER_FOND_FINTS_PRODUCT_ID"),
            Environment.GetEnvironmentVariable("BROILER_FOND_FINTS_PRODUCT_VERSION")));
        _dialogService = dialogService ?? new BroilerDialogService();

        _host = new FondWindowsUiHost(
            this,
            () => ClientSize,
            () => DpiScale,
            Invalidate,
            ReadClipboardText,
            WriteClipboardText);

        _dispatcher = new StandardQueuedUiDispatcher(() => PostToUiThread(DrainDispatcher));
        _session = new StandardUiSessionBuilder().WithDispatcher(_dispatcher).Build(_host);

        _workspace = new BroilerMainWindow(_bankingSession, _dialogService);
        _dialogService.WindowAccessor = () => _workspace.Window;
        _workspace.Attach(_session);
    }

    protected override void OnCreated()
    {
        _clipboard = new WindowsClipboard(NativeHandle);
        _closeGuard = new FondWindowCloseGuard(NativeHandle, OnRequestClose);
        SetTitle("Broiler Fond - Finance on Demand");
    }

    private void OnRequestClose()
    {
        CloseNativeWindow();
    }

    private void CloseNativeWindow()
    {
        if (!PostToUiThread(CloseNativeWindowNow))
        {
            CloseNativeWindowNow();
        }
    }

    private void CloseNativeWindowNow()
    {
        if (NativeHandle != IntPtr.Zero)
        {
            _ = DestroyWindow(NativeHandle);
        }
    }

    private string? ReadClipboardText() =>
        _clipboard is not null && _clipboard.TryGetText(out string text) ? text : null;

    private void WriteClipboardText(string text) => _clipboard?.SetText(text);

    protected override BRenderList? BuildRenderList(BSize clientSize)
    {
        DrainDispatcher();
        return _session.RenderFrame();
    }
    private void DrainDispatcher()
    {
        if (_uiDisposed || !_dispatcher.CheckAccess()) return;
        try { _dispatcher.Drain(); }
        catch (Exception) { /* Do not log arbitrary callback text or abandon later queued callbacks. */ }
    }

    protected override void OnResized(BSize clientSize, double dpiScale) => Invalidate();

    protected override void OnPointerDown(BPointerEventArgs e) => Dispatch(_legacyInput.FromPointerButton(e));

    protected override void OnPointerMove(BPointerEventArgs e) => Dispatch(_legacyInput.FromPointerMove(e));

    protected override void OnPointerUp(BPointerEventArgs e) => Dispatch(_legacyInput.FromPointerButton(e));

    protected override void OnMouseWheel(BMouseWheelEventArgs e) => Dispatch(_legacyInput.FromMouseWheel(e));

    protected override void OnKeyDown(BKeyEventArgs e) => Dispatch(_legacyInput.FromKey(e, KeyboardKeyTransition.Down));

    protected override void OnKeyUp(BKeyEventArgs e) => Dispatch(_legacyInput.FromKey(e, KeyboardKeyTransition.Up));

    protected override void OnTextInput(BTextInputEventArgs e) => Dispatch(_legacyInput.FromText(e));

    private void Dispatch(UiInputEvent input)
    {
        if (_session.DispatchInput(input))
        {
            Invalidate();
        }
    }

    protected override void OnNativeWindowMessage(IntPtr hwnd, uint message, IntPtr wParam, IntPtr lParam)
    {
        base.OnNativeWindowMessage(hwnd, message, wParam, lParam);
        if (hwnd == NativeHandle)
        {
            WindowsWindowSizing.OnMessage(hwnd, message, lParam, DpiScale);
            // Transfer Win32 focus from the top-level frame to the render child window
            // on activation and focus so keystrokes are never dropped.
            if ((message == 0x0006 && wParam.ToInt64() != 0) || message == 0x0007)
            {
                if (RenderNativeHandle != IntPtr.Zero)
                {
                    _ = SetFocus(RenderNativeHandle);
                }
            }
        }
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _closeGuard?.Dispose();
            _workspace.Dispose();
            DrainDispatcher();
            _uiDisposed = true;
            _session.Dispose();
            _host.Dispose();
        }
        base.Dispose(disposing);
    }

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DestroyWindow(IntPtr hwnd);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr SetFocus(IntPtr hWnd);
}
