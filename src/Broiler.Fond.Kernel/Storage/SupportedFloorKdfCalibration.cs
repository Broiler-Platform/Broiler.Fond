using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Security.Cryptography;

namespace Broiler.Fond.Kernel.Storage;

/// <summary>
/// Machine execution context captured during KDF calibration on the host.
/// </summary>
public sealed record KdfHostEnvironment(
    string OsDescription,
    Architecture ProcessArchitecture,
    string FrameworkDescription,
    int ProcessorCount);

/// <summary>
/// Statistical latency metrics for a specific KDF iteration count.
/// </summary>
public sealed record KdfIterationMetrics(
    int Iterations,
    int SampleCount,
    double MinMs,
    double MaxMs,
    double MeanMs,
    double MedianMs,
    double P95Ms)
{
    public bool MeetsMedianTarget => MedianMs is >= 500.0 and <= 1000.0;
    public bool MeetsP95Target => P95Ms <= 2000.0;
    public bool MeetsAllTargets => MeetsMedianTarget && MeetsP95Target;
}

/// <summary>
/// Full calibration report across evaluated iteration counts per ADR 0004.
/// </summary>
public sealed record KdfCalibrationReport(
    KdfHostEnvironment Environment,
    IReadOnlyList<KdfIterationMetrics> CandidateMetrics,
    int RecommendedIterations,
    string EvaluationSummary);

/// <summary>
/// Supported-floor KDF calibration runner executing warmup, statistical multi-sample timing,
/// and target evaluation against ADR 0004 latency bounds (500–1,000 ms median, max 2,000 ms p95).
/// </summary>
public static class SupportedFloorKdfCalibration
{
    public const int MinimumAllowedIterations = ProfileEnvelopeCodec.MinimumIterations;
    public const int MaximumAllowedIterations = ProfileEnvelopeCodec.MaximumIterations;
    public const double TargetMedianLowerMs = 500.0;
    public const double TargetMedianUpperMs = 1000.0;
    public const double TargetP95UpperMs = 2000.0;

    public static KdfHostEnvironment GetHostEnvironment() =>
        new(
            RuntimeInformation.OSDescription,
            RuntimeInformation.ProcessArchitecture,
            RuntimeInformation.FrameworkDescription,
            Environment.ProcessorCount);

    public static KdfIterationMetrics MeasureIterations(
        int iterations,
        int sampleCount,
        byte[] normalizedPassword,
        byte[] salt)
    {
        ArgumentNullException.ThrowIfNull(normalizedPassword);
        ArgumentNullException.ThrowIfNull(salt);

        if (iterations is < MinimumAllowedIterations or > MaximumAllowedIterations)
        {
            throw new ArgumentOutOfRangeException(nameof(iterations),
                $"Iterations {iterations} outside format bounds ({MinimumAllowedIterations}..{MaximumAllowedIterations}).");
        }

        if (sampleCount <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(sampleCount), "At least one sample is required.");
        }

        // Warmup: run at least 1 derivation before recording metrics
        byte[] warmup = Rfc2898DeriveBytes.Pbkdf2(normalizedPassword, salt, iterations, HashAlgorithmName.SHA256, 32);
        CryptographicOperations.ZeroMemory(warmup);

        double[] samples = new double[sampleCount];
        for (int i = 0; i < sampleCount; i++)
        {
            long start = Stopwatch.GetTimestamp();
            byte[] derived = Rfc2898DeriveBytes.Pbkdf2(normalizedPassword, salt, iterations, HashAlgorithmName.SHA256, 32);
            long elapsed = Stopwatch.GetTimestamp() - start;
            CryptographicOperations.ZeroMemory(derived);

            samples[i] = (double)elapsed * 1000.0 / Stopwatch.Frequency;
        }

        Array.Sort(samples);
        double min = samples[0];
        double max = samples[^1];
        double sum = 0;
        for (int i = 0; i < samples.Length; i++)
        {
            sum += samples[i];
        }

        double mean = sum / samples.Length;
        double median = samples.Length % 2 == 1
            ? samples[samples.Length / 2]
            : (samples[samples.Length / 2 - 1] + samples[samples.Length / 2]) / 2.0;

        int p95Index = (int)Math.Ceiling(samples.Length * 0.95) - 1;
        p95Index = Math.Clamp(p95Index, 0, samples.Length - 1);
        double p95 = samples[p95Index];

        return new KdfIterationMetrics(iterations, sampleCount, min, max, mean, median, p95);
    }

    public static KdfCalibrationReport Calibrate(
        IReadOnlyList<int> candidateIterations,
        int samplesPerCandidate = 30,
        string passphrase = "CalibrationTestPassphrase123!",
        byte[]? salt = null)
    {
        ArgumentNullException.ThrowIfNull(candidateIterations);
        if (candidateIterations.Count == 0)
        {
            throw new ArgumentException("At least one candidate iteration count is required.", nameof(candidateIterations));
        }

        byte[] password = ProfileEnvelopeCodec.NormalizePassphrase(passphrase);
        salt ??= new byte[32];

        try
        {
            List<KdfIterationMetrics> metricsList = new(candidateIterations.Count);
            foreach (int count in candidateIterations)
            {
                metricsList.Add(MeasureIterations(count, samplesPerCandidate, password, salt));
            }

            int recommended = SelectRecommended(metricsList);
            KdfIterationMetrics best = metricsList.First(m => m.Iterations == recommended);
            string summary = $"Selected {recommended:N0} iterations (Median: {best.MedianMs:F1}ms, P95: {best.P95Ms:F1}ms) on {RuntimeInformation.OSDescription}.";

            return new KdfCalibrationReport(GetHostEnvironment(), metricsList, recommended, summary);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(password);
        }
    }

    private static int SelectRecommended(List<KdfIterationMetrics> metrics)
    {
        // 1. Look for candidate meeting both median (500-1000ms) and p95 (<= 2000ms)
        var qualified = metrics.Where(m => m.MeetsAllTargets).ToList();
        if (qualified.Count > 0)
        {
            // Pick the one closest to 750ms target center
            return qualified.OrderBy(m => Math.Abs(m.MedianMs - 750.0)).First().Iterations;
        }

        // 2. If all are faster than 500ms (fast machine), pick the highest iteration count whose median <= 1000ms and p95 <= 2000ms
        var underUpper = metrics.Where(m => m.MedianMs <= TargetMedianUpperMs && m.MeetsP95Target).ToList();
        if (underUpper.Count > 0)
        {
            return underUpper.OrderByDescending(m => m.Iterations).First().Iterations;
        }

        // 3. If hardware is slow and even minimum exceeds targets, security fail-closed rule applies:
        // ADR 0004 forbids lowering the candidate floor below 600,000.
        return metrics.OrderBy(m => m.Iterations).First().Iterations;
    }
}
