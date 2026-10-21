using Broiler.Graphics.RenderList;

namespace Broiler.Fond.Host.Desktop.Windowing;

/// <summary>
/// Identifies the graphics composition and windowing backend.
/// </summary>
public enum DesktopBackendType
{
    WindowsDxgi = 1,
    LinuxWayland = 2,
    LinuxX11Fallback = 3,
    MacMetalCocoa = 4,
    HeadlessOffscreen = 99
}

/// <summary>
/// Lifecycle state of a desktop application window.
/// </summary>
public enum DesktopWindowState
{
    Normal = 0,
    Minimized = 1,
    Maximized = 2,
    Hidden = 3
}

/// <summary>
/// Contract for platform-specific windowing and swapchain presentation surfaces.
/// Invariant: Pure BCL presentation abstractions with zero third-party UI dependencies.
/// </summary>
public interface IDesktopWindowSurface : IDisposable
{
    /// <summary>Unique identifier of this surface instance.</summary>
    Guid SurfaceId { get; }

    /// <summary>Active composition and windowing backend.</summary>
    DesktopBackendType BackendType { get; }

    /// <summary>Current client surface width in logical pixels.</summary>
    int Width { get; }

    /// <summary>Current client surface height in logical pixels.</summary>
    int Height { get; }

    /// <summary>Display scaling factor (1.0 = 96 DPI / 100%, 2.0 = 200% Retina/High-DPI).</summary>
    double DpiScale { get; }

    /// <summary>Indicates whether the surface is currently mapped and visible.</summary>
    bool IsVisible { get; }

    /// <summary>Current window presentation state.</summary>
    DesktopWindowState WindowState { get; }

    /// <summary>Window title bar text.</summary>
    string Title { get; set; }

    /// <summary>Total number of frames successfully presented to the compositor.</summary>
    long FrameCount { get; }

    /// <summary>Initializes the window surface with dimensions and title.</summary>
    void Initialize(int width = 1280, int height = 800, string title = "Broiler Fond - Finance on Demand");

    /// <summary>Resizes the presentation surface.</summary>
    void Resize(int width, int height);

    /// <summary>Updates display DPI scale.</summary>
    void SetDpiScale(double dpiScale);

    /// <summary>Shows and activates the window surface.</summary>
    void Show();

    /// <summary>Hides the window surface from display and taskbar.</summary>
    void Hide();

    /// <summary>Minimizes the window surface.</summary>
    void Minimize();

    /// <summary>Maximizes the window surface to fill the work area.</summary>
    void Maximize();

    /// <summary>Restores the window surface from minimized or maximized state.</summary>
    void Restore();

    /// <summary>Closes and releases the window surface.</summary>
    void Close();

    /// <summary>Acquires a render list for the next frame.</summary>
    BRenderList BeginDraw();

    /// <summary>Commits and presents the current frame buffer to the compositor.</summary>
    void PresentFrame();

    /// <summary>Raised when window state (Normal, Minimized, Maximized, Hidden) changes.</summary>
    event EventHandler<DesktopWindowState>? StateChanged;

    /// <summary>Raised when client dimensions change.</summary>
    event EventHandler<(int Width, int Height)>? Resized;

    /// <summary>Raised when display DPI scale changes.</summary>
    event EventHandler<double>? DpiChanged;

    /// <summary>Raised when the window surface is activated / receives focus.</summary>
    event EventHandler? Activated;

    /// <summary>Raised when the window surface is deactivated / loses focus.</summary>
    event EventHandler? Deactivated;

    /// <summary>Raised when the window surface is closed.</summary>
    event EventHandler? Closed;

    /// <summary>Raised when a frame has been presented.</summary>
    event EventHandler? FramePresented;
}
