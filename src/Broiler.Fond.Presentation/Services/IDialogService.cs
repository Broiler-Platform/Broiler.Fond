namespace Broiler.Fond.Presentation.Services;

/// <summary>
/// Result of an interactive profile unlock request.
/// </summary>
public sealed record ProfileUnlockResult(bool Succeeded, string? Passphrase, bool Cancelled);

/// <summary>
/// Request parameters for prompting the user for an ephemeral banking credential (PIN/TAN).
/// </summary>
public sealed record CredentialPromptRequest(
    string Title,
    string PromptMessage,
    string ConnectionName,
    bool IsTan = false,
    string? TanProcedureName = null);

/// <summary>
/// Result of prompting for an ephemeral credential.
/// Legacy string result: immutable managed copies cannot be deterministically erased.
/// New bank operations use IBankCredentialPrompt.
/// </summary>
public sealed record CredentialPromptResult(bool Succeeded, string? Credential, bool Cancelled);

/// <summary>
/// Request parameters for interactive review of a prepared SEPA payment draft before submission.
/// </summary>
public sealed record PaymentReviewRequest(
    ulong DraftId,
    string BeneficiaryName,
    string BeneficiaryIban,
    decimal Amount,
    string Currency,
    string SourceAccountDisplay,
    string ExecutionMode,
    string? RemittanceText,
    string? VoPStatus = null,
    string? BankWarning = null);

/// <summary>
/// Result of reviewing a payment draft.
/// </summary>
public sealed record PaymentReviewResult(bool Confirmed, bool Cancelled);

/// <summary>
/// Request parameters for manual bank institution endpoint setup.
/// </summary>
public sealed record InstitutionSetupRequest(
    string InitialName = "",
    string InitialBankCode = "",
    string InitialEndpoint = "",
    string InitialUserId = "",
    string? InitialCustomerId = null);

/// <summary>
/// Result of manual institution setup.
/// </summary>
public sealed record InstitutionSetupResult(
    bool Succeeded,
    string DisplayName,
    string BankCode,
    string EndpointUri,
    bool Cancelled,
    string UserId = "",
    string? CustomerId = null);

/// <summary>
/// Platform-agnostic dialog service abstraction allowing view models and presentation coordinators
/// to request interactive modal workflows without direct windowing or UI toolkit dependencies.
/// </summary>
public interface IDialogService
{
    Task<bool> ShowEndpointConfirmationAsync(string endpoint, CancellationToken cancellationToken = default);
    Task<AccountSetupResult> ShowAccountSetupAsync(
        AccountSetupRequest request,
        CancellationToken cancellationToken = default);

    Task<bool> ShowConfirmationAsync(
        string title,
        string message,
        string? confirmLabel = null,
        string? cancelLabel = null,
        CancellationToken cancellationToken = default);

    Task ShowAlertAsync(
        string title,
        string message,
        CancellationToken cancellationToken = default);

    Task<ProfileUnlockResult> ShowProfileUnlockAsync(
        string profileDirectory,
        CancellationToken cancellationToken = default);

    Task<CredentialPromptResult> ShowCredentialPromptAsync(
        CredentialPromptRequest request,
        CancellationToken cancellationToken = default);

    Task<PaymentReviewResult> ShowPaymentReviewAsync(
        PaymentReviewRequest request,
        CancellationToken cancellationToken = default);

    Task<InstitutionSetupResult> ShowInstitutionSetupAsync(
        InstitutionSetupRequest request,
        CancellationToken cancellationToken = default);
}
