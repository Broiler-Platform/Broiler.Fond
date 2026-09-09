using System.Globalization;
using System.Reflection;
using System.Text;
using System.Text.Json;
using Broiler.Fond.Kernel.FinTs;

namespace Broiler.Fond.Kernel.Tests;

internal static class FinTsUnsignedReadRequestTests
{
    internal static void Run(Action<bool, string> check)
    {
        int count = 0;
        void Verify(bool condition, string message) { count++; check(condition, message); }
        void Reject(Action action, FinTsSyntaxError expected = FinTsSyntaxError.InvalidReadData)
        {
            try { action(); Verify(false, "Invalid writer input must fail before returning bytes."); }
            catch (FinTsFormatException error)
            {
                Verify(error.Error == expected, "Writer errors use a fixed category.");
                Verify(!error.ToString().Contains("PUBLIC-SECRET", StringComparison.Ordinal), "Writer errors do not echo caller data.");
            }
        }
        using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream("FinTs.unsigned-read-requests-v1.json")!;
        using var document = JsonDocument.Parse(stream);
        var vectors = document.RootElement.GetProperty("vectors").EnumerateArray().ToArray();
        foreach (var vector in vectors)
        {
            string Text(string name) => vector.GetProperty(name).GetString()!;
            var accounts = vector.GetProperty("accounts").EnumerateArray().Select(Input).ToArray();
            int? maximum = vector.GetProperty("maximumEntries").ValueKind == JsonValueKind.Null ? null : vector.GetProperty("maximumEntries").GetInt32();
            string? continuation = vector.GetProperty("continuationToken").GetString();
            byte[] wire = Text("operation") == "HKSPA"
                ? FinTsUnsignedReadRequestWriter.EncodeDiscovery(Text("dialogue"), vector.GetProperty("messageNumber").GetInt32(), accounts)
                : FinTsUnsignedReadRequestWriter.EncodeBalance(Text("dialogue"), vector.GetProperty("messageNumber").GetInt32(), vector.GetProperty("version").GetInt32(), accounts[0], vector.GetProperty("allAccounts").GetBoolean(), maximum, continuation);
            Verify(wire.SequenceEqual(Convert.FromBase64String(Text("wireBase64"))), $"Unsigned bytes match independent vector {Text("name")}.");
            var context = FinTsReadRequestContext.Parse(FinTsMessageFrame.Parse(wire), 17);
            var request = context.Request;
            Verify(context.ExpectedBankMessageNumber == 17 && context.Frame.MessageNumber == vector.GetProperty("messageNumber").GetInt32(), "Expected bank counter remains separate from encoded client counter.");
            Verify(Encoding.Latin1.GetString(context.Frame.Syntax.Segments[0].Fields[2].Elements[0].CopyValueBytes()) == Text("dialogue"), "Dialogue text survives delimiters and Latin-1.");
            Verify(request.Source.Code == Text("operation") && request.Source.Version == vector.GetProperty("version").GetInt32() && request.Source.Reference is null, "Only the selected unsigned operation/version is encoded.");
            Verify(request.AllAccounts == vector.GetProperty("allAccounts").GetBoolean() && request.MaximumEntries == maximum && request.ContinuationToken == continuation, "Scope and optional pagination positions survive encoding.");
            Verify(request.Accounts.Count == accounts.Length, "The writer preserves account count including duplicates.");
            for (int i = 0; i < accounts.Length; i++)
            {
                var actual = request.Accounts[i]; var input = accounts[i];
                Verify(actual.Number == input.Number && actual.Subaccount == input.Subaccount && actual.Country == input.Country && actual.Institution == input.Institution && actual.Iban == input.Iban && actual.Bic == input.Bic, "All account identifiers retain exact source order, spelling and whitespace.");
            }
            wire[0] = 0;
            Verify(context.Frame.Syntax.CopyWireBytes()[0] == 'H', "Returned wire buffers do not mutate parsed contexts.");
        }
        Verify(vectors.Length == 10, "All ten independent encoding fixtures ran.");
        var national = new FinTsReadAccountInput("PUBLIC-001", "00", "280", "PUBLIC-BANK");
        var international = new FinTsReadAccountInput(iban: "PUBLIC-IBAN", bic: "PUBLIC-BIC");
        byte[] Balance(FinTsReadAccountInput account, int version = 8, int? maximum = null, string? token = null) =>
            FinTsUnsignedReadRequestWriter.EncodeBalance("SYNTHETIC", 2, version, account, maximumEntries: maximum, continuationToken: token);
        foreach (int version in new[] { -1, 0, 1, 5, 9, 999, int.MaxValue })
        { Reject(() => Balance(national, version), FinTsSyntaxError.UnsupportedReadDataVersion); }
        foreach (int maximum in new[] { int.MinValue, -1, 0, 10000, int.MaxValue }) { Reject(() => Balance(national, maximum: maximum)); }
        foreach (string token in new[] { "", new string('a', 36), "PUBLIC-SECRET\n", "€", "\ud800", "😀" }) { Reject(() => Balance(national, token: token)); }
        foreach (string dialogue in new[] { "", "0", "unbekannt", new string('a', 31), "PUBLIC-SECRET\n", "€" })
        { Reject(() => FinTsUnsignedReadRequestWriter.EncodeDiscovery(dialogue, 1, [])); }
        foreach (int number in new[] { int.MinValue, 0, 10000, int.MaxValue })
        { Reject(() => FinTsUnsignedReadRequestWriter.EncodeDiscovery("SYNTHETIC", number, [])); }
        foreach (var invalid in new[]
        {
            new FinTsReadAccountInput(), new FinTsReadAccountInput("PUBLIC-SECRET"),
            new FinTsReadAccountInput(country: "280"), new FinTsReadAccountInput(subaccount: "00", iban: "IBAN", bic: "BIC"),
            new FinTsReadAccountInput("1", country: "28"), new FinTsReadAccountInput("1", country: "28A"),
            new FinTsReadAccountInput(iban: "IBAN"), new FinTsReadAccountInput(bic: "BIC"),
            new FinTsReadAccountInput(new string('n', 31), country: "280"),
            new FinTsReadAccountInput("1", new string('s', 31), "280"),
            new FinTsReadAccountInput("1", country: "280", institution: new string('i', 31)),
            new FinTsReadAccountInput(iban: new string('a', 35), bic: "BIC"),
            new FinTsReadAccountInput(iban: "IBAN", bic: new string('b', 12)),
            new FinTsReadAccountInput("PUBLIC-SECRET\0", country: "280"),
        }) { Reject(() => Balance(invalid)); }
        Reject(() => Balance(international, 6));
        Reject(() => FinTsUnsignedReadRequestWriter.EncodeDiscovery("SYNTHETIC", 1, [international]));
        var combined = new FinTsReadAccountInput("1", country: "280", iban: "IBAN", bic: "BIC");
        Reject(() => Balance(combined, 6));
        Reject(() => FinTsUnsignedReadRequestWriter.EncodeDiscovery("SYNTHETIC", 1, [combined]));
        // Exercise the entire single-byte text range and the lossy Latin-1 boundary.
        for (int character = 0; character <= 256; character++)
        {
            string value = ((char)character).ToString();
            if (character < 32 || character is >= 127 and <= 160 || character > 255) { Reject(() => Balance(national, token: value)); }
            else
            {
                var parsed = FinTsReadRequestContext.Parse(FinTsMessageFrame.Parse(Balance(national, token: value)), 1);
                Verify(parsed.Request.ContinuationToken == value && parsed.Frame.Syntax.Segments.Count == 3, "Each permitted byte remains one data character without syntax injection.");
            }
        }
        var many = Enumerable.Repeat(national, 999).ToArray();
        var largest = FinTsReadRequestContext.Parse(FinTsMessageFrame.Parse(FinTsUnsignedReadRequestWriter.EncodeDiscovery("SYNTHETIC", 1, many)), 1);
        Verify(largest.Request.Accounts.Count == 999, "The discovery repetition limit is accepted without deduplication.");
        Reject(() => FinTsUnsignedReadRequestWriter.EncodeDiscovery("SYNTHETIC", 1, Enumerable.Repeat(national, 1000).ToArray()), FinTsSyntaxError.LimitExceeded);
        var originalCulture = CultureInfo.CurrentCulture;
        byte[] baseline = Balance(national, maximum: 1234, token: "token");
        try
        {
            foreach (string culture in new[] { "de-DE", "ar-SA", "tr-TR" })
            {
                CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo(culture);
                Verify(Balance(national, maximum: 1234, token: "token").SequenceEqual(baseline), "Wire numbers and lengths are culture invariant.");
            }
        }
        finally { CultureInfo.CurrentCulture = originalCulture; }
        using var cancelled = new CancellationTokenSource(); cancelled.Cancel();
        foreach (Action action in new Action[]
        {
            () => FinTsUnsignedReadRequestWriter.EncodeDiscovery("SYNTHETIC", 1, many, cancelled.Token),
            () => FinTsUnsignedReadRequestWriter.EncodeBalance("SYNTHETIC", 1, 8, national, cancellationToken: cancelled.Token),
        })
        {
            try { action(); Verify(false, "Cancelled encoding must return no bytes."); }
            catch (OperationCanceledException) { Verify(true, "Cancellation is propagated."); }
        }
        foreach (Action action in new Action[]
        {
            () => FinTsUnsignedReadRequestWriter.EncodeDiscovery(null!, 1, []),
            () => FinTsUnsignedReadRequestWriter.EncodeDiscovery("SYNTHETIC", 1, null!),
            () => FinTsUnsignedReadRequestWriter.EncodeDiscovery("SYNTHETIC", 1, [null!]),
            () => Balance(null!), () => new FinTsReadAccountInput(number: null!),
        })
        {
            try { action(); Verify(false, "Null inputs must fail."); }
            catch (ArgumentNullException) { Verify(true, "Null inputs are explicitly rejected."); }
        }
        using var discoveryStream = Assembly.GetExecutingAssembly().GetManifestResourceStream("FinTs.all-discovery-v1.json")!;
        using var discoveryDocument = JsonDocument.Parse(discoveryStream);
        var sample = discoveryDocument.RootElement.GetProperty("vectors")[0];
        var parameters = FinTsReadParameterSet.Parse(FinTsParameterSet.Parse(FinTsResponse.Parse(FinTsMessageFrame.Parse(Convert.FromBase64String(sample.GetProperty("parametersBase64").GetString()!)))));
        var response = FinTsReadDataSet.Parse(FinTsResponse.Parse(FinTsMessageFrame.Parse(Convert.FromBase64String(sample.GetProperty("responseBase64").GetString()!))));
        var generated = FinTsReadRequestContext.Parse(FinTsMessageFrame.Parse(FinTsUnsignedReadRequestWriter.EncodeDiscovery("SYNTHETIC", 2, [])), 2);
        var attempt = new FinTsAllAccountDiscoveryAttempt(TimeSpan.FromSeconds(10));
        Verify(attempt.Start(generated, parameters) == FinTsAllDiscoveryTransition.RequestRecorded && attempt.AcceptResponse(response).Transition == FinTsAllDiscoveryTransition.ExecutionReported, "Generated discovery frames integrate with existing pinned-context consumption.");
        Verify(!national.ToString()!.Contains("PUBLIC", StringComparison.Ordinal), "Default input diagnostics exclude identifiers.");
        Console.WriteLine($"FinTS unsigned read-request encoding verification passed ({vectors.Length} independent vectors, {count} checks).");
    }

    private static FinTsReadAccountInput Input(JsonElement value)
    {
        string Get(string name) => value.TryGetProperty(name, out var property) ? property.GetString()! : "";
        return new(Get("number"), Get("subaccount"), Get("country"), Get("institution"), Get("iban"), Get("bic"));
    }
}
