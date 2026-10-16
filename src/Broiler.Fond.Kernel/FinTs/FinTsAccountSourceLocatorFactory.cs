using Broiler.Fond.Kernel.Domain.Accounts;
using Broiler.Fond.Kernel.Domain.Connections;

namespace Broiler.Fond.Kernel.FinTs;

/// <summary>
/// Translates bank-supplied FinTS account evidence into canonical <see cref="AccountSourceLocator"/>
/// records bound to a specific <see cref="BankConnection"/>.
/// </summary>
public static class FinTsAccountSourceLocatorFactory
{
    public const string ConnectorName = "fints";

    /// <summary>
    /// Creates an <see cref="AccountSourceLocator"/> from a parsed <see cref="FinTsReadAccount"/> observation.
    /// Preserves raw whitespace, letter case, and leading zeros from bank wire strings.
    /// </summary>
    public static AccountSourceLocator CreateLocator(
        BankConnection connection,
        FinTsReadAccount account,
        string? currency = null)
    {
        ArgumentNullException.ThrowIfNull(connection);
        ArgumentNullException.ThrowIfNull(account);

        List<BankAccountIdentifier> identifiers = [];

        if (!string.IsNullOrEmpty(account.Bic))
        {
            identifiers.Add(new BankAccountIdentifier("fints", "account", "bic", account.Bic));
        }

        if (!string.IsNullOrEmpty(account.Country))
        {
            identifiers.Add(new BankAccountIdentifier("fints", "account", "country", account.Country));
        }

        string? iban = string.IsNullOrEmpty(account.Iban) ? null : account.Iban;
        string? domesticAccountNumber = string.IsNullOrEmpty(account.Number) ? null : account.Number;
        string? domesticBankCode = string.IsNullOrEmpty(account.Institution) ? null : account.Institution;
        string? subaccount = string.IsNullOrEmpty(account.Subaccount) ? null : account.Subaccount;
        string? curr = string.IsNullOrEmpty(currency) ? null : currency;

        return new AccountSourceLocator(
            institutionId: connection.InstitutionId,
            connectionId: connection.Id,
            connector: ConnectorName,
            connectorVersion: connection.ConnectorVersion,
            configuration: connection.InstitutionConfiguration,
            iban: iban,
            domesticAccountNumber: domesticAccountNumber,
            domesticBankCode: domesticBankCode,
            subaccount: subaccount,
            currency: curr,
            bankIdentifiers: identifiers.Count > 0 ? identifiers : null);
    }

    /// <summary>
    /// Creates an <see cref="AccountSourceLocator"/> from user parameter data (<see cref="FinTsAccountParameters"/> / HIUPD).
    /// </summary>
    public static AccountSourceLocator CreateLocator(
        BankConnection connection,
        FinTsAccountParameters account)
    {
        ArgumentNullException.ThrowIfNull(connection);
        ArgumentNullException.ThrowIfNull(account);

        string? iban = account.Iban.IsEmpty ? null : account.Iban.HeaderText();
        string? curr = (account.Currency is null || account.Currency.IsEmpty) ? null : account.Currency.HeaderText();
        string? domesticAccountNumber = null;
        string? subaccount = null;
        string? domesticBankCode = null;
        List<BankAccountIdentifier> identifiers = [];

        if (account.HasAccountConnection)
        {
            var elements = account.AccountConnection.Elements;
            if (elements.Count > 0 && !elements[0].IsEmpty)
            {
                domesticAccountNumber = elements[0].HeaderText();
            }

            if (elements.Count > 1 && !elements[1].IsEmpty)
            {
                subaccount = elements[1].HeaderText();
            }

            if (elements.Count > 2 && !elements[2].IsEmpty)
            {
                string country = elements[2].HeaderText();
                identifiers.Add(new BankAccountIdentifier("fints", "account", "country", country));
            }

            if (elements.Count > 3 && !elements[3].IsEmpty)
            {
                domesticBankCode = elements[3].HeaderText();
            }
        }

        if (!account.CustomerId.IsEmpty)
        {
            identifiers.Add(new BankAccountIdentifier("fints", "account", "customer_id", account.CustomerId.HeaderText()));
        }

        return new AccountSourceLocator(
            institutionId: connection.InstitutionId,
            connectionId: connection.Id,
            connector: ConnectorName,
            connectorVersion: connection.ConnectorVersion,
            configuration: connection.InstitutionConfiguration,
            iban: iban,
            domesticAccountNumber: domesticAccountNumber,
            domesticBankCode: domesticBankCode,
            subaccount: subaccount,
            currency: curr,
            bankIdentifiers: identifiers.Count > 0 ? identifiers : null);
    }
}
