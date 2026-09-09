using System.Collections.Frozen;

namespace Broiler.Fond.Kernel.Domain;

/// <summary>
/// Frozen ISO 4217 List One reference, not bank/account support or a payment
/// capability. XTS and XXX are deliberately excluded from monetary values.
/// </summary>
public static class CurrencyCatalog
{
    public const string ReferenceVersion = "SIX-List-One-2026-01-01";
    public const string ReferenceSource = "https://www.six-group.com/dam/download/financial-information/data-center/iso-currrency/lists/list-one.xml";

    // Alphabetic codes only, retrieved 2026-09-05. No country names, minor-unit
    // assumptions, historical conversions, or runtime network lookup are used.
    private static readonly FrozenSet<string> Codes = (
        "AED AFN ALL AMD AOA ARS AUD AWG AZN BAM BBD BDT BHD BIF BMD BND BOB BOV BRL BSD BTN BWP BYN BZD " +
        "CAD CDF CHE CHF CHW CLF CLP CNY COP COU CRC CUP CVE CZK DJF DKK DOP DZD EGP ERN ETB EUR FJD FKP " +
        "GBP GEL GHS GIP GMD GNF GTQ GYD HKD HNL HTG HUF IDR ILS INR IQD IRR ISK JMD JOD JPY KES KGS KHR " +
        "KMF KPW KRW KWD KYD KZT LAK LBP LKR LRD LSL LYD MAD MDL MGA MKD MMK MNT MOP MRU MUR MVR MWK MXN " +
        "MXV MYR MZN NAD NGN NIO NOK NPR NZD OMR PAB PEN PGK PHP PKR PLN PYG QAR RON RSD RUB RWF SAR SBD SCR " +
        "SDG SEK SGD SHP SLE SOS SRD SSP STN SVC SYP SZL THB TJS TMT TND TOP TRY TTD TWD TZS UAH UGX USD USN " +
        "UYI UYU UYW UZS VED VES VND VUV WST XAD XAF XAG XAU XBA XBB XBC XBD XCD XCG XDR XOF XPD XPF XPT " +
        "XSU XUA YER ZAR ZMW ZWG").Split(' ').ToFrozenSet(StringComparer.Ordinal);

    public static bool IsRecognized(string? code) => code is not null && Codes.Contains(code);
}
