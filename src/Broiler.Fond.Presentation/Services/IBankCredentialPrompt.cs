using Broiler.Fond.Kernel.FinTs;

namespace Broiler.Fond.Presentation.Services;

/// <summary>A null result means cancellation. Ownership of a returned credential passes to the caller.
/// Implementations must honor cancellation and must not retain or log credentials.</summary>
public interface IBankCredentialPrompt
{
    Task<FinTsSessionCredential?> AcquireBankCredentialAsync(CredentialPromptRequest request,
        CancellationToken cancellationToken = default);
}
