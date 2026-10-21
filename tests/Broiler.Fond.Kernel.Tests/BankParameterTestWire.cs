using System.Globalization;
using System.Text;

namespace Broiler.Fond.TestSupport;

/// <summary>Independent synthetic bank frames; production code never reads these examples.</summary>
internal static class BankParameterTestWire
{
    internal static byte[] Initialization(string bankCode = "PUBLIC-BANK", string extra = "", string protocol = "300", string status = "0020", string identityExtra = "") => Frame(
        "BOOTSTRAP", 1, $"HIRMG:2:2+0010::PUBLIC-REPLY'HIRMS:3:2:2+{status}::PUBLIC-ID{identityExtra}'HIRMS:4:2:3+0020::PUBLIC-PREP'" +
        $"HIBPA:5:3:3+1+280:{bankCode}+PUBLIC-BANK+1+1:2:3+{protocol}'{extra}", extra.Length == 0 ? 6 : 7);
    internal static byte[] Closing(string dialogue = "BOOTSTRAP", string code = "0100") =>
        Frame(dialogue, 2, $"HIRMG:2:2+{code}::PUBLIC-CLOSE'", 3);
    internal static byte[] Edit(byte[] wire, Func<string, string> edit)
    {
        string value = edit(Encoding.Latin1.GetString(wire));
        int start = value.IndexOf('+') + 1;
        value = value.Remove(start, 12).Insert(start, Encoding.Latin1.GetByteCount(value).ToString("D12", CultureInfo.InvariantCulture));
        return Encoding.Latin1.GetBytes(value);
    }
    internal static byte[] Frame(string dialogue, int message, string body, int trailer)
    {
        string value = $"HNHBK:1:3+000000000000+300+{dialogue}+{message}+{dialogue}:{message}'{body}HNHBS:{trailer}:1+{message}'";
        value = value.Replace("000000000000", Encoding.Latin1.GetByteCount(value).ToString("D12", CultureInfo.InvariantCulture), StringComparison.Ordinal);
        return Encoding.Latin1.GetBytes(value);
    }
}
