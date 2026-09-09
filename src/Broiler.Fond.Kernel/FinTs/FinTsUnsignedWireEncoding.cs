using System.Globalization;
using System.Text;

namespace Broiler.Fond.Kernel.FinTs;

internal static class FinTsUnsignedWireEncoding
{
    internal static string Text(string value, int maximum, FinTsSyntaxError error)
    {
        ArgumentNullException.ThrowIfNull(value);
        if (value.Length > maximum || value.Any(c => c < 32 || c is >= (char)127 and <= (char)160 || c > 255))
        { throw new FinTsFormatException(error); }
        return Encoding.Latin1.GetString(FinTsWireEncoding.EncodeText(Encoding.Latin1.GetBytes(value)));
    }

    // Internal callers supply already escaped, schema-restricted bodies and dialogue IDs.
    internal static byte[] Frame(string escapedDialogue, int messageNumber, StringBuilder body, int trailerNumber)
    {
        string number = messageNumber.ToString(CultureInfo.InvariantCulture);
        var wire = new StringBuilder("HNHBK:1:3+000000000000+300+").Append(escapedDialogue).Append('+').Append(number).Append('\'')
            .Append(body).Append("HNHBS:").Append(trailerNumber.ToString(CultureInfo.InvariantCulture)).Append(":1+").Append(number).Append('\'');
        if (wire.Length > FinTsSyntax.MaximumWireBytes) { throw new FinTsFormatException(FinTsSyntaxError.LimitExceeded); }
        // Every character is one Latin-1 byte, including escapes; size has a fixed width.
        string size = wire.Length.ToString("D12", CultureInfo.InvariantCulture);
        for (int i = 0; i < size.Length; i++) { wire[10 + i] = size[i]; }
        return Encoding.Latin1.GetBytes(wire.ToString());
    }
}
