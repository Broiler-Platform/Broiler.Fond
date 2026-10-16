using System.Collections.ObjectModel;
using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using Broiler.Fond.Kernel.Domain;

namespace Broiler.Fond.Kernel.Formats.Mt940;

/// <summary>
/// Secure, bounded parser for SWIFT MT940 and MT942 statement files and wire payloads.
/// </summary>
public static class Mt940Parser
{
    public const int MaximumInputBytes = 10 * 1024 * 1024; // 10 MiB
    public const int MaximumStatementsPerBatch = 1000;
    public const int MaximumEntriesPerStatement = 50000;

    public static IReadOnlyList<Mt940Statement> Parse(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        if (text.Length > MaximumInputBytes)
        {
            throw new FormatException($"Input text exceeds maximum allowed size of {MaximumInputBytes} characters.");
        }

        return ParseLines(SplitLines(text));
    }

    public static IReadOnlyList<Mt940Statement> Parse(ReadOnlySpan<byte> bytes, Encoding? encoding = null)
    {
        if (bytes.Length > MaximumInputBytes)
        {
            throw new FormatException($"Input byte span exceeds maximum allowed size of {MaximumInputBytes} bytes.");
        }

        encoding ??= Encoding.Latin1;
        string text = encoding.GetString(bytes);
        return ParseLines(SplitLines(text));
    }

    private static List<string> SplitLines(string text)
    {
        List<string> lines = [];
        using StringReader reader = new(text);
        string? line;
        while ((line = reader.ReadLine()) != null)
        {
            lines.Add(line);
        }

        return lines;
    }

    private static IReadOnlyList<Mt940Statement> ParseLines(List<string> lines)
    {
        List<Mt940Statement> statements = [];

        int lineIndex = 0;
        while (lineIndex < lines.Count)
        {
            // Skip empty lines or trailing SWIFT block delimiters
            string trimmed = lines[lineIndex].Trim();
            if (trimmed.Length == 0 || trimmed == "-" || trimmed.StartsWith("{", StringComparison.Ordinal))
            {
                lineIndex++;
                continue;
            }

            // Beginning of a statement must start with :20:
            if (!trimmed.StartsWith(":20:", StringComparison.Ordinal))
            {
                lineIndex++;
                continue;
            }

            if (statements.Count >= MaximumStatementsPerBatch)
            {
                throw new FormatException($"Batch exceeds maximum statements limit of {MaximumStatementsPerBatch}.");
            }

            var statement = ParseSingleStatement(lines, ref lineIndex);
            statements.Add(statement);
        }

        if (statements.Count == 0)
        {
            throw new FormatException("Input does not contain any valid MT940 statement (:20:).");
        }

        return statements.AsReadOnly();
    }

    private static Mt940Statement ParseSingleStatement(List<string> lines, ref int lineIndex)
    {
        string? trn = null;
        string? relatedRef = null;
        string? accountId = null;
        string? statementNumber = null;
        string? sequenceNumber = null;
        Mt940Balance? openingBalance = null;
        Mt940Balance? closingBalance = null;
        List<Mt940Entry> entries = [];

        Mt940EntryBuilder? currentEntry = null;

        while (lineIndex < lines.Count)
        {
            string line = lines[lineIndex];
            string trimmed = line.Trim();

            // End of block 4 marker or empty line before next :20:
            if (trimmed == "-" || (trimmed.StartsWith(":20:", StringComparison.Ordinal) && trn != null))
            {
                if (trimmed == "-")
                {
                    lineIndex++;
                }
                break;
            }

            if (trimmed.Length == 0)
            {
                lineIndex++;
                continue;
            }

            // Check if line starts with a tag :XX:
            if (line.StartsWith(':'))
            {
                int secondColon = line.IndexOf(':', 1);
                if (secondColon > 1 && secondColon <= 5)
                {
                    string tag = line[1..secondColon].ToUpperInvariant();
                    string content = line[(secondColon + 1)..];

                    switch (tag)
                    {
                        case "20":
                            trn = content.Trim();
                            break;

                        case "21":
                            relatedRef = content.Trim();
                            break;

                        case "25":
                            accountId = content.Trim();
                            break;

                        case "28":
                        case "28C":
                            ParseStatementNumber(content.Trim(), out statementNumber, out sequenceNumber);
                            break;

                        case "60F":
                        case "60M":
                            openingBalance = ParseBalance(content.Trim(), tag);
                            break;

                        case "61":
                            if (currentEntry != null)
                            {
                                entries.Add(currentEntry.Build());
                                if (entries.Count > MaximumEntriesPerStatement)
                                {
                                    throw new FormatException($"Statement exceeds maximum entry count of {MaximumEntriesPerStatement}.");
                                }
                            }

                            string currency = openingBalance?.Amount.Currency ?? "EUR";
                            currentEntry = ParseField61(content.Trim(), currency);
                            break;

                        case "86":
                            if (currentEntry != null)
                            {
                                currentEntry.AppendRemittanceLine(content);
                            }
                            break;

                        case "62F":
                        case "62M":
                            if (currentEntry != null)
                            {
                                entries.Add(currentEntry.Build());
                                currentEntry = null;
                            }
                            closingBalance = ParseBalance(content.Trim(), tag);
                            break;

                        case "64":
                        case "65":
                            // Optional forward/available balance
                            break;
                    }

                    lineIndex++;
                    continue;
                }
            }

            // Line continuation (no leading tag)
            if (currentEntry != null)
            {
                currentEntry.AppendRemittanceLine(line);
            }

            lineIndex++;
        }

        if (currentEntry != null)
        {
            entries.Add(currentEntry.Build());
        }

        if (string.IsNullOrWhiteSpace(trn))
        {
            throw new FormatException("MT940 statement missing mandatory Transaction Reference Number (:20:).");
        }

        if (string.IsNullOrWhiteSpace(accountId))
        {
            throw new FormatException("MT940 statement missing mandatory Account Identification (:25:).");
        }

        if (openingBalance == null)
        {
            throw new FormatException("MT940 statement missing mandatory Opening Balance (:60F: or :60M:).");
        }

        return new Mt940Statement(
            trn,
            relatedRef,
            accountId,
            statementNumber ?? "1",
            sequenceNumber,
            openingBalance,
            closingBalance,
            entries);
    }

    private static void ParseStatementNumber(string content, out string statementNumber, out string? sequenceNumber)
    {
        int slash = content.IndexOf('/');
        if (slash >= 0)
        {
            statementNumber = content[..slash].Trim();
            sequenceNumber = content[(slash + 1)..].Trim();
        }
        else
        {
            statementNumber = content.Trim();
            sequenceNumber = null;
        }
    }

    private static Mt940Balance ParseBalance(string content, string tag)
    {
        // Format: [D|C][YYMMDD][Currency 3 chars][Amount]
        if (content.Length < 11)
        {
            throw new FormatException($"Malformed balance field :{tag}: length too short: '{content}'");
        }

        bool isCredit = content[0] switch
        {
            'C' => true,
            'D' => false,
            _ => throw new FormatException($"Invalid balance debit/credit indicator in :{tag}: '{content[0]}'")
        };

        string dateStr = content.Substring(1, 6);
        DateOnly date = ParseDate(dateStr);

        string currency = content.Substring(7, 3);
        string amountStr = content[10..].Replace(',', '.').Trim();

        if (!decimal.TryParse(amountStr, NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out decimal amount))
        {
            throw new FormatException($"Malformed balance amount in :{tag}: '{amountStr}'");
        }

        return new Mt940Balance(isCredit, date, new Money(amount, currency), tag);
    }

    private static DateOnly ParseDate(string yymmdd)
    {
        if (yymmdd.Length != 6 || !int.TryParse(yymmdd[0..2], out int yy) ||
            !int.TryParse(yymmdd[2..4], out int mm) ||
            !int.TryParse(yymmdd[4..6], out int dd))
        {
            throw new FormatException($"Invalid date string: '{yymmdd}'");
        }

        int year = yy >= 70 ? 1900 + yy : 2000 + yy;
        return new DateOnly(year, mm, dd);
    }

    private static Mt940EntryBuilder ParseField61(string content, string fallbackCurrency)
    {
        // Format: 6!n[4!n]2a[1!a]15d1!a3!c16x[//16x]
        // Examples:
        // 2610141014CD1234,56NTRFNONREF//1234567890
        // 261014D50,00NTRFREF123//BANKREF456
        if (content.Length < 11)
        {
            throw new FormatException($"Malformed statement line :61: '{content}'");
        }

        int index = 0;
        string valDateStr = content.Substring(index, 6);
        DateOnly valueDate = ParseDate(valDateStr);
        index += 6;

        DateOnly bookingDate = valueDate;
        // Check for optional 4-digit booking date (MMDD)
        if (index + 4 <= content.Length && char.IsAsciiDigit(content[index]) && char.IsAsciiDigit(content[index + 1]) &&
            char.IsAsciiDigit(content[index + 2]) && char.IsAsciiDigit(content[index + 3]))
        {
            int mm = int.Parse(content.Substring(index, 2), CultureInfo.InvariantCulture);
            int dd = int.Parse(content.Substring(index + 2, 2), CultureInfo.InvariantCulture);
            index += 4;
            int year = valueDate.Year;
            if (valueDate.Month == 1 && mm == 12)
            {
                year--;
            }
            else if (valueDate.Month == 12 && mm == 1)
            {
                year++;
            }
            bookingDate = new DateOnly(year, mm, dd);
        }

        // Debit/Credit mark: D, C, RC, RD
        bool isCredit;
        bool isReversal = false;
        ReadOnlySpan<char> remaining = content.AsSpan(index);
        if (remaining.StartsWith("RC", StringComparison.OrdinalIgnoreCase))
        {
            isCredit = false;
            isReversal = true;
            index += 2;
        }
        else if (remaining.StartsWith("RD", StringComparison.OrdinalIgnoreCase))
        {
            isCredit = true;
            isReversal = true;
            index += 2;
        }
        else if (index < content.Length && (content[index] == 'C' || content[index] == 'c'))
        {
            isCredit = true;
            index++;
        }
        else if (index < content.Length && (content[index] == 'D' || content[index] == 'd'))
        {
            isCredit = false;
            index++;
        }
        else
        {
            throw new FormatException($"Missing debit/credit indicator in :61: '{content}'");
        }

        // Optional funds code (1 letter if before amount digits)
        if (index < content.Length && char.IsLetter(content[index]) && index + 1 < content.Length && (char.IsAsciiDigit(content[index + 1]) || content[index + 1] == ',' || content[index + 1] == '.'))
        {
            index++;
        }

        // Amount: digits with comma or period
        int amountStart = index;
        while (index < content.Length && (char.IsAsciiDigit(content[index]) || content[index] == ',' || content[index] == '.'))
        {
            index++;
        }

        if (index == amountStart)
        {
            throw new FormatException($"Missing amount in :61: '{content}'");
        }

        string amountStr = content[amountStart..index].Replace(',', '.');
        if (!decimal.TryParse(amountStr, NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out decimal amount))
        {
            throw new FormatException($"Malformed amount in :61: '{amountStr}'");
        }

        // Transaction code (4 chars)
        string transactionCode = string.Empty;
        if (index + 4 <= content.Length)
        {
            transactionCode = content.Substring(index, 4);
            index += 4;
        }

        // Reference: CustomerRef//BankRef or CustomerRef
        string? customerRef = null;
        string? bankRef = null;
        if (index < content.Length)
        {
            string refPart = content[index..].Trim();
            int slashPos = refPart.IndexOf("//", StringComparison.Ordinal);
            if (slashPos >= 0)
            {
                customerRef = refPart[..slashPos].Trim();
                bankRef = refPart[(slashPos + 2)..].Trim();
            }
            else
            {
                customerRef = refPart;
            }
        }

        return new Mt940EntryBuilder(
            valueDate,
            bookingDate,
            isCredit,
            isReversal,
            new Money(amount, fallbackCurrency),
            transactionCode,
            customerRef,
            bankRef);
    }

    private sealed class Mt940EntryBuilder
    {
        private readonly DateOnly _valueDate;
        private readonly DateOnly _bookingDate;
        private readonly bool _isCredit;
        private readonly bool _isReversal;
        private readonly Money _amount;
        private readonly string _transactionCode;
        private readonly string? _customerRef;
        private readonly string? _bankRef;
        private readonly StringBuilder _remittanceBuilder = new();

        public Mt940EntryBuilder(
            DateOnly valueDate,
            DateOnly bookingDate,
            bool isCredit,
            bool isReversal,
            Money amount,
            string transactionCode,
            string? customerRef,
            string? bankRef)
        {
            _valueDate = valueDate;
            _bookingDate = bookingDate;
            _isCredit = isCredit;
            _isReversal = isReversal;
            _amount = amount;
            _transactionCode = transactionCode;
            _customerRef = customerRef;
            _bankRef = bankRef;
        }

        public void AppendRemittanceLine(string line)
        {
            if (_remittanceBuilder.Length > 0)
            {
                _remittanceBuilder.Append('\n');
            }
            _remittanceBuilder.Append(line);
        }

        public Mt940Entry Build()
        {
            string rawRemittance = _remittanceBuilder.ToString().Trim();
            var subfields = ParseStructuredSubfields(rawRemittance);

            // If structured subfields exist, build clean remittance text from ?20..?29
            string cleanRemittance;
            if (subfields.Count > 0)
            {
                StringBuilder text = new();
                for (int i = 20; i <= 29; i++)
                {
                    string key = i.ToString(CultureInfo.InvariantCulture);
                    if (subfields.TryGetValue(key, out string? val) && !string.IsNullOrWhiteSpace(val))
                    {
                        if (text.Length > 0)
                        {
                            text.Append(' ');
                        }
                        text.Append(val.Trim());
                    }
                }
                cleanRemittance = text.Length > 0 ? text.ToString() : rawRemittance;
            }
            else
            {
                cleanRemittance = rawRemittance;
            }

            return new Mt940Entry(
                _valueDate,
                _bookingDate,
                _isCredit,
                _isReversal,
                _amount,
                _transactionCode,
                _customerRef,
                _bankRef,
                cleanRemittance,
                subfields);
        }

        private static Dictionary<string, string> ParseStructuredSubfields(string text)
        {
            Dictionary<string, string> subfields = new(StringComparer.Ordinal);
            if (!text.Contains('?'))
            {
                return subfields;
            }

            // German FinTS / DTAUS SEPA format uses ?XX to separate subfields (e.g. ?00GUTSCHRIFT?20Verwendungszweck)
            var matches = Regex.Matches(text, @"\?([0-9]{2})([^\?]*)");
            foreach (Match match in matches)
            {
                if (match.Success)
                {
                    string code = match.Groups[1].Value;
                    string val = match.Groups[2].Value.Trim();
                    subfields[code] = val;
                }
            }

            return subfields;
        }
    }
}
