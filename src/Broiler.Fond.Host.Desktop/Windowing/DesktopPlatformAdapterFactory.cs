using System.Runtime.InteropServices;
using Broiler.Fond.Host.Desktop.Windowing.Headless;
using Broiler.Fond.Host.Desktop.Windowing.Linux;
using Broiler.Fond.Host.Desktop.Windowing.Mac;
using Broiler.Fond.Host.Desktop.Windowing.Windows;

namespace Broiler.Fond.Host.Desktop.Windowing;

/// <summary>
/// Factory that creates the appropriate desktop presentation surface adapter for the active host OS.
/// </summary>
public static class DesktopPlatformAdapterFactory
{
    /// <summary>
    /// Creates the native window presentation surface for the current operating system.
    /// </summary>
    /// <param name="forceHeadless">If true, returns a <see cref="HeadlessOffscreenPresentationSurface"/> regardless of OS.</param>
    public static IDesktopWindowSurface CreateSurface(bool forceHeadless = false)
    {
        if (forceHeadless)
        {
            return new HeadlessOffscreenPresentationSurface();
        }

        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            return new WindowsDxgiPresentationSurface();
        }

        if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
        {
            return new MacMetalPresentationSurface();
        }

        if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
        {
            if (LinuxWaylandPresentationSurface.IsWaylandEnvironmentAvailable())
            {
                return new LinuxWaylandPresentationSurface();
            }
            return new LinuxX11FallbackPresentationSurface();
        }

        // Fallback for other / virtualized environments
        return new HeadlessOffscreenPresentationSurface();
    }
}
