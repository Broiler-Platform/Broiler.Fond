using System.Security.Cryptography;
using System.Text;
using System.Xml;

namespace Broiler.Fond.Kernel.Professional;

/// <summary>
/// Supported EBICS protocol versions.
/// </summary>
public enum EbicsVersion
{
    /// <summary>
    /// EBICS 2.5 (Schema H004)
    /// </summary>
    H004 = 0,

    /// <summary>
    /// EBICS 3.0 (Schema H005)
    /// </summary>
    H005 = 1
}

/// <summary>
/// Host configuration descriptor for direct device-to-bank EBICS connectivity.
/// </summary>
public sealed record EbicsHostConfig
{
    public string HostId { get; }
    public string PartnerId { get; }
    public string UserId { get; }
    public Uri ServerUrl { get; }
    public EbicsVersion Version { get; }

    public EbicsHostConfig(
        string hostId,
        string partnerId,
        string userId,
        Uri serverUrl,
        EbicsVersion version = EbicsVersion.H004)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(hostId);
        ArgumentException.ThrowIfNullOrWhiteSpace(partnerId);
        ArgumentException.ThrowIfNullOrWhiteSpace(userId);
        ArgumentNullException.ThrowIfNull(serverUrl);

        if (!serverUrl.IsAbsoluteUri || (serverUrl.Scheme != Uri.UriSchemeHttps && serverUrl.Scheme != Uri.UriSchemeHttp))
        {
            throw new ArgumentException("Server URL must be an absolute HTTP/HTTPS URI.", nameof(serverUrl));
        }

        HostId = hostId.Trim();
        PartnerId = partnerId.Trim();
        UserId = userId.Trim();
        ServerUrl = serverUrl;
        Version = version;
    }
}

/// <summary>
/// Standard EBICS order types for initialization, cash management, and SEPA payments.
/// </summary>
public enum EbicsOrderType
{
    /// <summary>Bank initialization: user electronic signature public key (A005/A006)</summary>
    INI = 0,

    /// <summary>Host initialization: authentication (X002) and encryption (E002) public keys</summary>
    HIA = 1,

    /// <summary>Download bank certificates and public keys</summary>
    HPB = 2,

    /// <summary>End-of-day bank statement retrieval (SWIFT MT940 / CAMT.053)</summary>
    STA = 3,

    /// <summary>Interim intraday transaction report (SWIFT MT942 / CAMT.052)</summary>
    VMK = 4,

    /// <summary>Corporate Customer Credit Transfer (SEPA PAIN.001)</summary>
    CCT = 5,

    /// <summary>Corporate Customer Direct Debit (SEPA PAIN.008)</summary>
    CDD = 6,

    /// <summary>Suspension of corporate user access</summary>
    SPR = 7
}

public static class EbicsOrderTypeExtensions
{
    public static string ToOrderCode(this EbicsOrderType orderType) => orderType switch
    {
        EbicsOrderType.INI => "INI",
        EbicsOrderType.HIA => "HIA",
        EbicsOrderType.HPB => "HPB",
        EbicsOrderType.STA => "STA",
        EbicsOrderType.VMK => "VMK",
        EbicsOrderType.CCT => "CCT",
        EbicsOrderType.CDD => "CDD",
        EbicsOrderType.SPR => "SPR",
        _ => throw new ArgumentOutOfRangeException(nameof(orderType), orderType, null)
    };

    public static EbicsOrderType ParseOrderType(string code) => code?.Trim().ToUpperInvariant() switch
    {
        "INI" => EbicsOrderType.INI,
        "HIA" => EbicsOrderType.HIA,
        "HPB" => EbicsOrderType.HPB,
        "STA" => EbicsOrderType.STA,
        "VMK" => EbicsOrderType.VMK,
        "CCT" => EbicsOrderType.CCT,
        "CDD" => EbicsOrderType.CDD,
        "SPR" => EbicsOrderType.SPR,
        _ => throw new ArgumentException($"Unknown or unsupported EBICS order type code: '{code}'", nameof(code))
    };

    public static bool IsInitialization(this EbicsOrderType orderType) =>
        orderType is EbicsOrderType.INI or EbicsOrderType.HIA or EbicsOrderType.HPB;
}

/// <summary>
/// Classification of cryptographic key pairs used in EBICS protocol.
/// </summary>
public enum EbicsKeyType
{
    /// <summary>Electronic signature key (A005 / A006)</summary>
    SignatureA005 = 0,

    /// <summary>Authentication key (X002)</summary>
    AuthenticationX002 = 1,

    /// <summary>Payload encryption key (E002)</summary>
    EncryptionE002 = 2
}

/// <summary>
/// Managed RSA keypair for EBICS digital signature, authentication, and payload encryption.
/// Uses exclusively .NET 10 BCL cryptography.
/// </summary>
public sealed class EbicsKeyPair : IDisposable
{
    private readonly RSA _rsa;
    private bool _disposed;

    public EbicsKeyType KeyType { get; }
    public int KeySize { get; }

    public EbicsKeyPair(EbicsKeyType keyType, int keySize = 2048)
    {
        if (keySize < 2048)
        {
            throw new ArgumentOutOfRangeException(nameof(keySize), "Key size must be at least 2048 bits for EBICS conformance.");
        }

        KeyType = keyType;
        KeySize = keySize;
        _rsa = RSA.Create(keySize);
    }

    public RSA Rsa => _disposed ? throw new ObjectDisposedException(nameof(EbicsKeyPair)) : _rsa;

    /// <summary>
    /// Exports modulus and exponent in base64 format for XML inclusion.
    /// </summary>
    public (string ModulusBase64, string ExponentBase64) ExportPublicKeyParameters()
    {
        if (_disposed) throw new ObjectDisposedException(nameof(EbicsKeyPair));

        RSAParameters parameters = _rsa.ExportParameters(includePrivateParameters: false);
        return (Convert.ToBase64String(parameters.Modulus!), Convert.ToBase64String(parameters.Exponent!));
    }

    /// <summary>
    /// Calculates standard public key digest (SHA-256 of exponent + modulus).
    /// </summary>
    public string ComputePublicKeyDigest()
    {
        if (_disposed) throw new ObjectDisposedException(nameof(EbicsKeyPair));

        RSAParameters parameters = _rsa.ExportParameters(includePrivateParameters: false);
        byte[] payload = [.. parameters.Exponent!, .. parameters.Modulus!];
        return Convert.ToHexString(SHA256.HashData(payload)).ToLowerInvariant();
    }

    public void Dispose()
    {
        if (!_disposed)
        {
            _rsa.Dispose();
            _disposed = true;
        }
    }
}

/// <summary>
/// Pure BCL EBICS digital signing and digest verification.
/// </summary>
public static class EbicsSigner
{
    public static byte[] SignDigest(byte[] digest, RSA privateKey)
    {
        ArgumentNullException.ThrowIfNull(digest);
        ArgumentNullException.ThrowIfNull(privateKey);

        return privateKey.SignHash(digest, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
    }

    public static bool VerifyDigest(byte[] digest, byte[] signature, RSA publicKey)
    {
        ArgumentNullException.ThrowIfNull(digest);
        ArgumentNullException.ThrowIfNull(signature);
        ArgumentNullException.ThrowIfNull(publicKey);

        return publicKey.VerifyHash(digest, signature, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
    }

    public static byte[] ComputeSha256(byte[] data)
    {
        ArgumentNullException.ThrowIfNull(data);
        return SHA256.HashData(data);
    }

    public static byte[] ComputeSha256(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        return SHA256.HashData(Encoding.UTF8.GetBytes(text));
    }
}

/// <summary>
/// Codec for generating and parsing EBICS XML request and response envelopes.
/// </summary>
public static class EbicsMessageCodec
{
    public static string GetNamespaceUri(EbicsVersion version) => version switch
    {
        EbicsVersion.H004 => "http://www.ebics.org/H004",
        EbicsVersion.H005 => "http://www.ebics.org/H005",
        _ => "http://www.ebics.org/H004"
    };

    /// <summary>
    /// Builds an unsecured INI request XML for electronic signature public key upload.
    /// </summary>
    public static string BuildUnsecuredIniRequest(
        EbicsHostConfig hostConfig,
        EbicsKeyPair signatureKey,
        DateTimeOffset timestampUtc)
    {
        ArgumentNullException.ThrowIfNull(hostConfig);
        ArgumentNullException.ThrowIfNull(signatureKey);

        string ns = GetNamespaceUri(hostConfig.Version);
        var (modulus, exponent) = signatureKey.ExportPublicKeyParameters();

        var settings = new XmlWriterSettings
        {
            Encoding = Encoding.UTF8,
            OmitXmlDeclaration = false,
            Indent = false
        };

        var sb = new StringBuilder();
        using (var writer = XmlWriter.Create(sb, settings))
        {
            writer.WriteStartDocument();
            writer.WriteStartElement("ebicsUnsecuredRequest", ns);
            writer.WriteAttributeString("version", hostConfig.Version == EbicsVersion.H005 ? "H005" : "H004");
            writer.WriteAttributeString("revision", "1");

            writer.WriteStartElement("header");
            writer.WriteAttributeString("authenticate", "true");

            writer.WriteStartElement("staticHeader");
            writer.WriteElementString("HostID", hostConfig.HostId);
            writer.WriteElementString("PartnerID", hostConfig.PartnerId);
            writer.WriteElementString("UserID", hostConfig.UserId);

            writer.WriteStartElement("OrderDetails");
            writer.WriteElementString("OrderType", "INI");
            writer.WriteElementString("OrderAttribute", "DZHNN");
            writer.WriteEndElement(); // OrderDetails

            writer.WriteEndElement(); // staticHeader

            writer.WriteStartElement("mutableHeader");
            writer.WriteElementString("Timestamp", timestampUtc.ToString("yyyy-MM-ddTHH:mm:ssZ"));
            writer.WriteEndElement(); // mutableHeader

            writer.WriteEndElement(); // header

            writer.WriteStartElement("body");
            writer.WriteStartElement("DataTransfer");
            writer.WriteStartElement("DataEncryptionInfo");
            writer.WriteAttributeString("authenticate", "false");
            writer.WriteElementString("EncryptionPubKeyDigest", signatureKey.ComputePublicKeyDigest());
            writer.WriteEndElement(); // DataEncryptionInfo

            writer.WriteStartElement("OrderData");
            writer.WriteStartElement("SignaturePubKeyInfo");
            writer.WriteStartElement("PubKeyValue");
            writer.WriteStartElement("RSAKeyValue");
            writer.WriteElementString("Modulus", modulus);
            writer.WriteElementString("Exponent", exponent);
            writer.WriteEndElement(); // RSAKeyValue
            writer.WriteEndElement(); // PubKeyValue
            writer.WriteElementString("SignatureVersion", "A005");
            writer.WriteEndElement(); // SignaturePubKeyInfo
            writer.WriteEndElement(); // OrderData

            writer.WriteEndElement(); // DataTransfer
            writer.WriteEndElement(); // body

            writer.WriteEndElement(); // ebicsUnsecuredRequest
            writer.WriteEndDocument();
        }

        return sb.ToString();
    }

    /// <summary>
    /// Builds an unsecured HIA request XML for authentication (X002) and encryption (E002) keys upload.
    /// </summary>
    public static string BuildUnsecuredHiaRequest(
        EbicsHostConfig hostConfig,
        EbicsKeyPair authKey,
        EbicsKeyPair encKey,
        DateTimeOffset timestampUtc)
    {
        ArgumentNullException.ThrowIfNull(hostConfig);
        ArgumentNullException.ThrowIfNull(authKey);
        ArgumentNullException.ThrowIfNull(encKey);

        string ns = GetNamespaceUri(hostConfig.Version);
        var (authModulus, authExponent) = authKey.ExportPublicKeyParameters();
        var (encModulus, encExponent) = encKey.ExportPublicKeyParameters();

        var settings = new XmlWriterSettings
        {
            Encoding = Encoding.UTF8,
            OmitXmlDeclaration = false,
            Indent = false
        };

        var sb = new StringBuilder();
        using (var writer = XmlWriter.Create(sb, settings))
        {
            writer.WriteStartDocument();
            writer.WriteStartElement("ebicsUnsecuredRequest", ns);
            writer.WriteAttributeString("version", hostConfig.Version == EbicsVersion.H005 ? "H005" : "H004");
            writer.WriteAttributeString("revision", "1");

            writer.WriteStartElement("header");
            writer.WriteAttributeString("authenticate", "true");

            writer.WriteStartElement("staticHeader");
            writer.WriteElementString("HostID", hostConfig.HostId);
            writer.WriteElementString("PartnerID", hostConfig.PartnerId);
            writer.WriteElementString("UserID", hostConfig.UserId);

            writer.WriteStartElement("OrderDetails");
            writer.WriteElementString("OrderType", "HIA");
            writer.WriteElementString("OrderAttribute", "DZHNN");
            writer.WriteEndElement(); // OrderDetails

            writer.WriteEndElement(); // staticHeader

            writer.WriteStartElement("mutableHeader");
            writer.WriteElementString("Timestamp", timestampUtc.ToString("yyyy-MM-ddTHH:mm:ssZ"));
            writer.WriteEndElement(); // mutableHeader

            writer.WriteEndElement(); // header

            writer.WriteStartElement("body");
            writer.WriteStartElement("DataTransfer");

            writer.WriteStartElement("OrderData");
            writer.WriteStartElement("HIARequestOrderData");

            // Authentication key X002
            writer.WriteStartElement("AuthenticationPubKeyInfo");
            writer.WriteStartElement("PubKeyValue");
            writer.WriteStartElement("RSAKeyValue");
            writer.WriteElementString("Modulus", authModulus);
            writer.WriteElementString("Exponent", authExponent);
            writer.WriteEndElement(); // RSAKeyValue
            writer.WriteEndElement(); // PubKeyValue
            writer.WriteElementString("AuthenticationVersion", "X002");
            writer.WriteEndElement(); // AuthenticationPubKeyInfo

            // Encryption key E002
            writer.WriteStartElement("EncryptionPubKeyInfo");
            writer.WriteStartElement("PubKeyValue");
            writer.WriteStartElement("RSAKeyValue");
            writer.WriteElementString("Modulus", encModulus);
            writer.WriteElementString("Exponent", encExponent);
            writer.WriteEndElement(); // RSAKeyValue
            writer.WriteEndElement(); // PubKeyValue
            writer.WriteElementString("EncryptionVersion", "E002");
            writer.WriteEndElement(); // EncryptionPubKeyInfo

            writer.WriteEndElement(); // HIARequestOrderData
            writer.WriteEndElement(); // OrderData

            writer.WriteEndElement(); // DataTransfer
            writer.WriteEndElement(); // body

            writer.WriteEndElement(); // ebicsUnsecuredRequest
            writer.WriteEndDocument();
        }

        return sb.ToString();
    }

    /// <summary>
    /// Builds an operational signed ebicsRequest for data transmission (CCT, STA, etc.).
    /// </summary>
    public static string BuildOperationalRequest(
        EbicsHostConfig hostConfig,
        EbicsOrderType orderType,
        string? transactionId,
        ReadOnlyMemory<byte> payload,
        EbicsKeyPair authKey,
        DateTimeOffset timestampUtc)
    {
        ArgumentNullException.ThrowIfNull(hostConfig);
        ArgumentNullException.ThrowIfNull(authKey);

        string ns = GetNamespaceUri(hostConfig.Version);
        string orderCode = orderType.ToOrderCode();
        string base64Payload = Convert.ToBase64String(payload.Span);

        var settings = new XmlWriterSettings
        {
            Encoding = Encoding.UTF8,
            OmitXmlDeclaration = false,
            Indent = false
        };

        var sb = new StringBuilder();
        using (var writer = XmlWriter.Create(sb, settings))
        {
            writer.WriteStartDocument();
            writer.WriteStartElement("ebicsRequest", ns);
            writer.WriteAttributeString("version", hostConfig.Version == EbicsVersion.H005 ? "H005" : "H004");
            writer.WriteAttributeString("revision", "1");

            writer.WriteStartElement("header");
            writer.WriteAttributeString("authenticate", "true");

            writer.WriteStartElement("staticHeader");
            writer.WriteElementString("HostID", hostConfig.HostId);
            writer.WriteElementString("PartnerID", hostConfig.PartnerId);
            writer.WriteElementString("UserID", hostConfig.UserId);

            writer.WriteStartElement("OrderDetails");
            writer.WriteElementString("OrderType", orderCode);
            writer.WriteElementString("OrderAttribute", "OZHNN");
            writer.WriteEndElement(); // OrderDetails

            writer.WriteEndElement(); // staticHeader

            writer.WriteStartElement("mutableHeader");
            if (!string.IsNullOrWhiteSpace(transactionId))
            {
                writer.WriteElementString("TransactionID", transactionId);
            }
            writer.WriteElementString("Timestamp", timestampUtc.ToString("yyyy-MM-ddTHH:mm:ssZ"));
            writer.WriteEndElement(); // mutableHeader

            writer.WriteEndElement(); // header

            // Digital signature of header
            byte[] headerDigest = EbicsSigner.ComputeSha256(sb.ToString());
            byte[] signature = EbicsSigner.SignDigest(headerDigest, authKey.Rsa);
            string signatureBase64 = Convert.ToBase64String(signature);

            writer.WriteStartElement("AuthSignature");
            writer.WriteElementString("SignatureValue", signatureBase64);
            writer.WriteEndElement(); // AuthSignature

            writer.WriteStartElement("body");
            writer.WriteStartElement("DataTransfer");
            writer.WriteStartElement("OrderData");
            writer.WriteString(base64Payload);
            writer.WriteEndElement(); // OrderData
            writer.WriteEndElement(); // DataTransfer
            writer.WriteEndElement(); // body

            writer.WriteEndElement(); // ebicsRequest
            writer.WriteEndDocument();
        }

        return sb.ToString();
    }

    /// <summary>
    /// Parses return code and text from an EBICS response XML envelope safely.
    /// </summary>
    public static (string ReturnCode, string? ReportText) ParseResponseReturnCode(string responseXml)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(responseXml);

        var readerSettings = new XmlReaderSettings
        {
            DtdProcessing = DtdProcessing.Prohibit,
            XmlResolver = null,
            IgnoreWhitespace = true
        };

        string returnCode = "UNKNOWN";
        string? reportText = null;

        using var reader = XmlReader.Create(new StringReader(responseXml), readerSettings);
        while (!reader.EOF)
        {
            if (reader.NodeType == XmlNodeType.Element)
            {
                if (reader.LocalName == "ReturnCode")
                {
                    returnCode = reader.ReadElementContentAsString().Trim();
                    continue;
                }

                if (reader.LocalName == "ReportText")
                {
                    reportText = reader.ReadElementContentAsString().Trim();
                    continue;
                }
            }

            reader.Read();
        }

        return (returnCode, reportText);
    }

    /// <summary>
    /// Returns true if an EBICS return code represents successful execution.
    /// Standard EBICS return codes:
    /// - 000000: OK
    /// - 011000: [Authentication OK]
    /// - 011001: [Download OK]
    /// </summary>
    public static bool IsSuccessReturnCode(string returnCode) =>
        string.Equals(returnCode, "000000", StringComparison.Ordinal) ||
        string.Equals(returnCode, "011000", StringComparison.Ordinal) ||
        string.Equals(returnCode, "011001", StringComparison.Ordinal);
}
