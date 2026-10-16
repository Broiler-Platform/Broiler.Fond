using System.Collections.ObjectModel;
using System.Globalization;
using System.Security.Cryptography;
using Broiler.Fond.Kernel.Domain.Accounts;
using Broiler.Fond.Kernel.Domain.Connections;
using Broiler.Fond.Kernel.Domain.Identifiers;
using Broiler.Fond.Kernel.FinTs.Transport;
using Broiler.Fond.Kernel.Product;

namespace Broiler.Fond.Kernel.FinTs;

/// <summary>Specifies the operational read category to execute in the opened FinTS dialogue.</summary>
public enum FinTsReadExecutionKind
{
    /// <summary>All-account discovery (HKSPA) to locate and bind all customer accounts.</summary>
    AllAccountDiscovery,

    /// <summary>Balance refresh (HKSAL) for a specific bound account, including pagination.</summary>
    BalanceRefresh,
}

/// <summary>Represents the terminal outcome status of a FinTS read execution pipeline run.</summary>
public enum FinTsReadExecutionStatus
{
    /// <summary>The read operation completed, dialogue closed cleanly, and data was ingested into the store.</summary>
    Completed,

    /// <summary>Execution was rejected because the bank connection is disabled.</summary>
    RejectedDisabledConnection,

    /// <summary>Execution was rejected because the bank connection is not synchronized.</summary>
    RejectedUnsynchronizedConnection,

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

    /// <summary>The dialogue completed but store ingestion was rejected or encountered an unresolvable conflict.</summary>
    RejectedIngestionFailure,

    /// <summary>The execution was cancelled by the caller.</summary>
    Cancelled,
}

/// <summary>Configures the operational parameters and candidate builders for a FinTS read execution run.</summary>
public sealed class FinTsReadExecutionOptions
{
    public FinTsReadExecutionKind Kind { get; init; } = FinTsReadExecutionKind.AllAccountDiscovery;
    public KnownAccountBinding? TargetAccountBinding { get; init; }
    public TimeSpan Timeout { get; init; } = TimeSpan.FromSeconds(60);
    public int MaximumBalancePages { get; init; } = 16;
    public TimeProvider? Clock { get; init; }

    /// <summary>The registered FinTS product identity used for initialization and capability negotiation.</summary>
    public FinTsProductRegistration ProductRegistration { get; init; } = KernelProduct.FinTsRegistration;

    /// <summary>
    /// Optional interactive challenge handler invoked when a bank returns an SCA challenge (HITAN with TAN prompt).
    /// The handler receives the challenge context and matching procedure, prompts the user, and returns a transient TAN credential.
    /// If null, or if the user cancels (returns null), the pipeline aborts cleanly without persisting any credentials.
    /// </summary>
    public Func<FinTsTanChallenge, FinTsTanProcedure?, CancellationToken, ValueTask<FinTsSessionCredential?>>? ScaChallengeHandler { get; init; }

    /// <summary>Optional custom candidate factory for the SCA continuation request (e.g. HKTAN process 2 or S).</summary>
    public Func<FinTsScaContinuation, FinTsSessionCredential, FinTsPinTanRequestBinding>? ScaContinuationCandidateFactory { get; init; }

    /// <summary>Maximum number of decoupled status queries to execute before failing (default 32).</summary>
    public int MaximumStatusQueries { get; init; } = 32;

    /// <summary>Whether automatic decoupled polling is allowed (default true).</summary>
    public bool AllowAutomaticQueries { get; init; } = true;

    /// <summary>Optional custom candidate factory for client message 1 (initialization).</summary>
    public Func<BankConnection, FinTsPinTanRequestBinding>? InitializationCandidateFactory { get; init; }

    /// <summary>Optional custom candidate factory for all-account discovery.</summary>
    public Func<FinTsPinTanReadSessionAttempt, FinTsPinTanAllAccountRequestBinding>? AllAccountCandidateFactory { get; init; }

    /// <summary>Optional custom candidate factory for single-account balance first read.</summary>
    public Func<FinTsPinTanReadSessionAttempt, FinTsPinTanReadRequestBinding>? BalanceFirstReadCandidateFactory { get; init; }

    /// <summary>Optional custom candidate factory for balance continuation pages.</summary>
    public Func<FinTsPinTanReadSessionAttempt, FinTsPinTanReadContinuationRequestBinding>? BalanceContinuationCandidateFactory { get; init; }

    /// <summary>Optional custom candidate factory for dialogue closing (HKEND).</summary>
    public Func<FinTsPinTanReadSessionAttempt, FinTsPinTanDialogueEndRequestBinding>? ClosingCandidateFactory { get; init; }

    public static FinTsReadExecutionOptions Discovery(
        Func<BankConnection, FinTsPinTanRequestBinding>? initFactory = null,
        Func<FinTsPinTanReadSessionAttempt, FinTsPinTanAllAccountRequestBinding>? discoveryFactory = null,
        Func<FinTsPinTanReadSessionAttempt, FinTsPinTanDialogueEndRequestBinding>? closingFactory = null,
        Func<FinTsTanChallenge, FinTsTanProcedure?, CancellationToken, ValueTask<FinTsSessionCredential?>>? scaChallengeHandler = null,
        Func<FinTsScaContinuation, FinTsSessionCredential, FinTsPinTanRequestBinding>? scaContinuationFactory = null,
        FinTsProductRegistration? productRegistration = null,
        int maximumStatusQueries = 32,
        bool allowAutomaticQueries = true,
        TimeSpan? timeout = null,
        TimeProvider? clock = null) => new()
        {
            Kind = FinTsReadExecutionKind.AllAccountDiscovery,
            InitializationCandidateFactory = initFactory,
            AllAccountCandidateFactory = discoveryFactory,
            ClosingCandidateFactory = closingFactory,
            ScaChallengeHandler = scaChallengeHandler,
            ScaContinuationCandidateFactory = scaContinuationFactory,
            ProductRegistration = productRegistration ?? KernelProduct.FinTsRegistration,
            MaximumStatusQueries = maximumStatusQueries,
            AllowAutomaticQueries = allowAutomaticQueries,
            Timeout = timeout ?? TimeSpan.FromSeconds(60),
            Clock = clock,
        };

    public static FinTsReadExecutionOptions Balance(
        KnownAccountBinding targetBinding,
        Func<BankConnection, FinTsPinTanRequestBinding>? initFactory = null,
        Func<FinTsPinTanReadSessionAttempt, FinTsPinTanReadRequestBinding>? balanceFactory = null,
        Func<FinTsPinTanReadSessionAttempt, FinTsPinTanReadContinuationRequestBinding>? continuationFactory = null,
        Func<FinTsPinTanReadSessionAttempt, FinTsPinTanDialogueEndRequestBinding>? closingFactory = null,
        Func<FinTsTanChallenge, FinTsTanProcedure?, CancellationToken, ValueTask<FinTsSessionCredential?>>? scaChallengeHandler = null,
        Func<FinTsScaContinuation, FinTsSessionCredential, FinTsPinTanRequestBinding>? scaContinuationFactory = null,
        FinTsProductRegistration? productRegistration = null,
        int maximumStatusQueries = 32,
        bool allowAutomaticQueries = true,
        TimeSpan? timeout = null,
        int maximumBalancePages = 16,
        TimeProvider? clock = null) => new()
        {
            Kind = FinTsReadExecutionKind.BalanceRefresh,
            TargetAccountBinding = targetBinding ?? throw new ArgumentNullException(nameof(targetBinding)),
            InitializationCandidateFactory = initFactory,
            BalanceFirstReadCandidateFactory = balanceFactory,
            BalanceContinuationCandidateFactory = continuationFactory,
            ClosingCandidateFactory = closingFactory,
            ScaChallengeHandler = scaChallengeHandler,
            ScaContinuationCandidateFactory = scaContinuationFactory,
            ProductRegistration = productRegistration ?? KernelProduct.FinTsRegistration,
            MaximumStatusQueries = maximumStatusQueries,
            AllowAutomaticQueries = allowAutomaticQueries,
            Timeout = timeout ?? TimeSpan.FromSeconds(60),
            MaximumBalancePages = maximumBalancePages,
            Clock = clock,
        };
}

/// <summary>Immutable result of a FinTS read execution pipeline run.</summary>
public sealed class FinTsReadExecutionResult
{
    private FinTsReadExecutionResult(
        FinTsReadExecutionStatus status,
        BankConnection updatedConnection,
        FinTsPinTanReadSessionResult? sessionResult,
        FinTsReadIngestionResult? ingestionResult,
        IList<string> issues)
    {
        Status = status;
        UpdatedConnection = updatedConnection;
        SessionResult = sessionResult;
        IngestionResult = ingestionResult;
        Issues = new ReadOnlyCollection<string>(issues);
    }

    public FinTsReadExecutionStatus Status { get; }
    public bool IsSuccess => Status == FinTsReadExecutionStatus.Completed;
    public BankConnection UpdatedConnection { get; }
    public FinTsPinTanReadSessionResult? SessionResult { get; }
    public FinTsReadIngestionResult? IngestionResult { get; }
    public ReadOnlyCollection<string> Issues { get; }

    public static FinTsReadExecutionResult Completed(
        BankConnection updatedConnection,
        FinTsPinTanReadSessionResult sessionResult,
        FinTsReadIngestionResult ingestionResult)
    {
        ArgumentNullException.ThrowIfNull(updatedConnection);
        ArgumentNullException.ThrowIfNull(sessionResult);
        ArgumentNullException.ThrowIfNull(ingestionResult);

        return new(FinTsReadExecutionStatus.Completed, updatedConnection, sessionResult, ingestionResult, Array.Empty<string>());
    }

    public static FinTsReadExecutionResult Failure(
        FinTsReadExecutionStatus status,
        BankConnection connection,
        string reason,
        FinTsPinTanReadSessionResult? sessionResult = null,
        FinTsReadIngestionResult? ingestionResult = null,
        IList<string>? additionalIssues = null)
    {
        ArgumentNullException.ThrowIfNull(connection);
        List<string> issues = [reason];
        if (additionalIssues is not null)
        {
            issues.AddRange(additionalIssues);
        }

        return new(status, connection, sessionResult, ingestionResult, issues);
    }
}

/// <summary>
/// End-to-end FinTS read execution pipeline coordinating transport, session attempt state machine,
/// dialogue sequencing, response accounting, and local store ingestion.
/// Transient credentials (PIN/TAN) are cleared immediately from buffers and never persisted.
/// </summary>
public static class FinTsReadExecutionPipeline
{
    /// <summary>
    /// Executes a complete FinTS read session dialogue (initialization, operational read, continuation, closing)
    /// over the supplied transport, ingesting resulting accounts and balances into the identity ledger.
    /// </summary>
    public static async ValueTask<FinTsReadExecutionResult> ExecuteAsync(
        BankConnection connection,
        IFinTsTransport transport,
        FinTsSessionCredential credential,
        LocalIdentityLedger ledger,
        IReadOnlyList<KnownAccountBinding> knownBindings,
        FinTsReadExecutionOptions options,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(connection);
        ArgumentNullException.ThrowIfNull(transport);
        ArgumentNullException.ThrowIfNull(credential);
        ArgumentNullException.ThrowIfNull(ledger);
        ArgumentNullException.ThrowIfNull(knownBindings);
        ArgumentNullException.ThrowIfNull(options);

        var clock = options.Clock ?? TimeProvider.System;
        DateTimeOffset now = clock.GetUtcNow();

        // 1. Connection and credential preflight validation
        if (connection.State == BankConnectionState.Disabled)
        {
            return FinTsReadExecutionResult.Failure(
                FinTsReadExecutionStatus.RejectedDisabledConnection,
                connection.WithAttemptedContact(now),
                "Connection is disabled; read execution rejected.");
        }

        if (!connection.IsSynchronized && connection.State != BankConnectionState.Connected)
        {
            return FinTsReadExecutionResult.Failure(
                FinTsReadExecutionStatus.RejectedUnsynchronizedConnection,
                connection.WithAttemptedContact(now),
                "Connection must be synchronized before executing a read session.");
        }

        if (!connection.InstitutionConfiguration.IsEndpointConfirmed)
        {
            return FinTsReadExecutionResult.Failure(
                FinTsReadExecutionStatus.RejectedInvalidEndpoint,
                connection.WithAttemptedContact(now),
                "Connection endpoint is unconfirmed; read execution rejected.");
        }

        Uri endpoint = connection.InstitutionConfiguration.FinTsEndpoint;

        var credSnapshot = credential.GetSnapshot();
        if (credSnapshot.Kind != FinTsCredentialKind.Pin || credSnapshot.State != FinTsCredentialState.Available)
        {
            return FinTsReadExecutionResult.Failure(
                FinTsReadExecutionStatus.RejectedInvalidCredential,
                connection.WithAttemptedContact(now),
                "A valid, available PIN credential is required.");
        }

        if (options.Kind == FinTsReadExecutionKind.BalanceRefresh && options.TargetAccountBinding is null)
        {
            return FinTsReadExecutionResult.Failure(
                FinTsReadExecutionStatus.RejectedMissingCandidate,
                connection.WithAttemptedContact(now),
                "A target account binding is required for balance refresh.");
        }

        // 2. Open session coordinator attempt
        using var attempt = new FinTsPinTanReadSessionAttempt(options.Timeout, options.MaximumBalancePages, clock);

        try
        {
            cancellationToken.ThrowIfCancellationRequested();

            // -------------------------------------------------------------
            // Stage 1: Initialization
            // -------------------------------------------------------------
            if (options.InitializationCandidateFactory is null)
            {
                return FinTsReadExecutionResult.Failure(
                    FinTsReadExecutionStatus.RejectedMissingCandidate,
                    connection.WithAttemptedContact(now),
                    "No initialization candidate factory configured.");
            }

            FinTsPinTanRequestBinding initCandidate;
            try
            {
                initCandidate = options.InitializationCandidateFactory(connection);
            }
            catch (Exception ex)
            {
                return FinTsReadExecutionResult.Failure(
                    FinTsReadExecutionStatus.RejectedMissingCandidate,
                    connection.WithAttemptedContact(now),
                    $"Failed to build initialization candidate: {ex.Message}");
            }

            var startTransition = attempt.StartInitialization(initCandidate, cancellationToken);
            if (startTransition != FinTsPinTanReadSessionTransition.CandidateRecorded)
            {
                return MapFailedTransition(attempt, connection, startTransition, now);
            }

            FinTsPinTanReadSessionResult initResult;
            // Encode initialization envelope
            byte[] initBuffer = new byte[FinTsPinTanRequestEnvelopeWriter.MaximumEncodedLength];
            int initWritten;
            try
            {
                var writeResult = FinTsPinTanRequestEnvelopeWriter.TryEncode(
                    initCandidate.SignatureEvidence, credential, null, initBuffer, out initWritten, cancellationToken);

                if (writeResult != FinTsPinTanRequestWriteResult.Written)
                {
                    return FinTsReadExecutionResult.Failure(
                        FinTsReadExecutionStatus.RejectedProtocolFailure,
                        connection.WithAttemptedContact(now),
                        $"Failed to encode initialization request envelope: {writeResult}");
                }

                // Transmit initialization
                var initTransportResp = await transport.SendAsync(
                    endpoint, initBuffer.AsMemory(0, initWritten), cancellationToken).ConfigureAwait(false);

                if (!initTransportResp.IsSuccess)
                {
                    return FinTsReadExecutionResult.Failure(
                        FinTsReadExecutionStatus.RejectedTransportFailure,
                        connection.WithAttemptedContact(now),
                        $"Initialization transport error: HTTP {initTransportResp.StatusCode}");
                }

                // Parse initialization response
                var initResp = ParseWireResponse(initTransportResp.Payload.Span, cancellationToken);
                var initChallengeSet = FinTsTanChallengeSet.Parse(initResp, cancellationToken);
                if (initChallengeSet.Challenges.Count > 0)
                {
                    FinTsTanParameterSet? procs = null;
                    FinTsPermittedProcedureSet? perms = null;
                    try { procs = FinTsTanParameterSet.Parse(FinTsParameterSet.Parse(initResp, cancellationToken), cancellationToken); } catch { }
                    try { perms = FinTsPermittedProcedureSet.Parse(initResp, cancellationToken); } catch { }

                    if (procs is not null && perms is not null && procs.Advertisements.Count > 0 && procs.Advertisements[0].Procedures.Count > 0)
                    {
                        var matchingProc = procs.Advertisements[0].Procedures.FirstOrDefault(p => perms.Reports.Any(r => r.SecurityFunctions.Contains(p.SecurityFunction)))
                            ?? procs.Advertisements[0].Procedures[0];

                        var initReqFrame = initCandidate.SignatureEvidence.Request.Frame;
                        FinTsTanRequestContext initReqContext;
                        if (initReqFrame.Syntax.Segments.Any(s => s.Code == "HKTAN"))
                        {
                            initReqContext = FinTsTanRequestContext.Parse(initReqFrame, 2, 1, cancellationToken);
                        }
                        else
                        {
                            initReqContext = FinTsScaExecutionCoordinator.CreateRequestContext(
                                "0", 1, 1, matchingProc.SegmentVersion, "HKIDN", cancellationToken: cancellationToken);
                        }

                        var initScaEvidence = FinTsTanChallengeEvidence.Evaluate(initReqContext, procs, matchingProc, perms, initChallengeSet, cancellationToken);
                        if (initScaEvidence.Observation == FinTsTanOutcomeObservation.ChallengeReported)
                        {
                            connection = connection.WithState(BankConnectionState.ChallengeRequired, now);
                            var scaResult = await FinTsScaExecutionCoordinator.ExecuteAsync(
                                initScaEvidence, transport, endpoint, credential,
                                options.Timeout, options.MaximumStatusQueries, options.AllowAutomaticQueries,
                                options.ScaChallengeHandler, options.ScaContinuationCandidateFactory,
                                clock, cancellationToken).ConfigureAwait(false);

                            if (scaResult.IsCancelled)
                            {
                                return FinTsReadExecutionResult.Failure(
                                    FinTsReadExecutionStatus.Cancelled,
                                    connection.WithAttemptedContact(now),
                                    scaResult.FailureReason ?? "User cancelled interactive SCA challenge.");
                            }

                            if (!scaResult.IsSuccess)
                            {
                                return FinTsReadExecutionResult.Failure(
                                    FinTsReadExecutionStatus.RejectedDialogueOutcome,
                                    connection.WithState(BankConnectionState.ReauthenticationRequired, now),
                                    scaResult.FailureReason ?? "SCA continuation failed.");
                            }

                            if (scaResult.TerminalResponse is not null)
                            {
                                initResp = scaResult.TerminalResponse;
                            }
                            connection = connection.WithState(BankConnectionState.Connected, now);
                        }
                    }
                }

                initResult = AcceptInitializationResponse(attempt, initResp, cancellationToken);

                if (initResult.Transition != FinTsPinTanReadSessionTransition.DialogueOpen)
                {
                    return MapFailedTransition(attempt, connection, initResult.Transition, now, initResult);
                }
            }
            finally
            {
                CryptographicOperations.ZeroMemory(initBuffer);
            }

            // -------------------------------------------------------------
            // Stage 2: Operational Read
            // -------------------------------------------------------------
            FinTsPinTanReadSessionResult operationResult;

            if (options.Kind == FinTsReadExecutionKind.AllAccountDiscovery)
            {
                if (options.AllAccountCandidateFactory is null)
                {
                    return FinTsReadExecutionResult.Failure(
                        FinTsReadExecutionStatus.RejectedMissingCandidate,
                        connection.WithAttemptedContact(now),
                        "No all-account candidate factory configured.");
                }

                FinTsPinTanAllAccountRequestBinding discoveryCandidate = options.AllAccountCandidateFactory(attempt);
                var recTrans = attempt.RecordAllAccountCandidate(discoveryCandidate, cancellationToken);
                if (recTrans != FinTsPinTanReadSessionTransition.CandidateRecorded)
                {
                    return MapFailedTransition(attempt, connection, recTrans, now);
                }

                byte[] discoveryBuffer = new byte[FinTsPinTanAllAccountRequestEnvelopeWriter.MaximumEncodedLength];
                int discWritten;
                try
                {
                    var writeResult = FinTsPinTanAllAccountRequestEnvelopeWriter.TryEncodePinOnly(
                        discoveryCandidate, credential, discoveryBuffer, out discWritten, cancellationToken);

                    if (writeResult != FinTsPinTanRequestWriteResult.Written)
                    {
                        return FinTsReadExecutionResult.Failure(
                            FinTsReadExecutionStatus.RejectedProtocolFailure,
                            connection.WithAttemptedContact(now),
                            $"Failed to encode all-account discovery envelope: {writeResult}");
                    }

                    var discTransportResp = await transport.SendAsync(
                        endpoint, discoveryBuffer.AsMemory(0, discWritten), cancellationToken).ConfigureAwait(false);

                    if (!discTransportResp.IsSuccess)
                    {
                        return FinTsReadExecutionResult.Failure(
                            FinTsReadExecutionStatus.RejectedTransportFailure,
                            connection.WithAttemptedContact(now),
                            $"All-account discovery transport error: HTTP {discTransportResp.StatusCode}");
                    }

                    var discResp = ParseWireResponse(discTransportResp.Payload.Span, cancellationToken);
                    var discChallengeSet = FinTsTanChallengeSet.Parse(discResp, cancellationToken);
                    if (discChallengeSet.Challenges.Count > 0)
                    {
                        var procs = initResult.ProcedureEvidence?.Procedures;
                        var perms = initResult.ProcedureEvidence?.Permissions;
                        if (procs is not null && perms is not null && procs.Advertisements.Count > 0 && procs.Advertisements[0].Procedures.Count > 0)
                        {
                            var matchingProc = procs.Advertisements[0].Procedures.FirstOrDefault(p => perms.Reports.Any(r => r.SecurityFunctions.Contains(p.SecurityFunction)))
                                ?? procs.Advertisements[0].Procedures[0];

                            var discReqFrame = discoveryCandidate.Context.Signature.Request.Frame;
                            FinTsTanRequestContext discReqContext;
                            if (discReqFrame.Syntax.Segments.Any(s => s.Code == "HKTAN"))
                            {
                                discReqContext = FinTsTanRequestContext.Parse(discReqFrame, 2, 2, cancellationToken);
                            }
                            else
                            {
                                string dialogueId = attempt.DialogueId ?? "0";
                                discReqContext = FinTsScaExecutionCoordinator.CreateRequestContext(
                                    dialogueId, discoveryCandidate.MessageNumber, 2, matchingProc.SegmentVersion, "HKSPA", cancellationToken: cancellationToken);
                            }

                            var discScaEvidence = FinTsTanChallengeEvidence.Evaluate(discReqContext, procs, matchingProc, perms, discChallengeSet, cancellationToken);
                            if (discScaEvidence.Observation == FinTsTanOutcomeObservation.ChallengeReported)
                            {
                                connection = connection.WithState(BankConnectionState.ChallengeRequired, now);
                                var scaResult = await FinTsScaExecutionCoordinator.ExecuteAsync(
                                    discScaEvidence, transport, endpoint, credential,
                                    options.Timeout, options.MaximumStatusQueries, options.AllowAutomaticQueries,
                                    options.ScaChallengeHandler, options.ScaContinuationCandidateFactory,
                                    clock, cancellationToken).ConfigureAwait(false);

                                if (scaResult.IsCancelled)
                                {
                                    return FinTsReadExecutionResult.Failure(
                                        FinTsReadExecutionStatus.Cancelled,
                                        connection.WithAttemptedContact(now),
                                        scaResult.FailureReason ?? "User cancelled interactive SCA challenge.");
                                }

                                if (!scaResult.IsSuccess)
                                {
                                    return FinTsReadExecutionResult.Failure(
                                        FinTsReadExecutionStatus.RejectedDialogueOutcome,
                                        connection.WithState(BankConnectionState.ReauthenticationRequired, now),
                                        scaResult.FailureReason ?? "SCA continuation failed.");
                                }

                                if (scaResult.TerminalResponse is not null)
                                {
                                    discResp = scaResult.TerminalResponse;
                                }
                                connection = connection.WithState(BankConnectionState.Connected, now);
                            }
                        }
                    }

                    var readData = FinTsReadDataSet.Parse(discResp, cancellationToken);
                    operationResult = attempt.AcceptAllAccountResponse(readData, cancellationToken);
                }
                finally
                {
                    CryptographicOperations.ZeroMemory(discoveryBuffer);
                }
            }
            else // BalanceRefresh
            {
                if (options.BalanceFirstReadCandidateFactory is null)
                {
                    return FinTsReadExecutionResult.Failure(
                        FinTsReadExecutionStatus.RejectedMissingCandidate,
                        connection.WithAttemptedContact(now),
                        "No balance first-read candidate factory configured.");
                }

                FinTsPinTanReadRequestBinding balanceCandidate = options.BalanceFirstReadCandidateFactory(attempt);
                var recTrans = attempt.RecordBalanceFirstReadCandidate(balanceCandidate, cancellationToken);
                if (recTrans != FinTsPinTanReadSessionTransition.CandidateRecorded)
                {
                    return MapFailedTransition(attempt, connection, recTrans, now);
                }

                byte[] balanceBuffer = new byte[FinTsPinTanReadRequestEnvelopeWriter.MaximumEncodedLength];
                int balWritten;
                try
                {
                    var writeResult = FinTsPinTanReadRequestEnvelopeWriter.TryEncodePinOnly(
                        balanceCandidate, credential, balanceBuffer, out balWritten, cancellationToken);

                    if (writeResult != FinTsPinTanRequestWriteResult.Written)
                    {
                        return FinTsReadExecutionResult.Failure(
                            FinTsReadExecutionStatus.RejectedProtocolFailure,
                            connection.WithAttemptedContact(now),
                            $"Failed to encode balance request envelope: {writeResult}");
                    }

                    var balTransportResp = await transport.SendAsync(
                        endpoint, balanceBuffer.AsMemory(0, balWritten), cancellationToken).ConfigureAwait(false);

                    if (!balTransportResp.IsSuccess)
                    {
                        return FinTsReadExecutionResult.Failure(
                            FinTsReadExecutionStatus.RejectedTransportFailure,
                            connection.WithAttemptedContact(now),
                            $"Balance transport error: HTTP {balTransportResp.StatusCode}");
                    }

                    var balResp = ParseWireResponse(balTransportResp.Payload.Span, cancellationToken);
                    var balChallengeSet = FinTsTanChallengeSet.Parse(balResp, cancellationToken);
                    if (balChallengeSet.Challenges.Count > 0)
                    {
                        var procs = initResult.ProcedureEvidence?.Procedures;
                        var perms = initResult.ProcedureEvidence?.Permissions;
                        if (procs is not null && perms is not null && procs.Advertisements.Count > 0 && procs.Advertisements[0].Procedures.Count > 0)
                        {
                            var matchingProc = procs.Advertisements[0].Procedures.FirstOrDefault(p => perms.Reports.Any(r => r.SecurityFunctions.Contains(p.SecurityFunction)))
                                ?? procs.Advertisements[0].Procedures[0];

                            var balReqFrame = balanceCandidate.Context.Signature.Request.Frame;
                            FinTsTanRequestContext balReqContext;
                            if (balReqFrame.Syntax.Segments.Any(s => s.Code == "HKTAN"))
                            {
                                balReqContext = FinTsTanRequestContext.Parse(balReqFrame, 2, 2, cancellationToken);
                            }
                            else
                            {
                                string dialogueId = attempt.DialogueId ?? "0";
                                balReqContext = FinTsScaExecutionCoordinator.CreateRequestContext(
                                    dialogueId, balanceCandidate.MessageNumber, 2, matchingProc.SegmentVersion, "HKSAL", cancellationToken: cancellationToken);
                            }

                            var balScaEvidence = FinTsTanChallengeEvidence.Evaluate(balReqContext, procs, matchingProc, perms, balChallengeSet, cancellationToken);
                            if (balScaEvidence.Observation == FinTsTanOutcomeObservation.ChallengeReported)
                            {
                                connection = connection.WithState(BankConnectionState.ChallengeRequired, now);
                                var scaResult = await FinTsScaExecutionCoordinator.ExecuteAsync(
                                    balScaEvidence, transport, endpoint, credential,
                                    options.Timeout, options.MaximumStatusQueries, options.AllowAutomaticQueries,
                                    options.ScaChallengeHandler, options.ScaContinuationCandidateFactory,
                                    clock, cancellationToken).ConfigureAwait(false);

                                if (scaResult.IsCancelled)
                                {
                                    return FinTsReadExecutionResult.Failure(
                                        FinTsReadExecutionStatus.Cancelled,
                                        connection.WithAttemptedContact(now),
                                        scaResult.FailureReason ?? "User cancelled interactive SCA challenge.");
                                }

                                if (!scaResult.IsSuccess)
                                {
                                    return FinTsReadExecutionResult.Failure(
                                        FinTsReadExecutionStatus.RejectedDialogueOutcome,
                                        connection.WithState(BankConnectionState.ReauthenticationRequired, now),
                                        scaResult.FailureReason ?? "SCA continuation failed.");
                                }

                                if (scaResult.TerminalResponse is not null)
                                {
                                    balResp = scaResult.TerminalResponse;
                                }
                                connection = connection.WithState(BankConnectionState.Connected, now);
                            }
                        }
                    }

                    var readData = FinTsReadDataSet.Parse(balResp, cancellationToken);
                    operationResult = attempt.AcceptBalanceResponse(readData, cancellationToken);
                }
                finally
                {
                    CryptographicOperations.ZeroMemory(balanceBuffer);
                }

                // Balance pagination loop
                while (attempt.GetSnapshot().State == FinTsPinTanReadSessionState.WaitingForBalanceContinuation &&
                       attempt.CurrentContinuationToken is not null)
                {
                    cancellationToken.ThrowIfCancellationRequested();

                    if (options.BalanceContinuationCandidateFactory is null)
                    {
                        break;
                    }

                    FinTsPinTanReadContinuationRequestBinding contCandidate = options.BalanceContinuationCandidateFactory(attempt);
                    var contTrans = attempt.RecordBalanceContinuationCandidate(contCandidate, cancellationToken);
                    if (contTrans != FinTsPinTanReadSessionTransition.CandidateRecorded)
                    {
                        break;
                    }

                    byte[] contBuffer = new byte[FinTsPinTanReadContinuationRequestEnvelopeWriter.MaximumEncodedLength];
                    int contWritten;
                    try
                    {
                        var writeResult = FinTsPinTanReadContinuationRequestEnvelopeWriter.TryEncodePinOnly(
                            contCandidate, credential, contBuffer, out contWritten, cancellationToken);

                        if (writeResult != FinTsPinTanRequestWriteResult.Written)
                        {
                            break;
                        }

                        var contTransportResp = await transport.SendAsync(
                            endpoint, contBuffer.AsMemory(0, contWritten), cancellationToken).ConfigureAwait(false);

                        if (!contTransportResp.IsSuccess)
                        {
                            break;
                        }

                        var contResp = ParseWireResponse(contTransportResp.Payload.Span, cancellationToken);
                        var contReadData = FinTsReadDataSet.Parse(contResp, cancellationToken);
                        operationResult = attempt.AcceptBalanceResponse(contReadData, cancellationToken);
                    }
                    finally
                    {
                        CryptographicOperations.ZeroMemory(contBuffer);
                    }
                }
            }

            if (operationResult.Transition is not (FinTsPinTanReadSessionTransition.ExecutionReported
                or FinTsPinTanReadSessionTransition.PageLimitReached))
            {
                return MapFailedTransition(attempt, connection, operationResult.Transition, now, operationResult);
            }

            // -------------------------------------------------------------
            // Stage 3: Dialogue Closing
            // -------------------------------------------------------------
            FinTsPinTanDialogueEndRequestBinding closingCandidate = options.ClosingCandidateFactory is not null
                ? options.ClosingCandidateFactory(attempt)
                : CreateDefaultClosingCandidate(attempt, cancellationToken);

            var closeTrans = attempt.RecordClosingCandidate(closingCandidate, cancellationToken);
            if (closeTrans != FinTsPinTanReadSessionTransition.CandidateRecorded)
            {
                return MapFailedTransition(attempt, connection, closeTrans, now);
            }

            byte[] closingBuffer = new byte[FinTsPinTanDialogueEndWriter.MaximumEncodedLength];
            int closeWritten;
            FinTsPinTanReadSessionResult closingResult;
            try
            {
                var writeResult = FinTsPinTanDialogueEndWriter.TryEncode(
                    closingCandidate.Context, credential, closingBuffer, out closeWritten, cancellationToken);

                if (writeResult != FinTsPinTanRequestWriteResult.Written)
                {
                    return FinTsReadExecutionResult.Failure(
                        FinTsReadExecutionStatus.RejectedProtocolFailure,
                        connection.WithAttemptedContact(now),
                        $"Failed to encode dialogue closing envelope: {writeResult}");
                }

                var closeTransportResp = await transport.SendAsync(
                    endpoint, closingBuffer.AsMemory(0, closeWritten), cancellationToken).ConfigureAwait(false);

                if (!closeTransportResp.IsSuccess)
                {
                    return FinTsReadExecutionResult.Failure(
                        FinTsReadExecutionStatus.RejectedTransportFailure,
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
            // Stage 4: Terminal Evaluation & Store Ingestion
            // -------------------------------------------------------------
            if (closingResult.Transition != FinTsPinTanReadSessionTransition.Closed)
            {
                return MapFailedTransition(attempt, connection, closingResult.Transition, now, closingResult);
            }

            // Synthesize consolidated verified session result for ingestion
            var consolidatedResult = FinTsPinTanReadSessionResult.Create(
                FinTsPinTanReadSessionTransition.ExecutionReported,
                FinTsPinTanReadSessionStage.Terminal,
                requirementsEvidence: initResult.RequirementsEvidence ?? operationResult.RequirementsEvidence,
                allAccountEvidence: operationResult.AllAccountEvidence,
                balanceFirstPageEvidence: operationResult.BalanceFirstPageEvidence,
                balanceContinuationEvidence: operationResult.BalanceContinuationEvidence,
                aggregatedBalances: operationResult.AggregatedBalances,
                closingEvidence: closingResult.ClosingEvidence,
                procedureEvidence: initResult.ProcedureEvidence ?? operationResult.ProcedureEvidence,
                initializationEvidence: initResult.InitializationEvidence ?? operationResult.InitializationEvidence);

            var ingestionResult = FinTsReadIngestionApplication.Apply(
                connection,
                consolidatedResult,
                knownBindings,
                ledger,
                now,
                cancellationToken);

            if (!ingestionResult.IsSuccess)
            {
                return FinTsReadExecutionResult.Failure(
                    FinTsReadExecutionStatus.RejectedIngestionFailure,
                    ingestionResult.UpdatedConnection,
                    $"Read store ingestion rejected with status {ingestionResult.Status}.",
                    consolidatedResult,
                    ingestionResult,
                    ingestionResult.Issues);
            }

            return FinTsReadExecutionResult.Completed(
                ingestionResult.UpdatedConnection,
                consolidatedResult,
                ingestionResult);
        }
        catch (OperationCanceledException)
        {
            return FinTsReadExecutionResult.Failure(
                FinTsReadExecutionStatus.Cancelled,
                connection.WithAttemptedContact(now),
                "Read execution was cancelled.");
        }
        catch (FinTsTransportException ex)
        {
            return FinTsReadExecutionResult.Failure(
                FinTsReadExecutionStatus.RejectedTransportFailure,
                connection.WithAttemptedContact(now),
                $"Transport error ({ex.Issue}): {ex.Message}");
        }
        catch (FinTsFormatException ex)
        {
            return FinTsReadExecutionResult.Failure(
                FinTsReadExecutionStatus.RejectedProtocolFailure,
                connection.WithAttemptedContact(now),
                $"Protocol framing error: {ex.Message}");
        }
        catch (Exception ex)
        {
            return FinTsReadExecutionResult.Failure(
                FinTsReadExecutionStatus.RejectedProtocolFailure,
                connection.WithAttemptedContact(now),
                $"Unexpected execution failure: {ex.Message}");
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

    private static FinTsPinTanReadSessionResult AcceptInitializationResponse(
        FinTsPinTanReadSessionAttempt attempt,
        FinTsResponse response,
        CancellationToken cancellationToken)
    {
        FinTsParameterSet parameters = FinTsParameterSet.Parse(response, cancellationToken);
        FinTsTanParameterSet? procedures = null;
        FinTsPermittedProcedureSet? permissions = null;
        FinTsPinTanParameterSet? pinTan = null;
        FinTsReadParameterSet? reads = null;

        try { procedures = FinTsTanParameterSet.Parse(parameters, cancellationToken); } catch { }
        try { permissions = FinTsPermittedProcedureSet.Parse(response, cancellationToken); } catch { }
        try { pinTan = FinTsPinTanParameterSet.Parse(parameters, cancellationToken); } catch { }
        if (pinTan is not null)
        {
            try { reads = FinTsReadParameterSet.Parse(pinTan.Source, cancellationToken); } catch { }
        }

        if (procedures is not null && permissions is not null && pinTan is not null && reads is not null)
        {
            return attempt.AcceptInitializationResponse(procedures, permissions, pinTan, reads, cancellationToken);
        }

        if (procedures is not null && permissions is not null && pinTan is not null)
        {
            return attempt.AcceptInitializationResponse(procedures, permissions, pinTan, cancellationToken);
        }

        if (procedures is not null && permissions is not null)
        {
            return attempt.AcceptInitializationResponse(procedures, permissions, cancellationToken);
        }

        return attempt.AcceptInitializationResponse(parameters, cancellationToken);
    }

    /// <summary>Constructs a default dialogue end candidate (HKEND) derived from initialization evidence and session counters.</summary>
    public static FinTsPinTanDialogueEndRequestBinding CreateDefaultClosingCandidate(
        FinTsPinTanReadSessionAttempt attempt,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(attempt);
        var initEvidence = attempt.InitializationEvidence
            ?? throw new InvalidOperationException("Session has no accepted initialization evidence.");

        var snapshot = attempt.GetSnapshot();
        int clientMsg = snapshot.ClientMessageNumber + 1;
        int expectedBankMsg = snapshot.BankMessageNumber + 1;
        string dialogueId = attempt.DialogueId ?? "0";

        byte[] unsignedEnd = FinTsUnsignedDialogueEndWriter.Encode(dialogueId, clientMsg, cancellationToken);
        var endFrame = FinTsMessageFrame.Parse(unsignedEnd, cancellationToken);
        var endRequest = FinTsUnsignedDialogueEndRequest.Parse(endFrame, expectedBankMsg, cancellationToken);

        var prevHeader = initEvidence.Request.SignatureEvidence.Header;
        var headerInput = new FinTsPinTanSignatureHeaderInput(
            profileVersion: prevHeader.ProfileVersion,
            securityFunction: int.Parse(prevHeader.SecurityFunction, CultureInfo.InvariantCulture),
            controlReference: "1",
            securitySupplierRole: 1,
            securityParty: 1,
            systemId: prevHeader.SystemId,
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

        var context = FinTsPinTanDialogueEndContext.EvaluateForInitialization(
            initEvidence, endRequest, header, clientMsg, expectedBankMsg, cancellationToken);

        return FinTsPinTanDialogueEndRequestBinding.ForEnvelopeCandidate(context, cancellationToken);
    }

    /// <summary>
    /// Constructs a default initialization candidate (HKIDN + HKVVB) with registered product identity and procedure evidence.
    /// Uses the registered product ID and version before any external live-bank dialogue.
    /// </summary>
    public static FinTsPinTanRequestBinding CreateDefaultInitializationCandidate(
        BankConnection connection,
        FinTsPinTanProcedureContext procedureContext,
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
        string systemId = connection.SystemId ?? "0";
        var systemStatus = systemId != "0" ? FinTsCustomerSystemStatus.Required : FinTsCustomerSystemStatus.NotRequired;

        var input = new FinTsInitializationInput(
            country: country,
            institution: institution,
            customerId: customerId,
            systemId: systemId,
            systemStatus: systemStatus,
            bankParameterVersion: 0,
            userParameterVersion: 0,
            language: FinTsDialogueLanguage.German,
            productIdentifier: reg.ProductIdentifier,
            productVersion: reg.ProductVersion);

        byte[] unsignedWire = FinTsUnsignedInitializationWriter.Encode(input, cancellationToken);
        var frame = FinTsMessageFrame.Parse(unsignedWire, cancellationToken);
        var unsignedInit = FinTsUnsignedInitializationRequest.Parse(frame, cancellationToken);

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

        var requestContext = FinTsPinTanSignatureRequestContext.ForInitialization(
            unsignedInit, connection.UserId, "1", selection);

        var signatureEvidence = FinTsPinTanSignatureEvidence.Evaluate(
            requestContext, header, procedureContext, cancellationToken);

        return FinTsPinTanRequestBinding.ForEnvelopeCandidate(signatureEvidence, cancellationToken);
    }

    private static FinTsReadExecutionResult MapFailedTransition(
        FinTsPinTanReadSessionAttempt attempt,
        BankConnection connection,
        FinTsPinTanReadSessionTransition transition,
        DateTimeOffset now,
        FinTsPinTanReadSessionResult? sessionResult = null)
    {
        return transition switch
        {
            FinTsPinTanReadSessionTransition.AbortReported =>
                FinTsReadExecutionResult.Failure(
                    FinTsReadExecutionStatus.RejectedProtocolFailure,
                    connection.WithState(BankConnectionState.TemporarilyUnavailable, now),
                    "Bank reported dialogue abort (code 9800); connection temporarily unavailable.",
                    sessionResult),

            FinTsPinTanReadSessionTransition.RejectedForReview or
            FinTsPinTanReadSessionTransition.ContextMismatch =>
                FinTsReadExecutionResult.Failure(
                    FinTsReadExecutionStatus.RejectedDialogueOutcome,
                    connection.WithState(BankConnectionState.ReauthenticationRequired, now),
                    $"Dialogue outcome requires authentication review ({transition}).",
                    sessionResult),

            FinTsPinTanReadSessionTransition.ReplayRejected =>
                FinTsReadExecutionResult.Failure(
                    FinTsReadExecutionStatus.RejectedProtocolFailure,
                    connection.WithAttemptedContact(now),
                    "Replayed bank message counter detected.",
                    sessionResult),

            _ =>
                FinTsReadExecutionResult.Failure(
                    FinTsReadExecutionStatus.RejectedProtocolFailure,
                    connection.WithAttemptedContact(now),
                    $"Read session transitioned to unexpected outcome: {transition}.",
                    sessionResult),
        };
    }
}
