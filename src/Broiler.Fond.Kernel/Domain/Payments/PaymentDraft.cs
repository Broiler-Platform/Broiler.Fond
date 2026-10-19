namespace Broiler.Fond.Kernel.Domain.Payments;

/// <summary>
/// Root aggregate representing a local payment draft.
/// Represents an editable local intent that cannot be mistaken for a submitted bank order.
/// </summary>
public sealed record PaymentDraft(
    ulong Id,
    ulong SourceAccountId,
    ulong SourceAccountBindingId,
    ulong SourceAccountIncarnationId,
    ulong CurrentRevisionId,
    PaymentDraftStatus Status,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);
