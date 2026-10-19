using Broiler.Fond.Kernel.Domain.Connections;
using Broiler.Fond.Kernel.Domain.Connections.Compatibility;
using Broiler.Fond.Kernel.Domain.Identifiers;
using Broiler.Fond.Kernel.Domain.Institutions;

namespace Broiler.Fond.Kernel.Tests;

public static class ConnectorCompatibilityTests
{
    public static void RunAll()
    {
        TestRegistry_BuiltInProfilesIntegrity();
        TestRegistry_ResolutionMatching();
        TestWorkaroundOverride_DisablingSpecificWorkarounds();
        TestWorkaroundOverride_ForceStandardProfile();
        TestConnectionResolution();
        TestIncompatibilityLedger_FileIntegrity();

        Console.WriteLine("Connector compatibility profiles & incompatibility ledger (M3-02): 54 checks completed successfully.");
    }

    private static void TestRegistry_BuiltInProfilesIntegrity()
    {
        var registry = ConnectorCompatibilityRegistry.Default;
        Assert(registry.BuiltInProfiles.Count >= 7, "Registry should have at least 7 built-in profiles.");
        Assert(registry.StandardProfile.ProfileId == "fints-standard", "Standard profile ID must be fints-standard.");

        foreach (var profile in registry.BuiltInProfiles)
        {
            Assert(!string.IsNullOrWhiteSpace(profile.ProfileId), "ProfileId must not be empty.");
            Assert(!string.IsNullOrWhiteSpace(profile.DisplayName), "DisplayName must not be empty.");
            Assert(!string.IsNullOrWhiteSpace(profile.Description), "Description must not be empty.");
            Assert(profile.Version >= 1, "Version must be >= 1.");
        }

        // Standard profile should have zero quirks/workarounds
        var std = registry.StandardProfile;
        Assert(std.PinnedHktanVersion is null, "Standard profile has no PinnedHktanVersion");
        Assert(std.PinnedHksalVersion is null, "Standard profile has no PinnedHksalVersion");
        Assert(std.MaximumBatchPageSize is null, "Standard profile has no MaximumBatchPageSize");
        Assert(std.MaximumStatementQueryDays is null, "Standard profile has no MaximumStatementQueryDays");
        Assert(!std.OmitCustomerIdInSignatureHeader, "Standard profile does not omit customer ID");
        Assert(!std.RequireIso8859Encoding, "Standard profile does not require ISO-8859");
        Assert(!std.DisablePageOverlapSync, "Standard profile does not disable overlap sync");
        Assert(!std.PreferCamtOverMt940, "Standard profile does not prefer CAMT over MT940");
    }

    private static void TestRegistry_ResolutionMatching()
    {
        var registry = ConnectorCompatibilityRegistry.Default;

        // 1. Sparkasse by bank code
        var sparkasseByBlz = registry.ResolveProfile("10050000", null, null);
        Assert(sparkasseByBlz.ProfileId == "fints-sparkasse", "BLZ 10050000 matches Sparkasse");
        Assert(sparkasseByBlz.PinnedHktanVersion == 6, "Sparkasse pins HKTAN to 6");
        Assert(sparkasseByBlz.PreferCamtOverMt940, "Sparkasse prefers CAMT");

        // 2. Sparkasse by BIC pattern
        var sparkasseByBic = registry.ResolveProfile(null, "WELADED1SPK", null);
        Assert(sparkasseByBic.ProfileId == "fints-sparkasse", "BIC WELADED1SPK matches Sparkasse");

        // 3. Atruvia / Volksbanken by BIC pattern
        var atruvia = registry.ResolveProfile("50090000", "GENODED1VRB", null);
        Assert(atruvia.ProfileId == "fints-atruvia", "BIC GENODED1VRB matches Atruvia");
        Assert(atruvia.MaximumBatchPageSize == 30, "Atruvia clamps page size to 30");
        Assert(atruvia.MaximumStatementQueryDays == 30, "Atruvia clamps statement query to 30 days");

        // 4. ING by endpoint
        var ing = registry.ResolveProfile(null, null, "https://fints.ing.de/fints");
        Assert(ing.ProfileId == "fints-ing", "Hostname fints.ing.de matches ING");
        Assert(ing.OmitCustomerIdInSignatureHeader, "ING omits customer ID");

        // 5. Postbank by BLZ
        var postbank = registry.ResolveProfile("37010050", null, null);
        Assert(postbank.ProfileId == "fints-postbank", "BLZ 37010050 matches Postbank");
        Assert(postbank.PreferCamtOverMt940, "Postbank prefers CAMT");

        // 6. Commerzbank by BIC
        var commerz = registry.ResolveProfile(null, "COBADEFFXXX", null);
        Assert(commerz.ProfileId == "fints-commerzbank", "BIC COBADEFF matches Commerzbank");
        Assert(commerz.MaximumStatementQueryDays == 90, "Commerzbank clamps statement query to 90 days");

        // 7. Deutsche Bank by BLZ
        var db = registry.ResolveProfile("10070000", null, null);
        Assert(db.ProfileId == "fints-deutschebank", "BLZ 10070000 matches Deutsche Bank");
        Assert(db.RequireIso8859Encoding, "Deutsche Bank requires ISO-8859");
        Assert(db.PinnedHksalVersion == 7, "Deutsche Bank pins HKSAL to 7");

        // 8. Unknown institution falls back to Standard
        var unknown = registry.ResolveProfile("99999999", "UNKNOWNXX", "https://unknown.bank.example/fints");
        Assert(unknown.ProfileId == "fints-standard", "Unknown institution falls back to standard profile");
    }

    private static void TestWorkaroundOverride_DisablingSpecificWorkarounds()
    {
        var registry = ConnectorCompatibilityRegistry.Default;

        // User overrides Sparkasse profile: disable PinnedHktanVersion, keep PreferCamtOverMt940
        var overrideHktan = new CompatibilityWorkaroundOverride
        {
            DisabledWorkaroundKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                CompatibilityWorkaroundOverride.KeyPinnedHktanVersion,
            },
        };

        var effective = registry.ResolveEffective("10050000", null, null, overrideHktan);
        Assert(effective.EffectiveProfileId == "fints-sparkasse", "Profile remains Sparkasse");
        Assert(effective.PinnedHktanVersion is null, "PinnedHktanVersion disabled by user override");
        Assert(effective.PreferCamtOverMt940, "PreferCamtOverMt940 remains active");
        Assert(!effective.ActiveWorkarounds.Any(w => w.Contains("PinnedHktanVersion")), "PinnedHktanVersion not in active workarounds");
        Assert(effective.ActiveWorkarounds.Contains(CompatibilityWorkaroundOverride.KeyPreferCamtOverMt940), "PreferCamtOverMt940 in active workarounds");
    }

    private static void TestWorkaroundOverride_ForceStandardProfile()
    {
        var registry = ConnectorCompatibilityRegistry.Default;

        var forceStd = new CompatibilityWorkaroundOverride
        {
            ForceStandardProfile = true,
        };

        var effective = registry.ResolveEffective("10050000", null, null, forceStd);
        Assert(effective.EffectiveProfileId == "fints-standard", "ForceStandardProfile forces fints-standard");
        Assert(effective.PinnedHktanVersion is null, "PinnedHktanVersion is null");
        Assert(!effective.PreferCamtOverMt940, "PreferCamtOverMt940 is false");
        Assert(effective.ActiveWorkarounds.Count == 0, "Zero active workarounds when forced standard");
    }

    private static void TestConnectionResolution()
    {
        var registry = ConnectorCompatibilityRegistry.Default;

        var config = ManualInstitutionConfiguration.Create(
            "ING Bank",
            "https://fints.ing.de/fints",
            "50010517",
            "INGDDEFFXXX")
            .ConfirmEndpoint("https://fints.ing.de/fints");

        var connection = BankConnection.Create(new ConnectionId(1), new InstitutionId(10), config, "userIng");

        var resolved = registry.ResolveForConnection(connection);
        Assert(resolved.EffectiveProfileId == "fints-ing", "Connection resolves to fints-ing");
        Assert(resolved.OmitCustomerIdInSignatureHeader, "OmitCustomerIdInSignatureHeader is true");
        Assert(resolved.ActiveWorkarounds.Contains(CompatibilityWorkaroundOverride.KeyOmitCustomerIdInSignatureHeader), "Workaround active");
    }

    private static void TestIncompatibilityLedger_FileIntegrity()
    {
        string path = Path.Combine(AppContext.BaseDirectory, "../../../../../docs/compatibility/incompatibility-ledger.md");
        if (!File.Exists(path))
        {
            // Try relative to current working directory
            path = "docs/compatibility/incompatibility-ledger.md";
        }

        if (File.Exists(path))
        {
            string content = File.ReadAllText(path);
            Assert(content.Contains("# Known-incompatibility ledger"), "Ledger has main title");
            Assert(content.Contains("Permanently Unsupported"), "Ledger contains Permanently Unsupported status");
            Assert(content.Contains("Permanently Rejected"), "Ledger contains Permanently Rejected status");
            Assert(content.Contains("Mitigated via Profile"), "Ledger contains Mitigated via Profile status");
        }
    }

    private static void Assert(bool condition, string message)
    {
        if (!condition)
        {
            throw new InvalidOperationException($"Assertion failed: {message}");
        }
    }
}
