using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace Broiler.Fond.Kernel.FinTs;

/// <summary>Plaintext PIN/TAN envelope for a bound first-read PIN-only candidate. No encryption, sending, TAN omission decision or SCA readiness.
/// Successful output contains a PIN and must be cleared by its caller.</summary>
public static class FinTsPinTanReadRequestEnvelopeWriter
{
    public const int MaximumEncodedLength = 3072;

    public static FinTsPinTanRequestWriteResult TryEncodePinOnly(FinTsPinTanReadRequestBinding candidate, FinTsSessionCredential pin,
        Span<byte> destination, out int bytesWritten, CancellationToken cancellationToken = default)
    {
        bytesWritten = 0; ArgumentNullException.ThrowIfNull(candidate); ArgumentNullException.ThrowIfNull(pin);
        Span<byte> plain = stackalloc byte[FinTsPinTanReadRequestWriter.MaximumEncodedLength];
        Span<byte> wire = stackalloc byte[MaximumEncodedLength]; plain.Clear(); wire.Clear();
        int copied = 0; bool success = false;
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (destination.Length < MaximumEncodedLength) { return FinTsPinTanRequestWriteResult.DestinationTooSmall; }
            string dialogue = FinTsUnsignedWireEncoding.Text(candidate.DialogueId, 30, FinTsSyntaxError.InvalidSignatureContext);
            string number = candidate.MessageNumber.ToString(CultureInfo.InvariantCulture);
            string framing = "HNHBK:1:3+000000000000+300+" + dialogue + "+" + number + "'";
            string ending = "HNHBS:5:1+" + number + "'";
            string prefix = FinTsPinTanRequestEnvelopeWriter.PublicPrefix(candidate.Context.Signature.Header, framing);
            if (prefix.Length + 6 + FinTsPinTanReadRequestWriter.MaximumEncodedLength + ending.Length > MaximumEncodedLength)
            { return FinTsPinTanRequestWriteResult.ContextNeedsReview; }
            var result = FinTsPinTanReadRequestWriter.TryEncodePinOnly(candidate.Context, pin, plain, out int plainLength, cancellationToken);
            if (result != FinTsPinTanRequestWriteResult.Written) { return result; }

            // Strip only our own assembler's canonical public framing. Never accept or parse caller-supplied secret-bearing wire.
            int bodyLength = plainLength - framing.Length - ending.Length;
            if (bodyLength <= 0 || !plain[..10].SequenceEqual("HNHBK:1:3+"u8) ||
                !plain.Slice(22, framing.Length - 22).SequenceEqual(Encoding.Latin1.GetBytes(framing[22..])) ||
                !plain.Slice(plainLength - ending.Length, ending.Length).SequenceEqual(Encoding.Latin1.GetBytes(ending)))
            { return FinTsPinTanRequestWriteResult.ContextNeedsReview; }
            int position = Encoding.Latin1.GetBytes(prefix, wire);
            position += Encoding.Latin1.GetBytes(bodyLength.ToString(CultureInfo.InvariantCulture), wire[position..]);
            wire[position++] = (byte)'@';
            plain.Slice(framing.Length, bodyLength).CopyTo(wire[position..]); position += bodyLength;
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
            CryptographicOperations.ZeroMemory(plain); CryptographicOperations.ZeroMemory(wire);
        }
    }
}
