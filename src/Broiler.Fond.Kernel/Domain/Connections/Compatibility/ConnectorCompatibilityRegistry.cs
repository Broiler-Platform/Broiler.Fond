namespace Broiler.Fond.Kernel.Domain.Connections.Compatibility;

/// <summary>
/// Built-in implementation of connector compatibility registry.
/// Encapsulates institution quirks inside signed binaries with zero remote fetch.
/// </summary>
public sealed class ConnectorCompatibilityRegistry : IConnectorCompatibilityRegistry
{
    private static readonly ConnectorCompatibilityProfile SparkasseProfile = new()
    {
        ProfileId = "fints-sparkasse",
        Version = 1,
        DisplayName = "Sparkassen-Finanzgruppe (Finanz Informatik)",
        Description = "Finanz Informatik compatibility profile. Pins HKTAN to version 6 and prefers CAMT over MT940.",
        BankCodePrefixes = ["10050", "20050", "30050", "40050", "50050", "60050", "70050", "80050"],
        BicPatterns = ["SPK", "WELADED1"],
        HostnamePatterns = ["sparkasse.de", "fints.sparkasse"],
        PinnedHktanVersion = 6,
        PreferCamtOverMt940 = true,
    };

    private static readonly ConnectorCompatibilityProfile AtruviaProfile = new()
    {
        ProfileId = "fints-atruvia",
        Version = 1,
        DisplayName = "Genossenschaftliche FinanzGruppe (Atruvia)",
        Description = "Atruvia compatibility profile. Clamps batch page sizes and statement queries to 30 to prevent 9800 dialogue aborts.",
        BicPatterns = ["GENODED1"],
        HostnamePatterns = ["fints.atruvia.de", "vr.de", "volksbank"],
        MaximumBatchPageSize = 30,
        MaximumStatementQueryDays = 30,
        PreferCamtOverMt940 = true,
    };

    private static readonly ConnectorCompatibilityProfile PostbankProfile = new()
    {
        ProfileId = "fints-postbank",
        Version = 1,
        DisplayName = "Postbank (Deutsche Bank Retail)",
        Description = "Postbank compatibility profile. Prefers ISO 20022 CAMT and pins HKTAN to v6.",
        BankCodePrefixes = ["37010050", "10010010", "20010020", "50010060"],
        BicPatterns = ["PBNKDEFF"],
        HostnamePatterns = ["postbank.de"],
        PinnedHktanVersion = 6,
        PreferCamtOverMt940 = true,
    };

    private static readonly ConnectorCompatibilityProfile IngProfile = new()
    {
        ProfileId = "fints-ing",
        Version = 1,
        DisplayName = "ING Germany",
        Description = "ING Germany compatibility profile. Omits customer ID in signature header.",
        BankCodePrefixes = ["50010517"],
        BicPatterns = ["INGDDEFF"],
        HostnamePatterns = ["fints.ing.de", "ing-diba.de"],
        OmitCustomerIdInSignatureHeader = true,
    };

    private static readonly ConnectorCompatibilityProfile CommerzbankProfile = new()
    {
        ProfileId = "fints-commerzbank",
        Version = 1,
        DisplayName = "Commerzbank",
        Description = "Commerzbank compatibility profile. Restricts statement query windows to 90 days and prefers CAMT.",
        BankCodePrefixes = ["10040000", "20040000", "30040000", "40040000", "50040000", "60040000", "70040000", "80040000"],
        BicPatterns = ["COBADEFF"],
        HostnamePatterns = ["commerzbank.de"],
        MaximumStatementQueryDays = 90,
        PreferCamtOverMt940 = true,
    };

    private static readonly ConnectorCompatibilityProfile DeutscheBankProfile = new()
    {
        ProfileId = "fints-deutschebank",
        Version = 1,
        DisplayName = "Deutsche Bank Core",
        Description = "Deutsche Bank core compatibility profile. Requires ISO-8859 encoding and pins HKSAL to v7.",
        BankCodePrefixes = ["10070000", "20070000", "30070000", "40070000", "50070000", "60070000", "70070000", "80070000"],
        BicPatterns = ["DEUTDEDD"],
        HostnamePatterns = ["deutsche-bank.de"],
        RequireIso8859Encoding = true,
        PinnedHksalVersion = 7,
    };

    private static readonly IReadOnlyList<ConnectorCompatibilityProfile> AllProfiles = new[]
    {
        ConnectorCompatibilityProfile.Standard,
        SparkasseProfile,
        AtruviaProfile,
        PostbankProfile,
        IngProfile,
        CommerzbankProfile,
        DeutscheBankProfile,
    };

    public static readonly ConnectorCompatibilityRegistry Default = new();

    public IReadOnlyList<ConnectorCompatibilityProfile> BuiltInProfiles => AllProfiles;

    public ConnectorCompatibilityProfile StandardProfile => ConnectorCompatibilityProfile.Standard;

    public ConnectorCompatibilityProfile ResolveProfile(string? bankCode, string? bic, string? endpointUri)
    {
        // Check specific profiles in deterministic priority order (excluding standard default)
        for (int i = 1; i < AllProfiles.Count; i++)
        {
            var profile = AllProfiles[i];
            if (profile.Matches(bankCode, bic, endpointUri))
            {
                return profile;
            }
        }

        return StandardProfile;
    }

    public ResolvedConnectorCompatibility ResolveEffective(
        string? bankCode,
        string? bic,
        string? endpointUri,
        CompatibilityWorkaroundOverride? overrides = null)
    {
        var profile = ResolveProfile(bankCode, bic, endpointUri);
        return ResolvedConnectorCompatibility.Create(profile, overrides);
    }

    public ResolvedConnectorCompatibility ResolveForConnection(
        BankConnection connection,
        CompatibilityWorkaroundOverride? overrides = null)
    {
        ArgumentNullException.ThrowIfNull(connection);

        string? bankCode = connection.InstitutionConfiguration.BankCode;
        string? bic = connection.InstitutionConfiguration.Bic;
        string? endpoint = connection.InstitutionConfiguration.FinTsEndpoint?.ToString();

        return ResolveEffective(bankCode, bic, endpoint, overrides);
    }
}
