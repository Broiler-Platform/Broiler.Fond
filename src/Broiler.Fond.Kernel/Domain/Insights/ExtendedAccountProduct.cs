using Broiler.Fond.Kernel.Domain.Identifiers;

namespace Broiler.Fond.Kernel.Domain.Insights;

/// <summary>
/// Encapsulates an account product presentation model with extended read-only and offline
/// classification without conflating payment-account or bank-sync semantics.
/// </summary>
public sealed class ExtendedAccountProduct
{
    public ExtendedAccountProduct(
        AccountId id,
        string displayName,
        string currency,
        AccountProductCategory productCategory,
        bool isBankSynced = true,
        bool isReadOnly = false,
        bool canInitiatePayments = false,
        Money? bookedBalance = null,
        Money? availableBalance = null,
        Money? creditLine = null,
        DateTimeOffset? lastRefreshedAt = null)
    {
        if (id.Value == 0)
        {
            throw new ArgumentException("A nonzero account ID is required.", nameof(id));
        }

        ArgumentException.ThrowIfNullOrWhiteSpace(displayName);
        if (!CurrencyCatalog.IsRecognized(currency))
        {
            throw new ArgumentException("A recognized ISO currency is required.", nameof(currency));
        }

        if (!Enum.IsDefined(productCategory))
        {
            throw new ArgumentException("A valid account product category is required.", nameof(productCategory));
        }

        // Non-conflation guard: Only bank-synced Current accounts can ever initiate payments
        if (canInitiatePayments && productCategory != AccountProductCategory.Current)
        {
            throw new InvalidOperationException(
                $"Payment initiation cannot be enabled for {productCategory} accounts; only current accounts may initiate money movement.");
        }

        // Offline guard: Manual cash accounts are never bank-synced
        if (productCategory == AccountProductCategory.ManualCash && isBankSynced)
        {
            throw new InvalidOperationException("Manual cash accounts cannot be bank-synced.");
        }

        // Savings, Loans, Depots and Manual Cash are always read-only in terms of bank payment movement
        if (productCategory is AccountProductCategory.Savings or AccountProductCategory.Loan or AccountProductCategory.ManualCash or AccountProductCategory.Depot)
        {
            isReadOnly = true;
        }

        if (bookedBalance is not null && bookedBalance.Currency != currency)
        {
            throw new ArgumentException("Booked balance currency must match account currency.", nameof(bookedBalance));
        }

        if (availableBalance is not null && availableBalance.Currency != currency)
        {
            throw new ArgumentException("Available balance currency must match account currency.", nameof(availableBalance));
        }

        if (creditLine is not null && creditLine.Currency != currency)
        {
            throw new ArgumentException("Credit line currency must match account currency.", nameof(creditLine));
        }

        Id = id;
        DisplayName = displayName;
        Currency = currency;
        ProductCategory = productCategory;
        IsBankSynced = isBankSynced;
        IsReadOnly = isReadOnly;
        CanInitiatePayments = canInitiatePayments;
        BookedBalance = bookedBalance;
        AvailableBalance = availableBalance;
        CreditLine = creditLine;
        LastRefreshedAt = lastRefreshedAt;
    }

    public AccountId Id { get; }
    public string DisplayName { get; }
    public string Currency { get; }
    public AccountProductCategory ProductCategory { get; }
    public bool IsBankSynced { get; }
    public bool IsReadOnly { get; }
    public bool CanInitiatePayments { get; }
    public Money? BookedBalance { get; }
    public Money? AvailableBalance { get; }
    public Money? CreditLine { get; }
    public DateTimeOffset? LastRefreshedAt { get; }

    /// <summary>
    /// Gets whether this account is an offline manual tracking account.
    /// </summary>
    public bool IsOfflineManual => ProductCategory == AccountProductCategory.ManualCash || !IsBankSynced;

    /// <summary>
    /// Classifies the account's current booked balance as Asset or Liability.
    /// </summary>
    public AccountClassificationKind Classification
    {
        get
        {
            if (ProductCategory == AccountProductCategory.Loan)
            {
                return AccountClassificationKind.Liability;
            }

            if (ProductCategory is AccountProductCategory.Savings or AccountProductCategory.ManualCash or AccountProductCategory.Depot)
            {
                return AccountClassificationKind.Asset;
            }

            // For Current, CreditCard, Other: balance < 0 is liability, balance >= 0 is asset
            decimal amount = BookedBalance?.Amount ?? 0m;
            return amount < 0m ? AccountClassificationKind.Liability : AccountClassificationKind.Asset;
        }
    }
}
