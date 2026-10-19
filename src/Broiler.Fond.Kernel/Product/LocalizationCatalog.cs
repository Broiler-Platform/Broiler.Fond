using System.Globalization;

namespace Broiler.Fond.Kernel.Product;

/// <summary>
/// Supported user interface and disclosure languages.
/// </summary>
public enum ApplicationLanguage
{
    German,
    English
}

/// <summary>
/// Centralized bilingual string resources and culture-aware formatting adhering to BFSG § 17 consumer transparency requirements.
/// Invariant: Exact decimal money and ISO currency codes are preserved across all language representations.
/// </summary>
public static class LocalizationCatalog
{
    private static readonly Dictionary<string, (string De, string En)> Catalog = new(StringComparer.OrdinalIgnoreCase)
    {
        // Navigation & tabs
        ["NavAccounts"] = ("Konten & Salden", "Accounts & Balances"),
        ["NavTransactions"] = ("Umsätze", "Transactions"),
        ["NavInsights"] = ("Finanzübersicht & Budgets", "Insights & Budgets"),
        ["NavPayments"] = ("Zahlungsverkehr", "Payments"),

        // Action buttons
        ["BtnUnlock"] = ("Profil entsperren", "Unlock Profile"),
        ["BtnLock"] = ("Profil sperren", "Lock Profile"),
        ["BtnRefresh"] = ("Aktualisieren", "Refresh"),
        ["BtnTransfer"] = ("Neue Überweisung", "New Transfer"),
        ["BtnStandingOrders"] = ("Daueraufträge", "Standing Orders"),
        ["BtnDirectDebitReturn"] = ("Lastschrift zurückgeben", "Return Direct Debit"),
        ["BtnExportCsv"] = ("Als CSV exportieren", "Export to CSV"),
        ["BtnRules"] = ("Kategorisierungsregeln", "Categorization Rules"),
        ["BtnForecast"] = ("Liquiditätsprognose", "Cash-Flow Forecast"),
        ["BtnSplit"] = ("Umsatz aufteilen", "Split Transaction"),
        ["BtnSave"] = ("Speichern", "Save"),
        ["BtnCancel"] = ("Abbrechen", "Cancel"),

        // Dialog titles
        ["TitleProfileUnlock"] = ("Profil entsperren", "Unlock Profile"),
        ["TitlePinPrompt"] = ("Bank-PIN eingeben", "Enter Banking PIN"),
        ["TitleScaChallenge"] = ("Starke Kundenauthentifizierung (SCA)", "Strong Customer Authentication (SCA)"),
        ["TitlePaymentInitiation"] = ("SEPA-Überweisung erstellen", "Create SEPA Credit Transfer"),
        ["TitlePaymentReview"] = ("Zahlung prüfen und freigeben", "Review and Authorize Payment"),
        ["TitleStandingOrders"] = ("Daueraufträge verwalten", "Manage Standing Orders"),
        ["TitleDirectDebitReturn"] = ("Lastschrift-Widerspruch", "Direct-Debit Return"),
        ["TitleRules"] = ("Kategorisierungsregeln & Vorschau", "Categorization Rules & Live Preview"),
        ["TitleForecast"] = ("Liquiditätsprognose [Nur Schätzungen]", "Cash-Flow Forecast [Estimates Only]"),
        ["TitleSplit"] = ("Umsatz aufteilen", "Split Transaction"),
        ["TitleSupportExport"] = ("Diagnosepaket exportieren", "Export Support Bundle"),
        ["TitleAbout"] = ("Über Broiler Fond", "About Broiler Fond"),

        // Disclosures & legal notices
        ["NoticePureUserOperated"] = (
            "Broiler Fond ist eine rein lokal betriebene Software. Es werden keine Daten an Broiler-Server übertragen.",
            "Broiler Fond is purely user-operated software. No data is transferred to any Broiler-operated servers."),
        ["NoticeBfsgCompliance"] = (
            "Barrierefreiheitserklärung: Diese Software erfüllt die Vorgaben des BFSG und der BFSGV für Bankdienstleistungen.",
            "Accessibility Declaration: This software complies with statutory BFSG and BFSGV accessibility requirements for banking services."),
        ["NoticeEstimatePrognosis"] = (
            "[Prognose / Schätzung] Zukünftige Salden basieren auf wiederkehrenden Posten und stellen keine Bankgarantie dar.",
            "[Estimate / Prognosis] Future balances are projected from recurring items and do not represent a guaranteed balance."),
    };

    /// <summary>
    /// Retrieves localized text by key for the requested language, falling back to English if missing.
    /// </summary>
    public static string GetText(string key, ApplicationLanguage language = ApplicationLanguage.German)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        if (Catalog.TryGetValue(key, out var pair))
        {
            return language == ApplicationLanguage.German ? pair.De : pair.En;
        }

        return key;
    }

    /// <summary>
    /// Formats an exact decimal amount with ISO currency code according to cultural formatting conventions.
    /// </summary>
    public static string FormatMoney(decimal amount, string currency, ApplicationLanguage language = ApplicationLanguage.German)
    {
        CultureInfo culture = language == ApplicationLanguage.German
            ? CultureInfo.GetCultureInfo("de-DE")
            : CultureInfo.GetCultureInfo("en-US");

        return $"{amount.ToString("N2", culture)} {currency}";
    }

    /// <summary>
    /// Creates an accessible dual-cue status badge with mandatory non-color-only text indicator.
    /// </summary>
    public static string FormatStatusBadge(string label, bool isSuccess = false, bool isWarning = false, bool isError = false)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(label);

        if (isError)
        {
            return $"[✕ {label}]";
        }

        if (isWarning)
        {
            return $"[⚠ {label}]";
        }

        if (isSuccess)
        {
            return $"[✓ {label}]";
        }

        return $"[{label}]";
    }
}
