using Broiler.Fond.Kernel.FinTs;

namespace Broiler.Fond.Kernel.Tests;

public static class FinTsProtocolNegotiationTests
{
    public static void RunAllTests()
    {
        Console.WriteLine("Running FinTS Protocol Negotiation verification (M9-01)...");
        int checks = 0;

        var negotiator = FinTsProtocolNegotiator.Instance;

        // 1. Auto with 3.0 only
        var res1 = negotiator.Negotiate(FinTsProtocolAdvertisement.FinTs30Only, FinTsProtocolPreference.Auto);
        Assert(res1.IsSuccessful, "Auto on 3.0 only should succeed");
        Assert(res1.Protocol!.Version == FinTsProtocolVersion.FinTs30, "Should negotiate 3.0");
        Assert(!res1.Protocol.IsXmlTransport, "FinTS 3.0 is not XML");
        Assert(res1.Protocol.ProtocolIdentifier == "FinTS-3.0", "Identifier must be FinTS-3.0");
        Assert(res1.Protocol.MajorVersion == 3 && res1.Protocol.MinorVersion == 0, "Version must be 3.0");
        checks += 5;

        // 2. Auto with 4.1 only
        var res2 = negotiator.Negotiate(FinTsProtocolAdvertisement.FinTs41Only, FinTsProtocolPreference.Auto);
        Assert(res2.IsSuccessful, "Auto on 4.1 only should succeed");
        Assert(res2.Protocol!.Version == FinTsProtocolVersion.FinTs41, "Should negotiate 4.1");
        Assert(res2.Protocol.IsXmlTransport, "FinTS 4.1 is XML");
        Assert(res2.Protocol.ProtocolIdentifier == "FinTS-4.1", "Identifier must be FinTS-4.1");
        Assert(res2.Protocol.MajorVersion == 4 && res2.Protocol.MinorVersion == 1, "Version must be 4.1");
        checks += 5;

        // 3. Auto with Dual 3.0 and 4.1 prefers 4.1
        var res3 = negotiator.Negotiate(FinTsProtocolAdvertisement.Dual30And41, FinTsProtocolPreference.Auto);
        Assert(res3.IsSuccessful, "Auto on dual should succeed");
        Assert(res3.Protocol!.Version == FinTsProtocolVersion.FinTs41, "Auto on dual should choose 4.1");
        checks += 2;

        // 4. PreferFinTs41 on dual chooses 4.1
        var res4 = negotiator.Negotiate(FinTsProtocolAdvertisement.Dual30And41, FinTsProtocolPreference.PreferFinTs41);
        Assert(res4.IsSuccessful && res4.Protocol!.Version == FinTsProtocolVersion.FinTs41, "Prefer 4.1 should choose 4.1 on dual");
        checks += 1;

        // 5. PreferFinTs41 with 3.0 only falls back to 3.0
        var res5 = negotiator.Negotiate(FinTsProtocolAdvertisement.FinTs30Only, FinTsProtocolPreference.PreferFinTs41);
        Assert(res5.IsSuccessful && res5.Protocol!.Version == FinTsProtocolVersion.FinTs30, "Prefer 4.1 on 3.0 only falls back to 3.0");
        checks += 1;

        // 6. ForceFinTs30 on dual chooses 3.0
        var res6 = negotiator.Negotiate(FinTsProtocolAdvertisement.Dual30And41, FinTsProtocolPreference.ForceFinTs30);
        Assert(res6.IsSuccessful && res6.Protocol!.Version == FinTsProtocolVersion.FinTs30, "Force 3.0 on dual must choose 3.0");
        checks += 1;

        // 7. ForceFinTs30 on 4.1 only fails
        var res7 = negotiator.Negotiate(FinTsProtocolAdvertisement.FinTs41Only, FinTsProtocolPreference.ForceFinTs30);
        Assert(!res7.IsSuccessful, "Force 3.0 on 4.1 only must fail");
        Assert(res7.FailureReason != null && res7.FailureReason.Contains("does not advertise FinTS 3.0"), "Reason must be explicit");
        checks += 2;

        // 8. ForceFinTs41 on 3.0 only fails
        var res8 = negotiator.Negotiate(FinTsProtocolAdvertisement.FinTs30Only, FinTsProtocolPreference.ForceFinTs41);
        Assert(!res8.IsSuccessful, "Force 4.1 on 3.0 only must fail");
        Assert(res8.FailureReason != null && res8.FailureReason.Contains("does not advertise FinTS 4.1"), "Reason must be explicit");
        checks += 2;

        // 9. ForceFinTs41 on dual succeeds
        var res9 = negotiator.Negotiate(FinTsProtocolAdvertisement.Dual30And41, FinTsProtocolPreference.ForceFinTs41);
        Assert(res9.IsSuccessful && res9.Protocol!.Version == FinTsProtocolVersion.FinTs41, "Force 4.1 on dual must succeed");
        checks += 1;

        // 10. Empty advertisement fails
        var res10 = negotiator.Negotiate(new FinTsProtocolAdvertisement(Array.Empty<FinTsProtocolVersion>()));
        Assert(!res10.IsSuccessful, "Empty advertisement must fail");
        checks += 1;

        // 11. Legacy HBCI 2.2 throws UnsupportedProtocolVersionException
        bool threwHbci = false;
        try
        {
            negotiator.Negotiate(new FinTsProtocolAdvertisement([FinTsProtocolVersion.FinTs30], "HBCI 2.2"));
        }
        catch (UnsupportedProtocolVersionException ex)
        {
            threwHbci = true;
            Assert(ex.AttemptedVersion == "HBCI 2.2", "Exception must capture attempted version");
            Assert(ex.Message.Contains("Legacy protocol"), "Exception message must identify legacy protocol rejection");
        }
        Assert(threwHbci, "Legacy HBCI 2.2 must throw UnsupportedProtocolVersionException");
        checks += 3;

        // 12. Legacy HBCI 2.01 throws UnsupportedProtocolVersionException
        bool threwHbci20 = false;
        try
        {
            negotiator.Negotiate(new FinTsProtocolAdvertisement([FinTsProtocolVersion.FinTs30], "HBCI2.01"));
        }
        catch (UnsupportedProtocolVersionException)
        {
            threwHbci20 = true;
        }
        Assert(threwHbci20, "Legacy HBCI 2.01 must throw UnsupportedProtocolVersionException");
        checks += 1;

        // 13. ValidateDialogueBinding passes on matching versions
        negotiator.ValidateDialogueBinding(FinTsProtocolVersion.FinTs41, FinTsProtocolVersion.FinTs41);
        negotiator.ValidateDialogueBinding(FinTsProtocolVersion.FinTs30, FinTsProtocolVersion.FinTs30);
        checks += 2;

        // 14. ValidateDialogueBinding throws FinTsProtocolMismatchException on mismatch
        bool threwMismatch = false;
        try
        {
            negotiator.ValidateDialogueBinding(FinTsProtocolVersion.FinTs41, FinTsProtocolVersion.FinTs30);
        }
        catch (FinTsProtocolMismatchException ex)
        {
            threwMismatch = true;
            Assert(ex.ExpectedVersion == FinTsProtocolVersion.FinTs41, "ExpectedVersion must be 4.1");
            Assert(ex.ActualVersion == FinTsProtocolVersion.FinTs30, "ActualVersion must be 3.0");
            Assert(ex.Message.Contains("Protocol version mutation during dialogue is forbidden"), "Must assert mutation rejection");
        }
        Assert(threwMismatch, "Version mismatch during dialogue must throw FinTsProtocolMismatchException");
        checks += 4;

        Console.WriteLine($"FinTS Protocol Negotiation (M9-01): {checks} checks completed successfully.");
    }

    private static void Assert(bool condition, string message)
    {
        if (!condition)
        {
            throw new InvalidOperationException($"Assertion failed: {message}");
        }
    }
}
