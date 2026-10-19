using System.Globalization;
using Broiler.Fond.Kernel.Domain.Accounts;
using Broiler.Fond.Kernel.Domain.Identifiers;
using Broiler.Fond.Kernel.FinTs;
using Broiler.Fond.Kernel.Storage;

namespace Broiler.Fond.Kernel.Domain.Connections.Orchestration;

/// <summary>
/// Pure helper methods for mutating <see cref="StoreSnapshot"/> documents during multibank synchronization.
/// Invariants:
/// - Pure immutable transformation.
/// - Preserves entity identity and cross-reference integrity.
/// - Never discards unaffected connections or accounts.
/// </summary>
public static class MultibankSnapshotMutator
{
    public static StoreSnapshot ApplyConnectionState(
        StoreSnapshot current,
        ConnectionId connectionId,
        BankConnectionState newState,
        DateTimeOffset timestamp)
    {
        ArgumentNullException.ThrowIfNull(current);

        var updatedConnections = new List<StoreConnectionEntry>(current.Connections.Connections.Count);
        bool found = false;

        foreach (var conn in current.Connections.Connections)
        {
            if (conn.Id == connectionId.Value)
            {
                found = true;
                updatedConnections.Add(conn with
                {
                    State = newState.ToString(),
                    LastAttemptedContact = timestamp,
                });
            }
            else
            {
                updatedConnections.Add(conn);
            }
        }

        if (!found)
        {
            return current;
        }

        var newConnDoc = new StoreConnectionsDocument(
            current.Profile.ProfileId,
            current.Profile.BranchId,
            current.Profile.Generation,
            updatedConnections.AsReadOnly());

        return current with { Connections = newConnDoc };
    }

    public static StoreSnapshot ApplyIngestionResult(
        StoreSnapshot current,
        BankConnection updatedConnection,
        FinTsReadIngestionResult? ingestionResult,
        DateTimeOffset timestamp)
    {
        ArgumentNullException.ThrowIfNull(current);
        ArgumentNullException.ThrowIfNull(updatedConnection);

        // 1. Update Connection
        var updatedConnections = new List<StoreConnectionEntry>(current.Connections.Connections.Count);
        bool found = false;

        foreach (var conn in current.Connections.Connections)
        {
            if (conn.Id == updatedConnection.Id.Value)
            {
                found = true;
                updatedConnections.Add(ToStoreEntry(updatedConnection));
            }
            else
            {
                updatedConnections.Add(conn);
            }
        }

        if (!found)
        {
            updatedConnections.Add(ToStoreEntry(updatedConnection));
        }

        var newConnDoc = new StoreConnectionsDocument(
            current.Profile.ProfileId,
            current.Profile.BranchId,
            current.Profile.Generation,
            updatedConnections.AsReadOnly());

        if (ingestionResult is null)
        {
            return current with { Connections = newConnDoc };
        }

        // 2. Update Accounts, Incarnations, Bindings
        var accList = new List<StoreAccountEntry>(current.Accounts.Accounts);
        var existingAccIds = new HashSet<ulong>(accList.Select(a => a.Id));

        var incList = new List<StoreIncarnationEntry>(current.Accounts.Incarnations);
        var existingIncIds = new HashSet<ulong>(incList.Select(i => i.Id));

        var bindList = new List<StoreBindingEntry>(current.Accounts.Bindings);
        var existingBindIds = new HashSet<ulong>(bindList.Select(b => b.Id));

        var capList = new List<StoreCapabilityEntry>(current.Accounts.Capabilities);

        foreach (var bound in ingestionResult.BoundAccounts)
        {
            var locator = bound.Locator;
            if (existingAccIds.Add(bound.AccountId.Value))
            {
                string displayName = !string.IsNullOrWhiteSpace(locator.Iban) ? $"Account {locator.Iban}" : "Account";
                accList.Add(new StoreAccountEntry(bound.AccountId.Value, displayName, locator.Currency ?? "EUR"));
            }

            if (existingIncIds.Add(bound.IncarnationId.Value))
            {
                incList.Add(new StoreIncarnationEntry(bound.IncarnationId.Value, bound.AccountId.Value, "LiveDialogue"));
            }

            if (existingBindIds.Add(bound.BindingId.Value))
            {
                var bankIdTuples = locator.BankIdentifiers
                    .Select(bi => (bi.Issuer, bi.Scope, bi.Kind, bi.Value))
                    .ToList();

                bindList.Add(new StoreBindingEntry(
                    bound.BindingId.Value,
                    bound.AccountId.Value,
                    bound.IncarnationId.Value,
                    locator.InstitutionId.Value,
                    updatedConnection.Id.Value,
                    bound.State.ToString(),
                    locator.Connector,
                    locator.ConnectorVersion,
                    locator.Endpoint,
                    locator.Iban,
                    locator.DomesticAccountNumber,
                    locator.DomesticBankCode,
                    locator.Subaccount,
                    locator.Currency,
                    bankIdTuples));
            }
        }

        var newAccDoc = new StoreAccountsDocument(
            current.Profile.ProfileId,
            current.Profile.BranchId,
            current.Profile.Generation,
            accList.AsReadOnly(),
            incList.AsReadOnly(),
            bindList.AsReadOnly(),
            capList.AsReadOnly());

        // 3. Update Balances
        var balList = new List<StoreBalanceEntry>(current.Balances.Balances);
        foreach (var b in ingestionResult.IngestedSnapshots)
        {
            balList.Add(new StoreBalanceEntry(
                b.Id.Value,
                b.ObservationId.Value,
                b.AccountId.Value,
                b.AccountIncarnationId.Value,
                b.AccountBindingId.Value,
                b.Kind.ToString(),
                b.Value?.Amount.ToString("0.00", CultureInfo.InvariantCulture),
                b.Value?.Currency,
                b.BankTimestamp,
                b.RetrievedAt,
                b.IsStale,
                b.SourceLabel));
        }

        var newBalDoc = new StoreBalancesDocument(
            current.Profile.ProfileId,
            current.Profile.BranchId,
            current.Profile.Generation,
            balList.AsReadOnly());

        return current with
        {
            Connections = newConnDoc,
            Accounts = newAccDoc,
            Balances = newBalDoc,
        };
    }

    public static StoreConnectionEntry ToStoreEntry(BankConnection conn)
    {
        ArgumentNullException.ThrowIfNull(conn);
        return new StoreConnectionEntry(
            conn.Id.Value,
            conn.InstitutionId.Value,
            conn.UserId,
            conn.CustomerId,
            conn.State.ToString(),
            conn.SystemId,
            conn.SystemStatus.ToString(),
            conn.NextClientMessageNumber,
            conn.IsSynchronized,
            conn.LastSuccessfulContact,
            conn.LastAttemptedContact,
            conn.ConnectorVersion);
    }
}
