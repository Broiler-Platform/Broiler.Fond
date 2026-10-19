using Broiler.Fond.Kernel.FinTs;

namespace Broiler.Fond.Kernel.Tests;

public static class FinTs41SecurityCodecTests
{
    public static void RunAllTests()
    {
        Console.WriteLine("Running FinTS 4.1 Security & SCA Codec verification (M9-03)...");
        int checks = 0;

        // 1. Security Header HNSHK v4 roundtrip
        var now = DateTimeOffset.UtcNow;
        var header = new FinTs41SecurityHeader("PIN:2", "920", "DLG-410:1", now);
        var headerSeg = header.ToSegment(1);
        Assert(headerSeg.Tag == "HNSHK" && headerSeg.Version == 4 && headerSeg.SegmentNumber == 1, "Header segment tag/version must match");
        checks++;

        var parsedHeader = FinTs41SecurityHeader.FromSegment(headerSeg);
        Assert(parsedHeader.SecurityProfile == "PIN:2", "Security profile must match");
        Assert(parsedHeader.SecurityFunction == "920", "Security function must match");
        Assert(parsedHeader.SecurityReference == "DLG-410:1", "Security reference must match");
        checks += 3;

        // 2. Security Trailer HNSHA v2 roundtrip with PIN and TAN
        using (var trailer = new FinTs41SecurityTrailer("DLG-410:1", "12345".AsSpan(), "654321".AsSpan()))
        {
            var trailerSeg = trailer.ToSegment(999);
            Assert(trailerSeg.Tag == "HNSHA" && trailerSeg.Version == 2 && trailerSeg.SegmentNumber == 999, "Trailer segment tag/version must match");
            Assert(trailer.ToString().Contains("PIN=***"), "ToString must mask PIN");
            Assert(trailer.ToString().Contains("TAN=***"), "ToString must mask TAN");
            checks += 3;

            using var parsedTrailer = FinTs41SecurityTrailer.FromSegment(trailerSeg);
            Assert(parsedTrailer.SecurityReference == "DLG-410:1", "Parsed trailer reference must match");
            checks++;
        }

        // 3. Security Trailer zeroization on Dispose
        var disposedTrailer = new FinTs41SecurityTrailer("DLG-410:1", "secretPin".AsSpan());
        disposedTrailer.Dispose();
        bool threwDisposed = false;
        try
        {
            disposedTrailer.ToSegment();
        }
        catch (ObjectDisposedException)
        {
            threwDisposed = true;
        }
        Assert(threwDisposed, "ToSegment on disposed trailer must throw ObjectDisposedException");
        checks++;

        // 4. ValidateSecurityBinding matching vs mismatch
        using var validTrailer = new FinTs41SecurityTrailer("DLG-410:1", "pin".AsSpan());
        FinTs41ScaChallengeParser.ValidateSecurityBinding(header, validTrailer);
        checks++;

        using var mismatchTrailer = new FinTs41SecurityTrailer("DLG-OTHER:2", "pin".AsSpan());
        bool threwMismatch = false;
        try
        {
            FinTs41ScaChallengeParser.ValidateSecurityBinding(header, mismatchTrailer);
        }
        catch (InvalidOperationException ex)
        {
            threwMismatch = true;
            Assert(ex.Message.Contains("binding mismatch"), "Must report binding mismatch");
        }
        Assert(threwMismatch, "Mismatched security references must fail validation");
        checks += 2;

        // 5. Parse HITAN v7 Decoupled / Push challenge
        var hitanPush = new FinTs41Segment("HITAN", 7, 2,
        [
            FinTs41Element.Leaf("processVariant", "1"),
            FinTs41Element.Leaf("challenge", "Bitte Auftrag in der App freigeben"),
            FinTs41Element.Leaf("tanMediumName", "Pixel-7-App")
        ]);
        var scaPush = FinTs41ScaChallengeParser.ParseHitan(hitanPush);
        Assert(scaPush != null, "ParseHitan must return challenge");
        Assert(scaPush!.IsDecoupledOrPush, "Must be decoupled/push");
        Assert(!scaPush.IsPhotoTan && !scaPush.IsFlicker, "Must not be photoTAN or flicker");
        Assert(scaPush.ChallengeText == "Bitte Auftrag in der App freigeben", "Challenge text must match");
        Assert(scaPush.TanMediumName == "Pixel-7-App", "TanMediumName must match");
        checks += 5;

        // 6. Parse HITAN v7 PhotoTan / Matrix challenge
        var hitanPhoto = new FinTs41Segment("HITAN", 7, 2,
        [
            FinTs41Element.Leaf("processVariant", "1"),
            FinTs41Element.Leaf("challenge", "Bitte QR-Code scannen"),
            FinTs41Element.Leaf("challengeMatrix", "iVBORw0KGgoAAAANSUhEUgAA...")
        ]);
        var scaPhoto = FinTs41ScaChallengeParser.ParseHitan(hitanPhoto);
        Assert(scaPhoto != null && scaPhoto.IsPhotoTan, "Must be identified as photoTan");
        Assert(!string.IsNullOrEmpty(scaPhoto!.ChallengeMatrix), "Matrix data must be present");
        checks += 2;

        // 7. Parse HITAN v7 ChipTan Flicker challenge
        var hitanFlicker = new FinTs41Segment("HITAN", 7, 2,
        [
            FinTs41Element.Leaf("processVariant", "1"),
            FinTs41Element.Leaf("challenge", "Lesegerät an Bildschirm halten"),
            FinTs41Element.Leaf("challengeHhduc", "0F0A010203040506070809")
        ]);
        var scaFlicker = FinTs41ScaChallengeParser.ParseHitan(hitanFlicker);
        Assert(scaFlicker != null && scaFlicker.IsFlicker, "Must be identified as flicker");
        Assert(!string.IsNullOrEmpty(scaFlicker!.ChallengeHhduc), "Hhduc data must be present");
        checks += 2;

        // 8. Create HKTAN v7 request segment
        var hktanReq = FinTs41ScaChallengeParser.CreateHktanRequest(3, 2, "Pixel-7-App", "987654");
        Assert(hktanReq.Tag == "HKTAN" && hktanReq.Version == 7 && hktanReq.SegmentNumber == 3, "HKTAN request header must match");
        Assert(hktanReq.GetElementValue("processVariant") == "2", "processVariant must be 2");
        Assert(hktanReq.GetElementValue("tanMediumName") == "Pixel-7-App", "tanMediumName must match");
        Assert(hktanReq.GetElementValue("responseTan") == "987654", "responseTan must match");
        checks += 4;

        Console.WriteLine($"FinTS 4.1 Security & SCA Codec (M9-03): {checks} checks completed successfully.");
    }

    private static void Assert(bool condition, string message)
    {
        if (!condition)
        {
            throw new InvalidOperationException($"Assertion failed: {message}");
        }
    }
}
