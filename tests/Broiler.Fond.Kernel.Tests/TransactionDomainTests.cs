using Broiler.Fond.Kernel.Domain;
using Broiler.Fond.Kernel.Domain.Identifiers;
using Broiler.Fond.Kernel.Domain.Transactions;

namespace Broiler.Fond.Kernel.Tests;

/// <summary>
/// Executable verification suite for M2-01 / ADR 0094: Immutable transaction domain models,
/// verbatim bank facts, independent user annotations, and pending-to-booked links.
/// </summary>
internal static class TransactionDomainTests
{
    private static readonly DateTimeOffset BaseTime = new(2026, 10, 16, 15, 0, 0, TimeSpan.Zero);

    internal static void Run(Action<bool, string> check)
    {
        int checks = 0;
        void Verify(bool condition, string message)
        {
            checks++;
            check(condition, message);
        }

        VerifyTypedIdentities(Verify);
        VerifyCounterparty(Verify);
        VerifyRemittanceInformation(Verify);
        VerifyTransactionRevisionInvariants(Verify);
        VerifyAnnotationRevisionInvariants(Verify);
        VerifyPendingToBookedLink(Verify);
        VerifyTransactionObservation(Verify);
        VerifyLedgerRevisionIntegration(Verify);

        Console.WriteLine($"Transaction domain models (M2-01): {checks} checks completed successfully.");
    }

    private static void VerifyTypedIdentities(Action<bool, string> verify)
    {
        TransactionId tx1 = new(101);
        TransactionId tx2 = new(101);
        TransactionId tx3 = new(102);
        verify(tx1 == tx2, "TransactionId equality must be based on value.");
        verify(tx1 != tx3, "TransactionId inequality must distinguish different values.");

        TransactionRevisionId rev1 = new(201);
        TransactionRevisionId rev2 = new(201);
        TransactionRevisionId rev3 = new(202);
        verify(rev1 == rev2, "TransactionRevisionId equality must be based on value.");
        verify(rev1 != rev3, "TransactionRevisionId inequality must distinguish different values.");

        AnnotationRevisionId ann1 = new(301);
        AnnotationRevisionId ann2 = new(301);
        AnnotationRevisionId ann3 = new(302);
        verify(ann1 == ann2, "AnnotationRevisionId equality must be based on value.");
        verify(ann1 != ann3, "AnnotationRevisionId inequality must distinguish different values.");
    }

    private static void VerifyCounterparty(Action<bool, string> verify)
    {
        Counterparty cp = new(
            name: "  Musterfirma GmbH  ",
            iban: " de89 3704 0044 0532 0130 00 ",
            bic: " soladestxxx ",
            accountNumber: " 0532013000 ",
            bankCode: " 37040044 ");

        verify(cp.Name == "Musterfirma GmbH", "Counterparty name must be trimmed.");
        verify(cp.Iban == "DE89370400440532013000", "Counterparty IBAN must be uppercase with spaces removed.");
        verify(cp.Bic == "SOLADESTXXX", "Counterparty BIC must be uppercase with spaces removed.");
        verify(cp.AccountNumber == "0532013000", "Counterparty account number must be trimmed.");
        verify(cp.BankCode == "37040044", "Counterparty bank code must be trimmed.");

        Counterparty empty = new("   ", "  ", null, string.Empty, " ");
        verify(empty.Name is null, "Whitespace-only name must normalize to null.");
        verify(empty.Iban is null, "Whitespace-only IBAN must normalize to null.");
        verify(empty.Bic is null, "Whitespace-only BIC must normalize to null.");
        verify(empty.AccountNumber is null, "Whitespace-only account number must normalize to null.");
        verify(empty.BankCode is null, "Whitespace-only bank code must normalize to null.");

        // Length boundary checks
        ExpectThrows<ArgumentException>(() => new Counterparty(new string('A', 141)), "Name > 140 must throw.");
        ExpectThrows<ArgumentException>(() => new Counterparty("Valid", iban: new string('A', 35)), "IBAN > 34 must throw.");
        ExpectThrows<ArgumentException>(() => new Counterparty("Valid", bic: new string('A', 12)), "BIC > 11 must throw.");
        ExpectThrows<ArgumentException>(() => new Counterparty("Valid", accountNumber: new string('A', 36)), "Account number > 35 must throw.");
        ExpectThrows<ArgumentException>(() => new Counterparty("Valid", bankCode: new string('A', 36)), "Bank code > 35 must throw.");
    }

    private static void VerifyRemittanceInformation(Action<bool, string> verify)
    {
        RemittanceInformation rem = new(
            unstructuredText: "  Invoice 2026-9901  ",
            endToEndId: " E2E-998877 ",
            mandateId: " MANDATE-42 ",
            creditorId: " DE98ZZZ09999999999 ",
            customerReference: " CUST-REF-01 ",
            bankReference: " BANK-REF-99 ",
            purposeCode: " sala ");

        verify(rem.UnstructuredText == "Invoice 2026-9901", "Unstructured text must be trimmed.");
        verify(rem.EndToEndId == "E2E-998877", "End-to-end ID must be trimmed.");
        verify(rem.MandateId == "MANDATE-42", "Mandate ID must be trimmed.");
        verify(rem.CreditorId == "DE98ZZZ09999999999", "Creditor ID must be trimmed.");
        verify(rem.CustomerReference == "CUST-REF-01", "Customer reference must be trimmed.");
        verify(rem.BankReference == "BANK-REF-99", "Bank reference must be trimmed.");
        verify(rem.PurposeCode == "SALA", "Purpose code must be uppercase trimmed.");

        // Length boundary checks
        ExpectThrows<ArgumentException>(() => new RemittanceInformation(new string('X', 1025)), "Text > 1024 must throw.");
        ExpectThrows<ArgumentException>(() => new RemittanceInformation("OK", endToEndId: new string('X', 36)), "E2E ID > 35 must throw.");
        ExpectThrows<ArgumentException>(() => new RemittanceInformation("OK", mandateId: new string('X', 36)), "Mandate ID > 35 must throw.");
        ExpectThrows<ArgumentException>(() => new RemittanceInformation("OK", creditorId: new string('X', 36)), "Creditor ID > 35 must throw.");
        ExpectThrows<ArgumentException>(() => new RemittanceInformation("OK", customerReference: new string('X', 36)), "Customer ref > 35 must throw.");
        ExpectThrows<ArgumentException>(() => new RemittanceInformation("OK", bankReference: new string('X', 36)), "Bank ref > 35 must throw.");
        ExpectThrows<ArgumentException>(() => new RemittanceInformation("OK", purposeCode: "SALARY"), "Purpose code > 4 must throw.");
    }

    private static void VerifyTransactionRevisionInvariants(Action<bool, string> verify)
    {
        DateOnly bookingDate = new(2026, 10, 15);
        DateOnly valueDate = new(2026, 10, 16);
        Money amount = new(150.75m, "EUR");
        Counterparty cp = new("Landlord", "DE89370400440532013000");
        RemittanceInformation rem = new("Rent October 2026", endToEndId: "RENT-2026-10");

        TransactionRevision rev1 = new(
            id: new TransactionRevisionId(10),
            transactionId: new TransactionId(1),
            observationId: new ObservationId(100),
            accountId: new AccountId(2),
            accountIncarnationId: new AccountIncarnationId(3),
            accountBindingId: new AccountBindingId(4),
            status: TransactionStatus.Booked,
            kind: TransactionKind.Transfer,
            amount: amount,
            isCredit: false,
            bookingDate: bookingDate,
            valueDate: valueDate,
            bankTimestamp: BaseTime,
            retrievedAt: BaseTime,
            counterparty: cp,
            remittance: rem,
            supersedesId: null,
            returnReasonCode: " ms02 ",
            rawBankCode: " GVC:166 ");

        verify(rev1.Id.Value == 10, "Id must match.");
        verify(rev1.TransactionId.Value == 1, "TransactionId must match.");
        verify(rev1.Status == TransactionStatus.Booked, "Status must match.");
        verify(rev1.Kind == TransactionKind.Transfer, "Kind must match.");
        verify(rev1.Amount == amount, "Amount must match exact money.");
        verify(!rev1.IsCredit, "IsCredit must be false for debit.");
        verify(rev1.BookingDate == bookingDate, "Booking date must match.");
        verify(rev1.ValueDate == valueDate, "Value date must match.");
        verify(rev1.ReturnReasonCode == "MS02", "Return reason code must be normalized.");
        verify(rev1.RawBankCode == "GVC:166", "Raw bank code must be trimmed.");

        // Default value date fallback
        TransactionRevision revNoValDate = new(
            id: new TransactionRevisionId(11),
            transactionId: new TransactionId(1),
            observationId: new ObservationId(101),
            accountId: new AccountId(2),
            accountIncarnationId: new AccountIncarnationId(3),
            accountBindingId: new AccountBindingId(4),
            status: TransactionStatus.Booked,
            kind: TransactionKind.Transfer,
            amount: amount,
            isCredit: false,
            bookingDate: bookingDate,
            valueDate: null,
            bankTimestamp: BaseTime,
            retrievedAt: BaseTime);
        verify(revNoValDate.ValueDate == bookingDate, "Null value date must default to booking date.");

        // Revision superseding
        TransactionRevision rev2 = new(
            id: new TransactionRevisionId(12),
            transactionId: new TransactionId(1),
            observationId: new ObservationId(102),
            accountId: new AccountId(2),
            accountIncarnationId: new AccountIncarnationId(3),
            accountBindingId: new AccountBindingId(4),
            status: TransactionStatus.Booked,
            kind: TransactionKind.Transfer,
            amount: amount,
            isCredit: false,
            bookingDate: bookingDate,
            valueDate: valueDate,
            bankTimestamp: BaseTime.AddMinutes(5),
            retrievedAt: BaseTime.AddMinutes(5),
            supersedesId: new TransactionRevisionId(10));
        verify(rev2.SupersedesId == new TransactionRevisionId(10), "SupersedesId must point to prior revision.");

        // Zero ID rejections
        ExpectThrows<ArgumentException>(() => new TransactionRevision(
            new TransactionRevisionId(0), new TransactionId(1), new ObservationId(1), new AccountId(1),
            new AccountIncarnationId(1), new AccountBindingId(1), TransactionStatus.Booked, TransactionKind.Debit,
            amount, false, bookingDate, null, BaseTime, BaseTime), "Zero Revision ID must throw.");

        ExpectThrows<ArgumentException>(() => new TransactionRevision(
            new TransactionRevisionId(1), new TransactionId(0), new ObservationId(1), new AccountId(1),
            new AccountIncarnationId(1), new AccountBindingId(1), TransactionStatus.Booked, TransactionKind.Debit,
            amount, false, bookingDate, null, BaseTime, BaseTime), "Zero Transaction ID must throw.");

        // Negative magnitude rejection
        ExpectThrows<ArgumentException>(() => new TransactionRevision(
            new TransactionRevisionId(1), new TransactionId(1), new ObservationId(1), new AccountId(1),
            new AccountIncarnationId(1), new AccountBindingId(1), TransactionStatus.Booked, TransactionKind.Debit,
            new Money(-50m, "EUR"), false, bookingDate, null, BaseTime, BaseTime), "Negative amount must throw.");

        // Self-supersede rejection
        ExpectThrows<ArgumentException>(() => new TransactionRevision(
            new TransactionRevisionId(10), new TransactionId(1), new ObservationId(1), new AccountId(1),
            new AccountIncarnationId(1), new AccountBindingId(1), TransactionStatus.Booked, TransactionKind.Debit,
            amount, false, bookingDate, null, BaseTime, BaseTime, supersedesId: new TransactionRevisionId(10)),
            "Self-superseding revision must throw.");
    }

    private static void VerifyAnnotationRevisionInvariants(Action<bool, string> verify)
    {
        AnnotationRevision ann1 = new(
            id: new AnnotationRevisionId(50),
            transactionId: new TransactionId(1),
            modifiedAt: BaseTime,
            supersedesId: null,
            note: " Monthly apartment rent ",
            category: " Housing ",
            tags: ["living", " Rent ", "living", "fixed-costs"],
            isExcludedFromBudget: false);

        verify(ann1.Id.Value == 50, "Id must match.");
        verify(ann1.TransactionId.Value == 1, "TransactionId must match.");
        verify(ann1.Note == "Monthly apartment rent", "Note must be trimmed.");
        verify(ann1.Category == "Housing", "Category must be trimmed.");
        verify(ann1.Tags.Count == 3, "Tags must be deduplicated.");
        verify(ann1.Tags[0] == "fixed-costs" && ann1.Tags[1] == "living" && ann1.Tags[2] == "Rent",
            "Tags must be sorted alphabetically (case-insensitive).");

        // Annotation superseding
        AnnotationRevision ann2 = new(
            id: new AnnotationRevisionId(51),
            transactionId: new TransactionId(1),
            modifiedAt: BaseTime.AddHours(1),
            supersedesId: new AnnotationRevisionId(50),
            note: "Updated rent amount",
            category: "Living",
            tags: ["fixed"],
            isExcludedFromBudget: true);

        verify(ann2.SupersedesId == new AnnotationRevisionId(50), "SupersedesId must match.");
        verify(ann2.IsExcludedFromBudget, "IsExcludedFromBudget must be true.");

        // Zero ID rejections
        ExpectThrows<ArgumentException>(() => new AnnotationRevision(
            new AnnotationRevisionId(0), new TransactionId(1), BaseTime), "Zero annotation ID must throw.");
        ExpectThrows<ArgumentException>(() => new AnnotationRevision(
            new AnnotationRevisionId(1), new TransactionId(0), BaseTime), "Zero transaction ID must throw.");

        // Self-supersede rejection
        ExpectThrows<ArgumentException>(() => new AnnotationRevision(
            new AnnotationRevisionId(50), new TransactionId(1), BaseTime, supersedesId: new AnnotationRevisionId(50)),
            "Self-superseding annotation must throw.");

        // Length and count bounds
        ExpectThrows<ArgumentException>(() => new AnnotationRevision(
            new AnnotationRevisionId(1), new TransactionId(1), BaseTime, note: new string('N', 4097)), "Note > 4096 must throw.");
        ExpectThrows<ArgumentException>(() => new AnnotationRevision(
            new AnnotationRevisionId(1), new TransactionId(1), BaseTime, category: new string('C', 129)), "Category > 128 must throw.");
        ExpectThrows<ArgumentException>(() => new AnnotationRevision(
            new AnnotationRevisionId(1), new TransactionId(1), BaseTime, tags: [new string('T', 65)]), "Tag > 64 must throw.");

        string[] tooManyTags = Enumerable.Range(1, 33).Select(i => $"tag{i}").ToArray();
        ExpectThrows<ArgumentException>(() => new AnnotationRevision(
            new AnnotationRevisionId(1), new TransactionId(1), BaseTime, tags: tooManyTags), "Tag count > 32 must throw.");
    }

    private static void VerifyPendingToBookedLink(Action<bool, string> verify)
    {
        PendingToBookedLink link = new(
            pendingTransactionId: new TransactionId(100),
            bookedTransactionId: new TransactionId(200),
            linkedAt: BaseTime,
            matchCriterion: "EndToEndId-Matching");

        verify(link.PendingTransactionId.Value == 100, "Pending ID must match.");
        verify(link.BookedTransactionId.Value == 200, "Booked ID must match.");
        verify(link.MatchCriterion == "EndToEndId-Matching", "Criterion must match.");

        ExpectThrows<ArgumentException>(() => new PendingToBookedLink(
            new TransactionId(0), new TransactionId(200), BaseTime, "Match"), "Zero pending ID must throw.");
        ExpectThrows<ArgumentException>(() => new PendingToBookedLink(
            new TransactionId(100), new TransactionId(0), BaseTime, "Match"), "Zero booked ID must throw.");
        ExpectThrows<ArgumentException>(() => new PendingToBookedLink(
            new TransactionId(100), new TransactionId(100), BaseTime, "Match"), "Self-link must throw.");
        ExpectThrows<ArgumentException>(() => new PendingToBookedLink(
            new TransactionId(100), new TransactionId(200), BaseTime, "   "), "Whitespace criterion must throw.");
    }

    private static void VerifyTransactionObservation(Action<bool, string> verify)
    {
        string hash = new('A', 64);
        TransactionObservation obs = new(
            id: new ObservationId(500),
            accountBindingId: new AccountBindingId(10),
            retrievedAt: BaseTime,
            statementFormat: "CAMT.053",
            statementNumber: "2026/001",
            pageToken: "PAGE-2",
            sourceChecksum: hash);

        verify(obs.Id.Value == 500, "Observation ID must match.");
        verify(obs.AccountBindingId.Value == 10, "Binding ID must match.");
        verify(obs.StatementFormat == "CAMT.053", "Statement format must match.");
        verify(obs.StatementNumber == "2026/001", "Statement number must match.");
        verify(obs.PageToken == "PAGE-2", "Page token must match.");
        verify(obs.SourceChecksum == hash, "Checksum must match.");

        ExpectThrows<ArgumentException>(() => new TransactionObservation(
            new ObservationId(0), new AccountBindingId(10), BaseTime, "MT940"), "Zero ID must throw.");
        ExpectThrows<ArgumentException>(() => new TransactionObservation(
            new ObservationId(1), new AccountBindingId(0), BaseTime, "MT940"), "Zero binding ID must throw.");
        ExpectThrows<ArgumentException>(() => new TransactionObservation(
            new ObservationId(1), new AccountBindingId(1), BaseTime, "   "), "Empty format must throw.");
        ExpectThrows<ArgumentException>(() => new TransactionObservation(
            new ObservationId(1), new AccountBindingId(1), BaseTime, "MT940", sourceChecksum: "SHORT"),
            "Invalid checksum length must throw.");
    }

    private static void VerifyLedgerRevisionIntegration(Action<bool, string> verify)
    {
        // Verify that LocalIdentityLedger allocates Transaction, TransactionRevision,
        // and AnnotationRevision and validates revision predecessors cleanly without cycles.
        IdentityValidationResult openResult = LocalIdentityLedger.TryOpen(new IdentitySnapshot(1, []), out LocalIdentityLedger? ledger);
        verify(openResult.IsValid && ledger is not null, "Empty initial snapshot must open valid ledger.");

        using (IdentityAllocationBatch batch = ledger!.BeginBatch())
        {
            LocalEntityId txId = batch.Allocate(LocalEntityKind.Transaction);
            LocalEntityId rev1Id = batch.Allocate(LocalEntityKind.TransactionRevision,
                references: [new LocalEntityReference(txId, LocalEntityKind.Transaction)]);
            LocalEntityId rev2Id = batch.Allocate(LocalEntityKind.TransactionRevision,
                references: [new LocalEntityReference(txId, LocalEntityKind.Transaction)],
                supersedesId: rev1Id);

            LocalEntityId ann1Id = batch.Allocate(LocalEntityKind.AnnotationRevision,
                references: [new LocalEntityReference(txId, LocalEntityKind.Transaction)]);
            LocalEntityId ann2Id = batch.Allocate(LocalEntityKind.AnnotationRevision,
                references: [new LocalEntityReference(txId, LocalEntityKind.Transaction)],
                supersedesId: ann1Id);

            batch.Commit();
        }

        IdentitySnapshot snapshot = ledger.Snapshot;
        verify(snapshot.Records.Count == 5, "Committed batch must have 5 identity records.");
        IdentityValidationResult validationResult = IdentitySnapshotValidator.Validate(snapshot);
        verify(validationResult.IsValid, "IdentitySnapshotValidator must report zero issues for transaction revisions.");

        LocalEntityRecord rev2 = snapshot.Records.First(r => r.Kind == LocalEntityKind.TransactionRevision && r.SupersedesId.HasValue);
        verify(rev2.SupersedesId.HasValue, "Rev2 must supersede Rev1.");
    }

    private static void ExpectThrows<T>(Action action, string message) where T : Exception
    {
        try
        {
            action();
            throw new InvalidOperationException($"Expected exception of type {typeof(T).Name} but none was thrown: {message}");
        }
        catch (T)
        {
            // Expected
        }
    }
}
