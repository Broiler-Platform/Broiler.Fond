using Broiler.Fond.Kernel.Storage;
using Broiler.Fond.Presentation.Services;

namespace Broiler.Fond.Presentation.Sessions;

public partial class HostSession
{
    internal Broiler.Fond.Kernel.FinTs.Transport.IFinTsTransport CreateBankTransport(ConnectionSetupContext context,
        HttpMessageHandler? handler = null, TimeSpan? timeout = null)
    {
        lock (_syncRoot)
        {
            _ = RequireCurrentSetup(context);
            return new ConfirmedBankTransport(context, ProductConfiguration, () =>
            {
                lock (_syncRoot)
                {
                    try { _ = RequireCurrentSetup(context); return true; }
                    catch (InvalidOperationException) { return false; }
                }
            }, handler, timeout);
        }
    }
    public ConnectionSetupContext GetConnectionSetupContext(ulong connectionId)
    {
        lock (_syncRoot)
        {
            StoreSnapshot snapshot = RequireSetupSnapshot();
            var connection = snapshot.Connections.Connections.FirstOrDefault(c => c.Id == connectionId)
                ?? throw new InvalidOperationException("The selected connection no longer exists.");
            var institution = snapshot.Institutions.Institutions.First(i => i.Id == connection.InstitutionId);
            return new(snapshot.Profile.ProfileId, snapshot.Profile.BranchId, _refreshEpoch, snapshot.Profile.Generation, connection, institution);
        }
    }

    public void UpdateConnectionSetup(ConnectionSetupContext context, InstitutionSetupResult input)
    {
        if (!input.Succeeded || input.Cancelled) throw new ArgumentException("Connection setup was not accepted.");
        var configuration = BankConnectionSetup.Validate(input.DisplayName, input.EndpointUri, input.BankCode, input.UserId, input.CustomerId);
        lock (_syncRoot)
        {
            StoreSnapshot snapshot = RequireCurrentSetup(context);
            bool endpointChanged = configuration.FinTsEndpoint.AbsoluteUri != context.Institution.Endpoint;
            bool loginChanged = input.UserId != context.Connection.UserId || input.CustomerId != context.Connection.CustomerId;
            bool bankCodeChanged = input.BankCode != context.Institution.BankCode;
            var institution = context.Institution with
            {
                DisplayName = configuration.DisplayName,
                BankCode = input.BankCode,
                Endpoint = configuration.FinTsEndpoint.AbsoluteUri,
                VerificationState = endpointChanged ? "Quarantined" : context.Institution.VerificationState == "Confirmed" ? "Confirmed" : "Unconfirmed"
            };
            var affected = snapshot.Connections.Connections.Where(c => c.InstitutionId == institution.Id &&
                (endpointChanged || bankCodeChanged || c.Id == context.Connection.Id &&
                    (loginChanged || c.SystemStatus is not ("Required" or "NotRequired") ||
                     !Enum.TryParse<Broiler.Fond.Kernel.Domain.Connections.BankConnectionState>(c.State, out var parsedState) || !Enum.IsDefined(parsedState))))
                .Select(c => c.Id).ToHashSet();
            var connections = snapshot.Connections.Connections.Select(c =>
            {
                var updated = c.Id == context.Connection.Id ? c with { UserId = input.UserId, CustomerId = input.CustomerId } : c;
                return affected.Contains(c.Id)
                    ? updated with
                    {
                        State = "Unconfigured",
                        SystemId = null,
                        SystemStatus = "Required",
                        IsSynchronized = false,
                        NextClientMessageNumber = 1,
                        LastSuccessfulContact = null,
                        LastAttemptedContact = null
                    }
                    : updated;
            }).ToArray();
            _profileStore!.Save(snapshot with
            {
                Institutions = snapshot.Institutions with { Institutions = snapshot.Institutions.Institutions.Select(i => i.Id == institution.Id ? institution : i).ToArray() },
                Connections = snapshot.Connections with { Connections = connections },
                Accounts = snapshot.Accounts with
                {
                    Bindings = snapshot.Accounts.Bindings.Select(b => affected.Contains(b.ConnectionId) ? b with { State = "Quarantined" } : b).ToArray(),
                    Capabilities = snapshot.Accounts.Capabilities.Where(c => !affected.Contains(c.ConnectionId)).ToArray()
                }
            });
            ReloadDataInternal();
        }
        OnStateChanged();
    }

    public void ConfirmConnectionEndpoint(ConnectionSetupContext review, string reviewedEndpoint)
    {
        lock (_syncRoot)
        {
            StoreSnapshot snapshot = RequireCurrentSetup(review);
            var configuration = BankConnectionSetup.Validate(review.Institution.DisplayName, review.Institution.Endpoint,
                review.Institution.BankCode, review.Connection.UserId, review.Connection.CustomerId);
            _ = configuration.ConfirmEndpoint(reviewedEndpoint);
            _profileStore!.Save(snapshot with
            {
                Institutions = snapshot.Institutions with
                {
                    Institutions = snapshot.Institutions.Institutions.Select(i => i.Id == review.Institution.Id ? i with { VerificationState = "Confirmed" } : i).ToArray()
                }
            });
            ReloadDataInternal();
        }
        OnStateChanged();
    }

    private StoreSnapshot RequireSetupSnapshot()
    {
        if (_refreshDisposed || _isSafeModeActive || _profileStore is null || _profileStore.IsReadOnly || _profileStore.CurrentSnapshot is not StoreSnapshot snapshot)
            throw new InvalidOperationException("Unlock a writable profile outside Safe Mode before editing bank setup.");
        return snapshot;
    }

    private StoreSnapshot RequireCurrentSetup(ConnectionSetupContext context)
    {
        StoreSnapshot snapshot = RequireSetupSnapshot();
        if (_refreshEpoch != context.SessionEpoch || snapshot.Profile.ProfileId != context.ProfileId || snapshot.Profile.BranchId != context.BranchId ||
            snapshot.Profile.Generation != context.Revision || !snapshot.Connections.Connections.Contains(context.Connection) ||
            !snapshot.Institutions.Institutions.Contains(context.Institution))
            throw new InvalidOperationException("Bank setup changed while the dialog was open. Review the current settings again.");
        return snapshot;
    }
}
