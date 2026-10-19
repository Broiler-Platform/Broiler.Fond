using System.Globalization;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;

namespace Broiler.Fond.Kernel.Hosting;

/// <summary>
/// Detailed result of platform qualification checks.
/// </summary>
public sealed class HostPlatformQualificationResult
{
    public HostPlatformQualificationResult(
        bool isQualified,
        IReadOnlyList<string> checksPassed,
        IReadOnlyList<string> violations,
        string platformDescription)
    {
        IsQualified = isQualified;
        ChecksPassed = checksPassed ?? [];
        Violations = violations ?? [];
        PlatformDescription = platformDescription ?? string.Empty;
    }

    public bool IsQualified { get; }
    public IReadOnlyList<string> ChecksPassed { get; }
    public IReadOnlyList<string> Violations { get; }
    public string PlatformDescription { get; }
}

/// <summary>
/// Exception thrown when the host execution environment fails qualification invariants.
/// </summary>
public sealed class HostPlatformQualificationException : PlatformNotSupportedException
{
    public HostPlatformQualificationResult Result { get; }

    public HostPlatformQualificationException(string message, HostPlatformQualificationResult result)
        : base(message)
    {
        Result = result;
    }
}

/// <summary>
/// Automated qualification engine verifying critical host runtime primitives:
/// 1. AesGcm hardware/runtime support and authenticated encryption integrity.
/// 2. Unicode FormC normalization and strict UTF-8 serialization.
/// 3. Invariant culture globalization (formatting, parsing, case-folding).
/// Invariant: Pure BCL implementation; zero OS branching or native interop in kernel.
/// </summary>
public static class HostPlatformQualification
{
    private static readonly UTF8Encoding StrictUtf8 = new(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true);

    /// <summary>
    /// Executes all platform qualification checks and returns an immutable verification result.
    /// </summary>
    public static HostPlatformQualificationResult Qualify()
    {
        List<string> checksPassed = [];
        List<string> violations = [];

        string platformDesc = $"{RuntimeInformation.OSDescription} ({RuntimeInformation.ProcessArchitecture}) - {RuntimeInformation.FrameworkDescription}";

        // 1. AesGcm Support & Cryptographic Integrity
        VerifyAesGcmSupport(checksPassed, violations);

        // 2. Unicode FormC Normalization & UTF-8 Encoding
        VerifyUnicodeNormalization(checksPassed, violations);

        // 3. Invariant Culture Globalization & Lexical Determinism
        VerifyInvariantCultureFormatting(checksPassed, violations);

        bool isQualified = violations.Count == 0;
        return new HostPlatformQualificationResult(isQualified, checksPassed, violations, platformDesc);
    }

    /// <summary>
    /// Qualifies the current host platform and throws <see cref="HostPlatformQualificationException"/> if non-conforming.
    /// </summary>
    public static void EnsureQualified()
    {
        var result = Qualify();
        if (!result.IsQualified)
        {
            string message = $"Host platform failed qualification with {result.Violations.Count} violation(s): {string.Join("; ", result.Violations)}";
            throw new HostPlatformQualificationException(message, result);
        }
    }

    private static void VerifyAesGcmSupport(List<string> checksPassed, List<string> violations)
    {
        if (!AesGcm.IsSupported)
        {
            violations.Add("AesGcm.IsSupported is false. Hardware or runtime AES-GCM support is required.");
            return;
        }

        try
        {
            byte[] key = new byte[32];
            byte[] nonce = new byte[12];
            byte[] plaintext = new byte[64];
            byte[] aad = new byte[16];
            byte[] ciphertext = new byte[64];
            byte[] tag = new byte[16];
            byte[] decrypted = new byte[64];

            RandomNumberGenerator.Fill(key);
            RandomNumberGenerator.Fill(nonce);
            RandomNumberGenerator.Fill(plaintext);
            RandomNumberGenerator.Fill(aad);

            using (var aes = new AesGcm(key, 16))
            {
                aes.Encrypt(nonce, plaintext, ciphertext, tag, aad);
                aes.Decrypt(nonce, ciphertext, tag, decrypted, aad);
            }

            if (!CryptographicOperations.FixedTimeEquals(plaintext, decrypted))
            {
                violations.Add("AesGcm encryption/decryption roundtrip produced mismatched plaintext.");
                return;
            }

            // Verify authentication tag failure on bit-flip
            tag[0] ^= 0x01;
            bool failedAsExpected = false;
            try
            {
                using var aes = new AesGcm(key, 16);
                aes.Decrypt(nonce, ciphertext, tag, decrypted, aad);
            }
            catch (CryptographicException)
            {
                failedAsExpected = true;
            }

            if (!failedAsExpected)
            {
                violations.Add("AesGcm failed to reject a corrupted authentication tag.");
                return;
            }

            checksPassed.Add("AesGcm authenticated symmetric encryption verified (256-bit key, 96-bit nonce, 128-bit tag).");
        }
        catch (Exception ex)
        {
            violations.Add($"AesGcm qualification threw unexpected exception: {ex.Message}");
        }
    }

    private static void VerifyUnicodeNormalization(List<string> checksPassed, List<string> violations)
    {
        try
        {
            // Test composed vs decomposed equivalence under FormC
            // "München" (composed U+00FC vs decomposed u + combining diaeresis U+0308)
            string composedMunich = "M\u00FCnchen";
            string decomposedMunich = "Mu\u0308nchen";

            if (string.Equals(composedMunich, decomposedMunich, StringComparison.Ordinal))
            {
                violations.Add("Composed and decomposed test strings must not be equal before normalization.");
                return;
            }

            string normalizedComposed = composedMunich.Normalize(NormalizationForm.FormC);
            string normalizedDecomposed = decomposedMunich.Normalize(NormalizationForm.FormC);

            if (!string.Equals(normalizedComposed, normalizedDecomposed, StringComparison.Ordinal))
            {
                violations.Add("Unicode FormC normalization failed to equate composed and decomposed Munich strings.");
                return;
            }

            // Test additional multilingual forms: Zurich, Café, Straße
            (string comp, string decomp)[] pairs =
            [
                ("Z\u00FCrich", "Zu\u0308rich"),
                ("Caf\u00E9", "Cafe\u0301"),
                ("Sch\u00F6n", "Scho\u0308n")
            ];

            foreach (var (comp, decomp) in pairs)
            {
                if (!string.Equals(comp.Normalize(NormalizationForm.FormC), decomp.Normalize(NormalizationForm.FormC), StringComparison.Ordinal))
                {
                    violations.Add($"Unicode FormC normalization failed for pair ({comp}, {decomp}).");
                    return;
                }
            }

            // Strict UTF-8 serialization check
            byte[] encodedBytes = StrictUtf8.GetBytes(normalizedDecomposed);
            string decoded = StrictUtf8.GetString(encodedBytes);
            if (!string.Equals(normalizedDecomposed, decoded, StringComparison.Ordinal))
            {
                violations.Add("Strict UTF-8 roundtrip produced mismatched string.");
                return;
            }

            // Verify strict UTF-8 rejects invalid byte sequences
            byte[] invalidSequence = [0xC3, 0x28]; // invalid continuation byte
            bool threwOnInvalid = false;
            try
            {
                _ = StrictUtf8.GetString(invalidSequence);
            }
            catch (DecoderFallbackException)
            {
                threwOnInvalid = true;
            }

            if (!threwOnInvalid)
            {
                violations.Add("Strict UTF-8 failed to reject invalid multi-byte sequence.");
                return;
            }

            checksPassed.Add("Unicode FormC normalization and strict UTF-8 encoding verified across multilingual vectors.");
        }
        catch (Exception ex)
        {
            violations.Add($"Unicode normalization qualification threw unexpected exception: {ex.Message}");
        }
    }

    private static void VerifyInvariantCultureFormatting(List<string> checksPassed, List<string> violations)
    {
        try
        {
            // Decimal formatting and parsing
            decimal money = 1234567.89m;
            string formattedMoney = money.ToString("F2", CultureInfo.InvariantCulture);
            if (formattedMoney != "1234567.89")
            {
                violations.Add($"Invariant decimal formatting produced unexpected token '{formattedMoney}'. Expected '1234567.89'.");
                return;
            }

            if (!decimal.TryParse(formattedMoney, NumberStyles.Number, CultureInfo.InvariantCulture, out decimal parsedMoney) || parsedMoney != money)
            {
                violations.Add("Invariant decimal parsing failed to roundtrip exact money amount.");
                return;
            }

            // ISO 8601 DateOnly and DateTimeOffset
            DateOnly date = new(2026, 10, 19);
            string formattedDate = date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
            if (formattedDate != "2026-10-19")
            {
                violations.Add($"Invariant DateOnly formatting produced '{formattedDate}'. Expected '2026-10-19'.");
                return;
            }

            DateTimeOffset timestamp = new(2026, 10, 19, 15, 30, 0, TimeSpan.Zero);
            string formattedTimestamp = timestamp.ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture);
            if (formattedTimestamp != "2026-10-19T15:30:00Z")
            {
                violations.Add($"Invariant DateTimeOffset formatting produced '{formattedTimestamp}'. Expected '2026-10-19T15:30:00Z'.");
                return;
            }

            // Invariant case folding
            string lower = "i";
            string upper = lower.ToUpperInvariant();
            if (upper != "I" || upper.ToLowerInvariant() != "i")
            {
                violations.Add("Invariant culture case folding produced unexpected character mapping.");
                return;
            }

            checksPassed.Add("Invariant culture formatting and parsing verified (decimal dot, ISO 8601 timestamps, case folding).");
        }
        catch (Exception ex)
        {
            violations.Add($"Invariant culture qualification threw unexpected exception: {ex.Message}");
        }
    }
}
