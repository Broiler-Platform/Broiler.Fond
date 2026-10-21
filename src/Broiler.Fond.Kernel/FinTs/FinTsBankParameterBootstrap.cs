using Broiler.Fond.Kernel.FinTs.Transport;

namespace Broiler.Fond.Kernel.FinTs;

public enum FinTsBankBootstrapStatus { Acquired, RejectedResponse, RejectedParameters, CloseFailed, TransportFailed, Cancelled }

/// <summary>Closed anonymous bank observations, never identified user permission or a selected security procedure.</summary>
public sealed class FinTsBankParameterObservation
{
    internal FinTsBankParameterObservation(FinTsUnsignedInitializationRequest request, FinTsParameterSet parameters,
        FinTsTanParameterSet tan, FinTsPinTanParameterSet pinTan, FinTsReadParameterSet reads)
    { Request = request; Parameters = parameters; Tan = tan; PinTan = pinTan; Reads = reads; }
    public FinTsUnsignedInitializationRequest Request { get; }
    public FinTsParameterSet Parameters { get; }
    public FinTsTanParameterSet Tan { get; }
    public FinTsPinTanParameterSet PinTan { get; }
    public FinTsReadParameterSet Reads { get; }
}

public sealed record FinTsBankBootstrapResult(FinTsBankBootstrapStatus Status, FinTsBankParameterObservation? Observation = null);

/// <summary>One bounded anonymous initialization/close, without PIN, user data, retries or procedure selection.</summary>
public static class FinTsBankParameterBootstrap
{
    public static async Task<FinTsBankBootstrapResult> ExecuteAsync(IFinTsTransport transport, Uri endpoint, string country,
        string bankCode, string productId, string productVersion, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(transport);
        ArgumentNullException.ThrowIfNull(endpoint);
        if (!endpoint.IsAbsoluteUri || endpoint.Scheme != Uri.UriSchemeHttps) throw new ArgumentException("An HTTPS endpoint is required.", nameof(endpoint));
        using var lifetime = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        lifetime.CancelAfter(TimeSpan.FromSeconds(60));
        var ct = lifetime.Token;
        string? dialogue = null;
        bool closeAttempted = false;
        try
        {
            ct.ThrowIfCancellationRequested();
            byte[] wire = FinTsUnsignedInitializationWriter.EncodeAnonymous(country, bankCode, productId, productVersion, cancellationToken: ct);
            var request = FinTsUnsignedInitializationRequest.Parse(FinTsMessageFrame.Parse(wire, ct), ct);
            var correlation = new FinTsDialogueCorrelation();
            if (correlation.BeginRequest(request.Frame) != FinTsCorrelationStatus.RequestRecorded)
                return new(FinTsBankBootstrapStatus.RejectedResponse);
            var received = await transport.SendAsync(endpoint, wire, ct).ConfigureAwait(false);
            if (!received.IsSuccess) return new(FinTsBankBootstrapStatus.TransportFailed);
            var response = FinTsReadExecutionPipeline.ParseWireResponse(received.Payload.Span, ct);
            if (correlation.AcceptResponse(response) != FinTsCorrelationStatus.Matched)
                return new(FinTsBankBootstrapStatus.RejectedResponse);
            if (response.PinTanEnvelope is null)
                dialogue = response.Frame.Syntax.Segments[0].Fields[2].Elements[0].HeaderText();
            var parameters = FinTsParameterSet.Parse(response, ct);
            var initial = FinTsInitializationEvidence.Evaluate(request, parameters, cancellationToken: ct);
            // Only a correlated, valid assigned dialogue can be used for bounded unsigned cleanup.
            if ((initial.Issues & (FinTsInitializationIssue.MessageMismatch | FinTsInitializationIssue.InvalidAssignedDialogue |
                FinTsInitializationIssue.ReferenceMismatch | FinTsInitializationIssue.ProfileNeedsReview)) == 0)
                dialogue = initial.ReportedDialogueId;
            var tan = FinTsTanParameterSet.Parse(parameters, ct);
            var pinTan = FinTsPinTanParameterSet.Parse(parameters, ct);
            var reads = FinTsReadParameterSet.Parse(parameters, ct);
            var interpreted = tan.Advertisements.Select(a => a.Source).Concat(pinTan.Advertisements.Select(a => a.Source))
                .Concat(reads.Advertisements.Select(a => a.Source)).ToHashSet();
            // Re-evaluate only these two concerns with typed extensions; retain every identity/protocol/error check.
            var issues = initial.Issues & ~(FinTsInitializationIssue.UninterpretedParameters | FinTsInitializationIssue.StatusNeedsReview);
            if (parameters.UninterpretedSegments.Any(s => !interpreted.Contains(s))) issues |= FinTsInitializationIssue.UninterpretedParameters;
            if (!ValidStatuses(request, parameters, ct)) issues |= FinTsInitializationIssue.StatusNeedsReview;
            // Anonymous data must never establish an identified user's permissions or accounts.
            if (issues != 0 || parameters.User is not null || parameters.Accounts.Count != 0 || tan.HasDuplicateAdvertisementVersions ||
                tan.Advertisements.Any(a => a.HasDuplicateSecurityFunctions) || pinTan.Advertisements.Count > 1 ||
                pinTan.Advertisements.Any(a => a.HasConflictingPinLengthBounds) ||
                reads.Advertisements.GroupBy(a => (a.Source.Code, a.Source.Version)).Any(g => g.Count() > 1))
                return new(FinTsBankBootstrapStatus.RejectedParameters);
            ct.ThrowIfCancellationRequested();
            closeAttempted = true;
            if (!await CloseAsync(transport, endpoint, dialogue!, ct).ConfigureAwait(false))
                return new(FinTsBankBootstrapStatus.CloseFailed);
            ct.ThrowIfCancellationRequested();
            return new(FinTsBankBootstrapStatus.Acquired, new(request, parameters, tan, pinTan, reads));
        }
        catch (OperationCanceledException) { return new(FinTsBankBootstrapStatus.Cancelled); }
        catch (FinTsTransportException) { return new(ct.IsCancellationRequested ? FinTsBankBootstrapStatus.Cancelled : closeAttempted ? FinTsBankBootstrapStatus.CloseFailed : FinTsBankBootstrapStatus.TransportFailed); }
        catch (Exception ex) when (ex is FinTsFormatException or ArgumentException or InvalidOperationException)
        { return new(closeAttempted ? FinTsBankBootstrapStatus.CloseFailed : FinTsBankBootstrapStatus.RejectedResponse); }
        finally
        {
            if (dialogue is not null && !closeAttempted)
            {
                using var cleanup = new CancellationTokenSource(TimeSpan.FromSeconds(2));
                try { await CloseAsync(transport, endpoint, dialogue, cleanup.Token).ConfigureAwait(false); } catch (Exception) { }
            }
        }
    }

    private static bool ValidStatuses(FinTsUnsignedInitializationRequest request, FinTsParameterSet parameters, CancellationToken ct)
    {
        var permissions = FinTsPermittedProcedureSet.Parse(parameters.Source, ct);
        if (permissions.IsAmbiguous) return false;
        bool executed = false, identity = false, preparation = false;
        foreach (var segment in parameters.Source.ReplySegments)
        {
            var seen = new HashSet<string>(StringComparer.Ordinal);
            foreach (var reply in segment.Replies)
            {
                if (!seen.Add(reply.Code) || !reply.ElementReference.IsEmpty) return false;
                if (reply.Code == "3920")
                {
                    // Anonymous availability report only; not a permission for the configured login.
                    if (segment.IsMessageLevel || segment.RequestSegmentNumber != request.Identification.Source.Number) return false;
                    continue;
                }
                if (reply.Parameters.Any(p => !p.IsEmpty)) return false;
                if (!(segment.IsMessageLevel ? reply.Code is "0010" or "0020" : reply.Code == "0020" ||
                    reply.Code == "3050" && segment.RequestSegmentNumber == request.Preparation.Source.Number && parameters.Bank is not null)) return false;
                if (reply.Code != "0020") continue;
                if (segment.IsMessageLevel) executed = true;
                else if (segment.RequestSegmentNumber == request.Identification.Source.Number) identity = true;
                else if (segment.RequestSegmentNumber == request.Preparation.Source.Number) preparation = true;
            }
        }
        return executed || identity && preparation;
    }
    internal static async Task<bool> CloseAsync(IFinTsTransport transport, Uri endpoint, string dialogue, CancellationToken ct)
    {
        byte[] wire = FinTsUnsignedDialogueEndWriter.Encode(dialogue, 2, ct);
        var request = FinTsUnsignedDialogueEndRequest.Parse(FinTsMessageFrame.Parse(wire, ct), 2, ct);
        var received = await transport.SendAsync(endpoint, wire, ct).ConfigureAwait(false);
        if (!received.IsSuccess) return false;
        var response = FinTsReadExecutionPipeline.ParseWireResponse(received.Payload.Span, ct);
        return FinTsDialogueEndEvidence.Evaluate(request, response, ct).Outcome == FinTsDialogueEndOutcome.ClosureReported;
    }
}
