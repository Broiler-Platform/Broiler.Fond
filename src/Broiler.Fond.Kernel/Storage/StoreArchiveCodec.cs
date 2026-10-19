using System.Globalization;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Xml;
using System.Xml.Linq;
using System.Xml.Schema;
using Broiler.Fond.Kernel.Domain.Identifiers;

namespace Broiler.Fond.Kernel.Storage;

public sealed record StoreProfileDocument(
    Guid ProfileId,
    Guid BranchId,
    Guid? ParentBranchId,
    string? RecoveryProvenance,
    ulong Generation,
    ulong NextId,
    DateTimeOffset SavedAt,
    string Label,
    string Note);

public sealed record StoreInstitutionEntry(
    ulong Id,
    string DisplayName,
    string Endpoint,
    string? BankCode,
    string? Bic,
    string VerificationState);

public sealed record StoreInstitutionsDocument(
    Guid ProfileId,
    Guid BranchId,
    ulong Generation,
    IReadOnlyList<StoreInstitutionEntry> Institutions);

public sealed record StoreConnectionEntry(
    ulong Id,
    ulong InstitutionId,
    string UserId,
    string? CustomerId,
    string State,
    string? SystemId,
    string SystemStatus,
    int NextClientMessageNumber,
    bool IsSynchronized,
    DateTimeOffset? LastSuccessfulContact,
    DateTimeOffset? LastAttemptedContact,
    string ConnectorVersion);

public sealed record StoreConnectionsDocument(
    Guid ProfileId,
    Guid BranchId,
    ulong Generation,
    IReadOnlyList<StoreConnectionEntry> Connections);

public sealed record StoreAccountEntry(
    ulong Id,
    string DisplayName,
    string Currency);

public sealed record StoreIncarnationEntry(
    ulong Id,
    ulong AccountId,
    string LifetimeEvidence);

public sealed record StoreBindingEntry(
    ulong Id,
    ulong AccountId,
    ulong IncarnationId,
    ulong InstitutionId,
    ulong ConnectionId,
    string State,
    string Connector,
    string ConnectorVersion,
    string Endpoint,
    string? Iban,
    string? DomesticAccountNumber,
    string? DomesticBankCode,
    string? Subaccount,
    string? Currency,
    IReadOnlyList<(string Issuer, string Scope, string Kind, string Value)> BankIdentifiers);

public sealed record StoreCapabilityEntry(
    ulong ConnectionId,
    ulong AccountId,
    string OperationCode,
    int OperationVersion,
    string Status,
    string? AdvertisedLimit);

public sealed record StoreAccountsDocument(
    Guid ProfileId,
    Guid BranchId,
    ulong Generation,
    IReadOnlyList<StoreAccountEntry> Accounts,
    IReadOnlyList<StoreIncarnationEntry> Incarnations,
    IReadOnlyList<StoreBindingEntry> Bindings,
    IReadOnlyList<StoreCapabilityEntry> Capabilities);

public sealed record StoreBalanceEntry(
    ulong Id,
    ulong ObservationId,
    ulong AccountId,
    ulong IncarnationId,
    ulong BindingId,
    string Kind,
    string? Amount,
    string? Currency,
    DateTimeOffset? BankTimestamp,
    DateTimeOffset RetrievedAt,
    bool IsStale,
    string? SourceLabel);

public sealed record StoreBalancesDocument(
    Guid ProfileId,
    Guid BranchId,
    ulong Generation,
    IReadOnlyList<StoreBalanceEntry> Balances);

public sealed record StoreLedgerRecord(
    ulong Id,
    string Kind,
    ulong? SupersedesId,
    IReadOnlyList<(ulong TargetId, string ExpectedKind)> References);

public sealed record StoreLedgerDocument(
    Guid ProfileId,
    Guid BranchId,
    ulong Generation,
    IReadOnlyList<StoreLedgerRecord> Records);

public sealed record StoreTransactionObservationEntry(
    ulong Id,
    ulong AccountBindingId,
    DateTimeOffset RetrievedAt,
    string StatementFormat,
    string? StatementNumber,
    string? PageToken);

public sealed record StoreTransactionEntry(
    ulong Id,
    ulong AccountId,
    ulong AccountIncarnationId,
    string Status,
    string Kind);

public sealed record StoreTransactionRevisionEntry(
    ulong Id,
    ulong TransactionId,
    ulong ObservationId,
    ulong AccountId,
    ulong AccountIncarnationId,
    ulong AccountBindingId,
    string Status,
    string Kind,
    string Amount,
    string Currency,
    bool IsCredit,
    DateOnly BookingDate,
    DateOnly? ValueDate,
    DateTimeOffset? BankTimestamp,
    DateTimeOffset RetrievedAt,
    string? CounterpartyName,
    string? CounterpartyIban,
    string? CounterpartyBic,
    string? RemittanceText,
    string? EndToEndId,
    string? MandateId,
    string? CreditorId,
    string? CustomerReference,
    string? BankReference,
    string? PurposeCode,
    string? ReturnReasonCode,
    string? RawBankCode);

public sealed record StoreSplitItemEntry(
    int Index,
    decimal Amount,
    string Category,
    string? Note,
    IReadOnlyList<string> Tags);

public sealed record StoreAnnotationRevisionEntry(
    ulong Id,
    ulong TransactionId,
    DateTimeOffset RevisionTimestamp,
    string? UserNote,
    string? Category,
    IReadOnlyList<string> Tags,
    IReadOnlyList<StoreSplitItemEntry>? Splits = null);

public sealed record StorePendingToBookedLinkEntry(
    ulong PendingTransactionId,
    ulong BookedTransactionId,
    DateTimeOffset LinkedAt,
    string MatchCriterion);

public sealed record StoreStatementSyncCheckpointEntry(
    ulong AccountId,
    ulong AccountIncarnationId,
    ulong AccountBindingId,
    DateOnly WindowStart,
    DateOnly WindowEnd,
    DateTimeOffset LastSyncTimestamp,
    string? LastContinuationToken,
    bool IsComplete,
    int ObservedTransactionCount);

public sealed record StoreTransactionsDocument(
    Guid ProfileId,
    Guid BranchId,
    ulong Generation,
    IReadOnlyList<StoreTransactionObservationEntry> Observations,
    IReadOnlyList<StoreTransactionEntry> Transactions,
    IReadOnlyList<StoreTransactionRevisionEntry> Revisions,
    IReadOnlyList<StoreAnnotationRevisionEntry> Annotations,
    IReadOnlyList<StorePendingToBookedLinkEntry> Links,
    IReadOnlyList<StoreStatementSyncCheckpointEntry> Checkpoints);

public sealed record StorePaymentDraftEntry(
    ulong Id,
    ulong SourceAccountId,
    ulong SourceAccountBindingId,
    ulong SourceAccountIncarnationId,
    ulong CurrentRevisionId,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);

public sealed record StorePaymentDraftRevisionEntry(
    ulong Id,
    ulong DraftId,
    int RevisionNumber,
    ulong? PredecessorRevisionId,
    DateTimeOffset Timestamp,
    ulong SourceAccountId,
    ulong SourceAccountBindingId,
    ulong SourceAccountIncarnationId,
    string BeneficiaryName,
    string BeneficiaryIban,
    string? BeneficiaryBic,
    decimal Amount,
    string Currency,
    string? RemittanceUnstructured,
    string? RemittanceCreditorReference,
    string ExecutionMode,
    DateOnly ExecutionDate);

public sealed record StorePaymentVoPResultEntry(
    string Status,
    DateTimeOffset CheckedAt,
    string? MatchedName,
    string? ReasonCode,
    string? WarningMessage,
    bool UserOverride);

public sealed record StorePaymentAttemptEntry(
    ulong Id,
    ulong DraftId,
    ulong DraftRevisionId,
    ulong SourceAccountId,
    ulong SourceAccountBindingId,
    ulong SourceAccountIncarnationId,
    string EndToEndId,
    string State,
    StorePaymentVoPResultEntry? VoPResult,
    string? BankReference,
    string? ResponseCode,
    string? StatusDescription,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);

public sealed record StorePaymentStateTransitionEntry(
    ulong Id,
    ulong AttemptId,
    string FromState,
    string ToState,
    DateTimeOffset Timestamp,
    string Reason,
    string? BankCode);

public sealed record StorePaymentsDocument(
    Guid ProfileId,
    Guid BranchId,
    ulong Generation,
    IReadOnlyList<StorePaymentDraftEntry> Drafts,
    IReadOnlyList<StorePaymentDraftRevisionEntry> Revisions,
    IReadOnlyList<StorePaymentAttemptEntry> Attempts,
    IReadOnlyList<StorePaymentStateTransitionEntry> Transitions);

public sealed record StoreBeneficiaryTemplateEntry(
    ulong Id,
    string Label,
    string BeneficiaryName,
    string BeneficiaryIban,
    string? BeneficiaryBic,
    string? DefaultRemittanceUnstructured,
    string? DefaultRemittanceCreditorReference,
    decimal? DefaultAmount,
    string? DefaultCurrency,
    string? DefaultExecutionMode,
    bool IsSoftDeleted,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);

public sealed record StoreTemplatesDocument(
    Guid ProfileId,
    Guid BranchId,
    ulong Generation,
    IReadOnlyList<StoreBeneficiaryTemplateEntry> Templates);

public sealed record StoreSnapshot(
    StoreProfileDocument Profile,
    StoreInstitutionsDocument Institutions,
    StoreConnectionsDocument Connections,
    StoreAccountsDocument Accounts,
    StoreBalancesDocument Balances,
    StoreLedgerDocument Ledger,
    StoreTransactionsDocument? Transactions = null,
    StorePaymentsDocument? Payments = null,
    StoreTemplatesDocument? Templates = null)
{
    public StoreTransactionsDocument TransactionsOrDefault =>
        Transactions ?? new StoreTransactionsDocument(Profile.ProfileId, Profile.BranchId, Profile.Generation, [], [], [], [], [], []);

    public StorePaymentsDocument PaymentsOrDefault =>
        Payments ?? new StorePaymentsDocument(Profile.ProfileId, Profile.BranchId, Profile.Generation, [], [], [], []);

    public StoreTemplatesDocument TemplatesOrDefault =>
        Templates ?? new StoreTemplatesDocument(Profile.ProfileId, Profile.BranchId, Profile.Generation, []);
}

public sealed record StorePurgeIntent(
    Guid ProfileId,
    Guid BranchId,
    ulong Generation,
    string OperationType,
    string Status,
    DateTimeOffset CreatedAt,
    ulong? ReplacementGeneration,
    IReadOnlyList<(string Name, string Sha256)> TargetInventory,
    string? Note);

/// <summary>
/// Production serializer, schema validator, and semantic/identity validator for M1/M2 store-v1/transactions-v1
/// financial document archives and purge-intent records.
/// </summary>
public static class StoreArchiveCodec
{
    public const string ProfileNamespace = "urn:broiler:fond:profile:1";
    public const string PurgeNamespace = "urn:broiler:fond:purge:1";
    private const string TimestampFormat = "yyyy-MM-dd'T'HH:mm:ss.fffffff'Z'";
    private static readonly UTF8Encoding StrictUtf8 = new(false, true);
    private static readonly XNamespace NsProfile = ProfileNamespace;
    private static readonly XNamespace NsPurge = PurgeNamespace;

    public static readonly IReadOnlyList<string> LegacyStoreDocumentNames =
    [
        "profile.xml",
        "institutions.xml",
        "connections.xml",
        "accounts.xml",
        "balances.xml",
        "ledger.xml",
        "manifest.xml"
    ];

    public static readonly IReadOnlyList<string> StoreDocumentNamesV2 =
    [
        "profile.xml",
        "institutions.xml",
        "connections.xml",
        "accounts.xml",
        "balances.xml",
        "ledger.xml",
        "transactions.xml",
        "manifest.xml"
    ];

    public static readonly IReadOnlyList<string> StoreDocumentNamesV3 =
    [
        "profile.xml",
        "institutions.xml",
        "connections.xml",
        "accounts.xml",
        "balances.xml",
        "ledger.xml",
        "transactions.xml",
        "payments.xml",
        "manifest.xml"
    ];

    public static readonly IReadOnlyList<string> StoreDocumentNames =
    [
        "profile.xml",
        "institutions.xml",
        "connections.xml",
        "accounts.xml",
        "balances.xml",
        "ledger.xml",
        "transactions.xml",
        "payments.xml",
        "templates.xml",
        "manifest.xml"
    ];

    public static StoreSnapshot CreateEmptySnapshot(
        Guid profileId,
        Guid branchId,
        ulong generation,
        DateTimeOffset savedAt,
        string label,
        string note,
        Guid? parentBranchId = null,
        string? recoveryProvenance = null) =>
        new(
            new StoreProfileDocument(profileId, branchId, parentBranchId, recoveryProvenance, generation, 1UL, savedAt, label, note),
            new StoreInstitutionsDocument(profileId, branchId, generation, []),
            new StoreConnectionsDocument(profileId, branchId, generation, []),
            new StoreAccountsDocument(profileId, branchId, generation, [], [], [], []),
            new StoreBalancesDocument(profileId, branchId, generation, []),
            new StoreLedgerDocument(profileId, branchId, generation, []),
            new StoreTransactionsDocument(profileId, branchId, generation, [], [], [], [], [], []),
            new StorePaymentsDocument(profileId, branchId, generation, [], [], [], []),
            new StoreTemplatesDocument(profileId, branchId, generation, []));

    public static StoreSnapshot WithLineage(
        StoreSnapshot snapshot,
        Guid profileId,
        Guid branchId,
        ulong generation,
        DateTimeOffset savedAt,
        Guid? parentBranchId = null,
        string? recoveryProvenance = null)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        return new StoreSnapshot(
            snapshot.Profile with
            {
                ProfileId = profileId,
                BranchId = branchId,
                ParentBranchId = parentBranchId,
                RecoveryProvenance = recoveryProvenance,
                Generation = generation,
                SavedAt = savedAt,
            },
            snapshot.Institutions with { ProfileId = profileId, BranchId = branchId, Generation = generation },
            snapshot.Connections with { ProfileId = profileId, BranchId = branchId, Generation = generation },
            snapshot.Accounts with { ProfileId = profileId, BranchId = branchId, Generation = generation },
            snapshot.Balances with { ProfileId = profileId, BranchId = branchId, Generation = generation },
            snapshot.Ledger with { ProfileId = profileId, BranchId = branchId, Generation = generation },
            snapshot.TransactionsOrDefault with { ProfileId = profileId, BranchId = branchId, Generation = generation },
            snapshot.PaymentsOrDefault with { ProfileId = profileId, BranchId = branchId, Generation = generation },
            snapshot.TemplatesOrDefault with { ProfileId = profileId, BranchId = branchId, Generation = generation });
    }

    // ==========================================
    // 1. Serialization
    // ==========================================

    public static byte[] SerializeProfile(StoreProfileDocument profile) => Serialize(writer =>
    {
        writer.WriteStartElement("profile", ProfileNamespace);
        writer.WriteAttributeString("xmlns", ProfileNamespace);
        writer.WriteAttributeString("version", "1");
        writer.WriteAttributeString("profileId", profile.ProfileId.ToString("D"));
        writer.WriteAttributeString("branchId", profile.BranchId.ToString("D"));
        if (profile.ParentBranchId.HasValue)
        {
            writer.WriteAttributeString("parentBranchId", profile.ParentBranchId.Value.ToString("D"));
        }

        if (profile.RecoveryProvenance is not null)
        {
            writer.WriteAttributeString("recoveryProvenance", profile.RecoveryProvenance);
        }

        writer.WriteAttributeString("generation", profile.Generation.ToString(CultureInfo.InvariantCulture));
        writer.WriteAttributeString("nextId", profile.NextId.ToString(CultureInfo.InvariantCulture));
        writer.WriteAttributeString("savedAt", profile.SavedAt.ToUniversalTime().ToString(TimestampFormat, CultureInfo.InvariantCulture));
        writer.WriteElementString("label", ProfileNamespace, profile.Label);
        writer.WriteElementString("note", ProfileNamespace, profile.Note);
        writer.WriteEndElement();
    });

    public static byte[] SerializeInstitutions(StoreInstitutionsDocument doc) => Serialize(writer =>
    {
        WriteRootHeader(writer, "institutions", doc.ProfileId, doc.BranchId, doc.Generation);
        foreach (StoreInstitutionEntry inst in doc.Institutions)
        {
            writer.WriteStartElement("institution", ProfileNamespace);
            writer.WriteAttributeString("id", inst.Id.ToString(CultureInfo.InvariantCulture));
            writer.WriteElementString("displayName", ProfileNamespace, inst.DisplayName);
            writer.WriteElementString("endpoint", ProfileNamespace, inst.Endpoint);
            if (inst.BankCode is not null)
            {
                writer.WriteElementString("bankCode", ProfileNamespace, inst.BankCode);
            }

            if (inst.Bic is not null)
            {
                writer.WriteElementString("bic", ProfileNamespace, inst.Bic);
            }

            writer.WriteElementString("verificationState", ProfileNamespace, inst.VerificationState);
            writer.WriteEndElement();
        }

        writer.WriteEndElement();
    });

    public static byte[] SerializeConnections(StoreConnectionsDocument doc) => Serialize(writer =>
    {
        WriteRootHeader(writer, "connections", doc.ProfileId, doc.BranchId, doc.Generation);
        foreach (StoreConnectionEntry conn in doc.Connections)
        {
            writer.WriteStartElement("connection", ProfileNamespace);
            writer.WriteAttributeString("id", conn.Id.ToString(CultureInfo.InvariantCulture));
            writer.WriteAttributeString("institutionId", conn.InstitutionId.ToString(CultureInfo.InvariantCulture));
            writer.WriteElementString("userId", ProfileNamespace, conn.UserId);
            if (conn.CustomerId is not null)
            {
                writer.WriteElementString("customerId", ProfileNamespace, conn.CustomerId);
            }

            writer.WriteElementString("state", ProfileNamespace, conn.State);
            if (conn.SystemId is not null)
            {
                writer.WriteElementString("systemId", ProfileNamespace, conn.SystemId);
            }

            writer.WriteElementString("systemStatus", ProfileNamespace, conn.SystemStatus);
            writer.WriteElementString("nextClientMessageNumber", ProfileNamespace, conn.NextClientMessageNumber.ToString(CultureInfo.InvariantCulture));
            writer.WriteElementString("isSynchronized", ProfileNamespace, conn.IsSynchronized ? "true" : "false");
            if (conn.LastSuccessfulContact.HasValue)
            {
                writer.WriteElementString("lastSuccessfulContact", ProfileNamespace, conn.LastSuccessfulContact.Value.ToUniversalTime().ToString(TimestampFormat, CultureInfo.InvariantCulture));
            }

            if (conn.LastAttemptedContact.HasValue)
            {
                writer.WriteElementString("lastAttemptedContact", ProfileNamespace, conn.LastAttemptedContact.Value.ToUniversalTime().ToString(TimestampFormat, CultureInfo.InvariantCulture));
            }

            writer.WriteElementString("connectorVersion", ProfileNamespace, conn.ConnectorVersion);
            writer.WriteEndElement();
        }

        writer.WriteEndElement();
    });

    public static byte[] SerializeAccounts(StoreAccountsDocument doc) => Serialize(writer =>
    {
        WriteRootHeader(writer, "accounts", doc.ProfileId, doc.BranchId, doc.Generation);
        foreach (StoreAccountEntry acc in doc.Accounts)
        {
            writer.WriteStartElement("account", ProfileNamespace);
            writer.WriteAttributeString("id", acc.Id.ToString(CultureInfo.InvariantCulture));
            writer.WriteElementString("displayName", ProfileNamespace, acc.DisplayName);
            writer.WriteElementString("currency", ProfileNamespace, acc.Currency);
            writer.WriteEndElement();
        }

        foreach (StoreIncarnationEntry inc in doc.Incarnations)
        {
            writer.WriteStartElement("incarnation", ProfileNamespace);
            writer.WriteAttributeString("id", inc.Id.ToString(CultureInfo.InvariantCulture));
            writer.WriteAttributeString("accountId", inc.AccountId.ToString(CultureInfo.InvariantCulture));
            writer.WriteElementString("lifetimeEvidence", ProfileNamespace, inc.LifetimeEvidence);
            writer.WriteEndElement();
        }

        foreach (StoreBindingEntry bin in doc.Bindings)
        {
            writer.WriteStartElement("binding", ProfileNamespace);
            writer.WriteAttributeString("id", bin.Id.ToString(CultureInfo.InvariantCulture));
            writer.WriteAttributeString("accountId", bin.AccountId.ToString(CultureInfo.InvariantCulture));
            writer.WriteAttributeString("incarnationId", bin.IncarnationId.ToString(CultureInfo.InvariantCulture));
            writer.WriteAttributeString("institutionId", bin.InstitutionId.ToString(CultureInfo.InvariantCulture));
            writer.WriteAttributeString("connectionId", bin.ConnectionId.ToString(CultureInfo.InvariantCulture));
            writer.WriteElementString("state", ProfileNamespace, bin.State);
            writer.WriteStartElement("locator", ProfileNamespace);
            writer.WriteAttributeString("connector", bin.Connector);
            writer.WriteAttributeString("connectorVersion", bin.ConnectorVersion);
            writer.WriteAttributeString("endpoint", bin.Endpoint);
            if (bin.Iban is not null)
            {
                writer.WriteElementString("iban", ProfileNamespace, bin.Iban);
            }

            if (bin.DomesticAccountNumber is not null)
            {
                writer.WriteElementString("domesticAccountNumber", ProfileNamespace, bin.DomesticAccountNumber);
            }

            if (bin.DomesticBankCode is not null)
            {
                writer.WriteElementString("domesticBankCode", ProfileNamespace, bin.DomesticBankCode);
            }

            if (bin.Subaccount is not null)
            {
                writer.WriteElementString("subaccount", ProfileNamespace, bin.Subaccount);
            }

            if (bin.Currency is not null)
            {
                writer.WriteElementString("currency", ProfileNamespace, bin.Currency);
            }

            foreach ((string issuer, string scope, string kind, string val) in bin.BankIdentifiers)
            {
                writer.WriteStartElement("bankIdentifier", ProfileNamespace);
                writer.WriteAttributeString("issuer", issuer);
                writer.WriteAttributeString("scope", scope);
                writer.WriteAttributeString("kind", kind);
                writer.WriteAttributeString("value", val);
                writer.WriteEndElement();
            }

            writer.WriteEndElement(); // locator
            writer.WriteEndElement(); // binding
        }

        foreach (StoreCapabilityEntry cap in doc.Capabilities)
        {
            writer.WriteStartElement("capability", ProfileNamespace);
            writer.WriteAttributeString("connectionId", cap.ConnectionId.ToString(CultureInfo.InvariantCulture));
            writer.WriteAttributeString("accountId", cap.AccountId.ToString(CultureInfo.InvariantCulture));
            writer.WriteAttributeString("operationCode", cap.OperationCode);
            writer.WriteAttributeString("operationVersion", cap.OperationVersion.ToString(CultureInfo.InvariantCulture));
            writer.WriteAttributeString("status", cap.Status);
            if (cap.AdvertisedLimit is not null)
            {
                writer.WriteElementString("advertisedLimit", ProfileNamespace, cap.AdvertisedLimit);
            }

            writer.WriteEndElement();
        }

        writer.WriteEndElement();
    });

    public static byte[] SerializeBalances(StoreBalancesDocument doc) => Serialize(writer =>
    {
        WriteRootHeader(writer, "balances", doc.ProfileId, doc.BranchId, doc.Generation);
        foreach (StoreBalanceEntry bal in doc.Balances)
        {
            writer.WriteStartElement("balance", ProfileNamespace);
            writer.WriteAttributeString("id", bal.Id.ToString(CultureInfo.InvariantCulture));
            writer.WriteAttributeString("observationId", bal.ObservationId.ToString(CultureInfo.InvariantCulture));
            writer.WriteAttributeString("accountId", bal.AccountId.ToString(CultureInfo.InvariantCulture));
            writer.WriteAttributeString("incarnationId", bal.IncarnationId.ToString(CultureInfo.InvariantCulture));
            writer.WriteAttributeString("bindingId", bal.BindingId.ToString(CultureInfo.InvariantCulture));
            writer.WriteElementString("kind", ProfileNamespace, bal.Kind);
            if (bal.Amount is not null)
            {
                writer.WriteElementString("amount", ProfileNamespace, bal.Amount);
            }

            if (bal.Currency is not null)
            {
                writer.WriteElementString("currency", ProfileNamespace, bal.Currency);
            }

            if (bal.BankTimestamp.HasValue)
            {
                writer.WriteElementString("bankTimestamp", ProfileNamespace, bal.BankTimestamp.Value.ToUniversalTime().ToString(TimestampFormat, CultureInfo.InvariantCulture));
            }

            writer.WriteElementString("retrievedAt", ProfileNamespace, bal.RetrievedAt.ToUniversalTime().ToString(TimestampFormat, CultureInfo.InvariantCulture));
            writer.WriteElementString("isStale", ProfileNamespace, bal.IsStale ? "true" : "false");
            if (bal.SourceLabel is not null)
            {
                writer.WriteElementString("sourceLabel", ProfileNamespace, bal.SourceLabel);
            }

            writer.WriteEndElement();
        }

        writer.WriteEndElement();
    });

    public static byte[] SerializeLedger(StoreLedgerDocument doc) => Serialize(writer =>
    {
        WriteRootHeader(writer, "ledger", doc.ProfileId, doc.BranchId, doc.Generation);
        foreach (StoreLedgerRecord rec in doc.Records)
        {
            writer.WriteStartElement("record", ProfileNamespace);
            writer.WriteAttributeString("id", rec.Id.ToString(CultureInfo.InvariantCulture));
            writer.WriteAttributeString("kind", rec.Kind);
            if (rec.SupersedesId.HasValue)
            {
                writer.WriteStartElement("supersedes", ProfileNamespace);
                writer.WriteAttributeString("id", rec.SupersedesId.Value.ToString(CultureInfo.InvariantCulture));
                writer.WriteEndElement();
            }

            foreach ((ulong targetId, string expectedKind) in rec.References)
            {
                writer.WriteStartElement("reference", ProfileNamespace);
                writer.WriteAttributeString("targetId", targetId.ToString(CultureInfo.InvariantCulture));
                writer.WriteAttributeString("expectedKind", expectedKind);
                writer.WriteEndElement();
            }

            writer.WriteEndElement();
        }

        writer.WriteEndElement();
    });

    public static byte[] SerializeTransactions(StoreTransactionsDocument doc) => Serialize(writer =>
    {
        WriteRootHeader(writer, "transactions", doc.ProfileId, doc.BranchId, doc.Generation);
        foreach (StoreTransactionObservationEntry obs in doc.Observations)
        {
            writer.WriteStartElement("observation", ProfileNamespace);
            writer.WriteAttributeString("id", obs.Id.ToString(CultureInfo.InvariantCulture));
            writer.WriteAttributeString("accountBindingId", obs.AccountBindingId.ToString(CultureInfo.InvariantCulture));
            writer.WriteElementString("statementFormat", ProfileNamespace, obs.StatementFormat);
            if (obs.StatementNumber is not null)
            {
                writer.WriteElementString("statementNumber", ProfileNamespace, obs.StatementNumber);
            }

            if (obs.PageToken is not null)
            {
                writer.WriteElementString("pageToken", ProfileNamespace, obs.PageToken);
            }

            writer.WriteElementString("retrievedAt", ProfileNamespace, obs.RetrievedAt.ToUniversalTime().ToString(TimestampFormat, CultureInfo.InvariantCulture));
            writer.WriteEndElement();
        }

        foreach (StoreTransactionEntry tx in doc.Transactions)
        {
            writer.WriteStartElement("transaction", ProfileNamespace);
            writer.WriteAttributeString("id", tx.Id.ToString(CultureInfo.InvariantCulture));
            writer.WriteAttributeString("accountId", tx.AccountId.ToString(CultureInfo.InvariantCulture));
            writer.WriteAttributeString("accountIncarnationId", tx.AccountIncarnationId.ToString(CultureInfo.InvariantCulture));
            writer.WriteElementString("status", ProfileNamespace, tx.Status);
            writer.WriteElementString("kind", ProfileNamespace, tx.Kind);
            writer.WriteEndElement();
        }

        foreach (StoreTransactionRevisionEntry rev in doc.Revisions)
        {
            writer.WriteStartElement("revision", ProfileNamespace);
            writer.WriteAttributeString("id", rev.Id.ToString(CultureInfo.InvariantCulture));
            writer.WriteAttributeString("transactionId", rev.TransactionId.ToString(CultureInfo.InvariantCulture));
            writer.WriteAttributeString("observationId", rev.ObservationId.ToString(CultureInfo.InvariantCulture));
            writer.WriteAttributeString("accountId", rev.AccountId.ToString(CultureInfo.InvariantCulture));
            writer.WriteAttributeString("accountIncarnationId", rev.AccountIncarnationId.ToString(CultureInfo.InvariantCulture));
            writer.WriteAttributeString("accountBindingId", rev.AccountBindingId.ToString(CultureInfo.InvariantCulture));
            writer.WriteElementString("status", ProfileNamespace, rev.Status);
            writer.WriteElementString("kind", ProfileNamespace, rev.Kind);
            writer.WriteElementString("amount", ProfileNamespace, rev.Amount);
            writer.WriteElementString("currency", ProfileNamespace, rev.Currency);
            writer.WriteElementString("isCredit", ProfileNamespace, rev.IsCredit ? "true" : "false");
            writer.WriteElementString("bookingDate", ProfileNamespace, rev.BookingDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));
            if (rev.ValueDate.HasValue)
            {
                writer.WriteElementString("valueDate", ProfileNamespace, rev.ValueDate.Value.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));
            }

            if (rev.BankTimestamp.HasValue)
            {
                writer.WriteElementString("bankTimestamp", ProfileNamespace, rev.BankTimestamp.Value.ToUniversalTime().ToString(TimestampFormat, CultureInfo.InvariantCulture));
            }

            writer.WriteElementString("retrievedAt", ProfileNamespace, rev.RetrievedAt.ToUniversalTime().ToString(TimestampFormat, CultureInfo.InvariantCulture));

            if (rev.CounterpartyName is not null || rev.CounterpartyIban is not null || rev.CounterpartyBic is not null)
            {
                writer.WriteStartElement("counterparty", ProfileNamespace);
                if (rev.CounterpartyName is not null)
                {
                    writer.WriteElementString("name", ProfileNamespace, rev.CounterpartyName);
                }

                if (rev.CounterpartyIban is not null)
                {
                    writer.WriteElementString("iban", ProfileNamespace, rev.CounterpartyIban);
                }

                if (rev.CounterpartyBic is not null)
                {
                    writer.WriteElementString("bic", ProfileNamespace, rev.CounterpartyBic);
                }

                writer.WriteEndElement();
            }

            if (rev.RemittanceText is not null || rev.EndToEndId is not null || rev.MandateId is not null ||
                rev.CreditorId is not null || rev.CustomerReference is not null || rev.BankReference is not null || rev.PurposeCode is not null)
            {
                writer.WriteStartElement("remittance", ProfileNamespace);
                if (rev.RemittanceText is not null)
                {
                    writer.WriteElementString("unstructuredText", ProfileNamespace, rev.RemittanceText);
                }

                if (rev.EndToEndId is not null)
                {
                    writer.WriteElementString("endToEndId", ProfileNamespace, rev.EndToEndId);
                }

                if (rev.MandateId is not null)
                {
                    writer.WriteElementString("mandateId", ProfileNamespace, rev.MandateId);
                }

                if (rev.CreditorId is not null)
                {
                    writer.WriteElementString("creditorId", ProfileNamespace, rev.CreditorId);
                }

                if (rev.CustomerReference is not null)
                {
                    writer.WriteElementString("customerReference", ProfileNamespace, rev.CustomerReference);
                }

                if (rev.BankReference is not null)
                {
                    writer.WriteElementString("bankReference", ProfileNamespace, rev.BankReference);
                }

                if (rev.PurposeCode is not null)
                {
                    writer.WriteElementString("purposeCode", ProfileNamespace, rev.PurposeCode);
                }

                writer.WriteEndElement();
            }

            if (rev.ReturnReasonCode is not null)
            {
                writer.WriteElementString("returnReasonCode", ProfileNamespace, rev.ReturnReasonCode);
            }

            if (rev.RawBankCode is not null)
            {
                writer.WriteElementString("rawBankCode", ProfileNamespace, rev.RawBankCode);
            }

            writer.WriteEndElement();
        }

        foreach (StoreAnnotationRevisionEntry ann in doc.Annotations)
        {
            writer.WriteStartElement("annotation", ProfileNamespace);
            writer.WriteAttributeString("id", ann.Id.ToString(CultureInfo.InvariantCulture));
            writer.WriteAttributeString("transactionId", ann.TransactionId.ToString(CultureInfo.InvariantCulture));
            writer.WriteElementString("revisionTimestamp", ProfileNamespace, ann.RevisionTimestamp.ToUniversalTime().ToString(TimestampFormat, CultureInfo.InvariantCulture));
            if (ann.UserNote is not null)
            {
                writer.WriteElementString("userNote", ProfileNamespace, ann.UserNote);
            }

            if (ann.Category is not null)
            {
                writer.WriteElementString("category", ProfileNamespace, ann.Category);
            }

            foreach (string tag in ann.Tags)
            {
                writer.WriteElementString("tag", ProfileNamespace, tag);
            }

            if (ann.Splits is not null)
            {
                foreach (StoreSplitItemEntry split in ann.Splits.OrderBy(s => s.Index))
                {
                    writer.WriteStartElement("split", ProfileNamespace);
                    writer.WriteAttributeString("index", split.Index.ToString(CultureInfo.InvariantCulture));
                    writer.WriteElementString("amount", ProfileNamespace, split.Amount.ToString("0.00", CultureInfo.InvariantCulture));
                    writer.WriteElementString("category", ProfileNamespace, split.Category);
                    if (split.Note is not null)
                    {
                        writer.WriteElementString("note", ProfileNamespace, split.Note);
                    }

                    foreach (string splitTag in split.Tags)
                    {
                        writer.WriteElementString("tag", ProfileNamespace, splitTag);
                    }

                    writer.WriteEndElement();
                }
            }

            writer.WriteEndElement();
        }

        foreach (StorePendingToBookedLinkEntry link in doc.Links)
        {
            writer.WriteStartElement("link", ProfileNamespace);
            writer.WriteAttributeString("pendingTransactionId", link.PendingTransactionId.ToString(CultureInfo.InvariantCulture));
            writer.WriteAttributeString("bookedTransactionId", link.BookedTransactionId.ToString(CultureInfo.InvariantCulture));
            writer.WriteElementString("linkedAt", ProfileNamespace, link.LinkedAt.ToUniversalTime().ToString(TimestampFormat, CultureInfo.InvariantCulture));
            writer.WriteElementString("matchCriterion", ProfileNamespace, link.MatchCriterion);
            writer.WriteEndElement();
        }

        foreach (StoreStatementSyncCheckpointEntry cp in doc.Checkpoints)
        {
            writer.WriteStartElement("checkpoint", ProfileNamespace);
            writer.WriteAttributeString("accountId", cp.AccountId.ToString(CultureInfo.InvariantCulture));
            writer.WriteAttributeString("accountIncarnationId", cp.AccountIncarnationId.ToString(CultureInfo.InvariantCulture));
            writer.WriteAttributeString("accountBindingId", cp.AccountBindingId.ToString(CultureInfo.InvariantCulture));
            writer.WriteElementString("windowStart", ProfileNamespace, cp.WindowStart.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));
            writer.WriteElementString("windowEnd", ProfileNamespace, cp.WindowEnd.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));
            writer.WriteElementString("lastSyncTimestamp", ProfileNamespace, cp.LastSyncTimestamp.ToUniversalTime().ToString(TimestampFormat, CultureInfo.InvariantCulture));
            if (cp.LastContinuationToken is not null)
            {
                writer.WriteElementString("lastContinuationToken", ProfileNamespace, cp.LastContinuationToken);
            }

            writer.WriteElementString("isComplete", ProfileNamespace, cp.IsComplete ? "true" : "false");
            writer.WriteElementString("observedTransactionCount", ProfileNamespace, cp.ObservedTransactionCount.ToString(CultureInfo.InvariantCulture));
            writer.WriteEndElement();
        }

        writer.WriteEndElement();
    });

    public static byte[] SerializePayments(StorePaymentsDocument doc) => Serialize(writer =>
    {
        WriteRootHeader(writer, "payments", doc.ProfileId, doc.BranchId, doc.Generation);
        foreach (StorePaymentDraftEntry draft in doc.Drafts)
        {
            writer.WriteStartElement("draft", ProfileNamespace);
            writer.WriteAttributeString("id", draft.Id.ToString(CultureInfo.InvariantCulture));
            writer.WriteAttributeString("sourceAccountId", draft.SourceAccountId.ToString(CultureInfo.InvariantCulture));
            writer.WriteAttributeString("sourceAccountBindingId", draft.SourceAccountBindingId.ToString(CultureInfo.InvariantCulture));
            writer.WriteAttributeString("sourceAccountIncarnationId", draft.SourceAccountIncarnationId.ToString(CultureInfo.InvariantCulture));
            writer.WriteElementString("currentRevisionId", ProfileNamespace, draft.CurrentRevisionId.ToString(CultureInfo.InvariantCulture));
            writer.WriteElementString("createdAt", ProfileNamespace, draft.CreatedAt.ToUniversalTime().ToString(TimestampFormat, CultureInfo.InvariantCulture));
            writer.WriteElementString("updatedAt", ProfileNamespace, draft.UpdatedAt.ToUniversalTime().ToString(TimestampFormat, CultureInfo.InvariantCulture));
            writer.WriteEndElement();
        }

        foreach (StorePaymentDraftRevisionEntry rev in doc.Revisions)
        {
            writer.WriteStartElement("revision", ProfileNamespace);
            writer.WriteAttributeString("id", rev.Id.ToString(CultureInfo.InvariantCulture));
            writer.WriteAttributeString("draftId", rev.DraftId.ToString(CultureInfo.InvariantCulture));
            if (rev.PredecessorRevisionId.HasValue)
            {
                writer.WriteAttributeString("predecessorRevisionId", rev.PredecessorRevisionId.Value.ToString(CultureInfo.InvariantCulture));
            }

            writer.WriteElementString("revisionNumber", ProfileNamespace, rev.RevisionNumber.ToString(CultureInfo.InvariantCulture));
            writer.WriteElementString("timestamp", ProfileNamespace, rev.Timestamp.ToUniversalTime().ToString(TimestampFormat, CultureInfo.InvariantCulture));
            writer.WriteElementString("sourceAccountId", ProfileNamespace, rev.SourceAccountId.ToString(CultureInfo.InvariantCulture));
            writer.WriteElementString("sourceAccountBindingId", ProfileNamespace, rev.SourceAccountBindingId.ToString(CultureInfo.InvariantCulture));
            writer.WriteElementString("sourceAccountIncarnationId", ProfileNamespace, rev.SourceAccountIncarnationId.ToString(CultureInfo.InvariantCulture));

            writer.WriteStartElement("beneficiary", ProfileNamespace);
            writer.WriteElementString("name", ProfileNamespace, rev.BeneficiaryName);
            writer.WriteElementString("iban", ProfileNamespace, rev.BeneficiaryIban);
            if (rev.BeneficiaryBic is not null)
            {
                writer.WriteElementString("bic", ProfileNamespace, rev.BeneficiaryBic);
            }
            writer.WriteEndElement();

            writer.WriteElementString("amount", ProfileNamespace, rev.Amount.ToString("0.00", CultureInfo.InvariantCulture));
            writer.WriteElementString("currency", ProfileNamespace, rev.Currency);

            if (rev.RemittanceUnstructured is not null || rev.RemittanceCreditorReference is not null)
            {
                writer.WriteStartElement("remittance", ProfileNamespace);
                if (rev.RemittanceUnstructured is not null)
                {
                    writer.WriteElementString("unstructured", ProfileNamespace, rev.RemittanceUnstructured);
                }
                if (rev.RemittanceCreditorReference is not null)
                {
                    writer.WriteElementString("creditorReference", ProfileNamespace, rev.RemittanceCreditorReference);
                }
                writer.WriteEndElement();
            }

            writer.WriteElementString("executionMode", ProfileNamespace, rev.ExecutionMode);
            writer.WriteElementString("executionDate", ProfileNamespace, rev.ExecutionDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));
            writer.WriteEndElement();
        }

        foreach (StorePaymentAttemptEntry att in doc.Attempts)
        {
            writer.WriteStartElement("attempt", ProfileNamespace);
            writer.WriteAttributeString("id", att.Id.ToString(CultureInfo.InvariantCulture));
            writer.WriteAttributeString("draftId", att.DraftId.ToString(CultureInfo.InvariantCulture));
            writer.WriteAttributeString("draftRevisionId", att.DraftRevisionId.ToString(CultureInfo.InvariantCulture));
            writer.WriteElementString("sourceAccountId", ProfileNamespace, att.SourceAccountId.ToString(CultureInfo.InvariantCulture));
            writer.WriteElementString("sourceAccountBindingId", ProfileNamespace, att.SourceAccountBindingId.ToString(CultureInfo.InvariantCulture));
            writer.WriteElementString("sourceAccountIncarnationId", ProfileNamespace, att.SourceAccountIncarnationId.ToString(CultureInfo.InvariantCulture));
            writer.WriteElementString("endToEndId", ProfileNamespace, att.EndToEndId);
            writer.WriteElementString("state", ProfileNamespace, att.State);

            if (att.VoPResult is not null)
            {
                writer.WriteStartElement("vopResult", ProfileNamespace);
                writer.WriteElementString("status", ProfileNamespace, att.VoPResult.Status);
                writer.WriteElementString("checkedAt", ProfileNamespace, att.VoPResult.CheckedAt.ToUniversalTime().ToString(TimestampFormat, CultureInfo.InvariantCulture));
                if (att.VoPResult.MatchedName is not null)
                {
                    writer.WriteElementString("matchedName", ProfileNamespace, att.VoPResult.MatchedName);
                }
                if (att.VoPResult.ReasonCode is not null)
                {
                    writer.WriteElementString("reasonCode", ProfileNamespace, att.VoPResult.ReasonCode);
                }
                if (att.VoPResult.WarningMessage is not null)
                {
                    writer.WriteElementString("warningMessage", ProfileNamespace, att.VoPResult.WarningMessage);
                }
                writer.WriteElementString("userOverride", ProfileNamespace, att.VoPResult.UserOverride ? "true" : "false");
                writer.WriteEndElement();
            }

            if (att.BankReference is not null)
            {
                writer.WriteElementString("bankReference", ProfileNamespace, att.BankReference);
            }
            if (att.ResponseCode is not null)
            {
                writer.WriteElementString("responseCode", ProfileNamespace, att.ResponseCode);
            }
            if (att.StatusDescription is not null)
            {
                writer.WriteElementString("statusDescription", ProfileNamespace, att.StatusDescription);
            }
            writer.WriteElementString("createdAt", ProfileNamespace, att.CreatedAt.ToUniversalTime().ToString(TimestampFormat, CultureInfo.InvariantCulture));
            writer.WriteElementString("updatedAt", ProfileNamespace, att.UpdatedAt.ToUniversalTime().ToString(TimestampFormat, CultureInfo.InvariantCulture));
            writer.WriteEndElement();
        }

        foreach (StorePaymentStateTransitionEntry tr in doc.Transitions)
        {
            writer.WriteStartElement("transition", ProfileNamespace);
            writer.WriteAttributeString("id", tr.Id.ToString(CultureInfo.InvariantCulture));
            writer.WriteAttributeString("attemptId", tr.AttemptId.ToString(CultureInfo.InvariantCulture));
            writer.WriteElementString("fromState", ProfileNamespace, tr.FromState);
            writer.WriteElementString("toState", ProfileNamespace, tr.ToState);
            writer.WriteElementString("timestamp", ProfileNamespace, tr.Timestamp.ToUniversalTime().ToString(TimestampFormat, CultureInfo.InvariantCulture));
            writer.WriteElementString("reason", ProfileNamespace, tr.Reason);
            if (tr.BankCode is not null)
            {
                writer.WriteElementString("bankCode", ProfileNamespace, tr.BankCode);
            }
            writer.WriteEndElement();
        }

        writer.WriteEndElement();
    });

    public static byte[] SerializeTemplates(StoreTemplatesDocument doc) => Serialize(writer =>
    {
        WriteRootHeader(writer, "templates", doc.ProfileId, doc.BranchId, doc.Generation);
        foreach (StoreBeneficiaryTemplateEntry tmpl in doc.Templates)
        {
            writer.WriteStartElement("template", ProfileNamespace);
            writer.WriteAttributeString("id", tmpl.Id.ToString(CultureInfo.InvariantCulture));
            writer.WriteAttributeString("isSoftDeleted", tmpl.IsSoftDeleted ? "true" : "false");
            writer.WriteElementString("label", ProfileNamespace, tmpl.Label);

            writer.WriteStartElement("beneficiary", ProfileNamespace);
            writer.WriteElementString("name", ProfileNamespace, tmpl.BeneficiaryName);
            writer.WriteElementString("iban", ProfileNamespace, tmpl.BeneficiaryIban);
            if (tmpl.BeneficiaryBic is not null)
            {
                writer.WriteElementString("bic", ProfileNamespace, tmpl.BeneficiaryBic);
            }

            writer.WriteEndElement(); // beneficiary

            if (tmpl.DefaultRemittanceUnstructured is not null || tmpl.DefaultRemittanceCreditorReference is not null)
            {
                writer.WriteStartElement("defaultRemittance", ProfileNamespace);
                if (tmpl.DefaultRemittanceUnstructured is not null)
                {
                    writer.WriteElementString("unstructured", ProfileNamespace, tmpl.DefaultRemittanceUnstructured);
                }

                if (tmpl.DefaultRemittanceCreditorReference is not null)
                {
                    writer.WriteElementString("creditorReference", ProfileNamespace, tmpl.DefaultRemittanceCreditorReference);
                }

                writer.WriteEndElement(); // defaultRemittance
            }

            if (tmpl.DefaultAmount.HasValue)
            {
                writer.WriteElementString("defaultAmount", ProfileNamespace, tmpl.DefaultAmount.Value.ToString("0.00", CultureInfo.InvariantCulture));
            }

            if (tmpl.DefaultCurrency is not null)
            {
                writer.WriteElementString("defaultCurrency", ProfileNamespace, tmpl.DefaultCurrency);
            }

            if (tmpl.DefaultExecutionMode is not null)
            {
                writer.WriteElementString("defaultExecutionMode", ProfileNamespace, tmpl.DefaultExecutionMode);
            }

            writer.WriteElementString("createdAt", ProfileNamespace, tmpl.CreatedAt.ToUniversalTime().ToString(TimestampFormat, CultureInfo.InvariantCulture));
            writer.WriteElementString("updatedAt", ProfileNamespace, tmpl.UpdatedAt.ToUniversalTime().ToString(TimestampFormat, CultureInfo.InvariantCulture));
            writer.WriteEndElement(); // template
        }

        writer.WriteEndElement(); // templates
    });

    public static byte[] SerializeManifest(
        Guid profileId,
        Guid branchId,
        ulong generation,
        IReadOnlyList<(string Name, byte[] Bytes)> entries) => Serialize(writer =>
    {
        WriteRootHeader(writer, "manifest", profileId, branchId, generation);
        foreach ((string name, byte[] bytes) in entries)
        {
            writer.WriteStartElement("entry", ProfileNamespace);
            writer.WriteAttributeString("name", name);
            writer.WriteAttributeString("schema", "1");
            writer.WriteAttributeString("bytes", bytes.Length.ToString(CultureInfo.InvariantCulture));
            writer.WriteAttributeString("sha256", Convert.ToHexStringLower(SHA256.HashData(bytes)));
            writer.WriteEndElement();
        }

        writer.WriteEndElement();
    });

    public static byte[] SerializePurgeIntent(StorePurgeIntent intent) => Serialize(writer =>
    {
        writer.WriteStartElement("purgeIntent", PurgeNamespace);
        writer.WriteAttributeString("xmlns", PurgeNamespace);
        writer.WriteAttributeString("version", "1");
        writer.WriteAttributeString("profileId", intent.ProfileId.ToString("D"));
        writer.WriteAttributeString("branchId", intent.BranchId.ToString("D"));
        writer.WriteAttributeString("generation", intent.Generation.ToString(CultureInfo.InvariantCulture));
        writer.WriteAttributeString("operationType", intent.OperationType);
        writer.WriteAttributeString("status", intent.Status);
        writer.WriteAttributeString("createdAt", intent.CreatedAt.ToUniversalTime().ToString(TimestampFormat, CultureInfo.InvariantCulture));
        if (intent.ReplacementGeneration.HasValue)
        {
            writer.WriteElementString("replacementGeneration", PurgeNamespace, intent.ReplacementGeneration.Value.ToString(CultureInfo.InvariantCulture));
        }

        writer.WriteStartElement("targetInventory", PurgeNamespace);
        foreach ((string name, string hash) in intent.TargetInventory)
        {
            writer.WriteStartElement("targetFile", PurgeNamespace);
            writer.WriteAttributeString("name", name);
            writer.WriteAttributeString("sha256", hash);
            writer.WriteEndElement();
        }

        writer.WriteEndElement(); // targetInventory
        if (intent.Note is not null)
        {
            writer.WriteElementString("note", PurgeNamespace, intent.Note);
        }

        writer.WriteEndElement();
    });

    private static void WriteRootHeader(XmlWriter writer, string name, Guid profileId, Guid branchId, ulong generation)
    {
        writer.WriteStartElement(name, ProfileNamespace);
        writer.WriteAttributeString("xmlns", ProfileNamespace);
        writer.WriteAttributeString("version", "1");
        writer.WriteAttributeString("profileId", profileId.ToString("D"));
        writer.WriteAttributeString("branchId", branchId.ToString("D"));
        writer.WriteAttributeString("generation", generation.ToString(CultureInfo.InvariantCulture));
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

    // ==========================================
    // 2. ZIP Creation & Extraction
    // ==========================================

    public static byte[] CreateStoreZip(StoreSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        byte[] profileBytes = SerializeProfile(snapshot.Profile);
        byte[] instBytes = SerializeInstitutions(snapshot.Institutions);
        byte[] connBytes = SerializeConnections(snapshot.Connections);
        byte[] accBytes = SerializeAccounts(snapshot.Accounts);
        byte[] balBytes = SerializeBalances(snapshot.Balances);
        byte[] ledBytes = SerializeLedger(snapshot.Ledger);
        byte[] txBytes = SerializeTransactions(snapshot.TransactionsOrDefault);
        byte[] payBytes = SerializePayments(snapshot.PaymentsOrDefault);
        byte[] tmplBytes = SerializeTemplates(snapshot.TemplatesOrDefault);

        List<(string Name, byte[] Bytes)> manifestEntries =
        [
            ("profile.xml", profileBytes),
            ("institutions.xml", instBytes),
            ("connections.xml", connBytes),
            ("accounts.xml", accBytes),
            ("balances.xml", balBytes),
            ("ledger.xml", ledBytes),
            ("transactions.xml", txBytes),
            ("payments.xml", payBytes),
            ("templates.xml", tmplBytes),
        ];

        byte[] manifestBytes = SerializeManifest(snapshot.Profile.ProfileId, snapshot.Profile.BranchId, snapshot.Profile.Generation, manifestEntries);

        return CreateZip(
            ("profile.xml", profileBytes),
            ("institutions.xml", instBytes),
            ("connections.xml", connBytes),
            ("accounts.xml", accBytes),
            ("balances.xml", balBytes),
            ("ledger.xml", ledBytes),
            ("transactions.xml", txBytes),
            ("payments.xml", payBytes),
            ("templates.xml", tmplBytes),
            ("manifest.xml", manifestBytes));
    }

    public static byte[] CreateZip(params (string Name, byte[] Bytes)[] documents)
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

    // ==========================================
    // 3. Validation & Parsing
    // ==========================================

    public static StoreSnapshot ValidateAndParseStoreZip(ReadOnlySpan<byte> payload, ZipPreflightLimits? limits = null)
    {
        ZipPreflightResult preflight = ZipPreflight.Validate(payload, limits);
        ValidatePreflightEntryNames(preflight);

        byte[] copy = payload.ToArray();
        try
        {
            using MemoryStream input = new(copy, writable: false);
            return ExtractAndValidateSnapshotFromZipStream(input);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(copy);
        }
    }

    public static StoreSnapshot ValidateAndParseStoreZipFromStream(Stream authenticatedStream, ZipPreflightLimits? limits = null)
    {
        ArgumentNullException.ThrowIfNull(authenticatedStream);
        ZipPreflightResult preflight = ZipPreflight.ValidateStream(authenticatedStream, limits);
        ValidatePreflightEntryNames(preflight);

        authenticatedStream.Position = 0;
        return ExtractAndValidateSnapshotFromZipStream(authenticatedStream);
    }

    private static void ValidatePreflightEntryNames(ZipPreflightResult preflight)
    {
        IReadOnlyList<string> expected = preflight.Entries.Count switch
        {
            7 => LegacyStoreDocumentNames,
            8 => StoreDocumentNamesV2,
            9 => StoreDocumentNamesV3,
            10 => StoreDocumentNames,
            _ => throw new InvalidDataException($"Expected 7, 8, 9, or 10 entries in store ZIP, found {preflight.Entries.Count}.")
        };

        for (int i = 0; i < expected.Count; i++)
        {
            if (preflight.Entries[i].Name != expected[i])
            {
                throw new InvalidDataException($"Expected entry '{expected[i]}' at index {i}, found '{preflight.Entries[i].Name}'.");
            }
        }
    }

    private static StoreSnapshot ExtractAndValidateSnapshotFromZipStream(Stream stream)
    {
        Dictionary<string, byte[]> extracted = [];
        try
        {
            using (ZipArchive zip = new(stream, ZipArchiveMode.Read, leaveOpen: true, entryNameEncoding: StrictUtf8))
            {
                foreach (ZipArchiveEntry entry in zip.Entries)
                {
                    using Stream s = entry.Open();
                    byte[] buffer = new byte[(int)entry.Length];
                    s.ReadExactly(buffer);
                    extracted[entry.FullName] = buffer;
                }
            }

            StoreProfileDocument profile = ParseAndValidateProfile(extracted["profile.xml"]);
            StoreInstitutionsDocument inst = ParseAndValidateInstitutions(extracted["institutions.xml"]);
            StoreConnectionsDocument conn = ParseAndValidateConnections(extracted["connections.xml"]);
            StoreAccountsDocument acc = ParseAndValidateAccounts(extracted["accounts.xml"]);
            StoreBalancesDocument bal = ParseAndValidateBalances(extracted["balances.xml"]);
            StoreLedgerDocument led = ParseAndValidateLedger(extracted["ledger.xml"]);

            Guid pid = profile.ProfileId;
            Guid bid = profile.BranchId;
            ulong gen = profile.Generation;

            void CheckLineage(Guid p, Guid b, ulong g, string docName)
            {
                if (p != pid || b != bid || g != gen)
                {
                    throw new InvalidDataException($"Lineage mismatch in {docName}: expected ({pid}, {bid}, {gen}), found ({p}, {b}, {g}).");
                }
            }

            CheckLineage(inst.ProfileId, inst.BranchId, inst.Generation, "institutions.xml");
            CheckLineage(conn.ProfileId, conn.BranchId, conn.Generation, "connections.xml");
            CheckLineage(acc.ProfileId, acc.BranchId, acc.Generation, "accounts.xml");
            CheckLineage(bal.ProfileId, bal.BranchId, bal.Generation, "balances.xml");
            CheckLineage(led.ProfileId, led.BranchId, led.Generation, "ledger.xml");

            StoreTransactionsDocument transactionsDoc;
            if (extracted.TryGetValue("transactions.xml", out byte[]? txBytes))
            {
                transactionsDoc = ParseAndValidateTransactions(txBytes);
                CheckLineage(transactionsDoc.ProfileId, transactionsDoc.BranchId, transactionsDoc.Generation, "transactions.xml");
                if (!txBytes.AsSpan().SequenceEqual(SerializeTransactions(transactionsDoc)))
                {
                    throw new InvalidDataException("Transactions XML failed byte-exact serialization comparison.");
                }
            }
            else
            {
                transactionsDoc = new StoreTransactionsDocument(pid, bid, gen, [], [], [], [], [], []);
            }

            StorePaymentsDocument paymentsDoc;
            if (extracted.TryGetValue("payments.xml", out byte[]? payBytes))
            {
                paymentsDoc = ParseAndValidatePayments(payBytes);
                CheckLineage(paymentsDoc.ProfileId, paymentsDoc.BranchId, paymentsDoc.Generation, "payments.xml");
                if (!payBytes.AsSpan().SequenceEqual(SerializePayments(paymentsDoc)))
                {
                    throw new InvalidDataException("Payments XML failed byte-exact serialization comparison.");
                }
            }
            else
            {
                paymentsDoc = new StorePaymentsDocument(pid, bid, gen, [], [], [], []);
            }

            StoreTemplatesDocument templatesDoc;
            if (extracted.TryGetValue("templates.xml", out byte[]? tmplBytes))
            {
                templatesDoc = ParseAndValidateTemplates(tmplBytes);
                CheckLineage(templatesDoc.ProfileId, templatesDoc.BranchId, templatesDoc.Generation, "templates.xml");
                if (!tmplBytes.AsSpan().SequenceEqual(SerializeTemplates(templatesDoc)))
                {
                    throw new InvalidDataException("Templates XML failed byte-exact serialization comparison.");
                }
            }
            else
            {
                templatesDoc = new StoreTemplatesDocument(pid, bid, gen, []);
            }

            ValidateManifest(extracted["manifest.xml"], pid, bid, gen, extracted);

            StoreSnapshot snapshot = new(profile, inst, conn, acc, bal, led, transactionsDoc, paymentsDoc, templatesDoc);
            if (!extracted["profile.xml"].AsSpan().SequenceEqual(SerializeProfile(profile)))
            {
                throw new InvalidDataException("Profile XML failed byte-exact serialization comparison.");
            }

            if (!extracted["institutions.xml"].AsSpan().SequenceEqual(SerializeInstitutions(inst)))
            {
                throw new InvalidDataException("Institutions XML failed byte-exact serialization comparison.");
            }

            if (!extracted["connections.xml"].AsSpan().SequenceEqual(SerializeConnections(conn)))
            {
                throw new InvalidDataException("Connections XML failed byte-exact serialization comparison.");
            }

            if (!extracted["accounts.xml"].AsSpan().SequenceEqual(SerializeAccounts(acc)))
            {
                throw new InvalidDataException("Accounts XML failed byte-exact serialization comparison.");
            }

            if (!extracted["balances.xml"].AsSpan().SequenceEqual(SerializeBalances(bal)))
            {
                throw new InvalidDataException("Balances XML failed byte-exact serialization comparison.");
            }

            if (!extracted["ledger.xml"].AsSpan().SequenceEqual(SerializeLedger(led)))
            {
                throw new InvalidDataException("Ledger XML failed byte-exact serialization comparison.");
            }

            return snapshot;
        }
        finally
        {
            foreach (byte[] docBytes in extracted.Values)
            {
                CryptographicOperations.ZeroMemory(docBytes);
            }
        }
    }

    public static IdentitySnapshot ToIdentitySnapshot(StoreSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        List<LocalEntityRecord> records = new(snapshot.Ledger.Records.Count);
        foreach (StoreLedgerRecord rec in snapshot.Ledger.Records)
        {
            LocalEntityKind kind = ParseEntityKindOrZero(rec.Kind);
            List<LocalEntityReference> refs = new(rec.References.Count);
            foreach ((ulong targetId, string expectedKindStr) in rec.References)
            {
                LocalEntityKind expectedKind = ParseEntityKindOrZero(expectedKindStr);
                refs.Add(new LocalEntityReference(new LocalEntityId(targetId), expectedKind));
            }

            LocalEntityId? supersedes = rec.SupersedesId.HasValue
                ? new LocalEntityId(rec.SupersedesId.Value)
                : null;
            records.Add(new LocalEntityRecord(new LocalEntityId(rec.Id), kind, refs, supersedes));
        }

        return new IdentitySnapshot(snapshot.Profile.NextId, records);
    }

    /// <summary>
    /// Validates both the LocalEntityLedger invariants (unique IDs, NextId > Max(Id), reference integrity,
    /// acyclic revisions) and cross-document entity membership across institutions, connections, accounts, and balances.
    /// </summary>
    public static void ValidateIdentityAndSemanticInvariants(StoreSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        IdentitySnapshot identitySnapshot = ToIdentitySnapshot(snapshot);
        IdentityValidationResult validation = IdentitySnapshotValidator.Validate(identitySnapshot);
        if (!validation.IsValid)
        {
            IdentityValidationIssue first = validation.Issues[0];
            throw new InvalidDataException(
                $"Identity ledger validation failed with {validation.Issues.Count} issue(s); first issue: {first.Code} (entity {first.EntityId?.Value.ToString(CultureInfo.InvariantCulture) ?? "none"}).");
        }

        Dictionary<ulong, LocalEntityRecord> ledgerById = new(identitySnapshot.Records.Count);
        foreach (LocalEntityRecord rec in identitySnapshot.Records)
        {
            ledgerById[rec.Id.Value] = rec;
        }

        void RequireLedgerEntity(ulong id, LocalEntityKind expectedKind, string context)
        {
            if (!ledgerById.TryGetValue(id, out LocalEntityRecord? rec))
            {
                throw new InvalidDataException($"Entity ID {id} in {context} is missing from ledger.xml.");
            }

            if (rec.Kind != expectedKind)
            {
                throw new InvalidDataException($"Entity ID {id} in {context} has ledger kind '{rec.Kind}', expected '{expectedKind}'.");
            }
        }

        HashSet<ulong> seenInstitutions = [];
        foreach (StoreInstitutionEntry inst in snapshot.Institutions.Institutions)
        {
            if (!seenInstitutions.Add(inst.Id))
            {
                throw new InvalidDataException($"Duplicate institution ID {inst.Id} in institutions.xml.");
            }

            RequireLedgerEntity(inst.Id, LocalEntityKind.Institution, "institutions.xml");
        }

        HashSet<ulong> seenConnections = [];
        foreach (StoreConnectionEntry conn in snapshot.Connections.Connections)
        {
            if (!seenConnections.Add(conn.Id))
            {
                throw new InvalidDataException($"Duplicate connection ID {conn.Id} in connections.xml.");
            }

            RequireLedgerEntity(conn.Id, LocalEntityKind.Connection, "connections.xml");
            RequireLedgerEntity(conn.InstitutionId, LocalEntityKind.Institution, "connections.xml institutionId");
            if (!seenInstitutions.Contains(conn.InstitutionId))
            {
                throw new InvalidDataException($"Connection {conn.Id} references institution {conn.InstitutionId} absent from institutions.xml.");
            }
        }

        HashSet<ulong> seenAccounts = [];
        foreach (StoreAccountEntry acc in snapshot.Accounts.Accounts)
        {
            if (!seenAccounts.Add(acc.Id))
            {
                throw new InvalidDataException($"Duplicate account ID {acc.Id} in accounts.xml.");
            }

            RequireLedgerEntity(acc.Id, LocalEntityKind.Account, "accounts.xml account");
        }

        HashSet<ulong> seenIncarnations = [];
        foreach (StoreIncarnationEntry inc in snapshot.Accounts.Incarnations)
        {
            if (!seenIncarnations.Add(inc.Id))
            {
                throw new InvalidDataException($"Duplicate incarnation ID {inc.Id} in accounts.xml.");
            }

            RequireLedgerEntity(inc.Id, LocalEntityKind.AccountIncarnation, "accounts.xml incarnation");
            RequireLedgerEntity(inc.AccountId, LocalEntityKind.Account, "accounts.xml incarnation accountId");
            if (!seenAccounts.Contains(inc.AccountId))
            {
                throw new InvalidDataException($"Incarnation {inc.Id} references account {inc.AccountId} absent from accounts.xml.");
            }
        }

        HashSet<ulong> seenBindings = [];
        foreach (StoreBindingEntry bin in snapshot.Accounts.Bindings)
        {
            if (!seenBindings.Add(bin.Id))
            {
                throw new InvalidDataException($"Duplicate binding ID {bin.Id} in accounts.xml.");
            }

            RequireLedgerEntity(bin.Id, LocalEntityKind.AccountBinding, "accounts.xml binding");
            RequireLedgerEntity(bin.AccountId, LocalEntityKind.Account, "accounts.xml binding accountId");
            RequireLedgerEntity(bin.IncarnationId, LocalEntityKind.AccountIncarnation, "accounts.xml binding incarnationId");
            RequireLedgerEntity(bin.InstitutionId, LocalEntityKind.Institution, "accounts.xml binding institutionId");
            RequireLedgerEntity(bin.ConnectionId, LocalEntityKind.Connection, "accounts.xml binding connectionId");
        }

        foreach (StoreCapabilityEntry cap in snapshot.Accounts.Capabilities)
        {
            RequireLedgerEntity(cap.ConnectionId, LocalEntityKind.Connection, "accounts.xml capability connectionId");
            RequireLedgerEntity(cap.AccountId, LocalEntityKind.Account, "accounts.xml capability accountId");
        }

        HashSet<ulong> seenBalances = [];
        foreach (StoreBalanceEntry bal in snapshot.Balances.Balances)
        {
            if (!seenBalances.Add(bal.Id))
            {
                throw new InvalidDataException($"Duplicate balance ID {bal.Id} in balances.xml.");
            }

            RequireLedgerEntity(bal.Id, LocalEntityKind.BalanceSnapshot, "balances.xml balance");
            if (bal.ObservationId == 0 || bal.ObservationId >= snapshot.Profile.NextId)
            {
                throw new InvalidDataException($"Balance {bal.Id} has observationId {bal.ObservationId} outside valid ledger counter bounds (1..{snapshot.Profile.NextId - 1}).");
            }

            if (ledgerById.TryGetValue(bal.ObservationId, out LocalEntityRecord? obsRec) && obsRec.Kind != LocalEntityKind.Observation)
            {
                throw new InvalidDataException($"Observation ID {bal.ObservationId} in balances.xml has ledger kind '{obsRec.Kind}', expected '{LocalEntityKind.Observation}'.");
            }

            RequireLedgerEntity(bal.AccountId, LocalEntityKind.Account, "balances.xml accountId");
            RequireLedgerEntity(bal.IncarnationId, LocalEntityKind.AccountIncarnation, "balances.xml incarnationId");
            RequireLedgerEntity(bal.BindingId, LocalEntityKind.AccountBinding, "balances.xml bindingId");
        }

        if (snapshot.Transactions != null)
        {
            HashSet<ulong> seenObs = [];
            foreach (StoreTransactionObservationEntry obs in snapshot.Transactions.Observations)
            {
                if (!seenObs.Add(obs.Id))
                {
                    throw new InvalidDataException($"Duplicate observation ID {obs.Id} in transactions.xml.");
                }

                RequireLedgerEntity(obs.Id, LocalEntityKind.Observation, "transactions.xml observation");
                RequireLedgerEntity(obs.AccountBindingId, LocalEntityKind.AccountBinding, "transactions.xml observation accountBindingId");
            }

            HashSet<ulong> seenTx = [];
            foreach (StoreTransactionEntry tx in snapshot.Transactions.Transactions)
            {
                if (!seenTx.Add(tx.Id))
                {
                    throw new InvalidDataException($"Duplicate transaction ID {tx.Id} in transactions.xml.");
                }

                RequireLedgerEntity(tx.Id, LocalEntityKind.Transaction, "transactions.xml transaction");
                RequireLedgerEntity(tx.AccountId, LocalEntityKind.Account, "transactions.xml transaction accountId");
                RequireLedgerEntity(tx.AccountIncarnationId, LocalEntityKind.AccountIncarnation, "transactions.xml transaction accountIncarnationId");
            }

            HashSet<ulong> seenRevs = [];
            foreach (StoreTransactionRevisionEntry rev in snapshot.Transactions.Revisions)
            {
                if (!seenRevs.Add(rev.Id))
                {
                    throw new InvalidDataException($"Duplicate transaction revision ID {rev.Id} in transactions.xml.");
                }

                RequireLedgerEntity(rev.Id, LocalEntityKind.TransactionRevision, "transactions.xml revision");
                RequireLedgerEntity(rev.TransactionId, LocalEntityKind.Transaction, "transactions.xml revision transactionId");
                RequireLedgerEntity(rev.ObservationId, LocalEntityKind.Observation, "transactions.xml revision observationId");
                RequireLedgerEntity(rev.AccountId, LocalEntityKind.Account, "transactions.xml revision accountId");
                RequireLedgerEntity(rev.AccountIncarnationId, LocalEntityKind.AccountIncarnation, "transactions.xml revision accountIncarnationId");
                RequireLedgerEntity(rev.AccountBindingId, LocalEntityKind.AccountBinding, "transactions.xml revision accountBindingId");
            }

            HashSet<ulong> seenAnns = [];
            foreach (StoreAnnotationRevisionEntry ann in snapshot.Transactions.Annotations)
            {
                if (!seenAnns.Add(ann.Id))
                {
                    throw new InvalidDataException($"Duplicate annotation revision ID {ann.Id} in transactions.xml.");
                }

                RequireLedgerEntity(ann.Id, LocalEntityKind.AnnotationRevision, "transactions.xml annotation");
                RequireLedgerEntity(ann.TransactionId, LocalEntityKind.Transaction, "transactions.xml annotation transactionId");
            }

            foreach (StorePendingToBookedLinkEntry link in snapshot.Transactions.Links)
            {
                RequireLedgerEntity(link.PendingTransactionId, LocalEntityKind.Transaction, "transactions.xml link pendingTransactionId");
                RequireLedgerEntity(link.BookedTransactionId, LocalEntityKind.Transaction, "transactions.xml link bookedTransactionId");
            }

            foreach (StoreStatementSyncCheckpointEntry cp in snapshot.Transactions.Checkpoints)
            {
                RequireLedgerEntity(cp.AccountId, LocalEntityKind.Account, "transactions.xml checkpoint accountId");
                RequireLedgerEntity(cp.AccountIncarnationId, LocalEntityKind.AccountIncarnation, "transactions.xml checkpoint accountIncarnationId");
                RequireLedgerEntity(cp.AccountBindingId, LocalEntityKind.AccountBinding, "transactions.xml checkpoint accountBindingId");
            }
        }

        if (snapshot.Payments != null)
        {
            HashSet<ulong> seenDrafts = [];
            foreach (StorePaymentDraftEntry draft in snapshot.Payments.Drafts)
            {
                if (!seenDrafts.Add(draft.Id))
                {
                    throw new InvalidDataException($"Duplicate payment draft ID {draft.Id} in payments.xml.");
                }

                RequireLedgerEntity(draft.Id, LocalEntityKind.PaymentDraft, "payments.xml draft");
                RequireLedgerEntity(draft.SourceAccountId, LocalEntityKind.Account, "payments.xml draft sourceAccountId");
                RequireLedgerEntity(draft.SourceAccountIncarnationId, LocalEntityKind.AccountIncarnation, "payments.xml draft sourceAccountIncarnationId");
                RequireLedgerEntity(draft.SourceAccountBindingId, LocalEntityKind.AccountBinding, "payments.xml draft sourceAccountBindingId");
                RequireLedgerEntity(draft.CurrentRevisionId, LocalEntityKind.PaymentDraftRevision, "payments.xml draft currentRevisionId");
            }

            HashSet<ulong> seenRevs = [];
            foreach (StorePaymentDraftRevisionEntry rev in snapshot.Payments.Revisions)
            {
                if (!seenRevs.Add(rev.Id))
                {
                    throw new InvalidDataException($"Duplicate payment revision ID {rev.Id} in payments.xml.");
                }

                RequireLedgerEntity(rev.Id, LocalEntityKind.PaymentDraftRevision, "payments.xml revision");
                RequireLedgerEntity(rev.DraftId, LocalEntityKind.PaymentDraft, "payments.xml revision draftId");
                if (rev.PredecessorRevisionId.HasValue)
                {
                    RequireLedgerEntity(rev.PredecessorRevisionId.Value, LocalEntityKind.PaymentDraftRevision, "payments.xml revision predecessorRevisionId");
                }
                RequireLedgerEntity(rev.SourceAccountId, LocalEntityKind.Account, "payments.xml revision sourceAccountId");
                RequireLedgerEntity(rev.SourceAccountIncarnationId, LocalEntityKind.AccountIncarnation, "payments.xml revision sourceAccountIncarnationId");
                RequireLedgerEntity(rev.SourceAccountBindingId, LocalEntityKind.AccountBinding, "payments.xml revision sourceAccountBindingId");
            }

            HashSet<ulong> seenAttempts = [];
            foreach (StorePaymentAttemptEntry att in snapshot.Payments.Attempts)
            {
                if (!seenAttempts.Add(att.Id))
                {
                    throw new InvalidDataException($"Duplicate payment attempt ID {att.Id} in payments.xml.");
                }

                RequireLedgerEntity(att.Id, LocalEntityKind.PaymentAttempt, "payments.xml attempt");
                RequireLedgerEntity(att.DraftId, LocalEntityKind.PaymentDraft, "payments.xml attempt draftId");
                RequireLedgerEntity(att.DraftRevisionId, LocalEntityKind.PaymentDraftRevision, "payments.xml attempt draftRevisionId");
                RequireLedgerEntity(att.SourceAccountId, LocalEntityKind.Account, "payments.xml attempt sourceAccountId");
                RequireLedgerEntity(att.SourceAccountIncarnationId, LocalEntityKind.AccountIncarnation, "payments.xml attempt sourceAccountIncarnationId");
                RequireLedgerEntity(att.SourceAccountBindingId, LocalEntityKind.AccountBinding, "payments.xml attempt sourceAccountBindingId");
            }

            HashSet<ulong> seenTransitions = [];
            foreach (StorePaymentStateTransitionEntry tr in snapshot.Payments.Transitions)
            {
                if (!seenTransitions.Add(tr.Id))
                {
                    throw new InvalidDataException($"Duplicate payment transition ID {tr.Id} in payments.xml.");
                }

                RequireLedgerEntity(tr.Id, LocalEntityKind.PaymentStateTransition, "payments.xml transition");
                RequireLedgerEntity(tr.AttemptId, LocalEntityKind.PaymentAttempt, "payments.xml transition attemptId");
            }
        }

        if (snapshot.Templates != null)
        {
            HashSet<ulong> seenTemplates = [];
            foreach (StoreBeneficiaryTemplateEntry tmpl in snapshot.Templates.Templates)
            {
                if (!seenTemplates.Add(tmpl.Id))
                {
                    throw new InvalidDataException($"Duplicate template ID {tmpl.Id} in templates.xml.");
                }

                RequireLedgerEntity(tmpl.Id, LocalEntityKind.BeneficiaryTemplate, "templates.xml template");
            }
        }
    }

    /// <summary>
    /// Merges an imported snapshot into a non-empty target snapshot by deterministically remapping every
    /// imported entity ID into the target's NextId space and rewriting all cross-references.
    /// </summary>
    public static StoreSnapshot MergeSnapshotsWithIdRemapping(
        StoreSnapshot targetSnapshot,
        StoreSnapshot importedSnapshot,
        Guid targetProfileId,
        Guid targetBranchId,
        ulong targetGeneration,
        DateTimeOffset savedAt,
        bool rejectForkConflicts = false)
    {
        ArgumentNullException.ThrowIfNull(targetSnapshot);
        ArgumentNullException.ThrowIfNull(importedSnapshot);

        ValidateIdentityAndSemanticInvariants(targetSnapshot);
        ValidateIdentityAndSemanticInvariants(importedSnapshot);

        if (rejectForkConflicts)
        {
            LineageForkReport forkReport = LineageForkDetector.Detect(targetSnapshot, importedSnapshot);
            if (forkReport.HasConflicts)
            {
                throw new InvalidOperationException($"Lineage fork conflict detected: {forkReport.Conflicts[0].Message}");
            }
        }

        ulong nextAllocatedId = targetSnapshot.Profile.NextId;
        Dictionary<ulong, ulong> idMap = new(importedSnapshot.Ledger.Records.Count);
        foreach (StoreLedgerRecord rec in importedSnapshot.Ledger.Records.OrderBy(r => r.Id))
        {
            if (nextAllocatedId == ulong.MaxValue)
            {
                throw new IdentityAllocationExhaustedException();
            }

            idMap[rec.Id] = nextAllocatedId++;
        }

        foreach (StoreBalanceEntry bal in importedSnapshot.Balances.Balances.OrderBy(b => b.ObservationId))
        {
            if (!idMap.ContainsKey(bal.ObservationId))
            {
                if (nextAllocatedId == ulong.MaxValue)
                {
                    throw new IdentityAllocationExhaustedException();
                }

                idMap[bal.ObservationId] = nextAllocatedId++;
            }
        }

        if (importedSnapshot.Transactions != null)
        {
            foreach (StoreTransactionObservationEntry obs in importedSnapshot.Transactions.Observations.OrderBy(o => o.Id))
            {
                if (!idMap.ContainsKey(obs.Id))
                {
                    if (nextAllocatedId == ulong.MaxValue)
                    {
                        throw new IdentityAllocationExhaustedException();
                    }

                    idMap[obs.Id] = nextAllocatedId++;
                }
            }
        }

        ulong Remap(ulong oldId) =>
            idMap.TryGetValue(oldId, out ulong newId)
                ? newId
                : throw new InvalidDataException($"Imported entity ID {oldId} is missing from the imported ledger map.");

        List<StoreInstitutionEntry> mergedInstitutions = new(targetSnapshot.Institutions.Institutions.Count + importedSnapshot.Institutions.Institutions.Count);
        mergedInstitutions.AddRange(targetSnapshot.Institutions.Institutions);
        foreach (StoreInstitutionEntry inst in importedSnapshot.Institutions.Institutions)
        {
            mergedInstitutions.Add(inst with { Id = Remap(inst.Id) });
        }

        List<StoreConnectionEntry> mergedConnections = new(targetSnapshot.Connections.Connections.Count + importedSnapshot.Connections.Connections.Count);
        mergedConnections.AddRange(targetSnapshot.Connections.Connections);
        foreach (StoreConnectionEntry conn in importedSnapshot.Connections.Connections)
        {
            string remappedState = conn.State switch
            {
                "Connected" or "Authenticating" or "ChallengeRequired" or "TemporarilyUnavailable" => "ReauthenticationRequired",
                _ => conn.State,
            };

            mergedConnections.Add(conn with
            {
                Id = Remap(conn.Id),
                InstitutionId = Remap(conn.InstitutionId),
                State = remappedState,
            });
        }

        List<StoreAccountEntry> mergedAccounts = new(targetSnapshot.Accounts.Accounts.Count + importedSnapshot.Accounts.Accounts.Count);
        mergedAccounts.AddRange(targetSnapshot.Accounts.Accounts);
        foreach (StoreAccountEntry acc in importedSnapshot.Accounts.Accounts)
        {
            mergedAccounts.Add(acc with { Id = Remap(acc.Id) });
        }

        List<StoreIncarnationEntry> mergedIncarnations = new(targetSnapshot.Accounts.Incarnations.Count + importedSnapshot.Accounts.Incarnations.Count);
        mergedIncarnations.AddRange(targetSnapshot.Accounts.Incarnations);
        foreach (StoreIncarnationEntry inc in importedSnapshot.Accounts.Incarnations)
        {
            mergedIncarnations.Add(inc with
            {
                Id = Remap(inc.Id),
                AccountId = Remap(inc.AccountId),
            });
        }

        List<StoreBindingEntry> mergedBindings = new(targetSnapshot.Accounts.Bindings.Count + importedSnapshot.Accounts.Bindings.Count);
        mergedBindings.AddRange(targetSnapshot.Accounts.Bindings);
        foreach (StoreBindingEntry bin in importedSnapshot.Accounts.Bindings)
        {
            mergedBindings.Add(bin with
            {
                Id = Remap(bin.Id),
                AccountId = Remap(bin.AccountId),
                IncarnationId = Remap(bin.IncarnationId),
                InstitutionId = Remap(bin.InstitutionId),
                ConnectionId = Remap(bin.ConnectionId),
            });
        }

        List<StoreCapabilityEntry> mergedCapabilities = new(targetSnapshot.Accounts.Capabilities.Count + importedSnapshot.Accounts.Capabilities.Count);
        mergedCapabilities.AddRange(targetSnapshot.Accounts.Capabilities);
        foreach (StoreCapabilityEntry cap in importedSnapshot.Accounts.Capabilities)
        {
            mergedCapabilities.Add(cap with
            {
                ConnectionId = Remap(cap.ConnectionId),
                AccountId = Remap(cap.AccountId),
            });
        }

        List<StoreBalanceEntry> mergedBalances = new(targetSnapshot.Balances.Balances.Count + importedSnapshot.Balances.Balances.Count);
        mergedBalances.AddRange(targetSnapshot.Balances.Balances);
        foreach (StoreBalanceEntry bal in importedSnapshot.Balances.Balances)
        {
            mergedBalances.Add(bal with
            {
                Id = Remap(bal.Id),
                ObservationId = Remap(bal.ObservationId),
                AccountId = Remap(bal.AccountId),
                IncarnationId = Remap(bal.IncarnationId),
                BindingId = Remap(bal.BindingId),
            });
        }

        List<StoreLedgerRecord> mergedLedger = new(targetSnapshot.Ledger.Records.Count + importedSnapshot.Ledger.Records.Count);
        mergedLedger.AddRange(targetSnapshot.Ledger.Records);
        foreach (StoreLedgerRecord rec in importedSnapshot.Ledger.Records)
        {
            ulong? remappedSupersedes = rec.SupersedesId.HasValue ? Remap(rec.SupersedesId.Value) : null;
            List<(ulong TargetId, string ExpectedKind)> remappedRefs = new(rec.References.Count);
            foreach ((ulong targetId, string expectedKind) in rec.References)
            {
                remappedRefs.Add((Remap(targetId), expectedKind));
            }

            mergedLedger.Add(new StoreLedgerRecord(Remap(rec.Id), rec.Kind, remappedSupersedes, remappedRefs));
        }

        List<StoreTransactionObservationEntry> mergedObservations = [];
        List<StoreTransactionEntry> mergedTransactions = [];
        List<StoreTransactionRevisionEntry> mergedRevisions = [];
        List<StoreAnnotationRevisionEntry> mergedAnnotations = [];
        List<StorePendingToBookedLinkEntry> mergedLinks = [];
        List<StoreStatementSyncCheckpointEntry> mergedCheckpoints = [];

        var targetTx = targetSnapshot.TransactionsOrDefault;
        var importedTx = importedSnapshot.TransactionsOrDefault;

        mergedObservations.AddRange(targetTx.Observations);
        foreach (StoreTransactionObservationEntry obs in importedTx.Observations)
        {
            mergedObservations.Add(obs with
            {
                Id = Remap(obs.Id),
                AccountBindingId = Remap(obs.AccountBindingId),
            });
        }

        mergedTransactions.AddRange(targetTx.Transactions);
        foreach (StoreTransactionEntry tx in importedTx.Transactions)
        {
            mergedTransactions.Add(tx with
            {
                Id = Remap(tx.Id),
                AccountId = Remap(tx.AccountId),
                AccountIncarnationId = Remap(tx.AccountIncarnationId),
            });
        }

        mergedRevisions.AddRange(targetTx.Revisions);
        foreach (StoreTransactionRevisionEntry rev in importedTx.Revisions)
        {
            mergedRevisions.Add(rev with
            {
                Id = Remap(rev.Id),
                TransactionId = Remap(rev.TransactionId),
                ObservationId = Remap(rev.ObservationId),
                AccountId = Remap(rev.AccountId),
                AccountIncarnationId = Remap(rev.AccountIncarnationId),
                AccountBindingId = Remap(rev.AccountBindingId),
            });
        }

        mergedAnnotations.AddRange(targetTx.Annotations);
        foreach (StoreAnnotationRevisionEntry ann in importedTx.Annotations)
        {
            mergedAnnotations.Add(ann with
            {
                Id = Remap(ann.Id),
                TransactionId = Remap(ann.TransactionId),
            });
        }

        mergedLinks.AddRange(targetTx.Links);
        foreach (StorePendingToBookedLinkEntry link in importedTx.Links)
        {
            mergedLinks.Add(link with
            {
                PendingTransactionId = Remap(link.PendingTransactionId),
                BookedTransactionId = Remap(link.BookedTransactionId),
            });
        }

        mergedCheckpoints.AddRange(targetTx.Checkpoints);
        foreach (StoreStatementSyncCheckpointEntry cp in importedTx.Checkpoints)
        {
            mergedCheckpoints.Add(cp with
            {
                AccountId = Remap(cp.AccountId),
                AccountIncarnationId = Remap(cp.AccountIncarnationId),
                AccountBindingId = Remap(cp.AccountBindingId),
            });
        }

        StoreTransactionsDocument mergedTx = new(
            targetProfileId, targetBranchId, targetGeneration,
            mergedObservations, mergedTransactions, mergedRevisions,
            mergedAnnotations, mergedLinks, mergedCheckpoints);

        List<StorePaymentDraftEntry> mergedDrafts = [];
        List<StorePaymentDraftRevisionEntry> mergedPaymentRevisions = [];
        List<StorePaymentAttemptEntry> mergedAttempts = [];
        List<StorePaymentStateTransitionEntry> mergedTransitions = [];

        var targetPay = targetSnapshot.PaymentsOrDefault;
        var importedPay = importedSnapshot.PaymentsOrDefault;

        mergedDrafts.AddRange(targetPay.Drafts);
        foreach (StorePaymentDraftEntry draft in importedPay.Drafts)
        {
            mergedDrafts.Add(draft with
            {
                Id = Remap(draft.Id),
                SourceAccountId = Remap(draft.SourceAccountId),
                SourceAccountBindingId = Remap(draft.SourceAccountBindingId),
                SourceAccountIncarnationId = Remap(draft.SourceAccountIncarnationId),
                CurrentRevisionId = Remap(draft.CurrentRevisionId),
            });
        }

        mergedPaymentRevisions.AddRange(targetPay.Revisions);
        foreach (StorePaymentDraftRevisionEntry rev in importedPay.Revisions)
        {
            mergedPaymentRevisions.Add(rev with
            {
                Id = Remap(rev.Id),
                DraftId = Remap(rev.DraftId),
                PredecessorRevisionId = rev.PredecessorRevisionId.HasValue ? Remap(rev.PredecessorRevisionId.Value) : null,
                SourceAccountId = Remap(rev.SourceAccountId),
                SourceAccountBindingId = Remap(rev.SourceAccountBindingId),
                SourceAccountIncarnationId = Remap(rev.SourceAccountIncarnationId),
            });
        }

        mergedAttempts.AddRange(targetPay.Attempts);
        foreach (StorePaymentAttemptEntry att in importedPay.Attempts)
        {
            mergedAttempts.Add(att with
            {
                Id = Remap(att.Id),
                DraftId = Remap(att.DraftId),
                DraftRevisionId = Remap(att.DraftRevisionId),
                SourceAccountId = Remap(att.SourceAccountId),
                SourceAccountBindingId = Remap(att.SourceAccountBindingId),
                SourceAccountIncarnationId = Remap(att.SourceAccountIncarnationId),
            });
        }

        mergedTransitions.AddRange(targetPay.Transitions);
        foreach (StorePaymentStateTransitionEntry tr in importedPay.Transitions)
        {
            mergedTransitions.Add(tr with
            {
                Id = Remap(tr.Id),
                AttemptId = Remap(tr.AttemptId),
            });
        }

        StorePaymentsDocument mergedPay = new(
            targetProfileId, targetBranchId, targetGeneration,
            mergedDrafts, mergedPaymentRevisions, mergedAttempts, mergedTransitions);

        List<StoreBeneficiaryTemplateEntry> mergedTemplates = [];
        StoreTemplatesDocument targetTmpl = targetSnapshot.TemplatesOrDefault;
        StoreTemplatesDocument importedTmpl = importedSnapshot.TemplatesOrDefault;
        mergedTemplates.AddRange(targetTmpl.Templates);
        foreach (StoreBeneficiaryTemplateEntry tmpl in importedTmpl.Templates)
        {
            mergedTemplates.Add(tmpl with
            {
                Id = Remap(tmpl.Id),
            });
        }

        StoreTemplatesDocument mergedTmplDoc = new(
            targetProfileId, targetBranchId, targetGeneration,
            mergedTemplates);

        StoreSnapshot merged = new(
            targetSnapshot.Profile with
            {
                ProfileId = targetProfileId,
                BranchId = targetBranchId,
                Generation = targetGeneration,
                NextId = nextAllocatedId,
                SavedAt = savedAt,
            },
            new StoreInstitutionsDocument(targetProfileId, targetBranchId, targetGeneration, mergedInstitutions),
            new StoreConnectionsDocument(targetProfileId, targetBranchId, targetGeneration, mergedConnections),
            new StoreAccountsDocument(targetProfileId, targetBranchId, targetGeneration, mergedAccounts, mergedIncarnations, mergedBindings, mergedCapabilities),
            new StoreBalancesDocument(targetProfileId, targetBranchId, targetGeneration, mergedBalances),
            new StoreLedgerDocument(targetProfileId, targetBranchId, targetGeneration, mergedLedger),
            mergedTx,
            mergedPay,
            mergedTmplDoc);

        ValidateIdentityAndSemanticInvariants(merged);
        return merged;
    }

    private static LocalEntityKind ParseEntityKindOrZero(string kind) =>
        Enum.TryParse<LocalEntityKind>(kind, ignoreCase: false, out LocalEntityKind parsed) && Enum.IsDefined(parsed)
            ? parsed
            : default;

    private static StoreProfileDocument ParseAndValidateProfile(byte[] bytes)
    {
        XElement root = ReadXml(bytes, "profile", "store-v1.xsd");
        Guid profileId = Guid.ParseExact(Attr(root, "profileId"), "D");
        Guid branchId = Guid.ParseExact(Attr(root, "branchId"), "D");
        string? parentBranchStr = root.Attribute("parentBranchId")?.Value;
        Guid? parentBranchId = string.IsNullOrEmpty(parentBranchStr) ? null : Guid.ParseExact(parentBranchStr, "D");
        string? recoveryProv = root.Attribute("recoveryProvenance")?.Value;
        ulong generation = ulong.Parse(Attr(root, "generation"), NumberStyles.None, CultureInfo.InvariantCulture);
        ulong nextId = ulong.Parse(Attr(root, "nextId"), NumberStyles.None, CultureInfo.InvariantCulture);
        DateTimeOffset savedAt = DateTimeOffset.ParseExact(Attr(root, "savedAt"), TimestampFormat,
            CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal);
        string label = root.Element(NsProfile + "label")!.Value;
        string note = root.Element(NsProfile + "note")!.Value;

        return new StoreProfileDocument(profileId, branchId, parentBranchId, recoveryProv, generation, nextId, savedAt, label, note);
    }

    private static StoreInstitutionsDocument ParseAndValidateInstitutions(byte[] bytes)
    {
        XElement root = ReadXml(bytes, "institutions", "store-v1.xsd");
        (Guid profileId, Guid branchId, ulong generation) = ReadHeader(root);
        List<StoreInstitutionEntry> list = [];
        foreach (XElement el in root.Elements(NsProfile + "institution"))
        {
            ulong id = ulong.Parse(Attr(el, "id"), NumberStyles.None, CultureInfo.InvariantCulture);
            string name = el.Element(NsProfile + "displayName")!.Value;
            string ep = el.Element(NsProfile + "endpoint")!.Value;
            string? bc = el.Element(NsProfile + "bankCode")?.Value;
            string? bic = el.Element(NsProfile + "bic")?.Value;
            string vs = el.Element(NsProfile + "verificationState")!.Value;
            list.Add(new StoreInstitutionEntry(id, name, ep, bc, bic, vs));
        }

        return new StoreInstitutionsDocument(profileId, branchId, generation, list);
    }

    private static StoreConnectionsDocument ParseAndValidateConnections(byte[] bytes)
    {
        XElement root = ReadXml(bytes, "connections", "store-v1.xsd");
        (Guid profileId, Guid branchId, ulong generation) = ReadHeader(root);
        List<StoreConnectionEntry> list = [];
        foreach (XElement el in root.Elements(NsProfile + "connection"))
        {
            ulong id = ulong.Parse(Attr(el, "id"), NumberStyles.None, CultureInfo.InvariantCulture);
            ulong instId = ulong.Parse(Attr(el, "institutionId"), NumberStyles.None, CultureInfo.InvariantCulture);
            string uid = el.Element(NsProfile + "userId")!.Value;
            string? cid = el.Element(NsProfile + "customerId")?.Value;
            string state = el.Element(NsProfile + "state")!.Value;
            string? sysId = el.Element(NsProfile + "systemId")?.Value;
            string sysStatus = el.Element(NsProfile + "systemStatus")!.Value;
            int nextMsg = int.Parse(el.Element(NsProfile + "nextClientMessageNumber")!.Value, NumberStyles.None, CultureInfo.InvariantCulture);
            bool isSync = bool.Parse(el.Element(NsProfile + "isSynchronized")!.Value);
            string? lastSuccStr = el.Element(NsProfile + "lastSuccessfulContact")?.Value;
            DateTimeOffset? lastSucc = string.IsNullOrEmpty(lastSuccStr) ? null : DateTimeOffset.ParseExact(lastSuccStr, TimestampFormat, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal);
            string? lastAttStr = el.Element(NsProfile + "lastAttemptedContact")?.Value;
            DateTimeOffset? lastAtt = string.IsNullOrEmpty(lastAttStr) ? null : DateTimeOffset.ParseExact(lastAttStr, TimestampFormat, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal);
            string connVer = el.Element(NsProfile + "connectorVersion")!.Value;

            list.Add(new StoreConnectionEntry(id, instId, uid, cid, state, sysId, sysStatus, nextMsg, isSync, lastSucc, lastAtt, connVer));
        }

        return new StoreConnectionsDocument(profileId, branchId, generation, list);
    }

    private static StoreAccountsDocument ParseAndValidateAccounts(byte[] bytes)
    {
        XElement root = ReadXml(bytes, "accounts", "store-v1.xsd");
        (Guid profileId, Guid branchId, ulong generation) = ReadHeader(root);

        List<StoreAccountEntry> accs = [];
        foreach (XElement el in root.Elements(NsProfile + "account"))
        {
            ulong id = ulong.Parse(Attr(el, "id"), NumberStyles.None, CultureInfo.InvariantCulture);
            string name = el.Element(NsProfile + "displayName")!.Value;
            string cur = el.Element(NsProfile + "currency")!.Value;
            accs.Add(new StoreAccountEntry(id, name, cur));
        }

        List<StoreIncarnationEntry> incs = [];
        foreach (XElement el in root.Elements(NsProfile + "incarnation"))
        {
            ulong id = ulong.Parse(Attr(el, "id"), NumberStyles.None, CultureInfo.InvariantCulture);
            ulong accId = ulong.Parse(Attr(el, "accountId"), NumberStyles.None, CultureInfo.InvariantCulture);
            string life = el.Element(NsProfile + "lifetimeEvidence")!.Value;
            incs.Add(new StoreIncarnationEntry(id, accId, life));
        }

        List<StoreBindingEntry> bins = [];
        foreach (XElement el in root.Elements(NsProfile + "binding"))
        {
            ulong id = ulong.Parse(Attr(el, "id"), NumberStyles.None, CultureInfo.InvariantCulture);
            ulong accId = ulong.Parse(Attr(el, "accountId"), NumberStyles.None, CultureInfo.InvariantCulture);
            ulong incId = ulong.Parse(Attr(el, "incarnationId"), NumberStyles.None, CultureInfo.InvariantCulture);
            ulong instId = ulong.Parse(Attr(el, "institutionId"), NumberStyles.None, CultureInfo.InvariantCulture);
            ulong connId = ulong.Parse(Attr(el, "connectionId"), NumberStyles.None, CultureInfo.InvariantCulture);
            string state = el.Element(NsProfile + "state")!.Value;
            XElement loc = el.Element(NsProfile + "locator")!;
            string connector = Attr(loc, "connector");
            string connVer = Attr(loc, "connectorVersion");
            string ep = Attr(loc, "endpoint");
            string? iban = loc.Element(NsProfile + "iban")?.Value;
            string? dan = loc.Element(NsProfile + "domesticAccountNumber")?.Value;
            string? dbc = loc.Element(NsProfile + "domesticBankCode")?.Value;
            string? sub = loc.Element(NsProfile + "subaccount")?.Value;
            string? cur = loc.Element(NsProfile + "currency")?.Value;
            List<(string, string, string, string)> bIds = [];
            foreach (XElement bEl in loc.Elements(NsProfile + "bankIdentifier"))
            {
                bIds.Add((Attr(bEl, "issuer"), Attr(bEl, "scope"), Attr(bEl, "kind"), Attr(bEl, "value")));
            }

            bins.Add(new StoreBindingEntry(id, accId, incId, instId, connId, state, connector, connVer, ep, iban, dan, dbc, sub, cur, bIds));
        }

        List<StoreCapabilityEntry> caps = [];
        foreach (XElement el in root.Elements(NsProfile + "capability"))
        {
            ulong connId = ulong.Parse(Attr(el, "connectionId"), NumberStyles.None, CultureInfo.InvariantCulture);
            ulong accId = ulong.Parse(Attr(el, "accountId"), NumberStyles.None, CultureInfo.InvariantCulture);
            string opCode = Attr(el, "operationCode");
            int opVer = int.Parse(Attr(el, "operationVersion"), NumberStyles.None, CultureInfo.InvariantCulture);
            string status = Attr(el, "status");
            string? limit = el.Element(NsProfile + "advertisedLimit")?.Value;
            caps.Add(new StoreCapabilityEntry(connId, accId, opCode, opVer, status, limit));
        }

        return new StoreAccountsDocument(profileId, branchId, generation, accs, incs, bins, caps);
    }

    private static StoreBalancesDocument ParseAndValidateBalances(byte[] bytes)
    {
        XElement root = ReadXml(bytes, "balances", "store-v1.xsd");
        (Guid profileId, Guid branchId, ulong generation) = ReadHeader(root);

        List<StoreBalanceEntry> list = [];
        foreach (XElement el in root.Elements(NsProfile + "balance"))
        {
            ulong id = ulong.Parse(Attr(el, "id"), NumberStyles.None, CultureInfo.InvariantCulture);
            ulong obsId = ulong.Parse(Attr(el, "observationId"), NumberStyles.None, CultureInfo.InvariantCulture);
            ulong accId = ulong.Parse(Attr(el, "accountId"), NumberStyles.None, CultureInfo.InvariantCulture);
            ulong incId = ulong.Parse(Attr(el, "incarnationId"), NumberStyles.None, CultureInfo.InvariantCulture);
            ulong binId = ulong.Parse(Attr(el, "bindingId"), NumberStyles.None, CultureInfo.InvariantCulture);
            string kind = el.Element(NsProfile + "kind")!.Value;
            string? amt = el.Element(NsProfile + "amount")?.Value;
            string? cur = el.Element(NsProfile + "currency")?.Value;
            string? btStr = el.Element(NsProfile + "bankTimestamp")?.Value;
            DateTimeOffset? bt = string.IsNullOrEmpty(btStr) ? null : DateTimeOffset.ParseExact(btStr, TimestampFormat, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal);
            DateTimeOffset retAt = DateTimeOffset.ParseExact(el.Element(NsProfile + "retrievedAt")!.Value, TimestampFormat, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal);
            bool isStale = bool.Parse(el.Element(NsProfile + "isStale")!.Value);
            string? sl = el.Element(NsProfile + "sourceLabel")?.Value;

            list.Add(new StoreBalanceEntry(id, obsId, accId, incId, binId, kind, amt, cur, bt, retAt, isStale, sl));
        }

        return new StoreBalancesDocument(profileId, branchId, generation, list);
    }

    private static StoreLedgerDocument ParseAndValidateLedger(byte[] bytes)
    {
        XElement root = ReadXml(bytes, "ledger", "store-v1.xsd");
        (Guid profileId, Guid branchId, ulong generation) = ReadHeader(root);

        List<StoreLedgerRecord> list = [];
        foreach (XElement el in root.Elements(NsProfile + "record"))
        {
            ulong id = ulong.Parse(Attr(el, "id"), NumberStyles.None, CultureInfo.InvariantCulture);
            string kind = Attr(el, "kind");
            ulong? supId = null;
            XElement? supEl = el.Element(NsProfile + "supersedes");
            if (supEl is not null)
            {
                supId = ulong.Parse(Attr(supEl, "id"), NumberStyles.None, CultureInfo.InvariantCulture);
            }

            List<(ulong, string)> refs = [];
            foreach (XElement rEl in el.Elements(NsProfile + "reference"))
            {
                ulong tid = ulong.Parse(Attr(rEl, "targetId"), NumberStyles.None, CultureInfo.InvariantCulture);
                string expKind = Attr(rEl, "expectedKind");
                refs.Add((tid, expKind));
            }

            list.Add(new StoreLedgerRecord(id, kind, supId, refs));
        }

        return new StoreLedgerDocument(profileId, branchId, generation, list);
    }

    public static StoreTransactionsDocument ParseAndValidateTransactions(byte[] bytes)
    {
        XElement root = ReadXml(bytes, "transactions", "transactions-v1.xsd");
        (Guid profileId, Guid branchId, ulong generation) = ReadHeader(root);

        List<StoreTransactionObservationEntry> obsList = [];
        foreach (XElement el in root.Elements(NsProfile + "observation"))
        {
            ulong id = ulong.Parse(Attr(el, "id"), NumberStyles.None, CultureInfo.InvariantCulture);
            ulong bindingId = ulong.Parse(Attr(el, "accountBindingId"), NumberStyles.None, CultureInfo.InvariantCulture);
            string format = el.Element(NsProfile + "statementFormat")!.Value;
            string? num = el.Element(NsProfile + "statementNumber")?.Value;
            string? token = el.Element(NsProfile + "pageToken")?.Value;
            DateTimeOffset retrievedAt = DateTimeOffset.ParseExact(el.Element(NsProfile + "retrievedAt")!.Value, TimestampFormat, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal);
            obsList.Add(new StoreTransactionObservationEntry(id, bindingId, retrievedAt, format, num, token));
        }

        List<StoreTransactionEntry> txList = [];
        foreach (XElement el in root.Elements(NsProfile + "transaction"))
        {
            ulong id = ulong.Parse(Attr(el, "id"), NumberStyles.None, CultureInfo.InvariantCulture);
            ulong accId = ulong.Parse(Attr(el, "accountId"), NumberStyles.None, CultureInfo.InvariantCulture);
            ulong incId = ulong.Parse(Attr(el, "accountIncarnationId"), NumberStyles.None, CultureInfo.InvariantCulture);
            string status = el.Element(NsProfile + "status")!.Value;
            string kind = el.Element(NsProfile + "kind")!.Value;
            txList.Add(new StoreTransactionEntry(id, accId, incId, status, kind));
        }

        List<StoreTransactionRevisionEntry> revList = [];
        foreach (XElement el in root.Elements(NsProfile + "revision"))
        {
            ulong id = ulong.Parse(Attr(el, "id"), NumberStyles.None, CultureInfo.InvariantCulture);
            ulong txId = ulong.Parse(Attr(el, "transactionId"), NumberStyles.None, CultureInfo.InvariantCulture);
            ulong obsId = ulong.Parse(Attr(el, "observationId"), NumberStyles.None, CultureInfo.InvariantCulture);
            ulong accId = ulong.Parse(Attr(el, "accountId"), NumberStyles.None, CultureInfo.InvariantCulture);
            ulong incId = ulong.Parse(Attr(el, "accountIncarnationId"), NumberStyles.None, CultureInfo.InvariantCulture);
            ulong binId = ulong.Parse(Attr(el, "accountBindingId"), NumberStyles.None, CultureInfo.InvariantCulture);
            string status = el.Element(NsProfile + "status")!.Value;
            string kind = el.Element(NsProfile + "kind")!.Value;
            string amount = el.Element(NsProfile + "amount")!.Value;
            string currency = el.Element(NsProfile + "currency")!.Value;
            bool isCredit = bool.Parse(el.Element(NsProfile + "isCredit")!.Value);
            DateOnly bookingDate = DateOnly.ParseExact(el.Element(NsProfile + "bookingDate")!.Value, "yyyy-MM-dd", CultureInfo.InvariantCulture);
            DateOnly? valueDate = el.Element(NsProfile + "valueDate") is { } vdEl
                ? DateOnly.ParseExact(vdEl.Value, "yyyy-MM-dd", CultureInfo.InvariantCulture)
                : null;
            string? btStr = el.Element(NsProfile + "bankTimestamp")?.Value;
            DateTimeOffset? bankTimestamp = string.IsNullOrEmpty(btStr) ? null : DateTimeOffset.ParseExact(btStr, TimestampFormat, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal);
            DateTimeOffset retrievedAt = DateTimeOffset.ParseExact(el.Element(NsProfile + "retrievedAt")!.Value, TimestampFormat, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal);

            XElement? cpEl = el.Element(NsProfile + "counterparty");
            string? cpName = cpEl?.Element(NsProfile + "name")?.Value;
            string? cpIban = cpEl?.Element(NsProfile + "iban")?.Value;
            string? cpBic = cpEl?.Element(NsProfile + "bic")?.Value;

            XElement? remEl = el.Element(NsProfile + "remittance");
            string? remText = remEl?.Element(NsProfile + "unstructuredText")?.Value;
            string? e2e = remEl?.Element(NsProfile + "endToEndId")?.Value;
            string? manId = remEl?.Element(NsProfile + "mandateId")?.Value;
            string? credId = remEl?.Element(NsProfile + "creditorId")?.Value;
            string? custRef = remEl?.Element(NsProfile + "customerReference")?.Value;
            string? bankRef = remEl?.Element(NsProfile + "bankReference")?.Value;
            string? purpose = remEl?.Element(NsProfile + "purposeCode")?.Value;

            string? rtrReason = el.Element(NsProfile + "returnReasonCode")?.Value;
            string? rawCode = el.Element(NsProfile + "rawBankCode")?.Value;

            revList.Add(new StoreTransactionRevisionEntry(
                id, txId, obsId, accId, incId, binId,
                status, kind, amount, currency, isCredit,
                bookingDate, valueDate, bankTimestamp, retrievedAt,
                cpName, cpIban, cpBic,
                remText, e2e, manId, credId, custRef, bankRef, purpose,
                rtrReason, rawCode));
        }

        List<StoreAnnotationRevisionEntry> annList = [];
        foreach (XElement el in root.Elements(NsProfile + "annotation"))
        {
            ulong id = ulong.Parse(Attr(el, "id"), NumberStyles.None, CultureInfo.InvariantCulture);
            ulong txId = ulong.Parse(Attr(el, "transactionId"), NumberStyles.None, CultureInfo.InvariantCulture);
            DateTimeOffset revTs = DateTimeOffset.ParseExact(el.Element(NsProfile + "revisionTimestamp")!.Value, TimestampFormat, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal);
            string? note = el.Element(NsProfile + "userNote")?.Value;
            string? cat = el.Element(NsProfile + "category")?.Value;
            List<string> tags = el.Elements(NsProfile + "tag").Select(t => t.Value).ToList();

            List<StoreSplitItemEntry> splits = [];
            foreach (XElement splitEl in el.Elements(NsProfile + "split"))
            {
                int index = int.Parse(Attr(splitEl, "index"), NumberStyles.None, CultureInfo.InvariantCulture);
                decimal splitAmount = decimal.Parse(splitEl.Element(NsProfile + "amount")!.Value, NumberStyles.AllowLeadingSign | NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture);
                string splitCat = splitEl.Element(NsProfile + "category")!.Value;
                string? splitNote = splitEl.Element(NsProfile + "note")?.Value;
                List<string> splitTags = splitEl.Elements(NsProfile + "tag").Select(t => t.Value).ToList();
                splits.Add(new StoreSplitItemEntry(index, splitAmount, splitCat, splitNote, splitTags));
            }

            annList.Add(new StoreAnnotationRevisionEntry(id, txId, revTs, note, cat, tags, splits.Count > 0 ? splits : null));
        }

        List<StorePendingToBookedLinkEntry> linkList = [];
        foreach (XElement el in root.Elements(NsProfile + "link"))
        {
            ulong pendingId = ulong.Parse(Attr(el, "pendingTransactionId"), NumberStyles.None, CultureInfo.InvariantCulture);
            ulong bookedId = ulong.Parse(Attr(el, "bookedTransactionId"), NumberStyles.None, CultureInfo.InvariantCulture);
            DateTimeOffset linkedAt = DateTimeOffset.ParseExact(el.Element(NsProfile + "linkedAt")!.Value, TimestampFormat, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal);
            string crit = el.Element(NsProfile + "matchCriterion")!.Value;
            linkList.Add(new StorePendingToBookedLinkEntry(pendingId, bookedId, linkedAt, crit));
        }

        List<StoreStatementSyncCheckpointEntry> cpList = [];
        foreach (XElement el in root.Elements(NsProfile + "checkpoint"))
        {
            ulong accId = ulong.Parse(Attr(el, "accountId"), NumberStyles.None, CultureInfo.InvariantCulture);
            ulong incId = ulong.Parse(Attr(el, "accountIncarnationId"), NumberStyles.None, CultureInfo.InvariantCulture);
            ulong binId = ulong.Parse(Attr(el, "accountBindingId"), NumberStyles.None, CultureInfo.InvariantCulture);
            DateOnly winStart = DateOnly.ParseExact(el.Element(NsProfile + "windowStart")!.Value, "yyyy-MM-dd", CultureInfo.InvariantCulture);
            DateOnly winEnd = DateOnly.ParseExact(el.Element(NsProfile + "windowEnd")!.Value, "yyyy-MM-dd", CultureInfo.InvariantCulture);
            if (winStart > winEnd)
            {
                throw new InvalidDataException($"Checkpoint windowStart {winStart} is after windowEnd {winEnd}.");
            }

            DateTimeOffset lastSync = DateTimeOffset.ParseExact(el.Element(NsProfile + "lastSyncTimestamp")!.Value, TimestampFormat, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal);
            string? lastToken = el.Element(NsProfile + "lastContinuationToken")?.Value;
            bool isComp = bool.Parse(el.Element(NsProfile + "isComplete")!.Value);
            int obsCount = int.Parse(el.Element(NsProfile + "observedTransactionCount")!.Value, NumberStyles.None, CultureInfo.InvariantCulture);
            cpList.Add(new StoreStatementSyncCheckpointEntry(accId, incId, binId, winStart, winEnd, lastSync, lastToken, isComp, obsCount));
        }

        return new StoreTransactionsDocument(profileId, branchId, generation, obsList, txList, revList, annList, linkList, cpList);
    }

    private static StorePaymentsDocument ParseAndValidatePayments(byte[] bytes)
    {
        XElement root = ReadXml(bytes, "payments", "payments-v1.xsd");
        (Guid profileId, Guid branchId, ulong generation) = ReadHeader(root);

        List<StorePaymentDraftEntry> draftList = [];
        foreach (XElement el in root.Elements(NsProfile + "draft"))
        {
            ulong id = ulong.Parse(Attr(el, "id"), NumberStyles.None, CultureInfo.InvariantCulture);
            ulong srcAccId = ulong.Parse(Attr(el, "sourceAccountId"), NumberStyles.None, CultureInfo.InvariantCulture);
            ulong srcBinId = ulong.Parse(Attr(el, "sourceAccountBindingId"), NumberStyles.None, CultureInfo.InvariantCulture);
            ulong srcIncId = ulong.Parse(Attr(el, "sourceAccountIncarnationId"), NumberStyles.None, CultureInfo.InvariantCulture);
            ulong curRevId = ulong.Parse(el.Element(NsProfile + "currentRevisionId")!.Value, NumberStyles.None, CultureInfo.InvariantCulture);
            DateTimeOffset createdAt = DateTimeOffset.ParseExact(el.Element(NsProfile + "createdAt")!.Value, TimestampFormat, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal);
            DateTimeOffset updatedAt = DateTimeOffset.ParseExact(el.Element(NsProfile + "updatedAt")!.Value, TimestampFormat, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal);
            draftList.Add(new StorePaymentDraftEntry(id, srcAccId, srcBinId, srcIncId, curRevId, createdAt, updatedAt));
        }

        List<StorePaymentDraftRevisionEntry> revList = [];
        foreach (XElement el in root.Elements(NsProfile + "revision"))
        {
            ulong id = ulong.Parse(Attr(el, "id"), NumberStyles.None, CultureInfo.InvariantCulture);
            ulong draftId = ulong.Parse(Attr(el, "draftId"), NumberStyles.None, CultureInfo.InvariantCulture);
            string? predStr = el.Attribute("predecessorRevisionId")?.Value;
            ulong? predecessorRevisionId = string.IsNullOrEmpty(predStr) ? null : ulong.Parse(predStr, NumberStyles.None, CultureInfo.InvariantCulture);
            int revNum = int.Parse(el.Element(NsProfile + "revisionNumber")!.Value, NumberStyles.None, CultureInfo.InvariantCulture);
            DateTimeOffset timestamp = DateTimeOffset.ParseExact(el.Element(NsProfile + "timestamp")!.Value, TimestampFormat, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal);
            ulong srcAccId = ulong.Parse(el.Element(NsProfile + "sourceAccountId")!.Value, NumberStyles.None, CultureInfo.InvariantCulture);
            ulong srcBinId = ulong.Parse(el.Element(NsProfile + "sourceAccountBindingId")!.Value, NumberStyles.None, CultureInfo.InvariantCulture);
            ulong srcIncId = ulong.Parse(el.Element(NsProfile + "sourceAccountIncarnationId")!.Value, NumberStyles.None, CultureInfo.InvariantCulture);

            XElement benEl = el.Element(NsProfile + "beneficiary")!;
            string benName = benEl.Element(NsProfile + "name")!.Value;
            string benIban = benEl.Element(NsProfile + "iban")!.Value;
            string? benBic = benEl.Element(NsProfile + "bic")?.Value;

            decimal amount = decimal.Parse(el.Element(NsProfile + "amount")!.Value, NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture);
            string currency = el.Element(NsProfile + "currency")!.Value;

            XElement? remEl = el.Element(NsProfile + "remittance");
            string? remUnstructured = remEl?.Element(NsProfile + "unstructured")?.Value;
            string? remCreditorRef = remEl?.Element(NsProfile + "creditorReference")?.Value;

            string execMode = el.Element(NsProfile + "executionMode")!.Value;
            DateOnly execDate = DateOnly.ParseExact(el.Element(NsProfile + "executionDate")!.Value, "yyyy-MM-dd", CultureInfo.InvariantCulture);

            revList.Add(new StorePaymentDraftRevisionEntry(
                id, draftId, revNum, predecessorRevisionId, timestamp,
                srcAccId, srcBinId, srcIncId, benName, benIban, benBic,
                amount, currency, remUnstructured, remCreditorRef,
                execMode, execDate));
        }

        List<StorePaymentAttemptEntry> attList = [];
        foreach (XElement el in root.Elements(NsProfile + "attempt"))
        {
            ulong id = ulong.Parse(Attr(el, "id"), NumberStyles.None, CultureInfo.InvariantCulture);
            ulong draftId = ulong.Parse(Attr(el, "draftId"), NumberStyles.None, CultureInfo.InvariantCulture);
            ulong draftRevId = ulong.Parse(Attr(el, "draftRevisionId"), NumberStyles.None, CultureInfo.InvariantCulture);
            ulong srcAccId = ulong.Parse(el.Element(NsProfile + "sourceAccountId")!.Value, NumberStyles.None, CultureInfo.InvariantCulture);
            ulong srcBinId = ulong.Parse(el.Element(NsProfile + "sourceAccountBindingId")!.Value, NumberStyles.None, CultureInfo.InvariantCulture);
            ulong srcIncId = ulong.Parse(el.Element(NsProfile + "sourceAccountIncarnationId")!.Value, NumberStyles.None, CultureInfo.InvariantCulture);
            string e2eId = el.Element(NsProfile + "endToEndId")!.Value;
            string state = el.Element(NsProfile + "state")!.Value;

            StorePaymentVoPResultEntry? vopResult = null;
            if (el.Element(NsProfile + "vopResult") is { } vopEl)
            {
                string vopStatus = vopEl.Element(NsProfile + "status")!.Value;
                DateTimeOffset vopCheckedAt = DateTimeOffset.ParseExact(vopEl.Element(NsProfile + "checkedAt")!.Value, TimestampFormat, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal);
                string? vopMatchedName = vopEl.Element(NsProfile + "matchedName")?.Value;
                string? vopReasonCode = vopEl.Element(NsProfile + "reasonCode")?.Value;
                string? vopWarningMessage = vopEl.Element(NsProfile + "warningMessage")?.Value;
                bool vopUserOverride = bool.Parse(vopEl.Element(NsProfile + "userOverride")!.Value);
                vopResult = new StorePaymentVoPResultEntry(vopStatus, vopCheckedAt, vopMatchedName, vopReasonCode, vopWarningMessage, vopUserOverride);
            }

            string? bankRef = el.Element(NsProfile + "bankReference")?.Value;
            string? respCode = el.Element(NsProfile + "responseCode")?.Value;
            string? statusDesc = el.Element(NsProfile + "statusDescription")?.Value;
            DateTimeOffset createdAt = DateTimeOffset.ParseExact(el.Element(NsProfile + "createdAt")!.Value, TimestampFormat, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal);
            DateTimeOffset updatedAt = DateTimeOffset.ParseExact(el.Element(NsProfile + "updatedAt")!.Value, TimestampFormat, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal);

            attList.Add(new StorePaymentAttemptEntry(
                id, draftId, draftRevId, srcAccId, srcBinId, srcIncId,
                e2eId, state, vopResult, bankRef, respCode, statusDesc,
                createdAt, updatedAt));
        }

        List<StorePaymentStateTransitionEntry> trList = [];
        foreach (XElement el in root.Elements(NsProfile + "transition"))
        {
            ulong id = ulong.Parse(Attr(el, "id"), NumberStyles.None, CultureInfo.InvariantCulture);
            ulong attId = ulong.Parse(Attr(el, "attemptId"), NumberStyles.None, CultureInfo.InvariantCulture);
            string fromState = el.Element(NsProfile + "fromState")!.Value;
            string toState = el.Element(NsProfile + "toState")!.Value;
            DateTimeOffset timestamp = DateTimeOffset.ParseExact(el.Element(NsProfile + "timestamp")!.Value, TimestampFormat, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal);
            string reason = el.Element(NsProfile + "reason")!.Value;
            string? bankCode = el.Element(NsProfile + "bankCode")?.Value;
            trList.Add(new StorePaymentStateTransitionEntry(id, attId, fromState, toState, timestamp, reason, bankCode));
        }

        return new StorePaymentsDocument(profileId, branchId, generation, draftList, revList, attList, trList);
    }

    private static StoreTemplatesDocument ParseAndValidateTemplates(byte[] bytes)
    {
        XElement root = ReadXml(bytes, "templates", "templates-v1.xsd");
        (Guid profileId, Guid branchId, ulong generation) = ReadHeader(root);

        List<StoreBeneficiaryTemplateEntry> list = [];
        foreach (XElement el in root.Elements(NsProfile + "template"))
        {
            ulong id = ulong.Parse(Attr(el, "id"), NumberStyles.None, CultureInfo.InvariantCulture);
            bool isSoftDeleted = bool.Parse(Attr(el, "isSoftDeleted"));
            string label = el.Element(NsProfile + "label")!.Value;

            XElement benEl = el.Element(NsProfile + "beneficiary")!;
            string name = benEl.Element(NsProfile + "name")!.Value;
            string iban = benEl.Element(NsProfile + "iban")!.Value;
            string? bic = benEl.Element(NsProfile + "bic")?.Value;

            XElement? remEl = el.Element(NsProfile + "defaultRemittance");
            string? remUnstruct = remEl?.Element(NsProfile + "unstructured")?.Value;
            string? remCredRef = remEl?.Element(NsProfile + "creditorReference")?.Value;

            string? amtStr = el.Element(NsProfile + "defaultAmount")?.Value;
            decimal? defaultAmt = string.IsNullOrEmpty(amtStr)
                ? null
                : decimal.Parse(amtStr, NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture);

            string? defaultCur = el.Element(NsProfile + "defaultCurrency")?.Value;
            string? defaultMode = el.Element(NsProfile + "defaultExecutionMode")?.Value;

            DateTimeOffset createdAt = DateTimeOffset.ParseExact(el.Element(NsProfile + "createdAt")!.Value, TimestampFormat,
                CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal);
            DateTimeOffset updatedAt = DateTimeOffset.ParseExact(el.Element(NsProfile + "updatedAt")!.Value, TimestampFormat,
                CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal);

            list.Add(new StoreBeneficiaryTemplateEntry(
                id,
                label,
                name,
                iban,
                bic,
                remUnstruct,
                remCredRef,
                defaultAmt,
                defaultCur,
                defaultMode,
                isSoftDeleted,
                createdAt,
                updatedAt));
        }

        return new StoreTemplatesDocument(profileId, branchId, generation, list);
    }

    private static void ValidateManifest(
        byte[] manifestBytes,
        Guid profileId,
        Guid branchId,
        ulong generation,
        Dictionary<string, byte[]> extractedDocs)
    {
        XElement root = ReadXml(manifestBytes, "manifest", "store-v1.xsd");
        (Guid p, Guid b, ulong g) = ReadHeader(root);
        if (p != profileId || b != branchId || g != generation)
        {
            throw new InvalidDataException("Manifest header lineage mismatch.");
        }

        bool hasTransactions = extractedDocs.ContainsKey("transactions.xml");
        bool hasPayments = extractedDocs.ContainsKey("payments.xml");
        bool hasTemplates = extractedDocs.ContainsKey("templates.xml");
        List<string> expectedNames =
        [
            "profile.xml",
            "institutions.xml",
            "connections.xml",
            "accounts.xml",
            "balances.xml",
            "ledger.xml"
        ];
        if (hasTransactions)
        {
            expectedNames.Add("transactions.xml");
        }
        if (hasPayments)
        {
            expectedNames.Add("payments.xml");
        }
        if (hasTemplates)
        {
            expectedNames.Add("templates.xml");
        }

        int expectedCount = expectedNames.Count;
        var entries = root.Elements(NsProfile + "entry").ToList();
        if (entries.Count != expectedCount)
        {
            throw new InvalidDataException($"Expected {expectedCount} document entries in manifest, found {entries.Count}.");
        }

        for (int i = 0; i < expectedCount; i++)
        {
            XElement entry = entries[i];
            string name = Attr(entry, "name");
            if (name != expectedNames[i])
            {
                throw new InvalidDataException($"Manifest entry order mismatch: expected '{expectedNames[i]}', found '{name}'.");
            }

            ulong expectedBytes = ulong.Parse(Attr(entry, "bytes"), NumberStyles.None, CultureInfo.InvariantCulture);
            string expectedSha = Attr(entry, "sha256");

            if (!extractedDocs.TryGetValue(name, out byte[]? actualBytes))
            {
                throw new InvalidDataException($"Document '{name}' listed in manifest was not found in archive.");
            }

            if ((ulong)actualBytes.Length != expectedBytes)
            {
                throw new InvalidDataException($"Byte count mismatch for '{name}': manifest declares {expectedBytes}, actual is {actualBytes.Length}.");
            }

            string actualSha = Convert.ToHexStringLower(SHA256.HashData(actualBytes));
            if (!string.Equals(actualSha, expectedSha, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidDataException($"SHA-256 digest mismatch for '{name}'.");
            }
        }
    }

    public static StorePurgeIntent ValidateAndParsePurgeIntent(byte[] xmlBytes)
    {
        ArgumentNullException.ThrowIfNull(xmlBytes);
        XElement root = ReadXml(xmlBytes, "purgeIntent", "purge-intent-v1.xsd");
        Guid profileId = Guid.ParseExact(Attr(root, "profileId"), "D");
        Guid branchId = Guid.ParseExact(Attr(root, "branchId"), "D");
        ulong generation = ulong.Parse(Attr(root, "generation"), NumberStyles.None, CultureInfo.InvariantCulture);
        string opType = Attr(root, "operationType");
        string status = Attr(root, "status");
        DateTimeOffset createdAt = DateTimeOffset.ParseExact(Attr(root, "createdAt"), TimestampFormat,
            CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal);

        string? repGenStr = root.Element(NsPurge + "replacementGeneration")?.Value;
        ulong? repGen = string.IsNullOrEmpty(repGenStr) ? null : ulong.Parse(repGenStr, NumberStyles.None, CultureInfo.InvariantCulture);

        List<(string, string)> inv = [];
        XElement invEl = root.Element(NsPurge + "targetInventory")!;
        foreach (XElement tEl in invEl.Elements(NsPurge + "targetFile"))
        {
            inv.Add((Attr(tEl, "name"), Attr(tEl, "sha256")));
        }

        string? note = root.Element(NsPurge + "note")?.Value;

        StorePurgeIntent intent = new(profileId, branchId, generation, opType, status, createdAt, repGen, inv, note);
        if (!xmlBytes.AsSpan().SequenceEqual(SerializePurgeIntent(intent)))
        {
            throw new InvalidDataException("Purge intent XML failed byte-exact serialization comparison.");
        }

        return intent;
    }

    private static (Guid ProfileId, Guid BranchId, ulong Generation) ReadHeader(XElement root)
    {
        Guid profileId = Guid.ParseExact(Attr(root, "profileId"), "D");
        Guid branchId = Guid.ParseExact(Attr(root, "branchId"), "D");
        ulong generation = ulong.Parse(Attr(root, "generation"), NumberStyles.None, CultureInfo.InvariantCulture);
        return (profileId, branchId, generation);
    }

    private static XElement ReadXml(byte[] bytes, string rootName, string schemaResourceName)
    {
        if (bytes.Length is 0 or > 1024 * 1024)
        {
            throw new InvalidDataException($"Document length {bytes.Length} outside 1..1048576 byte bounds.");
        }

        _ = StrictUtf8.GetCharCount(bytes);
        XmlSchemaSet schemas = new() { XmlResolver = null };
        using Stream schemaStream = typeof(StoreArchiveCodec).Assembly.GetManifestResourceStream($"ProfileEnvelope.{schemaResourceName}")
            ?? throw new InvalidOperationException($"Embedded schema resource 'ProfileEnvelope.{schemaResourceName}' not found.");
        using XmlReader schemaReader = XmlReader.Create(schemaStream, new XmlReaderSettings
        {
            DtdProcessing = DtdProcessing.Prohibit,
            XmlResolver = null,
            MaxCharactersInDocument = 64 * 1024,
            MaxCharactersFromEntities = 1024,
        });

        string ns = schemaResourceName.StartsWith("purge", StringComparison.OrdinalIgnoreCase) ? PurgeNamespace : ProfileNamespace;
        schemas.Add(ns, schemaReader);

        using MemoryStream input = new(bytes, writable: false);
        using (XmlReader reader = XmlReader.Create(input, new XmlReaderSettings
        {
            DtdProcessing = DtdProcessing.Prohibit,
            XmlResolver = null,
            MaxCharactersInDocument = 1024 * 1024,
            MaxCharactersFromEntities = 1024,
            ValidationType = ValidationType.Schema,
            Schemas = schemas,
            ValidationFlags = XmlSchemaValidationFlags.None,
        }))
        {
            while (reader.Read())
            {
                if (reader.Depth > 8)
                {
                    throw new InvalidDataException($"XML depth {reader.Depth} exceeds maximum allowed of 8.");
                }
            }
        }

        input.Position = 0;
        using XmlReader materializer = XmlReader.Create(input, new XmlReaderSettings
        {
            DtdProcessing = DtdProcessing.Prohibit,
            XmlResolver = null,
            MaxCharactersInDocument = 1024 * 1024,
            MaxCharactersFromEntities = 1024,
        });

        XElement root = XDocument.Load(materializer, LoadOptions.PreserveWhitespace).Root!;
        XNamespace expectedNs = ns;
        if (root.Name != expectedNs + rootName)
        {
            throw new InvalidDataException($"Expected root element '{expectedNs + rootName}', found '{root.Name}'.");
        }

        return root;
    }

    private static string Attr(XElement element, string name) =>
        element.Attribute(name)?.Value ?? throw new InvalidDataException($"Missing required attribute '{name}'.");
}
