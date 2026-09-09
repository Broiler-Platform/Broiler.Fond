using System.Numerics;

namespace Broiler.Fond.Kernel.Domain;

// A common 10^-28 unit avoids decimal addition's implicit loss of precision.
internal static class ExactDecimal
{
    private static readonly BigInteger MaximumCoefficient = (BigInteger.One << 96) - 1;

    internal static BigInteger ToUnits(decimal value)
    {
        int[] bits = decimal.GetBits(value);
        BigInteger coefficient = unchecked((uint)bits[0]) +
            ((BigInteger)unchecked((uint)bits[1]) << 32) + ((BigInteger)unchecked((uint)bits[2]) << 64);
        int scale = (bits[3] >> 16) & 0xff;
        return (bits[3] < 0 ? -coefficient : coefficient) * BigInteger.Pow(10, 28 - scale);
    }

    internal static decimal FromUnits(BigInteger units) => FromCoefficient(units, 28);

    private static decimal FromCoefficient(BigInteger signedCoefficient, int scale)
    {
        bool negative = signedCoefficient.Sign < 0;
        BigInteger coefficient = BigInteger.Abs(signedCoefficient);
        while (coefficient > MaximumCoefficient && scale > 0 && coefficient % 10 == 0)
        {
            coefficient /= 10;
            scale--;
        }

        if (coefficient > MaximumCoefficient)
        {
            throw new OverflowException("The exact amount cannot be represented as decimal without rounding.");
        }

        int low = unchecked((int)(uint)(coefficient & uint.MaxValue));
        int middle = unchecked((int)(uint)((coefficient >> 32) & uint.MaxValue));
        int high = unchecked((int)(uint)((coefficient >> 64) & uint.MaxValue));
        return new(low, middle, high, negative, (byte)scale);
    }

    internal static decimal Parse(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        if (text.Length is 0 or > 64)
        {
            throw new FormatException("Amount text is outside the format bounds.");
        }

        int offset = text[0] == '-' ? 1 : 0;
        int integerStart = offset;
        BigInteger coefficient = 0;
        while (offset < text.Length && text[offset] is >= '0' and <= '9')
        {
            coefficient = coefficient * 10 + (text[offset++] - '0');
        }

        if (offset == integerStart || (offset - integerStart > 1 && text[integerStart] == '0'))
        {
            throw new FormatException("Amount requires canonical integer digits.");
        }

        int scale = 0;
        if (offset < text.Length && text[offset] == '.')
        {
            offset++;
            while (offset < text.Length && text[offset] is >= '0' and <= '9')
            {
                coefficient = coefficient * 10 + (text[offset++] - '0');
                scale++;
            }

            if (scale is 0 or > 28)
            {
                throw new FormatException("Amount fractional precision is outside the format bounds.");
            }
        }

        if (offset != text.Length)
        {
            throw new FormatException("Amount requires invariant decimal notation.");
        }

        return FromCoefficient(integerStart == 1 ? -coefficient : coefficient, scale);
    }
}
