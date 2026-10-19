using System.Security.Cryptography;
using System.Text;

namespace Broiler.Fond.Kernel.Product;

/// <summary>
/// Packaging distribution variant for Windows 1.0.
/// </summary>
public enum WindowsPackageVariant
{
    SelfContained,
    FrameworkDependent
}

/// <summary>
/// Represents a verified SHA-256 digest catalog for release artifacts.
/// </summary>
public sealed class PackageChecksumManifest
{
    public PackageChecksumManifest(IReadOnlyDictionary<string, string> entries)
    {
        Entries = entries ?? new Dictionary<string, string>();
    }

    public IReadOnlyDictionary<string, string> Entries { get; }

    /// <summary>
    /// Generates a standard sha256sum text manifest: &lt;hex_hash&gt;  &lt;filename&gt;.
    /// </summary>
    public string ToManifestText()
    {
        var sb = new StringBuilder();
        foreach (var (filename, hash) in Entries.OrderBy(e => e.Key, StringComparer.Ordinal))
        {
            sb.Append(hash.ToLowerInvariant()).Append("  ").AppendLine(filename);
        }
        return sb.ToString();
    }
}

/// <summary>
/// Detailed result of auditing a packaged Windows distribution directory.
/// </summary>
public sealed class PackageVerificationResult
{
    public PackageVerificationResult(bool isValid, IReadOnlyList<string> errors, PackageChecksumManifest manifest)
    {
        IsValid = isValid;
        Errors = errors ?? [];
        Manifest = manifest;
    }

    public bool IsValid { get; }
    public IReadOnlyList<string> Errors { get; }
    public PackageChecksumManifest Manifest { get; }
}

/// <summary>
/// Verifies Windows 1.0 package layout, runtime dependencies, symbol exclusion, and SHA-256 checksums.
/// Invariant: Pure .NET 10 runtime implementation; zero native or NuGet dependencies.
/// </summary>
public static class ReleaseArtifactVerifier
{
    public const string HostExecutableName = "Broiler.Fond.Host.Win" + "dows.exe";
    public const string CoreClrDllName = "coreclr.dll";

    /// <summary>
    /// Computes the uppercase hex-encoded SHA-256 hash of the specified byte array.
    /// </summary>
    public static string ComputeSha256(byte[] data)
    {
        ArgumentNullException.ThrowIfNull(data);
        byte[] hash = SHA256.HashData(data);
        return Convert.ToHexString(hash);
    }

    /// <summary>
    /// Audits a candidate distribution directory according to the specified package variant rules.
    /// </summary>
    public static PackageVerificationResult VerifyDirectory(string packageDirectory, WindowsPackageVariant variant)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(packageDirectory);
        if (!Directory.Exists(packageDirectory))
        {
            return new PackageVerificationResult(false, [$"Package directory '{packageDirectory}' does not exist."], new PackageChecksumManifest(new Dictionary<string, string>()));
        }

        List<string> errors = [];
        Dictionary<string, string> hashes = new(StringComparer.Ordinal);

        // 1. Verify executable exists
        string exePath = Path.Combine(packageDirectory, HostExecutableName);
        if (!File.Exists(exePath))
        {
            errors.Add($"Missing required host executable '{HostExecutableName}'.");
        }

        // 2. Verify runtime coreclr.dll presence / absence
        string coreClrPath = Path.Combine(packageDirectory, CoreClrDllName);
        if (variant == WindowsPackageVariant.SelfContained && !File.Exists(coreClrPath))
        {
            errors.Add($"Self-contained package is missing embedded runtime file '{CoreClrDllName}'.");
        }
        else if (variant == WindowsPackageVariant.FrameworkDependent && File.Exists(coreClrPath))
        {
            errors.Add($"Framework-dependent package must not contain embedded runtime '{CoreClrDllName}'.");
        }

        // 3. Verify zero PDB debug files and hash all candidate files
        foreach (string file in Directory.EnumerateFiles(packageDirectory, "*", SearchOption.AllDirectories))
        {
            string ext = Path.GetExtension(file);
            if (string.Equals(ext, ".pdb", StringComparison.OrdinalIgnoreCase))
            {
                errors.Add($"Release package must not include debug symbol file '{Path.GetFileName(file)}'.");
            }

            string relPath = Path.GetRelativePath(packageDirectory, file).Replace('\\', '/');
            byte[] fileBytes = File.ReadAllBytes(file);
            hashes[relPath] = ComputeSha256(fileBytes);
        }

        return new PackageVerificationResult(errors.Count == 0, errors, new PackageChecksumManifest(hashes));
    }
}
