using Broiler.Fond.Kernel.Domain.Payments;
using Broiler.Fond.Kernel.FinTs.Transport;

namespace Broiler.Fond.Kernel.FinTs;

/// <summary>
/// Outcome of a SEPA transfer submission execution cycle over transport.
/// </summary>
public sealed record SepaTransferExecutionResult(
    PaymentAttempt UpdatedAttempt,
    IReadOnlyList<PaymentStateTransition> Transitions,
    PaymentSubmissionReceipt Receipt,
    bool IsSuccess,
    bool IsScaRequired,
    bool IsUnknown,
    string? ChallengeText = null,
    string? ChallengeMatrixPayload = null)
{
    public bool IsRejected => UpdatedAttempt.State == PaymentState.Rejected;
}

/// <summary>
/// Coordinates Single SEPA Credit Transfer (HKCCS) initiation, pre-send durable commitment,
/// bank SCA continuation, submission receipt generation, and fail-closed network failure injection.
/// </summary>
public static class SepaTransferExecutionCoordinator
{
    /// <summary>
    /// Executes the first phase of a Single SEPA Credit Transfer (pre-send commit, HKCCS transmission,
    /// and response processing). Transitions to <see cref="PaymentState.Unknown"/> on network faults.
    /// </summary>
    public static async ValueTask<SepaTransferExecutionResult> InitiateTransferAsync(
        PaymentAttempt attempt,
        PaymentDraftRevision draftRevision,
        string debtorName,
        string debtorIban,
        string? debtorBic,
        Uri endpoint,
        IFinTsTransport transport,
        string dialogueId,
        int messageNumber,
        int hkccsVersion = 1,
        ulong transitionIdStart = 1,
        DateTimeOffset? timestamp = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(attempt);
        ArgumentNullException.ThrowIfNull(draftRevision);
        ArgumentNullException.ThrowIfNull(endpoint);
        ArgumentNullException.ThrowIfNull(transport);

        DateTimeOffset now = timestamp ?? DateTimeOffset.UtcNow;
        List<PaymentStateTransition> transitions = [];

        // 1. Pre-send durable commitment: Transition attempt to Submitting
        PaymentAttempt submittingAttempt = attempt.TransitionTo(
            PaymentState.Submitting,
            now,
            statusDescription: "Submitting order to bank over transport.");

        PaymentStateTransition preSendTransition = PaymentStateTransition.Record(
            transitionId: transitionIdStart,
            attempt: attempt,
            newState: PaymentState.Submitting,
            timestamp: now,
            reason: "Pre-send durable commitment before transmitting request bytes.");
        transitions.Add(preSendTransition);

        // 2. Deterministic ISO 20022 XML generation
        byte[] painXmlBytes = SepaPainDocumentWriter.GenerateXmlBytes(
            messageId: attempt.EndToEndId,
            initiatingPartyName: debtorName,
            debtorName: debtorName,
            debtorIban: debtorIban,
            debtorBic: debtorBic,
            paymentInfoId: attempt.EndToEndId,
            executionDate: draftRevision.ExecutionDate,
            endToEndId: attempt.EndToEndId,
            amount: draftRevision.Amount.Amount,
            creditorName: draftRevision.Beneficiary.Name,
            creditorIban: draftRevision.Beneficiary.Iban,
            creditorBic: draftRevision.Beneficiary.Bic,
            remittance: draftRevision.Remittance,
            creationDateTime: now,
            executionMode: draftRevision.ExecutionMode);

        // 3. FinTS Wire Encoding (HKCCS for Standard, HKIPB for Instant, HKCME for Scheduled)
        byte[] requestPayload = draftRevision.ExecutionMode switch
        {
            PaymentExecutionMode.InstantCreditTransfer => FinTsSepaTransferRequestWriter.EncodeUnsignedInstant(
                dialogueId: dialogueId,
                messageNumber: messageNumber,
                version: hkccsVersion,
                originIban: debtorIban,
                originBic: debtorBic,
                painXmlBytes: painXmlBytes,
                sepaDescriptor: SepaPainDocumentWriter.PainNamespace,
                cancellationToken: cancellationToken),

            PaymentExecutionMode.ScheduledCreditTransfer => FinTsScheduledTransferRequestWriter.EncodeUnsignedScheduled(
                dialogueId: dialogueId,
                messageNumber: messageNumber,
                version: hkccsVersion,
                originIban: debtorIban,
                originBic: debtorBic,
                painXmlBytes: painXmlBytes,
                sepaDescriptor: SepaPainDocumentWriter.PainNamespace,
                cancellationToken: cancellationToken),

            _ => FinTsSepaTransferRequestWriter.EncodeUnsigned(
                dialogueId: dialogueId,
                messageNumber: messageNumber,
                version: hkccsVersion,
                originIban: debtorIban,
                originBic: debtorBic,
                painXmlBytes: painXmlBytes,
                sepaDescriptor: SepaPainDocumentWriter.PainNamespace,
                cancellationToken: cancellationToken),
        };

        // 4. Transmission with fail-closed failure boundary
        FinTsTransportResponse transportResponse;
        DateTimeOffset confirmedTime;
        try
        {
            transportResponse = await transport.SendAsync(endpoint, requestPayload, cancellationToken).ConfigureAwait(false);
            confirmedTime = DateTimeOffset.UtcNow;
        }
        catch (Exception ex)
        {
            // Network failure after or during write boundary crossing -> DURABLE UNKNOWN
            DateTimeOffset failureTime = DateTimeOffset.UtcNow;
            string faultReason = $"Transport fault during submission: {ex.Message}";

            PaymentAttempt unknownAttempt = submittingAttempt.TransitionTo(
                PaymentState.Unknown,
                failureTime,
                statusDescription: faultReason);

            PaymentStateTransition unknownTransition = PaymentStateTransition.Record(
                transitionId: transitionIdStart + 1,
                attempt: submittingAttempt,
                newState: PaymentState.Unknown,
                timestamp: failureTime,
                reason: faultReason,
                bankCode: "NET_FAULT");
            transitions.Add(unknownTransition);

            PaymentSubmissionReceipt unknownReceipt = new(
                attemptId: attempt.Id,
                endToEndId: attempt.EndToEndId,
                bankReference: null,
                responseCode: "NET_FAULT",
                responseText: faultReason,
                submittedAt: now,
                confirmedAt: failureTime,
                finalState: PaymentState.Unknown);

            return new SepaTransferExecutionResult(
                UpdatedAttempt: unknownAttempt,
                Transitions: transitions.AsReadOnly(),
                Receipt: unknownReceipt,
                IsSuccess: false,
                IsScaRequired: false,
                IsUnknown: true);
        }

        // 5. Decode Response Frame
        FinTsMessageFrame frame = FinTsMessageFrame.Parse(transportResponse.Payload.Span);
        FinTsResponse response = FinTsResponse.Parse(frame, cancellationToken);
        FinTsSepaTransferResponseResult parsed = FinTsSepaTransferResponseParser.Parse(response);

        // 6. Transition to final state based on authoritative response
        PaymentAttempt finalAttempt = submittingAttempt.TransitionTo(
            newState: parsed.TargetState,
            timestamp: confirmedTime,
            bankReference: parsed.BankOrderReference,
            responseCode: parsed.PrimaryReturnCode,
            statusDescription: parsed.PrimaryReturnText);

        PaymentStateTransition responseTransition = PaymentStateTransition.Record(
            transitionId: transitionIdStart + 1,
            attempt: submittingAttempt,
            newState: parsed.TargetState,
            timestamp: confirmedTime,
            reason: parsed.PrimaryReturnText,
            bankCode: parsed.PrimaryReturnCode);
        transitions.Add(responseTransition);

        PaymentSubmissionReceipt receipt = new(
            attemptId: attempt.Id,
            endToEndId: attempt.EndToEndId,
            bankReference: parsed.BankOrderReference,
            responseCode: parsed.PrimaryReturnCode,
            responseText: parsed.PrimaryReturnText,
            submittedAt: now,
            confirmedAt: confirmedTime,
            finalState: parsed.TargetState);

        return new SepaTransferExecutionResult(
            UpdatedAttempt: finalAttempt,
            Transitions: transitions.AsReadOnly(),
            Receipt: receipt,
            IsSuccess: parsed.IsSuccess,
            IsScaRequired: parsed.IsScaRequired,
            IsUnknown: false,
            ChallengeText: parsed.ChallengeText,
            ChallengeMatrixPayload: parsed.ChallengeMatrixPayload);
    }

    /// <summary>
    /// Executes the second phase of a SEPA transfer when SCA challenge response (TAN) is provided.
    /// </summary>
    public static async ValueTask<SepaTransferExecutionResult> CompleteScaAuthorizationAsync(
        PaymentAttempt attempt,
        string orderReference,
        string tan,
        Uri endpoint,
        IFinTsTransport transport,
        string dialogueId,
        int messageNumber,
        int hktanVersion = 1,
        ulong transitionIdStart = 10,
        DateTimeOffset? timestamp = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(attempt);
        ArgumentException.ThrowIfNullOrWhiteSpace(orderReference);
        ArgumentException.ThrowIfNullOrWhiteSpace(tan);
        ArgumentNullException.ThrowIfNull(endpoint);
        ArgumentNullException.ThrowIfNull(transport);

        DateTimeOffset now = timestamp ?? DateTimeOffset.UtcNow;
        List<PaymentStateTransition> transitions = [];

        // Pre-send commit if still awaiting authorization
        PaymentAttempt currentAttempt = attempt;
        if (attempt.State == PaymentState.AwaitingAuthorization)
        {
            currentAttempt = attempt.TransitionTo(
                PaymentState.Submitting,
                now,
                statusDescription: "Submitting SCA authorization challenge completion to bank.");

            PaymentStateTransition preSendTransition = PaymentStateTransition.Record(
                transitionId: transitionIdStart,
                attempt: attempt,
                newState: PaymentState.Submitting,
                timestamp: now,
                reason: "Pre-send commitment for SCA challenge completion.");
            transitions.Add(preSendTransition);
        }

        // Build HKTAN process-2 segment
        string hktanSegment = FinTsSepaTransferRequestWriter.BuildHktanCompletionSegment(
            ordinal: 2,
            version: hktanVersion,
            orderReference: orderReference,
            tan: tan);

        // Frame into unsigned message
        string escapedDialogue = FinTsUnsignedWireEncoding.Text(dialogueId.Trim(), 30, FinTsSyntaxError.InvalidReadData);
        string msgNumStr = messageNumber.ToString(System.Globalization.CultureInfo.InvariantCulture);
        string hnhbk = $"HNHBK:1:3+000000000000+300+{escapedDialogue}+{msgNumStr}'";
        string hnhbs = $"HNHBS:3:1+{msgNumStr}'";

        int totalLen = hnhbk.Length + hktanSegment.Length + hnhbs.Length;
        string sizeStr = totalLen.ToString("D12", System.Globalization.CultureInfo.InvariantCulture);
        var wireStr = new System.Text.StringBuilder(hnhbk);
        for (int i = 0; i < 12; i++)
        {
            wireStr[10 + i] = sizeStr[i];
        }
        wireStr.Append(hktanSegment).Append(hnhbs);

        byte[] requestPayload = System.Text.Encoding.Latin1.GetBytes(wireStr.ToString());

        // Send over transport
        FinTsTransportResponse transportResponse;
        DateTimeOffset confirmedTime;
        try
        {
            transportResponse = await transport.SendAsync(endpoint, requestPayload, cancellationToken).ConfigureAwait(false);
            confirmedTime = DateTimeOffset.UtcNow;
        }
        catch (Exception ex)
        {
            DateTimeOffset failureTime = DateTimeOffset.UtcNow;
            string faultReason = $"Transport fault during SCA completion: {ex.Message}";

            PaymentAttempt unknownAttempt = currentAttempt.TransitionTo(
                PaymentState.Unknown,
                failureTime,
                statusDescription: faultReason);

            PaymentStateTransition unknownTransition = PaymentStateTransition.Record(
                transitionId: transitionIdStart + 1,
                attempt: currentAttempt,
                newState: PaymentState.Unknown,
                timestamp: failureTime,
                reason: faultReason,
                bankCode: "NET_FAULT");
            transitions.Add(unknownTransition);

            PaymentSubmissionReceipt unknownReceipt = new(
                attemptId: attempt.Id,
                endToEndId: attempt.EndToEndId,
                bankReference: orderReference,
                responseCode: "NET_FAULT",
                responseText: faultReason,
                submittedAt: now,
                confirmedAt: failureTime,
                finalState: PaymentState.Unknown);

            return new SepaTransferExecutionResult(
                UpdatedAttempt: unknownAttempt,
                Transitions: transitions.AsReadOnly(),
                Receipt: unknownReceipt,
                IsSuccess: false,
                IsScaRequired: false,
                IsUnknown: true);
        }

        // Decode response
        FinTsMessageFrame frame = FinTsMessageFrame.Parse(transportResponse.Payload.Span);
        FinTsResponse response = FinTsResponse.Parse(frame, cancellationToken);
        FinTsSepaTransferResponseResult parsed = FinTsSepaTransferResponseParser.Parse(response);

        PaymentAttempt finalAttempt = currentAttempt;
        if (parsed.TargetState != currentAttempt.State)
        {
            finalAttempt = currentAttempt.TransitionTo(
                newState: parsed.TargetState,
                timestamp: confirmedTime,
                bankReference: parsed.BankOrderReference ?? orderReference,
                responseCode: parsed.PrimaryReturnCode,
                statusDescription: parsed.PrimaryReturnText);

            PaymentStateTransition responseTransition = PaymentStateTransition.Record(
                transitionId: transitionIdStart + 1,
                attempt: currentAttempt,
                newState: parsed.TargetState,
                timestamp: confirmedTime,
                reason: parsed.PrimaryReturnText,
                bankCode: parsed.PrimaryReturnCode);
            transitions.Add(responseTransition);
        }
        else
        {
            finalAttempt = currentAttempt with
            {
                BankReference = parsed.BankOrderReference ?? orderReference,
                ResponseCode = parsed.PrimaryReturnCode,
                StatusDescription = parsed.PrimaryReturnText,
                UpdatedAt = confirmedTime
            };
        }

        PaymentSubmissionReceipt receipt = new(
            attemptId: attempt.Id,
            endToEndId: attempt.EndToEndId,
            bankReference: parsed.BankOrderReference ?? orderReference,
            responseCode: parsed.PrimaryReturnCode,
            responseText: parsed.PrimaryReturnText,
            submittedAt: now,
            confirmedAt: confirmedTime,
            finalState: parsed.TargetState);

        return new SepaTransferExecutionResult(
            UpdatedAttempt: finalAttempt,
            Transitions: transitions.AsReadOnly(),
            Receipt: receipt,
            IsSuccess: parsed.IsSuccess,
            IsScaRequired: parsed.IsScaRequired,
            IsUnknown: false,
            ChallengeText: parsed.ChallengeText,
            ChallengeMatrixPayload: parsed.ChallengeMatrixPayload);
    }

    /// <summary>
    /// Enforces the strict anti-downgrade invariant: an instant payment instruction
    /// must never be downgraded or converted to a standard transfer without explicit user intervention.
    /// </summary>
    public static void AssertNoInstantDowngrade(PaymentDraftRevision originalDraft, PaymentExecutionMode requestedExecution)
    {
        ArgumentNullException.ThrowIfNull(originalDraft);
        if (originalDraft.ExecutionMode == PaymentExecutionMode.InstantCreditTransfer &&
            requestedExecution != PaymentExecutionMode.InstantCreditTransfer)
        {
            throw new InstantPaymentDowngradeException(
                "An instant SEPA credit transfer (SCT Inst) cannot be automatically converted or downgraded to a standard SEPA transfer. Explicit user intervention is required.");
        }
    }
}

