using System.Security.Cryptography;
using Broiler.Fond.Kernel.Domain.Connections;
using Broiler.Fond.Kernel.Product;

namespace Broiler.Fond.Kernel.FinTs;

public enum FinTsCandidateFailure { MissingParameters, UnsupportedVersion, IdentityMismatch, MissingSystemId, UnqualifiedEvidence, UnsupportedSca }

/// <summary>Safe construction failures; never includes raw protocol or credential data.</summary>
public sealed class FinTsCandidateException(FinTsCandidateFailure failure) : InvalidOperationException($"Bank request preparation failed: {failure}.")
{
    public FinTsCandidateFailure Failure { get; } = failure;
}

/// <summary>
/// Production adapters over the existing bounded encoders and evidence checks. The caller supplies
/// actual bank parameter provenance and an explicit permitted procedure; no first-procedure or product fallback.
/// Instances belong to one connection configuration and must not outlive its endpoint/login revision.
/// </summary>
public sealed class FinTsProductionCandidateFactory
{
    private readonly BankConnection _connection;
    private readonly FinTsProductRegistration _registration;
    private readonly FinTsPinTanProcedureContext _procedures;
    private readonly FinTsPinTanSignatureSelection _selection;
    private readonly TimeProvider _clock;
    private readonly HashSet<FinTsPinTanRequestBinding> _continuedApprovalRequests = new(ReferenceEqualityComparer.Instance);

    public FinTsProductionCandidateFactory(BankConnection connection, FinTsProductRegistration registration,
        FinTsPinTanProcedureContext procedures, FinTsPinTanSignatureSelection selection, TimeProvider? clock = null)
    {
        ArgumentNullException.ThrowIfNull(connection); ArgumentNullException.ThrowIfNull(registration);
        ArgumentNullException.ThrowIfNull(procedures); ArgumentNullException.ThrowIfNull(selection);
        _connection = connection; _registration = registration; _procedures = procedures; _selection = selection; _clock = clock ?? TimeProvider.System;
        if (connection.SystemStatus != FinTsCustomerSystemStatus.Required)
            throw new FinTsCandidateException(FinTsCandidateFailure.UnsupportedVersion);
        var identity = procedures.Initialization.Identification;
        if (identity.IsAnonymous || identity.Country != "280" || identity.Institution != connection.InstitutionConfiguration.BankCode ||
            identity.CustomerId != (connection.CustomerId ?? connection.UserId))
            throw new FinTsCandidateException(FinTsCandidateFailure.IdentityMismatch);
        if (procedures.Parameters.Source.Bank is not { } bank || !bank.ProtocolVersions.Contains(300) || procedures.Parameters.Source.User is null)
            throw new FinTsCandidateException(FinTsCandidateFailure.MissingParameters);
        // Encoding and evidence evaluation validate exact source association, permissions and selected procedure.
        _ = CreateSynchronization(FinTsSynchronizationMode.SystemId);
    }

    public FinTsPinTanRequestBinding CreateSynchronization(FinTsSynchronizationMode mode = FinTsSynchronizationMode.SystemId,
        CancellationToken cancellationToken = default)
    {
        if (mode is not (FinTsSynchronizationMode.SystemId or FinTsSynchronizationMode.LastMessageNumber))
            throw new FinTsCandidateException(FinTsCandidateFailure.UnsupportedVersion);
        string system = mode == FinTsSynchronizationMode.SystemId ? "0" : SystemId();
        var input = InitializationInput(system);
        var request = FinTsUnsignedSynchronizationRequest.Parse(FinTsMessageFrame.Parse(
            FinTsUnsignedSynchronizationWriter.Encode(input, mode, cancellationToken), cancellationToken), cancellationToken);
        var header = Header(system, 1, cancellationToken);
        var context = FinTsPinTanSignatureRequestContext.ForSynchronization(request, _connection.UserId, header.ControlReference, _selection);
        return Bind(context, header, cancellationToken);
    }

    public FinTsPinTanRequestBinding CreateInitialization(CancellationToken cancellationToken = default)
    {
        var request = FinTsUnsignedInitializationRequest.Parse(FinTsMessageFrame.Parse(
            FinTsUnsignedInitializationWriter.Encode(InitializationInput(SystemId()), cancellationToken), cancellationToken), cancellationToken);
        var header = Header(SystemId(), 1, cancellationToken);
        return Bind(FinTsPinTanSignatureRequestContext.ForInitialization(request, _connection.UserId, header.ControlReference, _selection), header, cancellationToken);
    }

    public FinTsPinTanAllAccountRequestBinding CreateDiscovery(FinTsPinTanReadSessionAttempt attempt, CancellationToken cancellationToken = default)
    {
        var initialization = Requirements(attempt);
        var frame = FinTsMessageFrame.Parse(FinTsUnsignedReadRequestWriter.EncodeDiscovery(Dialogue(attempt), 2, [], cancellationToken), cancellationToken);
        var signature = FirstSignature(initialization, FinTsReadRequestContext.Parse(frame, 2, cancellationToken), cancellationToken);
        var capability = FinTsPinTanAllAccountCapabilityContext.Evaluate(signature, cancellationToken);
        if (!capability.HasMatchingEvidence) throw new FinTsCandidateException(FinTsCandidateFailure.UnqualifiedEvidence);
        return FinTsPinTanAllAccountRequestBinding.ForEnvelopeCandidate(capability, cancellationToken);
    }

    public FinTsPinTanReadRequestBinding CreateBalance(FinTsPinTanReadSessionAttempt attempt, FinTsAccountParameters account,
        FinTsReadAccountInput accountInput, CancellationToken cancellationToken = default)
    {
        var initialization = Requirements(attempt);
        // Select only an advertised schema that also qualifies for this exact bank account and HIPINS context.
        var versions = initialization.ReadParameters!.Advertisements.Where(a => a.Operation == FinTsReadOperation.Balance)
            .Select(a => a.Version).Distinct().OrderDescending();
        foreach (int version in versions)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (version == 6 && (accountInput.Iban.Length != 0 || accountInput.Bic.Length != 0)) continue;
            var frame = FinTsMessageFrame.Parse(FinTsUnsignedReadRequestWriter.EncodeBalance(Dialogue(attempt), 2, version, accountInput,
                cancellationToken: cancellationToken), cancellationToken);
            var signature = FirstSignature(initialization, FinTsReadRequestContext.Parse(frame, 2, cancellationToken), cancellationToken);
            var capability = FinTsPinTanReadCapabilityContext.Evaluate(signature, account, cancellationToken: cancellationToken);
            if (capability.HasMatchingEvidence)
            {
                return FinTsPinTanReadRequestBinding.ForEnvelopeCandidate(capability, cancellationToken);
            }
        }
        throw new FinTsCandidateException(FinTsCandidateFailure.UnsupportedVersion);
    }

    public FinTsPinTanReadContinuationRequestBinding CreateBalanceContinuation(FinTsPinTanReadSessionAttempt attempt, CancellationToken cancellationToken = default)
    {
        var first = attempt.BalanceFirstPageEvidence ?? throw new FinTsCandidateException(FinTsCandidateFailure.MissingParameters);
        var pages = attempt.BalanceContinuationPages;
        var previous = pages.LastOrDefault();
        var priorRequest = previous?.Request.Context.Request ?? first.Request.Context.Signature.Request;
        var source = priorRequest.Request;
        if (source.Accounts.Count != 1 || attempt.CurrentContinuationToken is not string token)
            throw new FinTsCandidateException(FinTsCandidateFailure.UnqualifiedEvidence);
        var a = source.Accounts[0];
        var input = new FinTsReadAccountInput(a.Number, a.Subaccount, a.Country, a.Institution, a.Iban, a.Bic);
        var state = attempt.GetSnapshot();
        var frame = FinTsMessageFrame.Parse(FinTsUnsignedReadRequestWriter.EncodeBalance(Dialogue(attempt), checked(state.ClientMessageNumber + 1),
            source.Source.Version, input, maximumEntries: source.MaximumEntries, continuationToken: token, cancellationToken: cancellationToken), cancellationToken);
        var request = FinTsReadRequestContext.Parse(frame, checked(state.BankMessageNumber + 1), cancellationToken);
        var continuation = previous is null ? FinTsPinTanReadContinuationContext.Evaluate(first, request, cancellationToken)
            : FinTsPinTanReadContinuationContext.Evaluate(previous, request, cancellationToken);
        var header = Header(SystemId(), frame.MessageNumber, cancellationToken);
        var signature = FinTsPinTanReadContinuationSignatureContext.Evaluate(continuation, header, _selection, header.ControlReference, cancellationToken);
        if (!signature.HasMatchingEvidence) throw new FinTsCandidateException(FinTsCandidateFailure.UnqualifiedEvidence);
        return FinTsPinTanReadContinuationRequestBinding.ForEnvelopeCandidate(signature, cancellationToken);
    }

    public FinTsPinTanDialogueEndRequestBinding CreateClosing(FinTsPinTanReadSessionAttempt attempt, CancellationToken cancellationToken = default)
    {
        var evidence = attempt.InitializationEvidence ?? throw new FinTsCandidateException(FinTsCandidateFailure.MissingParameters);
        CheckOrigin(evidence.Request);
        var state = attempt.GetSnapshot();
        int client = checked(state.ClientMessageNumber + 1), bank = checked(state.BankMessageNumber + 1);
        var request = FinTsUnsignedDialogueEndRequest.Parse(FinTsMessageFrame.Parse(
            FinTsUnsignedDialogueEndWriter.Encode(Dialogue(attempt), client, cancellationToken), cancellationToken), bank, cancellationToken);
        return FinTsPinTanDialogueEndRequestBinding.ForEnvelopeCandidate(FinTsPinTanDialogueEndContext.EvaluateForInitialization(
            evidence, request, Header(SystemId(), client, cancellationToken), client, bank, cancellationToken), cancellationToken);
    }

    public FinTsPinTanDialogueEndRequestBinding CreateSynchronizationClosing(FinTsPinTanSynchronizationSessionAttempt attempt,
        FinTsPinTanSynchronizationSessionResult result, CancellationToken cancellationToken = default)
    {
        var evidence = result.SynchronizationEvidence ?? throw new FinTsCandidateException(FinTsCandidateFailure.MissingParameters);
        CheckOrigin(evidence.Request);
        var state = attempt.GetSnapshot();
        int client = checked(state.ClientMessageNumber + 1), bank = checked(state.BankMessageNumber + 1);
        var request = FinTsUnsignedDialogueEndRequest.Parse(FinTsMessageFrame.Parse(
            FinTsUnsignedDialogueEndWriter.Encode(attempt.DialogueId ?? throw new FinTsCandidateException(FinTsCandidateFailure.UnqualifiedEvidence), client, cancellationToken), cancellationToken), bank, cancellationToken);
        string system = evidence.Request.SignatureEvidence.Request.Synchronization!.Synchronization.Mode == FinTsSynchronizationMode.SystemId
            ? evidence.MatchingReport?.SystemId ?? throw new FinTsCandidateException(FinTsCandidateFailure.MissingSystemId) : SystemId();
        return FinTsPinTanDialogueEndRequestBinding.ForEnvelopeCandidate(FinTsPinTanDialogueEndContext.Evaluate(evidence, request,
            Header(system, client, cancellationToken), cancellationToken), cancellationToken);
    }

    public FinTsSynchronizationExecutionOptions SynchronizationOptions(FinTsSynchronizationMode mode = FinTsSynchronizationMode.SystemId,
        TimeSpan? timeout = null, FinTsSynchronizationRecoveryContext? recoveryContext = null,
        Func<FinTsTanChallenge, CancellationToken, ValueTask<FinTsSessionCredential?>>? textTanPrompt = null)
    {
        _ = CreateSynchronization(mode);
        if (mode == FinTsSynchronizationMode.LastMessageNumber && recoveryContext is null)
            throw new FinTsCandidateException(FinTsCandidateFailure.MissingParameters);
        return new()
        {
            Mode = mode,
            RecoveryContext = recoveryContext,
            ProductRegistration = _registration,
            Clock = _clock,
            Timeout = timeout ?? TimeSpan.FromSeconds(60),
            SynchronizationCandidateFactory = _ => textTanPrompt is not null && mode == FinTsSynchronizationMode.SystemId && SupportsTextTan()
                ? CreateSynchronizationApprovalRequest() : CreateSynchronization(mode),
            TextTanPrompt = textTanPrompt,
            TextTanCandidateFactory = (origin, response) => CreateTextTanContinuation(origin, response),
            ClosingCandidateFactory = (attempt, result) => CreateSynchronizationClosing(attempt, result),
            Procedures = _procedures.Parameters,
            Permissions = _procedures.Permissions,
            AllowAutomaticQueries = false
        };
    }

    /// <summary>SCA request construction belongs to BR-04; never substitute unsigned or fixture bytes.</summary>
    public static void RequireScaBuilder() => throw new FinTsCandidateException(FinTsCandidateFailure.UnsupportedSca);
    private bool SupportsTextTan()
    {
        var procedure = CreateSynchronization().SignatureEvidence.MatchingProcedure;
        return procedure is not null && procedure.ProcessVariant == "2" && !procedure.IsDecoupled && procedure.MediumNameRequirement == "0" &&
            !procedure.HhdResponseRequired && procedure.SmsAccountRequirement != "2" &&
            procedure.MaximumTanLength is > 0 && procedure.InputFormat is "1" or "2";
    }
    public FinTsPinTanRequestBinding CreateSynchronizationApprovalRequest(CancellationToken cancellationToken = default)
    {
        var procedure = CreateSynchronization().SignatureEvidence.MatchingProcedure;
        if (procedure is null || procedure.ProcessVariant != "2" || procedure.IsDecoupled || procedure.MediumNameRequirement != "0" ||
            procedure.HhdResponseRequired || procedure.SmsAccountRequirement == "2" ||
            procedure.MaximumTanLength is null or 0 || procedure.InputFormat is not ("1" or "2"))
            throw new FinTsCandidateException(FinTsCandidateFailure.UnsupportedSca);
        var request = FinTsUnsignedSynchronizationRequest.Parse(FinTsMessageFrame.Parse(
            FinTsUnsignedSynchronizationWriter.EncodeWithApprovalRequest(InitializationInput("0"), _selection.TanSegmentVersion, cancellationToken), cancellationToken), cancellationToken);
        var header = Header("0", 1, cancellationToken);
        return Bind(FinTsPinTanSignatureRequestContext.ForSynchronization(request, _connection.UserId, header.ControlReference, _selection), header, cancellationToken);
    }
    public FinTsSignedTextTanRequest CreateTextTanContinuation(FinTsPinTanRequestBinding origin, FinTsResponse response, CancellationToken cancellationToken = default)
    {
        CheckOrigin(origin);
        lock (_continuedApprovalRequests)
        {
            if (_continuedApprovalRequests.Count >= 128 || _continuedApprovalRequests.Contains(origin))
                throw new FinTsCandidateException(FinTsCandidateFailure.UnqualifiedEvidence);
            var candidate = FinTsSignedTextTanRequest.Create(origin, response, Header(origin.SignatureEvidence.Header.SystemId, 2, cancellationToken), _clock, cancellationToken);
            _continuedApprovalRequests.Add(origin); return candidate;
        }
    }

    private FinTsInitializationInput InitializationInput(string system) => new("280", _connection.InstitutionConfiguration.BankCode!,
        _connection.CustomerId ?? _connection.UserId, system, FinTsCustomerSystemStatus.Required,
        _procedures.Parameters.Source.Bank!.Version, _procedures.Parameters.Source.User!.Version,
        FinTsDialogueLanguage.German, _registration.ProductIdentifier, _registration.ProductVersion);

    private FinTsPinTanRequestBinding Bind(FinTsPinTanSignatureRequestContext request, FinTsPinTanSignatureHeader header, CancellationToken ct)
    {
        var evidence = FinTsPinTanSignatureEvidence.Evaluate(request, header, _procedures, ct);
        if (!evidence.HasMatchingEvidence) throw new FinTsCandidateException(FinTsCandidateFailure.UnqualifiedEvidence);
        return FinTsPinTanRequestBinding.ForEnvelopeCandidate(evidence, ct);
    }

    private FinTsPinTanSignatureHeader Header(string system, int message, CancellationToken ct)
    {
        DateTimeOffset now = _clock.GetUtcNow();
        // Control references are fresh per candidate; signature reference follows the current dialogue counter.
        string control = Convert.ToHexString(RandomNumberGenerator.GetBytes(7));
        var input = new FinTsPinTanSignatureHeaderInput(_selection.ProfileVersion, _selection.SecurityFunction, control,
            1, 1, system, checked((ulong)message), DateOnly.FromDateTime(now.UtcDateTime),
            new TimeOnly(now.UtcDateTime.Hour, now.UtcDateTime.Minute, now.UtcDateTime.Second), "999", "10", "16", "280", _connection.InstitutionConfiguration.BankCode!, _connection.UserId, 0, 0);
        return FinTsPinTanSignatureHeader.Parse(FinTsSyntax.ParseSegments(FinTsPinTanSignatureHeaderWriter.Encode(input, 2, ct), ct).Segments.Single(), ct);
    }

    private string SystemId() => _connection.SystemId is string id && id is not ("0" or "unbekannt") && _connection.IsSynchronized
        ? id : throw new FinTsCandidateException(FinTsCandidateFailure.MissingSystemId);
    private static string Dialogue(FinTsPinTanReadSessionAttempt attempt) => attempt.DialogueId ?? throw new FinTsCandidateException(FinTsCandidateFailure.MissingParameters);
    private FinTsPinTanInitializationRequirementsEvidence Requirements(FinTsPinTanReadSessionAttempt attempt)
    {
        var evidence = attempt.RequirementsEvidence;
        if (evidence is null || !evidence.HasMatchingEvidence || evidence.ReadParameters is null)
            throw new FinTsCandidateException(FinTsCandidateFailure.MissingParameters);
        CheckOrigin(evidence.Procedures.Initialization.Request);
        return evidence;
    }
    private void CheckOrigin(FinTsPinTanRequestBinding request)
    {
        var source = request.SignatureEvidence;
        if (source.Request.ExpectedUserId != _connection.UserId || source.Request.Identification.Institution != _connection.InstitutionConfiguration.BankCode ||
            source.Request.Identification.CustomerId != (_connection.CustomerId ?? _connection.UserId) ||
            source.Request.Selection.ProfileVersion != _selection.ProfileVersion || source.Request.Selection.SecurityFunction != _selection.SecurityFunction ||
            source.Request.Selection.TanSegmentVersion != _selection.TanSegmentVersion ||
            (source.Request.Initialization?.Preparation ?? source.Request.Synchronization!.Preparation).ProductIdentifier != _registration.ProductIdentifier ||
            (source.Request.Initialization?.Preparation ?? source.Request.Synchronization!.Preparation).ProductVersion != _registration.ProductVersion)
            throw new FinTsCandidateException(FinTsCandidateFailure.IdentityMismatch);
    }
    private FinTsPinTanReadSignatureContext FirstSignature(FinTsPinTanInitializationRequirementsEvidence evidence, FinTsReadRequestContext request, CancellationToken ct)
    {
        var header = Header(SystemId(), request.Frame.MessageNumber, ct);
        return FinTsPinTanReadSignatureContext.Evaluate(evidence, request, header, _selection, header.ControlReference, ct);
    }
}
