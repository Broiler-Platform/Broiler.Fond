using Broiler.Fond.Kernel.Domain.Accounts;
using Broiler.Fond.Kernel.Storage;
using Broiler.Fond.Presentation.Sessions;

namespace Broiler.Fond.Presentation.Services;

public enum BankBalanceOutcome { Saved, Blocked, AlreadyRunning, Unsupported, RequiresAuthentication, Cancelled, ContextChanged, Failed, ReceivedNotSaved }
public sealed record BankSavedBalance(string Currency, decimal? Booked, decimal? Available, decimal? CreditLine, DateTimeOffset RetrievedAt);
public sealed record BankBalanceResult(BankBalanceOutcome Outcome, string Explanation, ulong AccountId, int SavedObservations = 0,
    Broiler.Fond.Kernel.FinTs.FinTsReadExecutionStatus? ProtocolStatus = null, BankSavedBalance? SavedBalance = null)
{
    public bool IsSuccess => Outcome == BankBalanceOutcome.Saved;
}

/// <summary>Captured verified source identity, never a banking authorization or credential.</summary>
public sealed class BankBalanceContext
{
    internal BankBalanceContext(HostSession owner, ConnectionSetupContext connection, KnownAccountBinding binding,
        StoreIncarnationEntry incarnation, StoreCapabilityEntry capability)
    { Owner = owner; Connection = connection; Binding = binding; Incarnation = incarnation; Capability = capability; }
    internal HostSession Owner { get; }
    internal ConnectionSetupContext Connection { get; }
    internal KnownAccountBinding Binding { get; }
    internal StoreIncarnationEntry Incarnation { get; }
    internal StoreCapabilityEntry Capability { get; }
    public ulong AccountId => Binding.AccountId.Value;
    public ulong BindingId => Binding.BindingId.Value;
}
