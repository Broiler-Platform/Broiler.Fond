using Broiler.Fond.Kernel.Hosting;

namespace Broiler.Fond.Presentation.Services;

/// <summary>
/// Presentation clipboard abstraction with ephemeral security wiping.
/// </summary>
public interface IClipboardService
{
    ValueTask SetTransientTextAsync(string text, TimeSpan timeout, CancellationToken cancellationToken = default);
    ValueTask<string?> GetTextAsync(CancellationToken cancellationToken = default);
    ValueTask ClearAsync(CancellationToken cancellationToken = default);
}

/// <summary>
/// In-memory headless clipboard service for unit testing and headless CI runs.
/// </summary>
public sealed class HeadlessClipboardService : IClipboardService, IHostClipboardService
{
    private string? _text;

    public ValueTask SetTransientTextAsync(string text, TimeSpan timeout, CancellationToken cancellationToken = default)
    {
        _text = text;
        return ValueTask.CompletedTask;
    }

    public ValueTask<string?> GetTextAsync(CancellationToken cancellationToken = default)
    {
        return ValueTask.FromResult(_text);
    }

    public ValueTask ClearAsync(CancellationToken cancellationToken = default)
    {
        _text = null;
        return ValueTask.CompletedTask;
    }
}
