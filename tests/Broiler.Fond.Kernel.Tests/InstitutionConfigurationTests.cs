using Broiler.Fond.Kernel.Domain.Institutions;

namespace Broiler.Fond.Kernel.Tests;

internal static class InstitutionConfigurationTests
{
    internal static void Run(Action<bool, string> check)
    {
        const string originalEndpoint = "https://bank.example:8443/fints?version=3";
        ManualInstitutionConfiguration original = ManualInstitutionConfiguration.Create(
            "My bank", originalEndpoint, "00123456", "EXAMPLE1XXX");
        check(original.EndpointHost == "bank.example", "The endpoint hostname must be independent of the friendly label.");
        check(original.FinTsEndpoint.AbsoluteUri == originalEndpoint, "Endpoint path, port, and query must survive setup.");
        check(original.VerificationState == EndpointVerificationState.Unconfirmed && !original.IsEndpointConfirmed,
            "New endpoints must require explicit confirmation.");

        ManualInstitutionConfiguration confirmed = original.ConfirmEndpoint(originalEndpoint);
        check(confirmed.IsEndpointConfirmed && !original.IsEndpointConfirmed,
            "Confirmation must create a new immutable configuration.");
        check(ReferenceEquals(confirmed, confirmed.ChangeEndpoint(originalEndpoint)),
            "An unchanged endpoint must retain its confirmation.");

        string[] changedEndpoints =
        [
            "https://other.example:8443/fints?version=3",
            "https://bank.example:9443/fints?version=3",
            "https://bank.example:8443/other?version=3",
            "https://bank.example:8443/fints?version=4",
            "https://bank.example:8443/FinTs?version=3",
        ];
        foreach (string endpoint in changedEndpoints)
        {
            ManualInstitutionConfiguration changed = confirmed.ChangeEndpoint(endpoint);
            check(changed.VerificationState == EndpointVerificationState.Quarantined && !changed.IsEndpointConfirmed,
                "A hostname, port, path, case-sensitive path, or query change must quarantine the endpoint.");
            Reject(() => changed.ConfirmEndpoint(originalEndpoint), check, "A stale endpoint review must be rejected.");
            check(changed.ConfirmEndpoint(endpoint).IsEndpointConfirmed,
                "A quarantined endpoint must support deliberate reconfirmation.");
            check(changed.ChangeEndpoint(originalEndpoint).VerificationState == EndpointVerificationState.Quarantined,
                "Returning to an earlier endpoint must not revive its old confirmation.");
            check(changed.DisplayName == original.DisplayName && changed.BankCode == "00123456" && changed.Bic == "EXAMPLE1XXX",
                "Endpoint changes must preserve institution metadata and leading-zero identifiers.");
        }

        string[] invalidEndpoints =
        [
            "", " ", "/fints", "//bank.example/fints", "http://bank.example/fints",
            "file:///fints", "https:///fints", "https://", "https://user:secret@bank.example/fints",
            "https://user@bank.example/fints", "https://@bank.example/fints", "https://bank.example/fints#fragment",
            "https://bank.example/fints#", " https://bank.example/fints", "https://bank.example/fints ",
            "https://bank.example/fi\nnts", "https://bank.example/fi\tnts", "https://bank.example/fi\0nts",
            "https://bank.example\\fints", "https://bank.example:99999/fints",
            "https://bank.example/" + new string('a', ManualInstitutionConfiguration.MaximumEndpointLength),
        ];
        foreach (string endpoint in invalidEndpoints)
        {
            Reject(() => ManualInstitutionConfiguration.Create("Bank", endpoint), check, "An invalid endpoint was accepted at creation.");
            Reject(() => confirmed.ChangeEndpoint(endpoint), check, "An invalid endpoint was accepted during an edit.");
        }

        Reject(() => ManualInstitutionConfiguration.Create(" ", originalEndpoint), check, "An empty institution name must be rejected.");
        Reject(() => ManualInstitutionConfiguration.Create("Bank", null!), check, "A null endpoint must be rejected.");
        Reject(() => confirmed.ConfirmEndpoint(confirmed.EndpointHost), check, "Hostname alone cannot confirm the full endpoint.");
        Reject(() => confirmed.ConfirmEndpoint(null!), check, "A null review must be rejected.");

        ManualInstitutionConfiguration international = ManualInstitutionConfiguration.Create("Bank", "https://bücher.example/fints");
        check(international.EndpointHost == "xn--bcher-kva.example", "International hostnames must expose their ASCII form for review.");
        check(international.ConfirmEndpoint(international.FinTsEndpoint.AbsoluteUri).IsEndpointConfirmed,
            "International endpoints must support review of their displayed URI.");

        const string prefix = "https://bank.example/";
        string maximumEndpoint = prefix + new string('a', ManualInstitutionConfiguration.MaximumEndpointLength - prefix.Length);
        check(ManualInstitutionConfiguration.Create("Bank", maximumEndpoint).FinTsEndpoint.AbsoluteUri == maximumEndpoint,
            "The documented maximum endpoint length must be accepted.");
        foreach (string endpoint in new[] { "https://192.0.2.1/fints", "https://[2001:db8::1]/fints", "https://bank.example/a%20b?x=%23" })
        {
            check(!ManualInstitutionConfiguration.Create("Bank", endpoint).IsEndpointConfirmed,
                "Valid IP endpoints and escaped URL data must still require confirmation.");
        }

        const string sentinel = "SENTINEL-DO-NOT-LOG";
        try
        {
            _ = ManualInstitutionConfiguration.Create("Bank", $"https://user:{sentinel}@bank.example/fints");
            check(false, "Credential-bearing endpoints must be rejected.");
        }
        catch (ArgumentException exception)
        {
            check(!exception.ToString().Contains(sentinel, StringComparison.Ordinal),
                "Endpoint validation errors must not echo supplied secrets.");
        }
    }

    private static void Reject(Action action, Action<bool, string> check, string message)
    {
        try
        {
            action();
            check(false, message);
        }
        catch (ArgumentException)
        {
            // Expected fail-closed validation outcome.
        }
    }
}
