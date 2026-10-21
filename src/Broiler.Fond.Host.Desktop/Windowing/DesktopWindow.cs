namespace Broiler.Fond.Host.Desktop.Windowing;

using System;
using Broiler.Fond.Presentation.Hosting;
using Broiler.Fond.Presentation.Views;
using Broiler.Graphics.Geometry;
using Broiler.Graphics.RenderList;
using Broiler.UI;
using Broiler.UI.Standard;

/// <summary>
/// Top-level desktop window hosting a <see cref="BroilerMainWindow"/> shell inside a <see cref="UiSession"/>.
/// Coordinates two-pass layout, frame presentation via <see cref="IDesktopWindowSurface"/>,
/// and window lifecycle events.
/// </summary>
public sealed class DesktopWindow : IDisposable
{
    private readonly PresentationUiHost _host;
    private readonly UiSession _session;
    private readonly DesktopFocusManager _focusManager;
    private BroilerMainWindow? _content;
    private bool _isDisposed;

    public IDesktopWindowSurface Surface { get; }
    public UiSession Session => _session;
    public PresentationUiHost Host => _host;
    public DesktopFocusManager FocusManager => _focusManager;

    /// <summary>
    /// When true, attempting to close the window minimizes it to the system tray instead.
    /// </summary>
    public bool MinimizeToTrayOnClose { get; set; }

    public string Title
    {
        get => Surface.Title;
        set => Surface.Title = value;
    }

    public int Width => Surface.Width;
    public int Height => Surface.Height;
    public double DpiScale => Surface.DpiScale;
    public bool IsVisible => Surface.IsVisible;
    public DesktopWindowState WindowState => Surface.WindowState;

    public event EventHandler? Closing;
    public event EventHandler? Closed;

    public BroilerMainWindow? Content
    {
        get => _content;
        set
        {
            if (!ReferenceEquals(_content, value))
            {
                _content = value;
                if (_content != null)
                {
                    _content.Attach(_session);
                }
                InvalidateVisual();
            }
        }
    }

    public DesktopWindow(IDesktopWindowSurface? surface = null)
    {
        Surface = surface ?? DesktopPlatformAdapterFactory.CreateSurface();
        Surface.Resized += OnSurfaceResized;
        Surface.DpiChanged += OnSurfaceDpiChanged;
        Surface.Closed += OnSurfaceClosed;

        _host = new PresentationUiHost
        {
            ViewportSize = new BSize(Surface.Width, Surface.Height),
            Scale = Surface.DpiScale
        };

        _session = new StandardUiSessionBuilder().Build(_host);
        _focusManager = new DesktopFocusManager(_session);
    }

    public void Show()
    {
        Surface.Show();
        InvalidateVisual();
    }

    public void Hide() => Surface.Hide();
    public void Minimize() => Surface.Minimize();

    public void Maximize()
    {
        Surface.Maximize();
        InvalidateVisual();
    }

    public void Restore()
    {
        Surface.Restore();
        InvalidateVisual();
    }

    public void Close()
    {
        if (MinimizeToTrayOnClose)
        {
            Minimize();
            return;
        }

        Closing?.Invoke(this, EventArgs.Empty);
        Surface.Close();
    }

    /// <summary>
    /// Triggers layout and frame presentation via the underlying presentation surface.
    /// </summary>
    public void InvalidateVisual()
    {
        if (_content == null || !Surface.IsVisible || Surface.WindowState == DesktopWindowState.Minimized)
        {
            return;
        }

        _host.ViewportSize = new BSize(Surface.Width, Surface.Height);
        _host.Scale = Surface.DpiScale;

        // Render frame through the UI session
        _session.RenderFrame();

        // Present frame on the surface
        Surface.BeginDraw();
        Surface.PresentFrame();
    }

    /// <summary>
    /// Dispatches a key down event to the desktop window focus and navigation system.
    /// </summary>
    public bool DispatchKeyDown(KeyEventArgs e)
    {
        if (e.Key == Key.Tab)
        {
            if (_content != null)
            {
                _session.SetFocus(_content.SyncAllButton);
            }
            e.Handled = true;
            InvalidateVisual();
            return true;
        }

        return false;
    }

    private void OnSurfaceResized(object? sender, (int Width, int Height) e) => InvalidateVisual();
    private void OnSurfaceDpiChanged(object? sender, double dpiScale) => InvalidateVisual();
    private void OnSurfaceClosed(object? sender, EventArgs e) => Closed?.Invoke(this, EventArgs.Empty);

    public void Dispose()
    {
        if (!_isDisposed)
        {
            _isDisposed = true;
            Surface.Resized -= OnSurfaceResized;
            Surface.DpiChanged -= OnSurfaceDpiChanged;
            Surface.Closed -= OnSurfaceClosed;
            Surface.Dispose();
            _session.Dispose();
        }
    }
}

/// <summary>
/// Supported keyboard keys for desktop window input dispatch.
/// </summary>
public enum Key
{
    None = 0,
    Tab,
    Enter,
    Escape,
    Space,
    Up,
    Down,
    Left,
    Right
}

/// <summary>
/// Event arguments for key down/up events dispatched to the desktop window.
/// </summary>
public sealed class KeyEventArgs(Key key) : EventArgs
{
    public Key Key { get; } = key;
    public bool Handled { get; set; }
}

/// <summary>
/// Focus manager providing inquiry into the currently focused control in the UI session.
/// </summary>
public sealed class DesktopFocusManager(UiSession session)
{
    private readonly UiSession _session = session;

    public object? FocusedControl => _session.FocusedElement;
}
