using System.Text;
using Broiler.Fond.Kernel.Product;

namespace Broiler.Fond.Kernel.Tests;

public static class PackagingAndReleaseLifecycleTests
{
    public static void Run()
    {
        Console.WriteLine("Running Packaging & Release Lifecycle verification (M7-05)...");
        int checks = 0;

        void Assert(bool condition, string message)
        {
            if (!condition)
            {
                throw new InvalidOperationException($"Packaging & Release Lifecycle check failed: {message}");
            }
            checks++;
        }

        // 1. SHA-256 test vectors
        string emptyHash = ReleaseArtifactVerifier.ComputeSha256([]);
        Assert(emptyHash == "E3B0C44298FC1C149AFBF4C8996FB92427AE41E4649B934CA495991B7852B855", "Empty bytes SHA-256 matches NIST test vector.");

        byte[] testData = Encoding.UTF8.GetBytes("Broiler.Fond-Windows-1.0-Release-Verification");
        string testHash = ReleaseArtifactVerifier.ComputeSha256(testData);
        Assert(testHash.Length == 64, "SHA-256 hex string is 64 characters.");

        // 2. Checksum manifest formatting
        var manifest = new PackageChecksumManifest(new Dictionary<string, string>
        {
            ["Broiler.Fond.Host.Windows.exe"] = "AAAA1111BBBB2222CCCC3333DDDD4444EEEE5555FFFF66667777888899990000",
            ["Broiler.Fond.Kernel.dll"] = "1111222233334444555566667777888899990000AAAABBBBCCCCDDDDEEEEFFFF"
        });

        string manifestText = manifest.ToManifestText();
        Assert(manifestText.Contains("aaaa1111bbbb2222cccc3333dddd4444eeee5555ffff66667777888899990000  Broiler.Fond.Host.Windows.exe"), "Manifest text formats lowercase hash and filename.");
        Assert(manifestText.Contains("1111222233334444555566667777888899990000aaaabbbbccccddddeeeeffff  Broiler.Fond.Kernel.dll"), "Manifest text contains all entries.");

        // 3. Package directory structure auditing
        string tempDir = Path.Combine(Path.GetTempPath(), "BroilerFondPkgTest", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);

        try
        {
            // Missing executable -> fails
            var emptyResult = ReleaseArtifactVerifier.VerifyDirectory(tempDir, WindowsPackageVariant.SelfContained);
            Assert(!emptyResult.IsValid, "Empty directory fails package verification.");
            Assert(emptyResult.Errors.Any(e => e.Contains("Missing required host executable")), "Reports missing executable.");

            // Create host executable
            string exePath = Path.Combine(tempDir, ReleaseArtifactVerifier.HostExecutableName);
            File.WriteAllBytes(exePath, [0x4D, 0x5A, 0x90, 0x00]); // MZ header mock

            // Framework-dependent without coreclr -> valid
            var fdResult = ReleaseArtifactVerifier.VerifyDirectory(tempDir, WindowsPackageVariant.FrameworkDependent);
            Assert(fdResult.IsValid, "Framework-dependent package without coreclr.dll is valid.");
            Assert(fdResult.Manifest.Entries.ContainsKey(ReleaseArtifactVerifier.HostExecutableName), "Manifest contains executable.");

            // Self-contained without coreclr -> invalid
            var scMissingResult = ReleaseArtifactVerifier.VerifyDirectory(tempDir, WindowsPackageVariant.SelfContained);
            Assert(!scMissingResult.IsValid, "Self-contained package missing coreclr.dll is invalid.");

            // Add coreclr.dll
            string coreClrPath = Path.Combine(tempDir, ReleaseArtifactVerifier.CoreClrDllName);
            File.WriteAllBytes(coreClrPath, [0x4D, 0x5A, 0x00, 0x01]);

            // Self-contained with coreclr -> valid
            var scValidResult = ReleaseArtifactVerifier.VerifyDirectory(tempDir, WindowsPackageVariant.SelfContained);
            Assert(scValidResult.IsValid, "Self-contained package with coreclr.dll is valid.");

            // Framework-dependent with coreclr -> invalid
            var fdWithCoreClr = ReleaseArtifactVerifier.VerifyDirectory(tempDir, WindowsPackageVariant.FrameworkDependent);
            Assert(!fdWithCoreClr.IsValid, "Framework-dependent package must not contain coreclr.dll.");

            // Add forbidden PDB file
            string pdbPath = Path.Combine(tempDir, "Broiler.Fond.Host.Windows.pdb");
            File.WriteAllBytes(pdbPath, [0x00, 0x00, 0x00]);

            var pdbResult = ReleaseArtifactVerifier.VerifyDirectory(tempDir, WindowsPackageVariant.SelfContained);
            Assert(!pdbResult.IsValid, "Package with PDB debug file is rejected.");
            Assert(pdbResult.Errors.Any(e => e.Contains("debug symbol file")), "Reports forbidden PDB file.");
        }
        finally
        {
            try
            {
                if (Directory.Exists(tempDir))
                {
                    Directory.Delete(tempDir, recursive: true);
                }
            }
            catch { }
        }

        Console.WriteLine($"Packaging & Release Lifecycle (M7-05): {checks} checks completed successfully.");
    }
}
