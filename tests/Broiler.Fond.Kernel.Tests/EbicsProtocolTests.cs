using System.Security.Cryptography;
using System.Text;
using Broiler.Fond.Kernel.Professional;

namespace Broiler.Fond.Kernel.Tests;

public static class EbicsProtocolTests
{
    public static void RunAllTests()
    {
        Console.WriteLine("Running EBICS Protocol Architecture & Order Types verification (P-02)...");
        int checks = 0;

        // 1. EbicsHostConfig invariants
        var hostConfig = new EbicsHostConfig("EBICS01", "PARTNER123", "USER456", new Uri("https://ebics.bank.example/server"), EbicsVersion.H004);
        Assert(hostConfig.HostId == "EBICS01", "HostId must match");
        Assert(hostConfig.PartnerId == "PARTNER123", "PartnerId must match");
        Assert(hostConfig.UserId == "USER456", "UserId must match");
        Assert(hostConfig.Version == EbicsVersion.H004, "Version must match H004");
        checks += 4;

        bool invalidUriCaught = false;
        try
        {
            _ = new EbicsHostConfig("EBICS01", "PARTNER123", "USER456", new Uri("ftp://ebics.bank.example"));
        }
        catch (ArgumentException)
        {
            invalidUriCaught = true;
        }
        Assert(invalidUriCaught, "Non-HTTP/HTTPS URI must be rejected");
        checks++;

        // 2. EbicsOrderType mappings
        Assert(EbicsOrderType.INI.ToOrderCode() == "INI", "INI code matches");
        Assert(EbicsOrderType.HIA.ToOrderCode() == "HIA", "HIA code matches");
        Assert(EbicsOrderType.HPB.ToOrderCode() == "HPB", "HPB code matches");
        Assert(EbicsOrderType.STA.ToOrderCode() == "STA", "STA code matches");
        Assert(EbicsOrderType.VMK.ToOrderCode() == "VMK", "VMK code matches");
        Assert(EbicsOrderType.CCT.ToOrderCode() == "CCT", "CCT code matches");
        Assert(EbicsOrderType.CDD.ToOrderCode() == "CDD", "CDD code matches");
        Assert(EbicsOrderType.SPR.ToOrderCode() == "SPR", "SPR code matches");
        checks += 8;

        Assert(EbicsOrderTypeExtensions.ParseOrderType("cct") == EbicsOrderType.CCT, "ParseOrderType case-insensitive matches");
        Assert(EbicsOrderType.INI.IsInitialization(), "INI is initialization");
        Assert(EbicsOrderType.HIA.IsInitialization(), "HIA is initialization");
        Assert(!EbicsOrderType.CCT.IsInitialization(), "CCT is not initialization");
        checks += 4;

        // 3. EbicsKeyPair generation & BCL cryptographic properties
        using var sigKey = new EbicsKeyPair(EbicsKeyType.SignatureA005, 2048);
        using var authKey = new EbicsKeyPair(EbicsKeyType.AuthenticationX002, 2048);
        using var encKey = new EbicsKeyPair(EbicsKeyType.EncryptionE002, 2048);

        Assert(sigKey.KeyType == EbicsKeyType.SignatureA005, "KeyType matches SignatureA005");
        Assert(sigKey.KeySize == 2048, "KeySize is 2048");
        var (modulus, exponent) = sigKey.ExportPublicKeyParameters();
        Assert(!string.IsNullOrWhiteSpace(modulus), "Modulus is exported");
        Assert(!string.IsNullOrWhiteSpace(exponent), "Exponent is exported");
        string pubDigest = sigKey.ComputePublicKeyDigest();
        Assert(pubDigest.Length == 64, "SHA-256 digest is 64 hex characters");
        checks += 5;

        // Minimum key size check
        bool smallKeyCaught = false;
        try
        {
            _ = new EbicsKeyPair(EbicsKeyType.SignatureA005, 1024);
        }
        catch (ArgumentOutOfRangeException)
        {
            smallKeyCaught = true;
        }
        Assert(smallKeyCaught, "Key sizes below 2048 bits must be rejected");
        checks++;

        // Disposal check
        var tempKey = new EbicsKeyPair(EbicsKeyType.AuthenticationX002, 2048);
        tempKey.Dispose();
        bool disposedCallCaught = false;
        try
        {
            _ = tempKey.ExportPublicKeyParameters();
        }
        catch (ObjectDisposedException)
        {
            disposedCallCaught = true;
        }
        Assert(disposedCallCaught, "Disposed key must throw ObjectDisposedException");
        checks++;

        // 4. Digital Signature generation & verification (pure BCL)
        byte[] testData = Encoding.UTF8.GetBytes("Corporate Payment Order Payload Hash Verification");
        byte[] digest = EbicsSigner.ComputeSha256(testData);
        byte[] signature = EbicsSigner.SignDigest(digest, sigKey.Rsa);

        bool signatureValid = EbicsSigner.VerifyDigest(digest, signature, sigKey.Rsa);
        Assert(signatureValid, "Signature must verify with matching public key");
        checks++;

        // Tampered signature or digest
        byte[] tamperedDigest = [.. digest];
        tamperedDigest[0] ^= 0xFF;
        bool tamperedVerified = EbicsSigner.VerifyDigest(tamperedDigest, signature, sigKey.Rsa);
        Assert(!tamperedVerified, "Tampered digest must fail signature verification");
        checks++;

        // Verification with wrong key
        bool wrongKeyVerified = EbicsSigner.VerifyDigest(digest, signature, authKey.Rsa);
        Assert(!wrongKeyVerified, "Signature must fail with different key");
        checks++;

        // 5. EbicsMessageCodec: Unsecured INI Request XML
        var now = DateTimeOffset.UtcNow;
        string iniXml = EbicsMessageCodec.BuildUnsecuredIniRequest(hostConfig, sigKey, now);
        Assert(iniXml.Contains("ebicsUnsecuredRequest"), "Must contain ebicsUnsecuredRequest");
        Assert(iniXml.Contains("HostID>EBICS01<"), "Must contain HostID");
        Assert(iniXml.Contains("PartnerID>PARTNER123<"), "Must contain PartnerID");
        Assert(iniXml.Contains("UserID>USER456<"), "Must contain UserID");
        Assert(iniXml.Contains("OrderType>INI<"), "Must contain OrderType INI");
        Assert(iniXml.Contains("SignatureVersion>A005<"), "Must contain SignatureVersion A005");
        Assert(iniXml.Contains(modulus), "Must contain public key modulus");
        checks += 7;

        // 6. EbicsMessageCodec: Unsecured HIA Request XML
        string hiaXml = EbicsMessageCodec.BuildUnsecuredHiaRequest(hostConfig, authKey, encKey, now);
        Assert(hiaXml.Contains("OrderType>HIA<"), "Must contain OrderType HIA");
        Assert(hiaXml.Contains("AuthenticationVersion>X002<"), "Must contain AuthenticationVersion X002");
        Assert(hiaXml.Contains("EncryptionVersion>E002<"), "Must contain EncryptionVersion E002");
        checks += 3;

        // 7. EbicsMessageCodec: Operational Request (CCT SEPA Transfer)
        byte[] painPayload = Encoding.UTF8.GetBytes("<pain.001.001.09>BatchCreditTransfer</pain.001.001.09>");
        string operationalXml = EbicsMessageCodec.BuildOperationalRequest(
            hostConfig,
            EbicsOrderType.CCT,
            "TX-9901",
            painPayload,
            authKey,
            now);

        Assert(operationalXml.Contains("ebicsRequest"), "Must contain ebicsRequest root");
        Assert(operationalXml.Contains("OrderType>CCT<"), "Must contain OrderType CCT");
        Assert(operationalXml.Contains("TransactionID>TX-9901<"), "Must contain TransactionID");
        Assert(operationalXml.Contains("AuthSignature"), "Must contain AuthSignature");
        Assert(operationalXml.Contains("SignatureValue"), "Must contain SignatureValue");
        string base64Payload = Convert.ToBase64String(painPayload);
        Assert(operationalXml.Contains(base64Payload), "Must contain base64 payload");
        checks += 6;

        // 8. Response parsing and return code classification
        string successXml = @"<?xml version=""1.0"" encoding=""UTF-8""?>
<ebicsResponse xmlns=""http://www.ebics.org/H004"">
  <header>
    <ReturnCode>000000</ReturnCode>
    <ReportText>[EBICS_OK] Request processed successfully.</ReportText>
  </header>
  <body>
    <DataTransfer/>
  </body>
</ebicsResponse>";

        var (successCode, successReport) = EbicsMessageCodec.ParseResponseReturnCode(successXml);
        Assert(successCode == "000000", "Must parse return code 000000");
        Assert(successReport?.Contains("EBICS_OK") == true, "Must parse report text");
        Assert(EbicsMessageCodec.IsSuccessReturnCode(successCode), "000000 must be recognized as success");
        Assert(EbicsMessageCodec.IsSuccessReturnCode("011000"), "011000 authentication OK is recognized as success");
        checks += 4;

        string errorXml = @"<?xml version=""1.0"" encoding=""UTF-8""?>
<ebicsResponse xmlns=""http://www.ebics.org/H004"">
  <header>
    <ReturnCode>091002</ReturnCode>
    <ReportText>[EBICS_USER_UNKNOWN] User not found.</ReportText>
  </header>
</ebicsResponse>";

        var (errorCode, errorReport) = EbicsMessageCodec.ParseResponseReturnCode(errorXml);
        Assert(errorCode == "091002", "Must parse return code 091002");
        Assert(errorReport?.Contains("EBICS_USER_UNKNOWN") == true, "Must parse user unknown error");
        Assert(!EbicsMessageCodec.IsSuccessReturnCode(errorCode), "091002 must not be recognized as success");
        checks += 3;

        Console.WriteLine($"EBICS Protocol Architecture & Order Types checks passed ({checks} checks).");
    }

    private static void Assert(bool condition, string message)
    {
        if (!condition)
        {
            throw new InvalidOperationException($"Assertion failed: {message}");
        }
    }
}
