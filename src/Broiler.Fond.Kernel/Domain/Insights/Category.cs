namespace Broiler.Fond.Kernel.Domain.Insights;

/// <summary>
/// Domain model for an economic transaction category.
/// </summary>
public sealed record Category(
    ulong Id,
    string Name,
    ulong? ParentCategoryId = null,
    bool IsSystemDefault = false,
    string? ColorHex = null,
    string? Description = null)
{
    public const int MaxNameLength = 64;

    public bool IsSubcategory => ParentCategoryId.HasValue;

    public static Category CreateUserCategory(ulong id, string name, ulong? parentCategoryId = null, string? colorHex = null, string? description = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        string trimmed = name.Trim();
        if (trimmed.Length > MaxNameLength)
        {
            throw new ArgumentException($"Category name exceeds maximum length of {MaxNameLength} characters.", nameof(name));
        }

        return new Category(id, trimmed, parentCategoryId, IsSystemDefault: false, colorHex, description);
    }

    /// <summary>
    /// Static collection of well-known standard consumer homebanking categories.
    /// </summary>
    public static readonly IReadOnlyList<Category> SystemDefaults =
    [
        // Root / Major groups
        new Category(1, "Income", null, true, "#2E7D32", "Salaries, wages, freelance, and other income"),
        new Category(2, "Salary", 1, true, "#388E3C", "Regular employment wage and salary"),
        new Category(3, "Investment Income", 1, true, "#4CAF50", "Dividends, interest, and capital gains"),

        new Category(10, "Housing & Utilities", null, true, "#1565C0", "Rent, mortgage, electricity, and utilities"),
        new Category(11, "Rent & Mortgage", 10, true, "#1976D2", "Monthly rent or loan interest payments"),
        new Category(12, "Utilities", 10, true, "#2196F3", "Electricity, gas, water, heating, and trash collection"),
        new Category(13, "Internet & Telecom", 10, true, "#42A5F5", "Mobile phone, landline, and broadband contracts"),

        new Category(20, "Living & Daily", null, true, "#F57C00", "Food, supermarkets, household supplies, and clothing"),
        new Category(21, "Groceries", 20, true, "#FB8C00", "Supermarkets, bakeries, butcher shops, and groceries"),
        new Category(22, "Household & Supplies", 20, true, "#FFA726", "Hardware, drugstores, cleaning supplies, and furniture"),
        new Category(23, "Clothing & Shoes", 20, true, "#FFB74D", "Apparel, footwear, and accessories"),

        new Category(30, "Mobility & Transport", null, true, "#00838F", "Public transit, vehicle expenses, and fuel"),
        new Category(31, "Public Transit", 30, true, "#0097A7", "Train, bus, subway tickets, and transit passes"),
        new Category(32, "Fuel & Charging", 30, true, "#00ACC1", "Gasoline, diesel, and electric vehicle charging"),
        new Category(33, "Vehicle Maintenance", 30, true, "#26C6DA", "Insurance, tax, repairs, and parking"),

        new Category(40, "Health & Care", null, true, "#C2185B", "Healthcare, pharmacies, and medical expenses"),
        new Category(41, "Pharmacy & Medicine", 40, true, "#D81B60", "Prescriptions and drugstore medications"),
        new Category(42, "Doctors & Therapies", 40, true, "#E91E63", "Medical visits, dental, and treatments"),

        new Category(50, "Leisure & Lifestyle", null, true, "#7B1FA2", "Dining, entertainment, hobbies, and holidays"),
        new Category(51, "Restaurants & Takeaway", 50, true, "#8E24AA", "Dining out, cafes, and delivery food"),
        new Category(52, "Subscriptions & Media", 50, true, "#AB47BC", "Streaming services, magazines, and memberships"),
        new Category(53, "Travel & Holidays", 50, true, "#BA68C8", "Flights, hotels, and vacation expenses"),

        new Category(60, "Finance & Taxes", null, true, "#455A64", "Transfers, insurance, taxes, and bank fees"),
        new Category(61, "Insurance", 60, true, "#546E7A", "Health, liability, home, and legal insurance"),
        new Category(62, "Taxes & Duties", 60, true, "#607D8B", "Income tax, property tax, and municipal fees"),
        new Category(63, "Bank Fees & Interest", 60, true, "#78909C", "Account maintenance and loan interest"),
        new Category(64, "Own-Account Transfer", 60, true, "#90A4AE", "Internal transfer between user accounts"),

        new Category(99, "Uncategorized", null, true, "#757575", "Unclassified transactions awaiting review")
    ];
}
