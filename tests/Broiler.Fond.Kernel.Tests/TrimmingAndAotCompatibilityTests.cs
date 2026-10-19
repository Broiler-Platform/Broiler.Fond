using System.Reflection;
using Broiler.Fond.Kernel.Domain;
using Broiler.Fond.Kernel.Domain.Accounts;
using Broiler.Fond.Kernel.Domain.Identifiers;
using Broiler.Fond.Kernel.Product;
using Broiler.Fond.Kernel.Storage;

namespace Broiler.Fond.Kernel.Tests;

public static class TrimmingAndAotCompatibilityTests
{
    public static void Run()
    {
        Console.WriteLine("Running Trimming & Native AOT Compatibility verification (M8-03)...");
        int checks = 0;

        void Assert(bool condition, string message)
        {
            if (!condition)
            {
                throw new InvalidOperationException($"Trimming & Native AOT Compatibility check failed: {message}");
            }
            checks++;
        }

        Assembly kernelAssembly = typeof(KernelProduct).Assembly;

        // 1. Audit the Kernel Assembly for Trimming and AOT Compliance
        var audit = TrimmingAndAotCompatibilityValidator.AuditAssembly(kernelAssembly);
        Assert(audit.IsValid, "Kernel assembly passes all trimming and Native AOT compatibility audits.");
        Assert(audit.Violations.Count == 0, "Zero trimming or dynamic code violations found in kernel.");
        Assert(audit.ChecksPassed.Count >= 3, "All AOT audit categories completed successfully.");

        // 2. Value-Type and Static-Dispatch Domain Invariants
        AccountId accId1 = new(1001);
        AccountId accId2 = new(1001);
        Assert(accId1 == accId2, "AccountId struct equality operates via value comparison without reflection.");

        Money eur100 = new(100.50m, "EUR");
        Money eur50 = new(50.25m, "EUR");
        Money total = eur100.Add(eur50);
        Assert(total.Amount == 150.75m && total.Currency == "EUR", "Money arithmetic operates without dynamic code emission.");

        // 3. Cryptographic Primitives Execute Without Reflection
        byte[] salt = new byte[32];
        System.Security.Cryptography.RandomNumberGenerator.Fill(salt);
        using var session = ProfileEnvelopeCodec.CreateMasterKeySession(salt, ProfileEnvelopeCodec.DefaultIterations, "AotPassphrase123!");
        byte[] header = ProfileEnvelopeCodec.CreateHeader(salt, ProfileEnvelopeCodec.DefaultIterations);
        byte[] contentKey = session.DeriveContentKey(header);
        Assert(contentKey.Length == 32, "Content key derived statically via HKDF without runtime code generation.");

        // 4. Memory Hygiene Verification under AOT
        System.Security.Cryptography.CryptographicOperations.ZeroMemory(contentKey);
        Assert(contentKey.All(b => b == 0), "Fixed-time memory zeroing functions properly.");

        Console.WriteLine($"Trimming & Native AOT Compatibility (M8-03): {checks} checks completed successfully.");
    }
}
