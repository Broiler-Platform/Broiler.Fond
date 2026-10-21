using System.Globalization;
using System.Text;
using Broiler.Fond.Kernel.Hosting;

namespace Broiler.Fond.Host.Desktop.Qualification;

/// <summary>
/// Startup qualification guard enforcing runtime invariants before profile archive access or UI initialization.
/// Asserts AesGcm support, UTF-8 FormC normalization, and invariant culture globalization per ADR 0130.
/// Invariant: Fail-closed; profile archives must never be opened on an unqualified host platform.
/// </summary>
public static class DesktopStartupQualificationGuard
{
    private static HostPlatformQualificationResult? _cachedResult;
    private static readonly object _syncLock = new();

    /// <summary>
    /// Executes all platform qualification checks, caching the result.
    /// </summary>
    public static HostPlatformQualificationResult Qualify()
    {
        lock (_syncLock)
        {
            _cachedResult = HostPlatformQualification.Qualify();
            return _cachedResult;
        }
    }

    /// <summary>
    /// Asserts that the current runtime environment is qualified.
    /// Throws <see cref="HostPlatformQualificationException"/> if any qualification check fails.
    /// </summary>
    public static void EnsureQualified()
    {
        var result = _cachedResult ?? Qualify();
        if (!result.IsQualified)
        {
            string violationSummary = string.Join("; ", result.Violations);
            string message = $"Host platform failed qualification guard with {result.Violations.Count} violation(s): {violationSummary}";
            throw new HostPlatformQualificationException(message, result);
        }
    }

    /// <summary>
    /// Preflight check for profile archive paths.
    /// Asserts host platform qualification AND validates that the target profile directory path is non-empty.
    /// </summary>
    public static void AssertProfileStorePreflight(string? profileDirectory)
    {
        EnsureQualified();

        if (string.IsNullOrWhiteSpace(profileDirectory))
        {
            throw new ArgumentException("Profile directory path must not be null or whitespace.", nameof(profileDirectory));
        }
    }

    /// <summary>
    /// Formats a human-readable diagnostic report of the qualification results.
    /// </summary>
    public static string FormatDiagnosticReport(HostPlatformQualificationResult result)
    {
        ArgumentNullException.ThrowIfNull(result);

        var sb = new StringBuilder();
        sb.AppendLine("================================================================================");
        sb.AppendLine("           BROILER FOND — DESKTOP PLATFORM QUALIFICATION REPORT");
        sb.AppendLine("================================================================================");
        sb.AppendLine(CultureInfo.InvariantCulture, $"Timestamp (UTC)     : {DateTimeOffset.UtcNow:yyyy-MM-ddTHH:mm:ssZ}");
        sb.AppendLine(CultureInfo.InvariantCulture, $"Platform Description: {result.PlatformDescription}");
        sb.AppendLine(CultureInfo.InvariantCulture, $"Qualification Status: {(result.IsQualified ? "QUALIFIED [PASS]" : "FAILED [FAIL-CLOSED]")}");
        sb.AppendLine("--------------------------------------------------------------------------------");
        sb.AppendLine(CultureInfo.InvariantCulture, $"Passed Checks ({result.ChecksPassed.Count}):");
        foreach (var check in result.ChecksPassed)
        {
            sb.AppendLine(CultureInfo.InvariantCulture, $"  [PASS] {check}");
        }

        if (result.Violations.Count > 0)
        {
            sb.AppendLine("--------------------------------------------------------------------------------");
            sb.AppendLine(CultureInfo.InvariantCulture, $"Violations ({result.Violations.Count}):");
            foreach (var violation in result.Violations)
            {
                sb.AppendLine(CultureInfo.InvariantCulture, $"  [FAIL] {violation}");
            }
        }

        sb.AppendLine("================================================================================");
        return sb.ToString();
    }
}
