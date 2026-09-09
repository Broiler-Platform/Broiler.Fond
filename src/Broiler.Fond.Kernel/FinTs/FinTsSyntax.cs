using System.Collections.ObjectModel;
using System.Globalization;
using System.Text;

namespace Broiler.Fond.Kernel.FinTs;

public enum FinTsSyntaxError { LimitExceeded, IncompleteInput, InvalidEscape, InvalidBinary, InvalidHeader, InvalidFrame, InvalidResponse, UnsupportedResponseVersion, UnsupportedSecurityWrapper, InvalidParameters, UnsupportedParameterVersion, UnsupportedParameterLayout, InvalidSecurityEnvelope, InvalidTanChallenge, InvalidTanContext, InvalidReadData, UnsupportedReadDataVersion, InvalidInitialization, UnsupportedInitializationVersion, InvalidSynchronization, UnsupportedSynchronizationVersion, InvalidDialogueEnd, UnsupportedDialogueEndVersion, InvalidSignatureHeader, UnsupportedSignatureHeader, InvalidSignatureContext }

/// <summary>Fixed diagnostics only; never includes input bytes or decoded bank text.</summary>
public sealed class FinTsFormatException : FormatException
{
    internal FinTsFormatException(FinTsSyntaxError error) : base($"Invalid FinTS syntax: {error}.") => Error = error;
    public FinTsSyntaxError Error { get; }
}

/// <summary>An untrusted scalar. Bytes are copied out explicitly and must never be logged.</summary>
public sealed class FinTsDataElement
{
    private readonly byte[] _wire;
    private readonly int _offset;
    private readonly int _length;

    internal FinTsDataElement(byte[] wire, int offset, int length, bool binary)
    {
        _wire = wire;
        _offset = offset;
        _length = length;
        IsBinary = binary;
    }

    public bool IsBinary { get; }
    public bool IsEmpty => _length == 0;

    public byte[] CopyValueBytes()
    {
        ReadOnlySpan<byte> source = _wire.AsSpan(_offset, _length);
        if (IsBinary)
        {
            return source.ToArray();
        }

        byte[] result = new byte[_length];
        int written = 0;
        for (int index = 0; index < source.Length; index++)
        {
            if (source[index] == '?') { index++; }
            result[written++] = source[index];
        }

        return result.AsSpan(0, written).ToArray();
    }

    internal string HeaderText()
    {
        if (IsBinary) { throw new FinTsFormatException(FinTsSyntaxError.InvalidHeader); }
        return Encoding.Latin1.GetString(CopyValueBytes());
    }
}

/// <summary>A positional DE/DEG; empty and trailing components are preserved.</summary>
public sealed class FinTsField
{
    internal FinTsField(List<FinTsDataElement> elements) => Elements = elements.AsReadOnly();
    public ReadOnlyCollection<FinTsDataElement> Elements { get; }
}

/// <summary>Unknown codes and versions remain untrusted syntax, never supported operations.</summary>
public sealed class FinTsSegment
{
    internal FinTsSegment(string code, int number, int version, int? reference, List<FinTsField> fields)
    {
        Code = code;
        Number = number;
        Version = version;
        Reference = reference;
        Fields = fields.AsReadOnly();
    }

    public string Code { get; }
    public int Number { get; }
    public int Version { get; }
    public int? Reference { get; }
    /// <summary>Fields after the segment header, in wire order.</summary>
    public ReadOnlyCollection<FinTsField> Fields { get; }
}

public sealed class FinTsSyntaxDocument
{
    private readonly byte[] _wire;
    internal FinTsSyntaxDocument(byte[] wire, List<FinTsSegment> segments, int elementCount)
    {
        _wire = wire;
        Segments = segments.AsReadOnly();
        ElementCount = elementCount;
    }

    public ReadOnlyCollection<FinTsSegment> Segments { get; }
    public int ByteLength => _wire.Length;
    internal int ElementCount { get; }
    /// <summary>Exact original representation, including omissions and binary length spelling.</summary>
    public byte[] CopyWireBytes() => (byte[])_wire.Clone();
}

/// <summary>
/// Bounded byte syntax only. No transport, charset negotiation, business schema,
/// signature verification, decryption, credential handling or bank authentication.
/// </summary>
public static class FinTsSyntax
{
    public const int MaximumWireBytes = 1_048_576;
    public const int MaximumSegments = 999;
    public const int MaximumFieldsPerSegment = 1024;
    public const int MaximumComponentsPerField = 256;
    public const int MaximumElements = 32_768;

    public static FinTsSyntaxDocument ParseSegments(ReadOnlySpan<byte> input, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (input.Length > MaximumWireBytes) { throw Error(FinTsSyntaxError.LimitExceeded); }
        if (input.IsEmpty) { throw Error(FinTsSyntaxError.IncompleteInput); }
        // One owned snapshot prevents mutations of caller buffers from changing parsed evidence.
        byte[] wire = input.ToArray();
        List<FinTsSegment> segments = [];
        int position = 0;
        int elementCount = 0;
        while (position < wire.Length)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (segments.Count == MaximumSegments) { throw Error(FinTsSyntaxError.LimitExceeded); }
            List<FinTsField> fields = [];
            bool ended = false;
            while (!ended)
            {
                // Includes the segment-header DEG in the field bound.
                if (fields.Count == MaximumFieldsPerSegment) { throw Error(FinTsSyntaxError.LimitExceeded); }
                List<FinTsDataElement> elements = [];
                byte separator;
                do
                {
                    if (elements.Count == MaximumComponentsPerField || elementCount == MaximumElements)
                    {
                        throw Error(FinTsSyntaxError.LimitExceeded);
                    }

                    elementCount++;
                    elements.Add(ReadElement(wire, ref position, cancellationToken));
                    if (position == wire.Length) { throw Error(FinTsSyntaxError.IncompleteInput); }
                    separator = wire[position++];
                } while (separator == ':');
                fields.Add(new(elements));
                ended = separator == '\'';
            }

            ReadOnlyCollection<FinTsDataElement> header = fields[0].Elements;
            if (header.Count is < 3 or > 4) { throw Error(FinTsSyntaxError.InvalidHeader); }
            string code = header[0].HeaderText();
            if (code.Length is < 1 or > 6 || code.Any(c => c is not (>= 'A' and <= 'Z') and not (>= '0' and <= '9')))
            {
                throw Error(FinTsSyntaxError.InvalidHeader);
            }

            int number = Number(header[1], 3, allowZero: false);
            int version = Number(header[2], 3, allowZero: true);
            int? reference = header.Count == 4 && !header[3].IsEmpty ? Number(header[3], 3, allowZero: false) : null;
            if (header.Count == 4 && header[3].IsBinary) { throw Error(FinTsSyntaxError.InvalidHeader); }
            fields.RemoveAt(0);
            segments.Add(new(code, number, version, reference, fields));
        }

        return new(wire, segments, elementCount);
    }

    private static FinTsDataElement ReadElement(byte[] wire, ref int position, CancellationToken token)
    {
        int start = position;
        if (position < wire.Length && wire[position] == '@')
        {
            position++;
            int digits = 0;
            int length = 0;
            while (position < wire.Length && wire[position] is >= (byte)'0' and <= (byte)'9')
            {
                if (++digits > 9) { throw Error(FinTsSyntaxError.InvalidBinary); }
                length = length * 10 + wire[position++] - '0';
                if (length > MaximumWireBytes) { throw Error(FinTsSyntaxError.LimitExceeded); }
            }

            if (digits == 0 || position == wire.Length || wire[position++] != '@') { throw Error(FinTsSyntaxError.InvalidBinary); }
            if (length > wire.Length - position) { throw Error(FinTsSyntaxError.IncompleteInput); }
            start = position;
            position += length;
            if (position < wire.Length && !IsSeparator(wire[position])) { throw Error(FinTsSyntaxError.InvalidBinary); }
            return new(wire, start, length, binary: true);
        }

        while (position < wire.Length && !IsSeparator(wire[position]))
        {
            if ((position & 1023) == 0) { token.ThrowIfCancellationRequested(); }
            byte value = wire[position++];
            if (value == '@') { throw Error(FinTsSyntaxError.InvalidBinary); }
            if (value == '?')
            {
                if (position == wire.Length || !IsSyntax(wire[position++])) { throw Error(FinTsSyntaxError.InvalidEscape); }
            }
        }

        return new(wire, start, position - start, binary: false);
    }

    internal static int Number(FinTsDataElement element, int maximumDigits, bool allowZero)
    {
        string value = element.HeaderText();
        if (value.Length < 1 || value.Length > maximumDigits || value.Any(c => c is < '0' or > '9') ||
            value.Length > 1 && value[0] == '0') { throw Error(FinTsSyntaxError.InvalidHeader); }
        int number = int.Parse(value, CultureInfo.InvariantCulture);
        if (!allowZero && number == 0) { throw Error(FinTsSyntaxError.InvalidHeader); }
        return number;
    }

    internal static bool IsSyntax(byte value) => IsSeparator(value) || value is (byte)'?' or (byte)'@';
    private static bool IsSeparator(byte value) => value is (byte)'+' or (byte)':' or (byte)'\'';
    private static FinTsFormatException Error(FinTsSyntaxError error) => new(error);
}

/// <summary>Lexical element encoding; accepts explicit bytes without guessing a text encoding.</summary>
public static class FinTsWireEncoding
{
    public static byte[] EncodeText(ReadOnlySpan<byte> value)
    {
        if (value.Length > FinTsSyntax.MaximumWireBytes) { throw new FinTsFormatException(FinTsSyntaxError.LimitExceeded); }
        int length = value.Length;
        foreach (byte item in value) { if (FinTsSyntax.IsSyntax(item)) { length++; } }
        if (length > FinTsSyntax.MaximumWireBytes) { throw new FinTsFormatException(FinTsSyntaxError.LimitExceeded); }
        byte[] output = new byte[length];
        int position = 0;
        foreach (byte item in value)
        {
            if (FinTsSyntax.IsSyntax(item)) { output[position++] = (byte)'?'; }
            output[position++] = item;
        }

        return output;
    }

    public static byte[] EncodeBinary(ReadOnlySpan<byte> value)
    {
        if (value.Length > FinTsSyntax.MaximumWireBytes) { throw new FinTsFormatException(FinTsSyntaxError.LimitExceeded); }
        byte[] prefix = Encoding.ASCII.GetBytes($"@{value.Length.ToString(CultureInfo.InvariantCulture)}@");
        if (prefix.Length + value.Length > FinTsSyntax.MaximumWireBytes) { throw new FinTsFormatException(FinTsSyntaxError.LimitExceeded); }
        byte[] output = new byte[prefix.Length + value.Length];
        prefix.CopyTo(output, 0);
        value.CopyTo(output.AsSpan(prefix.Length));
        return output;
    }
}
