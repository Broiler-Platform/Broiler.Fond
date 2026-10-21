using Broiler.UI;

namespace Broiler.Fond.Presentation.Services;

/// <summary>Captures bank workflow continuations on the toolkit dispatcher, including profile commits and prompts.</summary>
internal sealed class RefreshUiContext(IUiDispatcher dispatcher) : SynchronizationContext
{
    public override void Post(SendOrPostCallback callback, object? state) => dispatcher.Post(() =>
    {
        var prior = Current; SetSynchronizationContext(this);
        try { callback(state); } finally { SetSynchronizationContext(prior); }
    });
    internal static Task RunAsync(IUiDispatcher? dispatcher, Func<Task> operation)
    {
        if (dispatcher is null) return operation();
        if (!dispatcher.CheckAccess())
        {
            var completed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            dispatcher.Post(async () => { try { await RunAsync(dispatcher, operation); completed.SetResult(); } catch (Exception ex) { completed.SetException(ex); } });
            return completed.Task;
        }
        var prior = Current;
        SetSynchronizationContext(new RefreshUiContext(dispatcher));
        try { return operation(); } finally { SetSynchronizationContext(prior); }
    }
}
