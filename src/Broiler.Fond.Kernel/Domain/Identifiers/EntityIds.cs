namespace Broiler.Fond.Kernel.Domain.Identifiers;

/// <summary>Identifies one portable local profile lineage.</summary>
public readonly record struct ProfileId(Guid Value);

/// <summary>Identifies one manually configured institution in a profile.</summary>
public readonly record struct InstitutionId(ulong Value);

/// <summary>Identifies one bank-access context in a profile.</summary>
public readonly record struct ConnectionId(ulong Value);

/// <summary>Identifies one user-facing account in a profile.</summary>
public readonly record struct AccountId(ulong Value);

/// <summary>Identifies one bank-serviced lifetime of an account.</summary>
public readonly record struct AccountIncarnationId(ulong Value);

/// <summary>Identifies one connector path to an account incarnation.</summary>
public readonly record struct AccountBindingId(ulong Value);

/// <summary>Identifies one immutable received occurrence in the local store.</summary>
public readonly record struct ObservationId(ulong Value);

/// <summary>Identifies one immutable bank-supplied balance observation.</summary>
public readonly record struct BalanceSnapshotId(ulong Value);
