namespace Broiler.Fond.Kernel.Domain.Institutions;

/// <summary>
/// Validated manual setup. Confirmation records endpoint review; it does not
/// establish bank ownership, authenticate a session, or grant capabilities.
/// </summary>
public sealed record ManualInstitutionConfiguration
{
    /// <summary>Maximum endpoint input length, before URI parsing.</summary>
    public const int MaximumEndpointLength = 2048;

    private ManualInstitutionConfiguration(string displayName, Uri finTsEndpoint,
        string? bankCode, string? bic, EndpointVerificationState verificationState)
    {
        DisplayName = displayName;
        FinTsEndpoint = finTsEndpoint;
        BankCode = bankCode;
        Bic = bic;
        VerificationState = verificationState;
    }

    public string DisplayName { get; }

    public Uri FinTsEndpoint { get; }

    /// <summary>The ASCII hostname to display separately from the institution label.</summary>
    public string EndpointHost => FinTsEndpoint.IdnHost;

    public string? BankCode { get; }

    public string? Bic { get; }

    public EndpointVerificationState VerificationState { get; }

    /// <summary>
    /// Whether this exact endpoint has been reviewed. A future connector must
    /// also require fresh authentication and ordinary TLS checks.
    /// </summary>
    public bool IsEndpointConfirmed => VerificationState == EndpointVerificationState.Confirmed;

    /// <summary>
    /// Creates an unconfirmed setup. Bank-specific identifier requirements are
    /// checked later by the connector; supplied identifiers are retained verbatim.
    /// </summary>
    public static ManualInstitutionConfiguration Create(string displayName,
        string finTsEndpoint, string? bankCode = null, string? bic = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(displayName);
        return new(displayName, ParseEndpoint(finTsEndpoint), bankCode, bic,
            EndpointVerificationState.Unconfirmed);
    }

    /// <summary>
    /// Records explicit review of the complete displayed AbsoluteUri, including
    /// path, port, and query. A stale review cannot confirm a changed endpoint.
    /// </summary>
    public ManualInstitutionConfiguration ConfirmEndpoint(string reviewedEndpoint)
    {
        if (!string.Equals(reviewedEndpoint, FinTsEndpoint.AbsoluteUri, StringComparison.Ordinal))
        {
            throw new ArgumentException("The reviewed endpoint must match the current displayed endpoint.", nameof(reviewedEndpoint));
        }

        return new(DisplayName, FinTsEndpoint, BankCode, Bic, EndpointVerificationState.Confirmed);
    }

    /// <summary>
    /// Any effective endpoint change quarantines the setup. Reconfirmation does
    /// not restore an old authenticated session; reauthentication is required.
    /// </summary>
    public ManualInstitutionConfiguration ChangeEndpoint(string finTsEndpoint)
    {
        Uri endpoint = ParseEndpoint(finTsEndpoint);
        return string.Equals(endpoint.AbsoluteUri, FinTsEndpoint.AbsoluteUri, StringComparison.Ordinal)
            ? this
            : new(DisplayName, endpoint, BankCode, Bic, EndpointVerificationState.Quarantined);
    }

    private static Uri ParseEndpoint(string endpoint)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(endpoint);

        // Reject characters URI parsing might silently strip or reinterpret.
        if (endpoint.Length > MaximumEndpointLength ||
            endpoint.Any(static character => char.IsWhiteSpace(character) || char.IsControl(character) || character == '\\') ||
            !Uri.TryCreate(endpoint, UriKind.Absolute, out Uri? uri) ||
            uri.Scheme != Uri.UriSchemeHttps ||
            string.IsNullOrEmpty(uri.Host) ||
            !string.IsNullOrEmpty(uri.UserInfo) ||
            uri.GetLeftPart(UriPartial.Authority).Contains('@', StringComparison.Ordinal) ||
            !string.IsNullOrEmpty(uri.Fragment))
        {
            // Do not echo arbitrary endpoint input into an exception or diagnostic.
            throw new ArgumentException("A bounded absolute HTTPS endpoint without user information, fragments, whitespace, or backslashes is required.", nameof(endpoint));
        }

        return uri;
    }
}
