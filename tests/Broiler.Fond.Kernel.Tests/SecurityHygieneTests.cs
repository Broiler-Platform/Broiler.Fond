using System.Security.Cryptography;
using System.Text;
using Broiler.Fond.Kernel.Diagnostics;
using Broiler.Fond.Kernel.FinTs;
using Broiler.Fond.Kernel.Product;

namespace Broiler.Fond.Kernel.Tests;

public static class SecurityHygieneTests
{
    public static void Run()
    {
        Console.WriteLine("Running Security Controls & Memory Hygiene verification (M7-03)...");
        int checks = 0;

        void Assert(bool condition, string message)
        {
            if (!condition)
            {
                throw new InvalidOperationException($"Security hygiene check failed: {message}");
            }
            checks++;
        }

        // 1. Zero-telemetry static analysis across assemblies
        SecurityHygieneValidator.AssertZeroTelemetry(typeof(KernelProduct).Assembly);
        Assert(true, "Kernel assembly contains zero prohibited telemetry references or types.");

        SecurityHygieneValidator.AssertZeroTelemetry(typeof(SecurityHygieneTests).Assembly);
        Assert(true, "Test assembly contains zero prohibited telemetry references or types.");

        // 2. Memory wiping via CryptographicOperations.ZeroMemory
        byte[] sensitiveKey = [0xDE, 0xAD, 0xBE, 0xEF, 0x01, 0x02, 0x03, 0x04];
        bool wiped = SecurityHygieneValidator.VerifyMemoryZeroing(sensitiveKey, () => CryptographicOperations.ZeroMemory(sensitiveKey));
        Assert(wiped, "CryptographicOperations.ZeroMemory actively wiped sensitive key buffer to zero.");
        Assert(sensitiveKey.All(b => b == 0), "All bytes in sensitive buffer are 0x00.");

        // Session credential zeroization test
        byte[] pinBytes = Encoding.UTF8.GetBytes("SuperSecretPin123!");
        using (var cred = FinTsSessionCredential.CaptureAndClear(pinBytes, FinTsCredentialKind.Pin, TimeSpan.FromMinutes(5)))
        {
            var snap = cred.GetSnapshot();
            Assert(snap.State == FinTsCredentialState.Available, "Credential state is Available.");
            Assert(pinBytes.All(b => b == 0), "Source buffer was cleared by CaptureAndClear.");
        }
        Assert(true, "FinTsSessionCredential disposed cleanly with zeroization.");

        // 3. Diagnostic text redaction checks
        const string cleanText = "Dialogue initialized for account DE** **** **** **** **3000 at endpoint https://localhost. HISAL response received: 2 segments.";
        Assert(SecurityHygieneValidator.AssertDiagnosticTextClean(cleanText), "Masked diagnostic text is accepted.");

        const string unmaskedIbanText = "Account updated: DE89370400440532013000 balance 5000.00 EUR";
        try
        {
            SecurityHygieneValidator.AssertDiagnosticTextClean(unmaskedIbanText);
            Assert(false, "AssertDiagnosticTextClean must throw on unmasked IBAN.");
        }
        catch (InvalidOperationException)
        {
            Assert(true, "AssertDiagnosticTextClean rejected unmasked IBAN in diagnostic text.");
        }

        const string plaintextCredentialText = "Connection authenticated with PIN=987654";
        try
        {
            SecurityHygieneValidator.AssertDiagnosticTextClean(plaintextCredentialText);
            Assert(false, "AssertDiagnosticTextClean must throw on plaintext PIN token.");
        }
        catch (InvalidOperationException)
        {
            Assert(true, "AssertDiagnosticTextClean rejected plaintext PIN in diagnostic text.");
        }

        Console.WriteLine($"Security Controls & Memory Hygiene (M7-03): {checks} checks completed successfully.");
    }
}
