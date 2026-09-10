using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace Broiler.Fond.Kernel.FinTs;

/// <summary>Wraps a restricted PIN/TAN initialization/synchronization candidate in a plaintext security envelope.
/// This performs no encryption, authentication or sending. Successful caller output contains credentials and must be cleared.</summary>
public static class FinTsPinTanRequestEnvelopeWriter
{
    public const int MaximumEncodedLength = 3072;
    private const string MessageHeader = "HNHBK:1:3+000000000000+300+0+1'";

    /// <summary>Requires the full reserve before credential access. Failure reports zero bytes and erases any published prefix.
    /// Cancellation cancels supplied owners; downstream failure never restores an already consumed TAN.</summary>
    public static FinTsPinTanRequestWriteResult TryEncode(FinTsPinTanSignatureEvidence context, FinTsSessionCredential pin,
        FinTsSessionCredential? tan, Span<byte> destination, out int bytesWritten, CancellationToken cancellationToken = default)
    {
        bytesWritten = 0;
        ArgumentNullException.ThrowIfNull(context); ArgumentNullException.ThrowIfNull(pin);
        Span<byte> plain = stackalloc byte[FinTsPinTanRequestWriter.MaximumEncodedLength];
        Span<byte> wire = stackalloc byte[MaximumEncodedLength]; plain.Clear(); wire.Clear();
        int copied = 0; bool success = false;
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!context.HasMatchingEvidence) { return FinTsPinTanRequestWriteResult.ContextNeedsReview; }
            if (destination.Length < MaximumEncodedLength) { return FinTsPinTanRequestWriteResult.DestinationTooSmall; }
            string prefix = PublicPrefix(context.Header);
            string ending = "HNHBS:" + (context.Request.Frame.Syntax.Segments.Count + 2).ToString(CultureInfo.InvariantCulture) + ":1+1'";
            // Reserve four binary-length digits, closing @ and payload terminator before touching credentials.
            if (prefix.Length + 6 + FinTsPinTanRequestWriter.MaximumEncodedLength + ending.Length > MaximumEncodedLength)
            { return FinTsPinTanRequestWriteResult.ContextNeedsReview; }
            var result = FinTsPinTanRequestWriter.TryEncode(context, pin, tan, plain, out int plainLength, cancellationToken);
            if (result != FinTsPinTanRequestWriteResult.Written) { return result; }

            // The only source is our restricted assembler, never caller wire. Strip its canonical outer framing by known lengths.
            int bodyLength = plainLength - MessageHeader.Length - ending.Length;
            if (bodyLength <= 0 || !plain[..10].SequenceEqual("HNHBK:1:3+"u8) ||
                !plain.Slice(22, MessageHeader.Length - 22).SequenceEqual("+300+0+1'"u8) ||
                !plain.Slice(plainLength - ending.Length, ending.Length).SequenceEqual(Encoding.Latin1.GetBytes(ending)))
            { return FinTsPinTanRequestWriteResult.ContextNeedsReview; }
            int position = Encoding.Latin1.GetBytes(prefix, wire);
            position += Encoding.Latin1.GetBytes(bodyLength.ToString(CultureInfo.InvariantCulture), wire[position..]);
            wire[position++] = (byte)'@';
            plain.Slice(MessageHeader.Length, bodyLength).CopyTo(wire[position..]); position += bodyLength;
            wire[position++] = (byte)'\'';
            position += Encoding.Latin1.GetBytes(ending, wire[position..]);
            int remaining = position;
            for (int i = 21; i >= 10; i--) { wire[i] = (byte)('0' + remaining % 10); remaining /= 10; }
            cancellationToken.ThrowIfCancellationRequested();
            if (pin.GetSnapshot().State != FinTsCredentialState.Available) { return FinTsPinTanRequestWriteResult.CredentialUnavailable; }
            cancellationToken.ThrowIfCancellationRequested();
            wire[..position].CopyTo(destination); copied = position;
            if (pin.GetSnapshot().State != FinTsCredentialState.Available) { return FinTsPinTanRequestWriteResult.CredentialUnavailable; }
            cancellationToken.ThrowIfCancellationRequested();
            bytesWritten = position; success = true;
            return FinTsPinTanRequestWriteResult.Written;
        }
        catch (OperationCanceledException)
        {
            pin.Cancel(); tan?.Cancel(); throw;
        }
        finally
        {
            if (!success && copied > 0) { CryptographicOperations.ZeroMemory(destination[..copied]); }
            CryptographicOperations.ZeroMemory(plain); CryptographicOperations.ZeroMemory(wire);
        }
    }

    internal static string PublicPrefix(FinTsPinTanSignatureHeader header, string messageHeader = MessageHeader)
    {
        string Text(string value) => FinTsUnsignedWireEncoding.Text(value, 30, FinTsSyntaxError.InvalidSignatureContext);
        var output = new StringBuilder(messageHeader).Append("HNVSK:998:3+PIN:")
            .Append(header.ProfileVersion.ToString(CultureInfo.InvariantCulture)).Append("+998+1+1::").Append(Text(header.SystemId)).Append("+1");
        // Reuse explicit caller-supplied signature timestamp metadata; never sample a wall clock or assert freshness.
        if (header.SecurityDate is { } date)
        {
            output.Append(':').Append(date.ToString("yyyyMMdd", CultureInfo.InvariantCulture));
            if (header.SecurityTime is { } time) { output.Append(':').Append(time.ToString("HHmmss", CultureInfo.InvariantCulture)); }
        }
        // PIN/TAN protocol fillers only: usage/mode/algorithm, eight zero octets, key parameter 5, IV identifier 1.
        output.Append("+2:2:13:@8@").Append('\0', 8).Append(":5:1+").Append(Text(header.CountryCode)).Append(':')
            .Append(Text(header.InstitutionId)).Append(':').Append(Text(header.UserId)).Append(":V:0:0+0'")
            .Append("HNVSD:999:1+@");
        return output.ToString();
    }
}
