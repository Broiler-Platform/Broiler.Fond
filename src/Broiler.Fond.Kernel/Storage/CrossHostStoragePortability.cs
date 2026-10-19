using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;

namespace Broiler.Fond.Kernel.Storage;

/// <summary>
/// Result of auditing a profile storage archive for cross-platform portability.
/// </summary>
public sealed class CrossHostPortabilityAuditResult
{
    public CrossHostPortabilityAuditResult(bool isValid, IReadOnlyList<string> checksPassed, IReadOnlyList<string> errors)
    {
        IsValid = isValid;
        ChecksPassed = checksPassed ?? [];
        Errors = errors ?? [];
    }

    public bool IsValid { get; }
    public IReadOnlyList<string> ChecksPassed { get; }
    public IReadOnlyList<string> Errors { get; }
}

/// <summary>
/// Verifies storage archive portability invariants across Windows, Linux, and macOS:
/// 1. Zero CRLF (\r\n) line endings in XML documents (strict LF-only).
/// 2. Canonical flat or forward-slash ZIP entry paths without backslashes or traversal tokens.
/// 3. Passphrase Unicode FormC normalization ensuring cross-host PBKDF2 key equivalence.
/// 4. Generation filename parsing compatibility across POSIX and Windows paths.
/// </summary>
public static class CrossHostStoragePortability
{
    private static readonly UTF8Encoding StrictUtf8 = new(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true);

    /// <summary>
    /// Audits a serialized store ZIP archive for cross-platform lexical and structure invariants.
    /// </summary>
    public static CrossHostPortabilityAuditResult AuditZipBytes(byte[] zipBytes)
    {
        ArgumentNullException.ThrowIfNull(zipBytes);

        List<string> passed = [];
        List<string> errors = [];

        // 1. Hardened preflight check
        try
        {
            var preflightResult = ZipPreflight.Validate(zipBytes);
            passed.Add($"ZIP preflight validated {preflightResult.Entries.Count} entries.");
        }
        catch (Exception ex)
        {
            errors.Add($"ZIP preflight rejected archive: {ex.Message}");
            return new CrossHostPortabilityAuditResult(false, passed, errors);
        }

        using MemoryStream ms = new(zipBytes, writable: false);
        using ZipArchive archive = new(ms, ZipArchiveMode.Read, leaveOpen: false, entryNameEncoding: Encoding.UTF8);

        foreach (ZipArchiveEntry entry in archive.Entries)
        {
            // Verify path separator rules: must never contain backslashes or traversal
            if (entry.FullName.Contains('\\'))
            {
                errors.Add($"ZIP entry '{entry.FullName}' contains forbidden Windows backslash separator.");
            }
            if (entry.FullName.StartsWith('/') || entry.FullName.Contains(".."))
            {
                errors.Add($"ZIP entry '{entry.FullName}' contains forbidden absolute or directory traversal path.");
            }

            // Verify entry uncompressed bytes
            using Stream entryStream = entry.Open();
            using MemoryStream entryBuffer = new();
            entryStream.CopyTo(entryBuffer);
            byte[] rawBytes = entryBuffer.ToArray();

            // Strict UTF-8 validation
            string text;
            try
            {
                text = StrictUtf8.GetString(rawBytes);
            }
            catch (DecoderFallbackException)
            {
                errors.Add($"ZIP entry '{entry.FullName}' contains invalid non-UTF-8 byte sequences.");
                continue;
            }

            // Verify LF-only line endings (no \r / 0x0D bytes allowed anywhere in XML body)
            if (rawBytes.Contains((byte)0x0D))
            {
                errors.Add($"ZIP entry '{entry.FullName}' contains non-canonical Carriage Return (\\r / 0x0D) line endings.");
            }
            else
            {
                passed.Add($"Entry '{entry.FullName}' verified: pure LF line endings, strict UTF-8 ({rawBytes.Length} bytes).");
            }

            // Verify XML declaration starts with standard declaration
            if (!text.StartsWith("<?xml version=\"1.0\" encoding=\"utf-8\"?>", StringComparison.Ordinal))
            {
                errors.Add($"ZIP entry '{entry.FullName}' missing canonical XML declaration header.");
            }
        }

        return new CrossHostPortabilityAuditResult(errors.Count == 0, passed, errors);
    }

    /// <summary>
    /// Audits a store snapshot by serializing it and verifying the resulting archive.
    /// </summary>
    public static CrossHostPortabilityAuditResult AuditSnapshotArchive(StoreSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        byte[] zipBytes = StoreArchiveCodec.CreateStoreZip(snapshot);
        return AuditZipBytes(zipBytes);
    }

    /// <summary>
    /// Verifies that composed and decomposed Unicode representations of a passphrase derive identical master keys.
    /// </summary>
    public static bool VerifyNormalizedPassphraseDerivation(
        string composedPassphrase,
        string decomposedPassphrase,
        byte[] profileSalt,
        int iterations = ProfileEnvelopeCodec.DefaultIterations)
    {
        ArgumentNullException.ThrowIfNull(composedPassphrase);
        ArgumentNullException.ThrowIfNull(decomposedPassphrase);
        ArgumentNullException.ThrowIfNull(profileSalt);

        using var sessionComposed = ProfileEnvelopeCodec.CreateMasterKeySession(profileSalt, iterations, composedPassphrase);
        using var sessionDecomposed = ProfileEnvelopeCodec.CreateMasterKeySession(profileSalt, iterations, decomposedPassphrase);

        byte[] header = ProfileEnvelopeCodec.CreateHeader(profileSalt, iterations);

        byte[] key1 = sessionComposed.DeriveContentKey(header);
        byte[] key2 = sessionDecomposed.DeriveContentKey(header);

        bool match = CryptographicOperations.FixedTimeEquals(key1, key2);

        CryptographicOperations.ZeroMemory(key1);
        CryptographicOperations.ZeroMemory(key2);

        return match;
    }

    /// <summary>
    /// Verifies generation filename parsing on POSIX (/path/to/file) and Windows (C:\path\to\file) paths.
    /// </summary>
    public static bool VerifyPathParsing(string posixPath, string windowsPath, ulong expectedGeneration)
    {
        string posixName = Path.GetFileName(posixPath.Replace('\\', '/'));
        string windowsName = Path.GetFileName(windowsPath.Replace('/', '\\'));

        bool posixOk = ProfileStore.TryParseGenerationFileName(posixName, out ulong gen1) && gen1 == expectedGeneration;
        bool windowsOk = ProfileStore.TryParseGenerationFileName(windowsName, out ulong gen2) && gen2 == expectedGeneration;

        return posixOk && windowsOk;
    }
}
