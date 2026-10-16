using System.Collections.ObjectModel;
using System.Globalization;
using Broiler.Fond.Kernel.Domain.Identifiers;
using Broiler.Fond.Kernel.Storage;

namespace Broiler.Fond.Kernel.Domain.Accounts;

/// <summary>
/// Result of planning or executing an account binding relink operation.
/// </summary>
public sealed record AccountRelinkingResult(
    ulong RelinkedBindingId,
    ulong PreviousAccountId,
    ulong TargetAccountId,
    ulong TargetIncarnationId,
    ulong? PrunedAccountId,
    ulong? PrunedIncarnationId,
    StoreSnapshot UpdatedSnapshot,
    ProfileSaveMetrics? SaveMetrics);

/// <summary>
/// Plans and validates deterministic account binding relink transformations over a <see cref="StoreSnapshot"/>.
/// Allows user-reviewed reconnection of quarantined or candidate account bindings to an existing logical account identity.
/// </summary>
public static class AccountRelinkingPlanner
{
    public static AccountRelinkingResult PlanRelink(
        StoreSnapshot snapshot,
        ulong bindingId,
        ulong targetAccountId,
        bool pruneOrphanedAccount = true,
        DateTimeOffset? savedAt = null)
    {
        ArgumentNullException.ThrowIfNull(snapshot);

        StoreArchiveCodec.ValidateIdentityAndSemanticInvariants(snapshot);

        StoreBindingEntry targetBinding = snapshot.Accounts.Bindings.FirstOrDefault(b => b.Id == bindingId)
            ?? throw new InvalidOperationException($"Binding ID {bindingId} does not exist in the snapshot.");

        StoreAccountEntry targetAccount = snapshot.Accounts.Accounts.FirstOrDefault(a => a.Id == targetAccountId)
            ?? throw new InvalidOperationException($"Target account ID {targetAccountId} does not exist in the snapshot.");

        ulong previousAccountId = targetBinding.AccountId;
        ulong previousIncarnationId = targetBinding.IncarnationId;

        if (previousAccountId == targetAccountId && string.Equals(targetBinding.State, "Active", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException($"Binding {bindingId} is already linked to account {targetAccountId} and is active.");
        }

        // Currency validation: binding currency and target account currency must be compatible
        if (!string.IsNullOrWhiteSpace(targetBinding.Currency) && !string.IsNullOrWhiteSpace(targetAccount.Currency))
        {
            if (!string.Equals(targetBinding.Currency.Trim(), targetAccount.Currency.Trim(), StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException(
                    $"Cannot relink binding {bindingId} with currency '{targetBinding.Currency}' to account {targetAccountId} with currency '{targetAccount.Currency}'.");
            }
        }

        // Locate active incarnation under target account
        List<StoreIncarnationEntry> targetIncarnations = snapshot.Accounts.Incarnations
            .Where(i => i.AccountId == targetAccountId)
            .ToList();

        if (targetIncarnations.Count == 0)
        {
            throw new InvalidOperationException($"Target account ID {targetAccountId} has no incarnations in the profile.");
        }

        StoreIncarnationEntry targetIncarnation = targetIncarnations
            .FirstOrDefault(i => !string.Equals(i.LifetimeEvidence, "ClosedReported", StringComparison.OrdinalIgnoreCase))
            ?? targetIncarnations[^1];

        // Check for duplicate active binding on target account under the same connection
        bool duplicateBindingExists = snapshot.Accounts.Bindings.Any(b =>
            b.Id != bindingId &&
            b.AccountId == targetAccountId &&
            b.ConnectionId == targetBinding.ConnectionId &&
            string.Equals(b.State, "Active", StringComparison.OrdinalIgnoreCase) &&
            HasMatchingLocator(b, targetBinding));

        if (duplicateBindingExists)
        {
            throw new InvalidOperationException(
                $"Target account {targetAccountId} already has an active binding on connection {targetBinding.ConnectionId} with matching locator.");
        }

        DateTimeOffset timestamp = savedAt ?? DateTimeOffset.UtcNow;
        Guid pid = snapshot.Profile.ProfileId;
        Guid bid = snapshot.Profile.BranchId;
        ulong gen = snapshot.Profile.Generation;

        // 1. Update binding to point to target account and incarnation with Active state
        List<StoreBindingEntry> updatedBindings = new(snapshot.Accounts.Bindings.Count);
        foreach (StoreBindingEntry b in snapshot.Accounts.Bindings)
        {
            if (b.Id == bindingId)
            {
                updatedBindings.Add(b with
                {
                    AccountId = targetAccountId,
                    IncarnationId = targetIncarnation.Id,
                    State = "Active",
                });
            }
            else
            {
                updatedBindings.Add(b);
            }
        }

        // 2. Update capabilities: capabilities under (ConnectionId, previousAccountId) move to (ConnectionId, targetAccountId)
        List<StoreCapabilityEntry> updatedCapabilities = [];
        HashSet<(ulong ConnId, ulong AccId, string Op)> seenCaps = [];
        foreach (StoreCapabilityEntry cap in snapshot.Accounts.Capabilities)
        {
            ulong capAccId = (cap.ConnectionId == targetBinding.ConnectionId && cap.AccountId == previousAccountId)
                ? targetAccountId
                : cap.AccountId;

            if (seenCaps.Add((cap.ConnectionId, capAccId, cap.OperationCode)))
            {
                updatedCapabilities.Add(cap with { AccountId = capAccId });
            }
        }

        // 3. Provenance preservation: per ADR 0007 / Roadmap, every balance snapshot, observation,
        // and history entry permanently retains the AccountBindingId and AccountIncarnationId active at origin.
        // Relinking updates bindings; it never rewrites historical balance snapshots.

        // 4. Prune orphaned previous account if requested, when it was an unconfirmed/temporary candidate shell
        // with no remaining bindings and no historical balance entries referencing it.
        ulong? prunedAccountId = null;
        ulong? prunedIncarnationId = null;
        List<StoreAccountEntry> updatedAccounts = snapshot.Accounts.Accounts.ToList();
        List<StoreIncarnationEntry> updatedIncarnations = snapshot.Accounts.Incarnations.ToList();

        List<ulong> previousIncarnationIds = snapshot.Accounts.Incarnations
            .Where(i => i.AccountId == previousAccountId)
            .Select(i => i.Id)
            .ToList();

        bool previousHasOtherBindings = updatedBindings.Any(b => b.AccountId == previousAccountId);
        bool previousHasBalances = snapshot.Balances.Balances.Any(b =>
            b.AccountId == previousAccountId ||
            previousIncarnationIds.Contains(b.IncarnationId) ||
            b.BindingId == bindingId);

        if (pruneOrphanedAccount && !previousHasOtherBindings && !previousHasBalances && previousAccountId != targetAccountId)
        {
            prunedAccountId = previousAccountId;
            prunedIncarnationId = previousIncarnationId;
            updatedAccounts.RemoveAll(a => a.Id == previousAccountId);
            updatedIncarnations.RemoveAll(i => previousIncarnationIds.Contains(i.Id));
            updatedCapabilities.RemoveAll(c => c.AccountId == previousAccountId);
        }

        // 5. Update ledger records
        HashSet<ulong> purgedEntityIds = [];
        if (prunedAccountId.HasValue)
        {
            purgedEntityIds.Add(prunedAccountId.Value);
            foreach (ulong incId in previousIncarnationIds)
            {
                purgedEntityIds.Add(incId);
            }
        }

        List<StoreLedgerRecord> updatedLedger = [];
        foreach (StoreLedgerRecord rec in snapshot.Ledger.Records)
        {
            if (purgedEntityIds.Contains(rec.Id))
            {
                continue;
            }

            if (rec.Id == bindingId)
            {
                // Update binding references to target incarnation and connection
                List<(ulong TargetId, string ExpectedKind)> updatedRefs =
                [
                    (targetIncarnation.Id, "AccountIncarnation"),
                    (targetBinding.ConnectionId, "Connection"),
                ];

                updatedLedger.Add(new StoreLedgerRecord(rec.Id, rec.Kind, rec.SupersedesId, updatedRefs));
            }
            else
            {
                // Filter out any references to pruned entities
                List<(ulong TargetId, string ExpectedKind)> filteredRefs = rec.References
                    .Where(r => !purgedEntityIds.Contains(r.TargetId))
                    .ToList();
                ulong? filteredSup = rec.SupersedesId.HasValue && !purgedEntityIds.Contains(rec.SupersedesId.Value)
                    ? rec.SupersedesId
                    : null;

                updatedLedger.Add(new StoreLedgerRecord(rec.Id, rec.Kind, filteredSup, filteredRefs));
            }
        }

        StoreSnapshot updatedSnapshot = new(
            Profile: snapshot.Profile with { SavedAt = timestamp },
            Institutions: snapshot.Institutions,
            Connections: snapshot.Connections,
            Accounts: new StoreAccountsDocument(pid, bid, gen, updatedAccounts, updatedIncarnations, updatedBindings, updatedCapabilities),
            Balances: snapshot.Balances,
            Ledger: new StoreLedgerDocument(pid, bid, gen, updatedLedger));

        StoreArchiveCodec.ValidateIdentityAndSemanticInvariants(updatedSnapshot);

        return new AccountRelinkingResult(
            RelinkedBindingId: bindingId,
            PreviousAccountId: previousAccountId,
            TargetAccountId: targetAccountId,
            TargetIncarnationId: targetIncarnation.Id,
            PrunedAccountId: prunedAccountId,
            PrunedIncarnationId: prunedIncarnationId,
            UpdatedSnapshot: updatedSnapshot,
            SaveMetrics: null);
    }

    private static bool HasMatchingLocator(StoreBindingEntry a, StoreBindingEntry b)
    {
        if (!string.IsNullOrWhiteSpace(a.Iban) && !string.IsNullOrWhiteSpace(b.Iban))
        {
            return string.Equals(a.Iban.Trim(), b.Iban.Trim(), StringComparison.OrdinalIgnoreCase);
        }

        if (!string.IsNullOrWhiteSpace(a.DomesticAccountNumber) && !string.IsNullOrWhiteSpace(b.DomesticAccountNumber))
        {
            return string.Equals(a.DomesticAccountNumber.Trim(), b.DomesticAccountNumber.Trim(), StringComparison.OrdinalIgnoreCase) &&
                   string.Equals((a.DomesticBankCode ?? "").Trim(), (b.DomesticBankCode ?? "").Trim(), StringComparison.OrdinalIgnoreCase);
        }

        return false;
    }
}
