namespace Broiler.Fond.Kernel.Domain.Institutions;

/// <summary>
/// Captures user-entered institution metadata. HTTPS and bank-ownership
/// verification are use-case responsibilities introduced after Milestone 0.
/// </summary>
public sealed record ManualInstitutionConfiguration(
    string DisplayName,
    Uri FinTsEndpoint,
    string? BankCode,
    string? Bic);
