namespace Broiler.Fond.Host.Desktop.Windowing.Headless;

using System;
using Broiler.Graphics.RenderList;

/// <summary>
/// Headless in-memory offscreen presentation surface for automated CI testing and verification.
/// Dispatches rendering commands to <see cref="BRenderList"/> and records frame metrics.
/// </summary>
public sealed class HeadlessOffscreenPresentationSurface : DesktopWindowSurfaceBase
{
    public override DesktopBackendType BackendType => DesktopBackendType.HeadlessOffscreen;

    /// <summary>UTC timestamp of the most recent presented frame.</summary>
    public DateTimeOffset? LastPresentedFrameTimestamp { get; private set; }

    /// <summary>Snapshot of the render list from the last presented frame.</summary>
    public BRenderList? LastPresentedRenderList { get; private set; }

    public override void PresentFrame()
    {
        LastPresentedFrameTimestamp = DateTimeOffset.UtcNow;
        LastPresentedRenderList = new BRenderList();
        base.PresentFrame();
    }
}
