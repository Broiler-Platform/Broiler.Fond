using Broiler.Fond.Kernel.Domain.Institutions;

namespace Broiler.Fond.Presentation.Models;

/// <summary>
/// Immutable, validated HTTPS FinTS endpoint representation fulfilling M1-06 manual entry requirements.
/// </summary>
public sealed record FinTsEndpoint(Uri Uri, bool IsConfirmed = false)
{
    public string IdnHost => Uri.IdnHost;

    public static FinTsEndpoint Create(string uriString)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(uriString);
        if (uriString.Length > ManualInstitutionConfiguration.MaximumEndpointLength ||
            uriString.Any(static c => char.IsWhiteSpace(c) || char.IsControl(c) || c == '\\') ||
            !Uri.TryCreate(uriString, UriKind.Absolute, out Uri? uri) ||
            uri.Scheme != Uri.UriSchemeHttps ||
            string.IsNullOrEmpty(uri.Host) ||
            !string.IsNullOrEmpty(uri.UserInfo) ||
            uri.GetLeftPart(UriPartial.Authority).Contains('@', StringComparison.Ordinal) ||
            !string.IsNullOrEmpty(uri.Fragment))
        {
            throw new ArgumentException("A bounded absolute HTTPS endpoint without user information, fragments, whitespace, or backslashes is required.", nameof(uriString));
        }

        return new FinTsEndpoint(uri, IsConfirmed: false);
    }

    public FinTsEndpoint WithConfirmation(bool confirmed) => this with { IsConfirmed = confirmed };
}
