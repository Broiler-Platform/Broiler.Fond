using System.Collections.ObjectModel;
using System.Globalization;
using System.Linq;
using System.Security.Cryptography;
using Broiler.Fond.Kernel.Domain.Connections;
using Broiler.Fond.Kernel.FinTs.Transport;
using Broiler.Fond.Kernel.Product;

namespace Broiler.Fond.Kernel.FinTs;

/// <summary>Represents the terminal outcome status of a FinTS synchronization execution pipeline run.</summary>
public enum FinTsSynchronizationExecutionStatus
{
    /// <summary>The synchronization completed, dialogue closed cleanly, and recovery state was applied to the connection.</summary>
    Completed,

    /// <summary>Execution was rejected because the bank connection is disabled.</summary>
    RejectedDisabledConnection,

    /// <summary>Execution was rejected because the endpoint is unconfirmed or not absolute HTTPS.</summary>
    RejectedInvalidEndpoint,

    /// <summary>Execution was rejected because the session credential is null, wrong kind, or expired.</summary>
    RejectedInvalidCredential,

    /// <summary>Execution was rejected because a candidate request binding could not be constructed.</summary>
    RejectedMissingCandidate,

    /// <summary>A network, TLS, timeout, or HTTP transport error occurred.</summary>
    RejectedTransportFailure,

    /// <summary>A protocol framing error, parse failure, or terminal bank abort occurred.</summary>
    RejectedProtocolFailure,

    /// <summary>The dialogue ended in an outcome requiring user authentication review.</summary>
    RejectedDialogueOutcome,

    /// <summary>The dialogue completed but recovery application was rejected.</summary>
    RejectedRecoveryFailure,

    /// <summary>The execution was cancelled by the caller.</summary>
    Cancelled,
}

/// <summary>Configures the operational parameters and candidate builders for a FinTS synchronization execution run.</summary>
public sealed class FinTsSynchronizationExecutionOptions
{
    public Func<FinTsTanChallenge, CancellationToken, ValueTask<FinTsSessionCredential?>>? TextTanPrompt { get; init; }
    public Func<FinTsPinTanRequestBinding, FinTsResponse, FinTsSignedTextTanRequest>? TextTanCandidateFactory { get; init; }
    public Action<FinTsDialogueCleanupOutcome>? CleanupObserver { get; init; }
    public FinTsSynchronizationMode Mode { get; init; } = FinTsSynchronizationMode.SystemId;
    public TimeSpan Timeout { get; init; } = TimeSpan.FromSeconds(60);
    public TimeProvider? Clock { get; init; }
    public FinTsSynchronizationRecoveryContext? RecoveryContext { get; init; }

    /// <summary>The registered FinTS product identity used for synchronization candidate generation.</summary>
    public FinTsProductRegistration ProductRegistration { get; init; } = KernelProduct.FinTsRegistration;

    /// <summary>
    /// Optional interactive challenge handler invoked when a bank returns an SCA challenge (HITAN with TAN prompt).
    /// </summary>
    public Func<FinTsTanChallenge, FinTsTanProcedure?, CancellationToken, ValueTask<FinTsSessionCredential?>>? ScaChallengeHandler { get; init; }

    /// <summary>Optional custom candidate factory for the SCA continuation request (e.g. HKTAN process 2 or S).</summary>
    public Func<FinTsScaContinuation, FinTsSessionCredential, FinTsPinTanRequestBinding>? ScaContinuationCandidateFactory { get; init; }

    /// <summary>Maximum number of decoupled status queries to execute before failing (default 32).</summary>
    public int MaximumStatusQueries { get; init; } = 32;

    /// <summary>Whether automatic decoupled polling is allowed (default true).</summary>
    public bool AllowAutomaticQueries { get; init; } = true;

    /// <summary>Optional custom candidate factory for client message 1 (synchronization).</summary>
    public Func<BankConnection, FinTsPinTanRequestBinding>? SynchronizationCandidateFactory { get; init; }

    /// <summary>Optional TAN parameter set advertising supported procedures (e.g. from prior BPD/UPD).</summary>
    public FinTsTanParameterSet? Procedures { get; init; }

    /// <summary>Optional permitted procedure set (e.g. from prior BPD/UPD).</summary>
    public FinTsPermittedProcedureSet? Permissions { get; init; }

    /// <summary>Optional custom candidate factory for dialogue closing (HKEND).</summary>
    public Func<FinTsPinTanSynchronizationSessionAttempt, FinTsPinTanSynchronizationSessionResult, FinTsPinTanDialogueEndRequestBinding>? ClosingCandidateFactory { get; init; }

    public static FinTsSynchronizationExecutionOptions SystemId(
        Func<BankConnection, FinTsPinTanRequestBinding>? syncFactory = null,
        Func<FinTsPinTanSynchronizationSessionAttempt, FinTsPinTanSynchronizationSessionResult, FinTsPinTanDialogueEndRequestBinding>? closingFactory = null,
        Func<FinTsTanChallenge, FinTsTanProcedure?, CancellationToken, ValueTask<FinTsSessionCredential?>>? scaChallengeHandler = null,
        Func<FinTsScaContinuation, FinTsSessionCredential, FinTsPinTanRequestBinding>? scaContinuationFactory = null,
        FinTsProductRegistration? productRegistration = null,
        int maximumStatusQueries = 32,
        bool allowAutomaticQueries = true,
        TimeSpan? timeout = null,
        TimeProvider? clock = null) => new()
        {
            Mode = FinTsSynchronizationMode.SystemId,
            SynchronizationCandidateFactory = syncFactory,
            ClosingCandidateFactory = closingFactory,
            ScaChallengeHandler = scaChallengeHandler,
            ScaContinuationCandidateFactory = scaContinuationFactory,
            ProductRegistration = productRegistration ?? KernelProduct.FinTsRegistration,
            MaximumStatusQueries = maximumStatusQueries,
            AllowAutomaticQueries = allowAutomaticQueries,
            Timeout = timeout ?? TimeSpan.FromSeconds(60),
            Clock = clock,
        };

    public static FinTsSynchronizationExecutionOptions LastMessageNumber(
        FinTsSynchronizationRecoveryContext? recoveryContext = null,
        Func<BankConnection, FinTsPinTanRequestBinding>? syncFactory = null,
        Func<FinTsPinTanSynchronizationSessionAttempt, FinTsPinTanSynchronizationSessionResult, FinTsPinTanDialogueEndRequestBinding>? closingFactory = null,
        Func<FinTsTanChallenge, FinTsTanProcedure?, CancellationToken, ValueTask<FinTsSessionCredential?>>? scaChallengeHandler = null,
        Func<FinTsScaContinuation, FinTsSessionCredential, FinTsPinTanRequestBinding>? scaContinuationFactory = null,
        FinTsProductRegistration? productRegistration = null,
        int maximumStatusQueries = 32,
        bool allowAutomaticQueries = true,
        TimeSpan? timeout = null,
        TimeProvider? clock = null) => new()
        {
            Mode = FinTsSynchronizationMode.LastMessageNumber,
            RecoveryContext = recoveryContext,
            SynchronizationCandidateFactory = syncFactory,
            ClosingCandidateFactory = closingFactory,
            ScaChallengeHandler = scaChallengeHandler,
            ScaContinuationCandidateFactory = scaContinuationFactory,
            ProductRegistration = productRegistration ?? KernelProduct.FinTsRegistration,
            MaximumStatusQueries = maximumStatusQueries,
            AllowAutomaticQueries = allowAutomaticQueries,
            Timeout = timeout ?? TimeSpan.FromSeconds(60),
            Clock = clock,
        };
}

/// <summary>Immutable result of a FinTS synchronization execution pipeline run.</summary>
public sealed class FinTsSynchronizationExecutionResult
{
    private FinTsSynchronizationExecutionResult(
        FinTsSynchronizationExecutionStatus status,
        BankConnection updatedConnection,
        FinTsPinTanSynchronizationSessionResult? sessionResult,
        FinTsSynchronizationRecoveryResult? recoveryResult,
        IList<string> issues)
    {
        Status = status;
        UpdatedConnection = updatedConnection;
        SessionResult = sessionResult;
        RecoveryResult = recoveryResult;
        Issues = new ReadOnlyCollection<string>(issues);
    }

    public FinTsSynchronizationExecutionStatus Status { get; }
    public bool IsSuccess => Status == FinTsSynchronizationExecutionStatus.Completed;
    public BankConnection UpdatedConnection { get; }
    public FinTsPinTanSynchronizationSessionResult? SessionResult { get; }
    public FinTsSynchronizationRecoveryResult? RecoveryResult { get; }
    public ReadOnlyCollection<string> Issues { get; }

    public static FinTsSynchronizationExecutionResult Completed(
        BankConnection updatedConnection,
        FinTsPinTanSynchronizationSessionResult sessionResult,
        FinTsSynchronizationRecoveryResult recoveryResult)
    {
        ArgumentNullException.ThrowIfNull(updatedConnection);
        ArgumentNullException.ThrowIfNull(sessionResult);
        ArgumentNullException.ThrowIfNull(recoveryResult);

        return new(FinTsSynchronizationExecutionStatus.Completed, updatedConnection, sessionResult, recoveryResult, Array.Empty<string>());
    }

    public static FinTsSynchronizationExecutionResult Failure(
        FinTsSynchronizationExecutionStatus status,
        BankConnection connection,
        string reason,
        FinTsPinTanSynchronizationSessionResult? sessionResult = null,
        FinTsSynchronizationRecoveryResult? recoveryResult = null,
        IEnumerable<string>? additionalIssues = null)
    {
        ArgumentNullException.ThrowIfNull(connection);
        ArgumentException.ThrowIfNullOrWhiteSpace(reason);

        var list = new List<string> { reason };
        if (additionalIssues is not null)
        {
            list.AddRange(additionalIssues);
        }

        return new(status, connection, sessionResult, recoveryResult, list);
    }
}

/// <summary>
/// End-to-end FinTS 3.0 synchronization pipeline coordinating transport, dialogue state machine,
/// dialogue termination, and recovery application to a BankConnection.
/// Credential handling guarantees that transient PIN/TAN secrets are wiped from memory immediately
/// upon transmission and are never included in diagnostics, logs, or exception messages.
/// </summary>
public static class FinTsSynchronizationExecutionPipeline
{
    public static async ValueTask<FinTsSynchronizationExecutionResult> ExecuteAsync(
        BankConnection connection,
        IFinTsTransport transport,
        FinTsSessionCredential credential,
        FinTsSynchronizationExecutionOptions options,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(connection);
        ArgumentNullException.ThrowIfNull(transport);
        ArgumentNullException.ThrowIfNull(credential);
        ArgumentNullException.ThrowIfNull(options);

        var clock = options.Clock ?? TimeProvider.System;
        DateTimeOffset now = clock.GetUtcNow();

        // 1. Connection and credential preflight validation
        if (connection.State == BankConnectionState.Disabled)
        {
            return FinTsSynchronizationExecutionResult.Failure(
                FinTsSynchronizationExecutionStatus.RejectedDisabledConnection,
                connection.WithAttemptedContact(now),
                "Connection is disabled; synchronization execution rejected.");
        }

        if (!connection.InstitutionConfiguration.IsEndpointConfirmed)
        {
            return FinTsSynchronizationExecutionResult.Failure(
                FinTsSynchronizationExecutionStatus.RejectedInvalidEndpoint,
                connection.WithAttemptedContact(now),
                "Connection endpoint is unconfirmed; synchronization execution rejected.");
        }

        Uri endpoint = connection.InstitutionConfiguration.FinTsEndpoint;

        var credSnapshot = credential.GetSnapshot();
        if (credSnapshot.Kind != FinTsCredentialKind.Pin || credSnapshot.State != FinTsCredentialState.Available)
        {
            return FinTsSynchronizationExecutionResult.Failure(
                FinTsSynchronizationExecutionStatus.RejectedInvalidCredential,
                connection.WithAttemptedContact(now),
                "A valid, available PIN credential is required.");
        }

        // 2. Open synchronization session attempt
        using var attempt = new FinTsPinTanSynchronizationSessionAttempt(options.Timeout, clock);
        FinTsPinTanSynchronizationSessionResult? acceptedSynchronization = null;

        try
        {
            // -------------------------------------------------------------
            // Stage 1: Synchronization Request & Response
            // -------------------------------------------------------------
            FinTsPinTanRequestBinding syncCandidate;
            if (options.SynchronizationCandidateFactory is not null)
            {
                syncCandidate = options.SynchronizationCandidateFactory(connection);
            }
            else
            {
                return FinTsSynchronizationExecutionResult.Failure(
                    FinTsSynchronizationExecutionStatus.RejectedMissingCandidate,
                    connection.WithAttemptedContact(now),
                    "Synchronization candidate factory must be provided.");
            }

            var startTrans = attempt.StartSynchronization(syncCandidate, options.RecoveryContext, cancellationToken);
            if (startTrans != FinTsPinTanSynchronizationSessionTransition.CandidateRecorded)
            {
                return MapFailedTransition(attempt, connection, startTrans, now);
            }

            byte[] syncBuffer = new byte[FinTsPinTanRequestEnvelopeWriter.MaximumEncodedLength];
            int syncWritten;
            FinTsPinTanSynchronizationSessionResult syncResult;
            try
            {
                var writeResult = FinTsPinTanRequestEnvelopeWriter.TryEncode(
                    syncCandidate.SignatureEvidence,
                    credential,
                    tan: null,
                    syncBuffer,
                    out syncWritten,
                    cancellationToken);

                if (writeResult != FinTsPinTanRequestWriteResult.Written)
                {
                    return FinTsSynchronizationExecutionResult.Failure(
                        FinTsSynchronizationExecutionStatus.RejectedProtocolFailure,
                        connection.WithAttemptedContact(now),
                        $"Failed to encode synchronization request envelope: {writeResult}");
                }

                var syncTransportResp = await transport.SendAsync(
                    endpoint, syncBuffer.AsMemory(0, syncWritten), cancellationToken).ConfigureAwait(false);

                if (!syncTransportResp.IsSuccess)
                {
                    return FinTsSynchronizationExecutionResult.Failure(
                        FinTsSynchronizationExecutionStatus.RejectedTransportFailure,
                        connection.WithAttemptedContact(now),
                        $"Synchronization transport error: HTTP {syncTransportResp.StatusCode}");
                }

                var syncResp = ParseWireResponse(syncTransportResp.Payload.Span, cancellationToken);
                var syncChallengeSet = FinTsTanChallengeSet.Parse(syncResp, cancellationToken);
                FinTsSignedTanResult? approval = null;
                if (syncChallengeSet.Challenges.Count > 0)
                {
                    if (options.TextTanPrompt is null || options.TextTanCandidateFactory is null ||
                        !syncCandidate.Segments.Any(s => s.Role == FinTsPinTanRequestSegmentRole.TanOperation))
                        return FinTsSynchronizationExecutionResult.Failure(FinTsSynchronizationExecutionStatus.RejectedDialogueOutcome,
                            connection.WithState(BankConnectionState.ReauthenticationRequired, now),
                            "This challenge requires an actual supported signed approval request. No synthetic challenge or unsigned fallback was used.");
                    FinTsSignedTextTanRequest candidate;
                    try { candidate = options.TextTanCandidateFactory(syncCandidate, syncResp); }
                    catch (Exception ex) when (ex is FinTsCandidateException or FinTsFormatException or InvalidOperationException)
                    {
                        return FinTsSynchronizationExecutionResult.Failure(FinTsSynchronizationExecutionStatus.RejectedDialogueOutcome,
                            connection.WithState(BankConnectionState.ReauthenticationRequired, now),
                            "The challenge does not qualify for the selected text-TAN procedure.");
                    }
                    approval = await FinTsSignedTextTanExecution.ExecuteAsync(candidate, transport, endpoint, credential,
                        options.TextTanPrompt, cancellationToken).ConfigureAwait(false);
                    if (approval.Status != FinTsSignedTanStatus.Confirmed)
                        return FinTsSynchronizationExecutionResult.Failure(
                            approval.Status == FinTsSignedTanStatus.Cancelled
                                ? FinTsSynchronizationExecutionStatus.Cancelled
                                : FinTsSynchronizationExecutionStatus.RejectedDialogueOutcome,
                            approval.Status == FinTsSignedTanStatus.Cancelled
                                ? connection.WithAttemptedContact(now)
                                : connection.WithState(BankConnectionState.ReauthenticationRequired, now),
                            "Bank approval was cancelled, rejected or could not be validated. No synchronized state was applied.");
                }
                syncResult = approval is null ? attempt.AcceptSynchronizationResponse(syncResp, cancellationToken)
                    : attempt.AcceptSignedApproval(approval, cancellationToken);
                acceptedSynchronization = syncResult;
            }
            finally
            {
                CryptographicOperations.ZeroMemory(syncBuffer);
            }

            if (syncResult.Transition == FinTsPinTanSynchronizationSessionTransition.AbortReported)
            {
                var recovery = FinTsSynchronizationRecoveryApplication.Apply(connection, syncResult, now);
                return FinTsSynchronizationExecutionResult.Failure(
                    FinTsSynchronizationExecutionStatus.RejectedProtocolFailure,
                    recovery.UpdatedConnection,
                    "Bank reported dialogue abort (code 9800); connection temporarily unavailable.",
                    syncResult,
                    recovery);
            }

            if (syncResult.Transition == FinTsPinTanSynchronizationSessionTransition.RejectedForReview)
            {
                var recovery = FinTsSynchronizationRecoveryApplication.Apply(connection, syncResult, now);
                return FinTsSynchronizationExecutionResult.Failure(
                    FinTsSynchronizationExecutionStatus.RejectedDialogueOutcome,
                    recovery.UpdatedConnection,
                    "Synchronization outcome requires authentication review (RejectedForReview).",
                    syncResult,
                    recovery);
            }

            if (syncResult.Transition != FinTsPinTanSynchronizationSessionTransition.CloseRequired)
            {
                return MapFailedTransition(attempt, connection, syncResult.Transition, now, syncResult);
            }

            // -------------------------------------------------------------
            // Stage 2: Dialogue Closing (HKEND)
            // -------------------------------------------------------------
            FinTsPinTanDialogueEndRequestBinding closingCandidate;
            if (options.ClosingCandidateFactory is not null)
            {
                closingCandidate = options.ClosingCandidateFactory(attempt, syncResult);
            }
            else
            {
                closingCandidate = CreateDefaultClosingCandidate(attempt, syncResult, cancellationToken);
            }

            var closeTrans = attempt.RecordClosingCandidate(closingCandidate, cancellationToken);
            if (closeTrans != FinTsPinTanSynchronizationSessionTransition.CloseCandidateRecorded)
            {
                return MapFailedTransition(attempt, connection, closeTrans, now);
            }

            byte[] closingBuffer = new byte[FinTsPinTanDialogueEndWriter.MaximumEncodedLength];
            int closeWritten;
            FinTsPinTanSynchronizationSessionResult closingResult;
            try
            {
                var writeResult = FinTsPinTanDialogueEndWriter.TryEncode(
                    closingCandidate.Context, credential, closingBuffer, out closeWritten, cancellationToken);

                if (writeResult != FinTsPinTanRequestWriteResult.Written)
                {
                    return FinTsSynchronizationExecutionResult.Failure(
                        FinTsSynchronizationExecutionStatus.RejectedProtocolFailure,
                        connection.WithAttemptedContact(now),
                        $"Failed to encode dialogue closing envelope: {writeResult}");
                }

                var closeTransportResp = await transport.SendAsync(
                    endpoint, closingBuffer.AsMemory(0, closeWritten), cancellationToken).ConfigureAwait(false);

                if (!closeTransportResp.IsSuccess)
                {
                    return FinTsSynchronizationExecutionResult.Failure(
                        FinTsSynchronizationExecutionStatus.RejectedTransportFailure,
                        connection.WithAttemptedContact(now),
                        $"Closing transport error: HTTP {closeTransportResp.StatusCode}");
                }

                var closeResp = ParseWireResponse(closeTransportResp.Payload.Span, cancellationToken);
                closingResult = attempt.AcceptClosingResponse(closeResp, cancellationToken);
            }
            finally
            {
                CryptographicOperations.ZeroMemory(closingBuffer);
            }

            // -------------------------------------------------------------
            // Stage 3: Terminal Evaluation & Recovery Application
            // -------------------------------------------------------------
            var recoveryResult = FinTsSynchronizationRecoveryApplication.Apply(
                connection, closingResult, now);

            if (!recoveryResult.IsSuccess)
            {
                return FinTsSynchronizationExecutionResult.Failure(
                    recoveryResult.Outcome == FinTsSynchronizationRecoveryOutcome.BankAborted
                        ? FinTsSynchronizationExecutionStatus.RejectedProtocolFailure
                        : recoveryResult.Outcome == FinTsSynchronizationRecoveryOutcome.ReviewRequired
                            ? FinTsSynchronizationExecutionStatus.RejectedDialogueOutcome
                            : FinTsSynchronizationExecutionStatus.RejectedRecoveryFailure,
                    recoveryResult.UpdatedConnection,
                    $"Synchronization recovery rejected with outcome {recoveryResult.Outcome}.",
                    closingResult,
                    recoveryResult);
            }

            return FinTsSynchronizationExecutionResult.Completed(
                recoveryResult.UpdatedConnection,
                closingResult,
                recoveryResult);
        }
        catch (OperationCanceledException)
        {
            return FinTsSynchronizationExecutionResult.Failure(
                FinTsSynchronizationExecutionStatus.Cancelled,
                connection.WithAttemptedContact(now),
                "Synchronization execution was cancelled.");
        }
        catch (FinTsTransportException ex)
        {
            return FinTsSynchronizationExecutionResult.Failure(
                FinTsSynchronizationExecutionStatus.RejectedTransportFailure,
                connection.WithAttemptedContact(now),
                $"Transport error ({ex.Issue}): {ex.Message}");
        }
        catch (FinTsFormatException ex)
        {
            return FinTsSynchronizationExecutionResult.Failure(
                FinTsSynchronizationExecutionStatus.RejectedProtocolFailure,
                connection.WithAttemptedContact(now),
                $"Protocol framing error: {ex.Message}");
        }
        catch (Exception ex)
        {
            return FinTsSynchronizationExecutionResult.Failure(
                FinTsSynchronizationExecutionStatus.RejectedProtocolFailure,
                connection.WithAttemptedContact(now),
                $"Unexpected execution failure: {ex.Message}");
        }
        finally
        {
            if (attempt.GetSnapshot().State == FinTsPinTanSynchronizationSessionState.AwaitingCloseRequest &&
                acceptedSynchronization?.SynchronizationEvidence?.HasMatchingEvidence == true)
            {
                var cleanup = await FinTsDialogueCleanup.CloseAsync(
                    () => options.ClosingCandidateFactory?.Invoke(attempt, acceptedSynchronization) ?? CreateDefaultClosingCandidate(attempt, acceptedSynchronization),
                    transport, endpoint, credential).ConfigureAwait(false);
                FinTsDialogueCleanup.Notify(options.CleanupObserver, cleanup);
            }
        }
    }

    /// <summary>Parses wire response bytes into a typed FinTS response, unwrapping PIN/TAN envelopes when present.</summary>
    public static FinTsResponse ParseWireResponse(ReadOnlySpan<byte> wireBytes, CancellationToken cancellationToken = default)
    {
        var frame = FinTsMessageFrame.Parse(wireBytes, cancellationToken);
        if (frame.Syntax.Segments.Count > 1 && frame.Syntax.Segments[1].Code == "HNVSK")
        {
            var envelope = FinTsPinTanEnvelope.Parse(frame, cancellationToken);
            return FinTsResponse.ParsePinTan(envelope, cancellationToken);
        }

        return FinTsResponse.Parse(frame, cancellationToken);
    }

    /// <summary>Constructs a default dialogue end candidate (HKEND) derived from synchronization evidence and session counters.</summary>
    public static FinTsPinTanDialogueEndRequestBinding CreateDefaultClosingCandidate(
        FinTsPinTanSynchronizationSessionAttempt attempt,
        FinTsPinTanSynchronizationSessionResult syncResult,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(attempt);
        ArgumentNullException.ThrowIfNull(syncResult);
        var syncEvidence = syncResult.SynchronizationEvidence
            ?? throw new InvalidOperationException("Session has no accepted synchronization evidence.");

        var snapshot = attempt.GetSnapshot();
        int clientMsg = snapshot.ClientMessageNumber + 1; // 2
        int expectedBankMsg = snapshot.BankMessageNumber + 1; // 2
        string dialogueId = attempt.DialogueId ?? "0";

        byte[] unsignedEnd = FinTsUnsignedDialogueEndWriter.Encode(dialogueId, clientMsg, cancellationToken);
        var endFrame = FinTsMessageFrame.Parse(unsignedEnd, cancellationToken);
        var endRequest = FinTsUnsignedDialogueEndRequest.Parse(endFrame, expectedBankMsg, cancellationToken);

        var prevHeader = syncEvidence.Request.SignatureEvidence.Header;
        string systemId = syncEvidence.Request.SignatureEvidence.Request.Synchronization?.Synchronization.Mode == FinTsSynchronizationMode.SystemId
            ? (syncEvidence.MatchingReport?.SystemId ?? prevHeader.SystemId)
            : prevHeader.SystemId;

        var headerInput = new FinTsPinTanSignatureHeaderInput(
            profileVersion: prevHeader.ProfileVersion,
            securityFunction: int.Parse(prevHeader.SecurityFunction, CultureInfo.InvariantCulture),
            controlReference: "1",
            securitySupplierRole: 1,
            securityParty: 1,
            systemId: systemId,
            securityReferenceNumber: (ulong)clientMsg,
            securityDate: null,
            securityTime: null,
            hashAlgorithmCode: prevHeader.HashAlgorithmCode,
            signatureAlgorithmCode: prevHeader.SignatureAlgorithmCode,
            operationModeCode: prevHeader.OperationModeCode,
            countryCode: prevHeader.CountryCode,
            institutionId: prevHeader.InstitutionId,
            userId: prevHeader.UserId,
            keyNumber: prevHeader.KeyNumber,
            keyVersion: prevHeader.KeyVersion);

        byte[] hnshk = FinTsPinTanSignatureHeaderWriter.Encode(headerInput, 2, cancellationToken);
        var header = FinTsPinTanSignatureHeader.Parse(FinTsSyntax.ParseSegments(hnshk).Segments.Single(), cancellationToken);

        var context = FinTsPinTanDialogueEndContext.Evaluate(
            syncEvidence, endRequest, header, cancellationToken);

        return FinTsPinTanDialogueEndRequestBinding.ForEnvelopeCandidate(context, cancellationToken);
    }

    /// <summary>
    /// Constructs a default synchronization candidate (HKSYN) with registered product identity and procedure evidence.
    /// Uses the registered product ID and version before any external live-bank dialogue.
    /// </summary>
    public static FinTsPinTanRequestBinding CreateDefaultSynchronizationCandidate(
        BankConnection connection,
        FinTsPinTanProcedureContext procedureContext,
        FinTsSynchronizationMode mode = FinTsSynchronizationMode.SystemId,
        FinTsProductRegistration? registration = null,
        FinTsPinTanSignatureSelection? procedureSelection = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(connection);
        ArgumentNullException.ThrowIfNull(procedureContext);
        cancellationToken.ThrowIfCancellationRequested();

        var reg = registration ?? KernelProduct.FinTsRegistration;
        var config = connection.InstitutionConfiguration;

        string country = procedureContext.Initialization.Identification.Country;
        string institution = procedureContext.Initialization.Identification.Institution;
        string customerId = procedureContext.Initialization.Identification.CustomerId;
        string systemId = mode == FinTsSynchronizationMode.SystemId ? "0" : (connection.SystemId ?? "0");

        var input = new FinTsInitializationInput(
            country: country,
            institution: institution,
            customerId: customerId,
            systemId: systemId,
            systemStatus: FinTsCustomerSystemStatus.Required,
            bankParameterVersion: 0,
            userParameterVersion: 0,
            language: FinTsDialogueLanguage.German,
            productIdentifier: reg.ProductIdentifier,
            productVersion: reg.ProductVersion);

        byte[] unsignedWire = FinTsUnsignedSynchronizationWriter.Encode(input, mode, cancellationToken);
        var frame = FinTsMessageFrame.Parse(unsignedWire, cancellationToken);
        var unsignedSync = FinTsUnsignedSynchronizationRequest.Parse(frame, cancellationToken);

        var advertisement = procedureContext.Parameters.Advertisements[0];
        var matchingProc = advertisement.Procedures[0];

        var selection = procedureSelection ?? new FinTsPinTanSignatureSelection(
            profileVersion: 2,
            securityFunction: matchingProc.SecurityFunction,
            tanSegmentVersion: advertisement.Source.Version);

        var headerInput = new FinTsPinTanSignatureHeaderInput(
            profileVersion: selection.ProfileVersion,
            securityFunction: selection.SecurityFunction,
            controlReference: "1",
            securitySupplierRole: 1,
            securityParty: 1,
            systemId: systemId,
            securityReferenceNumber: 1,
            securityDate: null,
            securityTime: null,
            hashAlgorithmCode: "999",
            signatureAlgorithmCode: "10",
            operationModeCode: "16",
            countryCode: country,
            institutionId: institution,
            userId: connection.UserId,
            keyNumber: 0,
            keyVersion: 0);

        byte[] hnshkBytes = FinTsPinTanSignatureHeaderWriter.Encode(headerInput, 2, cancellationToken);
        var header = FinTsPinTanSignatureHeader.Parse(FinTsSyntax.ParseSegments(hnshkBytes).Segments.Single(), cancellationToken);

        var requestContext = FinTsPinTanSignatureRequestContext.ForSynchronization(
            unsignedSync, connection.UserId, "1", selection);

        var signatureEvidence = FinTsPinTanSignatureEvidence.Evaluate(
            requestContext, header, procedureContext, cancellationToken);

        return FinTsPinTanRequestBinding.ForEnvelopeCandidate(signatureEvidence, cancellationToken);
    }

    private static FinTsSynchronizationExecutionResult MapFailedTransition(
        FinTsPinTanSynchronizationSessionAttempt attempt,
        BankConnection connection,
        FinTsPinTanSynchronizationSessionTransition transition,
        DateTimeOffset now,
        FinTsPinTanSynchronizationSessionResult? sessionResult = null)
    {
        return transition switch
        {
            FinTsPinTanSynchronizationSessionTransition.AbortReported =>
                FinTsSynchronizationExecutionResult.Failure(
                    FinTsSynchronizationExecutionStatus.RejectedProtocolFailure,
                    connection.WithState(BankConnectionState.TemporarilyUnavailable, now),
                    "Bank reported dialogue abort (code 9800); connection temporarily unavailable.",
                    sessionResult),

            FinTsPinTanSynchronizationSessionTransition.RejectedForReview or
            FinTsPinTanSynchronizationSessionTransition.ContextMismatch =>
                FinTsSynchronizationExecutionResult.Failure(
                    FinTsSynchronizationExecutionStatus.RejectedDialogueOutcome,
                    connection.WithState(BankConnectionState.ReauthenticationRequired, now),
                    $"Dialogue outcome requires authentication review ({transition}).",
                    sessionResult),

            FinTsPinTanSynchronizationSessionTransition.ReplayRejected =>
                FinTsSynchronizationExecutionResult.Failure(
                    FinTsSynchronizationExecutionStatus.RejectedProtocolFailure,
                    connection.WithAttemptedContact(now),
                    "Replayed bank message counter detected.",
                    sessionResult),

            _ =>
                FinTsSynchronizationExecutionResult.Failure(
                    FinTsSynchronizationExecutionStatus.RejectedProtocolFailure,
                    connection.WithAttemptedContact(now),
                    $"Synchronization session transitioned to unexpected outcome: {transition}.",
                    sessionResult),
        };
    }
}
