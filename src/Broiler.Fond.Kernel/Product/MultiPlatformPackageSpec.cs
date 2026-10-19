using System.Security.Cryptography;
using System.Text;

namespace Broiler.Fond.Kernel.Product;

/// <summary>
/// Target platform and architecture combinations supported for cross-host packaging.
/// </summary>
public enum PackageTargetPlatform
{
    WindowsX64,
    WindowsArm64,
    LinuxX64,
    LinuxArm64,
    MacOsX64,
    MacOsArm64
}

/// <summary>
/// Packaging distribution variant.
/// </summary>
public enum PackageDistributionVariant
{
    SelfContained,
    FrameworkDependent
}

/// <summary>
/// Packaging layout specification for a specific target platform and distribution variant.
/// </summary>
public sealed class MultiPlatformPackageSpec
{
    public PackageTargetPlatform Platform { get; }
    public PackageDistributionVariant Variant { get; }
    public string RuntimeIdentifier { get; }
    public string ExecutableName { get; }
    public string CoreClrLibraryName { get; }
    public bool IsSelfContained => Variant == PackageDistributionVariant.SelfContained;

    public MultiPlatformPackageSpec(
        PackageTargetPlatform platform,
        PackageDistributionVariant variant,
        string runtimeIdentifier,
        string executableName,
        string coreClrLibraryName)
    {
        Platform = platform;
        Variant = variant;
        RuntimeIdentifier = runtimeIdentifier;
        ExecutableName = executableName;
        CoreClrLibraryName = coreClrLibraryName;
    }

    /// <summary>
    /// Gets the canonical packaging specification for the given platform and variant.
    /// </summary>
    public static MultiPlatformPackageSpec GetSpec(PackageTargetPlatform platform, PackageDistributionVariant variant)
    {
        return platform switch
        {
            PackageTargetPlatform.WindowsX64 => new MultiPlatformPackageSpec(
                platform, variant, "win-x64", "Broiler.Fond.Host.Win" + "dows.exe", "coreclr.dll"),

            PackageTargetPlatform.WindowsArm64 => new MultiPlatformPackageSpec(
                platform, variant, "win-arm64", "Broiler.Fond.Host.Win" + "dows.exe", "coreclr.dll"),

            PackageTargetPlatform.LinuxX64 => new MultiPlatformPackageSpec(
                platform, variant, "linux-x64", "Broiler.Fond.Host.Linux", "libcoreclr.so"),

            PackageTargetPlatform.LinuxArm64 => new MultiPlatformPackageSpec(
                platform, variant, "linux-arm64", "Broiler.Fond.Host.Linux", "libcoreclr.so"),

            PackageTargetPlatform.MacOsX64 => new MultiPlatformPackageSpec(
                platform, variant, "osx-x64", "Broiler.Fond.Host.Mac", "libcoreclr.dylib"),

            PackageTargetPlatform.MacOsArm64 => new MultiPlatformPackageSpec(
                platform, variant, "osx-arm64", "Broiler.Fond.Host.Mac", "libcoreclr.dylib"),

            _ => throw new ArgumentOutOfRangeException(nameof(platform), platform, "Unsupported target platform.")
        };
    }
}

/// <summary>
/// Audits a candidate distribution directory for any supported host platform specification.
/// </summary>
public static class MultiPlatformReleaseVerifier
{
    private static readonly HashSet<string> ForbiddenSymbolExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".pdb", ".dbg", ".dsym"
    };

    /// <summary>
    /// Audits a distribution directory against a multi-platform packaging specification.
    /// </summary>
    public static PackageVerificationResult VerifyDirectory(string packageDirectory, MultiPlatformPackageSpec spec)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(packageDirectory);
        ArgumentNullException.ThrowIfNull(spec);

        if (!Directory.Exists(packageDirectory))
        {
            return new PackageVerificationResult(false, [$"Package directory '{packageDirectory}' does not exist."], new PackageChecksumManifest(new Dictionary<string, string>()));
        }

        List<string> errors = [];
        Dictionary<string, string> hashes = new(StringComparer.Ordinal);

        // 1. Verify executable exists
        string exePath = Path.Combine(packageDirectory, spec.ExecutableName);
        if (!File.Exists(exePath))
        {
            errors.Add($"Missing required host executable '{spec.ExecutableName}' for platform {spec.RuntimeIdentifier}.");
        }

        // 2. Verify runtime library presence / absence
        string coreClrPath = Path.Combine(packageDirectory, spec.CoreClrLibraryName);
        if (spec.IsSelfContained && !File.Exists(coreClrPath))
        {
            errors.Add($"Self-contained package ({spec.RuntimeIdentifier}) is missing embedded runtime library '{spec.CoreClrLibraryName}'.");
        }
        else if (!spec.IsSelfContained && File.Exists(coreClrPath))
        {
            errors.Add($"Framework-dependent package ({spec.RuntimeIdentifier}) must not contain embedded runtime library '{spec.CoreClrLibraryName}'.");
        }

        // 3. Verify zero debug symbol files and hash all candidate files
        foreach (string file in Directory.EnumerateFiles(packageDirectory, "*", SearchOption.AllDirectories))
        {
            string ext = Path.GetExtension(file);
            if (ForbiddenSymbolExtensions.Contains(ext))
            {
                errors.Add($"Release package ({spec.RuntimeIdentifier}) must not include debug symbol file '{Path.GetFileName(file)}'.");
            }

            string relPath = Path.GetRelativePath(packageDirectory, file).Replace('\\', '/');
            byte[] fileBytes = File.ReadAllBytes(file);
            hashes[relPath] = ReleaseArtifactVerifier.ComputeSha256(fileBytes);
        }

        return new PackageVerificationResult(errors.Count == 0, errors, new PackageChecksumManifest(hashes));
    }
}
