using Broiler.Fond.Kernel.Domain.Connections;
using Broiler.Fond.Kernel.Domain.Identifiers;
using Broiler.Fond.Kernel.Domain.Institutions;
using Broiler.Fond.Kernel.FinTs;
using Broiler.Fond.Kernel.Storage;

namespace Broiler.Fond.Presentation.Services;

/// <summary>A review token includes the persisted generation to reject edit/revert and stale modal reviews.</summary>
public sealed record ConnectionSetupContext(Guid ProfileId, Guid BranchId, long SessionEpoch, ulong Revision,
    StoreConnectionEntry Connection, StoreInstitutionEntry Institution);

internal static class BankConnectionSetup
{
    internal static ManualInstitutionConfiguration Validate(string name, string endpoint, string? bankCode, string userId, string? customerId, bool requireLogin = true)
    {
        var configuration = ManualInstitutionConfiguration.Create(name, endpoint, bankCode);
        if (requireLogin || !string.IsNullOrWhiteSpace(userId))
        {
            if (userId == "default-user") throw new ArgumentException("Enter your bank user ID, not the old default placeholder.");
            _ = BankConnection.Create(new ConnectionId(2), new InstitutionId(1), configuration, userId, customerId);
        }
        return configuration;
    }

    internal static StoreSnapshot NormalizeLegacy(StoreSnapshot snapshot)
    {
        var institutions = snapshot.Institutions.Institutions.Select(i => i.VerificationState == "Verified" ? i with { VerificationState = "Unconfirmed" } : i).ToArray();
        var connections = snapshot.Connections.Connections.Select(c => c.SystemStatus == "Active"
            ? c with
            {
                SystemStatus = "Required",
                State = "Unconfigured",
                SystemId = null,
                IsSynchronized = false,
                LastSuccessfulContact = null,
                LastAttemptedContact = null,
                UserId = c.UserId == "default-user" ? "" : c.UserId
            }
            : c.State is "Configured" or "Synchronized" or "Quarantined" ? c with
            {
                State = c.State switch
                {
                    "Configured" => "Unconfigured",
                    "Quarantined" => "ReauthenticationRequired",
                    _ => c.IsSynchronized && c.LastSuccessfulContact.HasValue && !string.IsNullOrEmpty(c.SystemId) && c.SystemId != "0" ? "Connected" : "Unconfigured"
                }
            } : c).ToArray();
        return institutions.SequenceEqual(snapshot.Institutions.Institutions) && connections.SequenceEqual(snapshot.Connections.Connections)
            ? snapshot : snapshot with
            {
                Institutions = snapshot.Institutions with { Institutions = institutions },
                Connections = snapshot.Connections with { Connections = connections }
            };
    }

    internal static BankConnection ToDomain(StoreConnectionEntry connection, StoreInstitutionEntry institution)
    {
        // Unknown legacy values fail closed rather than being parsed as default enum values.
        if (connection.SystemStatus is not ("Required" or "NotRequired") ||
            connection.State is not ("Unconfigured" or "Authenticating" or "ChallengeRequired" or "Connected" or "Synchronized" or
                "ReauthenticationRequired" or "TemporarilyUnavailable" or "Quarantined" or "Configured" or "Disabled") ||
            institution.VerificationState is not ("Unconfirmed" or "Confirmed" or "Quarantined"))
            throw new ArgumentException("Saved bank configuration requires review. Edit the connection and confirm its endpoint.");
        var configuration = Validate(institution.DisplayName, institution.Endpoint, institution.BankCode, connection.UserId, connection.CustomerId);
        if (institution.VerificationState == "Confirmed") configuration = configuration.ConfirmEndpoint(configuration.FinTsEndpoint.AbsoluteUri);
        var result = BankConnection.Create(new ConnectionId(connection.Id), new InstitutionId(institution.Id), configuration,
            connection.UserId, connection.CustomerId, systemStatus: connection.SystemStatus == "Required" ? FinTsCustomerSystemStatus.Required : FinTsCustomerSystemStatus.NotRequired,
            connectorVersion: connection.ConnectorVersion);
        if (connection.IsSynchronized && connection.SystemId is not null && connection.LastSuccessfulContact is DateTimeOffset contact)
            result = result.WithSynchronization(connection.SystemId, connection.NextClientMessageNumber, contact);
        BankConnectionState state = connection.State switch
        {
            "Synchronized" => connection.IsSynchronized && connection.LastSuccessfulContact.HasValue ? BankConnectionState.Connected : BankConnectionState.Unconfigured,
            "Configured" => BankConnectionState.Unconfigured,
            "Quarantined" => BankConnectionState.ReauthenticationRequired,
            _ => Enum.Parse<BankConnectionState>(connection.State)
        };
        return result.WithState(state, connection.LastAttemptedContact);
    }
}
