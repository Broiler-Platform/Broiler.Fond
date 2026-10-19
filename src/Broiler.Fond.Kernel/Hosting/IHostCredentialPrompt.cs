namespace Broiler.Fond.Kernel.Hosting;

/// <summary>
/// Parameters for an interactive host credential request.
/// </summary>
public sealed record CredentialPromptRequest(
    string Title,
    string PromptMessage,
    bool IsSecret,
    TimeSpan Timeout,
    string? InstitutionName = null);

/// <summary>
/// Result of an interactive host credential prompt.
/// </summary>
public sealed record CredentialPromptResponse(
    bool IsSuccess,
    bool IsCancelled,
    string? PlainTextSecret,
    string? ErrorMessage = null);

/// <summary>
/// Host-neutral abstraction for interactive credential prompts.
/// Invariant: Passphrases and PINs acquired through this interface must be moved to
/// volatile zeroized memory immediately and never persisted.
/// </summary>
public interface IHostCredentialPrompt
{
    /// <summary>
    /// Requests a credential interactively from the user on the host UI thread.
    /// </summary>
    ValueTask<CredentialPromptResponse> RequestCredentialAsync(
        CredentialPromptRequest request,
        CancellationToken cancellationToken = default);
}
