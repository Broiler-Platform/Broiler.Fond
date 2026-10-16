using Broiler.Fond.Kernel.Domain.Identifiers;

namespace Broiler.Fond.Kernel.Domain.Transactions;

/// <summary>
/// Captures raw source wire evidence for a transaction observation.
/// </summary>
public sealed class TransactionObservation
{
    public const int MaximumFormatLength = 32;
    public const int MaximumReferenceLength = 128;

    public TransactionObservation(
        ObservationId id,
        AccountBindingId accountBindingId,
        DateTimeOffset retrievedAt,
        string statementFormat,
        string? statementNumber = null,
        string? pageToken = null,
        string? sourceChecksum = null)
    {
        if (id.Value == 0UL || accountBindingId.Value == 0UL)
        {
            throw new ArgumentException("Identities must be nonzero.");
        }

        if (string.IsNullOrWhiteSpace(statementFormat) || statementFormat.Length > MaximumFormatLength)
        {
            throw new ArgumentException($"Statement format must be non-empty and at most {MaximumFormatLength} characters.", nameof(statementFormat));
        }

        if (statementNumber?.Length > MaximumReferenceLength)
        {
            throw new ArgumentException($"Statement number exceeds {MaximumReferenceLength} characters.", nameof(statementNumber));
        }

        if (pageToken?.Length > MaximumReferenceLength)
        {
            throw new ArgumentException($"Page token exceeds {MaximumReferenceLength} characters.", nameof(pageToken));
        }

        if (sourceChecksum is not null && sourceChecksum.Length != 64)
        {
            throw new ArgumentException("Source checksum must be a 64-character SHA-256 hex string when present.", nameof(sourceChecksum));
        }

        Id = id;
        AccountBindingId = accountBindingId;
        RetrievedAt = retrievedAt;
        StatementFormat = statementFormat.Trim();
        StatementNumber = string.IsNullOrWhiteSpace(statementNumber) ? null : statementNumber.Trim();
        PageToken = string.IsNullOrWhiteSpace(pageToken) ? null : pageToken.Trim();
        SourceChecksum = sourceChecksum?.ToUpperInvariant();
    }

    public ObservationId Id { get; }
    public AccountBindingId AccountBindingId { get; }
    public DateTimeOffset RetrievedAt { get; }
    public string StatementFormat { get; }
    public string? StatementNumber { get; }
    public string? PageToken { get; }
    public string? SourceChecksum { get; }
}
