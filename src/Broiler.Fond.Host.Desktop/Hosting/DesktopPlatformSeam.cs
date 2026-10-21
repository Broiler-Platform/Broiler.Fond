using System.Runtime.InteropServices;
using Broiler.Fond.Host.Desktop.Integration.Clipboard;
using Broiler.Fond.Kernel.Hosting;
using Broiler.Fond.Presentation.Services;

namespace Broiler.Fond.Host.Desktop.Hosting;

/// <summary>
/// Root cross-platform desktop host seam bridging the presentation layer with the banking kernel.
/// Manages Safe Mode gating, local notifications, secure credential prompts, and clipboard hygiene.
/// Invariant: Pure BCL implementation compliant with ADR 0130.
/// </summary>
public sealed class DesktopPlatformSeam : IHostPlatformSeam
{
    private readonly object _syncLock = new();
    private bool _isSafeModeActive;

    public string HostIdentifier => "Broiler.Fond.Host.Desktop (1.0)";
    public HostPlatformType PlatformType { get; }

    public bool IsSafeModeActive
    {
        get
        {
            lock (_syncLock)
            {
                return _isSafeModeActive;
            }
        }
    }

    public DesktopNotificationChannel Notifications { get; } = new();
    public DesktopCredentialPrompt CredentialPrompt { get; }
    public DesktopClipboardService Clipboard { get; }

    IHostNotificationChannel IHostPlatformSeam.Notifications => Notifications;
    IHostCredentialPrompt IHostPlatformSeam.CredentialPrompt => CredentialPrompt;
    IHostClipboardService IHostPlatformSeam.Clipboard => Clipboard;

    public event EventHandler<SafeModeChangedEventArgs>? SafeModeChanged;

    public DesktopPlatformSeam(IDialogService? dialogService = null, DesktopClipboardService? clipboard = null)
    {
        PlatformType = DetectPlatformType();
        Clipboard = clipboard ?? new DesktopClipboardService();
        CredentialPrompt = new DesktopCredentialPrompt(dialogService);
    }

    public void EnterSafeMode(string reason)
    {
        ArgumentNullException.ThrowIfNull(reason);

        lock (_syncLock)
        {
            if (!_isSafeModeActive)
            {
                _isSafeModeActive = true;
                SafeModeChanged?.Invoke(this, new SafeModeChangedEventArgs(true, reason, DateTimeOffset.UtcNow));
            }
        }
    }

    public void ExitSafeMode()
    {
        lock (_syncLock)
        {
            if (_isSafeModeActive)
            {
                _isSafeModeActive = false;
                SafeModeChanged?.Invoke(this, new SafeModeChangedEventArgs(false, null, DateTimeOffset.UtcNow));
            }
        }
    }

    private static HostPlatformType DetectPlatformType()
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            return HostPlatformType.Windows;
        }
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
        {
            return HostPlatformType.Linux;
        }
        if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
        {
            return HostPlatformType.MacOS;
        }

        return HostPlatformType.Other;
    }
}
