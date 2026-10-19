using System.Text;
using Broiler.Fond.Kernel.FinTs;

namespace Broiler.Fond.Kernel.Tests;

public static class FinTs41MessageCodecTests
{
    public static void RunAllTests()
    {
        Console.WriteLine("Running FinTS 4.1 XML Message Codec verification (M9-02)...");
        int checks = 0;

        var codec = FinTs41MessageCodec.Instance;

        // 1. Roundtrip Initialization Request
        var header = new FinTs41Header("DLG-TEST-410", 1, "12345678", "SPK-DE-TEST", DateTimeOffset.UtcNow);
        var segHkidn = new FinTs41Segment("HKIDN", 4, 1,
        [
            FinTs41Element.Leaf("bankCode", "12345678"),
            FinTs41Element.Leaf("userId", "testuser"),
            FinTs41Element.Leaf("systemId", "0")
        ]);
        var segHkvvb = new FinTs41Segment("HKVVB", 4, 2,
        [
            FinTs41Element.Leaf("bpdVersion", "0"),
            FinTs41Element.Leaf("updVersion", "0"),
            FinTs41Element.Leaf("dialogueLanguage", "de"),
            FinTs41Element.Leaf("productId", "999999999"),
            FinTs41Element.Leaf("productVersion", "1.0.0")
        ]);
        var trailer = new FinTs41Trailer(1);

        var originalMsg = new FinTs41XmlMessage(header, [segHkidn, segHkvvb], trailer);
        byte[] encoded = codec.EncodeMessage(originalMsg);
        Assert(encoded.Length > 0, "Encoded message must not be empty");
        checks++;

        var decoded = codec.DecodeMessage(encoded);
        Assert(decoded.Header.DialogueId == "DLG-TEST-410", "Dialogue ID must match");
        Assert(decoded.Header.MessageNumber == 1, "Message number must match");
        Assert(decoded.Header.Sender == "12345678", "Sender must match");
        Assert(decoded.Header.Recipient == "SPK-DE-TEST", "Recipient must match");
        Assert(decoded.Segments.Count == 2, "Must decode exactly 2 segments");
        Assert(decoded.Segments[0].Tag == "HKIDN" && decoded.Segments[0].Version == 4, "First segment must be HKIDN v4");
        Assert(decoded.Segments[0].GetElementValue("userId") == "testuser", "userId must match");
        Assert(decoded.Segments[1].Tag == "HKVVB" && decoded.Segments[1].Version == 4, "Second segment must be HKVVB v4");
        Assert(decoded.Segments[1].GetElementValue("productId") == "999999999", "productId must match");
        Assert(decoded.Trailer.MessageNumber == 1, "Trailer message number must match");
        checks += 10;

        // 2. Bank Response with HIRMG and HIRMS return codes
        var respHeader = new FinTs41Header("DLG-TEST-410", 1, "SPK-DE-TEST", "12345678", DateTimeOffset.UtcNow);
        var segHirmg = new FinTs41Segment("HIRMG", 2, 1,
        [
            FinTs41Element.Leaf("code", "0010"),
            FinTs41Element.Leaf("text", "Dialog initialisiert")
        ]);
        var segHirms = new FinTs41Segment("HIRMS", 2, 2,
        [
            FinTs41Element.Leaf("code", "0020"),
            FinTs41Element.Leaf("text", "Information fehlerfrei entgegengenommen"),
            FinTs41Element.Leaf("referenceSegment", "HKIDN:1")
        ]);
        var respMsg = new FinTs41XmlMessage(respHeader, [segHirmg, segHirms], new FinTs41Trailer(1));
        byte[] respEncoded = codec.EncodeMessage(respMsg);
        var decodedResp = codec.DecodeMessage(respEncoded);

        var returnCodes = decodedResp.ExtractReturnCodes();
        Assert(returnCodes.Count == 2, "Must extract 2 return codes");
        Assert(returnCodes[0].Code == "0010" && returnCodes[0].IsSuccess, "0010 must be success");
        Assert(returnCodes[0].Text == "Dialog initialisiert", "Return code text must match");
        Assert(returnCodes[1].Code == "0020" && returnCodes[1].IsSuccess, "0020 must be success");
        Assert(returnCodes[1].ReferenceSegment == "HKIDN:1", "Reference segment must match");
        checks += 5;

        // 3. Mismatched trailer message number
        var badTrailerMsg = new FinTs41XmlMessage(header, [segHkidn], new FinTs41Trailer(99));
        byte[] badTrailerBytes = codec.EncodeMessage(badTrailerMsg);
        bool threwMismatch = false;
        try
        {
            codec.DecodeMessage(badTrailerBytes);
        }
        catch (FinTs41MalformedXmlException ex)
        {
            threwMismatch = true;
            Assert(ex.Message.Contains("framing mismatch"), "Must report message number mismatch");
        }
        Assert(threwMismatch, "Mismatched trailer must throw FinTs41MalformedXmlException");
        checks += 2;

        // 4. Empty payload
        bool threwEmpty = false;
        try
        {
            codec.DecodeMessage(Array.Empty<byte>());
        }
        catch (FinTs41MalformedXmlException ex)
        {
            threwEmpty = true;
            Assert(ex.Message.Contains("empty"), "Must report empty payload");
        }
        Assert(threwEmpty, "Empty payload must throw FinTs41MalformedXmlException");
        checks += 2;

        // 5. Malformed XML syntax
        bool threwSyntax = false;
        try
        {
            codec.DecodeMessage(Encoding.UTF8.GetBytes("<FinTS><HNHBK><dialogueId>123</HNHBK></FinTS>"));
        }
        catch (FinTs41MalformedXmlException)
        {
            threwSyntax = true;
        }
        Assert(threwSyntax, "Unclosed or malformed XML syntax must throw FinTs41MalformedXmlException");
        checks++;

        // 6. DTD Prohibited
        string hostileDtd = "<?xml version=\"1.0\"?>\n<!DOCTYPE FinTS [<!ENTITY xxe SYSTEM \"file:///etc/passwd\">]>\n<FinTS><HNHBK><dialogueId>&xxe;</dialogueId></HNHBK></FinTS>";
        bool threwDtd = false;
        try
        {
            codec.DecodeMessage(Encoding.UTF8.GetBytes(hostileDtd));
        }
        catch (FinTs41MalformedXmlException ex)
        {
            threwDtd = true;
            Assert(ex.InnerException is System.Xml.XmlException, "Inner exception must be XmlException for DTD");
        }
        Assert(threwDtd, "DTD injection must be prohibited");
        checks += 2;

        // 7. Missing trailer
        string noTrailer = "<FinTS xmlns=\"urn:org:fints:spec:xml:2008\"><HNHBK version=\"4\"><dialogueId>1</dialogueId><messageNumber>1</messageNumber><sender>A</sender><recipient>B</recipient><timestamp>2026-10-19T12:00:00Z</timestamp></HNHBK></FinTS>";
        bool threwNoTrailer = false;
        try
        {
            codec.DecodeMessage(Encoding.UTF8.GetBytes(noTrailer));
        }
        catch (FinTs41MalformedXmlException ex)
        {
            threwNoTrailer = true;
            Assert(ex.Message.Contains("Missing mandatory FinTS 4.1 trailer"), "Must report missing trailer");
        }
        Assert(threwNoTrailer, "Missing trailer must fail closed");
        checks += 2;

        Console.WriteLine($"FinTS 4.1 XML Message Codec (M9-02): {checks} checks completed successfully.");
    }

    private static void Assert(bool condition, string message)
    {
        if (!condition)
        {
            throw new InvalidOperationException($"Assertion failed: {message}");
        }
    }
}
