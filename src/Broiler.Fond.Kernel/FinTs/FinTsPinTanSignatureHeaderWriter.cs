using System.Globalization;
using System.Text;

namespace Broiler.Fond.Kernel.FinTs;

/// <summary>Explicit immutable header input. Values are validated when encoded; no PIN, TAN or default clock/counter allocation.</summary>
public sealed class FinTsPinTanSignatureHeaderInput
{
    public FinTsPinTanSignatureHeaderInput(int profileVersion, int securityFunction, string controlReference,
        int securitySupplierRole, int securityParty, string systemId, ulong securityReferenceNumber,
        DateOnly? securityDate, TimeOnly? securityTime, string hashAlgorithmCode, string signatureAlgorithmCode,
        string operationModeCode, string countryCode, string institutionId, string userId, int keyNumber, int keyVersion)
    {
        ArgumentNullException.ThrowIfNull(controlReference); ArgumentNullException.ThrowIfNull(systemId);
        ArgumentNullException.ThrowIfNull(hashAlgorithmCode); ArgumentNullException.ThrowIfNull(signatureAlgorithmCode);
        ArgumentNullException.ThrowIfNull(operationModeCode); ArgumentNullException.ThrowIfNull(countryCode);
        ArgumentNullException.ThrowIfNull(institutionId); ArgumentNullException.ThrowIfNull(userId);
        ProfileVersion = profileVersion; SecurityFunction = securityFunction; ControlReference = controlReference;
        SecuritySupplierRole = securitySupplierRole; SecurityParty = securityParty; SystemId = systemId;
        SecurityReferenceNumber = securityReferenceNumber; SecurityDate = securityDate; SecurityTime = securityTime;
        HashAlgorithmCode = hashAlgorithmCode; SignatureAlgorithmCode = signatureAlgorithmCode; OperationModeCode = operationModeCode;
        CountryCode = countryCode; InstitutionId = institutionId; UserId = userId; KeyNumber = keyNumber; KeyVersion = keyVersion;
    }
    public int ProfileVersion { get; }
    public int SecurityFunction { get; }
    public string ControlReference { get; }
    public int SecuritySupplierRole { get; }
    public int SecurityParty { get; }
    public string SystemId { get; }
    public ulong SecurityReferenceNumber { get; }
    public DateOnly? SecurityDate { get; }
    public TimeOnly? SecurityTime { get; }
    public string HashAlgorithmCode { get; }
    public string SignatureAlgorithmCode { get; }
    public string OperationModeCode { get; }
    public string CountryCode { get; }
    public string InstitutionId { get; }
    public string UserId { get; }
    public int KeyNumber { get; }
    public int KeyVersion { get; }
}

public static class FinTsPinTanSignatureHeaderWriter
{
    /// <summary>Encodes one HNSHK-4 segment only. No signed frame, permission check, credentials, transport or cryptographic operation.</summary>
    public static byte[] Encode(FinTsPinTanSignatureHeaderInput input, int segmentNumber, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(input); cancellationToken.ThrowIfCancellationRequested();
        if (segmentNumber is < 1 or > 999 || input.ProfileVersion is < 0 or > 999 || input.SecurityFunction is < 0 or > 999 ||
            input.SecuritySupplierRole is < 0 or > 999 || input.SecurityParty is < 0 or > 999 ||
            input.KeyNumber is < 0 or > 999 || input.KeyVersion is < 0 or > 999 || input.SecurityReferenceNumber > 9999999999999999UL ||
            input.SecurityTime is { } time && (input.SecurityDate is null || time.Ticks % TimeSpan.TicksPerSecond != 0))
        { throw new FinTsFormatException(FinTsSyntaxError.InvalidSignatureHeader); }
        static string Number(int value) => value.ToString(CultureInfo.InvariantCulture);
        static string Text(string value, int maximum) => FinTsUnsignedWireEncoding.Text(value, maximum, FinTsSyntaxError.InvalidSignatureHeader);
        var wire = new StringBuilder("HNSHK:").Append(Number(segmentNumber)).Append(":4+PIN:").Append(Number(input.ProfileVersion))
            .Append('+').Append(Number(input.SecurityFunction)).Append('+').Append(Text(input.ControlReference, 14))
            .Append("+1+").Append(Number(input.SecuritySupplierRole)).Append('+').Append(Number(input.SecurityParty)).Append("::").Append(Text(input.SystemId, 30))
            .Append('+').Append(input.SecurityReferenceNumber.ToString(CultureInfo.InvariantCulture)).Append("+1");
        if (input.SecurityDate is { } date)
        {
            wire.Append(':').Append(date.ToString("yyyyMMdd", CultureInfo.InvariantCulture));
            if (input.SecurityTime is { } stamp) { wire.Append(':').Append(stamp.ToString("HHmmss", CultureInfo.InvariantCulture)); }
        }
        wire.Append("+1:").Append(Text(input.HashAlgorithmCode, 3)).Append(":1+6:").Append(Text(input.SignatureAlgorithmCode, 3)).Append(':').Append(Text(input.OperationModeCode, 3))
            .Append('+').Append(Text(input.CountryCode, 3)).Append(':').Append(Text(input.InstitutionId, 30)).Append(':').Append(Text(input.UserId, 30))
            .Append(":S:").Append(Number(input.KeyNumber)).Append(':').Append(Number(input.KeyVersion)).Append('\'');
        byte[] bytes = Encoding.Latin1.GetBytes(wire.ToString());
        _ = FinTsPinTanSignatureHeader.Parse(FinTsSyntax.ParseSegments(bytes, cancellationToken).Segments.Single(), cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();
        return bytes;
    }
}
