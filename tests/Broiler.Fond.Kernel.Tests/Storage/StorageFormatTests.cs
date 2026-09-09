using System.Buffers.Binary;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Xml;
using System.Xml.Schema;

namespace Broiler.Fond.Kernel.Tests.Storage;

internal static class StorageFormatTests
{
    internal static void Run(Action<bool, string> check)
    {
        int checks = 0;
        void Verify(bool condition, string message)
        {
            checks++;
            check(condition, message);
        }

        using Stream stream = typeof(StorageFormatTests).Assembly.GetManifestResourceStream("ProfileEnvelope.v1.json")!;
        using JsonDocument fixture = JsonDocument.Parse(stream);
        JsonElement vectors = fixture.RootElement.GetProperty("vectors");
        foreach (JsonElement vector in vectors.EnumerateArray())
        {
            VerifyVector(vector, Verify);
        }

        JsonElement bootstrap = vectors[1];
        byte[] envelope = Base64(bootstrap, "envelopeBase64");
        byte[] key = Hex(bootstrap, "contentKeyHex");
        byte[] header = Hex(bootstrap, "headerHex");
        string passphrase = bootstrap.GetProperty("passphrase").GetString()!;
        Reject(() => EnvelopeReference.Decrypt(envelope, "wrong public fixture passphrase"), Verify, "Wrong passphrase");

        for (int offset = 0; offset < EnvelopeReference.HeaderSize; offset++)
        {
            byte[] changed = (byte[])envelope.Clone();
            changed[offset] ^= 1;
            Reject(() => EnvelopeReference.DecryptWithKey(changed, key), Verify, $"Header byte {offset} tamper");
        }

        foreach (uint iterations in new uint[] { 0, 599_999, 5_000_001, uint.MaxValue })
        {
            byte[] changed = (byte[])envelope.Clone();
            BinaryPrimitives.WriteUInt32LittleEndian(changed.AsSpan(16), iterations);
            // Invalid passphrase proves header limits are checked before passphrase/KDF work.
            try
            {
                _ = EnvelopeReference.Decrypt(changed, null!);
                Verify(false, "Unbounded KDF header accepted.");
            }
            catch (InvalidDataException)
            {
                Verify(true, "KDF bounds precede passphrase handling.");
            }
        }

        foreach (uint iterations in new uint[] { 600_000, 5_000_000 })
        {
            byte[] changed = (byte[])header.Clone();
            BinaryPrimitives.WriteUInt32LittleEndian(changed.AsSpan(16), iterations);
            EnvelopeReference.ValidateHeader(changed);
            Verify(true, "Inclusive KDF format bounds.");
        }

        for (int length = 0; length < envelope.Length; length++)
        {
            byte[] truncated = envelope[..length];
            Reject(() => EnvelopeReference.DecryptWithKey(truncated, key), Verify, $"Truncation at byte {length}");
        }

        for (int offset = EnvelopeReference.HeaderSize; offset < envelope.Length; offset++)
        {
            byte[] changed = (byte[])envelope.Clone();
            changed[offset] ^= 0x80;
            Reject(() => EnvelopeReference.DecryptWithKey(changed, key), Verify, $"Record byte {offset} tamper");
        }

        Reject(() => EnvelopeReference.DecryptWithKey([.. envelope, 0], key), Verify, "Trailing byte");
        foreach (int length in new[] { 16, 24, 31, 33 })
        {
            Reject(() => EnvelopeReference.Encrypt([], header, new byte[length]), Verify, "Non-AES-256 writer key");
            Reject(() => EnvelopeReference.DecryptWithKey(envelope, new byte[length]), Verify, "Non-AES-256 reader key");
        }
        VerifyFrameAttacks(vectors[4], Verify);
        VerifyPassphrases(Verify);
        VerifyXml(fixture.RootElement, Verify);
        Console.WriteLine($"Storage format candidate: {vectors.GetArrayLength()} independent vectors and {checks} checks completed.");
    }

    private static void VerifyVector(JsonElement vector, Action<bool, string> verify)
    {
        byte[] header = Hex(vector, "headerHex");
        byte[] payload = Base64(vector, "payloadBase64");
        byte[] expected = Base64(vector, "envelopeBase64");
        string passphrase = vector.GetProperty("passphrase").GetString()!;
        byte[] normalized = EnvelopeReference.NormalizePassphrase(passphrase);
        verify(normalized.AsSpan().SequenceEqual(Hex(vector, "normalizedUtf8Hex")), "Passphrase UTF-8 known answer.");
        byte[] master = EnvelopeReference.DeriveMasterKey(header, passphrase);
        verify(master.AsSpan().SequenceEqual(Hex(vector, "masterKeyHex")), "PBKDF2 independent known answer.");
        byte[] key = EnvelopeReference.DeriveContentKey(header, master);
        verify(key.AsSpan().SequenceEqual(Hex(vector, "contentKeyHex")), "HKDF independent known answer.");
        byte[] actual = EnvelopeReference.Encrypt(payload, header, key);
        verify(actual.AsSpan().SequenceEqual(expected), "Complete encrypted envelope independent known answer.");
        verify(Digest(actual) == vector.GetProperty("envelopeSha256").GetString(), "Final file digest.");
        verify(EnvelopeReference.Decrypt(expected, passphrase).AsSpan().SequenceEqual(payload), "Independent envelope decryption.");
        verify(EnvelopeReference.Decrypt(expected, vector.GetProperty("decomposedPassphrase").GetString()!).AsSpan().SequenceEqual(payload),
            "NFC composed and decomposed passphrases must unlock the same vector.");

        int offset = EnvelopeReference.HeaderSize;
        ulong ordinal = 0;
        foreach (JsonElement frame in vector.GetProperty("frames").EnumerateArray())
        {
            byte[] descriptor = expected.AsSpan(offset, 13).ToArray();
            int length = (int)BinaryPrimitives.ReadUInt32LittleEndian(descriptor.AsSpan(8));
            verify(descriptor.AsSpan().SequenceEqual(Hex(frame, "descriptorHex")), "Frame descriptor known answer.");
            byte[] nonce = new byte[12];
            header.AsSpan(88, 4).CopyTo(nonce);
            BinaryPrimitives.WriteUInt64BigEndian(nonce.AsSpan(4), ordinal++);
            verify(nonce.AsSpan().SequenceEqual(Hex(frame, "nonceHex")), "Frame nonce known answer.");
            byte[] aad = [.. header, .. descriptor];
            verify(aad.AsSpan().SequenceEqual(Hex(frame, "aadHex")), "Frame AAD known answer.");
            verify(Digest(expected.AsSpan(offset + 13, length)) == frame.GetProperty("ciphertextSha256").GetString(), "Frame ciphertext digest.");
            verify(expected.AsSpan(offset + 13 + length, 16).SequenceEqual(Hex(frame, "tagHex")), "Frame authentication tag.");
            offset += 13 + length + 16;
        }

        verify(offset == expected.Length, "Fixture frame inventory must cover the whole envelope.");
        CryptographicOperations.ZeroMemory(normalized);
        CryptographicOperations.ZeroMemory(master);
        CryptographicOperations.ZeroMemory(key);
    }

    private static void VerifyFrameAttacks(JsonElement vector, Action<bool, string> verify)
    {
        byte[] envelope = Base64(vector, "envelopeBase64");
        byte[] header = Hex(vector, "headerHex");
        byte[] key = Hex(vector, "contentKeyHex");
        const int full = 65_536 + 29;
        byte[] first = envelope[96..(96 + full)];
        byte[] second = envelope[(96 + full)..(96 + 2 * full)];
        byte[] tail = envelope[(96 + 2 * full)..];
        Reject(() => EnvelopeReference.DecryptWithKey([.. header, .. second, .. first, .. tail], key), verify, "Reordered frames");
        Reject(() => EnvelopeReference.DecryptWithKey([.. header, .. first, .. tail], key), verify, "Deleted middle frame");
        Reject(() => EnvelopeReference.DecryptWithKey([.. header, .. first, .. first, .. second, .. tail], key), verify, "Duplicated frame");
        byte[] differentHeader = (byte[])header.Clone();
        differentHeader[56] ^= 1;
        byte[] differentKey = EnvelopeReference.DeriveContentKey(differentHeader, Hex(vector, "masterKeyHex"));
        byte[] otherSnapshot = EnvelopeReference.Encrypt(Base64(vector, "payloadBase64"), differentHeader, differentKey);
        byte[] otherSecond = otherSnapshot[(96 + full)..(96 + 2 * full)];
        Reject(() => EnvelopeReference.DecryptWithKey([.. header, .. first, .. otherSecond, .. tail], key), verify, "Cross-snapshot splice");

        // Correct tags on structurally invalid records exercise grammar, not just authentication.
        Reject(() => EnvelopeReference.DecryptWithKey(SealRecords(header, key, ([], 0), ([], 1)), key), verify, "Empty data frame");
        Reject(() => EnvelopeReference.DecryptWithKey(SealRecords(header, key, ([1], 1)), key), verify, "Nonempty final frame");
        Reject(() => EnvelopeReference.DecryptWithKey(SealRecords(header, key, ([1], 2)), key), verify, "Unknown frame flag");
        Reject(() => EnvelopeReference.DecryptWithKey(SealRecords(header, key, ([1], 0), ([2], 0), ([], 1)), key), verify, "Data after short frame");
        Reject(() => EnvelopeReference.DecryptWithKey(SealRecords(header, key, (new byte[65_537], 0), ([], 1)), key), verify, "Oversize frame");
        Reject(() => EnvelopeReference.DecryptWithKey(SealRecords(header, key, ([], 1), ([], 1)), key), verify, "Duplicate final frame");
        Reject(() => EnvelopeReference.Encrypt(new byte[EnvelopeReference.TestPayloadLimit + 1], header, key), verify, "Test writer allocation bound");
        Reject(() => EnvelopeReference.DecryptWithKey(new byte[EnvelopeReference.TestPayloadLimit + 1_000_000], key), verify, "Test reader allocation bound");
    }

    private static byte[] SealRecords(byte[] header, byte[] key, params (byte[] Data, byte Flag)[] records)
    {
        using MemoryStream output = new();
        output.Write(header);
        using AesGcm aes = new(key, 16);
        for (int ordinal = 0; ordinal < records.Length; ordinal++)
        {
            (byte[] data, byte flag) = records[ordinal];
            byte[] descriptor = new byte[13];
            BinaryPrimitives.WriteUInt64LittleEndian(descriptor, (ulong)ordinal);
            BinaryPrimitives.WriteUInt32LittleEndian(descriptor.AsSpan(8), (uint)data.Length);
            descriptor[12] = flag;
            byte[] nonce = new byte[12];
            header.AsSpan(88, 4).CopyTo(nonce);
            BinaryPrimitives.WriteUInt64BigEndian(nonce.AsSpan(4), (ulong)ordinal);
            byte[] encrypted = new byte[data.Length];
            byte[] tag = new byte[16];
            byte[] aad = [.. header, .. descriptor];
            aes.Encrypt(nonce, data, encrypted, tag, aad);
            output.Write(descriptor);
            output.Write(encrypted);
            output.Write(tag);
        }

        return output.ToArray();
    }

    private static void VerifyPassphrases(Action<bool, string> verify)
    {
        foreach (string invalid in new[] { "", "\ud800", "\udc00", new string('a', 1025), new string('é', 513) })
        {
            Reject(() => EnvelopeReference.NormalizePassphrase(invalid), verify, "Invalid/bounded passphrase");
        }

        verify(EnvelopeReference.NormalizePassphrase(new string('a', 1024)).Length == 1024, "Maximum UTF-8 passphrase length.");
        verify(EnvelopeReference.NormalizePassphrase(new string('é', 512)).Length == 1024, "Maximum multibyte passphrase length.");
        verify(EnvelopeReference.NormalizePassphrase(" A ").AsSpan().SequenceEqual(" A "u8), "Passphrase whitespace and case must be preserved.");
        verify(!EnvelopeReference.NormalizePassphrase("A").AsSpan().SequenceEqual(EnvelopeReference.NormalizePassphrase("a")), "Passphrases remain case-sensitive.");
        byte[] ikm = Enumerable.Repeat((byte)0x0b, 22).ToArray();
        byte[] actual = HKDF.DeriveKey(HashAlgorithmName.SHA256, ikm, 42,
            Convert.FromHexString("000102030405060708090a0b0c"), Convert.FromHexString("f0f1f2f3f4f5f6f7f8f9"));
        verify(actual.AsSpan().SequenceEqual(Convert.FromHexString(
            "3cb25f25faacd57a90434f64d0362f2a2d2d0a90cf1a5a4c5db02d56ecc4c5bf34007208d5b887185865")), "RFC 5869 A.1 known answer.");
    }

    private static void VerifyXml(JsonElement fixture, Action<bool, string> verify)
    {
        byte[] profileBytes = Hex(fixture, "profileXmlHex");
        byte[] manifestBytes = Hex(fixture, "manifestXmlHex");
        byte[] zip = Base64(fixture.GetProperty("vectors")[1], "payloadBase64");
        BootstrapProfile profile = BootstrapArchiveReference.ValidateZip(zip);
        verify(profile.Label == "" && profile.Note == "Grüße 東京", "Empty and non-ASCII XML fields.");
        CultureInfo previous = CultureInfo.CurrentCulture;
        try
        {
            foreach (string culture in new[] { "de-DE", "tr-TR", "ar-SA" })
            {
                CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo(culture);
                verify(BootstrapArchiveReference.SerializeProfile(profile).AsSpan().SequenceEqual(profileBytes), "Profile XML must be byte-exact across cultures.");
                verify(BootstrapArchiveReference.SerializeManifest(profile, profileBytes).AsSpan().SequenceEqual(manifestBytes), "Manifest XML must be byte-exact across cultures.");
            }
        }
        finally
        {
            CultureInfo.CurrentCulture = previous;
        }

        byte[] ownZip = BootstrapArchiveReference.CreateZip(("profile.xml", profileBytes), ("manifest.xml", manifestBytes));
        verify(BootstrapArchiveReference.ValidateZip(ownZip) == profile, ".NET ZIP metadata and XML round-trip.");
        BootstrapProfile maximum = profile with { Label = string.Concat(Enumerable.Repeat("🔐", 256)), Note = new string('x', 1024), Generation = ulong.MaxValue, NextId = ulong.MaxValue };
        byte[] maximumBytes = BootstrapArchiveReference.SerializeProfile(maximum);
        verify(BootstrapArchiveReference.ValidateDocuments(maximumBytes, BootstrapArchiveReference.SerializeManifest(maximum, maximumBytes)) == maximum,
            "Maximum scalar field lengths and unsigned 64-bit integers.");
        foreach (BootstrapProfile invalid in new[] { profile with { Label = new string('x', 257) }, profile with { Note = new string('x', 1025) }, profile with { NextId = 0 }, profile with { Generation = 0 }, profile with { ProfileId = Guid.Empty } })
        {
            byte[] bytes = BootstrapArchiveReference.SerializeProfile(invalid);
            Reject(() => BootstrapArchiveReference.ValidateDocuments(bytes, BootstrapArchiveReference.SerializeManifest(invalid, bytes)), verify, "Invalid profile scalar");
        }

        string xml = Encoding.UTF8.GetString(profileBytes);
        string[] invalidXml =
        [
            xml.Replace("<profile ", "<!DOCTYPE profile [<!ENTITY e SYSTEM 'file:///never-read'>]><profile ", StringComparison.Ordinal),
            xml.Replace("<label />", "<label>&e;</label>", StringComparison.Ordinal),
            xml.Replace("<label />", "<label></label>", StringComparison.Ordinal),
            xml.Replace("<label />", "<!--hidden--><label />", StringComparison.Ordinal),
            xml.Replace("<label />", "<extra /><label />", StringComparison.Ordinal),
            xml.Replace("<label />", "<label><![CDATA[]]></label>", StringComparison.Ordinal),
            xml.Replace("<label />", "\r\n<label />", StringComparison.Ordinal),
            xml.Replace("generation=\"1\"", "generation=\"01\"", StringComparison.Ordinal),
            xml.Replace("generation=\"1\"", "generation=\"18446744073709551616\"", StringComparison.Ordinal),
            xml.Replace("2000-01-01", "2000-02-31", StringComparison.Ordinal),
            xml.Replace("version=\"1\" profileId", "version=\"2\" profileId", StringComparison.Ordinal),
            xml.Replace("<profile ", "<profile xmlns:xsi=\"http://www.w3.org/2001/XMLSchema-instance\" xsi:schemaLocation=\"urn:broiler:fond:profile:1 https://never-contact.example/schema\" ", StringComparison.Ordinal),
        ];
        foreach (string invalid in invalidXml)
        {
            Reject(() => BootstrapArchiveReference.ValidateDocuments(Encoding.UTF8.GetBytes(invalid), manifestBytes), verify, "Hostile/noncanonical XML");
        }

        Reject(() => BootstrapArchiveReference.ValidateDocuments([0xef, 0xbb, 0xbf, .. profileBytes], manifestBytes), verify, "XML BOM");
        Reject(() => BootstrapArchiveReference.ValidateDocuments([0xff, .. profileBytes], manifestBytes), verify, "Invalid UTF-8");
        Reject(() => BootstrapArchiveReference.ValidateDocuments(new byte[16 * 1024 + 1], manifestBytes), verify, "XML byte limit");
        byte[] changedManifest = Encoding.UTF8.GetBytes(Encoding.UTF8.GetString(manifestBytes).Replace("generation=\"1\"", "generation=\"2\"", StringComparison.Ordinal));
        Reject(() => BootstrapArchiveReference.ValidateDocuments(profileBytes, changedManifest), verify, "Manifest lineage mismatch");
        byte[] changedProfile = Encoding.UTF8.GetBytes(xml.Replace("Grüße", "Hello", StringComparison.Ordinal));
        Reject(() => BootstrapArchiveReference.ValidateDocuments(changedProfile, manifestBytes), verify, "Manifest digest mismatch");
        foreach (string invalidName in new[] { "../profile.xml", "/profile.xml", "PROFILE.XML", "profile.xml/" })
        {
            byte[] invalidZip = BootstrapArchiveReference.CreateZip((invalidName, profileBytes), ("manifest.xml", manifestBytes));
            Reject(() => BootstrapArchiveReference.ValidateZip(invalidZip), verify, "Unlisted ZIP entry name");
        }

        Reject(() => BootstrapArchiveReference.ValidateZip(BootstrapArchiveReference.CreateZip(("profile.xml", profileBytes), ("profile.xml", profileBytes))), verify, "Duplicate ZIP entries");
        Reject(() => BootstrapArchiveReference.ValidateZip(BootstrapArchiveReference.CreateZip(("manifest.xml", manifestBytes), ("profile.xml", profileBytes))), verify, "ZIP entry order");
        Reject(() => BootstrapArchiveReference.ValidateZip(BootstrapArchiveReference.CreateZip(("profile.xml", profileBytes))), verify, "Missing manifest");
        Reject(() => BootstrapArchiveReference.ValidateZip(BootstrapArchiveReference.CreateZip(("profile.xml", profileBytes), ("manifest.xml", manifestBytes), ("extra.xml", profileBytes))), verify, "Extra ZIP entry");
        Reject(() => BootstrapArchiveReference.ValidateZip(BootstrapArchiveReference.CreateZip(("profile.xml", new byte[16 * 1024 + 1]), ("manifest.xml", manifestBytes))), verify, "ZIP expansion bound");
    }

    private static void Reject(Action action, Action<bool, string> verify, string name)
    {
        try
        {
            action();
            verify(false, $"{name} must fail closed.");
        }
        catch (Exception exception) when (exception is InvalidDataException or CryptographicException or
            ArgumentException or FormatException or XmlException or XmlSchemaException)
        {
            verify(true, name);
        }
    }

    private static byte[] Hex(JsonElement element, string name) => Convert.FromHexString(element.GetProperty(name).GetString()!);
    private static byte[] Base64(JsonElement element, string name) => Convert.FromBase64String(element.GetProperty(name).GetString()!);
    private static string Digest(ReadOnlySpan<byte> bytes) => Convert.ToHexStringLower(SHA256.HashData(bytes));
}
