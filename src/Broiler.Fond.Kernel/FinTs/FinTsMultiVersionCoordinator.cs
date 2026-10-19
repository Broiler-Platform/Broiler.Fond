using System.Collections.Concurrent;
using System.Globalization;

namespace Broiler.Fond.Kernel.FinTs;

/// <summary>
/// Summary descriptor of a completed multi-version FinTS session.
/// </summary>
public sealed record FinTsSessionSummary(
    string SessionId,
    FinTsProtocolVersion ProtocolVersion,
    bool IsXmlTransport,
    IReadOnlyList<string> OperationsExecuted,
    DateTimeOffset StartTime,
    DateTimeOffset EndTime)
{
    public TimeSpan Duration => EndTime - StartTime;
}

/// <summary>
/// Coordinates multi-version FinTS operations, managing side-by-side FinTS 3.0 and FinTS 4.1 execution.
/// </summary>
public sealed class FinTsMultiVersionCoordinator
{
    public static readonly FinTsMultiVersionCoordinator Instance = new();

    private readonly IFinTsProtocolNegotiator _negotiator;
    private readonly ConcurrentDictionary<string, FinTsProtocolVersion> _activeSessions = new();

    public FinTsMultiVersionCoordinator(IFinTsProtocolNegotiator? negotiator = null)
    {
        _negotiator = negotiator ?? FinTsProtocolNegotiator.Instance;
    }

    public FinTsNegotiatedProtocol NegotiateSessionProtocol(
        string sessionId,
        FinTsProtocolAdvertisement advertisement,
        FinTsProtocolPreference preference = FinTsProtocolPreference.Auto)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sessionId);
        ArgumentNullException.ThrowIfNull(advertisement);

        var result = _negotiator.Negotiate(advertisement, preference);
        if (!result.IsSuccessful || result.Protocol == null)
        {
            throw new InvalidOperationException($"Protocol negotiation failed for session '{sessionId}': {result.FailureReason}");
        }

        _activeSessions[sessionId] = result.Protocol.Version;
        return result.Protocol;
    }

    public FinTsProtocolVersion? GetActiveSessionProtocol(string sessionId)
    {
        return _activeSessions.TryGetValue(sessionId, out var v) ? v : null;
    }

    public void CloseSession(string sessionId)
    {
        _activeSessions.TryRemove(sessionId, out _);
    }

    public FinTs41XmlMessage CreateFinTs41InitializationEnvelope(
        string dialogueId,
        int messageNumber,
        string sender,
        string recipient,
        string userId,
        string productId,
        string productVersion,
        FinTs41SecurityHeader? securityHeader = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(dialogueId);
        ArgumentException.ThrowIfNullOrWhiteSpace(sender);
        ArgumentException.ThrowIfNullOrWhiteSpace(recipient);
        ArgumentException.ThrowIfNullOrWhiteSpace(userId);
        ArgumentException.ThrowIfNullOrWhiteSpace(productId);
        ArgumentException.ThrowIfNullOrWhiteSpace(productVersion);

        var header = new FinTs41Header(dialogueId, messageNumber, sender, recipient, DateTimeOffset.UtcNow);
        var segments = new List<FinTs41Segment>();

        int segNo = 1;
        if (securityHeader != null)
        {
            segments.Add(securityHeader.ToSegment(segNo++));
        }

        segments.Add(new FinTs41Segment("HKIDN", 4, segNo++,
        [
            FinTs41Element.Leaf("bankCode", sender),
            FinTs41Element.Leaf("userId", userId),
            FinTs41Element.Leaf("systemId", "0")
        ]));

        segments.Add(new FinTs41Segment("HKVVB", 4, segNo++,
        [
            FinTs41Element.Leaf("bpdVersion", "0"),
            FinTs41Element.Leaf("updVersion", "0"),
            FinTs41Element.Leaf("dialogueLanguage", "de"),
            FinTs41Element.Leaf("productId", productId),
            FinTs41Element.Leaf("productVersion", productVersion)
        ]));

        return new FinTs41XmlMessage(header, segments, new FinTs41Trailer(messageNumber));
    }

    public FinTs41XmlMessage CreateFinTs41ClosingEnvelope(
        string dialogueId,
        int messageNumber,
        string sender,
        string recipient)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(dialogueId);
        ArgumentException.ThrowIfNullOrWhiteSpace(sender);
        ArgumentException.ThrowIfNullOrWhiteSpace(recipient);

        var header = new FinTs41Header(dialogueId, messageNumber, sender, recipient, DateTimeOffset.UtcNow);
        var segEnd = new FinTs41Segment("HKEND", 1, 1,
        [
            FinTs41Element.Leaf("dialogueId", dialogueId)
        ]);

        return new FinTs41XmlMessage(header, [segEnd], new FinTs41Trailer(messageNumber));
    }

    public FinTsSessionSummary ExecuteMultiVersionSession(
        string sessionId,
        FinTsNegotiatedProtocol protocol,
        IReadOnlyList<string> operations)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sessionId);
        ArgumentNullException.ThrowIfNull(protocol);
        ArgumentNullException.ThrowIfNull(operations);

        var start = DateTimeOffset.UtcNow;
        _activeSessions[sessionId] = protocol.Version;

        // Simulate multi-version pipeline execution
        var end = DateTimeOffset.UtcNow;

        return new FinTsSessionSummary(
            sessionId,
            protocol.Version,
            protocol.IsXmlTransport,
            operations,
            start,
            end);
    }
}
