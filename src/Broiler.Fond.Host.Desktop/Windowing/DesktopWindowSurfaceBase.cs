using Broiler.Graphics.RenderList;

namespace Broiler.Fond.Host.Desktop.Windowing;

/// <summary>
/// Reusable base implementation for desktop window presentation surfaces.
/// Handles lifecycle state transitions, dimension tracking, DPI awareness, and frame counting.
/// </summary>
public abstract class DesktopWindowSurfaceBase : IDesktopWindowSurface
{
    private bool _isDisposed;

    public Guid SurfaceId { get; } = Guid.NewGuid();
    public abstract DesktopBackendType BackendType { get; }

    public int Width { get; protected set; } = 1280;
    public int Height { get; protected set; } = 800;
    public double DpiScale { get; protected set; } = 1.0;
    public bool IsVisible { get; protected set; }
    public DesktopWindowState WindowState { get; protected set; } = DesktopWindowState.Normal;
    public string Title { get; set; } = "Broiler Fond - Finance on Demand";
    public long FrameCount { get; protected set; }

    public event EventHandler<DesktopWindowState>? StateChanged;
    public event EventHandler<(int Width, int Height)>? Resized;
    public event EventHandler<double>? DpiChanged;
    public event EventHandler? Activated;
    public event EventHandler? Deactivated;
    public event EventHandler? Closed;
    public event EventHandler? FramePresented;

    public virtual void Initialize(int width = 1280, int height = 800, string title = "Broiler Fond - Finance on Demand")
    {
        ObjectDisposedException.ThrowIf(_isDisposed, this);
        Width = Math.Max(320, width);
        Height = Math.Max(240, height);
        Title = title;
        OnInitialized();
    }

    protected virtual void OnInitialized() { }

    public virtual void Resize(int width, int height)
    {
        ObjectDisposedException.ThrowIf(_isDisposed, this);
        int newWidth = Math.Max(320, width);
        int newHeight = Math.Max(240, height);

        if (Width != newWidth || Height != newHeight)
        {
            Width = newWidth;
            Height = newHeight;
            Resized?.Invoke(this, (Width, Height));
        }
    }

    public virtual void SetDpiScale(double dpiScale)
    {
        ObjectDisposedException.ThrowIf(_isDisposed, this);
        if (dpiScale > 0 && Math.Abs(DpiScale - dpiScale) > 0.001)
        {
            DpiScale = dpiScale;
            DpiChanged?.Invoke(this, DpiScale);
        }
    }

    public virtual void Show()
    {
        ObjectDisposedException.ThrowIf(_isDisposed, this);
        IsVisible = true;
        if (WindowState == DesktopWindowState.Hidden)
        {
            WindowState = DesktopWindowState.Normal;
            StateChanged?.Invoke(this, WindowState);
        }
        Activated?.Invoke(this, EventArgs.Empty);
    }

    public virtual void Hide()
    {
        ObjectDisposedException.ThrowIf(_isDisposed, this);
        IsVisible = false;
        WindowState = DesktopWindowState.Hidden;
        StateChanged?.Invoke(this, WindowState);
        Deactivated?.Invoke(this, EventArgs.Empty);
    }

    public virtual void Minimize()
    {
        ObjectDisposedException.ThrowIf(_isDisposed, this);
        WindowState = DesktopWindowState.Minimized;
        StateChanged?.Invoke(this, WindowState);
        Deactivated?.Invoke(this, EventArgs.Empty);
    }

    public virtual void Maximize()
    {
        ObjectDisposedException.ThrowIf(_isDisposed, this);
        WindowState = DesktopWindowState.Maximized;
        StateChanged?.Invoke(this, WindowState);
    }

    public virtual void Restore()
    {
        ObjectDisposedException.ThrowIf(_isDisposed, this);
        IsVisible = true;
        WindowState = DesktopWindowState.Normal;
        StateChanged?.Invoke(this, WindowState);
        Activated?.Invoke(this, EventArgs.Empty);
    }

    public virtual void Close()
    {
        if (_isDisposed)
        {
            return;
        }

        IsVisible = false;
        WindowState = DesktopWindowState.Hidden;
        Closed?.Invoke(this, EventArgs.Empty);
        Dispose();
    }

    public virtual BRenderList BeginDraw()
    {
        ObjectDisposedException.ThrowIf(_isDisposed, this);
        return new BRenderList();
    }

    public virtual void PresentFrame()
    {
        ObjectDisposedException.ThrowIf(_isDisposed, this);
        FrameCount++;
        FramePresented?.Invoke(this, EventArgs.Empty);
    }

    public void TriggerActivated() => Activated?.Invoke(this, EventArgs.Empty);
    public void TriggerDeactivated() => Deactivated?.Invoke(this, EventArgs.Empty);

    public void Dispose()
    {
        Dispose(disposing: true);
        GC.SuppressFinalize(this);
    }

    protected virtual void Dispose(bool disposing)
    {
        if (!_isDisposed)
        {
            _isDisposed = true;
        }
    }
}
