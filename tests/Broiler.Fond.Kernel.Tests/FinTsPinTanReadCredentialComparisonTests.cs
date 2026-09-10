using System.Globalization;
using System.Reflection;
using System.Security.Cryptography;
using System.Text.Json;
using Broiler.Fond.Kernel.FinTs;

namespace Broiler.Fond.Kernel.Tests;

internal static class FinTsPinTanReadCredentialComparisonTests
{
    internal static void Run(Action<bool, string> check)
    {
        int count = 0;
        void Verify(bool condition, string message) { count++; check(condition, message); }
        using var document = JsonDocument.Parse(Assembly.GetExecutingAssembly().GetManifestResourceStream("FinTs.pin-tan-read-credential-comparison-v1.json")!);
        var vectors = document.RootElement.GetProperty("vectors").EnumerateArray().ToArray();
        foreach (var vector in vectors)
        {
            var context = Context(vector);
            byte[] value = Convert.FromBase64String(vector.GetProperty("valueBase64").GetString()!);
            byte[] original = value.ToArray();
            try
            {
                var expected = vector.GetProperty("issues").EnumerateArray().Aggregate(FinTsPinTanReadCredentialIssue.None,
                    (flags, item) => flags | Enum.Parse<FinTsPinTanReadCredentialIssue>(item.GetString()!));
                var kind = Enum.Parse<FinTsCredentialKind>(vector.GetProperty("kind").GetString()!);
                var actual = FinTsPinTanReadCredentialComparison.Compare(context, kind, value);
                Verify(actual == expected, "Independent credential comparison: " + vector.GetProperty("name").GetString() + " (" + actual + ").");
                Verify(value.SequenceEqual(original), "Caller-owned credential input remains unchanged on every comparison outcome.");
                Verify(FinTsPinTanReadCredentialComparison.Compare(context, kind, value) == actual, "Comparison is repeatable, with no credential consumption or retry state.");
            }
            finally { CryptographicOperations.ZeroMemory(value); CryptographicOperations.ZeroMemory(original); }
        }
        Verify(vectors.Length == 45, "All forty-five independent credential-comparison fixtures ran.");
        var pinContext = Context(vectors[0]);
        var numericContext = Context(vectors[16]);
        var textContext = Context(vectors[30]);
        var printable = Enumerable.Range(32, 95).Concat(Enumerable.Range(161, 95)).Select(n => (byte)n).ToHashSet();
        var digits = "0123456789"u8.ToArray().ToHashSet();
        for (int octet = 0; octet <= byte.MaxValue; octet++)
        {
            byte[] value = Enumerable.Repeat((byte)octet, 6).ToArray();
            try
            {
                var textIssue = printable.Contains((byte)octet) ? FinTsPinTanReadCredentialIssue.None : FinTsPinTanReadCredentialIssue.InvalidText;
                var numericIssue = textIssue | (digits.Contains((byte)octet) ? FinTsPinTanReadCredentialIssue.None : FinTsPinTanReadCredentialIssue.TanFormatMismatch);
                Verify(FinTsPinTanReadCredentialComparison.Compare(pinContext, FinTsCredentialKind.Pin, value) == textIssue &&
                    FinTsPinTanReadCredentialComparison.Compare(textContext, FinTsCredentialKind.Tan, value) == textIssue &&
                    FinTsPinTanReadCredentialComparison.Compare(numericContext, FinTsCredentialKind.Tan, value) == numericIssue,
                    "Every octet follows the supported text and numeric TAN alphabets.");
                Verify(value.All(b => b == octet), "Alphabet checks neither normalize nor erase caller input.");
            }
            finally { CryptographicOperations.ZeroMemory(value); }
        }
        byte[] sample = "PUBLIC-PIN"u8.ToArray();
        try
        {
            var outcomes = new FinTsPinTanReadCredentialIssue[16];
            Parallel.For(0, outcomes.Length, i => outcomes[i] = FinTsPinTanReadCredentialComparison.Compare(pinContext, FinTsCredentialKind.Pin, sample));
            Verify(outcomes.All(i => i == 0) && sample.AsSpan().SequenceEqual("PUBLIC-PIN"u8), "Concurrent immutable input comparisons retain the same bytes and flags.");
            var culture = CultureInfo.CurrentCulture;
            try
            {
                foreach (string name in new[] { "de-DE", "ar-SA", "tr-TR" })
                {
                    CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo(name);
                    Verify(FinTsPinTanReadCredentialComparison.Compare(numericContext, FinTsCredentialKind.Tan, "000123"u8) == 0,
                        "Numeric TAN comparison is culture independent and preserves leading zeroes.");
                }
            }
            finally { CultureInfo.CurrentCulture = culture; }
            using var cancellation = new CancellationTokenSource(); cancellation.Cancel();
            try { FinTsPinTanReadCredentialComparison.Compare(pinContext, FinTsCredentialKind.Pin, sample, cancellation.Token); Verify(false, "Cancelled comparison must throw."); }
            catch (OperationCanceledException error) { Verify(sample.AsSpan().SequenceEqual("PUBLIC-PIN"u8) && !error.ToString().Contains("PUBLIC-PIN", StringComparison.Ordinal), "Cancellation retains caller ownership and exposes no value."); }
            try { FinTsPinTanReadCredentialComparison.Compare(null!, FinTsCredentialKind.Pin, sample); Verify(false, "Null context must fail."); }
            catch (ArgumentNullException error) { Verify(sample.AsSpan().SequenceEqual("PUBLIC-PIN"u8) && !error.ToString().Contains("PUBLIC-PIN", StringComparison.Ordinal), "Null input uses fixed diagnostics without altering credentials."); }
            foreach (int invalid in new[] { -1, 2, int.MaxValue })
            {
                try { FinTsPinTanReadCredentialComparison.Compare(pinContext, (FinTsCredentialKind)invalid, sample); Verify(false, "Undefined credential kind must fail."); }
                catch (ArgumentOutOfRangeException error) { Verify(sample.AsSpan().SequenceEqual("PUBLIC-PIN"u8) && !error.ToString().Contains("PUBLIC-PIN", StringComparison.Ordinal), "Undefined kinds expose no credential data."); }
            }
        }
        finally { CryptographicOperations.ZeroMemory(sample); }
        byte[] delimited = "+:'?@+:'?@"u8.ToArray();
        try { Verify(FinTsPinTanReadCredentialComparison.Compare(pinContext, FinTsCredentialKind.Pin, delimited) == 0, "Length comparison counts unescaped bytes, not the larger future wire representation."); }
        finally { CryptographicOperations.ZeroMemory(delimited); }
        Verify(Enum.GetUnderlyingType(typeof(FinTsPinTanReadCredentialIssue)) == typeof(int), "Comparison returns only scalar flags and retains no input or value length.");
        Console.WriteLine($"FinTS first-read credential requirement comparison passed ({vectors.Length} independent vectors, {count} checks).");
    }

    private static FinTsPinTanReadCapabilityContext Context(JsonElement vector)
    {
        var (signature, account, national) = FinTsPinTanReadCapabilityContextTests.Inputs(vector.GetProperty("context"));
        return FinTsPinTanReadCapabilityContext.Evaluate(signature, account, national);
    }
}
