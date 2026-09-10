using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace Broiler.Fond.Kernel.FinTs;

public enum FinTsPinTanRequestWriteResult
{
    Written, DestinationTooSmall, ContextNeedsReview, TanNotPermitted, CredentialUnavailable, InvalidCredentialText, CredentialRequirementsNeedReview,
}

/// <summary>Assembles one bounded plain initialization/synchronization candidate, before the security envelope.
/// Successful caller output contains plaintext credentials and must be cleared. No authentication or sending.</summary>
public static class FinTsPinTanRequestWriter
{
    // Conservative reserve for the restricted header, two/three body segments, escaped credentials and framing.
    public const int MaximumEncodedLength = 2048;

    /// <summary>Requires the full reserve before credential access. Failure publishes no message and reports zero bytes.
    /// Cancellation cancels supplied owners; a TAN already copied out remains consumed on any later failure.</summary>
    public static FinTsPinTanRequestWriteResult TryEncode(FinTsPinTanSignatureEvidence context, FinTsSessionCredential pin,
        FinTsSessionCredential? tan, Span<byte> destination, out int bytesWritten, CancellationToken cancellationToken = default)
    {
        bytesWritten = 0;
        ArgumentNullException.ThrowIfNull(context); ArgumentNullException.ThrowIfNull(pin);
        Span<byte> wire = stackalloc byte[MaximumEncodedLength]; wire.Clear();
        int copied = 0; bool success = false;
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!context.HasMatchingEvidence) { return FinTsPinTanRequestWriteResult.ContextNeedsReview; }
            if (destination.Length < MaximumEncodedLength) { return FinTsPinTanRequestWriteResult.DestinationTooSmall; }

            // Only credential-free, schema-validated observations enter this builder. Preserve optional empty fields.
            var prefix = new StringBuilder("HNHBK:1:3+000000000000+300+0+1'");
            AppendPublicSegment(prefix, context.Header.Source, 2);
            var segments = context.Request.Frame.Syntax.Segments;
            for (int i = 1; i < segments.Count - 1; i++) { AppendPublicSegment(prefix, segments[i], i + 2); }
            int trailerNumber = segments.Count + 1;
            string ending = "HNHBS:" + (trailerNumber + 1).ToString(CultureInfo.InvariantCulture) + ":1+1'";
            if (prefix.Length + FinTsPinTanSignatureTrailerWriter.MaximumEncodedLength + ending.Length > MaximumEncodedLength)
            { return FinTsPinTanRequestWriteResult.ContextNeedsReview; }
            int position = Encoding.Latin1.GetBytes(prefix.ToString(), wire);
            var trailerResult = FinTsPinTanSignatureTrailerWriter.TryEncode(context, pin, tan, trailerNumber,
                wire.Slice(position, FinTsPinTanSignatureTrailerWriter.MaximumEncodedLength), out int trailerLength, cancellationToken);
            if (trailerResult != FinTsSignatureTrailerWriteResult.Written)
            {
                return trailerResult switch
                {
                    FinTsSignatureTrailerWriteResult.ContextNeedsReview => FinTsPinTanRequestWriteResult.ContextNeedsReview,
                    FinTsSignatureTrailerWriteResult.TanNotPermitted => FinTsPinTanRequestWriteResult.TanNotPermitted,
                    FinTsSignatureTrailerWriteResult.CredentialUnavailable => FinTsPinTanRequestWriteResult.CredentialUnavailable,
                    FinTsSignatureTrailerWriteResult.InvalidCredentialText => FinTsPinTanRequestWriteResult.InvalidCredentialText,
                    _ => throw new InvalidOperationException("Unexpected trailer encoding result."),
                };
            }
            position += trailerLength;
            position += Encoding.Latin1.GetBytes(ending, wire[position..]);
            // HNHBK's twelve-digit byte size includes both framing segments; all text is single-byte Latin-1.
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
            CryptographicOperations.ZeroMemory(wire);
        }
    }

    internal static void AppendPublicSegment(StringBuilder output, FinTsSegment source, int number)
    {
        output.Append(source.Code).Append(':').Append(number.ToString(CultureInfo.InvariantCulture)).Append(':')
            .Append(source.Version.ToString(CultureInfo.InvariantCulture));
        // The restricted schemas reject references and binary elements; never broaden this to secret-bearing segments.
        if (source.Reference is not null) { throw new FinTsFormatException(FinTsSyntaxError.InvalidSignatureContext); }
        foreach (var field in source.Fields)
        {
            output.Append('+');
            for (int i = 0; i < field.Elements.Count; i++)
            {
                if (i > 0) { output.Append(':'); }
                var element = field.Elements[i];
                if (element.IsBinary) { throw new FinTsFormatException(FinTsSyntaxError.InvalidSignatureContext); }
                output.Append(FinTsUnsignedWireEncoding.Text(Encoding.Latin1.GetString(element.CopyValueBytes()), int.MaxValue, FinTsSyntaxError.InvalidSignatureContext));
            }
        }
        output.Append('\'');
    }
}
