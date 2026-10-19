using System.Globalization;
using Broiler.Fond.Kernel.Domain.Payments;

namespace Broiler.Fond.Kernel.FinTs;

/// <summary>
/// Parsed representation of a standing order item received from an authoritative HICDB bank response.
/// </summary>
public sealed record FinTsDiscoveredStandingOrder(
    string BankOrderReference,
    string DebtorIban,
    StandingOrderFrequency Frequency,
    int ExecutionDay,
    DateOnly FirstExecutionDate,
    DateOnly? LastExecutionDate,
    decimal Amount,
    string Currency,
    string? CreditorName,
    string? CreditorIban,
    string? RemittanceText);

/// <summary>
/// Parses FinTS 3.0 standing order responses: HICDB (list items), HICDE (setup confirmation),
/// HICDA (modification confirmation), and HICDL (deletion confirmation).
/// </summary>
public static class FinTsStandingOrderResponseParser
{
    public const string HicdbSegmentCode = "HICDB";
    public const string HicdeSegmentCode = "HICDE";
    public const string HicdaSegmentCode = "HICDA";
    public const string HicdlSegmentCode = "HICDL";

    /// <summary>
    /// Parses all HICDB standing order segments returned in a response.
    /// </summary>
    public static IReadOnlyList<FinTsDiscoveredStandingOrder> ParseDiscoveredOrders(FinTsResponse response)
    {
        ArgumentNullException.ThrowIfNull(response);

        List<FinTsDiscoveredStandingOrder> orders = [];
        var segments = response.BodySegments
            .Where(s => s.Code == HicdbSegmentCode);

        foreach (var seg in segments)
        {
            // Expected FinTS 3.0 HICDB structure:
            // Field 0: Kontoverbindung international (IBAN:BIC)
            // Field 1: Auftragsreferenz (order reference)
            // Field 2: Turnus (1, 2, 3, 6, 12)
            // Field 3: Ausfuehrungstag (1..31)
            // Field 4: Erste Ausfuehrung (YYYYMMDD)
            // Field 5: Letzte Ausfuehrung (optional YYYYMMDD)
            // Field 6: Betrag/Waehrung (Amount:Currency)
            if (seg.Fields.Count < 5)
            {
                continue;
            }

            string debtorIban = seg.Fields[0].Elements[0].HeaderText();
            string orderRef = seg.Fields[1].Elements[0].HeaderText();

            int turnusVal = int.TryParse(seg.Fields[2].Elements[0].HeaderText(), NumberStyles.Integer, CultureInfo.InvariantCulture, out int t) ? t : 1;
            StandingOrderFrequency freq = Enum.IsDefined(typeof(StandingOrderFrequency), turnusVal)
                ? (StandingOrderFrequency)turnusVal
                : StandingOrderFrequency.Monthly;

            int execDay = int.TryParse(seg.Fields[3].Elements[0].HeaderText(), NumberStyles.Integer, CultureInfo.InvariantCulture, out int d) ? d : 1;

            DateOnly firstDate = DateOnly.FromDateTime(DateTime.UtcNow);
            string firstDateStr = seg.Fields[4].Elements[0].HeaderText();
            if (DateOnly.TryParseExact(firstDateStr, "yyyyMMdd", CultureInfo.InvariantCulture, DateTimeStyles.None, out DateOnly fd))
            {
                firstDate = fd;
            }

            DateOnly? lastDate = null;
            if (seg.Fields.Count > 5 && seg.Fields[5].Elements.Count > 0 && !seg.Fields[5].Elements[0].IsEmpty)
            {
                string lastDateStr = seg.Fields[5].Elements[0].HeaderText();
                if (DateOnly.TryParseExact(lastDateStr, "yyyyMMdd", CultureInfo.InvariantCulture, DateTimeStyles.None, out DateOnly ld))
                {
                    lastDate = ld;
                }
            }

            decimal amount = 0m;
            string currency = "EUR";
            if (seg.Fields.Count > 6 && seg.Fields[6].Elements.Count > 0)
            {
                string amtStr = seg.Fields[6].Elements[0].HeaderText().Replace(',', '.');
                if (decimal.TryParse(amtStr, NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out decimal a))
                {
                    amount = a;
                }
                if (seg.Fields[6].Elements.Count > 1 && !seg.Fields[6].Elements[1].IsEmpty)
                {
                    currency = seg.Fields[6].Elements[1].HeaderText();
                }
            }

            string? credName = seg.Fields.Count > 7 && seg.Fields[7].Elements.Count > 0 ? seg.Fields[7].Elements[0].HeaderText() : null;
            string? credIban = seg.Fields.Count > 8 && seg.Fields[8].Elements.Count > 0 ? seg.Fields[8].Elements[0].HeaderText() : null;
            string? remText = seg.Fields.Count > 9 && seg.Fields[9].Elements.Count > 0 ? seg.Fields[9].Elements[0].HeaderText() : null;

            orders.Add(new FinTsDiscoveredStandingOrder(
                BankOrderReference: orderRef,
                DebtorIban: debtorIban,
                Frequency: freq,
                ExecutionDay: execDay,
                FirstExecutionDate: firstDate,
                LastExecutionDate: lastDate,
                Amount: amount,
                Currency: currency,
                CreditorName: credName,
                CreditorIban: credIban,
                RemittanceText: remText));
        }

        return orders.AsReadOnly();
    }

    /// <summary>
    /// Extracts the order reference from an HICDE, HICDA, or HICDL confirmation segment.
    /// </summary>
    public static string? ExtractOrderReference(FinTsResponse response)
    {
        ArgumentNullException.ThrowIfNull(response);

        var seg = response.BodySegments
            .FirstOrDefault(s => s.Code is HicdeSegmentCode or HicdaSegmentCode or HicdlSegmentCode);

        if (seg is not null && seg.Fields.Count > 0 && seg.Fields[0].Elements.Count > 0)
        {
            return seg.Fields[0].Elements[0].HeaderText();
        }

        return null;
    }
}
