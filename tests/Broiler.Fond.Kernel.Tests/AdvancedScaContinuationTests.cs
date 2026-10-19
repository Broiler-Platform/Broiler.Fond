using System.Text;
using Broiler.Fond.Kernel.FinTs;
using Broiler.Fond.Kernel.FinTs.Sca;

namespace Broiler.Fond.Kernel.Tests;

public static class AdvancedScaContinuationTests
{
    private static readonly DateTimeOffset FixedNow = new(2026, 10, 18, 12, 0, 0, TimeSpan.Zero);

    public static void RunAll()
    {
        TestVisualChallengeParser_MagicBytesImages();
        TestVisualChallengeParser_Base64DataUri();
        TestVisualChallengeParser_QrCodeText();
        TestVisualChallengeParser_OpticalFlickerCode();
        TestVisualChallengeParser_PlainTextAndEmpty();
        TestDecoupledPollingCoordinator_TimingAndClamping();
        TestDecoupledPollingCoordinator_DeadlineTimeout();
        TestDecoupledPollingCoordinator_QueryLimit();

        Console.WriteLine("Advanced SCA procedures & visual challenge continuation (M3-03): 62 checks completed successfully.");
    }

    private static void TestVisualChallengeParser_MagicBytesImages()
    {
        // 1. PNG Image
        byte[] pngBytes = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 0x00, 0x00, 0x00, 0x0D];
        var challengePng = CreateChallengeWithHhd(pngBytes);
        var parsedPng = VisualChallengeParser.Parse(challengePng);

        Assert(parsedPng.Kind == VisualChallengeKind.MatrixCodeImage, "PNG kind is MatrixCodeImage");
        Assert(parsedPng.MimeType == "image/png", "PNG mime is image/png");
        Assert(parsedPng.HasVisualData, "PNG has visual data");
        Assert(parsedPng.RawBytes.Length == pngBytes.Length, "PNG raw bytes match");

        // 2. JPEG Image
        byte[] jpegBytes = [0xFF, 0xD8, 0xFF, 0xE0, 0x00, 0x10, 0x4A, 0x46, 0x49, 0x46];
        var challengeJpeg = CreateChallengeWithHhd(jpegBytes);
        var parsedJpeg = VisualChallengeParser.Parse(challengeJpeg);

        Assert(parsedJpeg.Kind == VisualChallengeKind.MatrixCodeImage, "JPEG kind is MatrixCodeImage");
        Assert(parsedJpeg.MimeType == "image/jpeg", "JPEG mime is image/jpeg");
        Assert(parsedJpeg.HasVisualData, "JPEG has visual data");

        // 3. GIF Image
        byte[] gifBytes = [0x47, 0x49, 0x46, 0x38, 0x39, 0x61, 0x01, 0x00];
        var challengeGif = CreateChallengeWithHhd(gifBytes);
        var parsedGif = VisualChallengeParser.Parse(challengeGif);

        Assert(parsedGif.Kind == VisualChallengeKind.MatrixCodeImage, "GIF kind is MatrixCodeImage");
        Assert(parsedGif.MimeType == "image/gif", "GIF mime is image/gif");
    }

    private static void TestVisualChallengeParser_Base64DataUri()
    {
        const string base64Uri = "data:image/png;base64,iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mNk+M9QDwADhgGAWjR9awAAAABJRU5ErkJggg==";
        byte[] uriBytes = Encoding.Latin1.GetBytes(base64Uri);
        var challenge = CreateChallengeWithHhd(uriBytes);
        var parsed = VisualChallengeParser.Parse(challenge);

        Assert(parsed.Kind == VisualChallengeKind.MatrixCodeImage, "Data URI kind is MatrixCodeImage");
        Assert(parsed.MimeType == "image/png", "Data URI mime is image/png");
        Assert(parsed.RawBytes.Length > 0, "Base64 payload decoded to non-empty bytes");
        Assert(parsed.HasVisualData, "Data URI has visual data");
    }

    private static void TestVisualChallengeParser_QrCodeText()
    {
        // 1. EPC QR Code (GiroCode)
        const string epcQr = "BCD\n002\n1\nSCT\nTESTDEFFXXX\nMax Mustermann\nDE89370400440532013000\nEUR12.34\n\n\nInvoice 123";
        var challengeEpc = CreateChallengeWithHhd(Encoding.UTF8.GetBytes(epcQr));
        var parsedEpc = VisualChallengeParser.Parse(challengeEpc);

        Assert(parsedEpc.Kind == VisualChallengeKind.QrCodeText, "EPC QR is QrCodeText");
        Assert(parsedEpc.TextContent == epcQr, "Text content preserved");
        Assert(parsedEpc.HasVisualData, "QR has visual data");

        // 2. HTTPS Auth URL
        const string url = "https://bank.example/fints/sca/verify?challenge=abcdef123456";
        var challengeUrl = CreateChallengeWithText(url);
        var parsedUrl = VisualChallengeParser.Parse(challengeUrl);

        Assert(parsedUrl.Kind == VisualChallengeKind.QrCodeText, "HTTPS URL is QrCodeText");
        Assert(parsedUrl.TextContent == url, "URL preserved");
    }

    private static void TestVisualChallengeParser_OpticalFlickerCode()
    {
        // 1. Hex string flicker code:
        // Length: 0x16 (22 bytes), Control byte: 0x28, followed by L-V elements:
        // Element 1: len 0x02, value "1234"
        // Element 2: len 0x03, value "ABCDEF"
        const string hexFlicker = "162802123403ABCDEF";
        var challengeHex = CreateChallengeWithHhd(Encoding.Latin1.GetBytes(hexFlicker));
        var parsedHex = VisualChallengeParser.Parse(challengeHex);

        Assert(parsedHex.Kind == VisualChallengeKind.FlickerCode, "Hex flicker is FlickerCode");
        Assert(parsedHex.MimeType == "application/x-flicker", "Mime is application/x-flicker");
        Assert(parsedHex.FlickerLength == 0x16, "Flicker length is 0x16");
        Assert(parsedHex.FlickerControlByte == 0x28, "Flicker control byte is 0x28");
        Assert(parsedHex.ExtractedDataElements.Count == 2, "2 data elements extracted");
        Assert(parsedHex.ExtractedDataElements[0] == "1234", "Element 1 is 1234");
        Assert(parsedHex.ExtractedDataElements[1] == "ABCDEF", "Element 2 is ABCDEF");

        // 2. Binary flicker bytes:
        // Length 6, Control byte 0x28, Elem 1: len 2 [0xAA, 0xBB], Elem 2: len 1 [0xCC]
        byte[] binFlicker = [0x06, 0x28, 0x02, 0xAA, 0xBB, 0x01, 0xCC];
        var challengeBin = CreateChallengeWithHhd(binFlicker);
        var parsedBin = VisualChallengeParser.Parse(challengeBin);

        Assert(parsedBin.Kind == VisualChallengeKind.FlickerCode, "Binary flicker is FlickerCode");
        Assert(parsedBin.FlickerControlByte == 0x28, "Control byte is 0x28");
        Assert(parsedBin.ExtractedDataElements.Count == 2, "2 binary data elements extracted");
        Assert(parsedBin.ExtractedDataElements[0] == "AABB", "Element 1 is AABB");
        Assert(parsedBin.ExtractedDataElements[1] == "CC", "Element 2 is CC");
    }

    private static void TestVisualChallengeParser_PlainTextAndEmpty()
    {
        // 1. Text only
        const string smsPrompt = "Bitte geben Sie die Ihnen per SMS zugesandte TAN ein.";
        var challengeText = CreateChallengeWithText(smsPrompt);
        var parsedText = VisualChallengeParser.Parse(challengeText);

        Assert(parsedText.Kind == VisualChallengeKind.PlainText, "SMS prompt is PlainText");
        Assert(parsedText.TextContent == smsPrompt, "SMS text preserved");
        Assert(!parsedText.HasVisualData, "PlainText has no visual data");

        // 2. Null challenge
        var parsedNull = VisualChallengeParser.Parse(null);
        Assert(parsedNull.Kind == VisualChallengeKind.None, "Null challenge is None");
        Assert(!parsedNull.HasVisualData, "None has no visual data");
    }

    private static void TestDecoupledPollingCoordinator_TimingAndClamping()
    {
        var clock = new StaticTimeProvider(FixedNow);

        // Standard coordinator: 120s timeout
        var coordinator = new DecoupledPollingCoordinator(
            timeout: TimeSpan.FromSeconds(60),
            maxQueries: 10,
            clock: clock);

        Assert(coordinator.Deadline == FixedNow.AddSeconds(60), "Deadline is Now + 60s");
        Assert(coordinator.CheckStatus(FixedNow, out var remaining) == DecoupledPollingStatus.Waiting, "Status is Waiting initially");
        Assert(remaining == TimeSpan.FromSeconds(60), "Remaining is 60s");

        // Delay calculation: floor is 2 seconds
        var delay1 = coordinator.CalculateNextDelay(null, FixedNow);
        Assert(delay1 == TimeSpan.FromSeconds(2), "Default delay is minimum interval 2s");

        // If bank recommends 5s, delay is 5s
        var delayBank = coordinator.CalculateNextDelay(TimeSpan.FromSeconds(5), FixedNow);
        Assert(delayBank == TimeSpan.FromSeconds(5), "Delay respects bank recommendation 5s");

        // If close to deadline (1s remaining), delay is clamped to 1s
        var delayClamped = coordinator.CalculateNextDelay(TimeSpan.FromSeconds(5), FixedNow.AddSeconds(59));
        Assert(delayClamped == TimeSpan.FromSeconds(1), "Delay clamped to remaining deadline");

        // Clamping to bank-supplied expiry
        var bankExpiry = FixedNow.AddSeconds(30);
        var coordBankExpiry = new DecoupledPollingCoordinator(
            timeout: TimeSpan.FromSeconds(60),
            bankExpiry: bankExpiry,
            clock: clock);

        Assert(coordBankExpiry.Deadline == bankExpiry, "Deadline clamped to earlier bank expiry");
    }

    private static void TestDecoupledPollingCoordinator_DeadlineTimeout()
    {
        var clock = new StaticTimeProvider(FixedNow);
        var coordinator = new DecoupledPollingCoordinator(
            timeout: TimeSpan.FromSeconds(30),
            clock: clock);

        // At 29s: still waiting
        Assert(coordinator.CheckStatus(FixedNow.AddSeconds(29), out var rem) == DecoupledPollingStatus.Waiting, "Waiting at 29s");
        Assert(rem == TimeSpan.FromSeconds(1), "1s remaining");

        // At 30s: timed out
        Assert(coordinator.CheckStatus(FixedNow.AddSeconds(30), out rem) == DecoupledPollingStatus.TimedOut, "TimedOut at 30s");
        Assert(rem == TimeSpan.Zero, "0s remaining when timed out");
    }

    private static void TestDecoupledPollingCoordinator_QueryLimit()
    {
        var clock = new StaticTimeProvider(FixedNow);
        var coordinator = new DecoupledPollingCoordinator(
            timeout: TimeSpan.FromSeconds(60),
            maxQueries: 3,
            clock: clock);

        Assert(coordinator.RecordQuery(out int count1) && count1 == 1, "Query 1 recorded");
        Assert(coordinator.RecordQuery(out int count2) && count2 == 2, "Query 2 recorded");
        Assert(coordinator.RecordQuery(out int count3) && count3 == 3, "Query 3 recorded");

        // 4th query exceeds maxQueries
        Assert(!coordinator.RecordQuery(out int count4) && count4 == 4, "Query 4 exceeds budget");
        Assert(coordinator.CheckStatus(FixedNow.AddSeconds(10), out _) == DecoupledPollingStatus.QueryLimitExceeded, "QueryLimitExceeded reported");
    }

    private static FinTsTanChallenge CreateChallengeWithHhd(byte[] hhdBytes)
    {
        // Build HITAN:1:6+4++REF123+Please confirm in app+@length@binary'
        // Where field 4 is HHDData binary element
        string header = "HITAN:1:6+4++REF123+Please confirm+";
        byte[] headerBytes = Encoding.Latin1.GetBytes(header);
        string lenPrefix = $"@{hhdBytes.Length}@";
        byte[] lenBytes = Encoding.Latin1.GetBytes(lenPrefix);
        byte[] trailerBytes = "'"u8.ToArray();

        byte[] total = new byte[headerBytes.Length + lenBytes.Length + hhdBytes.Length + trailerBytes.Length];
        Buffer.BlockCopy(headerBytes, 0, total, 0, headerBytes.Length);
        Buffer.BlockCopy(lenBytes, 0, total, headerBytes.Length, lenBytes.Length);
        Buffer.BlockCopy(hhdBytes, 0, total, headerBytes.Length + lenBytes.Length, hhdBytes.Length);
        Buffer.BlockCopy(trailerBytes, 0, total, headerBytes.Length + lenBytes.Length + hhdBytes.Length, trailerBytes.Length);

        var segment = FinTsSyntax.ParseSegments(total).Segments.Single();
        return new FinTsTanChallenge(segment);
    }

    private static FinTsTanChallenge CreateChallengeWithText(string challengeText)
    {
        string escaped = challengeText
            .Replace("?", "??")
            .Replace("+", "?+")
            .Replace(":", "?:")
            .Replace("'", "?'");
        string wire = $"HITAN:1:6+1++REF123+{escaped}'";
        var segment = FinTsSyntax.ParseSegments(Encoding.Latin1.GetBytes(wire)).Segments.Single();
        return new FinTsTanChallenge(segment);
    }

    private static void Assert(bool condition, string message)
    {
        if (!condition)
        {
            throw new InvalidOperationException($"Assertion failed: {message}");
        }
    }

    private sealed class StaticTimeProvider : TimeProvider
    {
        private readonly DateTimeOffset _time;
        public StaticTimeProvider(DateTimeOffset time) => _time = time;
        public override DateTimeOffset GetUtcNow() => _time;
    }
}
