using System.Buffers.Text;
using System.Security.Cryptography;

namespace Broiler.Fond.Kernel.FinTs;

public enum FinTsSignatureTrailerWriteResult
{
    Written, DestinationTooSmall, ContextNeedsReview, TanNotPermitted, CredentialUnavailable, InvalidCredentialText, CredentialRequirementsNeedReview,
}

/// <summary>Writes one local HNSHA-2 candidate from owned credentials. No transport, complete message or authentication.
/// Successful output contains secrets and must be cleared by its caller. A TAN copied out remains consumed even if a later check fails.</summary>
public static class FinTsPinTanSignatureTrailerWriter
{
    // HNSHA:nnn:2 + separator + escaped control(14) + two separators + escaped PIN(99) + colon + escaped TAN(99) + terminator.
    public const int MaximumEncodedLength = 440;

    /// <summary>Requires a full MaximumEncodedLength destination reserve before accessing credentials.
    /// Returns actual bytes written; unused destination bytes are untouched. No caller output survives a failed write.
    /// Cancellation cancels supplied owners. Other preflight failures leave available owners untouched.</summary>
    public static FinTsSignatureTrailerWriteResult TryEncode(FinTsPinTanSignatureEvidence context, FinTsSessionCredential pin,
        FinTsSessionCredential? tan, int segmentNumber, Span<byte> destination, out int bytesWritten, CancellationToken cancellationToken = default)
    {
        bytesWritten = 0; ArgumentNullException.ThrowIfNull(context);
        return TryEncodeCore(context.Header, context.HasMatchingEvidence && segmentNumber == context.Request.Frame.Syntax.Segments.Count + 1,
            context.Header.ProfileVersion == 1 || context.MatchingProcedure?.ProcessVariant == "1", pin, tan, segmentNumber, destination, out bytesWritten, cancellationToken);
    }

    internal static FinTsSignatureTrailerWriteResult TryEncodeClosing(FinTsPinTanDialogueEndContext context, FinTsSessionCredential pin,
        Span<byte> destination, out int bytesWritten, CancellationToken cancellationToken) =>
        TryEncodeCore(context.Header, context.HasMatchingEvidence, false, pin, null, 4, destination, out bytesWritten, cancellationToken);

    /// <summary>Writes only the PIN component for the first-read single-account context, at HNSHA segment 4.
    /// Applies reported PIN bounds to the actual copied bytes. This partial security component does not decide TAN omission or SCA readiness.</summary>
    public static FinTsSignatureTrailerWriteResult TryEncodeReadPinOnly(FinTsPinTanReadCapabilityContext context, FinTsSessionCredential pin,
        Span<byte> destination, out int bytesWritten, CancellationToken cancellationToken = default)
    {
        bytesWritten = 0; ArgumentNullException.ThrowIfNull(context);
        return TryEncodeCore(context.Signature.Header, context.HasMatchingEvidence, false, pin, null, 4, destination, out bytesWritten, cancellationToken, context);
    }

    private static FinTsSignatureTrailerWriteResult TryEncodeCore(FinTsPinTanSignatureHeader header, bool matching, bool tanPermitted,
        FinTsSessionCredential pin, FinTsSessionCredential? tan, int segmentNumber, Span<byte> destination, out int bytesWritten, CancellationToken cancellationToken,
        FinTsPinTanReadCapabilityContext? readContext = null)
    {
        bytesWritten = 0;
        ArgumentNullException.ThrowIfNull(pin);
        Span<byte> pinBytes = stackalloc byte[FinTsSessionCredential.MaximumLength];
        Span<byte> tanBytes = stackalloc byte[FinTsSessionCredential.MaximumLength];
        Span<byte> wire = stackalloc byte[MaximumEncodedLength];
        pinBytes.Clear(); tanBytes.Clear(); wire.Clear();
        int copied = 0; bool success = false;
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!matching)
            { return FinTsSignatureTrailerWriteResult.ContextNeedsReview; }
            if (readContext is not null && (readContext.Signature.MatchingRequirements!.MinimumPinLength is null || readContext.Signature.MatchingRequirements.MaximumPinLength is null))
            { return FinTsSignatureTrailerWriteResult.CredentialRequirementsNeedReview; }
            if (tan is not null && !tanPermitted)
            { return FinTsSignatureTrailerWriteResult.TanNotPermitted; }
            if (destination.Length < MaximumEncodedLength) { return FinTsSignatureTrailerWriteResult.DestinationTooSmall; }
            if (pin.GetSnapshot().Kind != FinTsCredentialKind.Pin || tan is not null && tan.GetSnapshot().Kind != FinTsCredentialKind.Tan)
            { return FinTsSignatureTrailerWriteResult.ContextNeedsReview; }
            if (pin.TryCopyTo(pinBytes, out int pinLength, cancellationToken) != FinTsCredentialCopyResult.Copied)
            { return FinTsSignatureTrailerWriteResult.CredentialUnavailable; }
            if (!Printable(pinBytes[..pinLength])) { return FinTsSignatureTrailerWriteResult.InvalidCredentialText; }
            if (readContext is not null && FinTsPinTanReadCredentialComparison.Compare(readContext, FinTsCredentialKind.Pin, pinBytes[..pinLength], cancellationToken) != FinTsPinTanReadCredentialIssue.None)
            { return FinTsSignatureTrailerWriteResult.CredentialRequirementsNeedReview; }
            int tanLength = 0;
            if (tan is not null)
            {
                if (tan.TryCopyTo(tanBytes, out tanLength, cancellationToken) != FinTsCredentialCopyResult.Copied)
                { return FinTsSignatureTrailerWriteResult.CredentialUnavailable; }
                if (!Printable(tanBytes[..tanLength])) { return FinTsSignatureTrailerWriteResult.InvalidCredentialText; }
            }
            int position = 0;
            "HNSHA:"u8.CopyTo(wire); position += 6;
            if (!Utf8Formatter.TryFormat(segmentNumber, wire[position..], out int numberLength)) { throw new InvalidOperationException("Trailer number does not fit."); }
            position += numberLength;
            ":2+"u8.CopyTo(wire[position..]); position += 3;
            foreach (char value in header.ControlReference) { Escaped((byte)value, wire, ref position); }
            "++"u8.CopyTo(wire[position..]); position += 2;
            foreach (byte value in pinBytes[..pinLength]) { Escaped(value, wire, ref position); }
            if (tan is not null)
            {
                wire[position++] = (byte)':';
                foreach (byte value in tanBytes[..tanLength]) { Escaped(value, wire, ref position); }
            }
            wire[position++] = (byte)'\'';
            cancellationToken.ThrowIfCancellationRequested();
            // The PIN may have expired or been cancelled while a separate TAN owner was being consumed.
            if (pin.GetSnapshot().State != FinTsCredentialState.Available) { return FinTsSignatureTrailerWriteResult.CredentialUnavailable; }
            cancellationToken.ThrowIfCancellationRequested();
            wire[..position].CopyTo(destination); copied = position;
            if (pin.GetSnapshot().State != FinTsCredentialState.Available) { return FinTsSignatureTrailerWriteResult.CredentialUnavailable; }
            cancellationToken.ThrowIfCancellationRequested();
            bytesWritten = position; success = true;
            return FinTsSignatureTrailerWriteResult.Written;
        }
        catch (OperationCanceledException)
        {
            pin.Cancel(); tan?.Cancel();
            throw;
        }
        finally
        {
            if (!success && copied > 0) { CryptographicOperations.ZeroMemory(destination[..copied]); }
            CryptographicOperations.ZeroMemory(pinBytes); CryptographicOperations.ZeroMemory(tanBytes); CryptographicOperations.ZeroMemory(wire);
        }
    }

    private static bool Printable(ReadOnlySpan<byte> value)
    {
        if (value.IsEmpty) { return false; }
        foreach (byte octet in value) { if (octet < 32 || octet is >= 127 and <= 160) { return false; } }
        return true;
    }
    private static void Escaped(byte value, Span<byte> output, ref int position)
    {
        if (value is (byte)'+' or (byte)':' or (byte)'\'' or (byte)'?' or (byte)'@') { output[position++] = (byte)'?'; }
        output[position++] = value;
    }
}
