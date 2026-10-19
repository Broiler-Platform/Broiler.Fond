using System.Globalization;
using System.Security.Cryptography;

namespace Broiler.Fond.Kernel.Domain.Payments;

/// <summary>
/// Immutable payment attempt capturing bank-facing authorization, submission, and reconciliation states.
/// Committed durably to profile storage before the first network request byte is transmitted.
/// </summary>
public sealed record PaymentAttempt
{
    public const int MaxEndToEndIdLength = 35;

    public ulong Id { get; init; }
    public ulong DraftId { get; init; }
    public ulong DraftRevisionId { get; init; }
    public ulong SourceAccountId { get; init; }
    public ulong SourceAccountBindingId { get; init; }
    public ulong SourceAccountIncarnationId { get; init; }
    public string EndToEndId { get; init; }
    public PaymentState State { get; init; }
    public VerificationOfPayeeResult? VoPResult { get; init; }
    public string? BankReference { get; init; }
    public string? ResponseCode { get; init; }
    public string? StatusDescription { get; init; }
    public DateTimeOffset CreatedAt { get; init; }
    public DateTimeOffset UpdatedAt { get; init; }

    public PaymentAttempt(
        ulong id,
        ulong draftId,
        ulong draftRevisionId,
        ulong sourceAccountId,
        ulong sourceAccountBindingId,
        ulong sourceAccountIncarnationId,
        string endToEndId,
        PaymentState state,
        VerificationOfPayeeResult? voPResult,
        string? bankReference,
        string? responseCode,
        string? statusDescription,
        DateTimeOffset createdAt,
        DateTimeOffset updatedAt)
    {
        if (id == 0)
        {
            throw new ArgumentOutOfRangeException(nameof(id), "Attempt ID must be greater than zero.");
        }

        if (draftId == 0)
        {
            throw new ArgumentOutOfRangeException(nameof(draftId), "Draft ID must be greater than zero.");
        }

        if (draftRevisionId == 0)
        {
            throw new ArgumentOutOfRangeException(nameof(draftRevisionId), "Draft revision ID must be greater than zero.");
        }

        if (sourceAccountId == 0)
        {
            throw new ArgumentOutOfRangeException(nameof(sourceAccountId), "Source account ID must be greater than zero.");
        }

        if (sourceAccountBindingId == 0)
        {
            throw new ArgumentOutOfRangeException(nameof(sourceAccountBindingId), "Source account binding ID must be greater than zero.");
        }

        if (sourceAccountIncarnationId == 0)
        {
            throw new ArgumentOutOfRangeException(nameof(sourceAccountIncarnationId), "Source account incarnation ID must be greater than zero.");
        }

        if (string.IsNullOrWhiteSpace(endToEndId))
        {
            throw new ArgumentException("End-to-end ID cannot be null or whitespace.", nameof(endToEndId));
        }

        if (endToEndId.Length > MaxEndToEndIdLength)
        {
            throw new ArgumentException($"End-to-end ID length ({endToEndId.Length}) exceeds maximum of {MaxEndToEndIdLength} characters.", nameof(endToEndId));
        }

        if (!Enum.IsDefined(state))
        {
            throw new ArgumentOutOfRangeException(nameof(state), state, "Undefined payment state.");
        }

        Id = id;
        DraftId = draftId;
        DraftRevisionId = draftRevisionId;
        SourceAccountId = sourceAccountId;
        SourceAccountBindingId = sourceAccountBindingId;
        SourceAccountIncarnationId = sourceAccountIncarnationId;
        EndToEndId = endToEndId;
        State = state;
        VoPResult = voPResult;
        BankReference = bankReference;
        ResponseCode = responseCode;
        StatusDescription = statusDescription;
        CreatedAt = createdAt;
        UpdatedAt = updatedAt;
    }

    /// <summary>
    /// Generates a unique, non-secret, non-PII SEPA EndToEndId matching format 'BF-{yyyyMMdd}-{16 hex digits}'.
    /// Length is 28 characters, strictly within SEPA max 35-character alphanumeric limit.
    /// </summary>
    public static string GenerateEndToEndId(DateTimeOffset? timestamp = null)
    {
        DateTimeOffset ts = timestamp ?? DateTimeOffset.UtcNow;
        string datePart = ts.ToString("yyyyMMdd", CultureInfo.InvariantCulture);
        string randomHex = RandomNumberGenerator.GetHexString(16, lowercase: false);
        return $"BF-{datePart}-{randomHex}";
    }

    /// <summary>
    /// Factory creating a fresh pre-send payment attempt ready for durable profile commitment before transmission.
    /// </summary>
    public static PaymentAttempt CreatePreSendAttempt(
        ulong id,
        PaymentDraftRevision draftRevision,
        DateTimeOffset timestamp,
        PaymentState initialState = PaymentState.AwaitingAuthorization,
        VerificationOfPayeeResult? voPResult = null,
        string? customEndToEndId = null)
    {
        ArgumentNullException.ThrowIfNull(draftRevision);

        string endToEndId = customEndToEndId ?? GenerateEndToEndId(timestamp);

        return new PaymentAttempt(
            id: id,
            draftId: draftRevision.DraftId,
            draftRevisionId: draftRevision.Id,
            sourceAccountId: draftRevision.SourceAccountId,
            sourceAccountBindingId: draftRevision.SourceAccountBindingId,
            sourceAccountIncarnationId: draftRevision.SourceAccountIncarnationId,
            endToEndId: endToEndId,
            state: initialState,
            voPResult: voPResult,
            bankReference: null,
            responseCode: null,
            statusDescription: null,
            createdAt: timestamp,
            updatedAt: timestamp);
    }

    /// <summary>
    /// Immutably transitions this payment attempt to a new state after validating that the transition is legal.
    /// </summary>
    public PaymentAttempt TransitionTo(
        PaymentState newState,
        DateTimeOffset timestamp,
        string? bankReference = null,
        string? responseCode = null,
        string? statusDescription = null)
    {
        PaymentStateMachine.ValidateTransition(State, newState);

        return this with
        {
            State = newState,
            BankReference = bankReference ?? BankReference,
            ResponseCode = responseCode ?? ResponseCode,
            StatusDescription = statusDescription ?? StatusDescription,
            UpdatedAt = timestamp
        };
    }

    /// <summary>
    /// Immutably attaches or updates the Verification of Payee result.
    /// </summary>
    public PaymentAttempt WithVoPResult(VerificationOfPayeeResult voPResult, DateTimeOffset timestamp)
    {
        ArgumentNullException.ThrowIfNull(voPResult);

        return this with
        {
            VoPResult = voPResult,
            UpdatedAt = timestamp
        };
    }
}
