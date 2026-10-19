using Broiler.Fond.Kernel.Product;

namespace Broiler.Fond.Kernel.Tests;

public static class AccessibilityAndLocalizationTests
{
    public static void Run()
    {
        Console.WriteLine("Running Accessibility & Localization verification (M7-04)...");
        int checks = 0;

        void Assert(bool condition, string message)
        {
            if (!condition)
            {
                throw new InvalidOperationException($"Accessibility & Localization check failed: {message}");
            }
            checks++;
        }

        // 1. Bilingual string resources
        string navAccountsDe = LocalizationCatalog.GetText("NavAccounts", ApplicationLanguage.German);
        string navAccountsEn = LocalizationCatalog.GetText("NavAccounts", ApplicationLanguage.English);
        Assert(navAccountsDe == "Konten & Salden", "German accounts navigation string matches.");
        Assert(navAccountsEn == "Accounts & Balances", "English accounts navigation string matches.");

        string noticeBfsgDe = LocalizationCatalog.GetText("NoticeBfsgCompliance", ApplicationLanguage.German);
        string noticeBfsgEn = LocalizationCatalog.GetText("NoticeBfsgCompliance", ApplicationLanguage.English);
        Assert(noticeBfsgDe.Contains("BFSG") && noticeBfsgDe.Contains("BFSGV"), "German BFSG notice contains statutory references.");
        Assert(noticeBfsgEn.Contains("accessibility requirements"), "English BFSG notice contains accessibility reference.");

        // Fallback for unmapped key
        Assert(LocalizationCatalog.GetText("UnmappedKey123") == "UnmappedKey123", "Unmapped key returns key text.");

        // 2. Cultural exact money formatting
        decimal testAmount = 1234567.89m;
        string formattedDe = LocalizationCatalog.FormatMoney(testAmount, "EUR", ApplicationLanguage.German);
        string formattedEn = LocalizationCatalog.FormatMoney(testAmount, "EUR", ApplicationLanguage.English);

        // In German: dot is thousand separator, comma is decimal: 1.234.567,89 EUR
        Assert(formattedDe.Contains("1.234.567,89") && formattedDe.EndsWith("EUR"), $"German money format matches (got {formattedDe}).");
        // In English: comma is thousand separator, dot is decimal: 1,234,567.89 EUR
        Assert(formattedEn.Contains("1,234,567.89") && formattedEn.EndsWith("EUR"), $"English money format matches (got {formattedEn}).");

        // 3. Dual visual cues for status badges (never relying solely on color)
        string activeBadge = LocalizationCatalog.FormatStatusBadge("Active", isSuccess: true);
        string staleBadge = LocalizationCatalog.FormatStatusBadge("Stale", isWarning: true);
        string blockedBadge = LocalizationCatalog.FormatStatusBadge("Blocked", isError: true);

        Assert(activeBadge == "[✓ Active]", "Active badge uses [✓ Active].");
        Assert(staleBadge == "[⚠ Stale]", "Stale badge uses [⚠ Stale].");
        Assert(blockedBadge == "[✕ Blocked]", "Blocked badge uses [✕ Blocked].");

        Console.WriteLine($"Accessibility & Localization (M7-04): {checks} checks completed successfully.");
    }
}
