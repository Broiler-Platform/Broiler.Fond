using Broiler.Fond.Kernel.Domain.Connections;

namespace Broiler.Fond.Kernel.FinTs;

/// <summary>
/// Authoritative outcome of applying synchronization recovery evidence to a bank connection.
/// </summary>
public enum FinTsSynchronizationRecoveryOutcome
{
    /// <summary>Recovery evidence was successfully validated and applied; connection is synchronized.</summary>
    Applied,

    /// <summary>The session result or evidence was incomplete, invalid, or did not report clean dialogue closure.</summary>
    RejectedInvalidResult,

    /// <summary>The session evidence did not contain a unique matching synchronization report.</summary>
    RejectedMissingReport,

    /// <summary>User ID or bank code in the synchronization dialogue did not match the connection configuration.</summary>
    RejectedIdentityMismatch,

    /// <summary>Mode 0 system identifier was null, zero, reserved ("unbekannt"), malformed, or out of bounds.</summary>
    RejectedInvalidSystemId,

    /// <summary>Mode 1 last message number was invalid (< 1 or > 9999) or exceeded the recovery context bound.</summary>
    RejectedInvalidMessageNumber,

    /// <summary>Connection is in Disabled state and cannot accept recovery without explicit reactivation.</summary>
    RejectedDisabledConnection,

    /// <summary>Dialogue terminated due to bank abort (9800); connection transitioned to TemporarilyUnavailable.</summary>
    BankAborted,

    /// <summary>Dialogue terminated with review or abnormal condition; connection transitioned to ReauthenticationRequired.</summary>
    ReviewRequired,
}

/// <summary>
/// Diagnostic issue flags recorded during synchronization recovery application.
/// </summary>
[Flags]
public enum FinTsSynchronizationRecoveryIssue
{
    None = 0,
    InvalidResultState = 1 << 0,
    MissingClosingEvidence = 1 << 1,
    ClosingNotReported = 1 << 2,
    MissingSynchronizationEvidence = 1 << 3,
    SynchronizationNotMatched = 1 << 4,
    MissingReport = 1 << 5,
    UserMismatch = 1 << 6,
    BankCodeMismatch = 1 << 7,
    ConnectionDisabled = 1 << 8,
    InvalidSystemId = 1 << 9,
    InvalidMessageNumber = 1 << 10,
    MessageNumberExceedsRecoveryContext = 1 << 11,
    UnsupportedMode = 1 << 12,
    BankAborted = 1 << 13,
    ReviewRequired = 1 << 14,
}

/// <summary>
/// Result container returned when applying synchronization recovery evidence to a bank connection.
/// </summary>
public sealed class FinTsSynchronizationRecoveryResult
{
    internal FinTsSynchronizationRecoveryResult(
        FinTsSynchronizationRecoveryOutcome outcome,
        BankConnection updatedConnection,
        FinTsSynchronizationRecoveryIssue issues,
        string? appliedSystemId = null,
        int? synchronizedMessageNumber = null)
    {
        Outcome = outcome;
        UpdatedConnection = updatedConnection;
        Issues = issues;
        AppliedSystemId = appliedSystemId;
        SynchronizedMessageNumber = synchronizedMessageNumber;
    }

    public FinTsSynchronizationRecoveryOutcome Outcome { get; }
    public BankConnection UpdatedConnection { get; }
    public FinTsSynchronizationRecoveryIssue Issues { get; }
    public string? AppliedSystemId { get; }
    public int? SynchronizedMessageNumber { get; }
    public bool IsSuccess => Outcome == FinTsSynchronizationRecoveryOutcome.Applied;
}

/// <summary>
/// Applies verified FinTS 3.0 synchronization session results to a bank connection.
/// Under FinTS 3.0 Formals C.5.3 and C.8, synchronization dialogues cannot execute business transactions;
/// once closed, subsequent operations must occur in a newly initialized dialogue using the assigned
/// system identifier and synchronized message counter.
/// </summary>
public static class FinTsSynchronizationRecoveryApplication
{
    public static FinTsSynchronizationRecoveryResult Apply(
        BankConnection connection,
        FinTsPinTanSynchronizationSessionResult result,
        DateTimeOffset? contactTime = null)
    {
        ArgumentNullException.ThrowIfNull(connection);
        ArgumentNullException.ThrowIfNull(result);

        var timestamp = contactTime ?? DateTimeOffset.UtcNow;

        if (connection.State == BankConnectionState.Disabled)
        {
            return new(
                FinTsSynchronizationRecoveryOutcome.RejectedDisabledConnection,
                connection,
                FinTsSynchronizationRecoveryIssue.ConnectionDisabled);
        }

        if (result.Transition == FinTsPinTanSynchronizationSessionTransition.AbortReported)
        {
            var updated = connection.WithState(BankConnectionState.TemporarilyUnavailable, timestamp);
            return new(
                FinTsSynchronizationRecoveryOutcome.BankAborted,
                updated,
                FinTsSynchronizationRecoveryIssue.BankAborted);
        }

        if (result.Transition is FinTsPinTanSynchronizationSessionTransition.RejectedForReview
            or FinTsPinTanSynchronizationSessionTransition.ContextMismatch
            or FinTsPinTanSynchronizationSessionTransition.ReplayRejected
            or FinTsPinTanSynchronizationSessionTransition.WrongState
            or FinTsPinTanSynchronizationSessionTransition.Terminal)
        {
            var updated = connection.WithState(BankConnectionState.ReauthenticationRequired, timestamp);
            return new(
                FinTsSynchronizationRecoveryOutcome.ReviewRequired,
                updated,
                FinTsSynchronizationRecoveryIssue.ReviewRequired);
        }

        if (result.Transition != FinTsPinTanSynchronizationSessionTransition.ReinitializationRequired)
        {
            return new(
                FinTsSynchronizationRecoveryOutcome.RejectedInvalidResult,
                connection,
                FinTsSynchronizationRecoveryIssue.InvalidResultState);
        }

        var issues = FinTsSynchronizationRecoveryIssue.None;
        if (!result.HasMatchingEvidence)
        {
            issues |= FinTsSynchronizationRecoveryIssue.InvalidResultState;
        }

        if (result.ClosingEvidence is null)
        {
            issues |= FinTsSynchronizationRecoveryIssue.MissingClosingEvidence;
        }
        else if (result.ClosingEvidence.Outcome != FinTsDialogueEndOutcome.ClosureReported)
        {
            issues |= FinTsSynchronizationRecoveryIssue.ClosingNotReported;
        }

        if (result.SynchronizationEvidence is null)
        {
            issues |= FinTsSynchronizationRecoveryIssue.MissingSynchronizationEvidence;
        }
        else if (!result.SynchronizationEvidence.HasMatchingEvidence)
        {
            issues |= FinTsSynchronizationRecoveryIssue.SynchronizationNotMatched;
        }

        if (issues != FinTsSynchronizationRecoveryIssue.None)
        {
            return new(FinTsSynchronizationRecoveryOutcome.RejectedInvalidResult, connection, issues);
        }

        var report = result.SynchronizationEvidence!.MatchingReport;
        if (report is null)
        {
            return new(
                FinTsSynchronizationRecoveryOutcome.RejectedMissingReport,
                connection,
                FinTsSynchronizationRecoveryIssue.MissingReport);
        }

        var request = result.SynchronizationEvidence.Request;
        var syncReq = request.SignatureEvidence.Request.Synchronization;
        if (syncReq is null)
        {
            return new(
                FinTsSynchronizationRecoveryOutcome.RejectedInvalidResult,
                connection,
                FinTsSynchronizationRecoveryIssue.InvalidResultState);
        }

        if (!string.Equals(request.ExpectedUserId, connection.UserId, StringComparison.Ordinal))
        {
            return new(
                FinTsSynchronizationRecoveryOutcome.RejectedIdentityMismatch,
                connection,
                FinTsSynchronizationRecoveryIssue.UserMismatch);
        }

        if (connection.CustomerId is not null &&
            !string.Equals(syncReq.Identification.CustomerId, connection.CustomerId, StringComparison.Ordinal))
        {
            return new(
                FinTsSynchronizationRecoveryOutcome.RejectedIdentityMismatch,
                connection,
                FinTsSynchronizationRecoveryIssue.UserMismatch);
        }

        if (connection.InstitutionConfiguration.BankCode is not null &&
            !string.Equals(syncReq.Identification.Institution, connection.InstitutionConfiguration.BankCode, StringComparison.Ordinal))
        {
            return new(
                FinTsSynchronizationRecoveryOutcome.RejectedIdentityMismatch,
                connection,
                FinTsSynchronizationRecoveryIssue.BankCodeMismatch);
        }

        var mode = syncReq.Synchronization.Mode;
        if (mode == FinTsSynchronizationMode.SystemId)
        {
            if (report.Shape != FinTsSynchronizationShape.SystemId)
            {
                return new(
                    FinTsSynchronizationRecoveryOutcome.RejectedInvalidResult,
                    connection,
                    FinTsSynchronizationRecoveryIssue.InvalidResultState);
            }

            string? sysId = report.SystemId;
            if (sysId is null || sysId.Length is < 1 or > BankConnection.MaximumSystemIdLength ||
                sysId is "0" || string.Equals(sysId, "unbekannt", StringComparison.OrdinalIgnoreCase) ||
                sysId[0] == ' ' || sysId[^1] == ' ' ||
                sysId.Any(static c => c < 32 || c is >= (char)127 and <= (char)160 || c > 255))
            {
                return new(
                    FinTsSynchronizationRecoveryOutcome.RejectedInvalidSystemId,
                    connection,
                    FinTsSynchronizationRecoveryIssue.InvalidSystemId);
            }

            var updated = connection.WithSynchronization(sysId, nextMessageNumber: 1, timestamp);
            return new(
                FinTsSynchronizationRecoveryOutcome.Applied,
                updated,
                FinTsSynchronizationRecoveryIssue.None,
                appliedSystemId: sysId,
                synchronizedMessageNumber: 1);
        }

        if (mode == FinTsSynchronizationMode.LastMessageNumber)
        {
            if (report.Shape != FinTsSynchronizationShape.LastMessageNumber)
            {
                return new(
                    FinTsSynchronizationRecoveryOutcome.RejectedInvalidResult,
                    connection,
                    FinTsSynchronizationRecoveryIssue.InvalidResultState);
            }

            int? lastMsg = report.LastMessageNumber;
            if (!lastMsg.HasValue || lastMsg.Value is < 1 or > 9999)
            {
                return new(
                    FinTsSynchronizationRecoveryOutcome.RejectedInvalidMessageNumber,
                    connection,
                    FinTsSynchronizationRecoveryIssue.InvalidMessageNumber);
            }

            if (result.SynchronizationEvidence.RecoveryContext is not null &&
                lastMsg.Value > result.SynchronizationEvidence.RecoveryContext.LastSubmittedMessageNumber)
            {
                return new(
                    FinTsSynchronizationRecoveryOutcome.RejectedInvalidMessageNumber,
                    connection,
                    FinTsSynchronizationRecoveryIssue.MessageNumberExceedsRecoveryContext);
            }

            int nextNumber = lastMsg.Value < 9999 ? lastMsg.Value + 1 : 1;
            var updated = connection
                .WithNextClientMessageNumber(nextNumber)
                .WithSuccessfulContact(timestamp)
                .WithState(BankConnectionState.Connected, timestamp);

            return new(
                FinTsSynchronizationRecoveryOutcome.Applied,
                updated,
                FinTsSynchronizationRecoveryIssue.None,
                appliedSystemId: connection.SystemId,
                synchronizedMessageNumber: nextNumber);
        }

        return new(
            FinTsSynchronizationRecoveryOutcome.RejectedInvalidResult,
            connection,
            FinTsSynchronizationRecoveryIssue.UnsupportedMode);
    }
}
