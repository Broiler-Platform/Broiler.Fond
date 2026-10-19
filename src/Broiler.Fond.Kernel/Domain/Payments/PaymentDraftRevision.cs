namespace Broiler.Fond.Kernel.Domain.Payments;

/// <summary>
/// Immutable snapshot revision of a local payment draft capturing the exact payment parameters
/// and provenance at a specific point in time.
/// </summary>
public sealed record PaymentDraftRevision(
    ulong Id,
    ulong DraftId,
    int RevisionNumber,
    ulong? PredecessorRevisionId,
    DateTimeOffset Timestamp,
    ulong SourceAccountId,
    ulong SourceAccountBindingId,
    ulong SourceAccountIncarnationId,
    PaymentBeneficiary Beneficiary,
    Money Amount,
    PaymentRemittance Remittance,
    PaymentExecutionMode ExecutionMode,
    DateOnly ExecutionDate,
    string? CustomerReference = null);
