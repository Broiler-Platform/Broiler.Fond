using System.Globalization;
using Broiler.Fond.Kernel.Domain;
using Broiler.Fond.Kernel.Domain.Accounts;
using Broiler.Fond.Kernel.Domain.Identifiers;
using Broiler.Fond.Kernel.Domain.Institutions;

namespace Broiler.Fond.Kernel.FinTs;

/// <summary>
/// FinTS 4.1 discovered account structure extracted from HISPA v3.
/// </summary>
public sealed record FinTs41DiscoveredAccount(
    string Iban,
    string? Bic,
    string? AccountNumber,
    string? SubAccount,
    string Currency,
    string? HolderName,
    string? AccountType)
{
    public AccountSourceLocator ToSourceLocator(
        InstitutionId institutionId,
        ConnectionId connectionId,
        ManualInstitutionConfiguration configuration)
    {
        var identifiers = new List<BankAccountIdentifier>();
        if (!string.IsNullOrEmpty(Bic))
        {
            identifiers.Add(new BankAccountIdentifier("fints", "account", "bic", Bic));
        }

        return new AccountSourceLocator(
            institutionId: institutionId,
            connectionId: connectionId,
            connector: "fints",
            connectorVersion: "FinTS-4.1",
            configuration: configuration,
            iban: Iban,
            domesticAccountNumber: AccountNumber,
            domesticBankCode: null,
            subaccount: SubAccount,
            currency: Currency,
            bankIdentifiers: identifiers.Count > 0 ? identifiers : null);
    }
}

/// <summary>
/// FinTS 4.1 account balance snapshot extracted from HISAL v8.
/// </summary>
public sealed record FinTs41BalanceInfo(
    string Iban,
    Money BookedBalance,
    Money? AvailableBalance,
    Money? CreditLine,
    DateTimeOffset BankTimestamp);

/// <summary>
/// FinTS 4.1 transaction parsed from embedded CAMT statement (HICAZ).
/// </summary>
public sealed record FinTs41CamtTransaction(
    string? EntryReference,
    string? EndToEndId,
    string? Uetr,
    Money Amount,
    bool IsCredit,
    DateOnly BookingDate,
    DateOnly? ValueDate,
    string? CounterpartyName,
    string? CounterpartyIban,
    string? RemittanceInformation);

/// <summary>
/// Builders and parsers for FinTS 4.1 business operations (account discovery, balances, CAMT statements, and SEPA transfers).
/// </summary>
public static class FinTs41AccountOperations
{
    public static FinTs41Segment BuildHkspaRequest(int segNo = 1)
    {
        return new FinTs41Segment("HKSPA", 3, segNo,
        [
            FinTs41Element.Leaf("allAccounts", "true")
        ]);
    }

    public static IReadOnlyList<FinTs41DiscoveredAccount> ParseHispaResponse(FinTs41Segment segment)
    {
        ArgumentNullException.ThrowIfNull(segment);
        if (!string.Equals(segment.Tag, "HISPA", StringComparison.OrdinalIgnoreCase))
        {
            return Array.Empty<FinTs41DiscoveredAccount>();
        }

        var accounts = new List<FinTs41DiscoveredAccount>();

        string iban = segment.GetElementValue("iban") ?? string.Empty;
        string? bic = segment.GetElementValue("bic");
        string? acctNum = segment.GetElementValue("accountNumber");
        string? subAcct = segment.GetElementValue("subAccount");
        string ccy = segment.GetElementValue("currency") ?? "EUR";
        string? holder = segment.GetElementValue("holderName");
        string? acctType = segment.GetElementValue("accountType");

        if (!string.IsNullOrEmpty(iban))
        {
            accounts.Add(new FinTs41DiscoveredAccount(iban, bic, acctNum, subAcct, ccy, holder, acctType));
        }

        return accounts;
    }

    public static FinTs41Segment BuildHksalRequest(int segNo, string iban, string? bic = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(iban);

        var elements = new List<FinTs41Element>
        {
            FinTs41Element.Leaf("iban", iban)
        };

        if (!string.IsNullOrWhiteSpace(bic))
        {
            elements.Add(FinTs41Element.Leaf("bic", bic));
        }

        return new FinTs41Segment("HKSAL", 8, segNo, elements);
    }

    public static FinTs41BalanceInfo? ParseHisalResponse(FinTs41Segment segment)
    {
        ArgumentNullException.ThrowIfNull(segment);
        if (!string.Equals(segment.Tag, "HISAL", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        string iban = segment.GetElementValue("iban") ?? string.Empty;
        string ccy = segment.GetElementValue("currency") ?? "EUR";

        string bookedStr = segment.GetElementValue("bookedBalance") ?? "0.00";
        decimal bookedAmount = decimal.Parse(bookedStr, CultureInfo.InvariantCulture);
        var bookedMoney = new Money(bookedAmount, ccy);

        Money? availMoney = null;
        string? availStr = segment.GetElementValue("availableBalance");
        if (!string.IsNullOrEmpty(availStr) && decimal.TryParse(availStr, CultureInfo.InvariantCulture, out decimal availAmount))
        {
            availMoney = new Money(availAmount, ccy);
        }

        Money? creditMoney = null;
        string? creditStr = segment.GetElementValue("creditLine");
        if (!string.IsNullOrEmpty(creditStr) && decimal.TryParse(creditStr, CultureInfo.InvariantCulture, out decimal creditAmount))
        {
            creditMoney = new Money(creditAmount, ccy);
        }

        string? tsRaw = segment.GetElementValue("timestamp");
        DateTimeOffset timestamp = DateTimeOffset.TryParse(tsRaw, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var parsedTs)
            ? parsedTs
            : DateTimeOffset.UtcNow;

        return new FinTs41BalanceInfo(iban, bookedMoney, availMoney, creditMoney, timestamp);
    }

    public static IReadOnlyList<FinTs41CamtTransaction> ParseHicazResponse(FinTs41Segment segment)
    {
        ArgumentNullException.ThrowIfNull(segment);
        if (!string.Equals(segment.Tag, "HICAZ", StringComparison.OrdinalIgnoreCase))
        {
            return Array.Empty<FinTs41CamtTransaction>();
        }

        var list = new List<FinTs41CamtTransaction>();

        string? refId = segment.GetElementValue("entryReference");
        string? endToEnd = segment.GetElementValue("endToEndId");
        string? uetr = segment.GetElementValue("uetr");

        string amtStr = segment.GetElementValue("amount") ?? "0.00";
        decimal amount = decimal.Parse(amtStr, CultureInfo.InvariantCulture);
        string ccy = segment.GetElementValue("currency") ?? "EUR";
        var money = new Money(amount, ccy);

        string indicator = segment.GetElementValue("creditDebitIndicator") ?? "CRDT";
        bool isCredit = string.Equals(indicator, "CRDT", StringComparison.OrdinalIgnoreCase);

        string? bookDateRaw = segment.GetElementValue("bookingDate");
        DateOnly bookingDate = !string.IsNullOrEmpty(bookDateRaw) && DateOnly.TryParse(bookDateRaw, CultureInfo.InvariantCulture, out var bDate)
            ? bDate
            : DateOnly.FromDateTime(DateTime.UtcNow);

        DateOnly? valDate = null;
        string? valDateRaw = segment.GetElementValue("valueDate");
        if (!string.IsNullOrEmpty(valDateRaw) && DateOnly.TryParse(valDateRaw, CultureInfo.InvariantCulture, out var vDate))
        {
            valDate = vDate;
        }

        string? cpName = segment.GetElementValue("counterpartyName");
        string? cpIban = segment.GetElementValue("counterpartyIban");
        string? remittance = segment.GetElementValue("remittanceInformation");

        list.Add(new FinTs41CamtTransaction(
            refId,
            endToEnd,
            uetr,
            money,
            isCredit,
            bookingDate,
            valDate,
            cpName,
            cpIban,
            remittance));

        return list;
    }

    public static FinTs41Segment BuildHkccsRequest(
        int segNo,
        string sourceIban,
        string recipientIban,
        string recipientName,
        Money amount,
        string remittanceInfo,
        string? endToEndId = null,
        bool vopChecked = true)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sourceIban);
        ArgumentException.ThrowIfNullOrWhiteSpace(recipientIban);
        ArgumentException.ThrowIfNullOrWhiteSpace(recipientName);

        var elements = new List<FinTs41Element>
        {
            FinTs41Element.Leaf("sourceIban", sourceIban),
            FinTs41Element.Leaf("recipientIban", recipientIban),
            FinTs41Element.Leaf("recipientName", recipientName),
            FinTs41Element.Leaf("amount", amount.Amount.ToString(CultureInfo.InvariantCulture)),
            FinTs41Element.Leaf("currency", amount.Currency),
            FinTs41Element.Leaf("remittanceInformation", remittanceInfo),
            FinTs41Element.Leaf("vopChecked", vopChecked ? "true" : "false")
        };

        if (!string.IsNullOrWhiteSpace(endToEndId))
        {
            elements.Add(FinTs41Element.Leaf("endToEndId", endToEndId));
        }

        return new FinTs41Segment("HKCCS", 2, segNo, elements);
    }
}
