using System.Globalization;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Xml.Linq;
using System.Xml.Schema;
using Broiler.Fond.Kernel.Domain;
using Broiler.Fond.Kernel.Domain.Identifiers;
using Broiler.Fond.Kernel.Domain.Payments;
using Broiler.Fond.Kernel.Storage;

namespace Broiler.Fond.Kernel.Tests.Payments;

/// <summary>
/// Executable verification suite for M5-05 Beneficiary Templates & Address Book in Profile Store:
/// templates-v1.xsd embedded schema validation, deterministic XML serialization, byte-exact round-tripping,
/// non-bypassing workflow invariants, backward compatibility with legacy 7-doc (M1), 8-doc (M2), and 9-doc (M4) containers,
/// hostile input and tamper detection, snapshot merge ID remapping, and production ProfileStore lifecycle.
/// </summary>
internal static class BeneficiaryTemplateStorageTests
{
    private const string TestPassphrase = "CorrectHorseBatteryStaple-TemplateStore-2026!";

    internal static void Run(Action<bool, string> check)
    {
        int checks = 0;
        void Verify(bool condition, string message)
        {
            checks++;
            check(condition, message);
        }

        VerifyBeneficiaryTemplateDomainValidation(Verify);
        VerifyBeneficiaryTemplatePopulateDraftNonBypassingInvariant(Verify);
        VerifyTemplatesSerializationAndDeserializationRoundTrip(Verify);
        VerifyFullStoreSnapshotWithTemplatesRoundTrip(Verify);
        VerifyLegacyStoreBackwardCompatibilityAndMigration(Verify);
        VerifyHostileInputAndSchemaViolations(Verify);
        VerifyIdentityAndSemanticInvariants(Verify);
        VerifySnapshotMergeWithTemplatesRemapping(Verify);
        VerifyProductionProfileStoreLifecycleWithTemplates(Verify);

        Console.WriteLine($"Beneficiary templates storage (M5-05): {checks} checks completed successfully.");
    }

    private static void VerifyBeneficiaryTemplateDomainValidation(Action<bool, string> verify)
    {
        DateTimeOffset now = new(2026, 10, 19, 12, 0, 0, TimeSpan.Zero);

        // 1. Valid full template
        BeneficiaryTemplate fullTemplate = new(
            Id: 501,
            Label: "Stadtwerke Electricity",
            BeneficiaryName: "Stadtwerke Muenchen GmbH",
            BeneficiaryIban: "DE89370400440532013000",
            BeneficiaryBic: "DBBADE33XXX",
            DefaultRemittanceUnstructured: "Customer 123456",
            DefaultRemittanceCreditorReference: "RF18539007547034",
            DefaultAmount: new Money(125.50m, "EUR"),
            DefaultExecutionMode: PaymentExecutionMode.StandardCreditTransfer,
            IsSoftDeleted: false,
            CreatedAt: now,
            UpdatedAt: now);

        SepaValidationResult fullResult = BeneficiaryTemplateValidator.Validate(fullTemplate);
        verify(fullResult.IsValid, "Complete valid beneficiary template passes validation.");

        // 2. Valid minimal template (no BIC, no remittance, no amount, no mode)
        BeneficiaryTemplate minimalTemplate = new(
            Id: 502,
            Label: "Landlord",
            BeneficiaryName: "Hans Mueller",
            BeneficiaryIban: "DE89370400440532013000",
            BeneficiaryBic: null,
            DefaultRemittanceUnstructured: null,
            DefaultRemittanceCreditorReference: null,
            DefaultAmount: null,
            DefaultExecutionMode: null,
            IsSoftDeleted: false,
            CreatedAt: now,
            UpdatedAt: now);

        SepaValidationResult minimalResult = BeneficiaryTemplateValidator.Validate(minimalTemplate);
        verify(minimalResult.IsValid, "Minimal valid beneficiary template passes validation.");

        // 3. Invalid label: empty or exceeds max length
        BeneficiaryTemplate emptyLabel = fullTemplate with { Label = "   " };
        verify(!BeneficiaryTemplateValidator.Validate(emptyLabel).IsValid, "Whitespace-only label is rejected.");

        BeneficiaryTemplate longLabel = fullTemplate with { Label = new string('A', BeneficiaryTemplate.MaxLabelLength + 1) };
        verify(!BeneficiaryTemplateValidator.Validate(longLabel).IsValid, "Label exceeding 70 characters is rejected.");

        // 4. Invalid name: empty or exceeds max length
        BeneficiaryTemplate emptyName = fullTemplate with { BeneficiaryName = "" };
        verify(!BeneficiaryTemplateValidator.Validate(emptyName).IsValid, "Empty beneficiary name is rejected.");

        BeneficiaryTemplate longName = fullTemplate with { BeneficiaryName = new string('B', BeneficiaryTemplate.MaxBeneficiaryNameLength + 1) };
        verify(!BeneficiaryTemplateValidator.Validate(longName).IsValid, "Name exceeding 70 characters is rejected.");

        // 5. Invalid IBAN: checksum or structure failure
        BeneficiaryTemplate invalidIban = fullTemplate with { BeneficiaryIban = "DE89370400440532013009" }; // wrong checksum
        verify(!BeneficiaryTemplateValidator.Validate(invalidIban).IsValid, "Corrupt IBAN checksum is rejected.");

        BeneficiaryTemplate garbageIban = fullTemplate with { BeneficiaryIban = "NOT-AN-IBAN" };
        verify(!BeneficiaryTemplateValidator.Validate(garbageIban).IsValid, "Garbage IBAN is rejected.");

        // 6. Invalid BIC: length not 8 or 11
        BeneficiaryTemplate shortBic = fullTemplate with { BeneficiaryBic = "DBBA" };
        verify(!BeneficiaryTemplateValidator.Validate(shortBic).IsValid, "BIC shorter than 8 chars is rejected.");

        BeneficiaryTemplate longBic = fullTemplate with { BeneficiaryBic = "DBBADE33XXXX" };
        verify(!BeneficiaryTemplateValidator.Validate(longBic).IsValid, "BIC longer than 11 chars is rejected.");

        // 7. Invalid default amount: non-EUR, non-positive, or scale > 2
        BeneficiaryTemplate nonEur = fullTemplate with { DefaultAmount = new Money(50.00m, "USD") };
        verify(!BeneficiaryTemplateValidator.Validate(nonEur).IsValid, "Non-EUR default currency is rejected.");

        BeneficiaryTemplate zeroAmt = fullTemplate with { DefaultAmount = new Money(0.00m, "EUR") };
        verify(!BeneficiaryTemplateValidator.Validate(zeroAmt).IsValid, "Zero default amount is rejected.");

        BeneficiaryTemplate negAmt = fullTemplate with { DefaultAmount = new Money(-10.00m, "EUR") };
        verify(!BeneficiaryTemplateValidator.Validate(negAmt).IsValid, "Negative default amount is rejected.");

        BeneficiaryTemplate subCent = fullTemplate with { DefaultAmount = new Money(10.555m, "EUR") };
        verify(!BeneficiaryTemplateValidator.Validate(subCent).IsValid, "Sub-cent fractions of cent in default amount are rejected.");

        // 8. Remittance too long
        BeneficiaryTemplate longRem = fullTemplate with { DefaultRemittanceUnstructured = new string('X', 141) };
        verify(!BeneficiaryTemplateValidator.Validate(longRem).IsValid, "Remittance exceeding 140 chars is rejected.");
    }

    private static void VerifyBeneficiaryTemplatePopulateDraftNonBypassingInvariant(Action<bool, string> verify)
    {
        DateTimeOffset now = new(2026, 10, 19, 12, 0, 0, TimeSpan.Zero);

        BeneficiaryTemplate template = new(
            Id: 501,
            Label: "Baker Shop",
            BeneficiaryName: "Baeckerei Schmidt",
            BeneficiaryIban: "DE89370400440532013000",
            BeneficiaryBic: "DBBADE33XXX",
            DefaultRemittanceUnstructured: "Weekly bread delivery",
            DefaultRemittanceCreditorReference: null,
            DefaultAmount: new Money(45.00m, "EUR"),
            DefaultExecutionMode: PaymentExecutionMode.InstantCreditTransfer,
            IsSoftDeleted: false,
            CreatedAt: now,
            UpdatedAt: now);

        // Create empty / bare draft revision
        PaymentDraftRevision draft = new(
            Id: 601,
            DraftId: 550,
            RevisionNumber: 1,
            PredecessorRevisionId: null,
            Timestamp: now,
            SourceAccountId: 10,
            SourceAccountBindingId: 12,
            SourceAccountIncarnationId: 11,
            Beneficiary: new PaymentBeneficiary("Temporary", "DE89370400440532013000", null),
            Amount: new Money(1.00m, "EUR"),
            Remittance: new PaymentRemittance(null, null),
            ExecutionMode: PaymentExecutionMode.StandardCreditTransfer,
            ExecutionDate: DateOnly.FromDateTime(DateTime.UtcNow));

        // Apply template
        PaymentDraftRevision populated = template.PopulateDraft(draft);

        verify(populated.Beneficiary.Name == "Baeckerei Schmidt", "Populated beneficiary name matches template.");
        verify(populated.Beneficiary.Iban == "DE89370400440532013000", "Populated beneficiary IBAN matches template.");
        verify(populated.Beneficiary.Bic == "DBBADE33XXX", "Populated beneficiary BIC matches template.");
        verify(populated.Remittance?.UnstructuredText == "Weekly bread delivery", "Populated remittance matches template.");
        verify(populated.Amount == new Money(45.00m, "EUR"), "Populated amount matches template default.");
        verify(populated.ExecutionMode == PaymentExecutionMode.InstantCreditTransfer, "Populated mode matches template preference.");

        // Anti-bypass invariant: Populating draft preserves draft entity identity and does NOT bypass domain validation
        verify(populated.DraftId == 550, "Draft ID remains unchanged.");
        verify(populated.RevisionNumber == 1, "Revision number remains unchanged.");

        // The populated draft must still undergo full SepaPaymentDraftValidator validation
        InstantPaymentCapability instantCap = new(
            IsSupported: true,
            MaximumAmount: 100_000m,
            EstimatedExecutionSeconds: 10,
            FeeNotice: null,
            SupportedSegmentCode: "HKIPB",
            SupportedSegmentVersion: 1);
        SepaValidationResult draftValidation = SepaPaymentDraftValidator.Validate(populated, instantCapability: instantCap);
        verify(draftValidation.IsValid, "Populated draft successfully undergoes standard SEPA validation.");

        // If template has corrupt IBAN, applying it will fail subsequent SEPA validation fail-closed
        BeneficiaryTemplate corruptTmpl = template with { BeneficiaryIban = "DE89370400440532013009" };
        PaymentDraftRevision corruptDraft = corruptTmpl.PopulateDraft(draft);
        SepaValidationResult corruptValidation = SepaPaymentDraftValidator.Validate(corruptDraft, instantCapability: instantCap);
        verify(!corruptValidation.IsValid, "Template cannot bypass SEPA validation; corrupt template IBAN fails SEPA check.");
    }

    private static void VerifyTemplatesSerializationAndDeserializationRoundTrip(Action<bool, string> verify)
    {
        Guid profileId = Guid.NewGuid();
        Guid branchId = Guid.NewGuid();
        ulong generation = 1;
        DateTimeOffset now = new(2026, 10, 19, 12, 0, 0, TimeSpan.Zero);

        StoreBeneficiaryTemplateEntry entry1 = new(
            Id: 701,
            Label: "Kindergarten Fee",
            BeneficiaryName: "Kita Sonnenschein eV",
            BeneficiaryIban: "DE89370400440532013000",
            BeneficiaryBic: "DBBADE33XXX",
            DefaultRemittanceUnstructured: "Child 987",
            DefaultRemittanceCreditorReference: "RF18539007547034",
            DefaultAmount: 320.00m,
            DefaultCurrency: "EUR",
            DefaultExecutionMode: "StandardCreditTransfer",
            IsSoftDeleted: false,
            CreatedAt: now,
            UpdatedAt: now.AddDays(1));

        StoreBeneficiaryTemplateEntry entry2 = new(
            Id: 702,
            Label: "Archived Landlord",
            BeneficiaryName: "Alte Vermietung GbR",
            BeneficiaryIban: "DE89370400440532013000",
            BeneficiaryBic: null,
            DefaultRemittanceUnstructured: null,
            DefaultRemittanceCreditorReference: null,
            DefaultAmount: null,
            DefaultCurrency: null,
            DefaultExecutionMode: null,
            IsSoftDeleted: true,
            CreatedAt: now.AddDays(-10),
            UpdatedAt: now.AddDays(-5));

        StoreTemplatesDocument originalDoc = new(
            profileId,
            branchId,
            generation,
            Templates: [entry1, entry2]);

        byte[] xmlBytes = StoreArchiveCodec.SerializeTemplates(originalDoc);
        verify(xmlBytes.Length > 0, "Serialized templates XML is non-empty.");

        // Byte-exact check: serializing twice produces identical bytes
        byte[] xmlBytesSecond = StoreArchiveCodec.SerializeTemplates(originalDoc);
        verify(xmlBytes.AsSpan().SequenceEqual(xmlBytesSecond), "SerializeTemplates is deterministic and byte-exact.");

        // Round-trip through complete 10-doc archive
        StoreSnapshot snapshot = CreateTestSnapshotWithTemplates(profileId, branchId, generation, originalDoc);
        byte[] zipBytes = StoreArchiveCodec.CreateStoreZip(snapshot);

        StoreSnapshot roundTripped = StoreArchiveCodec.ValidateAndParseStoreZip(zipBytes);
        StoreTemplatesDocument loadedDoc = roundTripped.TemplatesOrDefault;

        verify(loadedDoc.ProfileId == profileId, "Templates document profile ID matches.");
        verify(loadedDoc.BranchId == branchId, "Templates document branch ID matches.");
        verify(loadedDoc.Generation == generation, "Templates document generation matches.");
        verify(loadedDoc.Templates.Count == 2, "Both templates round-tripped successfully.");

        StoreBeneficiaryTemplateEntry loaded1 = loadedDoc.Templates[0];
        verify(loaded1.Id == 701, "Template 1 ID matches.");
        verify(loaded1.Label == "Kindergarten Fee", "Template 1 label matches.");
        verify(loaded1.BeneficiaryName == "Kita Sonnenschein eV", "Template 1 name matches.");
        verify(loaded1.BeneficiaryIban == "DE89370400440532013000", "Template 1 IBAN matches.");
        verify(loaded1.BeneficiaryBic == "DBBADE33XXX", "Template 1 BIC matches.");
        verify(loaded1.DefaultRemittanceUnstructured == "Child 987", "Template 1 remittance matches.");
        verify(loaded1.DefaultRemittanceCreditorReference == "RF18539007547034", "Template 1 creditor ref matches.");
        verify(loaded1.DefaultAmount == 320.00m, "Template 1 default amount matches.");
        verify(loaded1.DefaultCurrency == "EUR", "Template 1 default currency matches.");
        verify(loaded1.DefaultExecutionMode == "StandardCreditTransfer", "Template 1 default mode matches.");
        verify(!loaded1.IsSoftDeleted, "Template 1 active state matches.");
        verify(loaded1.CreatedAt == now, "Template 1 created at matches.");
        verify(loaded1.UpdatedAt == now.AddDays(1), "Template 1 updated at matches.");

        StoreBeneficiaryTemplateEntry loaded2 = loadedDoc.Templates[1];
        verify(loaded2.Id == 702, "Template 2 ID matches.");
        verify(loaded2.Label == "Archived Landlord", "Template 2 label matches.");
        verify(loaded2.IsSoftDeleted, "Template 2 soft-deleted state matches.");
        verify(loaded2.DefaultAmount == null, "Template 2 default amount is null.");
    }

    private static void VerifyFullStoreSnapshotWithTemplatesRoundTrip(Action<bool, string> verify)
    {
        Guid profileId = Guid.NewGuid();
        Guid branchId = Guid.NewGuid();
        ulong generation = 2;

        StoreSnapshot snapshot = CreateTestSnapshotWithTemplates(profileId, branchId, generation);
        byte[] zipBytes = StoreArchiveCodec.CreateStoreZip(snapshot);

        using (MemoryStream ms = new(zipBytes))
        using (ZipArchive zip = new(ms, ZipArchiveMode.Read))
        {
            verify(zip.Entries.Count == StoreArchiveCodec.StoreDocumentNames.Count,
                $"Store archive contains exactly {StoreArchiveCodec.StoreDocumentNames.Count} entries (10 documents).");
            for (int i = 0; i < StoreArchiveCodec.StoreDocumentNames.Count; i++)
            {
                verify(zip.Entries[i].FullName == StoreArchiveCodec.StoreDocumentNames[i],
                    $"Entry {i} is '{StoreArchiveCodec.StoreDocumentNames[i]}'.");
            }
        }

        StoreSnapshot roundTripped = StoreArchiveCodec.ValidateAndParseStoreZip(zipBytes);
        verify(roundTripped.Profile.ProfileId == profileId, "Profile ID matches after round-trip.");
        verify(roundTripped.Templates != null, "Templates document present in round-tripped snapshot.");
        StoreArchiveCodec.ValidateIdentityAndSemanticInvariants(roundTripped);
        verify(true, "Identity and semantic invariants pass for 10-document archive.");
    }

    private static void VerifyLegacyStoreBackwardCompatibilityAndMigration(Action<bool, string> verify)
    {
        Guid profileId = Guid.NewGuid();
        Guid branchId = Guid.NewGuid();
        ulong generation = 1;

        StoreSnapshot baseline = CreateTestSnapshotWithTemplates(profileId, branchId, generation);

        byte[] profileBytes = StoreArchiveCodec.SerializeProfile(baseline.Profile);
        byte[] instBytes = StoreArchiveCodec.SerializeInstitutions(baseline.Institutions);
        byte[] connBytes = StoreArchiveCodec.SerializeConnections(baseline.Connections);
        byte[] accBytes = StoreArchiveCodec.SerializeAccounts(baseline.Accounts);
        byte[] balBytes = StoreArchiveCodec.SerializeBalances(baseline.Balances);
        byte[] ledBytes = StoreArchiveCodec.SerializeLedger(baseline.Ledger);
        byte[] txBytes = StoreArchiveCodec.SerializeTransactions(baseline.TransactionsOrDefault);
        byte[] payBytes = StoreArchiveCodec.SerializePayments(baseline.PaymentsOrDefault);

        // 1. Create a 7-document v1 archive (M1: no transactions, no payments, no templates)
        List<(string Name, byte[] Bytes)> m1Entries =
        [
            ("profile.xml", profileBytes),
            ("institutions.xml", instBytes),
            ("connections.xml", connBytes),
            ("accounts.xml", accBytes),
            ("balances.xml", balBytes),
            ("ledger.xml", ledBytes),
        ];
        byte[] m1ManifestBytes = StoreArchiveCodec.SerializeManifest(profileId, branchId, generation, m1Entries);
        byte[] m1ZipBytes = StoreArchiveCodec.CreateZip(
            ("profile.xml", profileBytes),
            ("institutions.xml", instBytes),
            ("connections.xml", connBytes),
            ("accounts.xml", accBytes),
            ("balances.xml", balBytes),
            ("ledger.xml", ledBytes),
            ("manifest.xml", m1ManifestBytes));

        StoreSnapshot loadedM1 = StoreArchiveCodec.ValidateAndParseStoreZip(m1ZipBytes);
        verify(loadedM1.TemplatesOrDefault.Templates.Count == 0, "Legacy M1 archive loads default empty templates.");
        verify(loadedM1.TemplatesOrDefault.ProfileId == profileId, "Default empty templates inherits profile lineage.");

        byte[] upgradedM1Zip = StoreArchiveCodec.CreateStoreZip(loadedM1);
        using (MemoryStream ms = new(upgradedM1Zip))
        using (ZipArchive zip = new(ms, ZipArchiveMode.Read))
        {
            verify(zip.Entries.Count == StoreArchiveCodec.StoreDocumentNames.Count,
                $"Re-saving legacy M1 archive upgrades to {StoreArchiveCodec.StoreDocumentNames.Count} entries.");
        }

        // 2. Create an 8-document v2 archive (M2: with transactions, no payments, no templates)
        List<(string Name, byte[] Bytes)> m2Entries =
        [
            ("profile.xml", profileBytes),
            ("institutions.xml", instBytes),
            ("connections.xml", connBytes),
            ("accounts.xml", accBytes),
            ("balances.xml", balBytes),
            ("ledger.xml", ledBytes),
            ("transactions.xml", txBytes),
        ];
        byte[] m2ManifestBytes = StoreArchiveCodec.SerializeManifest(profileId, branchId, generation, m2Entries);
        byte[] m2ZipBytes = StoreArchiveCodec.CreateZip(
            ("profile.xml", profileBytes),
            ("institutions.xml", instBytes),
            ("connections.xml", connBytes),
            ("accounts.xml", accBytes),
            ("balances.xml", balBytes),
            ("ledger.xml", ledBytes),
            ("transactions.xml", txBytes),
            ("manifest.xml", m2ManifestBytes));

        StoreSnapshot loadedM2 = StoreArchiveCodec.ValidateAndParseStoreZip(m2ZipBytes);
        verify(loadedM2.TemplatesOrDefault.Templates.Count == 0, "Legacy M2 archive loads default empty templates.");

        byte[] upgradedM2Zip = StoreArchiveCodec.CreateStoreZip(loadedM2);
        using (MemoryStream ms = new(upgradedM2Zip))
        using (ZipArchive zip = new(ms, ZipArchiveMode.Read))
        {
            verify(zip.Entries.Count == StoreArchiveCodec.StoreDocumentNames.Count,
                $"Re-saving M2 archive upgrades to {StoreArchiveCodec.StoreDocumentNames.Count} entries.");
        }

        // 3. Create a 9-document v3 archive (M4: with transactions and payments, no templates)
        List<(string Name, byte[] Bytes)> m4Entries =
        [
            ("profile.xml", profileBytes),
            ("institutions.xml", instBytes),
            ("connections.xml", connBytes),
            ("accounts.xml", accBytes),
            ("balances.xml", balBytes),
            ("ledger.xml", ledBytes),
            ("transactions.xml", txBytes),
            ("payments.xml", payBytes),
        ];
        byte[] m4ManifestBytes = StoreArchiveCodec.SerializeManifest(profileId, branchId, generation, m4Entries);
        byte[] m4ZipBytes = StoreArchiveCodec.CreateZip(
            ("profile.xml", profileBytes),
            ("institutions.xml", instBytes),
            ("connections.xml", connBytes),
            ("accounts.xml", accBytes),
            ("balances.xml", balBytes),
            ("ledger.xml", ledBytes),
            ("transactions.xml", txBytes),
            ("payments.xml", payBytes),
            ("manifest.xml", m4ManifestBytes));

        StoreSnapshot loadedM4 = StoreArchiveCodec.ValidateAndParseStoreZip(m4ZipBytes);
        verify(loadedM4.TemplatesOrDefault.Templates.Count == 0, "Legacy M4 archive loads default empty templates.");

        byte[] upgradedM4Zip = StoreArchiveCodec.CreateStoreZip(loadedM4);
        using (MemoryStream ms = new(upgradedM4Zip))
        using (ZipArchive zip = new(ms, ZipArchiveMode.Read))
        {
            verify(zip.Entries.Count == StoreArchiveCodec.StoreDocumentNames.Count,
                $"Re-saving M4 archive upgrades to {StoreArchiveCodec.StoreDocumentNames.Count} entries.");
        }
    }

    private static void VerifyHostileInputAndSchemaViolations(Action<bool, string> verify)
    {
        Guid profileId = Guid.NewGuid();
        Guid branchId = Guid.NewGuid();
        ulong generation = 1;

        // Unknown element in templates.xml violates strict schema
        string invalidXml = $"""
            <?xml version="1.0" encoding="utf-8"?>
            <templates xmlns="urn:broiler:fond:profile:1" version="1" profileId="{profileId:D}" branchId="{branchId:D}" generation="{generation}">
              <template id="701" isSoftDeleted="false">
                <label>Hacker Template</label>
                <beneficiary>
                  <name>Malicious</name>
                  <iban>DE89370400440532013000</iban>
                </beneficiary>
                <unrecognizedElement>Injected</unrecognizedElement>
                <createdAt>2026-10-19T12:00:00+00:00</createdAt>
                <updatedAt>2026-10-19T12:00:00+00:00</updatedAt>
              </template>
            </templates>
            """;

        bool failedSchema = false;
        try
        {
            StoreSnapshot snap = CreateTestSnapshotWithTemplates(profileId, branchId, generation);
            byte[] profileBytes = StoreArchiveCodec.SerializeProfile(snap.Profile);
            byte[] instBytes = StoreArchiveCodec.SerializeInstitutions(snap.Institutions);
            byte[] connBytes = StoreArchiveCodec.SerializeConnections(snap.Connections);
            byte[] accBytes = StoreArchiveCodec.SerializeAccounts(snap.Accounts);
            byte[] balBytes = StoreArchiveCodec.SerializeBalances(snap.Balances);
            byte[] ledBytes = StoreArchiveCodec.SerializeLedger(snap.Ledger);
            byte[] txBytes = StoreArchiveCodec.SerializeTransactions(snap.TransactionsOrDefault);
            byte[] payBytes = StoreArchiveCodec.SerializePayments(snap.PaymentsOrDefault);
            byte[] badTmplBytes = Encoding.UTF8.GetBytes(invalidXml);

            List<(string Name, byte[] Bytes)> entries =
            [
                ("profile.xml", profileBytes),
                ("institutions.xml", instBytes),
                ("connections.xml", connBytes),
                ("accounts.xml", accBytes),
                ("balances.xml", balBytes),
                ("ledger.xml", ledBytes),
                ("transactions.xml", txBytes),
                ("payments.xml", payBytes),
                ("templates.xml", badTmplBytes),
            ];
            byte[] manifestBytes = StoreArchiveCodec.SerializeManifest(profileId, branchId, generation, entries);

            byte[] badZip = StoreArchiveCodec.CreateZip(
                ("profile.xml", profileBytes),
                ("institutions.xml", instBytes),
                ("connections.xml", connBytes),
                ("accounts.xml", accBytes),
                ("balances.xml", balBytes),
                ("ledger.xml", ledBytes),
                ("transactions.xml", txBytes),
                ("payments.xml", payBytes),
                ("templates.xml", badTmplBytes),
                ("manifest.xml", manifestBytes));

            StoreArchiveCodec.ValidateAndParseStoreZip(badZip);
        }
        catch (XmlSchemaValidationException)
        {
            failedSchema = true;
        }
        catch (InvalidDataException)
        {
            failedSchema = true;
        }

        verify(failedSchema, "Hostile XML with unrecognized schema elements in templates.xml fails closed.");
    }

    private static void VerifyIdentityAndSemanticInvariants(Action<bool, string> verify)
    {
        Guid profileId = Guid.NewGuid();
        Guid branchId = Guid.NewGuid();
        ulong generation = 1;
        DateTimeOffset now = new(2026, 10, 19, 12, 0, 0, TimeSpan.Zero);

        // 1. Template ID must be in ledger.xml as BeneficiaryTemplate
        StoreSnapshot snapMissingLedger = CreateTestSnapshotWithTemplates(profileId, branchId, generation) with
        {
            Templates = new StoreTemplatesDocument(profileId, branchId, generation,
            [
                new(999, "Missing Ledger", "Beneficiary", "DE89370400440532013000", null, null, null, null, null, null, false, now, now),
            ])
        };

        bool caughtMissingLedger = false;
        try
        {
            StoreArchiveCodec.ValidateIdentityAndSemanticInvariants(snapMissingLedger);
        }
        catch (InvalidDataException ex) when (ex.Message.Contains("missing from ledger.xml"))
        {
            caughtMissingLedger = true;
        }
        verify(caughtMissingLedger, "Template not registered in ledger is rejected by semantic validator.");

        // 2. Duplicate template ID rejection
        StoreLedgerDocument ledgerWith999 = snapMissingLedger.Ledger with
        {
            Records = [.. snapMissingLedger.Ledger.Records, new StoreLedgerRecord(999, "BeneficiaryTemplate", null, [])]
        };
        StoreSnapshot snapDuplicateTmpl = snapMissingLedger with
        {
            Ledger = ledgerWith999,
            Templates = new StoreTemplatesDocument(profileId, branchId, generation,
            [
                new(999, "Tmpl 1", "Beneficiary", "DE89370400440532013000", null, null, null, null, null, null, false, now, now),
                new(999, "Tmpl 2", "Beneficiary", "DE89370400440532013000", null, null, null, null, null, null, false, now, now),
            ])
        };

        bool caughtDuplicate = false;
        try
        {
            StoreArchiveCodec.ValidateIdentityAndSemanticInvariants(snapDuplicateTmpl);
        }
        catch (InvalidDataException ex) when (ex.Message.Contains("Duplicate template ID"))
        {
            caughtDuplicate = true;
        }
        verify(caughtDuplicate, "Duplicate template ID in templates.xml is rejected.");
    }

    private static void VerifySnapshotMergeWithTemplatesRemapping(Action<bool, string> verify)
    {
        Guid targetProfileId = Guid.NewGuid();
        Guid targetBranchId = Guid.NewGuid();
        ulong targetGen = 10;
        DateTimeOffset now = new(2026, 10, 19, 12, 0, 0, TimeSpan.Zero);

        // Target snapshot has template ID 10
        StoreSnapshot targetSnapshot = CreateTestSnapshotWithTemplates(targetProfileId, targetBranchId, targetGen,
            customTemplates: new StoreTemplatesDocument(targetProfileId, targetBranchId, targetGen,
            [
                new(10, "Target Landlord", "Landlord A", "DE89370400440532013000", null, null, null, null, null, null, false, now, now),
            ]),
            nextId: 20);

        // Imported snapshot has template ID 10 (conflicting ID!)
        Guid importedProfileId = Guid.NewGuid();
        Guid importedBranchId = Guid.NewGuid();
        StoreSnapshot importedSnapshot = CreateTestSnapshotWithTemplates(importedProfileId, importedBranchId, 5,
            customTemplates: new StoreTemplatesDocument(importedProfileId, importedBranchId, 5,
            [
                new(10, "Imported Electrician", "Electrician B", "DE89370400440532013000", null, null, null, null, null, null, false, now, now),
            ]),
            nextId: 20);

        StoreSnapshot merged = StoreArchiveCodec.MergeSnapshotsWithIdRemapping(
            targetSnapshot,
            importedSnapshot,
            targetProfileId,
            targetBranchId,
            targetGen + 1,
            now);

        verify(merged.TemplatesOrDefault.Templates.Count == 2, "Merged snapshot contains both target and imported templates.");

        StoreBeneficiaryTemplateEntry targetTmpl = merged.TemplatesOrDefault.Templates.First(t => t.Label == "Target Landlord");
        StoreBeneficiaryTemplateEntry importedTmpl = merged.TemplatesOrDefault.Templates.First(t => t.Label == "Imported Electrician");

        verify(targetTmpl.Id == 10, "Target template ID is retained.");
        verify(importedTmpl.Id >= 20, "Imported template ID is remapped monotonically beyond target NextId.");
        verify(targetTmpl.Id != importedTmpl.Id, "Template IDs do not collide after merge.");

        StoreArchiveCodec.ValidateIdentityAndSemanticInvariants(merged);
        verify(true, "Merged snapshot with remapped templates passes all semantic invariants.");
    }

    private static void VerifyProductionProfileStoreLifecycleWithTemplates(Action<bool, string> verify)
    {
        string tempDir = Path.Combine(Path.GetTempPath(), "BroilerFond_TemplatesTest_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);

        try
        {
            Guid profileId = Guid.NewGuid();
            DateTimeOffset now = new(2026, 10, 19, 12, 0, 0, TimeSpan.Zero);

            // 1. Initialize store with initial snapshot
            using (ProfileStore store = ProfileStore.CreateNew(tempDir, TestPassphrase, "Template Profile", "Testing M5 templates", profileId))
            {
                verify(store.State == ProfileStoreOpenState.Ready, "Store initialized in Ready state.");
                verify(store.CurrentSnapshot != null, "Store has initial snapshot.");

                StoreSnapshot baseSnap = store.CurrentSnapshot!;
                ulong nextId = baseSnap.Profile.NextId;

                ulong instId = nextId++;
                ulong connId = nextId++;
                ulong accId = nextId++;
                ulong incId = nextId++;
                ulong bindId = nextId++;
                ulong tmplId = nextId++;

                StoreInstitutionsDocument instDoc = new(
                    store.ProfileId, store.BranchId, store.CurrentGeneration + 1,
                    [new(instId, "Test Bank", "https://fints.testbank.de", "12345678", "TESTDEFFXXX", "Verified")]);

                StoreConnectionsDocument connDoc = new(
                    store.ProfileId, store.BranchId, store.CurrentGeneration + 1,
                    [new(connId, instId, "user123", null, "Connected", "SYS01", "Active", 1, true, now, now, "FinTS-3.0")]);

                StoreAccountsDocument accDoc = new(
                    store.ProfileId, store.BranchId, store.CurrentGeneration + 1,
                    Accounts: [new(accId, "Checking Account", "EUR")],
                    Incarnations: [new(incId, accId, "AccountActive")],
                    Bindings: [new(bindId, accId, incId, instId, connId, "Bound", "FinTS-3.0", "3.0", "https://fints.testbank.de", "DE89370400440532013000", null, null, null, "EUR", [])],
                    Capabilities: []);

                StoreBeneficiaryTemplateEntry newTemplate = new(
                    tmplId,
                    "Gym Membership",
                    "Fitness Studio GmbH",
                    "DE89370400440532013000",
                    "DBBADE33XXX",
                    "Member 4455",
                    null,
                    49.99m,
                    "EUR",
                    "StandardCreditTransfer",
                    false,
                    now,
                    now);

                List<StoreLedgerRecord> newLedger = new(baseSnap.Ledger.Records)
                {
                    new(instId, "Institution", null, []),
                    new(connId, "Connection", null, []),
                    new(accId, "Account", null, []),
                    new(incId, "AccountIncarnation", null, []),
                    new(bindId, "AccountBinding", null, []),
                    new(tmplId, "BeneficiaryTemplate", null, []),
                };

                StoreTemplatesDocument tmplDoc = new(
                    store.ProfileId, store.BranchId, store.CurrentGeneration + 1,
                    [newTemplate]);

                StoreSnapshot updatedSnap = baseSnap with
                {
                    Profile = baseSnap.Profile with { Generation = store.CurrentGeneration + 1, NextId = nextId },
                    Institutions = instDoc,
                    Connections = connDoc,
                    Accounts = accDoc,
                    Balances = baseSnap.Balances with { Generation = store.CurrentGeneration + 1 },
                    Ledger = baseSnap.Ledger with { Generation = store.CurrentGeneration + 1, Records = newLedger },
                    Transactions = baseSnap.TransactionsOrDefault with { Generation = store.CurrentGeneration + 1 },
                    Payments = baseSnap.PaymentsOrDefault with { Generation = store.CurrentGeneration + 1 },
                    Templates = tmplDoc
                };

                ProfileSaveMetrics metrics = store.Save(updatedSnap);
                verify(metrics.Generation == 2, "Generation 2 committed.");
                verify(store.CurrentSnapshot?.Templates?.Templates.Count == 1, "Template persisted in active store.");
            }

            // 2. Re-open and verify template persisted and decrypted via AEAD
            using (ProfileStore store = ProfileStore.Open(tempDir, TestPassphrase))
            {
                verify(store.State == ProfileStoreOpenState.Ready, "Store opened in Ready state.");
                StoreSnapshot loaded = store.CurrentSnapshot!;
                verify(loaded.TemplatesOrDefault.Templates.Count == 1, "Persisted template retrieved from encrypted store.");
                StoreBeneficiaryTemplateEntry tmpl = loaded.TemplatesOrDefault.Templates[0];
                verify(tmpl.Label == "Gym Membership", "Template label matches.");
                verify(tmpl.DefaultAmount == 49.99m, "Template amount matches.");
                verify(tmpl.DefaultExecutionMode == "StandardCreditTransfer", "Template mode matches.");

                // 3. Privacy purge test
                store.ExecutePrivacyPurge(store.CurrentSnapshot!, TestPassphrase);
                verify(store.CurrentGeneration == 3, "Privacy purge promoted generation to 3.");
                verify(store.CurrentSnapshot?.Templates?.Templates.Count == 1, "Template retained after privacy purge.");
            }
        }
        finally
        {
            if (Directory.Exists(tempDir))
            {
                Directory.Delete(tempDir, true);
            }
        }
    }

    private static StoreSnapshot CreateTestSnapshotWithTemplates(
        Guid profileId,
        Guid branchId,
        ulong generation,
        StoreTemplatesDocument? customTemplates = null,
        ulong nextId = 1000)
    {
        DateTimeOffset now = new(2026, 10, 19, 12, 0, 0, TimeSpan.Zero);

        StoreProfileDocument profile = new(
            profileId,
            branchId,
            null,
            null,
            generation,
            nextId,
            now,
            "Default Profile",
            "Notes");

        StoreInstitutionsDocument institutions = new(profileId, branchId, generation, [
            new(1, "Test Bank", "https://fints.testbank.de", "12345678", "TESTDEFFXXX", "Verified")
        ]);
        StoreConnectionsDocument connections = new(profileId, branchId, generation, [
            new(2, 1, "user123", null, "Connected", "SYS01", "Active", 1, true, now, now, "FinTS-3.0")
        ]);
        StoreAccountsDocument accounts = new(profileId, branchId, generation,
            Accounts: [new(5, "Checking Account", "EUR")],
            Incarnations: [new(6, 5, "AccountActive")],
            Bindings: [new(7, 5, 6, 1, 2, "Bound", "FinTS-3.0", "3.0", "https://fints.testbank.de", "DE89370400440532013000", null, null, null, "EUR", [])],
            Capabilities: []);
        StoreBalancesDocument balances = new(profileId, branchId, generation, []);

        List<StoreLedgerRecord> ledgerRecords =
        [
            new(1, "Institution", null, []),
            new(2, "Connection", null, []),
            new(5, "Account", null, []),
            new(6, "AccountIncarnation", null, []),
            new(7, "AccountBinding", null, []),
        ];

        if (customTemplates != null)
        {
            foreach (StoreBeneficiaryTemplateEntry tmpl in customTemplates.Templates)
            {
                ledgerRecords.Add(new StoreLedgerRecord(tmpl.Id, "BeneficiaryTemplate", null, []));
            }
        }

        StoreLedgerDocument ledger = new(profileId, branchId, generation, ledgerRecords);
        StoreTransactionsDocument transactions = new(profileId, branchId, generation, [], [], [], [], [], []);
        StorePaymentsDocument payments = new(profileId, branchId, generation, [], [], [], []);
        StoreTemplatesDocument templates = customTemplates ?? new StoreTemplatesDocument(profileId, branchId, generation, []);

        return new StoreSnapshot(profile, institutions, connections, accounts, balances, ledger, transactions, payments, templates);
    }
}
