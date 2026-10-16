using System.Buffers.Binary;
using System.Security.Cryptography;

namespace Broiler.Fond.Kernel.Storage;

/// <summary>
/// Seekable read-only stream over an encrypted v1 profile envelope that authenticates each
/// 64 KiB AES-256-GCM frame before returning any plaintext bytes to callers (such as ZipArchive).
/// </summary>
public sealed class SeekableAuthenticatedProfileStream : Stream
{
    private const int MaxCachedFrames = 4;

    private readonly Stream _innerStream;
    private readonly bool _leaveOpen;
    private readonly byte[] _header;
    private readonly byte[] _contentKey;
    private readonly AesGcm _aes;
    private readonly ulong _finalOrdinal;
    private readonly int _lastDataFrameLength;
    private readonly long _authenticatedPayloadLength;
    private readonly List<CachedFrame> _frameCache = new(MaxCachedFrames);
    private long _position;
    private bool _disposed;

    private sealed class CachedFrame(ulong ordinal, byte[] buffer, int length)
    {
        public ulong Ordinal { get; } = ordinal;
        public byte[] Buffer { get; } = buffer;
        public int Length { get; } = length;
    }

    private SeekableAuthenticatedProfileStream(
        Stream innerStream,
        bool leaveOpen,
        byte[] header,
        byte[] contentKey,
        AesGcm aes,
        ulong finalOrdinal,
        int lastDataFrameLength,
        long authenticatedPayloadLength)
    {
        _innerStream = innerStream;
        _leaveOpen = leaveOpen;
        _header = header;
        _contentKey = contentKey;
        _aes = aes;
        _finalOrdinal = finalOrdinal;
        _lastDataFrameLength = lastDataFrameLength;
        _authenticatedPayloadLength = authenticatedPayloadLength;
    }

    public ReadOnlySpan<byte> Header
    {
        get
        {
            EnsureNotDisposed();
            return _header;
        }
    }

    public ReadOnlySpan<byte> ProfileSalt
    {
        get
        {
            EnsureNotDisposed();
            return _header.AsSpan(24, 32);
        }
    }

    public int Iterations
    {
        get
        {
            EnsureNotDisposed();
            return (int)BinaryPrimitives.ReadUInt32LittleEndian(_header.AsSpan(16, 4));
        }
    }

    public static SeekableAuthenticatedProfileStream Open(
        Stream innerStream,
        string passphrase,
        bool leaveOpen = false)
    {
        ArgumentNullException.ThrowIfNull(innerStream);
        ProfileEnvelopeCodec.EnsureCryptographicHostSupport();
        if (!innerStream.CanRead || !innerStream.CanSeek)
        {
            throw new ArgumentException("Inner stream must be readable and seekable.", nameof(innerStream));
        }

        ProfileEnvelopeCodec.ValidateEnvelopeSize(innerStream.Length);
        byte[] header = new byte[ProfileEnvelopeCodec.HeaderSize];
        innerStream.Position = 0;
        innerStream.ReadExactly(header);
        ProfileEnvelopeCodec.ValidateHeader(header);

        byte[] masterKey = ProfileEnvelopeCodec.DeriveMasterKey(header, passphrase);
        try
        {
            byte[] contentKey = ProfileEnvelopeCodec.DeriveContentKey(header, masterKey);
            return OpenCore(innerStream, leaveOpen, header, contentKey);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(masterKey);
        }
    }

    public static SeekableAuthenticatedProfileStream OpenWithSession(
        Stream innerStream,
        ProfileMasterKeySession session,
        bool leaveOpen = false)
    {
        ArgumentNullException.ThrowIfNull(innerStream);
        ArgumentNullException.ThrowIfNull(session);
        ProfileEnvelopeCodec.EnsureCryptographicHostSupport();
        if (!innerStream.CanRead || !innerStream.CanSeek)
        {
            throw new ArgumentException("Inner stream must be readable and seekable.", nameof(innerStream));
        }

        ProfileEnvelopeCodec.ValidateEnvelopeSize(innerStream.Length);
        byte[] header = new byte[ProfileEnvelopeCodec.HeaderSize];
        innerStream.Position = 0;
        innerStream.ReadExactly(header);
        ProfileEnvelopeCodec.ValidateHeader(header);

        if (!session.MatchesHeader(header))
        {
            throw new InvalidDataException("Envelope header salt or KDF parameters do not match active profile session.");
        }

        byte[] contentKey = session.DeriveContentKey(header);
        return OpenCore(innerStream, leaveOpen, header, contentKey);
    }

    private static SeekableAuthenticatedProfileStream OpenCore(
        Stream innerStream,
        bool leaveOpen,
        byte[] header,
        byte[] contentKey)
    {
        AesGcm? aes = null;
        try
        {
            aes = new AesGcm(contentKey, ProfileEnvelopeCodec.TagSize);
            long physicalLength = innerStream.Length;

            // 1. Authenticate the final empty record at stream.Length - 29 before exposing Length.
            long finalRecordOffset = physicalLength - ProfileEnvelopeCodec.RecordOverhead;
            ProfileEnvelopeCodec.Require(finalRecordOffset >= ProfileEnvelopeCodec.HeaderSize);

            Span<byte> finalRecord = stackalloc byte[ProfileEnvelopeCodec.RecordOverhead];
            innerStream.Position = finalRecordOffset;
            innerStream.ReadExactly(finalRecord);

            ReadOnlySpan<byte> finalDescriptor = finalRecord[..ProfileEnvelopeCodec.DescriptorSize];
            ulong finalOrdinal = BinaryPrimitives.ReadUInt64LittleEndian(finalDescriptor);
            uint finalPlaintextLen = BinaryPrimitives.ReadUInt32LittleEndian(finalDescriptor[8..]);
            byte finalFlag = finalDescriptor[12];

            ProfileEnvelopeCodec.Require(finalFlag == 1, "Final envelope record flag must be 1.");
            ProfileEnvelopeCodec.Require(finalPlaintextLen == 0, "Final envelope record plaintext length must be 0.");
            ProfileEnvelopeCodec.Require(finalOrdinal <= 16_384, "Final envelope record ordinal exceeds 1 GiB frame bound.");

            AuthenticateRecord(aes, header, finalOrdinal, finalDescriptor, [], finalRecord[ProfileEnvelopeCodec.DescriptorSize..], []);

            // 2. If finalOrdinal == 0, the envelope must be exactly 125 bytes (0 payload).
            int lastDataLength = 0;
            long payloadLength = 0;
            if (finalOrdinal == 0)
            {
                ProfileEnvelopeCodec.Require(physicalLength == ProfileEnvelopeCodec.MinEnvelopePhysicalLength,
                    "Zero-frame envelope has unexpected physical length.");
            }
            else
            {
                // Full frames 0 .. finalOrdinal - 2 each occupy exactly MaxRecordSize (65,565 bytes).
                ulong lastDataOrdinal = finalOrdinal - 1;
                long lastDataOffset = ProfileEnvelopeCodec.HeaderSize + (long)lastDataOrdinal * ProfileEnvelopeCodec.MaxRecordSize;
                long lastDataPhysicalSize = finalRecordOffset - lastDataOffset;
                long expectedCiphertextLen = lastDataPhysicalSize - ProfileEnvelopeCodec.RecordOverhead;

                ProfileEnvelopeCodec.Require(expectedCiphertextLen is >= 1 and <= ProfileEnvelopeCodec.FrameSize,
                    "Last data frame physical size is inconsistent with frame layout.");

                lastDataLength = (int)expectedCiphertextLen;
                payloadLength = (long)lastDataOrdinal * ProfileEnvelopeCodec.FrameSize + lastDataLength;
                ProfileEnvelopeCodec.Require(payloadLength <= ProfileEnvelopeCodec.MaxPayloadLimit);

                // Authenticate the last data frame immediately so the exposed Length is cryptographically verified.
                byte[] lastPlaintext = new byte[lastDataLength];
                try
                {
                    ReadAndAuthenticateDataFrame(innerStream, aes, header, lastDataOrdinal, lastDataLength, lastPlaintext);
                }
                finally
                {
                    CryptographicOperations.ZeroMemory(lastPlaintext);
                }
            }

            SeekableAuthenticatedProfileStream stream = new(
                innerStream,
                leaveOpen,
                header,
                contentKey,
                aes,
                finalOrdinal,
                lastDataLength,
                payloadLength);
            aes = null;
            return stream;
        }
        catch
        {
            aes?.Dispose();
            CryptographicOperations.ZeroMemory(contentKey);
            throw;
        }
    }

    /// <summary>
    /// Sequentially reads and authenticates every frame (0 .. finalOrdinal) to prove whole-file integrity.
    /// </summary>
    public void VerifyAllFrames()
    {
        EnsureNotDisposed();
        byte[] buffer = new byte[ProfileEnvelopeCodec.FrameSize];
        try
        {
            for (ulong ordinal = 0; ordinal < _finalOrdinal; ordinal++)
            {
                int expectedLen = ordinal == _finalOrdinal - 1
                    ? _lastDataFrameLength
                    : ProfileEnvelopeCodec.FrameSize;
                ReadAndAuthenticateDataFrame(_innerStream, _aes, _header, ordinal, expectedLen, buffer.AsSpan(0, expectedLen));
                CryptographicOperations.ZeroMemory(buffer.AsSpan(0, expectedLen));
            }
        }
        finally
        {
            CryptographicOperations.ZeroMemory(buffer);
        }
    }

    public override bool CanRead => !_disposed;
    public override bool CanSeek => !_disposed;
    public override bool CanWrite => false;

    public override long Length
    {
        get
        {
            EnsureNotDisposed();
            return _authenticatedPayloadLength;
        }
    }

    public override long Position
    {
        get
        {
            EnsureNotDisposed();
            return _position;
        }
        set
        {
            EnsureNotDisposed();
            if (value < 0 || value > _authenticatedPayloadLength)
            {
                throw new ArgumentOutOfRangeException(nameof(value), "Stream position is outside authenticated payload bounds.");
            }

            _position = value;
        }
    }

    public override int Read(byte[] buffer, int offset, int count)
    {
        ValidateBufferArguments(buffer, offset, count);
        return Read(buffer.AsSpan(offset, count));
    }

    public override int Read(Span<byte> buffer)
    {
        EnsureNotDisposed();
        if (buffer.IsEmpty || _position >= _authenticatedPayloadLength)
        {
            return 0;
        }

        int totalRead = 0;
        while (!buffer.IsEmpty && _position < _authenticatedPayloadLength)
        {
            ulong frameOrdinal = (ulong)(_position / ProfileEnvelopeCodec.FrameSize);
            int offsetInFrame = (int)(_position % ProfileEnvelopeCodec.FrameSize);
            CachedFrame frame = GetOrLoadAuthenticatedFrame(frameOrdinal);

            int availableInFrame = frame.Length - offsetInFrame;
            int toCopy = Math.Min(buffer.Length, availableInFrame);
            frame.Buffer.AsSpan(offsetInFrame, toCopy).CopyTo(buffer);

            buffer = buffer[toCopy..];
            _position += toCopy;
            totalRead += toCopy;
        }

        return totalRead;
    }

    public override long Seek(long offset, SeekOrigin origin)
    {
        EnsureNotDisposed();
        long target = origin switch
        {
            SeekOrigin.Begin => offset,
            SeekOrigin.Current => _position + offset,
            SeekOrigin.End => _authenticatedPayloadLength + offset,
            _ => throw new ArgumentOutOfRangeException(nameof(origin)),
        };

        if (target < 0 || target > _authenticatedPayloadLength)
        {
            throw new IOException("Attempted to seek outside authenticated payload bounds.");
        }

        _position = target;
        return _position;
    }

    public override void Flush() => EnsureNotDisposed();

    public override void SetLength(long value) => throw new NotSupportedException();

    public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();

    private CachedFrame GetOrLoadAuthenticatedFrame(ulong ordinal)
    {
        for (int i = 0; i < _frameCache.Count; i++)
        {
            if (_frameCache[i].Ordinal == ordinal)
            {
                CachedFrame hit = _frameCache[i];
                if (i != _frameCache.Count - 1)
                {
                    _frameCache.RemoveAt(i);
                    _frameCache.Add(hit);
                }

                return hit;
            }
        }

        int expectedLen = ordinal == _finalOrdinal - 1
            ? _lastDataFrameLength
            : ProfileEnvelopeCodec.FrameSize;

        byte[] plaintext = new byte[expectedLen];
        try
        {
            ReadAndAuthenticateDataFrame(_innerStream, _aes, _header, ordinal, expectedLen, plaintext);
        }
        catch
        {
            CryptographicOperations.ZeroMemory(plaintext);
            throw;
        }

        if (_frameCache.Count == MaxCachedFrames)
        {
            CachedFrame evicted = _frameCache[0];
            _frameCache.RemoveAt(0);
            CryptographicOperations.ZeroMemory(evicted.Buffer);
        }

        CachedFrame loaded = new(ordinal, plaintext, expectedLen);
        _frameCache.Add(loaded);
        return loaded;
    }

    private static void ReadAndAuthenticateDataFrame(
        Stream innerStream,
        AesGcm aes,
        ReadOnlySpan<byte> header,
        ulong expectedOrdinal,
        int expectedLength,
        Span<byte> destinationPlaintext)
    {
        long frameOffset = ProfileEnvelopeCodec.HeaderSize + (long)expectedOrdinal * ProfileEnvelopeCodec.MaxRecordSize;
        int totalRecordBytes = ProfileEnvelopeCodec.DescriptorSize + expectedLength + ProfileEnvelopeCodec.TagSize;
        ProfileEnvelopeCodec.Require(frameOffset + totalRecordBytes <= innerStream.Length - ProfileEnvelopeCodec.RecordOverhead,
            "Data frame offset extends into or beyond the final record.");

        byte[] rawRecord = new byte[totalRecordBytes];
        try
        {
            innerStream.Position = frameOffset;
            innerStream.ReadExactly(rawRecord);

            ReadOnlySpan<byte> descriptor = rawRecord.AsSpan(0, ProfileEnvelopeCodec.DescriptorSize);
            ulong actualOrdinal = BinaryPrimitives.ReadUInt64LittleEndian(descriptor);
            uint actualLength = BinaryPrimitives.ReadUInt32LittleEndian(descriptor[8..]);
            byte flag = descriptor[12];

            ProfileEnvelopeCodec.Require(actualOrdinal == expectedOrdinal, "Data frame ordinal mismatch.");
            ProfileEnvelopeCodec.Require(flag == 0, "Data frame final flag must be 0.");
            ProfileEnvelopeCodec.Require(actualLength == (uint)expectedLength, "Data frame length mismatch.");

            ReadOnlySpan<byte> ciphertext = rawRecord.AsSpan(ProfileEnvelopeCodec.DescriptorSize, expectedLength);
            ReadOnlySpan<byte> tag = rawRecord.AsSpan(ProfileEnvelopeCodec.DescriptorSize + expectedLength, ProfileEnvelopeCodec.TagSize);
            AuthenticateRecord(aes, header, expectedOrdinal, descriptor, ciphertext, tag, destinationPlaintext);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(rawRecord);
        }
    }

    private static void AuthenticateRecord(
        AesGcm aes,
        ReadOnlySpan<byte> header,
        ulong ordinal,
        ReadOnlySpan<byte> descriptor,
        ReadOnlySpan<byte> ciphertext,
        ReadOnlySpan<byte> tag,
        Span<byte> destinationPlaintext)
    {
        Span<byte> nonce = stackalloc byte[12];
        header.Slice(88, 4).CopyTo(nonce);
        BinaryPrimitives.WriteUInt64BigEndian(nonce[4..], ordinal);

        Span<byte> aad = stackalloc byte[ProfileEnvelopeCodec.HeaderSize + ProfileEnvelopeCodec.DescriptorSize];
        header.CopyTo(aad);
        descriptor.CopyTo(aad[ProfileEnvelopeCodec.HeaderSize..]);

        try
        {
            aes.Decrypt(nonce, ciphertext, tag, destinationPlaintext, aad);
        }
        catch (CryptographicException ex)
        {
            CryptographicOperations.ZeroMemory(destinationPlaintext);
            throw new ProfileAuthenticationException($"Profile envelope authentication failed at frame {ordinal}.", ex);
        }
    }

    protected override void Dispose(bool disposing)
    {
        if (!_disposed)
        {
            if (disposing)
            {
                foreach (CachedFrame cached in _frameCache)
                {
                    CryptographicOperations.ZeroMemory(cached.Buffer);
                }

                _frameCache.Clear();
                _aes.Dispose();
                CryptographicOperations.ZeroMemory(_contentKey);
                CryptographicOperations.ZeroMemory(_header);
                if (!_leaveOpen)
                {
                    _innerStream.Dispose();
                }
            }

            _disposed = true;
        }

        base.Dispose(disposing);
    }

    private void EnsureNotDisposed() => ObjectDisposedException.ThrowIf(_disposed, this);
}
