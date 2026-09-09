using System.Collections.ObjectModel;
using Broiler.Fond.Kernel.Domain;
using Broiler.Fond.Kernel.Domain.Accounts;
using Broiler.Fond.Kernel.Domain.Identifiers;
using Broiler.Fond.Kernel.Domain.Institutions;

namespace Broiler.Fond.Kernel.Tests.Simulation;

internal sealed class SyntheticAccountFixture
{
    private readonly BalanceSnapshot _firstBalance;
    private readonly BalanceSnapshot _secondBalance;

    internal SyntheticAccountFixture(DateTimeOffset timestamp, string sourceIdentifier = "SYNTHETIC-NOT-AN-IBAN")
    {
        _ = LocalIdentityLedger.TryOpen(IdentitySnapshot.Empty(), out LocalIdentityLedger? ledger);
        using IdentityAllocationBatch batch = ledger!.BeginBatch();
        InstitutionId institution = new(batch.Allocate(LocalEntityKind.Institution).Value);
        ConnectionId connection = new(batch.Allocate(LocalEntityKind.Connection, [new(new(institution.Value), LocalEntityKind.Institution)]).Value);
        List<KnownAccountBinding> bindings = [];
        List<AccountDiscoveryOccurrence> occurrences = [];
        List<BalanceSnapshot> balances = [];
        foreach (decimal amount in new[] { 123.45m, -20.01m })
        {
            AccountId account = new(batch.Allocate(LocalEntityKind.Account).Value);
            AccountIncarnationId incarnation = new(batch.Allocate(LocalEntityKind.AccountIncarnation, [new(new(account.Value), LocalEntityKind.Account)]).Value);
            AccountBindingId binding = new(batch.Allocate(LocalEntityKind.AccountBinding,
                [new(new(incarnation.Value), LocalEntityKind.AccountIncarnation), new(new(connection.Value), LocalEntityKind.Connection)]).Value);
            ObservationId observation = new(batch.Allocate(LocalEntityKind.Observation).Value);
            BalanceSnapshotId balance = new(batch.Allocate(LocalEntityKind.BalanceSnapshot,
                [new(new(observation.Value), LocalEntityKind.Observation), new(new(binding.Value), LocalEntityKind.AccountBinding)]).Value);
            AccountSourceLocator locator = new(institution, connection, "synthetic-workflow", "1",
                ManualInstitutionConfiguration.Create("Synthetic fixture", "https://bank.example/fints"),
                sourceIdentifier + account.Value, null, null, null, "EUR");
            bindings.Add(new(account, incarnation, binding, locator, AccountBindingState.Active));
            occurrences.Add(new(observation, locator));
            balances.Add(new(balance, observation, account, incarnation, binding, BalanceKind.Booked,
                new Money(amount, "EUR"), timestamp, timestamp));
        }

        Identity = batch.Commit();
        Bindings = bindings.AsReadOnly();
        Occurrences = occurrences.AsReadOnly();
        _firstBalance = balances[0];
        _secondBalance = balances[1];
    }

    internal IdentitySnapshot Identity { get; }
    internal ReadOnlyCollection<KnownAccountBinding> Bindings { get; }
    internal ReadOnlyCollection<AccountDiscoveryOccurrence> Occurrences { get; }

    internal AccountValueInput[] Values(bool partial) =>
    [
        new(Bindings[0], AccountBalanceSupport.SupportedCurrentAccount, [_firstBalance]),
        new(Bindings[1], AccountBalanceSupport.SupportedCurrentAccount, partial ? [] : [_secondBalance],
            partial ? AccountRefreshOutcome.Partial : AccountRefreshOutcome.Succeeded),
    ];
}
