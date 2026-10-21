using System.Runtime.InteropServices;
using Broiler.Fond.Kernel.Hosting;
using Broiler.Fond.Presentation.Services;

namespace Broiler.Fond.Host.Desktop.Integration.Clipboard;

/// <summary>
/// Platform-aware desktop clipboard service implementing ephemeral security hygiene.
/// Tokens placed on the clipboard are automatically wiped upon timeout expiry per ADR 0130.
/// Invariant: Pure BCL implementation with safe platform P/Invoke fallbacks; zero third-party dependencies.
/// </summary>
public sealed class DesktopClipboardService : IHostClipboardService, IClipboardService, IDisposable
{
    private const uint CfUnicodetext = 13;
    private const uint GmemMoveable = 0x0002;

    private readonly object _syncLock = new();
    private string? _inMemoryClipboard;
    private CancellationTokenSource? _activeTimerCts;
    private bool _isDisposed;

    /// <summary>
    /// Total number of transient clipboard operations executed.
    /// </summary>
    public long TransientSetCount { get; private set; }

    /// <summary>
    /// Total number of automated timed wipes executed.
    /// </summary>
    public long TimedWipeCount { get; private set; }

    public async ValueTask SetTransientTextAsync(string text, TimeSpan timeout, CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_isDisposed, this);
        ArgumentNullException.ThrowIfNull(text);

        lock (_syncLock)
        {
            _activeTimerCts?.Cancel();
            _activeTimerCts?.Dispose();
            _activeTimerCts = new CancellationTokenSource();

            _inMemoryClipboard = text;
            TransientSetCount++;

            // Set OS clipboard if running on Windows
            if (OperatingSystem.IsWindows())
            {
                TrySetWindowsClipboard(text);
            }
        }

        // Schedule automated timed wipe
        var cts = _activeTimerCts;
        _ = Task.Run(async () =>
        {
            try
            {
                await Task.Delay(timeout, cts.Token).ConfigureAwait(false);

                lock (_syncLock)
                {
                    // Verify clipboard has not been overwritten by another operation
                    if (string.Equals(_inMemoryClipboard, text, StringComparison.Ordinal))
                    {
                        _inMemoryClipboard = null;
                        if (OperatingSystem.IsWindows())
                        {
                            TryClearWindowsClipboard();
                        }
                        TimedWipeCount++;
                    }
                }
            }
            catch (OperationCanceledException)
            {
                // Expected when a subsequent clipboard operation resets the timer
            }
            catch
            {
                // Best effort on background timed wipe
            }
        }, CancellationToken.None);

        await ValueTask.CompletedTask;
    }

    public ValueTask<string?> GetTextAsync(CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_isDisposed, this);

        lock (_syncLock)
        {
            if (OperatingSystem.IsWindows())
            {
                string? winText = TryGetWindowsClipboard();
                if (winText != null)
                {
                    return ValueTask.FromResult<string?>(winText);
                }
            }

            return ValueTask.FromResult(_inMemoryClipboard);
        }
    }

    public ValueTask ClearAsync(CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_isDisposed, this);

        lock (_syncLock)
        {
            _activeTimerCts?.Cancel();
            _activeTimerCts?.Dispose();
            _activeTimerCts = null;

            _inMemoryClipboard = null;

            if (OperatingSystem.IsWindows())
            {
                TryClearWindowsClipboard();
            }
        }

        return ValueTask.CompletedTask;
    }

    private static void TrySetWindowsClipboard(string text)
    {
        try
        {
            if (!OpenClipboard(IntPtr.Zero))
            {
                return;
            }

            try
            {
                EmptyClipboard();
                int byteCount = (text.Length + 1) * sizeof(char);
                IntPtr hGlobal = GlobalAlloc(GmemMoveable, (UIntPtr)byteCount);
                if (hGlobal == IntPtr.Zero)
                {
                    return;
                }

                IntPtr target = GlobalLock(hGlobal);
                if (target == IntPtr.Zero)
                {
                    GlobalFree(hGlobal);
                    return;
                }

                try
                {
                    Marshal.Copy(text.ToCharArray(), 0, target, text.Length);
                    Marshal.WriteInt16(target + (text.Length * sizeof(char)), 0); // null terminator
                }
                finally
                {
                    GlobalUnlock(hGlobal);
                }

                SetClipboardData(CfUnicodetext, hGlobal);
            }
            finally
            {
                CloseClipboard();
            }
        }
        catch
        {
            // Fallback to in-memory store
        }
    }

    private static string? TryGetWindowsClipboard()
    {
        try
        {
            if (!OpenClipboard(IntPtr.Zero))
            {
                return null;
            }

            try
            {
                IntPtr hData = GetClipboardData(CfUnicodetext);
                if (hData == IntPtr.Zero)
                {
                    return null;
                }

                IntPtr pText = GlobalLock(hData);
                if (pText == IntPtr.Zero)
                {
                    return null;
                }

                try
                {
                    return Marshal.PtrToStringUni(pText);
                }
                finally
                {
                    GlobalUnlock(hData);
                }
            }
            finally
            {
                CloseClipboard();
            }
        }
        catch
        {
            return null;
        }
    }

    private static void TryClearWindowsClipboard()
    {
        try
        {
            if (OpenClipboard(IntPtr.Zero))
            {
                try
                {
                    EmptyClipboard();
                }
                finally
                {
                    CloseClipboard();
                }
            }
        }
        catch
        {
            // Best effort
        }
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool OpenClipboard(IntPtr hWndNewOwner);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool CloseClipboard();

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool EmptyClipboard();

    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr SetClipboardData(uint uFormat, IntPtr hMem);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr GetClipboardData(uint uFormat);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr GlobalAlloc(uint uFlags, UIntPtr dwBytes);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr GlobalLock(IntPtr hMem);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool GlobalUnlock(IntPtr hMem);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr GlobalFree(IntPtr hMem);

    public void Dispose()
    {
        if (!_isDisposed)
        {
            _isDisposed = true;
            _activeTimerCts?.Cancel();
            _activeTimerCts?.Dispose();
            _activeTimerCts = null;
            _inMemoryClipboard = null;
        }
    }
}
