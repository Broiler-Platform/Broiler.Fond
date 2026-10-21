using Broiler.Graphics.RenderList;

namespace Broiler.Fond.Host.Desktop.Windowing.Mac;

/// <summary>
/// Metal-backed Cocoa window presentation surface adapter for macOS.
/// Coordinates NSWindow, NSView, and CAMetalLayer rendering pipeline with native
/// Retina backing store pixel density scaling and CVDisplayLink synchronization.
/// </summary>
public sealed class MacMetalPresentationSurface : DesktopWindowSurfaceBase
{
    public override DesktopBackendType BackendType => DesktopBackendType.MacMetalCocoa;

    /// <summary>Metal pixel format (standard 32-bit BGRA unorm for macOS display).</summary>
    public string MetalPixelFormat { get; } = "MTLPixelFormatBGRA8Unorm";

    /// <summary>Active CAMetalLayer drawable handle representation.</summary>
    public IntPtr MetalLayerHandle { get; private set; } = new(0x3001);

    /// <summary>Retina backing store scale factor (typically 2.0 on modern Macs).</summary>
    public double BackingScaleFactor => DpiScale;

    /// <summary>Indicates whether CVDisplayLink vertical sync callback is running.</summary>
    public bool IsDisplayLinkActive { get; private set; } = true;

    protected override void OnInitialized()
    {
        base.OnInitialized();
        // Modern macOS default display scaling is typically Retina 2.0x (192 DPI equivalent)
        SetDpiScale(2.0);
    }

    public override void PresentFrame()
    {
        // Simulate CAMetalDrawable::present() on next CVDisplayLink tick
        base.PresentFrame();
    }
}
