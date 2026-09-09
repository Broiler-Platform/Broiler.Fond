using System.Collections.ObjectModel;
using Broiler.Fond.Kernel.Domain.Identifiers;
using Broiler.Fond.Kernel.Domain.Institutions;

namespace Broiler.Fond.Kernel.Domain.Accounts;

/// <summary>One lossless, scoped bank-supplied identifier; duplicates are retained.</summary>
public sealed record BankAccountIdentifier
{
    public BankAccountIdentifier(string issuer, string scope, string kind, string value)
    {
        LocatorInput.Required(issuer);
        LocatorInput.Required(scope);
        LocatorInput.Required(kind);
        LocatorInput.Optional(value);
        ArgumentNullException.ThrowIfNull(value);
        Issuer = issuer;
        Scope = scope;
        Kind = kind;
        Value = value;
    }

    public string Issuer { get; }
    public string Scope { get; }
    public string Kind { get; }
    public string Value { get; }
    public override string ToString() => nameof(BankAccountIdentifier);
}

/// <summary>
/// Exact source identity, separate from local account identity. Raw strings are
/// never trimmed or case-folded. Bank identifiers compare as a multiset, while
/// their original order and multiplicity remain available for serialization.
/// </summary>
public sealed class AccountSourceLocator : IEquatable<AccountSourceLocator>
{
    public const int MaximumBankIdentifiers = 32;
    private readonly BankAccountIdentifier[] _canonicalIdentifiers;

    public AccountSourceLocator(InstitutionId institutionId, ConnectionId connectionId,
        string connector, string connectorVersion, ManualInstitutionConfiguration configuration,
        string? iban, string? domesticAccountNumber, string? domesticBankCode,
        string? subaccount, string? currency, IEnumerable<BankAccountIdentifier>? bankIdentifiers = null)
    {
        if (institutionId.Value == 0 || connectionId.Value == 0 || institutionId.Value == connectionId.Value)
        {
            throw new ArgumentException("Distinct nonzero institution and connection IDs are required.");
        }

        LocatorInput.Required(connector);
        LocatorInput.Required(connectorVersion);
        ArgumentNullException.ThrowIfNull(configuration);
        LocatorInput.Optional(iban);
        LocatorInput.Optional(domesticAccountNumber);
        LocatorInput.Optional(domesticBankCode);
        LocatorInput.Optional(subaccount);
        LocatorInput.Optional(currency);
        InstitutionId = institutionId;
        ConnectionId = connectionId;
        Connector = connector;
        ConnectorVersion = connectorVersion;
        Endpoint = configuration.FinTsEndpoint.AbsoluteUri;
        Iban = iban;
        DomesticAccountNumber = domesticAccountNumber;
        DomesticBankCode = domesticBankCode;
        Subaccount = subaccount;
        Currency = currency;
        List<BankAccountIdentifier> copy = [];
        if (bankIdentifiers is not null)
        {
            foreach (BankAccountIdentifier identifier in bankIdentifiers)
            {
                ArgumentNullException.ThrowIfNull(identifier);
                if (copy.Count == MaximumBankIdentifiers)
                {
                    throw new ArgumentException("Bank identifier limit exceeded.", nameof(bankIdentifiers));
                }

                copy.Add(identifier);
            }
        }

        BankIdentifiers = copy.AsReadOnly();
        _canonicalIdentifiers = copy.OrderBy(static item => item.Issuer, StringComparer.Ordinal)
            .ThenBy(static item => item.Scope, StringComparer.Ordinal)
            .ThenBy(static item => item.Kind, StringComparer.Ordinal)
            .ThenBy(static item => item.Value, StringComparer.Ordinal).ToArray();
        HasConflictingIdentifiers = _canonicalIdentifiers.Zip(_canonicalIdentifiers.Skip(1))
            .Any(static pair => pair.First.Issuer == pair.Second.Issuer && pair.First.Scope == pair.Second.Scope &&
                pair.First.Kind == pair.Second.Kind && pair.First.Value != pair.Second.Value);
    }

    public InstitutionId InstitutionId { get; }
    public ConnectionId ConnectionId { get; }
    public string Connector { get; }
    public string ConnectorVersion { get; }
    public string Endpoint { get; }
    public string? Iban { get; }
    public string? DomesticAccountNumber { get; }
    public string? DomesticBankCode { get; }
    public string? Subaccount { get; }
    public string? Currency { get; }
    public ReadOnlyCollection<BankAccountIdentifier> BankIdentifiers { get; }
    public bool HasConflictingIdentifiers { get; }
    public bool HasAccountIdentifier => !string.IsNullOrWhiteSpace(Iban) ||
        (!string.IsNullOrWhiteSpace(DomesticAccountNumber) && !string.IsNullOrWhiteSpace(DomesticBankCode)) ||
        BankIdentifiers.Any(static item => !string.IsNullOrWhiteSpace(item.Value));

    public bool Equals(AccountSourceLocator? other) => other is not null &&
        InstitutionId == other.InstitutionId && ConnectionId == other.ConnectionId &&
        Connector == other.Connector && ConnectorVersion == other.ConnectorVersion && Endpoint == other.Endpoint &&
        Iban == other.Iban && DomesticAccountNumber == other.DomesticAccountNumber &&
        DomesticBankCode == other.DomesticBankCode && Subaccount == other.Subaccount && Currency == other.Currency &&
        _canonicalIdentifiers.SequenceEqual(other._canonicalIdentifiers);

    public override bool Equals(object? obj) => obj is AccountSourceLocator other && Equals(other);

    public override int GetHashCode()
    {
        HashCode hash = new();
        hash.Add(InstitutionId);
        hash.Add(ConnectionId);
        hash.Add(Connector, StringComparer.Ordinal);
        hash.Add(ConnectorVersion, StringComparer.Ordinal);
        hash.Add(Endpoint, StringComparer.Ordinal);
        hash.Add(Iban, StringComparer.Ordinal);
        hash.Add(DomesticAccountNumber, StringComparer.Ordinal);
        hash.Add(DomesticBankCode, StringComparer.Ordinal);
        hash.Add(Subaccount, StringComparer.Ordinal);
        hash.Add(Currency, StringComparer.Ordinal);
        foreach (BankAccountIdentifier identifier in _canonicalIdentifiers)
        {
            hash.Add(identifier);
        }

        return hash.ToHashCode();
    }

    public override string ToString() => nameof(AccountSourceLocator);
}

internal static class LocatorInput
{
    internal const int MaximumFieldLength = 1024;

    internal static void Required(string value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value);
        Optional(value);
    }

    internal static void Optional(string? value)
    {
        if (value?.Length > MaximumFieldLength)
        {
            throw new ArgumentException("Source locator field limit exceeded.");
        }
    }
}
