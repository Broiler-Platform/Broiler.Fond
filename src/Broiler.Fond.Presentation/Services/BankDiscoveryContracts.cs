using Broiler.Fond.Kernel.Domain.Accounts;
using Broiler.Fond.Kernel.Domain.Connections;
using Broiler.Fond.Presentation.Sessions;

namespace Broiler.Fond.Presentation.Services;

public enum BankDiscoveryOutcome { ReviewRequired, Saved, Cancelled, Blocked, Unsupported, Failed, ContextChanged }
public enum BankDiscoveryMatch { KnownBinding, ManualAccount, NewAccount, NeedsReview }

/// <summary>Immutable, credential-free bank observation. An association is a proposal until explicitly confirmed.</summary>
public sealed class BankDiscoveredAccount
{
    internal BankDiscoveredAccount(int index, AccountSourceLocator locator, BankDiscoveryMatch match,
        ulong? accountId, ulong? bindingId, string? displayName, int? balanceVersion, string explanation)
    {
        Index = index; Locator = locator; Match = match; AccountId = accountId; BindingId = bindingId;
        DisplayName = displayName; BalanceVersion = balanceVersion; Explanation = explanation;
    }
    internal AccountSourceLocator Locator { get; }
    internal ulong? BindingId { get; }
    public int Index { get; }
    public string? Iban => Locator.Iban;
    public string? AccountNumber => Locator.DomesticAccountNumber;
    public string? Currency => Locator.Currency;
    public string? Bic => Locator.BankIdentifiers.SingleOrDefault(i => i.Kind == "bic")?.Value;
    public BankDiscoveryMatch Match { get; }
    public ulong? AccountId { get; }
    public string? DisplayName { get; }
    public int? BalanceVersion { get; }
    public string Explanation { get; }
    public bool CanConfirm => Match is BankDiscoveryMatch.ManualAccount or BankDiscoveryMatch.NewAccount;
}

/// <summary>Session-bound review token, not a reusable bank authorization. Contains no PIN/TAN.</summary>
public sealed class BankDiscoveryReview
{
    internal BankDiscoveryReview(HostSession owner, ConnectionSetupContext context, BankConnection connection,
        IEnumerable<BankDiscoveredAccount> accounts)
    { Owner = owner; Context = context; Connection = connection; Accounts = accounts.ToList().AsReadOnly(); }
    internal HostSession Owner { get; }
    internal ConnectionSetupContext Context { get; }
    internal BankConnection Connection { get; }
    internal long StartedAt { get; } = System.Diagnostics.Stopwatch.GetTimestamp();
    internal bool Consumed { get; set; }
    public string BankName => Context.Institution.DisplayName;
    public string Endpoint => Context.Institution.Endpoint;
    public string UserId => Context.Connection.UserId;
    public IReadOnlyList<BankDiscoveredAccount> Accounts { get; }
}

public sealed record BankDiscoveryResult(BankDiscoveryOutcome Outcome, string Explanation, BankDiscoveryReview? Review = null,
    Broiler.Fond.Kernel.FinTs.FinTsReadExecutionStatus? ProtocolStatus = null);

public interface IBankAccountDiscoveryReview
{
    /// <summary>Returns explicitly confirmed row indexes, or null to cancel the entire review.</summary>
    Task<IReadOnlyCollection<int>?> ReviewBankAccountsAsync(BankDiscoveryReview review, CancellationToken cancellationToken = default);
}
