using System.Reflection;
using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Broiler.Fond.Kernel.FinTs;

namespace Broiler.Fond.Kernel.Tests;

internal static class FinTsSessionCredentialTests
{
    private static readonly FieldInfo BufferField = typeof(FinTsSessionCredential).GetField("_bytes", BindingFlags.Instance | BindingFlags.NonPublic)!;
    internal static void Run(Action<bool, string> check)
    {
        int count = 0;
        void Verify(bool condition, string message) { count++; check(condition, message); }
        using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream("FinTs.credential-buffer-v1.json")!;
        using var doc = JsonDocument.Parse(stream);
        var vectors = doc.RootElement.GetProperty("vectors").EnumerateArray().ToArray();
        foreach (var v in vectors)
        {
            byte[] expected = Convert.FromBase64String(v.GetProperty("sourceBase64").GetString()!), source = expected.ToArray();
            var clock = new Clock();
            using var owner = FinTsSessionCredential.CaptureAndClear(source, Enum.Parse<FinTsCredentialKind>(v.GetProperty("kind").GetString()!), TimeSpan.FromMilliseconds(v.GetProperty("lifetimeMilliseconds").GetInt32()), clock);
            byte[] retained = Buffer(owner)!;
            Verify(source.All(b => b == 0) && !ReferenceEquals(source, retained), "Capture clears the input and owns a separate buffer.");
            foreach (var step in v.GetProperty("steps").EnumerateArray())
            {
                clock.Milliseconds = step.GetProperty("atMilliseconds").GetInt64();
                byte[] destination = Enumerable.Repeat((byte)0xCC, step.GetProperty("capacity").GetInt32()).ToArray();
                int written = 0; string result;
                switch (step.GetProperty("action").GetString())
                {
                    case "copy": result = owner.TryCopyTo(destination, out written).ToString(); break;
                    case "cancel": owner.Cancel(); result = "Cancelled"; break;
                    case "dispose": owner.Dispose(); result = "Disposed"; break;
                    case "snapshot": _ = owner.GetSnapshot(); result = "Snapshot"; break;
                    default: throw new InvalidOperationException("Unknown synthetic action.");
                }
                var snapshot = owner.GetSnapshot();
                Verify(result == step.GetProperty("result").GetString() && snapshot.State.ToString() == step.GetProperty("state").GetString(), $"Independent credential result/state match {v.GetProperty("name").GetString()}.");
                Verify(written == step.GetProperty("written").GetInt32() && snapshot.CopiesCompleted == step.GetProperty("copies").GetInt32(), "Independent written and copy counts match.");
                Verify(destination.AsSpan(0, written).SequenceEqual(expected.AsSpan(0, written)) && destination.Skip(written).All(b => b == 0xCC), "Copy-out preserves exact bytes and leaves unwritten destination bytes intact.");
                Verify(snapshot.State == FinTsCredentialState.Available ? Buffer(owner) is not null && retained.SequenceEqual(expected) : Buffer(owner) is null && retained.All(b => b == 0), "Every terminal path zeroes the actual owned storage before releasing its reference.");
                CryptographicOperations.ZeroMemory(destination);
            }
        }
        Verify(vectors.Length == 10, "All ten independent credential traces ran.");
        byte[] Public() => Encoding.ASCII.GetBytes("PUBLIC-SECRET");
        FinTsSessionCredential New(FinTsCredentialKind kind = FinTsCredentialKind.Pin, TimeProvider? clock = null) => FinTsSessionCredential.CaptureAndClear(Public(), kind, TimeSpan.FromSeconds(10), clock ?? new Clock());
        foreach (int length in new[] { 0, 100, 1000 })
        {
            byte[] source = Enumerable.Repeat((byte)0x5A, length).ToArray();
            try { using var owner = FinTsSessionCredential.CaptureAndClear(source, FinTsCredentialKind.Pin, TimeSpan.FromSeconds(1)); Verify(false, "Invalid length must fail."); }
            catch (ArgumentException error) { Verify(source.All(b => b == 0) && !error.ToString().Contains("PUBLIC", StringComparison.Ordinal), "Invalid source lengths still clear every supplied byte and use fixed diagnostics."); }
        }
        foreach (var policy in new[] { ((FinTsCredentialKind)99, TimeSpan.FromSeconds(1)), (FinTsCredentialKind.Pin, TimeSpan.Zero), (FinTsCredentialKind.Tan, TimeSpan.FromMinutes(16)), (FinTsCredentialKind.Pin, TimeSpan.FromTicks(-1)) })
        {
            byte[] source = Public();
            try { using var owner = FinTsSessionCredential.CaptureAndClear(source, policy.Item1, policy.Item2); Verify(false, "Invalid policy must fail."); }
            catch (ArgumentException) { Verify(source.All(b => b == 0), "Invalid kind or lifetime still clears source input."); }
        }
        foreach (TimeProvider clock in new TimeProvider[] { new InvalidClock(), new ThrowingClock() })
        {
            byte[] source = Public();
            try { using var owner = FinTsSessionCredential.CaptureAndClear(source, FinTsCredentialKind.Pin, TimeSpan.FromSeconds(1), clock); Verify(false, "Invalid clock must fail."); }
            catch (ArgumentException error) { Verify(source.All(b => b == 0) && !error.ToString().Contains("PUBLIC", StringComparison.Ordinal) && error.InnerException is null, "Clock construction failures clear input and do not forward arbitrary clock exception text."); }
        }
        using (var owner = FinTsSessionCredential.CaptureAndClear(new byte[] { 0xFF }, FinTsCredentialKind.Pin, TimeSpan.FromMinutes(15), new Clock()))
        { Verify(owner.GetSnapshot().RemainingTime == TimeSpan.FromMinutes(15), "Minimum nonempty length, opaque octets and maximum local lifetime are accepted."); }
        byte[] surrounding = Enumerable.Repeat((byte)0x5A, 20).ToArray();
        using (var owner = FinTsSessionCredential.CaptureAndClear(surrounding.AsSpan(3, 10), FinTsCredentialKind.Pin, TimeSpan.FromSeconds(10), new Clock()))
        { Verify(surrounding.Take(3).All(b => b == 0x5A) && surrounding.Skip(3).Take(10).All(b => b == 0) && surrounding.Skip(13).All(b => b == 0x5A), "Capture clears exactly the transferred span, never adjacent caller memory."); }
        using (var owner = New())
        {
            byte[] retained = Buffer(owner)!; byte[] destination = new byte[99]; owner.TryCopyTo(destination, out int written);
            destination[0] = 0;
            Verify(retained.SequenceEqual(Public()), "Caller-owned output cannot mutate the retained PIN.");
            owner.Dispose(); owner.Dispose(); owner.Cancel();
            Verify(owner.GetSnapshot().State == FinTsCredentialState.Disposed && retained.All(b => b == 0) && Buffer(owner) is null && destination.AsSpan(1, written - 1).SequenceEqual(Public().AsSpan(1)), "Disposal is idempotent and zeroes owned bytes; it cannot revoke prior caller copies.");
            CryptographicOperations.ZeroMemory(destination);
        }
        using (var cancelled = new CancellationTokenSource())
        {
            cancelled.Cancel(); byte[] source = Public();
            try { using var owner = FinTsSessionCredential.CaptureAndClear(source, FinTsCredentialKind.Tan, TimeSpan.FromSeconds(10), cancellationToken: cancelled.Token); Verify(false, "Capture cancellation must throw."); }
            catch (OperationCanceledException) { Verify(source.All(b => b == 0), "Pre-cancelled capture clears its input."); }
            using var owner2 = New(FinTsCredentialKind.Tan); byte[] retained = Buffer(owner2)!, destination = Enumerable.Repeat((byte)0xCC, 99).ToArray();
            try { owner2.TryCopyTo(destination, out _, cancelled.Token); Verify(false, "Copy cancellation must throw."); }
            catch (OperationCanceledException) { Verify(owner2.GetSnapshot().State == FinTsCredentialState.Cancelled && retained.All(b => b == 0) && destination.All(b => b == 0xCC), "Copy cancellation clears the owner and leaves untouched output intact."); }
        }
        foreach (bool cancel in new[] { false, true })
        {
            using var cancellation = new CancellationTokenSource(); var clock = new HookClock(); using var owner = New(clock: clock);
            byte[] retained = Buffer(owner)!, destination = Enumerable.Repeat((byte)0xCC, 99).ToArray();
            clock.AfterReads = 2; clock.Action = () => { if (cancel) { cancellation.Cancel(); } else { clock.Milliseconds = 10000; } };
            int written = -1;
            try
            {
                var result = owner.TryCopyTo(destination, out written, cancellation.Token);
                Verify(!cancel && result == FinTsCredentialCopyResult.Unavailable, "Expiry during copy withholds a success result.");
            }
            catch (OperationCanceledException) { Verify(cancel, "Cancellation during copy withholds a success result."); }
            Verify(written == 0 && destination.Take(retained.Length).All(b => b == 0) && destination.Skip(retained.Length).All(b => b == 0xCC) && retained.All(b => b == 0) && Buffer(owner) is null, "Post-copy cancellation/expiry erases both the owned bytes and copied destination prefix.");
        }
        var failureClock = new HookClock();
        using (var owner = New(clock: failureClock))
        {
            byte[] retained = Buffer(owner)!; failureClock.AfterReads = 1; failureClock.Action = () => throw new InvalidOperationException("PUBLIC-SECRET");
            Verify(owner.GetSnapshot().State == FinTsCredentialState.ClockInvalid && retained.All(b => b == 0), "Runtime clock failure terminates without propagating exception text or retaining bytes.");
        }
        var disposeClock = new HookClock();
        using (var owner = New(clock: disposeClock))
        {
            byte[] retained = Buffer(owner)!; disposeClock.AfterReads = 1; disposeClock.Action = () => throw new InvalidOperationException("Clock must not run during disposal.");
            owner.Dispose(); Verify(retained.All(b => b == 0) && owner.GetSnapshot().State == FinTsCredentialState.Disposed && disposeClock.AfterReads == 1, "Disposal never depends on a caller clock.");
        }
        using (var cancellation = new CancellationTokenSource())
        {
            var clock = new HookClock { AfterReads = 2, Action = cancellation.Cancel }; byte[] source = Public();
            try { using var owner = FinTsSessionCredential.CaptureAndClear(source, FinTsCredentialKind.Pin, TimeSpan.FromSeconds(10), clock, cancellation.Token); Verify(false, "Late capture cancellation must throw."); }
            catch (OperationCanceledException) { Verify(source.All(b => b == 0), "Cancellation after allocation clears input and disposes the unpublished owner."); }
        }
        var startClock = new Clock { Milliseconds = 20000 };
        var captureExpiryClock = new HookClock(); captureExpiryClock.AfterReads = 2; captureExpiryClock.Action = () => captureExpiryClock.Milliseconds = 10000;
        byte[] expiringSource = Public();
        using (var owner = FinTsSessionCredential.CaptureAndClear(expiringSource, FinTsCredentialKind.Pin, TimeSpan.FromSeconds(10), captureExpiryClock))
        { Verify(owner.GetSnapshot().State == FinTsCredentialState.Expired && Buffer(owner) is null && expiringSource.All(b => b == 0), "Expiry during capture returns an unusable owner with cleared input and no retained bytes."); }
        using (var owner = New(clock: startClock))
        {
            startClock.Milliseconds = 29999; Verify(owner.GetSnapshot().RemainingTime == TimeSpan.FromMilliseconds(1), "Deadline starts at capture time and does not use wall clock.");
            byte[] destination = new byte[99]; owner.TryCopyTo(destination, out _); startClock.Milliseconds = 30000;
            Verify(owner.TryCopyTo(destination, out int written) == FinTsCredentialCopyResult.Unavailable && written == 0 && owner.GetSnapshot().State == FinTsCredentialState.Expired, "PIN copies cannot renew the absolute deadline."); CryptographicOperations.ZeroMemory(destination);
        }
        using (var owner = New(FinTsCredentialKind.Tan))
        {
            byte[] retained = Buffer(owner)!; int successes = 0;
            Parallel.For(0, 32, _ =>
            {
                byte[] destination = new byte[99];
                try { if (owner.TryCopyTo(destination, out _) == FinTsCredentialCopyResult.Copied) { Interlocked.Increment(ref successes); } }
                finally { CryptographicOperations.ZeroMemory(destination); }
            });
            Verify(successes == 1 && owner.GetSnapshot().CopiesCompleted == 1 && owner.GetSnapshot().State == FinTsCredentialState.Consumed && retained.All(b => b == 0), "Concurrent TAN requests produce one copy and immediate owned-buffer zeroization.");
        }
        for (int i = 0; i < 16; i++)
        {
            using var owner = New(FinTsCredentialKind.Tan); byte[] retained = Buffer(owner)!, destination = new byte[99]; FinTsCredentialCopyResult result = default;
            Parallel.Invoke(owner.Cancel, () => result = owner.TryCopyTo(destination, out _));
            Verify(owner.GetSnapshot().State is FinTsCredentialState.Cancelled or FinTsCredentialState.Consumed && retained.All(b => b == 0) &&
                (result == FinTsCredentialCopyResult.Copied) == (owner.GetSnapshot().State == FinTsCredentialState.Consumed), "Cancellation/TAN copy races produce one terminal outcome without retaining secrets.");
            CryptographicOperations.ZeroMemory(destination);
        }
        using (var owner = New())
        {
            byte[] retained = Buffer(owner)!; int successes = 0;
            Parallel.For(0, 32, _ =>
            {
                byte[] destination = new byte[99];
                try { if (owner.TryCopyTo(destination, out _) == FinTsCredentialCopyResult.Copied) { Interlocked.Increment(ref successes); } }
                finally { CryptographicOperations.ZeroMemory(destination); }
            });
            owner.Dispose();
            Verify(successes == 32 && owner.GetSnapshot().CopiesCompleted == 32 && retained.All(b => b == 0), "PIN copies serialize without premature consumption and disposal clears the remaining owner.");
        }
        var abandoned = Abandon();
        for (int i = 0; i < 3 && abandoned.Owner.IsAlive; i++) { GC.Collect(); GC.WaitForPendingFinalizers(); GC.Collect(); }
        Verify(!abandoned.Owner.IsAlive && abandoned.Bytes.All(b => b == 0), "Finalization provides fallback zeroization for an abandoned owner.");
        using (var owner = New())
        {
            Verify(typeof(FinTsCredentialSnapshot).GetProperties().All(p => p.PropertyType.IsValueType) && typeof(FinTsSessionCredential).GetProperties().Length == 0 &&
                !owner.ToString()!.Contains("PUBLIC", StringComparison.Ordinal) && !JsonSerializer.Serialize(owner.GetSnapshot()).Contains("PUBLIC", StringComparison.Ordinal), "Default diagnostics and scalar snapshots expose no credential bytes or lengths.");
        }
        Console.WriteLine($"FinTS session credential ownership verification passed ({vectors.Length} independent traces, {count} checks).");
    }
    private static byte[]? Buffer(FinTsSessionCredential owner) => (byte[]?)BufferField.GetValue(owner);
    [MethodImpl(MethodImplOptions.NoInlining)]
    private static (WeakReference Owner, byte[] Bytes) Abandon()
    {
        var owner = FinTsSessionCredential.CaptureAndClear(new byte[] { 1, 2, 3 }, FinTsCredentialKind.Pin, TimeSpan.FromSeconds(10), new Clock());
        return (new WeakReference(owner), Buffer(owner)!);
    }
    private class Clock : TimeProvider
    {
        internal long Milliseconds { get; set; }
        public override long TimestampFrequency => 1000;
        public override long GetTimestamp() => Milliseconds;
        public override DateTimeOffset GetUtcNow() => throw new InvalidOperationException("Wall clock must not be used.");
    }
    private sealed class HookClock : Clock
    {
        internal int AfterReads { get; set; } = int.MaxValue;
        internal Action? Action { get; set; }
        public override long GetTimestamp() { if (--AfterReads == 0) { Action?.Invoke(); } return Milliseconds; }
    }
    private sealed class InvalidClock : TimeProvider { public override long TimestampFrequency => 0; }
    private sealed class ThrowingClock : TimeProvider { public override long GetTimestamp() => throw new InvalidOperationException("PUBLIC-SECRET"); }
}
