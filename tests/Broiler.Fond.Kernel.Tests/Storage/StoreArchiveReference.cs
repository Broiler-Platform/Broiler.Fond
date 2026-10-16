namespace Broiler.Fond.Kernel.Tests.Storage;

/// <summary>
/// Test forwarding wrapper to the production <see cref="StoreArchiveCodec"/> in <c>Broiler.Fond.Kernel.Storage</c>.
/// </summary>
internal static class StoreArchiveReference
{
    internal const string ProfileNamespace = StoreArchiveCodec.ProfileNamespace;
    internal const string PurgeNamespace = StoreArchiveCodec.PurgeNamespace;

    public static IReadOnlyList<string> StoreDocumentNames => StoreArchiveCodec.StoreDocumentNames;

    internal static byte[] SerializeProfile(StoreProfileDocument profile) =>
        StoreArchiveCodec.SerializeProfile(profile);

    internal static byte[] SerializeInstitutions(StoreInstitutionsDocument doc) =>
        StoreArchiveCodec.SerializeInstitutions(doc);

    internal static byte[] SerializeConnections(StoreConnectionsDocument doc) =>
        StoreArchiveCodec.SerializeConnections(doc);

    internal static byte[] SerializeAccounts(StoreAccountsDocument doc) =>
        StoreArchiveCodec.SerializeAccounts(doc);

    internal static byte[] SerializeBalances(StoreBalancesDocument doc) =>
        StoreArchiveCodec.SerializeBalances(doc);

    internal static byte[] SerializeLedger(StoreLedgerDocument doc) =>
        StoreArchiveCodec.SerializeLedger(doc);

    internal static byte[] SerializeManifest(
        Guid profileId,
        Guid branchId,
        ulong generation,
        IReadOnlyList<(string Name, byte[] Bytes)> entries) =>
        StoreArchiveCodec.SerializeManifest(profileId, branchId, generation, entries);

    internal static byte[] SerializePurgeIntent(StorePurgeIntent intent) =>
        StoreArchiveCodec.SerializePurgeIntent(intent);

    internal static byte[] CreateStoreZip(StoreSnapshot snapshot) =>
        StoreArchiveCodec.CreateStoreZip(snapshot);

    internal static byte[] CreateZip(params (string Name, byte[] Bytes)[] documents) =>
        StoreArchiveCodec.CreateZip(documents);

    internal static StoreSnapshot ValidateAndParseStoreZip(byte[] payload) =>
        StoreArchiveCodec.ValidateAndParseStoreZip(payload);

    internal static StorePurgeIntent ValidateAndParsePurgeIntent(byte[] xmlBytes) =>
        StoreArchiveCodec.ValidateAndParsePurgeIntent(xmlBytes);
}
