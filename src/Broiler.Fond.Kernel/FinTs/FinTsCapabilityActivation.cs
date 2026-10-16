using System.Collections.ObjectModel;
using Broiler.Fond.Kernel.Domain.Accounts;
using Broiler.Fond.Kernel.Domain.Connections;
using Broiler.Fond.Kernel.Domain.Identifiers;

namespace Broiler.Fond.Kernel.FinTs;

/// <summary>Status of a capability activation evaluation against connection and parameter evidence.</summary>
public enum FinTsCapabilityActivationStatus
{
    /// <summary>Capabilities were successfully activated and evaluated.</summary>
    Activated = 1,

    /// <summary>The bank connection is disabled; capabilities cannot be activated.</summary>
    RejectedDisabledConnection = 2,

    /// <summary>The bank connection is not synchronized or not in a connected state.</summary>
    RejectedUnsynchronizedConnection = 3,

    /// <summary>Required bank parameter data (BPD) or protocol 300 advertisement is missing.</summary>
    RejectedMissingParameters = 4,

    /// <summary>Institution identity in parameter data does not match the bank connection.</summary>
    RejectedInstitutionMismatch = 5,

    /// <summary>Parameter evidence contains fatal format, signature, or consistency issues.</summary>
    RejectedEvidenceIssue = 6,
}

/// <summary>Result of projecting parsed FinTS BPD and UPD parameters into active account capabilities.</summary>
public sealed class FinTsCapabilityActivationResult
{
    public FinTsCapabilityActivationStatus Status { get; }
    public ReadOnlyCollection<AccountCapability> Capabilities { get; }
    public ReadOnlyCollection<string> Issues { get; }
    public bool IsSuccess => Status == FinTsCapabilityActivationStatus.Activated;

    internal FinTsCapabilityActivationResult(
        FinTsCapabilityActivationStatus status,
        IList<AccountCapability> capabilities,
        IList<string> issues)
    {
        Status = status;
        Capabilities = new ReadOnlyCollection<AccountCapability>(capabilities);
        Issues = new ReadOnlyCollection<string>(issues);
    }

    public static FinTsCapabilityActivationResult Success(IList<AccountCapability> capabilities, IList<string>? issues = null) =>
        new(FinTsCapabilityActivationStatus.Activated, capabilities, issues ?? Array.Empty<string>());

    public static FinTsCapabilityActivationResult Rejected(FinTsCapabilityActivationStatus status, string issue) =>
        new(status, Array.Empty<AccountCapability>(), new[] { issue });
}

/// <summary>
/// Evaluates parsed FinTS 3.0 BPD and UPD parameter sets to project verified,
/// active <see cref="AccountCapability"/> records for a <see cref="BankConnection"/>.
/// </summary>
public static class FinTsCapabilityActivation
{
    public static FinTsCapabilityActivationResult Activate(
        BankConnection connection,
        FinTsReadParameterSet readParameters,
        DateTimeOffset activationTimestamp,
        Func<FinTsAccountParameters, AccountId?>? accountResolver = null)
    {
        ArgumentNullException.ThrowIfNull(connection);
        ArgumentNullException.ThrowIfNull(readParameters);

        // 1. Connection operational state validation
        if (connection.State == BankConnectionState.Disabled)
        {
            return FinTsCapabilityActivationResult.Rejected(
                FinTsCapabilityActivationStatus.RejectedDisabledConnection,
                "Bank connection is disabled; capability activation rejected.");
        }

        if (!connection.IsSynchronized && connection.State != BankConnectionState.Connected)
        {
            return FinTsCapabilityActivationResult.Rejected(
                FinTsCapabilityActivationStatus.RejectedUnsynchronizedConnection,
                "Bank connection must be synchronized or connected before activating capabilities.");
        }

        // 2. BPD parameter verification
        var bpd = readParameters.Source;
        if (bpd.Bank is null)
        {
            return FinTsCapabilityActivationResult.Rejected(
                FinTsCapabilityActivationStatus.RejectedMissingParameters,
                "BPD is missing bank parameter segment (HIBPA).");
        }

        if (!bpd.Bank.ProtocolVersions.Contains(300))
        {
            return FinTsCapabilityActivationResult.Rejected(
                FinTsCapabilityActivationStatus.RejectedMissingParameters,
                "BPD does not advertise FinTS 3.0 protocol version (300).");
        }

        // 3. Institution identity comparison
        if (!string.IsNullOrWhiteSpace(connection.InstitutionConfiguration.BankCode))
        {
            string expectedBankCode = connection.InstitutionConfiguration.BankCode.Trim();
            var bankFields = bpd.Bank.Source.Fields;
            if (bankFields.Count > 1 && bankFields[1].Elements.Count > 0)
            {
                var elements = bankFields[1].Elements;
                string reportedBankCode = elements.Count > 1
                    ? elements[1].HeaderText().Trim()
                    : elements[0].HeaderText().Trim();

                if (!string.Equals(reportedBankCode, expectedBankCode, StringComparison.OrdinalIgnoreCase))
                {
                    return FinTsCapabilityActivationResult.Rejected(
                        FinTsCapabilityActivationStatus.RejectedInstitutionMismatch,
                        $"Reported bank code '{reportedBankCode}' does not match connection bank code '{expectedBankCode}'.");
                }
            }
        }

        List<AccountCapability> capabilities = [];
        List<string> issues = [];

        // 4. Connection-level standard operational capabilities
        capabilities.Add(new AccountCapability(
            connection.Id,
            accountId: null,
            operationCode: "HKSYN",
            version: 3,
            minimumSignatures: 0,
            maximumOrders: 1,
            status: AccountCapabilityStatus.Active,
            activatedAt: activationTimestamp));

        capabilities.Add(new AccountCapability(
            connection.Id,
            accountId: null,
            operationCode: "HKEND",
            version: 1,
            minimumSignatures: 0,
            maximumOrders: 1,
            status: AccountCapabilityStatus.Active,
            activatedAt: activationTimestamp));

        // 5. Account-level capability projections
        var accounts = bpd.Accounts;
        var advertisements = readParameters.Advertisements;

        foreach (var account in accounts)
        {
            AccountId? localAccountId = accountResolver?.Invoke(account);

            // Evaluate HKSAL (Balance)
            var balanceAds = advertisements.Where(a => a.Operation == FinTsReadOperation.Balance).ToList();
            foreach (var ad in balanceAds)
            {
                var evidence = bpd.GetOperationEvidence(account, "HKSAL");
                var perm = account.Permissions.FirstOrDefault(p => p.Operation == "HKSAL");

                AccountCapabilityStatus status;
                if (evidence is FinTsOperationEvidence.UnlistedBlocked or FinTsOperationEvidence.Ambiguous or FinTsOperationEvidence.Unknown)
                {
                    status = AccountCapabilityStatus.Suspended;
                    issues.Add($"Account {account.Iban.HeaderText()}: HKSAL v{ad.Version} suspended due to permission status {evidence}.");
                }
                else if (ad.MaximumOrders == 0)
                {
                    status = AccountCapabilityStatus.Suspended;
                    issues.Add($"Account {account.Iban.HeaderText()}: HKSAL v{ad.Version} suspended due to zero order capacity.");
                }
                else if (perm is not null && perm.RequiredSignatures < ad.MinimumSignatures)
                {
                    status = AccountCapabilityStatus.Suspended;
                    issues.Add($"Account {account.Iban.HeaderText()}: HKSAL v{ad.Version} suspended due to signature requirement conflict.");
                }
                else if (ad.Version is 7 or 8 && account.Iban.IsEmpty)
                {
                    status = AccountCapabilityStatus.Suspended;
                    string accountIdentifier = account.HasAccountConnection && account.AccountConnection.Elements.Count > 0
                        ? account.AccountConnection.Elements[0].HeaderText()
                        : "Unknown";
                    issues.Add($"Account {accountIdentifier}: HKSAL v{ad.Version} suspended due to missing IBAN.");
                }
                else
                {
                    status = AccountCapabilityStatus.Active;
                }

                int effectiveSignatures = perm is not null ? Math.Max(ad.MinimumSignatures, perm.RequiredSignatures) : ad.MinimumSignatures;

                capabilities.Add(new AccountCapability(
                    connection.Id,
                    localAccountId,
                    operationCode: "HKSAL",
                    version: ad.Version,
                    minimumSignatures: effectiveSignatures,
                    maximumOrders: ad.MaximumOrders,
                    status: status,
                    activatedAt: activationTimestamp));
            }

            // Evaluate HKSPA (SEPA Account Details)
            var sepaAds = advertisements.Where(a => a.Operation == FinTsReadOperation.SepaAccountDetails).ToList();
            foreach (var ad in sepaAds)
            {
                var evidence = bpd.GetOperationEvidence(account, "HKSPA");
                var perm = account.Permissions.FirstOrDefault(p => p.Operation == "HKSPA");

                AccountCapabilityStatus status;
                if (evidence is FinTsOperationEvidence.UnlistedBlocked or FinTsOperationEvidence.Ambiguous or FinTsOperationEvidence.Unknown)
                {
                    status = AccountCapabilityStatus.Suspended;
                    issues.Add($"Account {account.Iban.HeaderText()}: HKSPA v{ad.Version} suspended due to permission status {evidence}.");
                }
                else if (ad.SingleAccountRequestAllowed != true)
                {
                    status = AccountCapabilityStatus.Suspended;
                    issues.Add($"Account {account.Iban.HeaderText()}: HKSPA v{ad.Version} suspended because single-account requests are not advertised.");
                }
                else if (ad.MaximumOrders == 0)
                {
                    status = AccountCapabilityStatus.Suspended;
                    issues.Add($"Account {account.Iban.HeaderText()}: HKSPA v{ad.Version} suspended due to zero order capacity.");
                }
                else if (perm is not null && perm.RequiredSignatures < ad.MinimumSignatures)
                {
                    status = AccountCapabilityStatus.Suspended;
                    issues.Add($"Account {account.Iban.HeaderText()}: HKSPA v{ad.Version} suspended due to signature requirement conflict.");
                }
                else
                {
                    status = AccountCapabilityStatus.Active;
                }

                int effectiveSignatures = perm is not null ? Math.Max(ad.MinimumSignatures, perm.RequiredSignatures) : ad.MinimumSignatures;

                capabilities.Add(new AccountCapability(
                    connection.Id,
                    localAccountId,
                    operationCode: "HKSPA",
                    version: ad.Version,
                    minimumSignatures: effectiveSignatures,
                    maximumOrders: ad.MaximumOrders,
                    status: status,
                    activatedAt: activationTimestamp));
            }
        }

        return FinTsCapabilityActivationResult.Success(capabilities, issues);
    }
}
