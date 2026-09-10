using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace Broiler.Fond.Kernel.FinTs;

/// <summary>Assembles a bounded plain first-read candidate with a PIN-only security component, before envelope encoding.
/// Successful output contains a PIN and must be cleared. This does not establish TAN omission, SCA readiness or permission to send.</summary>
public static class FinTsPinTanReadRequestWriter
{
    public const int MaximumEncodedLength = 2048;

    /// <summary>Requires the full reserve before credential access. Checks actual PIN bytes through the read trailer writer.
    /// Failed publication zeroes the copied message prefix; cancellation clears the PIN owner and all temporary secret staging.</summary>
    public static FinTsPinTanRequestWriteResult TryEncodePinOnly(FinTsPinTanReadCapabilityContext context, FinTsSessionCredential pin,
        Span<byte> destination, out int bytesWritten, CancellationToken cancellationToken = default)
    {
        bytesWritten = 0; ArgumentNullException.ThrowIfNull(context); ArgumentNullException.ThrowIfNull(pin);
        Span<byte> wire = stackalloc byte[MaximumEncodedLength]; wire.Clear();
        int copied = 0; bool success = false;
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!context.HasMatchingEvidence) { return FinTsPinTanRequestWriteResult.ContextNeedsReview; }
            if (destination.Length < MaximumEncodedLength) { return FinTsPinTanRequestWriteResult.DestinationTooSmall; }
            var signature = context.Signature;
            string dialogue = FinTsUnsignedWireEncoding.Text(FinTsReadContextEvidence.Dialog(signature.Request.Frame), 30, FinTsSyntaxError.InvalidSignatureContext);
            string number = signature.Request.Frame.MessageNumber.ToString(CultureInfo.InvariantCulture);
            // Only credential-free observations enter the builder. Preserve optional empty fields and explicit read versions.
            var prefix = new StringBuilder("HNHBK:1:3+000000000000+300+").Append(dialogue).Append('+').Append(number).Append('\'');
            FinTsPinTanRequestWriter.AppendPublicSegment(prefix, signature.Header.Source, 2);
            FinTsPinTanRequestWriter.AppendPublicSegment(prefix, signature.Request.Request.Source, 3);
            string ending = "HNHBS:5:1+" + number + "'";
            if (prefix.Length + FinTsPinTanSignatureTrailerWriter.MaximumEncodedLength + ending.Length > MaximumEncodedLength)
            { return FinTsPinTanRequestWriteResult.ContextNeedsReview; }
            int position = Encoding.Latin1.GetBytes(prefix.ToString(), wire);
            var result = FinTsPinTanSignatureTrailerWriter.TryEncodeReadPinOnly(context, pin,
                wire.Slice(position, FinTsPinTanSignatureTrailerWriter.MaximumEncodedLength), out int trailerLength, cancellationToken);
            if (result != FinTsSignatureTrailerWriteResult.Written)
            {
                return result switch
                {
                    FinTsSignatureTrailerWriteResult.ContextNeedsReview => FinTsPinTanRequestWriteResult.ContextNeedsReview,
                    FinTsSignatureTrailerWriteResult.CredentialUnavailable => FinTsPinTanRequestWriteResult.CredentialUnavailable,
                    FinTsSignatureTrailerWriteResult.InvalidCredentialText => FinTsPinTanRequestWriteResult.InvalidCredentialText,
                    FinTsSignatureTrailerWriteResult.CredentialRequirementsNeedReview => FinTsPinTanRequestWriteResult.CredentialRequirementsNeedReview,
                    _ => throw new InvalidOperationException("Unexpected read trailer result."),
                };
            }
            position += trailerLength;
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
            CryptographicOperations.ZeroMemory(wire);
        }
    }
}
