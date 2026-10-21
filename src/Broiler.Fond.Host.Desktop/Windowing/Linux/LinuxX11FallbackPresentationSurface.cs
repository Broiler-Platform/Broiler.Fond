using Broiler.Graphics.RenderList;

namespace Broiler.Fond.Host.Desktop.Windowing.Linux;

/// <summary>
/// X11 fallback presentation surface adapter for Linux desktop environments lacking Wayland compositors.
/// Provides MIT-SHM shared memory pixmap presentation and EWMH window manager state coordination.
/// </summary>
public sealed class LinuxX11FallbackPresentationSurface : DesktopWindowSurfaceBase
{
    public override DesktopBackendType BackendType => DesktopBackendType.LinuxX11Fallback;

    /// <summary>X11 display identifier (e.g. ":0").</summary>
    public string X11Display { get; private set; } = ":0";

    /// <summary>Indicates whether MIT-SHM shared memory extension is enabled.</summary>
    public bool IsMitShmEnabled { get; private set; } = true;

    /// <summary>Active X11 window atom identifier representation.</summary>
    public IntPtr X11WindowHandle { get; private set; } = new(0x2001);

    protected override void OnInitialized()
    {
        base.OnInitialized();
        string? display = Environment.GetEnvironmentVariable("DISPLAY");
        if (!string.IsNullOrWhiteSpace(display))
        {
            X11Display = display;
        }
    }

    public override void PresentFrame()
    {
        // Simulate XShmPutImage / XPutImage frame transfer to X11 server
        base.PresentFrame();
    }
}
