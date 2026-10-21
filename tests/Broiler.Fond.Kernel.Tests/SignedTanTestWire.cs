using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace Broiler.Fond.TestSupport;

internal static class SignedTanTestWire
{
    internal static byte[] Initial(byte[] template, byte[] procedureResponse)
    {
        string tan = Encoding.Latin1.GetString(procedureResponse).Split('\'').Single(s => s.StartsWith("HITANS:", StringComparison.Ordinal));
        return Wrap(template, 1, "HIRMG:2:2+0010::PUBLIC-RECEIPT'HIRMS:3:2:3+0020::PUBLIC-ID'" +
            "HIRMS:4:2:4+0020::PUBLIC-PREP+3920::PUBLIC-PERMISSIONS:900:999'HIRMS:5:2:5+0020::PUBLIC-SYNC'" +
            "HIRMS:6:2:6+0030::PUBLIC-CHALLENGE'HIBPA:7:3:4+1+280:PUBLIC-BANK+PUBLIC-BANK+1+1:2:3+300'" +
            "HIUPA:8:4:4+PUBLIC-USER+2+0'HITANS:9:7:4" + tan[tan.IndexOf('+')..] + "'" +
            "HISYN:10:4:5+PUBLIC-ASSIGNED'HITAN:11:7:6+4++PUBLIC-ORDER+Enter the TAN shown for bank sign-in'", 12);
    }
    internal static byte[] Confirm(byte[] template, string order = "PUBLIC-ORDER", string status = "0020") => Wrap(template, 2,
        $"HIRMG:2:2+0010::PUBLIC-RECEIPT'HIRMS:3:2:3+{status}::PUBLIC-APPROVAL'HITAN:4:7:3+2++{order}+nochallenge'", 5);
    internal static byte[] Close(byte[] template) => Wrap(template, 3, "HIRMG:2:2+0100::PUBLIC-CLOSE'HIRMS:3:2:3+0020::PUBLIC-CLOSE'", 4);
    internal static byte[] EditBody(byte[] wire, Func<string, string> change)
    {
        string raw = Encoding.Latin1.GetString(wire);
        var marker = Regex.Match(raw, "HNVSD:999:1\\+@([0-9]+)@");
        int start = marker.Index + marker.Length, length = int.Parse(marker.Groups[1].Value, CultureInfo.InvariantCulture);
        var trailer = Regex.Match(raw, @"HNHBS:(\d+):1\+(\d+)'$");
        return Wrap(wire, int.Parse(trailer.Groups[2].Value, CultureInfo.InvariantCulture), change(raw.Substring(start, length)),
            int.Parse(trailer.Groups[1].Value, CultureInfo.InvariantCulture));
    }
    internal static byte[] Wrap(byte[] template, int message, string body, int trailer)
    {
        string raw = Encoding.Latin1.GetString(template);
        var marker = Regex.Match(raw, "HNVSD:999:1\\+@([0-9]+)@");
        int start = marker.Index + marker.Length, length = int.Parse(marker.Groups[1].Value, CultureInfo.InvariantCulture);
        string result = raw[..marker.Index] + $"HNVSD:999:1+@{body.Length}@" + body + raw[(start + length)..];
        result = Regex.Replace(result, @"(?<=\+300\+SYNTHETIC\+)\d+\+SYNTHETIC:\d+", $"{message}+SYNTHETIC:{message}");
        result = Regex.Replace(result, @"HNHBS:\d+:1\+\d+'$", $"HNHBS:{trailer}:1+{message}'");
        result = Regex.Replace(result, @"(?<=HNHBK:1:3\+)\d{12}", result.Length.ToString("D12", CultureInfo.InvariantCulture));
        return Encoding.Latin1.GetBytes(result);
    }
}
