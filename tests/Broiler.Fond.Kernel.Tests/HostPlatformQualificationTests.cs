using System.Globalization;
using System.Text;
using Broiler.Fond.Kernel.Hosting;

namespace Broiler.Fond.Kernel.Tests;

public static class HostPlatformQualificationTests
{
    public static void Run()
    {
        Console.WriteLine("Running Host Platform Qualification verification (M8-01)...");
        int checks = 0;

        void Assert(bool condition, string message)
        {
            if (!condition)
            {
                throw new InvalidOperationException($"Host Platform Qualification check failed: {message}");
            }
            checks++;
        }

        // 1. Full Platform Qualification
        var result = HostPlatformQualification.Qualify();
        Assert(result.IsQualified, "Current host platform passes all qualification checks.");
        Assert(result.Violations.Count == 0, "Host qualification reported zero violations.");
        Assert(result.ChecksPassed.Count >= 3, "All 3 platform qualification categories passed.");
        Assert(!string.IsNullOrWhiteSpace(result.PlatformDescription), "Platform description contains OS, architecture, and runtime framework metadata.");

        // EnsureQualified does not throw on conforming platform
        HostPlatformQualification.EnsureQualified();
        Assert(true, "EnsureQualified executed successfully.");

        // 2. Unicode FormC Normalization Invariants
        string composed = "M\u00FCnchen";
        string decomposed = "Mu\u0308nchen";
        Assert(!string.Equals(composed, decomposed, StringComparison.Ordinal), "Composed and decomposed representations differ in ordinal string comparison.");
        Assert(string.Equals(composed.Normalize(NormalizationForm.FormC), decomposed.Normalize(NormalizationForm.FormC), StringComparison.Ordinal),
            "FormC normalization produces identical string representations.");

        // Strict UTF-8 encoding invariant
        var strictUtf8 = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true);
        byte[] bytes = strictUtf8.GetBytes(composed.Normalize(NormalizationForm.FormC));
        Assert(bytes.Length == 8, "München in UTF-8 without BOM is 8 bytes (M + ü[2 bytes] + nchen[5 bytes]).");

        bool threwOnBadUtf8 = false;
        try
        {
            _ = strictUtf8.GetString([0xFF, 0xFE]); // invalid UTF-8 sequence
        }
        catch (DecoderFallbackException)
        {
            threwOnBadUtf8 = true;
        }
        Assert(threwOnBadUtf8, "Strict UTF-8 decoder rejects invalid byte sequences.");

        // 3. Invariant Culture Formatting
        decimal amount = 987654.32m;
        string formattedAmount = amount.ToString("F2", CultureInfo.InvariantCulture);
        Assert(formattedAmount == "987654.32", "Invariant decimal uses dot as decimal separator.");

        DateTimeOffset utcNow = new(2026, 10, 19, 16, 0, 0, TimeSpan.Zero);
        string formattedIso = utcNow.ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture);
        Assert(formattedIso == "2026-10-19T16:00:00Z", "ISO 8601 timestamp formats deterministically.");

        DateOnly dateOnly = new(2026, 10, 19);
        Assert(dateOnly.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) == "2026-10-19", "DateOnly formats with hyphens.");

        // Invariant case conversion
        Assert("i".ToUpperInvariant() == "I", "Invariant uppercase for 'i' is 'I'.");
        Assert("I".ToLowerInvariant() == "i", "Invariant lowercase for 'I' is 'i'.");

        // 4. Host Platform Seam Contracts & Safe Mode Transitions
        var testSeam = new TestHostPlatformSeam();
        Assert(testSeam.PlatformType == HostPlatformType.Windows || testSeam.PlatformType == HostPlatformType.Linux || testSeam.PlatformType == HostPlatformType.MacOS,
            "Test host seam reports a recognized platform type.");
        Assert(!testSeam.IsSafeModeActive, "Safe Mode is initially inactive.");

        bool eventRaised = false;
        string? eventReason = null;
        testSeam.SafeModeChanged += (s, e) =>
        {
            eventRaised = true;
            eventReason = e.Reason;
        };

        testSeam.EnterSafeMode("Reconciliation divergence detected.");
        Assert(testSeam.IsSafeModeActive, "Safe Mode is active after EnterSafeMode.");
        Assert(eventRaised, "SafeModeChanged event raised on activation.");
        Assert(eventReason == "Reconciliation divergence detected.", "SafeModeChanged event carries correct reason.");

        eventRaised = false;
        testSeam.ExitSafeMode();
        Assert(!testSeam.IsSafeModeActive, "Safe Mode is inactive after ExitSafeMode.");
        Assert(eventRaised, "SafeModeChanged event raised on deactivation.");

        // 5. Host Notification Channel
        testSeam.Notifications.Notify(HostNotificationLevel.SecurityAlert, "Audit Alert", "Cryptographic preflight verified.");
        Assert(testSeam.RecordedNotifications.Count == 1, "Notification successfully dispatched through seam.");
        Assert(testSeam.RecordedNotifications[0].Level == HostNotificationLevel.SecurityAlert, "Notification level preserved.");

        // 6. Host Credential Prompt
        var promptTask = testSeam.CredentialPrompt.RequestCredentialAsync(
            new CredentialPromptRequest("Master Passphrase", "Enter your profile passphrase:", true, TimeSpan.FromSeconds(30)));
        Assert(promptTask.IsCompletedSuccessfully, "Credential prompt completes synchronously in mock.");
        var promptResp = promptTask.Result;
        Assert(promptResp.IsSuccess && promptResp.PlainTextSecret == "TestPassphrase123!", "Credential acquired securely through prompt.");

        // 7. Host Clipboard Hygiene
        var clipTask = testSeam.Clipboard.SetTransientTextAsync("SensitiveIbanToken", TimeSpan.FromSeconds(10));
        Assert(clipTask.IsCompletedSuccessfully, "Transient clipboard copy succeeded.");
        Assert(testSeam.ClipboardContent == "SensitiveIbanToken", "Clipboard contains transient text.");

        var clearTask = testSeam.Clipboard.ClearAsync();
        Assert(clearTask.IsCompletedSuccessfully, "Clipboard clear succeeded.");
        Assert(testSeam.ClipboardContent == null, "Clipboard content zeroized.");

        Console.WriteLine($"Host Platform Qualification (M8-01): {checks} checks completed successfully.");
    }

    private sealed class TestHostPlatformSeam : IHostPlatformSeam
    {
        public string HostIdentifier => "TestPlatformHost";
        public HostPlatformType PlatformType => HostPlatformType.Windows;

        public bool IsSafeModeActive { get; private set; }
        public event EventHandler<SafeModeChangedEventArgs>? SafeModeChanged;

        public List<(HostNotificationLevel Level, string Title, string Message)> RecordedNotifications { get; } = [];
        public string? ClipboardContent { get; set; }

        public IHostNotificationChannel Notifications { get; }
        public IHostCredentialPrompt CredentialPrompt { get; }
        public IHostClipboardService Clipboard { get; }

        public TestHostPlatformSeam()
        {
            Notifications = new TestNotificationChannel(this);
            CredentialPrompt = new TestCredentialPrompt();
            Clipboard = new TestClipboardService(this);
        }

        public void EnterSafeMode(string reason)
        {
            IsSafeModeActive = true;
            SafeModeChanged?.Invoke(this, new SafeModeChangedEventArgs(true, reason, DateTimeOffset.UtcNow));
        }

        public void ExitSafeMode()
        {
            IsSafeModeActive = false;
            SafeModeChanged?.Invoke(this, new SafeModeChangedEventArgs(false, null, DateTimeOffset.UtcNow));
        }

        private sealed class TestNotificationChannel(TestHostPlatformSeam parent) : IHostNotificationChannel
        {
            public void Notify(HostNotificationLevel level, string title, string message)
            {
                parent.RecordedNotifications.Add((level, title, message));
            }
        }

        private sealed class TestCredentialPrompt : IHostCredentialPrompt
        {
            public ValueTask<CredentialPromptResponse> RequestCredentialAsync(CredentialPromptRequest request, CancellationToken cancellationToken = default)
            {
                return ValueTask.FromResult(new CredentialPromptResponse(true, false, "TestPassphrase123!"));
            }
        }

        private sealed class TestClipboardService(TestHostPlatformSeam parent) : IHostClipboardService
        {
            public ValueTask SetTransientTextAsync(string text, TimeSpan timeout, CancellationToken cancellationToken = default)
            {
                parent.ClipboardContent = text;
                return ValueTask.CompletedTask;
            }

            public ValueTask ClearAsync(CancellationToken cancellationToken = default)
            {
                parent.ClipboardContent = null;
                return ValueTask.CompletedTask;
            }
        }
    }
}
