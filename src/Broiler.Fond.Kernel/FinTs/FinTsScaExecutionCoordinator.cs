using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Broiler.Fond.Kernel.Domain.Connections;
using Broiler.Fond.Kernel.FinTs.Transport;

namespace Broiler.Fond.Kernel.FinTs;

/// <summary>
/// Immutable outcome of an SCA continuation round-trip executed over transport.
/// </summary>
public sealed class FinTsScaExecutionResult
{
    private FinTsScaExecutionResult(
        bool isSuccess,
        bool isExempt,
        bool isCancelled,
        string? failureReason,
        FinTsResponse? terminalResponse,
        FinTsTanChallengeEvidence? challengeEvidence,
        int queriesRecorded)
    {
        IsSuccess = isSuccess;
        IsExempt = isExempt;
        IsCancelled = isCancelled;
        FailureReason = failureReason;
        TerminalResponse = terminalResponse;
        ChallengeEvidence = challengeEvidence;
        QueriesRecorded = queriesRecorded;
    }

    public bool IsSuccess { get; }
    public bool IsExempt { get; }
    public bool IsCancelled { get; }
    public string? FailureReason { get; }
    public FinTsResponse? TerminalResponse { get; }
    public FinTsTanChallengeEvidence? ChallengeEvidence { get; }
    public int QueriesRecorded { get; }

    public static FinTsScaExecutionResult Success(FinTsResponse? response, FinTsTanChallengeEvidence? evidence, int queries) =>
        new(true, false, false, null, response, evidence, queries);

    public static FinTsScaExecutionResult Exempt(FinTsTanChallengeEvidence evidence) =>
        new(true, true, false, null, null, evidence, 0);

    public static FinTsScaExecutionResult Cancelled(string reason) =>
        new(false, false, true, reason, null, null, 0);

    public static FinTsScaExecutionResult Failure(string reason, FinTsTanChallengeEvidence? evidence = null, int queries = 0) =>
        new(false, false, false, reason, null, evidence, queries);
}

/// <summary>
/// Coordinates in-memory SCA continuation. Unsupported signed adapters fail before dispatch.
/// Owns returned TAN credentials and temporary wire buffers; the caller retains ownership of the PIN.
/// </summary>
public static class FinTsScaExecutionCoordinator
{
    /// <summary>
    /// Encodes an unsigned FinTS status query frame (HKTAN process "S") with exact framing and 12-digit byte size.
    /// </summary>
    public static byte[] EncodeStatusQuery(
        string dialogueId,
        int clientMessageNumber,
        int segmentVersion,
        string orderReference,
        string? mediumName = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(dialogueId);
        ArgumentException.ThrowIfNullOrWhiteSpace(orderReference);
        cancellationToken.ThrowIfCancellationRequested();

        var body = new StringBuilder("HKTAN:2:")
            .Append(segmentVersion.ToString(CultureInfo.InvariantCulture))
            .Append("+S++++")
            .Append(orderReference)
            .Append("+N");

        if (!string.IsNullOrEmpty(mediumName))
        {
            body.Append("+++++").Append(mediumName);
        }

        body.Append('\'');
        return FinTsUnsignedWireEncoding.Frame(dialogueId, clientMessageNumber, body, 3);
    }

    /// <summary>
    /// Encodes an unsigned FinTS TAN continuation frame (HKTAN process "2") for two-step confirmation.
    /// </summary>
    public static byte[] EncodeTanContinuation(
        string dialogueId,
        int clientMessageNumber,
        int segmentVersion,
        string orderReference,
        string? operation = null,
        string? mediumName = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(dialogueId);
        ArgumentException.ThrowIfNullOrWhiteSpace(orderReference);
        cancellationToken.ThrowIfCancellationRequested();

        var body = new StringBuilder("HKTAN:2:")
            .Append(segmentVersion.ToString(CultureInfo.InvariantCulture))
            .Append("+2+");

        if (!string.IsNullOrEmpty(operation))
        {
            body.Append(operation);
        }

        body.Append("+++")
            .Append(orderReference)
            .Append("+N");

        if (!string.IsNullOrEmpty(mediumName))
        {
            body.Append("+++++").Append(mediumName);
        }

        body.Append('\'');
        return FinTsUnsignedWireEncoding.Frame(dialogueId, clientMessageNumber, body, 3);
    }

    /// <summary>
    /// Constructs a synthetic HKTAN request context (HKTAN process "4") representing the initiating order.
    /// </summary>
    public static FinTsTanRequestContext CreateRequestContext(
        string dialogueId,
        int clientMessageNumber,
        int expectedBankMessageNumber,
        int segmentVersion,
        string operationCode,
        string? orderReference = null,
        string? mediumName = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(dialogueId);
        ArgumentException.ThrowIfNullOrWhiteSpace(operationCode);
        cancellationToken.ThrowIfCancellationRequested();

        var body = new StringBuilder("HKTAN:2:")
            .Append(segmentVersion.ToString(CultureInfo.InvariantCulture))
            .Append("+4+")
            .Append(operationCode);

        if (!string.IsNullOrEmpty(orderReference))
        {
            body.Append("+++").Append(orderReference);
        }

        if (!string.IsNullOrEmpty(mediumName))
        {
            body.Append("++++++").Append(mediumName);
        }

        body.Append('\'');
        byte[] wire = FinTsUnsignedWireEncoding.Frame(dialogueId, clientMessageNumber, body, 3);
        var frame = FinTsMessageFrame.Parse(wire, cancellationToken);
        return FinTsTanRequestContext.Parse(frame, 2, expectedBankMessageNumber, cancellationToken);
    }

    /// <summary>
    /// Executes SCA challenge continuation for an initial or operational challenge evidence instance.
    /// </summary>
    public static async ValueTask<FinTsScaExecutionResult> ExecuteAsync(
        FinTsTanChallengeEvidence initialEvidence,
        IFinTsTransport transport,
        Uri endpoint,
        FinTsSessionCredential pin,
        TimeSpan timeout,
        int maximumStatusQueries,
        bool allowAutomaticQueries,
        Func<FinTsTanChallenge, FinTsTanProcedure?, CancellationToken, ValueTask<FinTsSessionCredential?>>? scaChallengeHandler,
        Func<FinTsScaContinuation, FinTsSessionCredential, FinTsPinTanRequestBinding>? scaContinuationCandidateFactory,
        TimeProvider? clock = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(initialEvidence);
        ArgumentNullException.ThrowIfNull(transport);
        ArgumentNullException.ThrowIfNull(endpoint);
        ArgumentNullException.ThrowIfNull(pin);

        cancellationToken.ThrowIfCancellationRequested();

        if (initialEvidence.Observation == FinTsTanOutcomeObservation.ExemptionReported)
        {
            return FinTsScaExecutionResult.Exempt(initialEvidence);
        }

        var effectiveClock = clock ?? TimeProvider.System;
        var sca = new FinTsScaContinuation(timeout, maximumStatusQueries, allowAutomaticQueries, effectiveClock);
        var startTrans = sca.Start(initialEvidence);

        if (startTrans == FinTsScaTransition.ExemptionReported)
        {
            return FinTsScaExecutionResult.Exempt(initialEvidence);
        }

        if (startTrans == FinTsScaTransition.RejectedForReview)
        {
            return FinTsScaExecutionResult.Failure("SCA challenge context requires review.", initialEvidence);
        }

        if (startTrans != FinTsScaTransition.Started)
        {
            return FinTsScaExecutionResult.Failure($"SCA continuation failed to start: {startTrans}", initialEvidence);
        }


        // 1. Decoupled SCA polling loop
        if (initialEvidence.Procedure.IsDecoupled && initialEvidence.Procedure.Method.HeaderText() == "Decoupled")
        {
            sca.Stop();
            return FinTsScaExecutionResult.Failure(
                "Decoupled approval requires a supported signed status-query builder. No query was sent.", initialEvidence);
        }

        // 2. Interactive TAN Challenge
        if (scaChallengeHandler is null)
        {
            sca.Stop();
            return FinTsScaExecutionResult.Failure(
                "Bank requires interactive SCA continuation, but no ScaChallengeHandler was configured.", initialEvidence);
        }

        FinTsSessionCredential? tanCredential;
        try
        {
            tanCredential = await scaChallengeHandler(initialEvidence.Challenge!, initialEvidence.Procedure, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            sca.Cancel();
            throw;
        }
        catch (Exception)
        {
            sca.Stop();
            return FinTsScaExecutionResult.Failure("SCA interaction failed. Start bank sign-in again.", initialEvidence);
        }

        if (tanCredential is null)
        {
            sca.Cancel();
            return FinTsScaExecutionResult.Cancelled("User cancelled interactive SCA challenge.");
        }

        using (tanCredential)
        {
            var tanSnap = tanCredential.GetSnapshot();
            if (tanSnap.Kind != FinTsCredentialKind.Tan || tanSnap.State != FinTsCredentialState.Available)
            {
                sca.Stop();
                return FinTsScaExecutionResult.Failure("Provided credential is not an available TAN.", initialEvidence);
            }

            var userTrans = sca.RecordUserContinuation();
            if (userTrans != FinTsScaTransition.UserContinuationRecorded)
            {
                return FinTsScaExecutionResult.Failure($"Failed to record user continuation: {userTrans}", initialEvidence);
            }

            if (scaContinuationCandidateFactory is not null)
            {
                var cand = scaContinuationCandidateFactory(sca, tanCredential);
                byte[] contBuffer = new byte[FinTsPinTanRequestEnvelopeWriter.MaximumEncodedLength];
                int contWritten;
                try
                {
                    var writeResult = FinTsPinTanRequestEnvelopeWriter.TryEncode(
                        cand.SignatureEvidence, pin, tanCredential, contBuffer, out contWritten, cancellationToken);

                    if (writeResult != FinTsPinTanRequestWriteResult.Written)
                    {
                        return FinTsScaExecutionResult.Failure($"Failed to encode TAN continuation envelope: {writeResult}", initialEvidence);
                    }

                    FinTsTransportResponse contTransportResp;
                    try
                    {
                        contTransportResp = await transport.SendAsync(
                            endpoint, contBuffer.AsMemory(0, contWritten), cancellationToken).ConfigureAwait(false);
                    }
                    catch (FinTsTransportException ex)
                    {
                        sca.Stop();
                        return FinTsScaExecutionResult.Failure(
                            $"Continuation transport error ({ex.Issue}).", initialEvidence, sca.GetSnapshot().QueriesRecorded);
                    }

                    if (!contTransportResp.IsSuccess)
                    {
                        return FinTsScaExecutionResult.Failure($"Continuation transport error: HTTP {contTransportResp.StatusCode}", initialEvidence);
                    }

                    var contResp = FinTsReadExecutionPipeline.ParseWireResponse(contTransportResp.Payload.Span, cancellationToken);
                    return FinTsScaExecutionResult.Success(contResp, initialEvidence, 1);
                }
                finally
                {
                    CryptographicOperations.ZeroMemory(contBuffer);
                }
            }
            else
            {
                sca.Stop();
                return FinTsScaExecutionResult.Failure(
                    "Interactive approval requires a supported signed TAN-continuation builder. No continuation was sent.", initialEvidence);
            }
        }
    }
}
