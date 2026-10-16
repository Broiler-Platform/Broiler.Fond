using System.Globalization;
using Broiler.Fond.Kernel.Domain.Identifiers;

namespace Broiler.Fond.Kernel.Domain.Accounts;

/// <summary>Status of an advertised FinTS account capability.</summary>
public enum AccountCapabilityStatus
{
    /// <summary>Advertised and permitted for operational use.</summary>
    Active = 1,

    /// <summary>Advertised but blocked, suspended, or lacking required customer permissions.</summary>
    Suspended = 2,

    /// <summary>Superseded by a newer version or no longer advertised.</summary>
    Deprecated = 3,
}

/// <summary>
/// Immutable representation of an operation and protocol version currently advertised
/// for a specific bank connection and optional account.
/// </summary>
public sealed class AccountCapability : IEquatable<AccountCapability>
{
    public const int MinimumOperationCodeLength = 4;
    public const int MaximumOperationCodeLength = 6;
    public const int MaximumSignatures = 9;
    public const int MaximumOrdersLimit = 999;
    public const int MinimumVersion = 1;
    public const int MaximumVersion = 999;

    public ConnectionId ConnectionId { get; }
    public AccountId? AccountId { get; }
    public string OperationCode { get; }
    public int Version { get; }
    public int MinimumSignatures { get; }
    public int MaximumOrders { get; }
    public AccountCapabilityStatus Status { get; }
    public DateTimeOffset ActivatedAt { get; }

    public AccountCapability(
        ConnectionId connectionId,
        AccountId? accountId,
        string operationCode,
        int version,
        int minimumSignatures,
        int maximumOrders,
        AccountCapabilityStatus status,
        DateTimeOffset activatedAt)
    {
        if (connectionId.Value == 0)
        {
            throw new ArgumentException("A nonzero connection ID is required.", nameof(connectionId));
        }

        if (accountId.HasValue && accountId.Value.Value == 0)
        {
            throw new ArgumentException("When specified, account ID must be nonzero.", nameof(accountId));
        }

        ArgumentException.ThrowIfNullOrWhiteSpace(operationCode);

        string trimmedCode = operationCode.Trim();
        if (trimmedCode.Length is < MinimumOperationCodeLength or > MaximumOperationCodeLength ||
            !trimmedCode.All(static c => char.IsAsciiLetterOrDigit(c) && (!char.IsAsciiLetter(c) || char.IsAsciiLetterUpper(c))))
        {
            throw new ArgumentException(
                "Operation code must contain 4 to 6 uppercase ASCII letters or digits.",
                nameof(operationCode));
        }

        if (version is < MinimumVersion or > MaximumVersion)
        {
            throw new ArgumentOutOfRangeException(
                nameof(version),
                version,
                $"Version must be between {MinimumVersion} and {MaximumVersion}.");
        }

        if (minimumSignatures is < 0 or > MaximumSignatures)
        {
            throw new ArgumentOutOfRangeException(
                nameof(minimumSignatures),
                minimumSignatures,
                $"Minimum signatures must be between 0 and {MaximumSignatures}.");
        }

        if (maximumOrders is < 0 or > MaximumOrdersLimit)
        {
            throw new ArgumentOutOfRangeException(
                nameof(maximumOrders),
                maximumOrders,
                $"Maximum orders must be between 0 and {MaximumOrdersLimit}.");
        }

        if (!Enum.IsDefined(status))
        {
            throw new ArgumentOutOfRangeException(nameof(status), status, "A defined capability status is required.");
        }

        ConnectionId = connectionId;
        AccountId = accountId;
        OperationCode = trimmedCode;
        Version = version;
        MinimumSignatures = minimumSignatures;
        MaximumOrders = maximumOrders;
        Status = status;
        ActivatedAt = activatedAt;
    }

    /// <summary>Returns a new capability instance with the updated operational status.</summary>
    public AccountCapability WithStatus(AccountCapabilityStatus newStatus)
    {
        if (newStatus == Status)
        {
            return this;
        }

        return new AccountCapability(
            ConnectionId,
            AccountId,
            OperationCode,
            Version,
            MinimumSignatures,
            MaximumOrders,
            newStatus,
            ActivatedAt);
    }

    public bool Equals(AccountCapability? other)
    {
        if (other is null)
        {
            return false;
        }

        if (ReferenceEquals(this, other))
        {
            return true;
        }

        return ConnectionId == other.ConnectionId &&
               AccountId == other.AccountId &&
               string.Equals(OperationCode, other.OperationCode, StringComparison.Ordinal) &&
               Version == other.Version &&
               MinimumSignatures == other.MinimumSignatures &&
               MaximumOrders == other.MaximumOrders &&
               Status == other.Status &&
               ActivatedAt == other.ActivatedAt;
    }

    public override bool Equals(object? obj) => Equals(obj as AccountCapability);

    public override int GetHashCode()
    {
        var hash = new HashCode();
        hash.Add(ConnectionId);
        hash.Add(AccountId);
        hash.Add(OperationCode, StringComparer.Ordinal);
        hash.Add(Version);
        hash.Add(MinimumSignatures);
        hash.Add(MaximumOrders);
        hash.Add(Status);
        hash.Add(ActivatedAt);
        return hash.ToHashCode();
    }

    public override string ToString()
    {
        string target = AccountId.HasValue
            ? $"Account {AccountId.Value.Value.ToString(CultureInfo.InvariantCulture)}"
            : "Connection";
        return $"{OperationCode}:v{Version.ToString(CultureInfo.InvariantCulture)} [{Status}] ({target}, MinSig={MinimumSignatures.ToString(CultureInfo.InvariantCulture)}, MaxOrd={MaximumOrders.ToString(CultureInfo.InvariantCulture)})";
    }

    public static bool operator ==(AccountCapability? left, AccountCapability? right)
    {
        if (left is null)
        {
            return right is null;
        }

        return left.Equals(right);
    }

    public static bool operator !=(AccountCapability? left, AccountCapability? right) => !(left == right);
}
