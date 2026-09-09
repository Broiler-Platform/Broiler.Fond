using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;

namespace Broiler.Fond.Kernel.Tests.Storage;

// Test-only executable specification for proposed ADR 0004. Never used by the host.
// Caller-supplied keys/nonces are for public vectors, not a production encryption API.
internal static class EnvelopeReference
{
    internal const int HeaderSize = 96;
    internal const int FrameSize = 65_536;
    internal const int DescriptorSize = 13;
    internal const int TagSize = 16;
    internal const int TestPayloadLimit = 8 * 1024 * 1024;
    internal const int MinimumIterations = 600_000;
    internal const int MaximumIterations = 5_000_000;
    private static readonly UTF8Encoding StrictUtf8 = new(false, true);

    internal static byte[] NormalizePassphrase(string passphrase)
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

    internal static void ValidateHeader(ReadOnlySpan<byte> header)
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

    internal static byte[] DeriveMasterKey(ReadOnlySpan<byte> header, string passphrase)
    {
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

    internal static byte[] DeriveContentKey(ReadOnlySpan<byte> header, ReadOnlySpan<byte> masterKey)
    {
        ValidateHeader(header);
        Require(masterKey.Length == 32);
        byte[] result = new byte[32];
        HKDF.DeriveKey(HashAlgorithmName.SHA256, masterKey, result,
            header.Slice(56, 32), "Broiler.Fond/profile-envelope/v1/content"u8);
        return result;
    }

    internal static byte[] Encrypt(ReadOnlySpan<byte> payload, ReadOnlySpan<byte> header, ReadOnlySpan<byte> key)
    {
        ValidateHeader(header);
        Require(payload.Length <= TestPayloadLimit);
        Require(key.Length == 32);
        using AesGcm aes = new(key, TagSize);
        using MemoryStream output = new();
        output.Write(header);
        ulong ordinal = 0;
        for (int offset = 0; offset < payload.Length; offset += FrameSize)
        {
            WriteRecord(output, aes, header, ordinal++, payload.Slice(offset, Math.Min(FrameSize, payload.Length - offset)), false);
        }

        WriteRecord(output, aes, header, ordinal, [], true);
        return output.ToArray();
    }

    private static void WriteRecord(Stream output, AesGcm aes, ReadOnlySpan<byte> header,
        ulong ordinal, ReadOnlySpan<byte> plaintext, bool final)
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
        Span<byte> tag = stackalloc byte[TagSize];
        aes.Encrypt(nonce, plaintext, ciphertext, tag, aad);
        output.Write(descriptor);
        output.Write(ciphertext);
        output.Write(tag);
    }

    internal static byte[] Decrypt(ReadOnlySpan<byte> envelope, string passphrase)
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

    internal static byte[] DecryptWithKey(ReadOnlySpan<byte> envelope, ReadOnlySpan<byte> key)
    {
        ValidateEnvelopeSize(envelope.Length);
        ReadOnlySpan<byte> header = envelope[..HeaderSize];
        ValidateHeader(header);
        Require(key.Length == 32);
        using AesGcm aes = new(key, TagSize);
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
                Require(envelope.Length - offset >= DescriptorSize + TagSize);
                ReadOnlySpan<byte> descriptor = envelope.Slice(offset, DescriptorSize);
                Require(BinaryPrimitives.ReadUInt64LittleEndian(descriptor) == ordinal);
                uint rawLength = BinaryPrimitives.ReadUInt32LittleEndian(descriptor[8..]);
                Require(rawLength <= FrameSize);
                int length = (int)rawLength;
                byte flag = descriptor[12];
                Require(flag <= 1);
                bool final = flag == 1;
                Require(final ? length == 0 : length > 0 && !shortDataSeen);
                Require(envelope.Length - offset - DescriptorSize - TagSize >= length);
                Require(plaintext.Length + length <= TestPayloadLimit);
                BinaryPrimitives.WriteUInt64BigEndian(nonce[4..], ordinal);
                descriptor.CopyTo(aad[HeaderSize..]);
                aes.Decrypt(nonce, envelope.Slice(offset + DescriptorSize, length),
                    envelope.Slice(offset + DescriptorSize + length, TagSize), frame.AsSpan(0, length), aad);
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

    private static void ValidateEnvelopeSize(int length) => Require(length >= HeaderSize + DescriptorSize + TagSize &&
        length <= HeaderSize + TestPayloadLimit + (DescriptorSize + TagSize) * (TestPayloadLimit / FrameSize + 1));

    private static void Require(bool valid)
    {
        if (!valid)
        {
            throw new InvalidDataException("Invalid or unsupported profile envelope.");
        }
    }
}
