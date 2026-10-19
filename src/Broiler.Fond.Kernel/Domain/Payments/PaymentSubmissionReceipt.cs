namespace Broiler.Fond.Kernel.Domain.Payments;

/// <summary>
/// Immutable receipt of a bank-facing payment submission attempt.
/// Retains authoritative bank return codes and order references without storing secrets.
/// </summary>
public sealed record PaymentSubmissionReceipt
{
    public ulong AttemptId { get; }
    public string EndToEndId { get; }
    public string? BankReference { get; }
    public string ResponseCode { get; }
    public string ResponseText { get; }
    public DateTimeOffset SubmittedAt { get; }
    public DateTimeOffset ConfirmedAt { get; }
    public PaymentState FinalState { get; }

    public PaymentSubmissionReceipt(
        ulong attemptId,
        string endToEndId,
        string? bankReference,
        string responseCode,
        string responseText,
        DateTimeOffset submittedAt,
        DateTimeOffset confirmedAt,
        PaymentState finalState)
    {
        if (attemptId == 0)
        {
            throw new ArgumentOutOfRangeException(nameof(attemptId), "Attempt ID must be greater than zero.");
        }

        if (string.IsNullOrWhiteSpace(endToEndId))
        {
            throw new ArgumentException("End-to-end ID cannot be null or whitespace.", nameof(endToEndId));
        }

        if (string.IsNullOrWhiteSpace(responseCode))
        {
            throw new ArgumentException("Response code cannot be null or whitespace.", nameof(responseCode));
        }

        if (string.IsNullOrWhiteSpace(responseText))
        {
            throw new ArgumentException("Response text cannot be null or whitespace.", nameof(responseText));
        }

        if (!Enum.IsDefined(finalState))
        {
            throw new ArgumentOutOfRangeException(nameof(finalState), finalState, "Undefined final payment state.");
        }

        AttemptId = attemptId;
        EndToEndId = endToEndId;
        BankReference = string.IsNullOrWhiteSpace(bankReference) ? null : bankReference.Trim();
        ResponseCode = responseCode.Trim();
        ResponseText = responseText.Trim();
        SubmittedAt = submittedAt;
        ConfirmedAt = confirmedAt;
        FinalState = finalState;
    }
}
