using Broiler.Fond.Kernel.Storage;
using Broiler.Fond.Presentation.Mvvm;

namespace Broiler.Fond.Presentation.ViewModels;

/// <summary>
/// Observable view model representing a configured bank connection and its operational endpoint state.
/// </summary>
public sealed class ConnectionViewModel : ObservableObject
{
    public ulong ConnectionId { get; }
    public ulong InstitutionId { get; }
    public string InstitutionName { get; }
    public string BankCode { get; }
    public string UserId { get; }
    public string? CustomerId { get; }
    public string CustomerIdDisplay { get; }

    public Uri EndpointUri { get; }
    public string EndpointUriDisplay { get; }
    public string EndpointHost { get; }
    public bool IsEndpointConfirmed { get; }

    public string State { get; }
    public string StateDisplay { get; }
    public bool CanRefresh { get; }
    public bool RequiresReconfirmation { get; }
    public bool CanReauthenticate { get; }
    public bool CanEditEndpoint => true;

    public string SecurityProfileDisplay { get; }
    public string ProceduresCountDisplay { get; }

    public ConnectionViewModel(StoreConnectionEntry connection, StoreInstitutionEntry? institution = null)
    {
        ArgumentNullException.ThrowIfNull(connection);

        ConnectionId = connection.Id;
        InstitutionId = connection.InstitutionId;
        InstitutionName = string.IsNullOrWhiteSpace(institution?.DisplayName) ? $"Institution #{connection.InstitutionId}" : institution.DisplayName;
        BankCode = institution?.BankCode ?? "[Unknown]";
        UserId = connection.UserId;
        CustomerId = connection.CustomerId;
        CustomerIdDisplay = string.IsNullOrWhiteSpace(connection.CustomerId) ? "[Same as User ID]" : connection.CustomerId;

        string ep = institution?.Endpoint ?? "https://localhost";
        if (Uri.TryCreate(ep, UriKind.Absolute, out Uri? uri))
        {
            EndpointUri = uri;
            EndpointUriDisplay = uri.ToString();
            EndpointHost = uri.IdnHost;
        }
        else
        {
            EndpointUri = new Uri("https://localhost");
            EndpointUriDisplay = ep;
            EndpointHost = ep;
        }

        bool isConfirmed = string.Equals(institution?.VerificationState, "Confirmed", StringComparison.OrdinalIgnoreCase);
        IsEndpointConfirmed = isConfirmed;

        State = connection.State;
        RequiresReconfirmation = !isConfirmed || string.Equals(connection.State, "Quarantined", StringComparison.OrdinalIgnoreCase);
        CanRefresh = (string.Equals(connection.State, "Connected", StringComparison.OrdinalIgnoreCase) ||
                      string.Equals(connection.State, "Synchronized", StringComparison.OrdinalIgnoreCase)) &&
                     isConfirmed;
        CanReauthenticate = string.Equals(connection.State, "ReauthenticationRequired", StringComparison.OrdinalIgnoreCase) ||
                            string.Equals(connection.State, "Configured", StringComparison.OrdinalIgnoreCase) ||
                            string.Equals(connection.State, "TemporarilyUnavailable", StringComparison.OrdinalIgnoreCase);

        StateDisplay = connection.State switch
        {
            "Synchronized" => isConfirmed
                ? "[✓ Synchronized]"
                : "[⚠ Synchronized (Unconfirmed Host)]",
            "Connected" => isConfirmed
                ? "[✓ Connected]"
                : "[⚠ Connected (Unconfirmed Host)]",
            "ReauthenticationRequired" => "[⚠ Reauthentication Required]",
            "TemporarilyUnavailable" => "[⏳ Temporarily Unavailable]",
            "ChallengeRequired" => "[🔐 Challenge Required]",
            "Authenticating" => "[🔄 Authenticating]",
            "Configured" or "Unconfigured" => "[⚪ Configured - Not Synchronized]",
            "Quarantined" => "[⚠ Quarantined - Reconfirmation Required]",
            "Disabled" => "[✕ Disabled]",
            _ => $"[{connection.State}]"
        };

        SecurityProfileDisplay = $"FinTS 3.0 PIN/TAN ({connection.ConnectorVersion})";
        ProceduresCountDisplay = connection.IsSynchronized ? "Synchronized" : "Pending Synchronization";
    }
}
