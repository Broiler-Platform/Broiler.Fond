using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace Broiler.Fond.Kernel.FinTs;

/// <summary>Encodes a restricted PIN-only closing envelope after synchronization. No sending or closure acknowledgement.
/// Successful output contains plaintext PIN bytes and must be cleared by its caller.</summary>
public static class FinTsPinTanDialogueEndWriter
{
    public const int MaximumEncodedLength = 2048;
    private const int MaximumPayloadLength = 1024;

    public static FinTsPinTanRequestWriteResult TryEncode(FinTsPinTanDialogueEndContext context, FinTsSessionCredential pin,
        Span<byte> destination, out int bytesWritten, CancellationToken cancellationToken = default)
    {
        bytesWritten = 0; ArgumentNullException.ThrowIfNull(context); ArgumentNullException.ThrowIfNull(pin);
        Span<byte> payload = stackalloc byte[MaximumPayloadLength];
        Span<byte> wire = stackalloc byte[MaximumEncodedLength]; payload.Clear(); wire.Clear();
        int copied = 0; bool success = false;
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!context.HasMatchingEvidence) { return FinTsPinTanRequestWriteResult.ContextNeedsReview; }
            if (destination.Length < MaximumEncodedLength) { return FinTsPinTanRequestWriteResult.DestinationTooSmall; }
            string dialogue = FinTsUnsignedWireEncoding.Text(context.Request.Request.DialogueId, 30, FinTsSyntaxError.InvalidDialogueEnd);
            string framing = "HNHBK:1:3+000000000000+300+" + dialogue + "+2'";
            string prefix = FinTsPinTanRequestEnvelopeWriter.PublicPrefix(context.Header, framing);
            var publicBody = new StringBuilder();
            FinTsPinTanRequestWriter.AppendPublicSegment(publicBody, context.Header.Source, 2);
            publicBody.Append("HKEND:3:1+").Append(dialogue).Append('\'');
            const string ending = "HNHBS:5:1+2'";
            if (publicBody.Length + FinTsPinTanSignatureTrailerWriter.MaximumEncodedLength > MaximumPayloadLength ||
                prefix.Length + 6 + MaximumPayloadLength + ending.Length > MaximumEncodedLength)
            { return FinTsPinTanRequestWriteResult.ContextNeedsReview; }
            int length = Encoding.Latin1.GetBytes(publicBody.ToString(), payload);
            var result = FinTsPinTanSignatureTrailerWriter.TryEncodeClosing(context, pin,
                payload.Slice(length, FinTsPinTanSignatureTrailerWriter.MaximumEncodedLength), out int trailerLength, cancellationToken);
            if (result != FinTsSignatureTrailerWriteResult.Written)
            {
                return result switch
                {
                    FinTsSignatureTrailerWriteResult.ContextNeedsReview => FinTsPinTanRequestWriteResult.ContextNeedsReview,
                    FinTsSignatureTrailerWriteResult.CredentialUnavailable => FinTsPinTanRequestWriteResult.CredentialUnavailable,
                    FinTsSignatureTrailerWriteResult.InvalidCredentialText => FinTsPinTanRequestWriteResult.InvalidCredentialText,
                    _ => throw new InvalidOperationException("Unexpected closing trailer result."),
                };
            }
            length += trailerLength;
            int position = Encoding.Latin1.GetBytes(prefix, wire);
            position += Encoding.Latin1.GetBytes(length.ToString(CultureInfo.InvariantCulture), wire[position..]);
            wire[position++] = (byte)'@'; payload[..length].CopyTo(wire[position..]); position += length;
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
        catch (OperationCanceledException) { pin.Cancel(); throw; }
        finally
        {
            if (!success && copied > 0) { CryptographicOperations.ZeroMemory(destination[..copied]); }
            CryptographicOperations.ZeroMemory(payload); CryptographicOperations.ZeroMemory(wire);
        }
    }
}
