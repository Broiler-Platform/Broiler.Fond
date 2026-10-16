namespace Broiler.Fond.Kernel.Storage;

/// <summary>
/// User-selected disposition for cached accounts, incarnations, bindings, and balance values
/// when removing a bank connection from a profile under M1-08.
/// </summary>
public enum ConnectionRemovalDataDisposition
{
    /// <summary>
    /// Removes the bank connection state and active capabilities, transitions its bindings to
    /// <c>Inactive</c>, retains cached accounts and balances for offline viewing, and executes
    /// a verified privacy purge of superseded generations containing the removed connection credentials/state.
    /// </summary>
    RetainCachedAccountsAndBalances = 1,

    /// <summary>
    /// Removes the bank connection state, capabilities, bindings, and all accounts, incarnations,
    /// observations, and balances exclusively associated with the connection (plus the institution
    /// if no other connection references it), and executes a verified privacy purge of all superseded generations.
    /// </summary>
    DeleteCachedAccountsAndPurge = 2,
}

/// <summary>
/// Result of planning or executing a connection removal and cached-data purge operation.
/// </summary>
public sealed record ConnectionRemovalResult(
    ulong RemovedConnectionId,
    ConnectionRemovalDataDisposition Disposition,
    ulong? RemovedInstitutionId,
    IReadOnlyList<ulong> RemovedBindingIds,
    IReadOnlyList<ulong> DeactivatedBindingIds,
    IReadOnlyList<ulong> RemovedAccountIds,
    IReadOnlyList<ulong> RemovedIncarnationIds,
    IReadOnlyList<ulong> RemovedBalanceIds,
    StoreSnapshot UpdatedSnapshot,
    ProfileSaveMetrics? SaveMetrics,
    bool ExternalCopyDisclosureRequired = true);

/// <summary>
/// Plans and validates deterministic connection removal and cached-data purge transformations over a <see cref="StoreSnapshot"/>.
/// </summary>
public static class ConnectionRemovalPlanner
{
    public static ConnectionRemovalResult PlanRemoval(
        StoreSnapshot snapshot,
        ulong connectionId,
        ConnectionRemovalDataDisposition disposition,
        bool removeOrphanedInstitution = true,
        DateTimeOffset? savedAt = null)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        if (!Enum.IsDefined(disposition))
        {
            throw new ArgumentOutOfRangeException(nameof(disposition));
        }

        StoreArchiveCodec.ValidateIdentityAndSemanticInvariants(snapshot);

        StoreConnectionEntry targetConn = snapshot.Connections.Connections.FirstOrDefault(c => c.Id == connectionId)
            ?? throw new InvalidOperationException($"Connection ID {connectionId} does not exist in the current snapshot.");

        DateTimeOffset timestamp = savedAt ?? DateTimeOffset.UtcNow;
        Guid pid = snapshot.Profile.ProfileId;
        Guid bid = snapshot.Profile.BranchId;
        ulong gen = snapshot.Profile.Generation;

        List<StoreConnectionEntry> remainingConnections = snapshot.Connections.Connections
            .Where(c => c.Id != connectionId)
            .ToList();

        List<StoreCapabilityEntry> remainingCapabilities = snapshot.Accounts.Capabilities
            .Where(c => c.ConnectionId != connectionId)
            .ToList();

        if (disposition == ConnectionRemovalDataDisposition.RetainCachedAccountsAndBalances)
        {
            List<ulong> deactivatedBindings = [];
            List<StoreBindingEntry> updatedBindings = new(snapshot.Accounts.Bindings.Count);
            foreach (StoreBindingEntry bin in snapshot.Accounts.Bindings)
            {
                if (bin.ConnectionId == connectionId)
                {
                    deactivatedBindings.Add(bin.Id);
                    updatedBindings.Add(bin with { State = "Inactive" });
                }
                else
                {
                    updatedBindings.Add(bin);
                }
            }

            StoreSnapshot retainedSnapshot = new(
                snapshot.Profile with { SavedAt = timestamp },
                snapshot.Institutions,
                new StoreConnectionsDocument(pid, bid, gen, remainingConnections),
                new StoreAccountsDocument(
                    pid,
                    bid,
                    gen,
                    snapshot.Accounts.Accounts,
                    snapshot.Accounts.Incarnations,
                    updatedBindings,
                    remainingCapabilities),
                snapshot.Balances,
                snapshot.Ledger);

            StoreArchiveCodec.ValidateIdentityAndSemanticInvariants(retainedSnapshot);

            return new ConnectionRemovalResult(
                RemovedConnectionId: connectionId,
                Disposition: disposition,
                RemovedInstitutionId: null,
                RemovedBindingIds: [],
                DeactivatedBindingIds: deactivatedBindings,
                RemovedAccountIds: [],
                RemovedIncarnationIds: [],
                RemovedBalanceIds: [],
                UpdatedSnapshot: retainedSnapshot,
                SaveMetrics: null,
                ExternalCopyDisclosureRequired: true);
        }

        // DeleteCachedAccountsAndPurge: remove bindings, balances, and exclusively owned incarnations/accounts/institution.
        HashSet<ulong> removedBindingSet = snapshot.Accounts.Bindings
            .Where(b => b.ConnectionId == connectionId)
            .Select(b => b.Id)
            .ToHashSet();

        List<StoreBindingEntry> keptBindings = snapshot.Accounts.Bindings
            .Where(b => !removedBindingSet.Contains(b.Id))
            .ToList();

        List<StoreBalanceEntry> removedBalances = snapshot.Balances.Balances
            .Where(b => removedBindingSet.Contains(b.BindingId))
            .ToList();
        HashSet<ulong> removedBalanceSet = removedBalances.Select(b => b.Id).ToHashSet();

        List<StoreBalanceEntry> keptBalances = snapshot.Balances.Balances
            .Where(b => !removedBalanceSet.Contains(b.Id))
            .ToList();

        HashSet<ulong> keptObservationIds = keptBalances.Select(b => b.ObservationId).ToHashSet();
        HashSet<ulong> removedObservationSet = removedBalances
            .Select(b => b.ObservationId)
            .Where(obsId => !keptObservationIds.Contains(obsId))
            .ToHashSet();

        // Incarnations still referenced by any kept binding or kept balance must be retained.
        HashSet<ulong> activeIncarnationIds = keptBindings.Select(b => b.IncarnationId)
            .Concat(keptBalances.Select(b => b.IncarnationId))
            .ToHashSet();

        HashSet<ulong> CandidateIncarnationsForConnection = snapshot.Accounts.Bindings
            .Where(b => b.ConnectionId == connectionId)
            .Select(b => b.IncarnationId)
            .ToHashSet();

        HashSet<ulong> removedIncarnationSet = snapshot.Accounts.Incarnations
            .Where(inc => CandidateIncarnationsForConnection.Contains(inc.Id) && !activeIncarnationIds.Contains(inc.Id))
            .Select(inc => inc.Id)
            .ToHashSet();

        List<StoreIncarnationEntry> keptIncarnations = snapshot.Accounts.Incarnations
            .Where(inc => !removedIncarnationSet.Contains(inc.Id))
            .ToList();

        // Accounts still referenced by any kept incarnation, kept binding, kept capability, or kept balance must be retained.
        HashSet<ulong> activeAccountIds = keptIncarnations.Select(inc => inc.AccountId)
            .Concat(keptBindings.Select(b => b.AccountId))
            .Concat(remainingCapabilities.Select(c => c.AccountId))
            .Concat(keptBalances.Select(b => b.AccountId))
            .ToHashSet();

        HashSet<ulong> candidateAccountsForConnection = snapshot.Accounts.Bindings
            .Where(b => b.ConnectionId == connectionId)
            .Select(b => b.AccountId)
            .Concat(snapshot.Accounts.Capabilities.Where(c => c.ConnectionId == connectionId).Select(c => c.AccountId))
            .ToHashSet();

        HashSet<ulong> removedAccountSet = snapshot.Accounts.Accounts
            .Where(acc => candidateAccountsForConnection.Contains(acc.Id) && !activeAccountIds.Contains(acc.Id))
            .Select(acc => acc.Id)
            .ToHashSet();

        List<StoreAccountEntry> keptAccounts = snapshot.Accounts.Accounts
            .Where(acc => !removedAccountSet.Contains(acc.Id))
            .ToList();

        ulong? removedInstitutionId = null;
        List<StoreInstitutionEntry> keptInstitutions = snapshot.Institutions.Institutions.ToList();
        if (removeOrphanedInstitution)
        {
            ulong instId = targetConn.InstitutionId;
            bool institutionStillReferenced =
                remainingConnections.Any(c => c.InstitutionId == instId) ||
                keptBindings.Any(b => b.InstitutionId == instId);

            if (!institutionStillReferenced)
            {
                removedInstitutionId = instId;
                keptInstitutions = keptInstitutions.Where(i => i.Id != instId).ToList();
            }
        }

        // Remove all purged entity IDs from ledger.xml while preserving Profile.NextId so IDs are never reused.
        HashSet<ulong> purgedEntityIds = [connectionId];
        if (removedInstitutionId.HasValue)
        {
            purgedEntityIds.Add(removedInstitutionId.Value);
        }

        purgedEntityIds.UnionWith(removedBindingSet);
        purgedEntityIds.UnionWith(removedBalanceSet);
        purgedEntityIds.UnionWith(removedObservationSet);
        purgedEntityIds.UnionWith(removedIncarnationSet);
        purgedEntityIds.UnionWith(removedAccountSet);

        List<StoreLedgerRecord> keptLedger = [];
        foreach (StoreLedgerRecord rec in snapshot.Ledger.Records)
        {
            if (purgedEntityIds.Contains(rec.Id))
            {
                continue;
            }

            List<(ulong TargetId, string ExpectedKind)> filteredRefs = rec.References
                .Where(r => !purgedEntityIds.Contains(r.TargetId))
                .ToList();
            ulong? filteredSup = rec.SupersedesId.HasValue && !purgedEntityIds.Contains(rec.SupersedesId.Value)
                ? rec.SupersedesId
                : null;

            keptLedger.Add(new StoreLedgerRecord(rec.Id, rec.Kind, filteredSup, filteredRefs));
        }

        StoreSnapshot purgedSnapshot = new(
            snapshot.Profile with { SavedAt = timestamp },
            new StoreInstitutionsDocument(pid, bid, gen, keptInstitutions),
            new StoreConnectionsDocument(pid, bid, gen, remainingConnections),
            new StoreAccountsDocument(pid, bid, gen, keptAccounts, keptIncarnations, keptBindings, remainingCapabilities),
            new StoreBalancesDocument(pid, bid, gen, keptBalances),
            new StoreLedgerDocument(pid, bid, gen, keptLedger));

        StoreArchiveCodec.ValidateIdentityAndSemanticInvariants(purgedSnapshot);

        return new ConnectionRemovalResult(
            RemovedConnectionId: connectionId,
            Disposition: disposition,
            RemovedInstitutionId: removedInstitutionId,
            RemovedBindingIds: removedBindingSet.OrderBy(id => id).ToList(),
            DeactivatedBindingIds: [],
            RemovedAccountIds: removedAccountSet.OrderBy(id => id).ToList(),
            RemovedIncarnationIds: removedIncarnationSet.OrderBy(id => id).ToList(),
            RemovedBalanceIds: removedBalanceSet.OrderBy(id => id).ToList(),
            UpdatedSnapshot: purgedSnapshot,
            SaveMetrics: null,
            ExternalCopyDisclosureRequired: true);
    }
}
