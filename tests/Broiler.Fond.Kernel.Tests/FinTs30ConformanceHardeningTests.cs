using System.Text;
using Broiler.Fond.Kernel.FinTs;

namespace Broiler.Fond.Kernel.Tests;

public static class FinTs30ConformanceHardeningTests
{
    public static void Run()
    {
        Console.WriteLine("Running FinTS 3.0 Conformance Hardening verification (M7-01)...");
        int checks = 0;

        void Assert(bool condition, string message)
        {
            if (!condition)
            {
                throw new InvalidOperationException($"FinTS 3.0 Conformance check failed: {message}");
            }
            checks++;
        }

        // 1. Frozen capability matrix verification
        Assert(FinTs30CapabilityLock.FrozenMatrix.Count > 25, "Frozen matrix contains complete segment catalog.");
        Assert(FinTs30CapabilityLock.IsSegmentSupported("HNHBK", 3), "HNHBK version 3 is supported.");
        Assert(!FinTs30CapabilityLock.IsSegmentSupported("HNHBK", 2), "HNHBK version 2 is not supported in FinTS 3.0.");
        Assert(!FinTs30CapabilityLock.IsSegmentSupported("HNHBK", 4), "HNHBK version 4 is not supported.");

        Assert(FinTs30CapabilityLock.IsSegmentSupported("HKIDN", 2), "HKIDN version 2 is supported.");
        Assert(FinTs30CapabilityLock.IsSegmentSupported("HKVVB", 3), "HKVVB version 3 is supported.");
        Assert(FinTs30CapabilityLock.IsSegmentSupported("HKTAN", 6) && FinTs30CapabilityLock.IsSegmentSupported("HKTAN", 7), "HKTAN versions 6 and 7 are supported.");
        Assert(FinTs30CapabilityLock.IsSegmentSupported("HKSAL", 5) && FinTs30CapabilityLock.IsSegmentSupported("HKSAL", 7), "HKSAL versions 5, 6, 7 are supported.");
        Assert(FinTs30CapabilityLock.IsSegmentSupported("HKKAZ", 6) && FinTs30CapabilityLock.IsSegmentSupported("HKCAZ", 1), "HKKAZ and HKCAZ are supported.");
        Assert(FinTs30CapabilityLock.IsSegmentSupported("HKCCS", 1), "Single SEPA transfer HKCCS version 1 is supported.");
        Assert(FinTs30CapabilityLock.IsSegmentSupported("HKIPB", 1), "Instant SEPA transfer HKIPB version 1 is supported.");
        Assert(FinTs30CapabilityLock.IsSegmentSupported("HKCDB", 1) && FinTs30CapabilityLock.IsSegmentSupported("HKCDL", 1), "Scheduled transfers HKCDB/HKCDL are supported.");
        Assert(FinTs30CapabilityLock.IsSegmentSupported("HKDMB", 1) && FinTs30CapabilityLock.IsSegmentSupported("HKDLS", 1), "Standing orders HKDMB/HKDLS are supported.");
        Assert(FinTs30CapabilityLock.IsSegmentSupported("HKLAS", 4), "Direct-debit return write HKLAS version 4 is supported.");

        // Rejection of unknown / speculative segments
        Assert(!FinTs30CapabilityLock.IsSegmentSupported("HK41X", 1), "Speculative FinTS 4.1 segment HK41X is rejected.");
        Assert(!FinTs30CapabilityLock.IsSegmentSupported("EBICS", 1), "EBICS segments outside FinTS are rejected.");

        try
        {
            FinTs30CapabilityLock.ValidateSegment("HKVVB", 99);
            Assert(false, "ValidateSegment must throw for unsupported version 99.");
        }
        catch (UnsupportedSegmentVersionException ex)
        {
            Assert(ex.SegmentCode == "HKVVB", "Exception carries SegmentCode.");
            Assert(ex.RequestedVersion == 99, "Exception carries RequestedVersion.");
            Assert(ex.SupportedVersions.Contains(3), "Exception lists valid versions.");
        }

        // 2. Conformance harness parser with capability lock
        const string validMsg = "HNHBK:1:3+000000000100+300+0+1'HKIDN:2:2+280:10050000+USER1+0+0'HKVVB:3:3+0+0+1+BroilerFond+1.0'HNHBS:4:3+1'";
        byte[] validBytes = Encoding.Latin1.GetBytes(validMsg);

        var doc = FinTs30ConformanceHarness.ValidateAndParse(validBytes, enforceCapabilityLock: true);
        Assert(doc.Segments.Count == 4, "Parsed 4 valid FinTS 3.0 segments.");
        Assert(doc.Segments[0].Code == "HNHBK" && doc.Segments[0].Version == 3, "HNHBK is version 3.");
        Assert(doc.Segments[2].Code == "HKVVB" && doc.Segments[2].Version == 3, "HKVVB is version 3.");

        // Message containing unsupported segment
        const string invalidMsg = "HNHBK:1:3+000000000100+300+0+1'HKVVB:2:99+0+0+1+BroilerFond+1.0'HNHBS:3:3+1'";
        byte[] invalidBytes = Encoding.Latin1.GetBytes(invalidMsg);

        try
        {
            FinTs30ConformanceHarness.ValidateAndParse(invalidBytes, enforceCapabilityLock: true);
            Assert(false, "ValidateAndParse must throw on unsupported segment version.");
        }
        catch (UnsupportedSegmentVersionException)
        {
            Assert(true, "ValidateAndParse threw UnsupportedSegmentVersionException as expected.");
        }

        // 3. Bank return code classification
        Assert(FinTs30ConformanceHarness.ClassifyReturnCode(10) == FinTsReturnCodeClassification.Success, "0010 is Success.");
        Assert(FinTs30ConformanceHarness.ClassifyReturnCode(20) == FinTsReturnCodeClassification.Success, "0020 is Success.");
        Assert(FinTs30ConformanceHarness.ClassifyReturnCode(3010) == FinTsReturnCodeClassification.Warning, "3010 is Warning.");
        Assert(FinTs30ConformanceHarness.ClassifyReturnCode(3060) == FinTsReturnCodeClassification.Warning, "3060 is Warning.");
        Assert(FinTs30ConformanceHarness.ClassifyReturnCode(3920) == FinTsReturnCodeClassification.ScaRequired, "3920 is ScaRequired.");
        Assert(FinTs30ConformanceHarness.ClassifyReturnCode(3955) == FinTsReturnCodeClassification.ScaRequired, "3955 is ScaRequired.");
        Assert(FinTs30ConformanceHarness.ClassifyReturnCode(9010) == FinTsReturnCodeClassification.AuthenticationFailed, "9010 is AuthFailed.");
        Assert(FinTs30ConformanceHarness.ClassifyReturnCode(9050) == FinTsReturnCodeClassification.AuthenticationFailed, "9050 is AuthFailed.");
        Assert(FinTs30ConformanceHarness.ClassifyReturnCode(9210) == FinTsReturnCodeClassification.AuthenticationFailed, "9210 is AuthFailed.");
        Assert(FinTs30ConformanceHarness.ClassifyReturnCode(9999) == FinTsReturnCodeClassification.BankSystemError, "9999 is BankSystemError.");
        Assert(FinTs30ConformanceHarness.ClassifyReturnCode(9800) == FinTsReturnCodeClassification.GeneralError, "9800 is GeneralError.");

        // 4. Failure injection fuzzing corpus
        var corpus = FinTs30ConformanceHarness.GenerateFuzzCorpus(validBytes);
        Assert(corpus.Count >= 5, "Corpus generated at least 5 distinct failure-injection vectors.");

        foreach (var vector in corpus)
        {
            bool threwExpected = false;
            try
            {
                FinTs30ConformanceHarness.ValidateAndParse(vector.WireBytes, enforceCapabilityLock: true);
            }
            catch (Exception ex) when (vector.ExpectedExceptionType.IsAssignableFrom(ex.GetType()))
            {
                threwExpected = true;
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException($"Vector '{vector.Name}' threw unexpected exception {ex.GetType().Name} instead of {vector.ExpectedExceptionType.Name}: {ex.Message}");
            }

            Assert(threwExpected, $"Fuzz vector '{vector.Name}' safely rejected with {vector.ExpectedExceptionType.Name}.");
        }

        Console.WriteLine($"FinTS 3.0 Conformance Hardening (M7-01): {checks} checks completed successfully.");
    }
}
