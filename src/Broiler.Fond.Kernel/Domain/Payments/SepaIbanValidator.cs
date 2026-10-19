using System.Collections.Frozen;

namespace Broiler.Fond.Kernel.Domain.Payments;

/// <summary>
/// Strict ISO 7064 / ISO 13616 MOD 97-10 IBAN validator for SEPA credit transfers.
/// Implemented strictly with BCL primitives and zero third-party dependencies.
/// </summary>
public static class SepaIbanValidator
{
    private static readonly FrozenDictionary<string, int> SepaCountryLengths = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase)
    {
        ["DE"] = 22,
        ["AT"] = 20,
        ["FR"] = 27,
        ["IT"] = 27,
        ["ES"] = 24,
        ["NL"] = 18,
        ["BE"] = 16,
        ["CH"] = 21,
        ["PL"] = 28,
        ["GB"] = 22,
        ["LU"] = 20,
        ["IE"] = 22,
        ["PT"] = 25,
        ["GR"] = 27,
        ["FI"] = 18,
        ["SE"] = 24,
        ["DK"] = 18,
        ["NO"] = 15,
        ["CZ"] = 24,
        ["SK"] = 24,
        ["HU"] = 28,
        ["RO"] = 24,
        ["BG"] = 22,
        ["HR"] = 21,
        ["SI"] = 19,
        ["EE"] = 20,
        ["LV"] = 21,
        ["LT"] = 20,
        ["CY"] = 28,
        ["MT"] = 31,
        ["IS"] = 26,
        ["LI"] = 21,
        ["MC"] = 27,
        ["SM"] = 27,
        ["AD"] = 24,
        ["GI"] = 23,
        ["VA"] = 22,
    }.ToFrozenDictionary(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Normalizes an IBAN by trimming whitespace, removing spaces, and converting to uppercase ASCII.
    /// Returns false if the input is null, empty, or contains non-alphanumeric characters.
    /// </summary>
    public static bool TryNormalize(string? input, out string normalized)
    {
        normalized = string.Empty;
        if (string.IsNullOrWhiteSpace(input))
        {
            return false;
        }

        Span<char> buffer = stackalloc char[input.Length];
        int writeIndex = 0;

        for (int i = 0; i < input.Length; i++)
        {
            char c = input[i];
            if (char.IsWhiteSpace(c))
            {
                continue;
            }

            if (c is >= 'a' and <= 'z')
            {
                buffer[writeIndex++] = (char)(c - 32);
            }
            else if (c is (>= 'A' and <= 'Z') or (>= '0' and <= '9'))
            {
                buffer[writeIndex++] = c;
            }
            else
            {
                return false; // Invalid non-alphanumeric character
            }
        }

        if (writeIndex is < 15 or > 34)
        {
            return false;
        }

        normalized = new string(buffer[..writeIndex]);
        return true;
    }

    /// <summary>
    /// Validates an IBAN string against SEPA length specifications and ISO 7064 MOD 97-10 check digits.
    /// </summary>
    public static bool Validate(string? input, out string normalized)
    {
        if (!TryNormalize(input, out normalized))
        {
            return false;
        }

        // 1. Country code validation (first 2 chars must be letters)
        if (!char.IsAsciiLetterUpper(normalized[0]) || !char.IsAsciiLetterUpper(normalized[1]))
        {
            return false;
        }

        string country = normalized[..2];

        // 2. Check digits (characters at index 2 and 3 must be digits)
        if (!char.IsAsciiDigit(normalized[2]) || !char.IsAsciiDigit(normalized[3]))
        {
            return false;
        }

        // 3. Known country length check
        if (SepaCountryLengths.TryGetValue(country, out int expectedLength))
        {
            if (normalized.Length != expectedLength)
            {
                return false;
            }
        }
        else
        {
            // For other countries, length must still conform to ISO 13616 bounds [15..34]
            if (normalized.Length is < 15 or > 34)
            {
                return false;
            }
        }

        // 4. ISO 7064 MOD 97-10 algorithm:
        // Rearrange: move first 4 characters to end
        // Evaluate: letters A=10..Z=35, digits 0..9, computing remainder mod 97 iteratively without overflow.
        int remainder = 0;

        // Process from index 4 to end
        for (int i = 4; i < normalized.Length; i++)
        {
            remainder = AppendCharToMod97(remainder, normalized[i]);
        }

        // Process first 4 chars
        for (int i = 0; i < 4; i++)
        {
            remainder = AppendCharToMod97(remainder, normalized[i]);
        }

        return remainder == 1;
    }

    /// <summary>
    /// Formats a valid or normalized IBAN with standard 4-character grouping (e.g. "DE89 3704 0044 0532 0130 00").
    /// </summary>
    public static string FormatWithSpaces(string normalizedIban)
    {
        if (string.IsNullOrWhiteSpace(normalizedIban))
        {
            return string.Empty;
        }

        int len = normalizedIban.Length;
        int spaces = (len - 1) / 4;
        Span<char> formatted = stackalloc char[len + spaces];

        int src = 0;
        int dst = 0;

        while (src < len)
        {
            if (src > 0 && src % 4 == 0)
            {
                formatted[dst++] = ' ';
            }
            formatted[dst++] = normalizedIban[src++];
        }

        return new string(formatted[..dst]);
    }

    private static int AppendCharToMod97(int currentRemainder, char c)
    {
        if (char.IsAsciiDigit(c))
        {
            return (currentRemainder * 10 + (c - '0')) % 97;
        }

        if (char.IsAsciiLetterUpper(c))
        {
            int val = c - 'A' + 10;
            // Letter contributes two digits [10..35]
            return ((currentRemainder * 100) + val) % 97;
        }

        throw new ArgumentOutOfRangeException(nameof(c), "Character must be ASCII alphanumeric.");
    }
}
