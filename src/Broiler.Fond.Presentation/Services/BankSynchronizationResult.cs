using Broiler.Fond.Kernel.FinTs;

namespace Broiler.Fond.Presentation.Services;

public enum BankSynchronizationOutcome { Synchronized, AlreadySynchronized, AlreadyRunning, Blocked, UnsupportedCandidate, RequiresAuthentication, Cancelled, ContextChanged, Failed, ReceivedNotSaved }
public sealed record BankSynchronizationResult(BankSynchronizationOutcome Outcome, string Explanation,
    FinTsSynchronizationExecutionStatus? ProtocolStatus = null)
{
    public bool IsSuccess => Outcome is BankSynchronizationOutcome.Synchronized or BankSynchronizationOutcome.AlreadySynchronized;
}
