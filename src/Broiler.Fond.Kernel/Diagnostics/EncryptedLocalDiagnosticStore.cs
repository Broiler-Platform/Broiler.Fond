using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Broiler.Fond.Kernel.Storage;

namespace Broiler.Fond.Kernel.Diagnostics;

/// <summary>
/// Opt-in encrypted on-device diagnostic store and preview-bound support exporter implementing M1-08.
/// Persists only predefined <see cref="LocalDiagnosticCode"/> and <see cref="LocalDiagnosticOutcome"/>
/// enum values inside a v1 AES-256-GCM envelope (<c>diagnostics.bffond</c>) with zero network upload path.
/// </summary>
public sealed class EncryptedLocalDiagnosticStore : ILocalDiagnosticSink
{
    public const string DiagnosticFileName = "diagnostics.bffond";
    public const int MaxDiagnosticPayloadBytes = 256 * 1024;

    private static readonly UTF8Encoding StrictUtf8 = new(false, true);

    private readonly object _gate = new();
    private readonly string _directoryPath;
    private readonly LocalDiagnosticBuffer _buffer;

    public EncryptedLocalDiagnosticStore(
        string directoryPath,
        int capacity = 256,
        bool enabled = false)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(directoryPath);
        _directoryPath = Path.GetFullPath(directoryPath);
        _buffer = new LocalDiagnosticBuffer(capacity, enabled);
    }

    public string DirectoryPath => _directoryPath;

    public string FilePath => Path.Combine(_directoryPath, DiagnosticFileName);

    public bool IsEnabled => _buffer.IsEnabled;

    /// <summary>
    /// Enables or disables local diagnostics. Disabling diagnostics immediately clears
    /// in-memory events and deletes any persisted <c>diagnostics.bffond</c> file on disk.
    /// </summary>
    public void SetEnabled(bool enabled)
    {
        lock (_gate)
        {
            _buffer.SetEnabled(enabled);
            if (!enabled)
            {
                DeleteDiagnosticFilesCore();
            }
        }
    }

    public void Record(LocalDiagnosticCode code, LocalDiagnosticOutcome outcome, DateTimeOffset timestamp) =>
        _buffer.Record(code, outcome, timestamp);

    /// <summary>
    /// Clears all in-memory diagnostic events and removes any persisted diagnostic file from disk.
    /// </summary>
    public void PurgeFromDisk()
    {
        lock (_gate)
        {
            _buffer.Clear();
            DeleteDiagnosticFilesCore();
        }
    }

    /// <summary>
    /// Saves the current opt-in diagnostic buffer to <c>diagnostics.bffond</c> encrypted under the
    /// active <see cref="ProfileMasterKeySession"/> using atomic candidate write + promotion.
    /// If diagnostics are disabled, ensures no diagnostic file remains on disk.
    /// </summary>
    public void SaveToDisk(ProfileMasterKeySession session)
    {
        ArgumentNullException.ThrowIfNull(session);
        ProfileEnvelopeCodec.EnsureCryptographicHostSupport();

        lock (_gate)
        {
            Directory.CreateDirectory(_directoryPath);
            if (!_buffer.IsEnabled)
            {
                DeleteDiagnosticFilesCore();
                return;
            }

            (ulong dropped, IReadOnlyList<LocalDiagnosticEntry> entries) = _buffer.SnapshotState();
            byte[] jsonBytes = SerializeStateToUtf8(dropped, entries);
            byte[] header = ProfileEnvelopeCodec.CreateHeader(session.ProfileSalt, session.Iterations);
            byte[] contentKey = session.DeriveContentKey(header);

            Span<byte> randomId = stackalloc byte[16];
            RandomNumberGenerator.Fill(randomId);
            string tempPath = Path.Combine(_directoryPath, $"diagnostics.new-{Convert.ToHexStringLower(randomId)}.tmp");
            string finalPath = FilePath;

            try
            {
                using (FileStream fs = new(tempPath, FileMode.CreateNew, FileAccess.ReadWrite, FileShare.None))
                {
                    ProfileEnvelopeCodec.EncryptToStream(fs, jsonBytes, header, contentKey);
                    fs.Flush(flushToDisk: true);
                }

                // Verify candidate before promotion
                using (FileStream verifyFs = new(tempPath, FileMode.Open, FileAccess.Read, FileShare.Read))
                using (SeekableAuthenticatedProfileStream auth = SeekableAuthenticatedProfileStream.OpenWithSession(verifyFs, session, leaveOpen: true))
                {
                    auth.VerifyAllFrames();
                }

                File.Move(tempPath, finalPath, overwrite: true);
            }
            finally
            {
                CryptographicOperations.ZeroMemory(jsonBytes);
                CryptographicOperations.ZeroMemory(contentKey);
                CryptographicOperations.ZeroMemory(header);
                if (File.Exists(tempPath))
                {
                    File.Delete(tempPath);
                }
            }
        }
    }

    /// <summary>
    /// Loads and authenticates <c>diagnostics.bffond</c> from disk using the active <see cref="ProfileMasterKeySession"/>.
    /// Returns false if no diagnostic file exists on disk.
    /// </summary>
    public bool TryLoadFromDisk(ProfileMasterKeySession session)
    {
        ArgumentNullException.ThrowIfNull(session);
        ProfileEnvelopeCodec.EnsureCryptographicHostSupport();

        lock (_gate)
        {
            string path = FilePath;
            if (!File.Exists(path))
            {
                return false;
            }

            using FileStream fs = new(path, FileMode.Open, FileAccess.Read, FileShare.Read);
            using SeekableAuthenticatedProfileStream auth = SeekableAuthenticatedProfileStream.OpenWithSession(fs, session, leaveOpen: true);
            auth.VerifyAllFrames();

            if (auth.Length is 0 or > MaxDiagnosticPayloadBytes)
            {
                throw new InvalidDataException($"Encrypted diagnostic payload length {auth.Length} is outside allowed bounds.");
            }

            byte[] payload = new byte[(int)auth.Length];
            try
            {
                auth.Position = 0;
                auth.ReadExactly(payload);
                (ulong dropped, List<LocalDiagnosticEntry> entries) = DeserializeAndValidateState(payload);
                _buffer.SetEnabled(true);
                _buffer.LoadState(dropped, entries);
                return true;
            }
            finally
            {
                CryptographicOperations.ZeroMemory(payload);
            }
        }
    }

    /// <summary>
    /// Creates an immutable redacted support preview of the current diagnostic events.
    /// </summary>
    public DiagnosticSupportPreview CreateSupportPreview(bool includeTimestamps = false) =>
        _buffer.CreateSupportPreview(includeTimestamps);

    /// <summary>
    /// Exports the exact bytes of a user-reviewed <see cref="DiagnosticSupportPreview"/> to a local file
    /// only if the caller supplies the matching <paramref name="confirmedPreviewSha256"/> digest.
    /// Never performs any network I/O.
    /// </summary>
    public static void ExportReviewedSupportPreview(
        DiagnosticSupportPreview preview,
        string confirmedPreviewSha256,
        string destinationFilePath)
    {
        ArgumentNullException.ThrowIfNull(preview);
        ArgumentException.ThrowIfNullOrWhiteSpace(confirmedPreviewSha256);
        ArgumentException.ThrowIfNullOrWhiteSpace(destinationFilePath);

        byte[] utf8Bytes = StrictUtf8.GetBytes(preview.Json);
        string actualSha = Convert.ToHexStringLower(SHA256.HashData(utf8Bytes));
        if (!string.Equals(actualSha, preview.PreviewSha256, StringComparison.OrdinalIgnoreCase) ||
            !string.Equals(actualSha, confirmedPreviewSha256, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                "Support export rejected: confirmed preview SHA-256 digest does not match the reviewed DiagnosticSupportPreview bytes.");
        }

        string fullDest = Path.GetFullPath(destinationFilePath);
        string? parentDir = Path.GetDirectoryName(fullDest);
        if (!string.IsNullOrEmpty(parentDir))
        {
            Directory.CreateDirectory(parentDir);
        }

        File.WriteAllBytes(fullDest, utf8Bytes);
    }

    private void DeleteDiagnosticFilesCore()
    {
        if (!Directory.Exists(_directoryPath))
        {
            return;
        }

        string mainPath = FilePath;
        if (File.Exists(mainPath))
        {
            File.Delete(mainPath);
        }

        foreach (string tmp in Directory.EnumerateFiles(_directoryPath, "diagnostics.new-*.tmp"))
        {
            File.Delete(tmp);
        }
    }

    private static byte[] SerializeStateToUtf8(ulong droppedEvents, IReadOnlyList<LocalDiagnosticEntry> entries)
    {
        using MemoryStream output = new();
        using (Utf8JsonWriter writer = new(output))
        {
            writer.WriteStartObject();
            writer.WriteNumber("schemaVersion", 1);
            writer.WriteNumber("droppedEvents", droppedEvents);
            writer.WriteStartArray("events");
            foreach (LocalDiagnosticEntry entry in entries)
            {
                writer.WriteStartObject();
                writer.WriteString("code", entry.Code.ToString());
                writer.WriteString("outcome", entry.Outcome.ToString());
                writer.WriteString("timestamp", entry.Timestamp.ToUniversalTime());
                writer.WriteEndObject();
            }

            writer.WriteEndArray();
            writer.WriteEndObject();
        }

        return output.ToArray();
    }

    private static (ulong DroppedEvents, List<LocalDiagnosticEntry> Entries) DeserializeAndValidateState(ReadOnlySpan<byte> utf8Json)
    {
        _ = StrictUtf8.GetCharCount(utf8Json);
        using JsonDocument doc = JsonDocument.Parse(utf8Json.ToArray(), new JsonDocumentOptions
        {
            AllowTrailingCommas = false,
            CommentHandling = JsonCommentHandling.Disallow,
            MaxDepth = 4,
        });

        JsonElement root = doc.RootElement;
        if (root.ValueKind != JsonValueKind.Object)
        {
            throw new InvalidDataException("Diagnostic root must be a JSON object.");
        }

        if (!root.TryGetProperty("schemaVersion", out JsonElement verEl) || verEl.GetInt32() != 1)
        {
            throw new InvalidDataException("Unsupported diagnostic schemaVersion.");
        }

        if (!root.TryGetProperty("droppedEvents", out JsonElement droppedEl) || !droppedEl.TryGetUInt64(out ulong dropped))
        {
            throw new InvalidDataException("Invalid or missing droppedEvents counter.");
        }

        if (!root.TryGetProperty("events", out JsonElement eventsEl) || eventsEl.ValueKind != JsonValueKind.Array)
        {
            throw new InvalidDataException("Missing events array in diagnostic payload.");
        }

        List<LocalDiagnosticEntry> entries = [];
        foreach (JsonElement item in eventsEl.EnumerateArray())
        {
            if (entries.Count >= LocalDiagnosticBuffer.MaximumCapacity)
            {
                throw new InvalidDataException("Persisted diagnostic event count exceeds MaximumCapacity.");
            }

            if (item.ValueKind != JsonValueKind.Object)
            {
                throw new InvalidDataException("Diagnostic event item must be a JSON object.");
            }

            // Ensure no unexpected properties (e.g. free text) exist on the object
            int propCount = 0;
            foreach (JsonProperty prop in item.EnumerateObject())
            {
                propCount++;
                if (prop.Name is not ("code" or "outcome" or "timestamp"))
                {
                    throw new InvalidDataException($"Forbidden property '{prop.Name}' in persisted diagnostic event.");
                }
            }

            if (propCount != 3)
            {
                throw new InvalidDataException("Persisted diagnostic event must have exactly code, outcome, and timestamp properties.");
            }

            string codeStr = item.GetProperty("code").GetString() ?? throw new InvalidDataException("Missing diagnostic code.");
            string outcomeStr = item.GetProperty("outcome").GetString() ?? throw new InvalidDataException("Missing diagnostic outcome.");
            DateTimeOffset timestamp = item.GetProperty("timestamp").GetDateTimeOffset();

            if (!Enum.TryParse<LocalDiagnosticCode>(codeStr, ignoreCase: false, out LocalDiagnosticCode code) || !Enum.IsDefined(code))
            {
                throw new InvalidDataException($"Unknown LocalDiagnosticCode '{codeStr}'.");
            }

            if (!Enum.TryParse<LocalDiagnosticOutcome>(outcomeStr, ignoreCase: false, out LocalDiagnosticOutcome outcome) || !Enum.IsDefined(outcome))
            {
                throw new InvalidDataException($"Unknown LocalDiagnosticOutcome '{outcomeStr}'.");
            }

            entries.Add(new LocalDiagnosticEntry(code, outcome, timestamp.ToUniversalTime()));
        }

        return (dropped, entries);
    }
}
