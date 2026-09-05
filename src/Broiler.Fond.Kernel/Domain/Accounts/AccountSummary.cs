using Broiler.Fond.Kernel.Domain.Identifiers;

namespace Broiler.Fond.Kernel.Domain.Accounts;

/// <summary>
/// Defines the immutable shape required by the future M1 account list. It does
/// not perform discovery, validation, masking, or persistence in Milestone 0.
/// </summary>
public sealed record AccountSummary(
    AccountId Id,
    AccountIncarnationId IncarnationId,
    AccountBindingId BindingId,
    InstitutionId InstitutionId,
    ConnectionId ConnectionId,
    string DisplayName,
    string Currency,
    string? MaskedAccountIdentifier);
