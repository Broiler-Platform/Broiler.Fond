using Broiler.Fond.Kernel.Product;

namespace Broiler.Fond.Kernel.Tests;

public static class MultiPlatformDistributionTests
{
    public static void Run()
    {
        Console.WriteLine("Running Multi-Platform Distribution verification (M8-05)...");
        int checks = 0;

        void Assert(bool condition, string message)
        {
            if (!condition)
            {
                throw new InvalidOperationException($"Multi-Platform Distribution check failed: {message}");
            }
            checks++;
        }

        // 1. Packaging Specifications for all 6 Canonical Platforms
        (PackageTargetPlatform Platform, string ExpectedRid, string ExpectedExe, string ExpectedClr)[] specs =
        [
            (PackageTargetPlatform.WindowsX64, "win-x64", "Broiler.Fond.Host.Windows.exe", "coreclr.dll"),
            (PackageTargetPlatform.WindowsArm64, "win-arm64", "Broiler.Fond.Host.Windows.exe", "coreclr.dll"),
            (PackageTargetPlatform.LinuxX64, "linux-x64", "Broiler.Fond.Host.Linux", "libcoreclr.so"),
            (PackageTargetPlatform.LinuxArm64, "linux-arm64", "Broiler.Fond.Host.Linux", "libcoreclr.so"),
            (PackageTargetPlatform.MacOsX64, "osx-x64", "Broiler.Fond.Host.Mac", "libcoreclr.dylib"),
            (PackageTargetPlatform.MacOsArm64, "osx-arm64", "Broiler.Fond.Host.Mac", "libcoreclr.dylib")
        ];

        foreach (var (platform, expectedRid, expectedExe, expectedClr) in specs)
        {
            var scSpec = MultiPlatformPackageSpec.GetSpec(platform, PackageDistributionVariant.SelfContained);
            Assert(scSpec.RuntimeIdentifier == expectedRid, $"Platform {platform} has RID {expectedRid}.");
            Assert(scSpec.ExecutableName == expectedExe, $"Platform {platform} has executable {expectedExe}.");
            Assert(scSpec.CoreClrLibraryName == expectedClr, $"Platform {platform} has runtime library {expectedClr}.");
            Assert(scSpec.IsSelfContained, "Self-contained flag set.");

            var fdSpec = MultiPlatformPackageSpec.GetSpec(platform, PackageDistributionVariant.FrameworkDependent);
            Assert(!fdSpec.IsSelfContained, "Framework-dependent flag set.");
        }

        // 2. Package Directory Auditing for Linux (Self-Contained & Framework-Dependent)
        string testDir = Path.Combine(Path.GetTempPath(), "BroilerDistTest", Guid.NewGuid().ToString("N"));
        string linuxScDir = Path.Combine(testDir, "linux-sc");
        string macFdDir = Path.Combine(testDir, "mac-fd");

        Directory.CreateDirectory(linuxScDir);
        Directory.CreateDirectory(macFdDir);

        try
        {
            var linuxScSpec = MultiPlatformPackageSpec.GetSpec(PackageTargetPlatform.LinuxX64, PackageDistributionVariant.SelfContained);

            // Missing executable -> fails
            var emptyAudit = MultiPlatformReleaseVerifier.VerifyDirectory(linuxScDir, linuxScSpec);
            Assert(!emptyAudit.IsValid, "Empty directory fails Linux package verification.");
            Assert(emptyAudit.Errors.Any(e => e.Contains("Missing required host executable")), "Reports missing Linux executable.");

            // Create Linux executable and embedded runtime
            File.WriteAllBytes(Path.Combine(linuxScDir, linuxScSpec.ExecutableName), [0x7F, 0x45, 0x4C, 0x46]); // ELF header mock
            File.WriteAllBytes(Path.Combine(linuxScDir, linuxScSpec.CoreClrLibraryName), [0x7F, 0x45, 0x4C, 0x46]);

            var validLinuxAudit = MultiPlatformReleaseVerifier.VerifyDirectory(linuxScDir, linuxScSpec);
            Assert(validLinuxAudit.IsValid, "Linux self-contained package with executable and runtime is valid.");
            Assert(validLinuxAudit.Manifest.Entries.ContainsKey(linuxScSpec.ExecutableName), "Manifest contains Linux executable.");
            Assert(validLinuxAudit.Manifest.Entries.ContainsKey(linuxScSpec.CoreClrLibraryName), "Manifest contains Linux coreclr.");

            // 3. macOS Framework-Dependent Auditing
            var macFdSpec = MultiPlatformPackageSpec.GetSpec(PackageTargetPlatform.MacOsArm64, PackageDistributionVariant.FrameworkDependent);
            File.WriteAllBytes(Path.Combine(macFdDir, macFdSpec.ExecutableName), [0xCF, 0xFA, 0xED, 0xFE]); // Mach-O header mock

            var validMacAudit = MultiPlatformReleaseVerifier.VerifyDirectory(macFdDir, macFdSpec);
            Assert(validMacAudit.IsValid, "macOS framework-dependent package is valid without runtime library.");

            // Adding coreclr to framework-dependent -> fails
            string forbiddenClrPath = Path.Combine(macFdDir, macFdSpec.CoreClrLibraryName);
            File.WriteAllBytes(forbiddenClrPath, [0xCF, 0xFA, 0xED, 0xFE]);
            var invalidMacAudit = MultiPlatformReleaseVerifier.VerifyDirectory(macFdDir, macFdSpec);
            Assert(!invalidMacAudit.IsValid, "Framework-dependent package must not contain embedded runtime.");
            File.Delete(forbiddenClrPath);

            // Adding debug symbol file -> fails
            string pdbPath = Path.Combine(macFdDir, "Broiler.Fond.Host.pdb");
            File.WriteAllBytes(pdbPath, [0x00]);
            var pdbAudit = MultiPlatformReleaseVerifier.VerifyDirectory(macFdDir, macFdSpec);
            Assert(!pdbAudit.IsValid, "Release package with .pdb symbol file is rejected.");
            Assert(pdbAudit.Errors.Any(e => e.Contains("debug symbol file")), "Reports debug symbol error.");
            File.Delete(pdbPath);
        }
        finally
        {
            try
            {
                Directory.Delete(testDir, recursive: true);
            }
            catch
            {
                // Best effort
            }
        }

        Console.WriteLine($"Multi-Platform Distribution (M8-05): {checks} checks completed successfully.");
    }
}
