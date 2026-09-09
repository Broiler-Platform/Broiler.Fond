using System.Globalization;
using System.Reflection;
using System.Text;
using System.Text.Json;
using Broiler.Fond.Kernel.FinTs;

namespace Broiler.Fond.Kernel.Tests;

internal static class FinTsReadDataTests
{
    private const string National = "PUBLIC-001:00:280:PUBLIC-BANK";
    private const string International = "PUBLIC-IBAN:PUBLIC-BIC:PUBLIC-001:00:280:PUBLIC-BANK";
    private const string Sepa = "J:PUBLIC-IBAN:PUBLIC-BIC:PUBLIC-001:00:280:PUBLIC-BANK";
    internal static void Run(Action<bool, string> check)
    {
        int count = 0;
        void Verify(bool value, string message) { count++; check(value, message); }
        void Reject(Action action, FinTsSyntaxError expected = FinTsSyntaxError.InvalidReadData)
        {
            try { action(); Verify(false, "Invalid read data must fail."); }
            catch (FinTsFormatException error)
            {
                Verify(error.Error == expected, $"Read-data error category at check {count + 1}: {error.Error} versus {expected}.");
                Verify(!error.ToString().Contains("PUBLIC", StringComparison.Ordinal), "Read-data diagnostics must omit source values.");
            }
        }
        foreach (int version in new[] { 6, 7, 8 })
        {
            string account = version == 6 ? National : International;
            var request = Request($"HKSAL:2:{version}+{account}+N+9999+PUBLIC?+PAGE");
            Verify(request.Accounts.Count == 1 && !request.AllAccounts && request.MaximumEntries == 9999 && request.ContinuationToken == "PUBLIC+PAGE", "Read requests retain exact options and decoded pagination tokens.");
            Verify(request.Accounts[0].Number == "PUBLIC-001" && request.Accounts[0].Subaccount == "00" && request.Accounts[0].Country == "280" && request.Accounts[0].Institution == "PUBLIC-BANK", "National identifiers are not normalized.");
            var all = Request($"HKSAL:2:{version}+{account}+J");
            Verify(all.AllAccounts && all.Accounts.Count == 1 && all.MaximumEntries is null && all.ContinuationToken is null, "All-account balance queries still require an account; missing options stay absent.");
            var minimum = Data(Balance(version, Fields(version)[..4])).Balances.Single();
            Verify(minimum.Booked.SignedValue == -1234.56m && minimum.Booked.CreditDebitIndicator == "D" && minimum.Booked.Amount.Currency == "EUR", "Debit balance retains sign and exact amount.");
            Verify(minimum.Pending is null && minimum.Available is null && minimum.CreditLine is null && minimum.AlreadyUsed is null && minimum.Overdraft is null && minimum.BookingTimestamp is null && minimum.DueDate is null && minimum.GarnishableFromMonthChange is null, "Omitted optional amounts and dates are not zeros or synthetic timestamps.");
            var complete = Data(Balance(version)).Balances.Single();
            Verify(complete.Pending?.SignedValue == 12.34m && complete.CreditLine?.Value == 500m && complete.Available?.Value == 0m && complete.AlreadyUsed?.Value == 9m && complete.Overdraft?.Value == 3m, "Pending, credit, available, used and overdraft amounts retain distinct meanings.");
            Verify(complete.Booked.Timestamp.Date == new DateOnly(2026, 9, 7) && complete.Booked.Timestamp.Time == new TimeOnly(12, 34, 56) && complete.BookingTimestamp?.Date == new DateOnly(2026, 9, 4) && complete.BookingTimestamp.Time is null && complete.DueDate == new DateOnly(2026, 10, 1), "Source balance and booking dates differ and optional time stays absent.");
            Verify(complete.GarnishableFromMonthChange?.Value == (version == 8 ? 45.67m : null) && !complete.HasCurrencyConflict && complete.RequestSegmentNumber == 2, "Version-eight extension and segment reference remain explicit.");
            for (int length = 4; length <= Fields(version).Length; length++)
            {
                Verify(Data(Balance(version, Fields(version)[..length])).Balances.Count == 1, "All valid optional suffix truncations parse.");
            }
            foreach (string invalid in new[] { "", "X", "j", "@1@J", "N:N" })
            { Reject(() => Request($"HKSAL:2:{version}+{account}+{invalid}")); }
            foreach (string invalid in new[] { "0", "00", "01", "10000", "-1", "1,", "@1@1", "1:1" })
            { Reject(() => Request($"HKSAL:2:{version}+{account}+N+{invalid}")); }
            Reject(() => Request($"HKSAL:2:{version}++J"));
            Reject(() => Request($"HKSAL:2:{version}+{account}"));
            Reject(() => Request($"HKSAL:2:{version}+{account}+N++{new string('x', 36)}"));
            Reject(() => Request($"HKSAL:2:{version}+{account}+N+++extra"));
            Reject(() => Request($"HKSAL:2:{version}:2+{account}+N"));
            Reject(() => Data(Balance(version).Replace($":{version}:2", $":{version}", StringComparison.Ordinal)));
        }
        var emptyDiscovery = Request("HKSPA:2:1");
        Verify(emptyDiscovery.AllAccounts && emptyDiscovery.Accounts.Count == 0, "An empty discovery request means all accounts.");
        Verify(Request($"HKSPA:2:1+{National}+{National}").Accounts.Count == 2, "Duplicate request identifiers survive without deduplication.");
        Verify(Data("HISPA:3:1:2").Discovery.Single().Accounts.Count == 0, "An empty discovery report creates no account.");
        var discovery = Data($"HISPA:3:1:2+{Sepa}+N:::PUBLIC-002::280:PUBLIC-BANK").Discovery.Single();
        Verify(discovery.Accounts[0].SepaUsageReported == true && discovery.Accounts[1].SepaUsageReported == false && discovery.Accounts[1].Iban == "" && discovery.Accounts[1].Number == "PUBLIC-002", "Non-SEPA account shells are retained.");
        Verify(Request("HKSPA:2:1+").AllAccounts && Data("HISPA:3:1:2+").Discovery[0].Accounts.Count == 0, "Explicit empty optional repetitions parse without inventing accounts.");
        Reject(() => Request($"HKSPA:2:1++{National}"));
        Reject(() => Data($"HISPA:3:1:2++{Sepa}"));
        foreach (string account in new[] { "PUBLIC-001::280", "PUBLIC-001::001:" })
        { Verify(Request($"HKSPA:2:1+{account}").Accounts.Count == 1, "Country codes retain leading zeros and country-dependent institution requirements remain unasserted."); }
        foreach (string account in new[] { "PUBLIC-IBAN:PUBLIC-BIC", "::PUBLIC-001::280:PUBLIC-BANK", International })
        { Verify(Request($"HKSAL:2:7+{account}+N").Accounts.Count == 1, "International, national and combined kti shapes parse."); }
        foreach (string account in new[] { "", "PUBLIC-IBAN", "PUBLIC-IBAN:", ":PUBLIC-BIC", "::PUBLIC-001", "::PUBLIC-001::28", "::PUBLIC-001::abc", "::::280:PUBLIC-BANK", ":::00", International + ":extra", "@1@A:PUBLIC-BIC", new string('x', 35) + ":PUBLIC-BIC" })
        { Reject(() => Request($"HKSAL:2:7+{account}+N")); }
        foreach (string account in new[] { "PUBLIC-001::28", "PUBLIC-001::abc", ":00:280:PUBLIC-BANK", National + ":extra", new string('x', 31) + "::280" })
        { Reject(() => Request($"HKSPA:2:1+{account}")); }
        foreach (string account in new[] { Sepa.Replace("J:", "N:", StringComparison.Ordinal), "J:::PUBLIC-001::280:PUBLIC-BANK", "X:::PUBLIC-001::280:PUBLIC-BANK", "N:::PUBLIC-001", Sepa + ":extra" })
        { Reject(() => Data($"HISPA:3:1:2+{account}")); }
        foreach (var mutation in new (int Index, string Value)[]
        {
            (1, ""), (1, new string('x', 31)), (1, "PUBLIC\r"), (1, "PUBLIC:extra"), (1, "@1@X"),
            (2, "eur"), (2, "EU"), (2, ""), (2, "EUR:USD"), (3, ""), (3, "X:1,:EUR:20260907"),
            (3, "C:1,:EUR"), (3, "C:1,:EUR:20260229"), (3, "C:1,:EUR:20260907:240000"),
            (3, "C:1,:EUR:20260907:235960"), (3, "C:1,:EUR:20260907:12345"), (3, "C:1,:EUR:2026097"),
            (3, "C:1,:EUR:20260907:000000:extra"), (4, "C:0,:EUR"), (4, ":::::"),
            (5, "1,"), (5, ":EUR"), (5, "1,:"), (5, "1,:EUR:extra"), (5, "@0@"),
            (6, "1,:EUR"), (6, ""), (9, ":123456"), (9, "20260229"), (9, "20260907:240000"),
            (9, "20260907:120000:extra"), (10, "20261301"), (10, "20260907:extra"), (11, "0,:eur"),
        })
        { var fields = Fields(8); fields[mutation.Index] = mutation.Value; Reject(() => Data(Balance(8, fields))); }
        foreach (string amount in new[] { "0", "1.2", "1,20", "01,", "-1,", "+1,", ",1", "1,,2", "1,e2", " 1,", "1, ", "100000000000000,", "@1@1" })
        { var fields = Fields(8); fields[3] = $"C:{amount}:EUR:20260907"; Reject(() => Data(Balance(8, fields))); }
        foreach (var pair in new[] { ("99999999999999,", 99999999999999m), ("0,0000000000001", 0.0000000000001m), ("0,", 0m) })
        {
            var fields = Fields(8); fields[3] = $"D:{pair.Item1}:EUR:20240229:000000";
            var balance = Data(Balance(8, fields)).Balances[0].Booked;
            Verify(balance.SignedValue == -pair.Item2 && balance.CreditDebitIndicator == "D" && balance.Timestamp.Time == TimeOnly.MinValue, "Wire precision boundaries, leap days and debit zero survive exactly.");
        }
        var optional = Fields(8); optional[4] = "::::"; optional[5] = optional[6] = optional[7] = optional[8] = optional[11] = ":"; optional[9] = ":"; optional[10] = "";
        Verify(Data(Balance(8, optional)).Balances[0].Available is null, "Fully empty groups preserve absence within their component bounds.");
        var conflicting = Fields(8); conflicting[6] = "0,:USD";
        Verify(Data(Balance(8, conflicting)).Balances[0].HasCurrencyConflict, "Currency disagreement is visible without conversion or discarding source evidence.");
        Reject(() => Data(Balance(7, Fields(8))));
        Reject(() => Data(Balance(8, Fields(8).Append("extra").ToArray())));
        var duplicates = Data(Balance(8), Balance(8), $"HISPA:3:1:2+{Sepa}+{Sepa}", "HISPA:3:2:2+opaque", "HISPA:3:3:2+opaque", "HISAL:3:9:2+opaque", "HIXYZ:3:1+opaque");
        Verify(duplicates.Balances.Count == 2 && duplicates.Discovery[0].Accounts.Count == 2 && duplicates.UninterpretedSegments.Count == 4, "Duplicate reports and identifiers remain visible; unknown versions do not fall back.");
        Verify(ReferenceEquals(duplicates.Source.UninterpretedSegments[0], duplicates.Balances[0].Source), "Reports preserve original source objects for later correlation.");
        foreach (string request in new[] { "HKSPA:2:2", "HKSPA:2:3", "HKSAL:2:5", "HKSAL:2:9" })
        { Reject(() => Request(request), FinTsSyntaxError.UnsupportedReadDataVersion); }
        Reject(() => Request("HKPAY:2:1"));
        Verify(Request("HKSPA:2:1+" + string.Join('+', Enumerable.Repeat(National, 999))).Accounts.Count == 999, "Maximum discovery request repetition parses.");
        Reject(() => Request("HKSPA:2:1+" + string.Join('+', Enumerable.Repeat(National, 1000))), FinTsSyntaxError.LimitExceeded);
        string Many(int n) => "HISPA:3:1:2+" + string.Join('+', Enumerable.Repeat(Sepa, n));
        Verify(Data(Many(999), Many(25)).Discovery.Sum(d => d.Accounts.Count) == 1024, "Aggregate account bound parses exactly.");
        Reject(() => Data(Many(999), Many(26)), FinTsSyntaxError.LimitExceeded);
        Reject(() => Data(Many(1000)), FinTsSyntaxError.LimitExceeded);
        Verify(Data(Enumerable.Repeat("HISAL:3:99:2+opaque", 128).ToArray()).UninterpretedSegments.Count == 128, "Opaque read reports count toward the report limit.");
        Reject(() => Data(Enumerable.Repeat("HISAL:3:99:2+opaque", 129).ToArray()), FinTsSyntaxError.LimitExceeded);
        var errors = Response("HIRMS:3:2:2+9210::No balance");
        var noBalance = FinTsReadDataSet.Parse(errors);
        Verify(noBalance.Balances.Count == 0 && noBalance.Source.HasErrors, "A bank error cannot become a zero balance.");
        var partial = FinTsReadDataSet.Parse(Response("HIRMS:3:2:2+3040::Partial+0020::Reported", Balance(8)));
        Verify(partial.Balances.Count == 1 && partial.Source.ReplySegments.Any(s => s.Replies.Any(r => r.Code == "3040")), "Partial response status remains attached to data without inferring completion.");
        using (var cancellation = new CancellationTokenSource())
        {
            cancellation.Cancel();
            try { FinTsReadRequest.Parse(Segment("HKSPA:2:1"), cancellation.Token); Verify(false, "Request parsing must honor cancellation."); }
            catch (OperationCanceledException) { Verify(true, "Request cancellation observed."); }
            try { FinTsReadDataSet.Parse(errors, cancellation.Token); Verify(false, "Response parsing must honor cancellation."); }
            catch (OperationCanceledException) { Verify(true, "Response cancellation observed."); }
        }
        Verify(!discovery.ToString()!.Contains("PUBLIC", StringComparison.Ordinal) && !discovery.Accounts[0].ToString()!.Contains("PUBLIC", StringComparison.Ordinal) && !duplicates.Balances[0].ToString()!.Contains("PUBLIC", StringComparison.Ordinal), "Default schema object diagnostics omit source account data.");
        Fixtures(Verify);
        Console.WriteLine($"FinTS read-data schema verification passed ({count} checks).");
    }
    private static string[] Fields(int version)
    {
        string[] fields = [version == 6 ? National : International, "PUBLIC Product", "EUR", "D:1234,56:EUR:20260907:123456", "C:12,34:EUR:20260907", "500,:EUR", "0,:EUR", "9,:EUR", "3,:EUR", "20260904", "20261001", "45,67:EUR"];
        return version == 8 ? fields : fields[..11];
    }
    private static string Balance(int version, string[]? fields = null) => $"HISAL:3:{version}:2+{string.Join('+', fields ?? Fields(version))}";
    private static FinTsSegment Segment(string wire) => FinTsSyntax.ParseSegments(Encoding.Latin1.GetBytes(wire + "'")).Segments.Single();
    private static FinTsReadRequest Request(string wire) => FinTsReadRequest.Parse(Segment(wire));
    private static FinTsReadDataSet Data(params string[] body) => FinTsReadDataSet.Parse(Response(body));
    private static FinTsResponse Response(params string[] body)
    {
        var pieces = new List<string> { "HIRMG:2:2+0010::Synthetic'" };
        foreach (string value in body)
        {
            int colon = value.IndexOf(':'), next = value.IndexOf(':', colon + 1);
            pieces.Add(value[..(colon + 1)] + (pieces.Count + 2).ToString(CultureInfo.InvariantCulture) + value[next..] + "'");
        }
        string payload = string.Concat(pieces) + $"HNHBS:{pieces.Count + 2}:1+1'";
        string header = "HNHBK:1:3+000000000000+300+SYNTHETIC+1+SYNTHETIC:1'";
        header = header.Replace("000000000000", (header.Length + payload.Length).ToString("D12", CultureInfo.InvariantCulture), StringComparison.Ordinal);
        return FinTsResponse.Parse(FinTsMessageFrame.Parse(Encoding.Latin1.GetBytes(header + payload)));
    }
    private static void Fixtures(Action<bool, string> verify)
    {
        using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream("FinTs.read-data-v1.json")!;
        using var document = JsonDocument.Parse(stream);
        int count = 0;
        foreach (var vector in document.RootElement.GetProperty("vectors").EnumerateArray())
        {
            count++;
            var requestFrame = FinTsMessageFrame.Parse(Convert.FromBase64String(vector.GetProperty("requestBase64").GetString()!));
            var request = FinTsReadRequest.Parse(requestFrame.Syntax.Segments[1]);
            var response = FinTsResponse.Parse(FinTsMessageFrame.Parse(Convert.FromBase64String(vector.GetProperty("responseBase64").GetString()!)));
            var data = FinTsReadDataSet.Parse(response);
            verify(request.AllAccounts == vector.GetProperty("allAccounts").GetBoolean() && request.Accounts.Count == vector.GetProperty("requestAccounts").GetInt32(), "Independent request expectations match.");
            verify(data.Discovery.Sum(d => d.Accounts.Count) == vector.GetProperty("discoveredAccounts").GetInt32() && data.UninterpretedSegments.Count == vector.GetProperty("unknownReports").GetInt32(), "Independent discovery and unknown-version expectations match.");
            var expected = vector.GetProperty("balances").EnumerateArray().ToArray();
            verify(data.Balances.Count == expected.Length, "Independent balance count matches.");
            for (int i = 0; i < expected.Length; i++)
            {
                var balance = data.Balances[i]; var item = expected[i];
                verify(balance.Booked.SignedValue == decimal.Parse(item.GetProperty("booked").GetString()!, CultureInfo.InvariantCulture) && balance.Booked.Amount.Currency == item.GetProperty("currency").GetString(), "Independent exact signed amount/currency matches.");
                verify(balance.Available?.Value == (item.GetProperty("available").ValueKind == JsonValueKind.Null ? null : decimal.Parse(item.GetProperty("available").GetString()!, CultureInfo.InvariantCulture)), "Independent absent/available amount matches.");
                verify(balance.HasCurrencyConflict == item.GetProperty("currencyConflict").GetBoolean() && balance.Account.Number == item.GetProperty("accountNumber").GetString(), "Independent conflict flag and lossless identifier match.");
                verify(balance.ProductLabel == item.GetProperty("productLabel").GetString(), "Independent escaped source text matches.");
            }
        }
        verify(count == 8, "All eight independent read-data fixtures ran.");
    }
}
