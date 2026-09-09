using System.Collections.ObjectModel;
using Broiler.Fond.Kernel.Domain.Identifiers;

namespace Broiler.Fond.Kernel.Domain.Accounts;

public enum AccountBindingState { Active, Closed, Quarantined }
public enum AccountLifetimeEvidence { Unspecified, NewLifetimeReported, ClosedReported }
public enum AccountRediscoveryAction { ReuseBinding, NewAccountCandidate, QuarantinedCandidate }
public enum AccountRediscoveryReason
{
    UniqueExactMatch,
    NoKnownIdentity,
    InsufficientIdentity,
    ConflictingBankIdentifiers,
    DuplicateOccurrences,
    AmbiguousBindings,
    InactiveBinding,
    NewLifetimeReported,
    ClosedReported,
    ChangedOrConflictingLocator,
}

/// <summary>Historical provenance. Rediscovery never changes these identities or this locator.</summary>
public sealed class KnownAccountBinding
{
    public KnownAccountBinding(AccountId accountId, AccountIncarnationId incarnationId,
        AccountBindingId bindingId, AccountSourceLocator locator, AccountBindingState state)
    {
        ArgumentNullException.ThrowIfNull(locator);
        ulong[] ids = [accountId.Value, incarnationId.Value, bindingId.Value, locator.InstitutionId.Value, locator.ConnectionId.Value];
        if (ids.Contains(0UL) || ids.Distinct().Count() != ids.Length || !Enum.IsDefined(state))
        {
            throw new ArgumentException("Invalid account binding identity or state.");
        }

        AccountId = accountId;
        IncarnationId = incarnationId;
        BindingId = bindingId;
        Locator = locator;
        State = state;
    }

    public AccountId AccountId { get; }
    public AccountIncarnationId IncarnationId { get; }
    public AccountBindingId BindingId { get; }
    public AccountSourceLocator Locator { get; }
    public AccountBindingState State { get; }
}

/// <summary>
/// An already assigned occurrence identity. The connector/store must preserve it
/// durably; this object does not prove authentication, replay or bank continuity.
/// </summary>
public sealed class AccountDiscoveryOccurrence
{
    public AccountDiscoveryOccurrence(ObservationId observationId, AccountSourceLocator locator,
        AccountLifetimeEvidence lifetimeEvidence = AccountLifetimeEvidence.Unspecified)
    {
        ArgumentNullException.ThrowIfNull(locator);
        if (observationId.Value == 0 || !Enum.IsDefined(lifetimeEvidence))
        {
            throw new ArgumentException("Invalid discovery occurrence identity or evidence.");
        }

        ObservationId = observationId;
        Locator = locator;
        LifetimeEvidence = lifetimeEvidence;
    }

    public ObservationId ObservationId { get; }
    public AccountSourceLocator Locator { get; }
    public AccountLifetimeEvidence LifetimeEvidence { get; }
}

/// <summary>A recommendation only; no allocation, relinking or balance mutation is performed.</summary>
public sealed class AccountRediscoveryDecision
{
    internal AccountRediscoveryDecision(AccountDiscoveryOccurrence occurrence, AccountRediscoveryAction action,
        AccountRediscoveryReason reason, IEnumerable<KnownAccountBinding> candidates, KnownAccountBinding? reusedBinding = null)
    {
        Occurrence = occurrence;
        Action = action;
        Reason = reason;
        Candidates = candidates.OrderBy(static item => item.BindingId.Value).ToList().AsReadOnly();
        ReusedBinding = reusedBinding;
    }

    public AccountDiscoveryOccurrence Occurrence { get; }
    public AccountRediscoveryAction Action { get; }
    public AccountRediscoveryReason Reason { get; }
    public ReadOnlyCollection<KnownAccountBinding> Candidates { get; }
    public KnownAccountBinding? ReusedBinding { get; }
}

/// <summary>
/// Pure, bounded matching against one complete discovery batch. Exact dictionary
/// matches compare the full tuple; hash equality alone is never evidence.
/// </summary>
public static class AccountRediscoveryPlanner
{
    public const int MaximumBindings = 10_000;
    public const int MaximumOccurrences = 10_000;
    public const int MaximumCandidateLinks = 100_000;

    public static ReadOnlyCollection<AccountRediscoveryDecision> Plan(IEnumerable<KnownAccountBinding> knownBindings,
        IEnumerable<AccountDiscoveryOccurrence> occurrences, CancellationToken cancellationToken = default)
    {
        List<KnownAccountBinding> bindings = CopyBounded(knownBindings, MaximumBindings, cancellationToken);
        List<AccountDiscoveryOccurrence> incoming = CopyBounded(occurrences, MaximumOccurrences, cancellationToken);
        ValidateIdentityInventory(bindings, incoming);
        Dictionary<AccountSourceLocator, List<KnownAccountBinding>> exact = [];
        Dictionary<SourceAnchor, HashSet<KnownAccountBinding>> anchors = [];
        Dictionary<InstitutionId, List<KnownAccountBinding>> institutions = [];
        foreach (KnownAccountBinding binding in bindings)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!exact.TryGetValue(binding.Locator, out List<KnownAccountBinding>? group))
            {
                exact.Add(binding.Locator, group = []);
            }

            group.Add(binding);
            if (!institutions.TryGetValue(binding.Locator.InstitutionId, out List<KnownAccountBinding>? institution))
            {
                institutions.Add(binding.Locator.InstitutionId, institution = []);
            }

            institution.Add(binding);
            foreach (SourceAnchor anchor in Anchors(binding.Locator))
            {
                if (!anchors.TryGetValue(anchor, out HashSet<KnownAccountBinding>? candidates))
                {
                    anchors.Add(anchor, candidates = []);
                }

                candidates.Add(binding);
            }
        }

        Dictionary<AccountSourceLocator, int> counts = [];
        foreach (AccountDiscoveryOccurrence occurrence in incoming)
        {
            counts.TryGetValue(occurrence.Locator, out int count);
            counts[occurrence.Locator] = count + 1;
        }

        List<AccountRediscoveryDecision> decisions = [];
        int candidateLinks = 0;
        foreach (AccountDiscoveryOccurrence occurrence in incoming)
        {
            cancellationToken.ThrowIfCancellationRequested();
            AccountSourceLocator locator = occurrence.Locator;
            exact.TryGetValue(locator, out List<KnownAccountBinding>? matches);
            IEnumerable<KnownAccountBinding> candidates = matches ?? FindRelated(locator, anchors, institutions);
            KnownAccountBinding[] materialized = candidates.ToArray();
            candidateLinks += materialized.Length;
            if (candidateLinks > MaximumCandidateLinks)
            {
                throw new InvalidOperationException("Rediscovery candidate link limit exceeded; no plan was returned.");
            }

            AccountRediscoveryReason? quarantine = !locator.HasAccountIdentifier ? AccountRediscoveryReason.InsufficientIdentity
                : locator.HasConflictingIdentifiers ? AccountRediscoveryReason.ConflictingBankIdentifiers
                : counts[locator] > 1 ? AccountRediscoveryReason.DuplicateOccurrences
                : occurrence.LifetimeEvidence == AccountLifetimeEvidence.NewLifetimeReported ? AccountRediscoveryReason.NewLifetimeReported
                : occurrence.LifetimeEvidence == AccountLifetimeEvidence.ClosedReported ? AccountRediscoveryReason.ClosedReported
                : matches?.Count > 1 ? AccountRediscoveryReason.AmbiguousBindings
                : matches?.Count == 1 && matches[0].State != AccountBindingState.Active ? AccountRediscoveryReason.InactiveBinding
                : matches is null && materialized.Length != 0 ? AccountRediscoveryReason.ChangedOrConflictingLocator
                : null;
            if (quarantine is AccountRediscoveryReason reason)
            {
                decisions.Add(new(occurrence, AccountRediscoveryAction.QuarantinedCandidate, reason, materialized));
            }
            else if (matches?.Count == 1)
            {
                decisions.Add(new(occurrence, AccountRediscoveryAction.ReuseBinding, AccountRediscoveryReason.UniqueExactMatch, materialized, matches[0]));
            }
            else
            {
                decisions.Add(new(occurrence, AccountRediscoveryAction.NewAccountCandidate, AccountRediscoveryReason.NoKnownIdentity, materialized));
            }
        }

        return decisions.AsReadOnly();
    }

    private static IEnumerable<KnownAccountBinding> FindRelated(AccountSourceLocator locator,
        Dictionary<SourceAnchor, HashSet<KnownAccountBinding>> anchors, Dictionary<InstitutionId, List<KnownAccountBinding>> institutions)
    {
        HashSet<KnownAccountBinding> related = [];
        foreach (SourceAnchor anchor in Anchors(locator))
        {
            if (anchors.TryGetValue(anchor, out HashSet<KnownAccountBinding>? candidates))
            {
                related.UnionWith(candidates);
            }
        }

        // Without a shared account anchor, same-institution records are only a
        // conservative review pool. A newly added account is not auto-linked.
        if (related.Count == 0 && institutions.TryGetValue(locator.InstitutionId, out List<KnownAccountBinding>? peers))
        {
            related.UnionWith(peers);
        }

        return related;
    }

    private static IEnumerable<SourceAnchor> Anchors(AccountSourceLocator locator)
    {
        if (!string.IsNullOrWhiteSpace(locator.Iban))
        {
            // Candidate lookup only. Raw tuple equality remains mandatory for reuse.
            yield return new("iban", default, "", "", locator.Iban.Replace(" ", "", StringComparison.Ordinal).ToUpperInvariant(), "");
        }

        if (!string.IsNullOrWhiteSpace(locator.DomesticAccountNumber) && !string.IsNullOrWhiteSpace(locator.DomesticBankCode))
        {
            yield return new("domestic", locator.InstitutionId, "", "", locator.DomesticAccountNumber, locator.DomesticBankCode);
        }

        foreach (BankAccountIdentifier identifier in locator.BankIdentifiers)
        {
            if (!string.IsNullOrWhiteSpace(identifier.Value))
            {
                yield return new("bank", locator.InstitutionId, identifier.Issuer, identifier.Scope, identifier.Kind, identifier.Value);
            }
        }
    }

    private static void ValidateIdentityInventory(List<KnownAccountBinding> bindings, List<AccountDiscoveryOccurrence> occurrences)
    {
        Dictionary<ulong, LocalEntityKind> kinds = [];
        HashSet<AccountBindingId> bindingIds = [];
        Dictionary<AccountIncarnationId, AccountId> incarnationOwners = [];
        Dictionary<ConnectionId, InstitutionId> connectionOwners = [];
        void Register(ulong id, LocalEntityKind kind)
        {
            if (kinds.TryGetValue(id, out LocalEntityKind previous) && previous != kind)
            {
                throw new ArgumentException("Conflicting local identity kinds in rediscovery input.");
            }

            kinds[id] = kind;
        }

        void Context(AccountSourceLocator locator)
        {
            Register(locator.InstitutionId.Value, LocalEntityKind.Institution);
            Register(locator.ConnectionId.Value, LocalEntityKind.Connection);
            if (connectionOwners.TryGetValue(locator.ConnectionId, out InstitutionId owner) && owner != locator.InstitutionId)
            {
                throw new ArgumentException("A connection cannot belong to two institutions.");
            }

            connectionOwners[locator.ConnectionId] = locator.InstitutionId;
        }

        foreach (KnownAccountBinding binding in bindings)
        {
            Context(binding.Locator);
            Register(binding.AccountId.Value, LocalEntityKind.Account);
            Register(binding.IncarnationId.Value, LocalEntityKind.AccountIncarnation);
            Register(binding.BindingId.Value, LocalEntityKind.AccountBinding);
            if (!bindingIds.Add(binding.BindingId) ||
                (incarnationOwners.TryGetValue(binding.IncarnationId, out AccountId owner) && owner != binding.AccountId))
            {
                throw new ArgumentException("Duplicate binding or conflicting incarnation ownership.");
            }

            incarnationOwners[binding.IncarnationId] = binding.AccountId;
        }

        HashSet<ObservationId> observationIds = [];
        foreach (AccountDiscoveryOccurrence occurrence in occurrences)
        {
            Context(occurrence.Locator);
            Register(occurrence.ObservationId.Value, LocalEntityKind.Observation);
            if (!observationIds.Add(occurrence.ObservationId))
            {
                throw new ArgumentException("Duplicate occurrence identity; replay must be resolved before rediscovery.");
            }
        }
    }

    private static List<T> CopyBounded<T>(IEnumerable<T> source, int maximum, CancellationToken token) where T : class
    {
        ArgumentNullException.ThrowIfNull(source);
        token.ThrowIfCancellationRequested();
        List<T> copy = [];
        foreach (T item in source)
        {
            token.ThrowIfCancellationRequested();
            ArgumentNullException.ThrowIfNull(item);
            if (copy.Count == maximum)
            {
                throw new ArgumentException("Rediscovery input limit exceeded.");
            }

            copy.Add(item);
        }

        return copy;
    }

    private readonly record struct SourceAnchor(string Family, InstitutionId Institution, string Issuer, string Scope, string Value, string Auxiliary);
}
