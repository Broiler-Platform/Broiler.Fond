using System.Text;
using System.Text.Json;

namespace Broiler.Fond.Kernel.Diagnostics;

public enum LocalDiagnosticCode
{
    SessionStarted = 1, Authentication, ChallengeIssued, ChallengeContinued,
    ReadCompleted, ParametersChanged, ResponseRejected, SessionCancelled, SessionTimedOut,
}

public enum LocalDiagnosticOutcome
{
    Pending = 1, Succeeded, InvalidCredentials, AccessLocked, PartialResult,
    TemporarilyUnavailable, Cancelled, TimedOut, MalformedResponse, ReauthenticationRequired,
}

/// <summary>
/// An immutable preview, not a file export or approval. The application must
/// present these exact bytes for review before a future explicit export.
/// </summary>
public sealed class DiagnosticSupportPreview
{
    internal DiagnosticSupportPreview(string json, int eventCount, ulong droppedEventCount, bool includesTimestamps)
    {
        Json = json;
        EventCount = eventCount;
        DroppedEventCount = droppedEventCount;
        IncludesTimestamps = includesTimestamps;
    }

    public string Json { get; }
    public int EventCount { get; }
    public ulong DroppedEventCount { get; }
    public bool IncludesTimestamps { get; }
}

/// <summary>
/// Opt-in bounded in-memory diagnostics. Predefined enums are the only payload;
/// this is not an arbitrary log redactor or an encrypted persistent log store.
/// </summary>
public sealed class LocalDiagnosticBuffer : ILocalDiagnosticSink
{
    public const int MaximumCapacity = 1024;
    private readonly object _gate = new();
    private readonly DiagnosticEvent?[] _events;
    private bool _enabled;
    private int _start;
    private int _count;
    private ulong _dropped;

    public LocalDiagnosticBuffer(int capacity = 256, bool enabled = false)
    {
        if (capacity is < 1 or > MaximumCapacity)
        {
            throw new ArgumentOutOfRangeException(nameof(capacity));
        }

        _events = new DiagnosticEvent[capacity];
        _enabled = enabled;
    }

    public bool IsEnabled
    {
        get { lock (_gate) { return _enabled; } }
    }

    /// <summary>Disabling diagnostics also clears retained events and drop counts.</summary>
    public void SetEnabled(bool enabled)
    {
        lock (_gate)
        {
            _enabled = enabled;
            if (!enabled)
            {
                ClearCore();
            }
        }
    }

    public void Record(LocalDiagnosticCode code, LocalDiagnosticOutcome outcome, DateTimeOffset timestamp)
    {
        if (!Enum.IsDefined(code) || !Enum.IsDefined(outcome))
        {
            throw new ArgumentException("Unknown local diagnostic code or outcome.");
        }

        lock (_gate)
        {
            if (!_enabled)
            {
                return;
            }

            if (_count == _events.Length)
            {
                _events[_start] = new(code, outcome, timestamp);
                _start = (_start + 1) % _events.Length;
                if (_dropped < ulong.MaxValue)
                {
                    _dropped++;
                }
            }
            else
            {
                _events[(_start + _count) % _events.Length] = new(code, outcome, timestamp);
                _count++;
            }
        }
    }

    public void Clear()
    {
        lock (_gate) { ClearCore(); }
    }

    public DiagnosticSupportPreview CreateSupportPreview(bool includeTimestamps = false)
    {
        lock (_gate)
        {
            using MemoryStream output = new();
            using (Utf8JsonWriter writer = new(output))
            {
                writer.WriteStartObject();
                writer.WriteNumber("schemaVersion", 1);
                writer.WriteNumber("droppedEvents", _dropped);
                writer.WriteStartArray("events");
                for (int index = 0; index < _count; index++)
                {
                    DiagnosticEvent entry = _events[(_start + index) % _events.Length]!;
                    writer.WriteStartObject();
                    writer.WriteString("code", entry.Code.ToString());
                    writer.WriteString("outcome", entry.Outcome.ToString());
                    if (includeTimestamps)
                    {
                        writer.WriteString("timestamp", entry.Timestamp.ToUniversalTime());
                    }

                    writer.WriteEndObject();
                }

                writer.WriteEndArray();
                writer.WriteEndObject();
            }

            return new(Encoding.UTF8.GetString(output.GetBuffer(), 0, (int)output.Length), _count, _dropped, includeTimestamps);
        }
    }

    private void ClearCore()
    {
        Array.Clear(_events);
        _start = 0;
        _count = 0;
        _dropped = 0;
    }

    private sealed record DiagnosticEvent(LocalDiagnosticCode Code, LocalDiagnosticOutcome Outcome, DateTimeOffset Timestamp);
}
