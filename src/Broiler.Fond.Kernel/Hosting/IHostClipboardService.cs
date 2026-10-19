namespace Broiler.Fond.Kernel.Hosting;

/// <summary>
/// Host-neutral clipboard interface enforcing transient data hygiene.
/// Invariant: Secret or sensitive tokens placed on the clipboard must be auto-cleared.
/// </summary>
public interface IHostClipboardService
{
    /// <summary>
    /// Places text onto the host clipboard with an automated expiry deadline after which it is wiped.
    /// </summary>
    ValueTask SetTransientTextAsync(string text, TimeSpan timeout, CancellationToken cancellationToken = default);

    /// <summary>
    /// Immediately clears or zeroes the system clipboard.
    /// </summary>
    ValueTask ClearAsync(CancellationToken cancellationToken = default);
}
