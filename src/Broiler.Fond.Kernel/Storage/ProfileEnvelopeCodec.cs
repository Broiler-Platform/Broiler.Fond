using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;

namespace Broiler.Fond.Kernel.Storage;

/// <summary>
/// Thrown when a profile generation cannot be authenticated or unlocked with the supplied passphrase.
/// </summary>
public sealed class ProfileAuthenticationException : CryptographicException
{
    public ProfileAuthenticationException(string message)
        : base(message)
    {
    }

    public ProfileAuthenticationException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}

/// <summary>
/// Holds an in-memory 32-byte master key bound to a specific profile lineage salt and KDF parameter tuple
/// for an unlocked session, zeroing the key material deterministically upon disposal or rotation.
/// </summary>
public sealed class ProfileMasterKeySession : IDisposable
{
    private readonly byte[] _profileSalt;
    private readonly byte[] _masterKey;
    private bool _disposed;

    public ProfileMasterKeySession(ReadOnlySpan<byte> profileSalt, int iterations, ReadOnlySpan<byte> masterKey)
    {
        if (profileSalt.Length != 32)
        {
            throw new ArgumentException("Profile salt must be 32 bytes.", nameof(profileSalt));
        }

        if (iterations is < ProfileEnvelopeCodec.MinimumIterations or > ProfileEnvelopeCodec.MaximumIterations)
        {
            throw new ArgumentOutOfRangeException(nameof(iterations));
        }

        if (masterKey.Length != 32)
        {
            throw new ArgumentException("Master key must be 32 bytes.", nameof(masterKey));
        }

        _profileSalt = profileSalt.ToArray();
        _masterKey = masterKey.ToArray();
        Iterations = iterations;
    }

    public int Iterations { get; }

    public ReadOnlySpan<byte> ProfileSalt
    {
        get
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            return _profileSalt;
        }
    }

    public bool MatchesHeader(ReadOnlySpan<byte> header)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (header.Length != ProfileEnvelopeCodec.HeaderSize)
        {
            return false;
        }

        uint headerIterations = BinaryPrimitives.ReadUInt32LittleEndian(header[16..]);
        return headerIterations == (uint)Iterations
            && header[12] == 1 // kdfId == 1
            && header[15] == 1 // passphraseRuleId == 1
            && CryptographicOperations.FixedTimeEquals(_profileSalt, header.Slice(24, 32));
    }

    public byte[] DeriveContentKey(ReadOnlySpan<byte> header)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (!MatchesHeader(header))
        {
            throw new InvalidOperationException("Header salt or KDF parameters do not match the cached master key session.");
        }

        return ProfileEnvelopeCodec.DeriveContentKey(header, _masterKey);
    }

    public void Dispose()
    {
        if (!_disposed)
        {
            CryptographicOperations.ZeroMemory(_masterKey);
            CryptographicOperations.ZeroMemory(_profileSalt);
            _disposed = true;
        }
    }
}

/// <summary>
/// Production profile envelope v1 codec implementing ADR 0004 (96-byte header, PBKDF2-HMAC-SHA256,
/// HKDF-SHA256, and framed AES-256-GCM authenticated encryption).
/// </summary>
public static class ProfileEnvelopeCodec
{
    public const int HeaderSize = 96;
    public const int FrameSize = 65_536;
    public const int DescriptorSize = 13;
    public const int TagSize = 16;
    public const int RecordOverhead = DescriptorSize + TagSize;
    public const int MaxRecordSize = FrameSize + RecordOverhead;
    public const long MaxPayloadLimit = 1_073_741_824L; // 1 GiB (16,384 full frames)
    public const long MinEnvelopePhysicalLength = HeaderSize + RecordOverhead; // 125 bytes
    public const long MaxEnvelopePhysicalLength = HeaderSize + MaxPayloadLimit + (16_384L + 1L) * RecordOverhead; // 1,074,217,085 bytes
    public const int MinimumIterations = 600_000;
    public const int MaximumIterations = 5_000_000;
    public const int DefaultIterations = 600_000;

    private static readonly UTF8Encoding StrictUtf8 = new(false, true);

    public static void EnsureCryptographicHostSupport()
    {
        if (!AesGcm.IsSupported)
        {
            throw new PlatformNotSupportedException("AES-GCM is not supported on this host runtime; profile storage fails closed.");
        }
    }

    public static byte[] NormalizePassphrase(string passphrase)
    {
        ArgumentNullException.ThrowIfNull(passphrase);
        if (passphrase.Length is 0 or > 1024)
        {
            throw new ArgumentException("Passphrase length is outside the format bounds.");
        }

        string normalized = passphrase.Normalize(NormalizationForm.FormC);
        int length = StrictUtf8.GetByteCount(normalized);
        if (length > 1024)
        {
            throw new ArgumentException("Normalized passphrase is outside the format bounds.");
        }

        return StrictUtf8.GetBytes(normalized);
    }

    public static byte[] CreateHeader(ReadOnlySpan<byte> profileSalt, int iterations = DefaultIterations)
    {
        if (profileSalt.Length != 32)
        {
            throw new ArgumentException("Profile salt must be 32 bytes.", nameof(profileSalt));
        }

        if (iterations is < MinimumIterations or > MaximumIterations)
        {
            throw new ArgumentOutOfRangeException(nameof(iterations),
                $"Iterations {iterations} outside allowed bounds ({MinimumIterations}..{MaximumIterations}).");
        }

        byte[] header = new byte[HeaderSize];
        "BFONDPRF"u8.CopyTo(header.AsSpan(0, 8));
        BinaryPrimitives.WriteUInt16LittleEndian(header.AsSpan(8, 2), 1);
        BinaryPrimitives.WriteUInt16LittleEndian(header.AsSpan(10, 2), HeaderSize);
        header[12] = 1; // kdfId: PBKDF2-HMAC-SHA256
        header[13] = 1; // hkdfId: HKDF-SHA256
        header[14] = 1; // aeadId: AES-256-GCM
        header[15] = 1; // passphraseRuleId: NFC UTF-8
        BinaryPrimitives.WriteUInt32LittleEndian(header.AsSpan(16, 4), (uint)iterations);
        BinaryPrimitives.WriteUInt32LittleEndian(header.AsSpan(20, 4), FrameSize);
        profileSalt.CopyTo(header.AsSpan(24, 32));
        RandomNumberGenerator.Fill(header.AsSpan(56, 32)); // fresh snapshotSalt
        RandomNumberGenerator.Fill(header.AsSpan(88, 4));  // fresh noncePrefix
        BinaryPrimitives.WriteUInt32LittleEndian(header.AsSpan(92, 4), 0); // reserved == 0
        return header;
    }

    public static void ValidateHeader(ReadOnlySpan<byte> header)
    {
        Require(header.Length == HeaderSize);
        Require(header[..8].SequenceEqual("BFONDPRF"u8));
        Require(BinaryPrimitives.ReadUInt16LittleEndian(header[8..]) == 1);
        Require(BinaryPrimitives.ReadUInt16LittleEndian(header[10..]) == HeaderSize);
        Require(header[12..16].SequenceEqual(new byte[] { 1, 1, 1, 1 }));
        uint iterations = BinaryPrimitives.ReadUInt32LittleEndian(header[16..]);
        Require(iterations is >= MinimumIterations and <= MaximumIterations);
        Require(BinaryPrimitives.ReadUInt32LittleEndian(header[20..]) == FrameSize);
        Require(BinaryPrimitives.ReadUInt32LittleEndian(header[92..]) == 0);
    }

    public static byte[] DeriveMasterKey(ReadOnlySpan<byte> header, string passphrase)
    {
        EnsureCryptographicHostSupport();
        ValidateHeader(header);
        byte[] password = NormalizePassphrase(passphrase);
        try
        {
            return Rfc2898DeriveBytes.Pbkdf2(password, header.Slice(24, 32),
                (int)BinaryPrimitives.ReadUInt32LittleEndian(header[16..]), HashAlgorithmName.SHA256, 32);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(password);
        }
    }

    public static ProfileMasterKeySession CreateMasterKeySession(ReadOnlySpan<byte> profileSalt, int iterations, string passphrase)
    {
        EnsureCryptographicHostSupport();
        if (profileSalt.Length != 32)
        {
            throw new ArgumentException("Profile salt must be 32 bytes.", nameof(profileSalt));
        }

        if (iterations is < MinimumIterations or > MaximumIterations)
        {
            throw new ArgumentOutOfRangeException(nameof(iterations));
        }

        byte[] password = NormalizePassphrase(passphrase);
        byte[] masterKey = Rfc2898DeriveBytes.Pbkdf2(password, profileSalt, iterations, HashAlgorithmName.SHA256, 32);
        try
        {
            return new ProfileMasterKeySession(profileSalt, iterations, masterKey);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(password);
            CryptographicOperations.ZeroMemory(masterKey);
        }
    }

    public static ProfileMasterKeySession CreateMasterKeySessionFromHeader(ReadOnlySpan<byte> header, string passphrase)
    {
        ValidateHeader(header);
        int iterations = (int)BinaryPrimitives.ReadUInt32LittleEndian(header[16..]);
        byte[] masterKey = DeriveMasterKey(header, passphrase);
        try
        {
            return new ProfileMasterKeySession(header.Slice(24, 32), iterations, masterKey);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(masterKey);
        }
    }

    public static byte[] DeriveContentKey(ReadOnlySpan<byte> header, ReadOnlySpan<byte> masterKey)
    {
        EnsureCryptographicHostSupport();
        ValidateHeader(header);
        Require(masterKey.Length == 32);
        byte[] result = new byte[32];
        HKDF.DeriveKey(HashAlgorithmName.SHA256, masterKey, result,
            header.Slice(56, 32), "Broiler.Fond/profile-envelope/v1/content"u8);
        return result;
    }

    public static void EncryptToStream(Stream output, ReadOnlySpan<byte> payload, ReadOnlySpan<byte> header, ReadOnlySpan<byte> contentKey)
    {
        ArgumentNullException.ThrowIfNull(output);
        EnsureCryptographicHostSupport();
        ValidateHeader(header);
        Require(payload.Length <= MaxPayloadLimit);
        Require(contentKey.Length == 32);

        using AesGcm aes = new(contentKey, TagSize);
        output.Write(header);
        ulong ordinal = 0;
        for (int offset = 0; offset < payload.Length; offset += FrameSize)
        {
            int frameLen = Math.Min(FrameSize, payload.Length - offset);
            WriteRecord(output, aes, header, ordinal++, payload.Slice(offset, frameLen), final: false);
        }

        WriteRecord(output, aes, header, ordinal, [], final: true);
    }

    public static byte[] Encrypt(ReadOnlySpan<byte> payload, ReadOnlySpan<byte> header, ReadOnlySpan<byte> contentKey)
    {
        using MemoryStream output = new();
        EncryptToStream(output, payload, header, contentKey);
        return output.ToArray();
    }

    private static void WriteRecord(
        Stream output,
        AesGcm aes,
        ReadOnlySpan<byte> header,
        ulong ordinal,
        ReadOnlySpan<byte> plaintext,
        bool final)
    {
        Span<byte> descriptor = stackalloc byte[DescriptorSize];
        BinaryPrimitives.WriteUInt64LittleEndian(descriptor, ordinal);
        BinaryPrimitives.WriteUInt32LittleEndian(descriptor[8..], (uint)plaintext.Length);
        descriptor[12] = final ? (byte)1 : (byte)0;

        Span<byte> nonce = stackalloc byte[12];
        header.Slice(88, 4).CopyTo(nonce);
        BinaryPrimitives.WriteUInt64BigEndian(nonce[4..], ordinal);

        Span<byte> aad = stackalloc byte[HeaderSize + DescriptorSize];
        header.CopyTo(aad);
        descriptor.CopyTo(aad[HeaderSize..]);

        byte[] ciphertext = new byte[plaintext.Length];
        try
        {
            Span<byte> tag = stackalloc byte[TagSize];
            aes.Encrypt(nonce, plaintext, ciphertext, tag, aad);
            output.Write(descriptor);
            output.Write(ciphertext);
            output.Write(tag);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(ciphertext);
        }
    }

    public static byte[] Decrypt(ReadOnlySpan<byte> envelope, string passphrase)
    {
        ValidateEnvelopeSize(envelope.Length);
        ReadOnlySpan<byte> header = envelope[..HeaderSize];
        byte[] master = DeriveMasterKey(header, passphrase);
        try
        {
            byte[] key = DeriveContentKey(header, master);
            try
            {
                return DecryptWithKey(envelope, key);
            }
            finally
            {
                CryptographicOperations.ZeroMemory(key);
            }
        }
        finally
        {
            CryptographicOperations.ZeroMemory(master);
        }
    }

    public static byte[] DecryptWithKey(ReadOnlySpan<byte> envelope, ReadOnlySpan<byte> contentKey)
    {
        EnsureCryptographicHostSupport();
        ValidateEnvelopeSize(envelope.Length);
        ReadOnlySpan<byte> header = envelope[..HeaderSize];
        ValidateHeader(header);
        Require(contentKey.Length == 32);

        using AesGcm aes = new(contentKey, TagSize);
        using MemoryStream plaintext = new();
        byte[] frame = new byte[FrameSize];
        Span<byte> nonce = stackalloc byte[12];
        Span<byte> aad = stackalloc byte[HeaderSize + DescriptorSize];
        header.CopyTo(aad);
        header.Slice(88, 4).CopyTo(nonce);

        int offset = HeaderSize;
        ulong ordinal = 0;
        bool shortDataSeen = false;
        try
        {
            while (true)
            {
                Require(envelope.Length - offset >= RecordOverhead);
                ReadOnlySpan<byte> descriptor = envelope.Slice(offset, DescriptorSize);
                Require(BinaryPrimitives.ReadUInt64LittleEndian(descriptor) == ordinal);
                uint rawLength = BinaryPrimitives.ReadUInt32LittleEndian(descriptor[8..]);
                Require(rawLength <= FrameSize);
                int length = (int)rawLength;
                byte flag = descriptor[12];
                Require(flag <= 1);
                bool final = flag == 1;
                Require(final ? length == 0 : length > 0 && !shortDataSeen);
                Require(envelope.Length - offset - RecordOverhead >= length);
                Require(plaintext.Length + length <= MaxPayloadLimit);

                BinaryPrimitives.WriteUInt64BigEndian(nonce[4..], ordinal);
                descriptor.CopyTo(aad[HeaderSize..]);
                try
                {
                    aes.Decrypt(
                        nonce,
                        envelope.Slice(offset + DescriptorSize, length),
                        envelope.Slice(offset + DescriptorSize + length, TagSize),
                        frame.AsSpan(0, length),
                        aad);
                }
                catch (CryptographicException ex)
                {
                    throw new ProfileAuthenticationException("Profile envelope frame authentication failed.", ex);
                }

                offset += DescriptorSize + length + TagSize;
                if (final)
                {
                    Require(offset == envelope.Length);
                    return plaintext.ToArray();
                }

                plaintext.Write(frame, 0, length);
                CryptographicOperations.ZeroMemory(frame);
                shortDataSeen = length < FrameSize;
                ordinal++;
            }
        }
        finally
        {
            CryptographicOperations.ZeroMemory(frame);
            if (plaintext.TryGetBuffer(out ArraySegment<byte> buffer))
            {
                CryptographicOperations.ZeroMemory(buffer.AsSpan());
            }
        }
    }

    public static void ValidateEnvelopeSize(long length) =>
        Require(length >= MinEnvelopePhysicalLength && length <= MaxEnvelopePhysicalLength);

    internal static void Require(bool valid, string message = "Invalid or unsupported profile envelope.")
    {
        if (!valid)
        {
            throw new InvalidDataException(message);
        }
    }
}
