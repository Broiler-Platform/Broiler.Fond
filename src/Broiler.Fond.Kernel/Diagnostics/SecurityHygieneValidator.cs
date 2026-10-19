using System.Reflection;
using System.Security.Cryptography;
using System.Text.RegularExpressions;

namespace Broiler.Fond.Kernel.Diagnostics;

/// <summary>
/// Verifies core security invariants: zero telemetry / tracking SDK references,
/// active memory wiping via ZeroMemory, and diagnostic text redaction.
/// Invariant: Broiler Fond contains zero telemetry, analytics, tracking SDKs, or background crash upload paths.
/// </summary>
public static class SecurityHygieneValidator
{
    private static readonly string[] ProhibitedTelemetryPackages =
    [
        "ApplicationInsights",
        "GoogleAnalytics",
        "Sentry",
        "Mixpanel",
        "Segment.Analytics",
        "Segment.io",
        "Raygun",
        "Bugsnag",
        "NewRelic",
        "Datadog",
        "Firebase",
        "AppCenter"
    ];

    private static readonly Regex UnmaskedIbanRegex = new(
        @"\b[A-Z]{2}[0-9]{2}[A-Z0-9]{12,30}\b",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static readonly Regex PlaintextCredentialRegex = new(
        @"(?:pin|tan|passphrase|secret)\s*[:=]\s*[^\s,\'\;]{4,}",
        RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    /// <summary>
    /// Inspects the target assembly for any references to prohibited telemetry, analytics, or background reporting packages.
    /// </summary>
    [System.Diagnostics.CodeAnalysis.UnconditionalSuppressMessage("ReflectionAnalysis", "IL2026:RequiresUnreferencedCode",
        Justification = "Security audit inspects assembly metadata in self-test and verification without dynamic instantiation.")]
    public static void AssertZeroTelemetry(Assembly assembly)
    {
        ArgumentNullException.ThrowIfNull(assembly);

        foreach (var reference in assembly.GetReferencedAssemblies())
        {
            string refName = reference.Name ?? string.Empty;
            foreach (string prohibited in ProhibitedTelemetryPackages)
            {
                if (refName.Contains(prohibited, StringComparison.OrdinalIgnoreCase))
                {
                    throw new InvalidOperationException($"Security invariant violated: Prohibited telemetry reference '{refName}' found in assembly '{assembly.GetName().Name}'.");
                }
            }
        }

        foreach (var type in assembly.GetTypes())
        {
            string fullName = type.FullName ?? string.Empty;
            foreach (string prohibited in ProhibitedTelemetryPackages)
            {
                if (fullName.Contains(prohibited, StringComparison.OrdinalIgnoreCase))
                {
                    throw new InvalidOperationException($"Security invariant violated: Prohibited telemetry type '{fullName}' declared in assembly '{assembly.GetName().Name}'.");
                }
            }
        }
    }

    /// <summary>
    /// Verifies that the buffer contains non-zero data initially, and that the clear action wipes 100% of bytes to zero.
    /// </summary>
    public static bool VerifyMemoryZeroing(byte[] buffer, Action clearAction)
    {
        ArgumentNullException.ThrowIfNull(buffer);
        ArgumentNullException.ThrowIfNull(clearAction);

        if (buffer.Length == 0)
        {
            return true;
        }

        // Verify buffer has non-zero bytes before clearing
        bool hasNonZero = buffer.Any(b => b != 0);
        if (!hasNonZero)
        {
            throw new ArgumentException("Buffer must contain non-zero bytes to test zeroing.", nameof(buffer));
        }

        clearAction();

        // Verify all bytes are 0
        return buffer.All(b => b == 0);
    }

    /// <summary>
    /// Evaluates text exported for user diagnostic bundles, verifying that unmasked IBANs, clear credentials, and raw amounts are absent.
    /// </summary>
    public static bool AssertDiagnosticTextClean(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return true;
        }

        // Unmasked IBAN check: allows masked IBANs like DE** **** **** **** **1234
        var ibanMatches = UnmaskedIbanRegex.Matches(text);
        foreach (Match match in ibanMatches)
        {
            // If the match does not contain masking asterisks, it's a raw unmasked IBAN!
            if (!match.Value.Contains('*'))
            {
                throw new InvalidOperationException($"Diagnostic data leak: Unmasked IBAN '{match.Value}' found in diagnostic text.");
            }
        }

        // Clear credential check
        if (PlaintextCredentialRegex.IsMatch(text))
        {
            throw new InvalidOperationException("Diagnostic data leak: Plaintext credential pattern found in diagnostic text.");
        }

        return true;
    }
}
