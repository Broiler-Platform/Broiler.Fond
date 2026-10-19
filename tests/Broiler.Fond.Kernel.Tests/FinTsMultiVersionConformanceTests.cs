using Broiler.Fond.Kernel.FinTs;

namespace Broiler.Fond.Kernel.Tests;

public static class FinTsMultiVersionConformanceTests
{
    public static void RunAllTests()
    {
        Console.WriteLine("Running Multi-Version Conformance Harness verification (M9-05)...");
        int checks = 0;

        var coordinator = FinTsMultiVersionCoordinator.Instance;
        var codec = FinTs41MessageCodec.Instance;

        // 1. Concurrent side-by-side session negotiation
        string session30 = "SESSION-F30-001";
        string session41 = "SESSION-F41-002";

        var proto30 = coordinator.NegotiateSessionProtocol(
            session30,
            FinTsProtocolAdvertisement.FinTs30Only,
            FinTsProtocolPreference.Auto);
        Assert(proto30.Version == FinTsProtocolVersion.FinTs30, "Session 30 must negotiate 3.0");
        Assert(!proto30.IsXmlTransport, "3.0 is not XML");
        checks += 2;

        var proto41 = coordinator.NegotiateSessionProtocol(
            session41,
            FinTsProtocolAdvertisement.FinTs41Only,
            FinTsProtocolPreference.Auto);
        Assert(proto41.Version == FinTsProtocolVersion.FinTs41, "Session 41 must negotiate 4.1");
        Assert(proto41.IsXmlTransport, "4.1 is XML");
        checks += 2;

        // Verify side-by-side active sessions
        Assert(coordinator.GetActiveSessionProtocol(session30) == FinTsProtocolVersion.FinTs30, "Session 30 active version must be 3.0");
        Assert(coordinator.GetActiveSessionProtocol(session41) == FinTsProtocolVersion.FinTs41, "Session 41 active version must be 4.1");
        checks += 2;

        // 2. Initialization Envelope Generation (FinTS 4.1)
        var secHeader = new FinTs41SecurityHeader("PIN:2", "920", "DLG-INIT-410:1", DateTimeOffset.UtcNow);
        var initMsg = coordinator.CreateFinTs41InitializationEnvelope(
            "DLG-INIT-410",
            1,
            "12030000",
            "DKB-CORE-41",
            "kundennummer1",
            "999999999",
            "1.0.0",
            secHeader);

        Assert(initMsg.Header.DialogueId == "DLG-INIT-410", "Init envelope dialogue ID must match");
        Assert(initMsg.Segments.Count == 3, "Init envelope must have 3 segments (HNSHK, HKIDN, HKVVB)");
        Assert(initMsg.Segments[0].Tag == "HNSHK", "First segment must be HNSHK");
        Assert(initMsg.Segments[1].Tag == "HKIDN", "Second segment must be HKIDN");
        Assert(initMsg.Segments[2].Tag == "HKVVB", "Third segment must be HKVVB");
        checks += 5;

        // Roundtrip encode/decode of generated init message
        byte[] initBytes = codec.EncodeMessage(initMsg);
        var decodedInit = codec.DecodeMessage(initBytes);
        Assert(decodedInit.Segments.Count == 3, "Decoded init message must retain 3 segments");
        Assert(decodedInit.Segments[1].GetElementValue("userId") == "kundennummer1", "userId must match");
        checks += 2;

        // 3. Closing Envelope Generation (FinTS 4.1)
        var closeMsg = coordinator.CreateFinTs41ClosingEnvelope(
            "DLG-INIT-410",
            2,
            "12030000",
            "DKB-CORE-41");

        Assert(closeMsg.Header.DialogueId == "DLG-INIT-410", "Close envelope dialogue ID must match");
        Assert(closeMsg.Segments.Count == 1 && closeMsg.Segments[0].Tag == "HKEND", "Close envelope must have HKEND");
        Assert(closeMsg.Segments[0].GetElementValue("dialogueId") == "DLG-INIT-410", "HKEND dialogueId must match");
        checks += 3;

        byte[] closeBytes = codec.EncodeMessage(closeMsg);
        var decodedClose = codec.DecodeMessage(closeBytes);
        Assert(decodedClose.Segments.Count == 1, "Decoded close message must have 1 segment");
        checks++;

        // 4. Session Execution and Summary
        var summary = coordinator.ExecuteMultiVersionSession(
            session41,
            proto41,
            ["HKIDN", "HKVVB", "HKSPA", "HKSAL", "HKEND"]);

        Assert(summary.SessionId == session41, "SessionId must match");
        Assert(summary.ProtocolVersion == FinTsProtocolVersion.FinTs41, "ProtocolVersion must match");
        Assert(summary.IsXmlTransport, "IsXmlTransport must be true");
        Assert(summary.OperationsExecuted.Count == 5, "Operations count must match");
        checks += 4;

        // 5. Session Cleanup
        coordinator.CloseSession(session30);
        coordinator.CloseSession(session41);
        Assert(coordinator.GetActiveSessionProtocol(session30) == null, "Session 30 must be closed");
        Assert(coordinator.GetActiveSessionProtocol(session41) == null, "Session 41 must be closed");
        checks += 2;

        Console.WriteLine($"Multi-Version Conformance Harness (M9-05): {checks} checks completed successfully.");
    }

    private static void Assert(bool condition, string message)
    {
        if (!condition)
        {
            throw new InvalidOperationException($"Assertion failed: {message}");
        }
    }
}
