using Broiler.Fond.Kernel.Hosting;
using Broiler.Fond.Presentation.Services;
using KernelCredentialPromptRequest = Broiler.Fond.Kernel.Hosting.CredentialPromptRequest;
using KernelCredentialPromptResponse = Broiler.Fond.Kernel.Hosting.CredentialPromptResponse;
using PresCredentialPromptRequest = Broiler.Fond.Presentation.Services.CredentialPromptRequest;

namespace Broiler.Fond.Host.Desktop.Hosting;

/// <summary>
/// Desktop implementation of interactive credential acquisition.
/// Integrates with <see cref="IDialogService"/> and secure text entry to ensure PINs/TANs are captured
/// into pinned unmanaged buffers with deterministic wiping per ADR 0130.
/// </summary>
public sealed class DesktopCredentialPrompt : IHostCredentialPrompt
{
    private readonly IDialogService? _dialogService;
    private Func<KernelCredentialPromptRequest, ValueTask<KernelCredentialPromptResponse>>? _headlessOverride;

    public DesktopCredentialPrompt(IDialogService? dialogService = null)
    {
        _dialogService = dialogService;
    }

    /// <summary>
    /// Configures a test/mock handler for headless automation or CI runs.
    /// </summary>
    public void SetHeadlessHandler(Func<KernelCredentialPromptRequest, ValueTask<KernelCredentialPromptResponse>> handler)
    {
        _headlessOverride = handler;
    }

    public async ValueTask<KernelCredentialPromptResponse> RequestCredentialAsync(
        KernelCredentialPromptRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (_headlessOverride != null)
        {
            return await _headlessOverride(request).ConfigureAwait(false);
        }

        // If no dialog service is wired or running in non-interactive environment
        if (_dialogService == null)
        {
            return new KernelCredentialPromptResponse(
                IsSuccess: false,
                IsCancelled: true,
                PlainTextSecret: null,
                ErrorMessage: "No interactive dialog service is configured on this desktop host.");
        }

        var presRequest = new PresCredentialPromptRequest(
            Title: request.Title,
            PromptMessage: request.PromptMessage,
            ConnectionName: request.InstitutionName ?? "Bank Connection",
            IsTan: !request.IsSecret);

        var result = await _dialogService.ShowCredentialPromptAsync(presRequest, cancellationToken).ConfigureAwait(false);

        return new KernelCredentialPromptResponse(
            IsSuccess: result.Succeeded,
            IsCancelled: result.Cancelled,
            PlainTextSecret: result.Credential);
    }
}
