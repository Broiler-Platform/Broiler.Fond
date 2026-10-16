using System.Buffers.Binary;
using System.Text;

namespace Broiler.Fond.Kernel.Storage;

/// <summary>
/// Operational limits for ZIP structure preflight checks before archive instantiation.
/// </summary>
public sealed record ZipPreflightLimits
{
    public int MaxEntries { get; init; } = 16;
    public int MaxCompressedPayload { get; init; } = 8 * 1024 * 1024; // 8 MiB default cap
    public int MaxUncompressedPerEntry { get; init; } = 1024 * 1024; // 1 MiB
    public int MaxTotalUncompressed { get; init; } = 8 * 1024 * 1024; // 8 MiB
    public int MaxExpansionRatio { get; init; } = 128; // 128:1 per ADR 0005
    public int MaxEntryNameLength { get; init; } = 128; // 128 bytes
}

/// <summary>
/// Verified entry metadata from the bounded ZIP central directory and local headers.
/// </summary>
public sealed record ZipPreflightEntry(
    string Name,
    ushort CompressionMethod,
    uint Crc32,
    uint CompressedSize,
    uint UncompressedSize,
    uint LocalHeaderOffset);

/// <summary>
/// Result of successful preflight structural validation of a ZIP payload.
/// </summary>
public sealed record ZipPreflightResult(
    IReadOnlyList<ZipPreflightEntry> Entries,
    uint CentralDirectoryOffset,
    uint CentralDirectorySize,
    long TotalCompressedSize,
    long TotalUncompressedSize);

/// <summary>
/// Enforces bounded, hostile-input ZIP preflight validation directly over raw bytes or seekable streams
/// before handing the input to System.IO.Compression.ZipArchive per ADRs 0004 and 0005.
/// </summary>
public static class ZipPreflight
{
    private const uint LocalHeaderSignature = 0x04034b50;
    private const uint CentralDirectorySignature = 0x02014b50;
    private const uint EndOfCentralDirectorySignature = 0x06054b50;
    private const uint DataDescriptorSignature = 0x08074b50;
    private const int EocdMinimumSize = 22;

    private static readonly UTF8Encoding StrictUtf8 = new(false, true);

    public static ZipPreflightResult Validate(ReadOnlySpan<byte> payload, ZipPreflightLimits? limits = null)
    {
        limits ??= new ZipPreflightLimits();

        if (payload.Length < EocdMinimumSize)
        {
            throw new InvalidDataException("ZIP payload is too short for End of Central Directory.");
        }

        if (payload.Length > limits.MaxCompressedPayload)
        {
            throw new InvalidDataException($"ZIP payload exceeds compressed maximum of {limits.MaxCompressedPayload} bytes.");
        }

        // 1. Locate and validate End of Central Directory (EOCD).
        // ADR 0005 requires no archive comment, so EOCD must be exactly at payload.Length - 22.
        int eocdOffset = payload.Length - EocdMinimumSize;
        ReadOnlySpan<byte> eocd = payload.Slice(eocdOffset, EocdMinimumSize);
        if (BinaryPrimitives.ReadUInt32LittleEndian(eocd) != EndOfCentralDirectorySignature)
        {
            throw new InvalidDataException("ZIP End of Central Directory signature not found at expected boundary (archive comment forbidden).");
        }

        ushort diskNumber = BinaryPrimitives.ReadUInt16LittleEndian(eocd[4..]);
        ushort cdStartDisk = BinaryPrimitives.ReadUInt16LittleEndian(eocd[6..]);
        ushort entriesOnThisDisk = BinaryPrimitives.ReadUInt16LittleEndian(eocd[8..]);
        ushort totalEntries = BinaryPrimitives.ReadUInt16LittleEndian(eocd[10..]);
        uint cdSize = BinaryPrimitives.ReadUInt32LittleEndian(eocd[12..]);
        uint cdOffset = BinaryPrimitives.ReadUInt32LittleEndian(eocd[16..]);
        ushort commentLength = BinaryPrimitives.ReadUInt16LittleEndian(eocd[20..]);

        if (diskNumber != 0 || cdStartDisk != 0)
        {
            throw new InvalidDataException("Multi-disk ZIP archives are forbidden.");
        }

        if (entriesOnThisDisk != totalEntries)
        {
            throw new InvalidDataException("Entry count mismatch across disks in EOCD.");
        }

        if (commentLength != 0)
        {
            throw new InvalidDataException("ZIP archive comments are forbidden.");
        }

        if (totalEntries == 0)
        {
            throw new InvalidDataException("ZIP archive contains zero entries.");
        }

        if (totalEntries > limits.MaxEntries)
        {
            throw new InvalidDataException($"ZIP entry count ({totalEntries}) exceeds limit of {limits.MaxEntries}.");
        }

        if ((long)cdOffset + cdSize != eocdOffset)
        {
            throw new InvalidDataException("Central directory and EOCD must be contiguous with no gaps or trailing bytes.");
        }

        // 2. Parse Central Directory headers
        List<ZipPreflightEntry> entries = new(totalEntries);
        HashSet<string> entryNames = new(totalEntries, StringComparer.Ordinal);
        int cdCursor = (int)cdOffset;
        long totalCompressed = 0;
        long totalUncompressed = 0;

        for (int i = 0; i < totalEntries; i++)
        {
            if (cdCursor + 46 > eocdOffset)
            {
                throw new InvalidDataException($"Central directory truncated at entry {i}.");
            }

            ReadOnlySpan<byte> cdHeader = payload.Slice(cdCursor);
            if (BinaryPrimitives.ReadUInt32LittleEndian(cdHeader) != CentralDirectorySignature)
            {
                throw new InvalidDataException($"Invalid Central Directory signature at entry {i}.");
            }

            ushort versionNeeded = BinaryPrimitives.ReadUInt16LittleEndian(cdHeader[6..]);
            if (versionNeeded > 20)
            {
                throw new InvalidDataException($"Unsupported ZIP version needed to extract: {versionNeeded} (ZIP64 forbidden).");
            }

            ushort flags = BinaryPrimitives.ReadUInt16LittleEndian(cdHeader[8..]);
            if ((flags & 0x0001) != 0)
            {
                throw new InvalidDataException("Encrypted ZIP entries are forbidden.");
            }

            // Only bit 3 (data descriptor) and bit 11 (UTF-8) are allowed
            if ((flags & ~0x0808) != 0)
            {
                throw new InvalidDataException($"Unsupported ZIP general purpose flags: 0x{flags:X4}.");
            }

            ushort method = BinaryPrimitives.ReadUInt16LittleEndian(cdHeader[10..]);
            if (method != 0 && method != 8)
            {
                throw new InvalidDataException($"Unsupported compression method {method}; only 0 (Stored) and 8 (Deflate) are allowed.");
            }

            uint crc32 = BinaryPrimitives.ReadUInt32LittleEndian(cdHeader[16..]);
            uint compressedSize = BinaryPrimitives.ReadUInt32LittleEndian(cdHeader[20..]);
            uint uncompressedSize = BinaryPrimitives.ReadUInt32LittleEndian(cdHeader[24..]);
            ushort nameLength = BinaryPrimitives.ReadUInt16LittleEndian(cdHeader[28..]);
            ushort extraLength = BinaryPrimitives.ReadUInt16LittleEndian(cdHeader[30..]);
            ushort entryCommentLength = BinaryPrimitives.ReadUInt16LittleEndian(cdHeader[32..]);
            ushort diskStart = BinaryPrimitives.ReadUInt16LittleEndian(cdHeader[34..]);
            uint localOffset = BinaryPrimitives.ReadUInt32LittleEndian(cdHeader[42..]);

            if (diskStart != 0)
            {
                throw new InvalidDataException("Entry start disk must be 0.");
            }

            if (extraLength != 0)
            {
                throw new InvalidDataException("ZIP entry extra fields are forbidden.");
            }

            if (entryCommentLength != 0)
            {
                throw new InvalidDataException("ZIP entry comments are forbidden.");
            }

            if (nameLength is 0 || nameLength > limits.MaxEntryNameLength)
            {
                throw new InvalidDataException($"Entry name length {nameLength} outside bounds (1..{limits.MaxEntryNameLength}).");
            }

            if (cdCursor + 46 + nameLength > eocdOffset)
            {
                throw new InvalidDataException("Entry name extends beyond central directory boundary.");
            }

            ReadOnlySpan<byte> nameBytes = payload.Slice(cdCursor + 46, nameLength);
            string name;
            try
            {
                name = StrictUtf8.GetString(nameBytes);
            }
            catch (Exception ex)
            {
                throw new InvalidDataException("Entry name is not valid UTF-8.", ex);
            }

            if (name.Contains('\\') || name.Contains('/'))
            {
                throw new InvalidDataException($"Entry name '{name}' contains path separators; directories are forbidden.");
            }

            if (name.Contains("..", StringComparison.Ordinal))
            {
                throw new InvalidDataException($"Entry name '{name}' contains path traversal.");
            }

            if (!entryNames.Add(name))
            {
                throw new InvalidDataException($"Duplicate entry name '{name}' in ZIP archive.");
            }

            if (uncompressedSize > limits.MaxUncompressedPerEntry)
            {
                throw new InvalidDataException($"Entry '{name}' uncompressed size {uncompressedSize} exceeds per-entry limit of {limits.MaxUncompressedPerEntry}.");
            }

            // Expansion ratio check against max(1, compressedSize)
            ulong maxAllowedUncompressed = Math.Max(1UL, (ulong)compressedSize) * (ulong)limits.MaxExpansionRatio;
            if ((ulong)uncompressedSize > maxAllowedUncompressed)
            {
                throw new InvalidDataException($"Entry '{name}' expansion ratio exceeds {limits.MaxExpansionRatio}:1 (possible compression bomb).");
            }

            if (method == 0 && compressedSize != uncompressedSize)
            {
                throw new InvalidDataException($"Stored entry '{name}' compressed and uncompressed sizes must match.");
            }

            totalCompressed += compressedSize;
            totalUncompressed += uncompressedSize;
            if (totalUncompressed > limits.MaxTotalUncompressed)
            {
                throw new InvalidDataException($"Total uncompressed archive size {totalUncompressed} exceeds limit of {limits.MaxTotalUncompressed}.");
            }

            entries.Add(new ZipPreflightEntry(name, method, crc32, compressedSize, uncompressedSize, localOffset));
            cdCursor += 46 + nameLength;
        }

        if (cdCursor != eocdOffset)
        {
            throw new InvalidDataException("Central directory size mismatch with parsed headers.");
        }

        // 3. Validate Local File Headers and strict sequential contiguity
        int localCursor = 0;
        for (int i = 0; i < entries.Count; i++)
        {
            ZipPreflightEntry expected = entries[i];
            if ((uint)localCursor != expected.LocalHeaderOffset)
            {
                throw new InvalidDataException($"Local header offset gap or overlap at entry {i} '{expected.Name}': expected {expected.LocalHeaderOffset}, actual {localCursor}.");
            }

            if (localCursor + 30 > (int)cdOffset)
            {
                throw new InvalidDataException($"Local header for entry {i} extends into Central Directory.");
            }

            ReadOnlySpan<byte> localHeader = payload.Slice(localCursor);
            if (BinaryPrimitives.ReadUInt32LittleEndian(localHeader) != LocalHeaderSignature)
            {
                throw new InvalidDataException($"Invalid Local File Header signature at offset {localCursor} for '{expected.Name}'.");
            }

            ushort localMethod = BinaryPrimitives.ReadUInt16LittleEndian(localHeader[8..]);
            if (localMethod != expected.CompressionMethod)
            {
                throw new InvalidDataException($"Compression method mismatch between local and central header for '{expected.Name}'.");
            }

            ushort localFlags = BinaryPrimitives.ReadUInt16LittleEndian(localHeader[6..]);
            ushort localNameLen = BinaryPrimitives.ReadUInt16LittleEndian(localHeader[26..]);
            ushort localExtraLen = BinaryPrimitives.ReadUInt16LittleEndian(localHeader[28..]);

            if (localExtraLen != 0)
            {
                throw new InvalidDataException($"Local header extra fields forbidden for '{expected.Name}'.");
            }

            if (localCursor + 30 + localNameLen > (int)cdOffset)
            {
                throw new InvalidDataException($"Local entry name extends beyond local header for '{expected.Name}'.");
            }

            ReadOnlySpan<byte> localNameBytes = payload.Slice(localCursor + 30, localNameLen);
            string localName = StrictUtf8.GetString(localNameBytes);
            if (!string.Equals(localName, expected.Name, StringComparison.Ordinal))
            {
                throw new InvalidDataException($"Local entry name '{localName}' does not match central directory name '{expected.Name}'.");
            }

            bool hasDataDescriptor = (localFlags & 0x0008) != 0;
            if (!hasDataDescriptor)
            {
                uint localCrc = BinaryPrimitives.ReadUInt16LittleEndian(localHeader[14..]) | ((uint)BinaryPrimitives.ReadUInt16LittleEndian(localHeader[16..]) << 16);
                uint localComp = BinaryPrimitives.ReadUInt32LittleEndian(localHeader[18..]);
                uint localUncomp = BinaryPrimitives.ReadUInt32LittleEndian(localHeader[22..]);

                if (localCrc != expected.Crc32 || localComp != expected.CompressedSize || localUncomp != expected.UncompressedSize)
                {
                    throw new InvalidDataException($"Local and central directory size/CRC metadata mismatch for '{expected.Name}'.");
                }
            }

            int dataStart = localCursor + 30 + localNameLen;
            if (dataStart + (int)expected.CompressedSize > (int)cdOffset)
            {
                throw new InvalidDataException($"Compressed data for '{expected.Name}' extends into Central Directory.");
            }

            localCursor = dataStart + (int)expected.CompressedSize;

            if (hasDataDescriptor)
            {
                if (localCursor + 12 > (int)cdOffset)
                {
                    throw new InvalidDataException($"Data descriptor truncated for '{expected.Name}'.");
                }

                uint descSigOrCrc = BinaryPrimitives.ReadUInt32LittleEndian(payload.Slice(localCursor, 4));
                uint descCrc, descComp, descUncomp;
                int descSize;

                if (descSigOrCrc == DataDescriptorSignature)
                {
                    if (localCursor + 16 > (int)cdOffset)
                    {
                        throw new InvalidDataException($"16-byte data descriptor truncated for '{expected.Name}'.");
                    }

                    descCrc = BinaryPrimitives.ReadUInt32LittleEndian(payload.Slice(localCursor + 4, 4));
                    descComp = BinaryPrimitives.ReadUInt32LittleEndian(payload.Slice(localCursor + 8, 4));
                    descUncomp = BinaryPrimitives.ReadUInt32LittleEndian(payload.Slice(localCursor + 12, 4));
                    descSize = 16;
                }
                else
                {
                    descCrc = descSigOrCrc;
                    descComp = BinaryPrimitives.ReadUInt32LittleEndian(payload.Slice(localCursor + 4, 4));
                    descUncomp = BinaryPrimitives.ReadUInt32LittleEndian(payload.Slice(localCursor + 8, 4));
                    descSize = 12;
                }

                if (descCrc != expected.Crc32 || descComp != expected.CompressedSize || descUncomp != expected.UncompressedSize)
                {
                    throw new InvalidDataException($"Data descriptor metadata mismatch for '{expected.Name}'.");
                }

                localCursor += descSize;
            }
        }

        // Final contiguity invariant: the last local record must end exactly at cdOffset
        if ((uint)localCursor != cdOffset)
        {
            throw new InvalidDataException($"Gap or hidden bytes between last local file entry and start of central directory (at {localCursor}, CD at {cdOffset}).");
        }

        return new ZipPreflightResult(entries, cdOffset, cdSize, totalCompressed, totalUncompressed);
    }

    public static ZipPreflightResult ValidateStream(Stream stream, ZipPreflightLimits? limits = null)
    {
        ArgumentNullException.ThrowIfNull(stream);
        if (!stream.CanRead || !stream.CanSeek)
        {
            throw new ArgumentException("Stream must be readable and seekable.", nameof(stream));
        }

        limits ??= new ZipPreflightLimits();
        long length = stream.Length;
        if (length < EocdMinimumSize)
        {
            throw new InvalidDataException("ZIP payload is too short for End of Central Directory.");
        }

        if (length > limits.MaxCompressedPayload)
        {
            throw new InvalidDataException($"ZIP payload exceeds compressed maximum of {limits.MaxCompressedPayload} bytes.");
        }

        byte[] buffer = new byte[(int)length];
        stream.Position = 0;
        stream.ReadExactly(buffer);
        stream.Position = 0;
        return Validate(buffer, limits);
    }
}
