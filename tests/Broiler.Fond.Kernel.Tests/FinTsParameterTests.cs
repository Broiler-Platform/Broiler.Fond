using System.Globalization;
using System.Reflection;
using System.Text;
using System.Text.Json;
using Broiler.Fond.Kernel.FinTs;

namespace Broiler.Fond.Kernel.Tests;

internal static class FinTsParameterTests
{
    private const string Secret = "PUBLIC-ACCOUNT-SENTINEL";
    private const string Bank = "HIBPA:3+7+280:10020030+Synthetic Bank+0+1:2:3+300:220+0+120+0";
    private const string User = "HIUPA:4+PUBLIC-USER+0+1";

    internal static void Run(Action<bool, string> check)
    {
        int count = 0;
        void Verify(bool condition, string message) { count++; check(condition, message); }
        void Reject(FinTsSyntaxError expected, params string[] parts)
        {
            try { _ = Parse(parts); Verify(false, "Invalid parameter evidence must fail closed."); }
            catch (FinTsFormatException error)
            {
                Verify(error.Error == expected, "Parameter error must retain its safe category.");
                Verify(!error.ToString().Contains(Secret, StringComparison.Ordinal), "Parameter exceptions must not echo account data.");
            }
        }

        FinTsParameterSet parsed = Parse(Bank, User, "HIUPD:6+" + Account("HKSAL:1", "HKCAZ:2"), "HIKOM:4+opaque");
        Verify(parsed.Bank!.Version == 7 && parsed.Bank.MaximumOperationTypes == 0 && parsed.Bank.MaximumMessageKiB == 0,
            "BPD version and explicit zero restrictions must survive.");
        Verify(parsed.Bank.MinimumTimeoutSeconds == 120 && parsed.Bank.MaximumTimeoutSeconds == 0,
            "Life-indicator interval and dialogue timeout must not be conflated or sorted.");
        Verify(parsed.Bank.Languages.SequenceEqual(new[] { 1, 2, 3 }) && parsed.Bank.ProtocolVersions.SequenceEqual(new[] { 300, 220 }),
            "Advertised lists must preserve order without choosing a protocol automatically.");
        Verify(parsed.User!.IsDialogueScoped && parsed.User.UnlistedOperations == FinTsUnlistedOperationPolicy.Unknown,
            "UPD zero version and unknown-unlisted policy must remain explicit.");
        var account = parsed.Accounts.Single();
        Verify(account.HasAccountConnection && Text(account.AccountConnection.Elements[0]) == Secret && Text(account.Iban) == "SYNTHETIC-NOT-IBAN" &&
            Text(account.CustomerId) == "CUSTOMER" && account.AccountType == 1 && Text(account.Currency!) == "EUR",
            "HIUPD source identifiers must be preserved without allocating or normalizing identities.");
        Verify(account.Permissions[1].RequiredSignatures == 2 && parsed.GetOperationEvidence(account, "HKSAL") == FinTsOperationEvidence.Listed,
            "Listed operation and signature requirements must remain evidence rather than execution permission.");
        Verify(parsed.GetOperationEvidence(account, "HKXYZ") == FinTsOperationEvidence.Unknown && parsed.UninterpretedSegments.Single().Code == "HIKOM",
            "Unlisted unknown operations and unimplemented endpoint parameters must stay unactivated.");
        byte[] errorWire = Encoding.Latin1.GetBytes(Encoding.Latin1.GetString(parsed.Source.Frame.Syntax.CopyWireBytes()).Replace("0010::received", "9000::received", StringComparison.Ordinal));
        var errorParameters = FinTsParameterSet.Parse(FinTsResponse.Parse(FinTsMessageFrame.Parse(errorWire)));
        Verify(errorParameters.Source.HasErrors && errorParameters.Source.HasIndeterminateProcessing && errorParameters.Accounts.Count == 1,
            "Parsing supplied parameters must preserve indeterminate response evidence rather than promote it to successful activation.");
        FinTsParameterSet blocked = Parse("HIUPA:4+PUBLIC-USER+9+0", "HIUPD:6+" + Account("HKSAL:1"));
        Verify(!blocked.User!.IsDialogueScoped && blocked.GetOperationEvidence(blocked.Accounts[0], "HKXYZ") == FinTsOperationEvidence.UnlistedBlocked,
            "UPD usage zero must distinguish unlisted-blocked from unknown.");
        FinTsParameterSet duplicate = Parse(User, "HIUPD:6+" + Account("HKSAL:1", "HKSAL:2"), "HIUPD:6+" + Account("HKSAL:1"));
        Verify(duplicate.Accounts.Count == 2 && duplicate.Accounts[0].Permissions.Count == 2 &&
            duplicate.GetOperationEvidence(duplicate.Accounts[0], "HKSAL") == FinTsOperationEvidence.Ambiguous,
            "Duplicate account occurrences and conflicting permission entries must not merge or pick a winner.");
        FinTsParameterSet noUser = Parse("HIUPD:6+" + Account("HKSAL:1"));
        Verify(noUser.User is null && noUser.GetOperationEvidence(noUser.Accounts[0], "HKXYZ") == FinTsOperationEvidence.Unknown,
            "Missing HIUPA must never invent unlisted permission policy.");
        Verify(Parse().Accounts.Count == 0 && Parse().Bank is null && Parse().User is null, "Absent parameter sections mean no supplied evidence, not deletion.");
        FinTsParameterSet optional = Parse("HIBPA:3+0+280:+Bank+0+1+300+++", "HIUPA:4+U+1+1++");
        Verify(optional.Bank!.MaximumMessageKiB is null && optional.Bank.MinimumTimeoutSeconds is null && optional.Bank.MaximumTimeoutSeconds is null,
            "Omitted numeric restrictions must not become zero.");
        Verify(Parse("HIBPA:3+1+280:10020030+Bank+1+1:1+300:300").Bank!.ProtocolVersions.Count == 2,
            "Repeated advertised versions remain source evidence.");
        Verify(Parse("HIBPA:3+1+280:10020030+Bank+1+1+410").Bank!.ProtocolVersions.Single() == 410,
            "A syntactically valid unknown protocol version must not be silently removed.");
        string[] fields = BaseFields();
        fields[4] = "ZZZ";
        fields[5] = new string('N', 35);
        Verify(Text(Parse("HIUPD:6+" + string.Join('+', fields)).Accounts[0].Currency!) == "ZZZ", "Unknown currency and explicitly allowed 35-byte owner must remain raw evidence.");
        string nonAccount = "++CUSTOMER+++++++HKSAL:0";
        Verify(!Parse("HIUPD:6+" + nonAccount).Accounts[0].HasAccountConnection,
            "An account-independent permission entry must not fabricate account identity.");
        var shortNonAccount = Parse("HIUPD:6+++CUSTOMER").Accounts[0];
        Verify(!shortNonAccount.HasAccountConnection && shortNonAccount.Currency is null && shortNonAccount.AccountType is null,
            "Truncated optional fields in an account-independent entry must remain absent.");
        foreach (string template in new[]
        {
            "HNHBK:1:3+{size}+300+D+1+D:1'HIRMG:2:2+0010::bad\u00a0text'HNHBS:3:1+1'",
            "HNHBK:1:3+{size}+300+D\u00a0+1+D\u00a0:1'HIRMG:2:2+0010::ok'HNHBS:3:1+1'",
        })
        {
            string provisional = template.Replace("{size}", "000000000000", StringComparison.Ordinal);
            byte[] wire = Encoding.Latin1.GetBytes(template.Replace("{size}", provisional.Length.ToString("D12", CultureInfo.InvariantCulture), StringComparison.Ordinal));
            try { _ = FinTsResponse.Parse(FinTsMessageFrame.Parse(wire)); Verify(false, "Nonbreaking-space octet must not pass typed text or dialog-ID validation."); }
            catch (FinTsFormatException) { Verify(true, "FinTS base printable range starts at A1, not A0."); }
        }

        foreach (string limit in new[] { "E", "T:123,45:EUR", "W:0,:ZZZ", "M:99999999999999,:EUR", "Z:::7", "Z" })
        {
            fields = BaseFields(); fields[8] = limit;
            Verify(Parse("HIUPD:6+" + string.Join('+', fields)).Accounts[0].HasAccountLimit, "Valid limits must remain exact, unactivated source evidence.");
            Verify(Parse("HIUPD:6+" + Account("HKSAL:1:" + limit)).Accounts[0].Permissions[0].HasLimit, "Per-operation limit shape must be recognized.");
        }
        fields = BaseFields(); fields[8] = "T:100,:EUR";
        Reject(FinTsSyntaxError.InvalidParameters, "HIUPD:6+" + string.Join('+', fields) + "+HKSAL:1:E:1,:EUR");
        foreach (string limit in new[] { "X", "E:1,00:EUR", "E:01,:EUR", "E:1.0:EUR", "E:-1,:EUR", "E:1,:eur", "E:1,", "E::EUR", "E:1,:EUR:2", "Z:1,:EUR", "Z:::0", "Z:::1000" })
        {
            fields = BaseFields(); fields[8] = limit;
            Reject(FinTsSyntaxError.InvalidParameters, "HIUPD:6+" + string.Join('+', fields));
        }

        foreach (string bad in new[]
        {
            "HIBPA:3+01+280:10020030+Bank+0+1+300", "HIBPA:3+1+28:10020030+Bank+0+1+300", "HIBPA:3+1+280:10020030+Bank+0+4+300",
            "HIBPA:3+1+280:10020030+Bank+0+1+", "HIBPA:3+1+280:10020030+Bank+0+1+300+10000",
            "HIBPA:3+1+280:10020030+Bank+0+1+300+0+1+2+3", "HIBPA:3+1+280:10020030+@4@Bank+0+1+300",
            "HIBPA:3+1+280:10020030+Bank+0+1:1:1:1:1:1:1:1:1:1+300",
            "HIUPA:4+U+1+2", "HIUPA:4++1+1", "HIUPA:4+U+1000+1", "HIUPA:4+U+1+1+a+b+c", "HIUPA:4+U+1+1+bad\rtext",
            "HIUPA:4+U+1+1+bad\u00a0text", "HIUPA:4+U+1+1+@0@", "HIUPD:6+missing",
            "HIUPD:6+" + Account("HKSAL:4"), "HIUPD:6+" + Account("hksal:1"), "HIUPD:6+" + Account("HKSAL:01"),
            "HIUPD:6+" + Account("HKSAL:1:::::"),
        }) { Reject(FinTsSyntaxError.InvalidParameters, bad); }
        Reject(FinTsSyntaxError.InvalidParameters, Bank, Bank);
        Reject(FinTsSyntaxError.InvalidParameters, User, User);
        Reject(FinTsSyntaxError.UnsupportedParameterVersion, "HIBPA:4+opaque");
        Reject(FinTsSyntaxError.UnsupportedParameterVersion, "HIUPA:3+opaque");
        Reject(FinTsSyntaxError.UnsupportedParameterVersion, "HIUPD:5+opaque");
        Reject(FinTsSyntaxError.UnsupportedParameterLayout, "HIUPD:6+" + Account("opaque-json"));
        for (int index = 0; index < 9; index++)
        {
            fields = BaseFields();
            fields[index] = "@1@x";
            Reject(FinTsSyntaxError.InvalidParameters, "HIUPD:6+" + string.Join('+', fields));
        }
        foreach ((int index, string value) in new[] { (0, new string('x', 31) + "::280:1"), (1, new string('I', 35)), (2, new string('C', 31)), (3, "100"), (4, "EURO"), (5, new string('N', 36)), (7, new string('P', 31)), (0, "::::"), (8, "::::") })
        {
            fields = BaseFields(); fields[index] = value;
            Reject(FinTsSyntaxError.InvalidParameters, "HIUPD:6+" + string.Join('+', fields));
        }
        string extensionFields = Account(Enumerable.Repeat("", 999).ToArray()) + "+{?\"note?\"?:?\"opaque?\"}";
        // JSON quotes do not need escaping; only the colon must be escaped in a scalar.
        extensionFields = extensionFields.Replace("?\"", "\"", StringComparison.Ordinal);
        Verify(Parse("HIUPD:6+" + extensionFields).Accounts[0].Extension is not null,
            "The explicit 999-slot layout must preserve the bounded extension without interpreting JSON.");
        Reject(FinTsSyntaxError.InvalidParameters, "HIUPD:6+" + Account(Enumerable.Repeat("", 999).ToArray()) + "+" + new string('x', 2049));
        Verify(Parse(Enumerable.Repeat("HIUPD:6+" + Account(), FinTsParameterSet.MaximumAccounts).ToArray()).Accounts.Count == FinTsParameterSet.MaximumAccounts,
            "Exact account bound must preserve every occurrence.");
        Reject(FinTsSyntaxError.LimitExceeded, Enumerable.Repeat("HIUPD:6+" + Account(), FinTsParameterSet.MaximumAccounts + 1).ToArray());
        foreach (int permissionTotal in new[] { FinTsParameterSet.MaximumPermissions, FinTsParameterSet.MaximumPermissions + 1 })
        {
            List<string> segments = [];
            for (int remaining = permissionTotal; remaining > 0; remaining -= Math.Min(999, remaining))
            {
                segments.Add("HIUPD:6+" + Account(Enumerable.Repeat("HKSAL:1", Math.Min(999, remaining)).ToArray()));
            }
            if (permissionTotal == FinTsParameterSet.MaximumPermissions)
            {
                Verify(Parse(segments.ToArray()).Accounts.Sum(a => a.Permissions.Count) == permissionTotal, "Exact global permission bound must parse.");
            }
            else { Reject(FinTsSyntaxError.LimitExceeded, segments.ToArray()); }
        }
        try { _ = FinTsParameterSet.Parse(parsed.Source, new CancellationToken(true)); Verify(false, "Pre-cancelled parameter parse must stop."); }
        catch (OperationCanceledException) { Verify(true, "Parameter cancellation is supported."); }
        try { _ = parsed.GetOperationEvidence(noUser.Accounts[0], "HKSAL"); Verify(false, "Foreign account cannot borrow a different UPD policy."); }
        catch (FinTsFormatException) { Verify(true, "Permission query is bound to its parameter evidence instance."); }
        byte[] copy = account.Iban.CopyValueBytes(); Array.Clear(copy);
        Verify(Text(account.Iban) == "SYNTHETIC-NOT-IBAN", "Mutation of copied source fields must not change evidence.");
        object[] printable = [parsed, parsed.Bank, parsed.User, account, account.Permissions[0]];
        Verify(printable.All(o => !o.ToString()!.Contains(Secret, StringComparison.Ordinal)), "Parameter ToString must never emit raw account/user data.");

        using Stream stream = Assembly.GetExecutingAssembly().GetManifestResourceStream("FinTs.parameters-v1.json")!;
        using JsonDocument corpus = JsonDocument.Parse(stream);
        int vectors = 0;
        foreach (JsonElement vector in corpus.RootElement.GetProperty("vectors").EnumerateArray())
        {
            vectors++;
            byte[] wire = Convert.FromBase64String(vector.GetProperty("wireBase64").GetString()!);
            FinTsParameterSet result = FinTsParameterSet.Parse(FinTsResponse.Parse(FinTsMessageFrame.Parse(wire)));
            Verify(result.Source.Frame.Syntax.CopyWireBytes().SequenceEqual(wire), "Independent parameter wire bytes must stay intact.");
            Verify(result.Accounts.Count == vector.GetProperty("accounts").GetInt32() && result.UninterpretedSegments.Count == vector.GetProperty("uninterpreted").GetInt32(),
                "Independent fixture account/opaque counts must match.");
            Verify(result.User?.IsDialogueScoped == (vector.GetProperty("dialogueScoped").ValueKind == JsonValueKind.Null ? null : vector.GetProperty("dialogueScoped").GetBoolean()),
                "Independent fixture must preserve UPD lifetime evidence.");
            foreach (JsonElement query in vector.GetProperty("queries").EnumerateArray())
            {
                Verify(result.GetOperationEvidence(result.Accounts[query.GetProperty("account").GetInt32()], query.GetProperty("operation").GetString()!).ToString() == query.GetProperty("expected").GetString(),
                    "Independent advertised/blocked/unknown/ambiguous permission evidence must agree.");
            }
        }
        Verify(vectors == 5, "All five independent parameter fixtures must execute.");
        Console.WriteLine($"FinTS BPD/UPD parameters: {vectors} independent vectors and {count} checks completed.");
    }
    private static string Text(FinTsDataElement element) => Encoding.Latin1.GetString(element.CopyValueBytes());
    private static string[] BaseFields() => [Secret + "::280:10020030", "SYNTHETIC-NOT-IBAN", "CUSTOMER", "1", "EUR", "Synthetic Owner", "", "", ""];
    private static string Account(params string[] permissions) => string.Join('+', BaseFields().Concat(permissions));
    private static FinTsParameterSet Parse(params string[] parts)
    {
        StringBuilder body = new("HIRMG:2:2+0010::received'");
        int number = 3;
        foreach (string part in parts)
        {
            int colon = part.IndexOf(':');
            body.Append(part.AsSpan(0, colon)).Append(':').Append((number++).ToString(CultureInfo.InvariantCulture)).Append(part.AsSpan(colon)).Append('\'');
        }
        string template = "HNHBK:1:3+000000000000+300+D+1+D:1'" + body + $"HNHBS:{number}:1+1'";
        string wire = template.Replace("000000000000", Encoding.Latin1.GetByteCount(template).ToString("D12", CultureInfo.InvariantCulture), StringComparison.Ordinal);
        return FinTsParameterSet.Parse(FinTsResponse.Parse(FinTsMessageFrame.Parse(Encoding.Latin1.GetBytes(wire))));
    }
}
