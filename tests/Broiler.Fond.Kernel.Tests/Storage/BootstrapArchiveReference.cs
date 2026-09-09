using System.Globalization;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Xml;
using System.Xml.Linq;
using System.Xml.Schema;

namespace Broiler.Fond.Kernel.Tests.Storage;

internal sealed record BootstrapProfile(Guid ProfileId, Guid BranchId, ulong Generation,
    ulong NextId, DateTimeOffset SavedAt, string Label, string Note);

// Test-only bootstrap schema/lexical verifier. A production ZIP preflight and
// streaming reader are still required; this accepts only bounded synthetic input.
internal static class BootstrapArchiveReference
{
    internal const string Namespace = "urn:broiler:fond:profile:1";
    private const int DocumentLimit = 16 * 1024;
    private const string TimestampFormat = "yyyy-MM-dd'T'HH:mm:ss.fffffff'Z'";
    private static readonly UTF8Encoding StrictUtf8 = new(false, true);
    private static readonly XNamespace Ns = Namespace;

    internal static byte[] SerializeProfile(BootstrapProfile profile) => Serialize(writer =>
    {
        WriteRoot(writer, "profile", profile);
        writer.WriteAttributeString("nextId", profile.NextId.ToString(CultureInfo.InvariantCulture));
        writer.WriteAttributeString("savedAt", profile.SavedAt.ToUniversalTime().ToString(TimestampFormat, CultureInfo.InvariantCulture));
        writer.WriteElementString("label", Namespace, profile.Label);
        writer.WriteElementString("note", Namespace, profile.Note);
        writer.WriteEndElement();
    });

    internal static byte[] SerializeManifest(BootstrapProfile profile, byte[] profileBytes) => Serialize(writer =>
    {
        WriteRoot(writer, "manifest", profile);
        writer.WriteStartElement("entry", Namespace);
        writer.WriteAttributeString("name", "profile.xml");
        writer.WriteAttributeString("schema", "1");
        writer.WriteAttributeString("bytes", profileBytes.Length.ToString(CultureInfo.InvariantCulture));
        writer.WriteAttributeString("sha256", Convert.ToHexStringLower(SHA256.HashData(profileBytes)));
        writer.WriteEndElement();
        writer.WriteEndElement();
    });

    private static void WriteRoot(XmlWriter writer, string name, BootstrapProfile profile)
    {
        writer.WriteStartElement(name, Namespace);
        writer.WriteAttributeString("xmlns", Namespace);
        writer.WriteAttributeString("version", "1");
        writer.WriteAttributeString("profileId", profile.ProfileId.ToString("D"));
        writer.WriteAttributeString("branchId", profile.BranchId.ToString("D"));
        writer.WriteAttributeString("generation", profile.Generation.ToString(CultureInfo.InvariantCulture));
    }

    private static byte[] Serialize(Action<XmlWriter> write)
    {
        using MemoryStream output = new();
        using (XmlWriter writer = XmlWriter.Create(output, new XmlWriterSettings
        {
            Encoding = StrictUtf8,
            Indent = false,
            NewLineChars = "\n",
            NewLineHandling = NewLineHandling.Entitize,
            CloseOutput = false,
        }))
        {
            writer.WriteStartDocument();
            write(writer);
            writer.WriteEndDocument();
        }

        return output.ToArray();
    }

    internal static byte[] CreateZip(params (string Name, byte[] Bytes)[] documents)
    {
        using MemoryStream output = new();
        using (ZipArchive zip = new(output, ZipArchiveMode.Create, leaveOpen: true, entryNameEncoding: Encoding.UTF8))
        {
            foreach ((string name, byte[] bytes) in documents)
            {
                ZipArchiveEntry entry = zip.CreateEntry(name, CompressionLevel.Optimal);
                entry.LastWriteTime = new(1980, 1, 1, 0, 0, 0, TimeSpan.Zero);
                entry.ExternalAttributes = 0x20;
                using Stream target = entry.Open();
                target.Write(bytes);
            }
        }

        return output.ToArray();
    }

    internal static BootstrapProfile ValidateZip(byte[] payload)
    {
        Require(payload.Length is > 0 and <= 1024 * 1024);
        using MemoryStream input = new(payload, writable: false);
        using ZipArchive zip = new(input, ZipArchiveMode.Read, leaveOpen: false, entryNameEncoding: StrictUtf8);
        Require(zip.Entries.Count == 2);
        string[] names = ["profile.xml", "manifest.xml"];
        byte[][] documents = new byte[2][];
        for (int index = 0; index < names.Length; index++)
        {
            ZipArchiveEntry entry = zip.Entries[index];
            Require(entry.FullName == names[index]);
            Require(entry.Length is > 0 and <= DocumentLimit);
            Require(entry.Length <= Math.Max(1, entry.CompressedLength) * 128);
            Require(entry.ExternalAttributes == 0x20);
            Require(entry.LastWriteTime.DateTime == new DateTime(1980, 1, 1, 0, 0, 0, DateTimeKind.Unspecified));
            using Stream source = entry.Open();
            byte[] bytes = new byte[(int)entry.Length];
            source.ReadExactly(bytes);
            Require(source.ReadByte() == -1);
            documents[index] = bytes;
        }

        return ValidateDocuments(documents[0], documents[1]);
    }

    internal static BootstrapProfile ValidateDocuments(byte[] profileBytes, byte[] manifestBytes)
    {
        XElement profile = ReadXml(profileBytes, "profile");
        XElement manifest = ReadXml(manifestBytes, "manifest");
        Guid profileId = Guid.ParseExact(Attribute(profile, "profileId"), "D");
        Guid branchId = Guid.ParseExact(Attribute(profile, "branchId"), "D");
        Require(profileId != Guid.Empty && branchId != Guid.Empty);
        ulong generation = ulong.Parse(Attribute(profile, "generation"), NumberStyles.None, CultureInfo.InvariantCulture);
        ulong nextId = ulong.Parse(Attribute(profile, "nextId"), NumberStyles.None, CultureInfo.InvariantCulture);
        DateTimeOffset savedAt = DateTimeOffset.ParseExact(Attribute(profile, "savedAt"), TimestampFormat,
            CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal);
        string label = profile.Element(Ns + "label")!.Value;
        string note = profile.Element(Ns + "note")!.Value;
        Require(ScalarCount(label) <= 256 && ScalarCount(note) <= 1024);
        BootstrapProfile result = new(profileId, branchId, generation, nextId, savedAt, label, note);
        Require(profileBytes.AsSpan().SequenceEqual(SerializeProfile(result)));

        Require(Attribute(manifest, "profileId") == Attribute(profile, "profileId") &&
            Attribute(manifest, "branchId") == Attribute(profile, "branchId") &&
            Attribute(manifest, "generation") == Attribute(profile, "generation"));
        Require(manifestBytes.AsSpan().SequenceEqual(SerializeManifest(result, profileBytes)));
        return result;
    }

    private static XElement ReadXml(byte[] bytes, string rootName)
    {
        Require(bytes.Length is > 0 and <= DocumentLimit);
        _ = StrictUtf8.GetCharCount(bytes);
        XmlSchemaSet schemas = new() { XmlResolver = null };
        using Stream schemaStream = typeof(BootstrapArchiveReference).Assembly.GetManifestResourceStream("ProfileEnvelope.bootstrap-v1.xsd")!;
        using XmlReader schemaReader = XmlReader.Create(schemaStream, new XmlReaderSettings
        {
            DtdProcessing = DtdProcessing.Prohibit,
            XmlResolver = null,
            MaxCharactersInDocument = 16384,
            MaxCharactersFromEntities = 1024,
        });
        schemas.Add(Namespace, schemaReader);
        using MemoryStream input = new(bytes, writable: false);
        using XmlReader reader = XmlReader.Create(input, new XmlReaderSettings
        {
            DtdProcessing = DtdProcessing.Prohibit,
            XmlResolver = null,
            MaxCharactersInDocument = 16384,
            MaxCharactersFromEntities = 1024,
            ValidationType = ValidationType.Schema,
            Schemas = schemas,
            ValidationFlags = XmlSchemaValidationFlags.None,
        });
        // First pass checks depth before materializing the bounded document.
        while (reader.Read())
        {
            Require(reader.Depth <= 8);
        }

        input.Position = 0;
        using XmlReader materializer = XmlReader.Create(input, new XmlReaderSettings
        {
            DtdProcessing = DtdProcessing.Prohibit,
            XmlResolver = null,
            MaxCharactersInDocument = 16384,
            MaxCharactersFromEntities = 1024,
        });
        XElement root = XDocument.Load(materializer, LoadOptions.PreserveWhitespace).Root!;
        Require(root.Name == Ns + rootName);
        return root;
    }

    private static int ScalarCount(string value)
    {
        int count = 0;
        foreach (Rune rune in value.EnumerateRunes())
        {
            count++;
        }

        return count;
    }

    private static string Attribute(XElement element, string name) => element.Attribute(name)!.Value;

    private static void Require(bool valid)
    {
        if (!valid)
        {
            throw new InvalidDataException("Invalid bootstrap profile archive.");
        }
    }
}
