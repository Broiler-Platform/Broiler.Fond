using System.Security.Cryptography;
using Broiler.Fond.Kernel.Hosting;

namespace Broiler.Fond.Kernel.Tests;

public static class MobileLifecycleHardeningTests
{
    public static void Run()
    {
        Console.WriteLine("Running Mobile Lifecycle Hardening verification (M8-04)...");
        int checks = 0;

        void Assert(bool condition, string message)
        {
            if (!condition)
            {
                throw new InvalidOperationException($"Mobile Lifecycle Hardening check failed: {message}");
            }
            checks++;
        }

        var mockClipboard = new MockClipboard();
        var coordinator = new MobileLifecycleCoordinator(mockClipboard);

        // 1. Initial State
        Assert(coordinator.CurrentState == HostLifecycleState.ForegroundActive, "Initial lifecycle state is ForegroundActive.");
        Assert(!coordinator.IsPrivacyCurtainActive, "Privacy curtain is initially inactive.");

        // 2. Pause & Privacy Curtain Activation
        bool curtainFired = false;
        bool curtainState = false;
        coordinator.PrivacyCurtainChanged += (s, active) =>
        {
            curtainFired = true;
            curtainState = active;
        };

        coordinator.TransitionTo(HostLifecycleState.ForegroundPaused);
        Assert(coordinator.CurrentState == HostLifecycleState.ForegroundPaused, "State transitioned to ForegroundPaused.");
        Assert(coordinator.IsPrivacyCurtainActive, "Privacy curtain activated upon pause.");
        Assert(curtainFired && curtainState, "PrivacyCurtainChanged event fired with active=true.");

        // 3. Volatile Secret Scrubbing on Suspension
        byte[] secretKey = new byte[32];
        RandomNumberGenerator.Fill(secretKey);
        Assert(secretKey.Any(b => b != 0), "Secret key initially contains non-zero bytes.");

        coordinator.RegisterVolatileSecret(() =>
        {
            CryptographicOperations.ZeroMemory(secretKey);
        });

        // Set clipboard content
        mockClipboard.Content = "SensitiveIbanPayload";

        curtainFired = false;
        coordinator.TransitionTo(HostLifecycleState.BackgroundSuspended);
        Assert(coordinator.CurrentState == HostLifecycleState.BackgroundSuspended, "State transitioned to BackgroundSuspended.");
        Assert(secretKey.All(b => b == 0), "Volatile secret buffer was 100% zeroed upon suspension.");
        Assert(mockClipboard.Content == null, "Clipboard was cleared immediately upon app suspension.");

        // 4. Return to Foreground & Curtain Deactivation
        curtainFired = false;
        coordinator.TransitionTo(HostLifecycleState.ForegroundActive);
        Assert(coordinator.CurrentState == HostLifecycleState.ForegroundActive, "State transitioned to ForegroundActive.");
        Assert(!coordinator.IsPrivacyCurtainActive, "Privacy curtain deactivated upon resume.");
        Assert(curtainFired && !curtainState, "PrivacyCurtainChanged event fired with active=false.");

        // 5. Decoupled SCA Background Timeout Guard
        DateTimeOffset t0 = new(2026, 10, 19, 12, 0, 0, TimeSpan.Zero);
        coordinator.BeginDecoupledScaWindow(TimeSpan.FromSeconds(60), t0);
        Assert(coordinator.IsDecoupledScaActive, "Decoupled SCA window is active.");
        Assert(!coordinator.DidDecoupledScaExpire, "SCA has not expired initially.");

        // User switches to banking app (suspend) and returns within deadline (e.g. at t0 + 45s)
        coordinator.TransitionTo(HostLifecycleState.BackgroundSuspended, t0.AddSeconds(5));
        coordinator.TransitionTo(HostLifecycleState.ForegroundActive, t0.AddSeconds(45));
        Assert(!coordinator.DidDecoupledScaExpire, "Decoupled SCA did not expire within 45s of 60s window.");

        // User switches away again and returns after deadline (e.g. at t0 + 65s)
        coordinator.TransitionTo(HostLifecycleState.BackgroundSuspended, t0.AddSeconds(50));
        coordinator.TransitionTo(HostLifecycleState.ForegroundActive, t0.AddSeconds(65));
        Assert(coordinator.DidDecoupledScaExpire, "Decoupled SCA flagged as expired when returning after deadline.");

        coordinator.EndDecoupledScaWindow();
        Assert(!coordinator.IsDecoupledScaActive, "Decoupled SCA window terminated.");
        Assert(!coordinator.DidDecoupledScaExpire, "Expiration flag reset after window ends.");

        // 6. Process Termination Scrubbing
        byte[] sessionPin = new byte[8];
        RandomNumberGenerator.Fill(sessionPin);
        coordinator.RegisterVolatileSecret(() => CryptographicOperations.ZeroMemory(sessionPin));

        coordinator.TransitionTo(HostLifecycleState.Terminating);
        Assert(sessionPin.All(b => b == 0), "Session PIN buffer zeroed upon Terminating transition.");

        Console.WriteLine($"Mobile Lifecycle Hardening (M8-04): {checks} checks completed successfully.");
    }

    private sealed class MockClipboard : IHostClipboardService
    {
        public string? Content { get; set; }

        public ValueTask SetTransientTextAsync(string text, TimeSpan timeout, CancellationToken cancellationToken = default)
        {
            Content = text;
            return ValueTask.CompletedTask;
        }

        public ValueTask ClearAsync(CancellationToken cancellationToken = default)
        {
            Content = null;
            return ValueTask.CompletedTask;
        }
    }
}
