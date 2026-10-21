using System.Security.Cryptography;
using Broiler.Fond.Kernel.FinTs.Transport;

namespace Broiler.Fond.Kernel.FinTs;

public enum FinTsSignedTanStatus { Confirmed, Cancelled, Unsupported, Rejected, TransportFailed, AlreadyRunning }
public sealed class FinTsSignedTanResult
{
    internal FinTsSignedTanResult(FinTsSignedTanStatus status, FinTsResponse? confirmation = null, FinTsSignedTextTanRequest? request = null)
    { Status = status; Confirmation = confirmation; Request = request; }
    public FinTsSignedTanStatus Status { get; }
    public FinTsResponse? Confirmation { get; }
    public FinTsSignedTextTanRequest? Request { get; }
}

/// <summary>One signed text-TAN round trip. Confirmation is not synchronization, account capability or durable business success.</summary>
public static class FinTsSignedTextTanExecution
{
    private static readonly System.Runtime.CompilerServices.ConditionalWeakTable<FinTsSignedTextTanRequest, object> Attempts = new();
    public static async Task<FinTsSignedTanResult> ExecuteAsync(FinTsSignedTextTanRequest request, IFinTsTransport transport, Uri endpoint,
        FinTsSessionCredential pin, Func<FinTsTanChallenge, CancellationToken, ValueTask<FinTsSessionCredential?>> prompt,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request); ArgumentNullException.ThrowIfNull(transport); ArgumentNullException.ThrowIfNull(endpoint);
        ArgumentNullException.ThrowIfNull(pin); ArgumentNullException.ThrowIfNull(prompt);
        if (!endpoint.IsAbsoluteUri || endpoint.Scheme != Uri.UriSchemeHttps || !string.IsNullOrEmpty(endpoint.UserInfo))
            return new(FinTsSignedTanStatus.Unsupported);
        lock (Attempts) { if (Attempts.TryGetValue(request, out _)) return new(FinTsSignedTanStatus.AlreadyRunning); Attempts.Add(request, new()); }
        using var lifetime = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        lifetime.CancelAfter(TimeSpan.FromMinutes(2));
        FinTsSessionCredential? tan = null;
        object gate = new();
        using var clear = lifetime.Token.Register(() => { lock (gate) { tan?.Dispose(); } });
        byte[] buffer = new byte[FinTsSignedTextTanRequest.MaximumEncodedLength];
        try
        {
            lifetime.Token.ThrowIfCancellationRequested();
            if (!request.IsCurrent || pin.GetSnapshot().Kind != FinTsCredentialKind.Pin || pin.GetSnapshot().State != FinTsCredentialState.Available)
                return new(FinTsSignedTanStatus.Rejected);
            var acquired = await prompt(request.Challenge.Challenge!, lifetime.Token).ConfigureAwait(false);
            lock (gate) { tan = acquired; }
            lifetime.Token.ThrowIfCancellationRequested();
            if (tan is null) return new(FinTsSignedTanStatus.Cancelled);
            if (request.TryEncode(pin, tan, buffer, out int written, lifetime.Token) != FinTsPinTanRequestWriteResult.Written)
                return new(FinTsSignedTanStatus.Rejected);
            var received = await transport.SendAsync(endpoint, buffer.AsMemory(0, written), lifetime.Token).ConfigureAwait(false);
            lifetime.Token.ThrowIfCancellationRequested();
            if (!received.IsSuccess) return new(FinTsSignedTanStatus.TransportFailed);
            var response = FinTsReadExecutionPipeline.ParseWireResponse(received.Payload.Span, lifetime.Token);
            return ValidConfirmation(request, response) ? new(FinTsSignedTanStatus.Confirmed, response, request) : new(FinTsSignedTanStatus.Rejected);
        }
        catch (OperationCanceledException) { return new(FinTsSignedTanStatus.Cancelled); }
        catch (FinTsTransportException) { return new(lifetime.IsCancellationRequested ? FinTsSignedTanStatus.Cancelled : FinTsSignedTanStatus.TransportFailed); }
        catch (Exception) { return new(FinTsSignedTanStatus.Rejected); }
        finally { lock (gate) { tan?.Dispose(); } CryptographicOperations.ZeroMemory(buffer); }
    }
    private static bool ValidConfirmation(FinTsSignedTextTanRequest request, FinTsResponse response)
    {
        if (!request.IsCurrent || response.PinTanEnvelope is not { ProfileVersion: 2 } envelope || response.HasErrors || response.HasConflictingClasses || response.HasIndeterminateProcessing)
            return false;
        var header = response.Frame.Syntax.Segments[0].Fields;
        var key = envelope.SecurityHeader.Fields[6].Elements;
        if (response.Frame.MessageNumber != request.ExpectedBankMessageNumber || header.Count != 5 || header[4].Elements.Count != 2 ||
            header[2].Elements[0].HeaderText() != request.DialogueId || header[4].Elements[0].HeaderText() != request.DialogueId || header[4].Elements[1].HeaderText() != "2" ||
            key[0].HeaderText() != request.Header.CountryCode || key[1].HeaderText() != request.Header.InstitutionId || key[2].HeaderText() != request.Header.UserId ||
            !envelope.SystemId.CopyValueBytes().AsSpan().SequenceEqual(request.Challenge.Response.Source.PinTanEnvelope!.SystemId.CopyValueBytes())) return false;
        var challenges = FinTsTanChallengeSet.Parse(response);
        if (challenges.Challenges.Count != 1 || response.UninterpretedSegments.Count != 1) return false;
        var completed = challenges.Challenges[0];
        if (completed.RequestSegmentNumber != 3 || completed.Source.Version != request.Challenge.Procedure.SegmentVersion || completed.Process != "2" ||
            completed.OrderReference is null || !completed.OrderReference.CopyValueBytes().AsSpan().SequenceEqual(request.Challenge.Challenge!.OrderReference!.CopyValueBytes()) ||
            completed.OrderHash is { IsEmpty: false } || completed.HhdData is { IsEmpty: false } || completed.MediumName is { IsEmpty: false } ||
            completed.ValidUntil is { } valid && !ParameterFields.Empty(valid)) return false;
        bool execution = false;
        foreach (var segment in response.ReplySegments)
        {
            if (!segment.IsMessageLevel && segment.RequestSegmentNumber != 3) return false;
            var seen = new HashSet<string>(StringComparer.Ordinal);
            foreach (var reply in segment.Replies)
            {
                if (!seen.Add(reply.Code) || !reply.ElementReference.IsEmpty || reply.Parameters.Any(p => !p.IsEmpty) ||
                    !(segment.IsMessageLevel ? reply.Code is "0010" or "0020" : reply.Code == "0020")) return false;
                if (reply.Code == "0020") execution = true;
            }
        }
        return execution;
    }
}
