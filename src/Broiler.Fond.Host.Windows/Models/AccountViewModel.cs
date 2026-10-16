using System.Globalization;
using Broiler.Fond.Kernel.Storage;

namespace Broiler.Fond.Host.Windows.Models;

/// <summary>
/// Immutable, accessible view model representing an account, its binding state, and truthful cached balances.
/// Invariant: Respects M1-07 strict multi-currency isolation; balances in different currencies are never summed.
/// </summary>
public sealed class AccountViewModel
{
    public ulong AccountId { get; }
    public ulong? BindingId { get; }
    public ulong? IncarnationId { get; }
    public ulong? ConnectionId { get; }
    public string AccountName { get; }
    public string AccountHolder { get; }
    public string AccountType { get; }
    public string AccountTypeDisplay { get; }
    public string LocatorDisplay { get; }
    public string Currency { get; }
    public string BindingState { get; }
    public string BindingStateDisplay { get; }
    public bool IsSupportedPaymentAccount { get; }

    public decimal? BookedAmount { get; }
    public string BookedCurrency { get; }
    public string BookedBalanceDisplay { get; }
    public DateTimeOffset? BookedAtUtc { get; }
    public string BookedAtDisplay { get; }

    public decimal? AvailableAmount { get; }
    public string AvailableCurrency { get; }
    public string AvailableBalanceDisplay { get; }
    public DateTimeOffset? AvailableAtUtc { get; }
    public string AvailableAtDisplay { get; }

    public DateTimeOffset? RetrievedAtUtc { get; }
    public string FreshnessDisplay { get; }
    public bool IsStale { get; }
    public bool IsOffline { get; }

    public AccountViewModel(
        StoreAccountEntry account,
        StoreBindingEntry? binding = null,
        StoreBalanceEntry? bookedBalance = null,
        StoreBalanceEntry? availableBalance = null,
        DateTimeOffset? asOfUtc = null,
        bool isOffline = false,
        string? accountType = null,
        string? accountHolder = null)
    {
        ArgumentNullException.ThrowIfNull(account);

        AccountId = account.Id;
        BindingId = binding?.Id;
        IncarnationId = binding?.IncarnationId;
        ConnectionId = binding?.ConnectionId;
        AccountName = string.IsNullOrWhiteSpace(account.DisplayName) ? $"Account #{account.Id}" : account.DisplayName;
        AccountHolder = string.IsNullOrWhiteSpace(accountHolder) ? "[Not Provided]" : accountHolder;

        string effectiveType = accountType ?? (string.Equals(account.Currency, "EUR", StringComparison.OrdinalIgnoreCase) ? "Payment" : "Identity-only");
        AccountType = effectiveType;
        Currency = binding?.Currency ?? account.Currency ?? "EUR";
        IsOffline = isOffline;

        // M1-05 & M1-07: Payment/Current accounts (Girokonto) are fully certified; other types are identity-only shells
        IsSupportedPaymentAccount = effectiveType.Equals("Payment", StringComparison.OrdinalIgnoreCase) ||
                                     effectiveType.Equals("Current", StringComparison.OrdinalIgnoreCase) ||
                                     effectiveType.Equals("Giro", StringComparison.OrdinalIgnoreCase);

        AccountTypeDisplay = IsSupportedPaymentAccount
            ? "Payment / Current (Girokonto)"
            : $"{effectiveType} [Identity-only Shell]";

        if (binding is not null)
        {
            if (!string.IsNullOrWhiteSpace(binding.Iban))
            {
                LocatorDisplay = $"IBAN: {binding.Iban}";
            }
            else if (!string.IsNullOrWhiteSpace(binding.DomesticAccountNumber))
            {
                LocatorDisplay = $"Account: {binding.DomesticAccountNumber} (BLZ: {binding.DomesticBankCode})";
            }
            else
            {
                LocatorDisplay = "[No Bound Locator]";
            }
        }
        else
        {
            LocatorDisplay = "[No Bound Locator]";
        }

        BindingState = binding?.State ?? "Unbound";
        BindingStateDisplay = binding?.State switch
        {
            "Active" => "[✓ Active]",
            "Quarantined" => "[⚠ Quarantined Candidate]",
            "Disabled" => "[✕ Disabled]",
            _ => "[? Unbound]"
        };

        // Booked balance
        if (bookedBalance is not null && decimal.TryParse(bookedBalance.Amount, NumberStyles.Number, CultureInfo.InvariantCulture, out decimal bookedAmt))
        {
            BookedAmount = bookedAmt;
            BookedCurrency = bookedBalance.Currency ?? Currency;
            BookedBalanceDisplay = FormatMoney(bookedAmt, BookedCurrency);
            BookedAtUtc = bookedBalance.BankTimestamp;
            BookedAtDisplay = bookedBalance.BankTimestamp?.ToString("yyyy-MM-dd HH:mm UTC", CultureInfo.InvariantCulture) ?? "[Reported Without Timestamp]";
            RetrievedAtUtc = bookedBalance.RetrievedAt;
        }
        else
        {
            BookedAmount = null;
            BookedCurrency = Currency;
            BookedBalanceDisplay = "[No Balance Recorded]";
            BookedAtUtc = null;
            BookedAtDisplay = "[Never]";
        }

        // Available balance
        if (availableBalance is not null && decimal.TryParse(availableBalance.Amount, NumberStyles.Number, CultureInfo.InvariantCulture, out decimal availAmt))
        {
            AvailableAmount = availAmt;
            AvailableCurrency = availableBalance.Currency ?? Currency;
            AvailableBalanceDisplay = FormatMoney(availAmt, AvailableCurrency);
            AvailableAtUtc = availableBalance.BankTimestamp;
            AvailableAtDisplay = availableBalance.BankTimestamp?.ToString("yyyy-MM-dd HH:mm UTC", CultureInfo.InvariantCulture) ?? "[Same as Booked]";
            if (!RetrievedAtUtc.HasValue)
            {
                RetrievedAtUtc = availableBalance.RetrievedAt;
            }
        }
        else
        {
            AvailableAmount = null;
            AvailableCurrency = Currency;
            AvailableBalanceDisplay = "[Unavailable]";
            AvailableAtUtc = null;
            AvailableAtDisplay = "[Not Reported by Bank]";
        }

        // Freshness determination
        if (RetrievedAtUtc.HasValue)
        {
            DateTimeOffset now = asOfUtc ?? DateTimeOffset.UtcNow;
            TimeSpan age = now - RetrievedAtUtc.Value;

            if (isOffline)
            {
                IsStale = true;
                FreshnessDisplay = $"[Offline - Cached {FormatAge(age)} ago]";
            }
            else if (age <= TimeSpan.FromHours(1))
            {
                IsStale = false;
                FreshnessDisplay = "[Fresh - < 1h ago]";
            }
            else if (age <= TimeSpan.FromHours(24))
            {
                IsStale = false;
                FreshnessDisplay = $"[Recent - {FormatAge(age)} ago]";
            }
            else
            {
                IsStale = true;
                FreshnessDisplay = $"[Stale - {FormatAge(age)} ago]";
            }
        }
        else
        {
            IsStale = true;
            FreshnessDisplay = isOffline ? "[Offline - No Cache]" : "[No Data]";
        }
    }

    /// <summary>
    /// Computes total booked balances aggregated strictly per ISO currency code.
    /// Invariant: Balances of different currencies are never combined or converted.
    /// </summary>
    public static IReadOnlyDictionary<string, decimal> ComputeMultiCurrencyTotals(IEnumerable<AccountViewModel> accounts)
    {
        ArgumentNullException.ThrowIfNull(accounts);

        var totals = new Dictionary<string, decimal>(StringComparer.OrdinalIgnoreCase);

        foreach (var account in accounts)
        {
            if (account.BookedAmount.HasValue && !string.IsNullOrWhiteSpace(account.BookedCurrency))
            {
                string curr = account.BookedCurrency.Trim().ToUpperInvariant();
                if (totals.TryGetValue(curr, out decimal existing))
                {
                    totals[curr] = existing + account.BookedAmount.Value;
                }
                else
                {
                    totals[curr] = account.BookedAmount.Value;
                }
            }
        }

        return totals;
    }

    public static string FormatMoney(decimal amount, string currency)
    {
        string curr = string.IsNullOrWhiteSpace(currency) ? "EUR" : currency.Trim().ToUpperInvariant();
        return $"{amount.ToString("N2", CultureInfo.CurrentCulture)} {curr}";
    }

    private static string FormatAge(TimeSpan age)
    {
        if (age < TimeSpan.Zero) { return "just now"; }
        if (age.TotalMinutes < 60) { return $"{(int)age.TotalMinutes}m"; }
        if (age.TotalHours < 24) { return $"{(int)age.TotalHours}h"; }
        return $"{(int)age.TotalDays}d";
    }
}
