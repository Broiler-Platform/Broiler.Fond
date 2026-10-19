namespace Broiler.Fond.Kernel.Domain.Connections.Compatibility;

/// <summary>
/// Registry of built-in FinTS connector compatibility profiles shipped inside the binary.
/// Zero remote control channel: profiles are strictly local and immutable.
/// </summary>
public interface IConnectorCompatibilityRegistry
{
    /// <summary>All built-in compatibility profiles supported by this software version.</summary>
    IReadOnlyList<ConnectorCompatibilityProfile> BuiltInProfiles { get; }

    /// <summary>The standard unadjusted profile.</summary>
    ConnectorCompatibilityProfile StandardProfile { get; }

    /// <summary>
    /// Resolves the best-matching compatibility profile based on institution indicators.
    /// Returns StandardProfile if no specific institution match is found.
    /// </summary>
    ConnectorCompatibilityProfile ResolveProfile(string? bankCode, string? bic, string? endpointUri);

    /// <summary>
    /// Resolves effective compatibility after applying user workaround overrides.
    /// </summary>
    ResolvedConnectorCompatibility ResolveEffective(
        string? bankCode,
        string? bic,
        string? endpointUri,
        CompatibilityWorkaroundOverride? overrides = null);

    /// <summary>
    /// Resolves effective compatibility for a specific bank connection.
    /// </summary>
    ResolvedConnectorCompatibility ResolveForConnection(
        BankConnection connection,
        CompatibilityWorkaroundOverride? overrides = null);
}
