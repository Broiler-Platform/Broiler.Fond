using System.Globalization;
using System.Text.RegularExpressions;

namespace Broiler.Fond.Kernel.Domain.Insights;

/// <summary>
/// Deterministic normalizer for counterparty names and payment processor noise.
/// Strips gateway prefixes (PayPal, SumUp, Klarna, etc.), terminal tags, and store branch markers.
/// </summary>
public static class MerchantNormalizer
{
    private static readonly Regex GatewayPrefixRegex = new(
        @"^(PAYPAL\s*\*?|PP\.\*|SUMUP\s*\*?|KLARNA\s*\*?|STRIPE\s*\*?|ADYEN\s*\*?|IZETTLE\s*\*?|GOOGLE\s*\*?|APPLE\.COM\/BILL\s*\*?|AMZN\s*Mktp\s*\*?|AMAZON\s*PAYMENTS\s*\*?|AMAZON\.DE\s*\*?|SQUAREUP\s*\*?|SQ\s*\*?)\s*",
        RegexOptions.Compiled | RegexOptions.IgnoreCase,
        TimeSpan.FromMilliseconds(200));

    private static readonly Regex TerminalNoiseRegex = new(
        @"\b(FILIALE\s*\d+|FIL\.\s*\d+|MARKT\s*\d+|MARKET\s*\d+|STORE\s*\d+|NR\.?\s*\d+|SAGT\s+DANKE|BEDANKT\s+SICH|EC\s+\d+|POS\s+\d+)\b",
        RegexOptions.Compiled | RegexOptions.IgnoreCase,
        TimeSpan.FromMilliseconds(200));

    private static readonly Regex TrailingLocationCodeRegex = new(
        @"(?<=\S\s+)\b([A-ZÄÖÜa-zäöüß]{2,})\s+(DE|AT|CH|LU|NL|FR|BE|IE|US|GB)\b|(?<=\S\s+)\b(DE|AT|CH|LU|NL|FR|BE|IE|US|GB)$",
        RegexOptions.Compiled | RegexOptions.IgnoreCase,
        TimeSpan.FromMilliseconds(200));

    private static readonly Regex TrailingNumericReferenceRegex = new(
        @"\s+(?=[0-9A-Z]*\d)[0-9A-Z]{4,}\b|\s+\d{4,}\b",
        RegexOptions.Compiled | RegexOptions.IgnoreCase,
        TimeSpan.FromMilliseconds(200));

    private static readonly HashSet<string> PreservedAcronyms = new(StringComparer.OrdinalIgnoreCase)
    {
        "DB", "BVG", "HVV", "RMV", "VBB", "MVG", "SBB", "ÖBB",
        "ALDI", "REWE", "LIDL", "EDEKA", "DM", "IKEA", "BMW", "SAP",
        "DHL", "DPD", "GLS", "UPS", "GEZ", "ARD", "ZDF"
    };

    /// <summary>
    /// Normalizes a counterparty name or remittance text into a clean canonical merchant label.
    /// </summary>
    public static string Normalize(string? counterparty, string? remittance = null)
    {
        string candidate = counterparty?.Trim() ?? string.Empty;

        // If counterparty is empty or generic, check remittance for payment processor patterns
        if (string.IsNullOrWhiteSpace(candidate) || candidate.Equals("PAYPAL", StringComparison.OrdinalIgnoreCase))
        {
            if (!string.IsNullOrWhiteSpace(remittance))
            {
                var match = GatewayPrefixRegex.Match(remittance.Trim());
                if (match.Success)
                {
                    candidate = remittance.Trim();
                }
            }
        }

        if (string.IsNullOrWhiteSpace(candidate))
        {
            return "(Unknown Merchant)";
        }

        // 1. Strip Gateway prefixes (e.g. "PAYPAL *NETFLIX" -> "NETFLIX")
        candidate = GatewayPrefixRegex.Replace(candidate, string.Empty).Trim();

        // 2. Strip Terminal Noise (e.g. "EDEKA FILIALE 491" -> "EDEKA")
        candidate = TerminalNoiseRegex.Replace(candidate, string.Empty).Trim();

        // 3. Strip Trailing numeric references / transaction IDs
        candidate = TrailingNumericReferenceRegex.Replace(candidate, string.Empty).Trim();

        // 4. Strip Trailing Location / Country codes
        candidate = TrailingLocationCodeRegex.Replace(candidate, string.Empty).Trim();

        // 5. Clean punctuation artifacts (*, -, /, .)
        candidate = candidate.Trim('*', '-', '/', '.', ',', ' ', '\t');

        if (string.IsNullOrWhiteSpace(candidate))
        {
            return counterparty?.Trim() ?? "(Unknown Merchant)";
        }

        // 6. Casing: if all caps and length > 2, convert words to Title Case unless they are preserved acronyms
        if (candidate.Length > 2 && candidate == candidate.ToUpperInvariant())
        {
            var words = candidate.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            for (int i = 0; i < words.Length; i++)
            {
                words[i] = PreservedAcronyms.Contains(words[i])
                    ? words[i].ToUpperInvariant()
                    : CultureInfo.InvariantCulture.TextInfo.ToTitleCase(words[i].ToLowerInvariant());
            }
            candidate = string.Join(" ", words);
        }

        return candidate;
    }
}
