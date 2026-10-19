using System.Text;
using Broiler.Fond.Kernel.Domain;
using Broiler.Fond.Kernel.Domain.Identifiers;
using Broiler.Fond.Kernel.Domain.Transactions;
using Broiler.Fond.Kernel.Domain.Transactions.Sync;
using Broiler.Fond.Kernel.FinTs;
using Broiler.Fond.Kernel.Formats.Camt;
using Broiler.Fond.Kernel.Formats.Mt940;

namespace Broiler.Fond.Kernel.Tests;

internal static class StatementSyncTests
{
    internal static void Run(Action<bool, string> check)
    {
        int count = 0;
        void Verify(bool result, string message) { count++; check(result, message); }

        var accountInput = new FinTsReadAccountInput(
            number: "1234567890",
            subaccount: "00",
            country: "280",
            institution: "10020030",
            iban: "DE89370400440532013000",
            bic: "TESTBIC");

        var nationalOnlyAccount = new FinTsReadAccountInput(
            number: "1234567890",
            subaccount: "00",
            country: "280",
            institution: "10020030",
            iban: "",
            bic: "");

        // -------------------------------------------------------------
        // 1. FinTsStatementRequestWriter: MT940 (HKKAZ)
        // -------------------------------------------------------------
        // v5
        byte[] v5Bytes = FinTsStatementRequestWriter.EncodeMt940(
            dialogueId: "DLG-100",
            messageNumber: 1,
            version: 5,
            account: accountInput,
            allAccounts: false,
            fromDate: new DateOnly(2026, 1, 1),
            toDate: new DateOnly(2026, 1, 31),
            maximumEntries: 50,
            continuationToken: "PAGE-2");

        string v5Str = Encoding.Latin1.GetString(v5Bytes);
        Verify(v5Str.Contains("HKKAZ:2:5+DE89370400440532013000:TESTBIC:1234567890:00:280:10020030+N+20260101+20260131+50+PAGE-2'"),
            "HKKAZ v5 wire format must match specification.");
        Verify(v5Str.StartsWith("HNHBK:1:3+") && v5Str.EndsWith("'HNHBS:3:1+1'"),
            "HKKAZ v5 message must be wrapped in valid FinTs unsigned envelope.");

        // v5 national only
        byte[] v5NatBytes = FinTsStatementRequestWriter.EncodeMt940(
            dialogueId: "DLG-100",
            messageNumber: 2,
            version: 5,
            account: nationalOnlyAccount);
        string v5NatStr = Encoding.Latin1.GetString(v5NatBytes);
        Verify(v5NatStr.Contains("HKKAZ:2:5+1234567890:00:280:10020030+N++"),
            "HKKAZ v5 national-only account must format without leading IBAN:BIC delimiters.");

        // v6
        byte[] v6Bytes = FinTsStatementRequestWriter.EncodeMt940(
            dialogueId: "DLG-100",
            messageNumber: 1,
            version: 6,
            account: accountInput,
            allAccounts: true);
        string v6Str = Encoding.Latin1.GetString(v6Bytes);
        Verify(v6Str.Contains("HKKAZ:2:6+DE89370400440532013000:TESTBIC:1234567890:00:280:10020030+J++'"),
            "HKKAZ v6 all-accounts request must emit +J.");

        // v7 with format
        byte[] v7Bytes = FinTsStatementRequestWriter.EncodeMt940(
            dialogueId: "DLG-100",
            messageNumber: 1,
            version: 7,
            account: accountInput,
            format: "MT940");
        string v7Str = Encoding.Latin1.GetString(v7Bytes);
        Verify(v7Str.Contains("HKKAZ:2:7+DE89370400440532013000:TESTBIC:1234567890:00:280:10020030+N+++++MT940'"),
            "HKKAZ v7 with format parameter must append format element.");

        // MT940 Rejections
        try
        {
            FinTsStatementRequestWriter.EncodeMt940("DLG-1", 1, version: 4, accountInput);
            Verify(false, "HKKAZ version 4 must be rejected.");
        }
        catch (FinTsFormatException ex)
        {
            Verify(ex.Error == FinTsSyntaxError.UnsupportedReadDataVersion, "Unsupported HKKAZ version must throw UnsupportedReadDataVersion.");
        }

        try
        {
            FinTsStatementRequestWriter.EncodeMt940("DLG-1", 1, version: 6, accountInput,
                fromDate: new DateOnly(2026, 2, 1), toDate: new DateOnly(2026, 1, 1));
            Verify(false, "HKKAZ with fromDate > toDate must be rejected.");
        }
        catch (FinTsFormatException ex)
        {
            Verify(ex.Error == FinTsSyntaxError.InvalidReadData, "Inverted date range must throw InvalidReadData.");
        }

        try
        {
            FinTsStatementRequestWriter.EncodeMt940("DLG-1", 1, version: 6, accountInput, maximumEntries: 0);
            Verify(false, "HKKAZ with maximumEntries = 0 must be rejected.");
        }
        catch (FinTsFormatException ex)
        {
            Verify(ex.Error == FinTsSyntaxError.InvalidReadData, "Zero max entries must throw InvalidReadData.");
        }

        try
        {
            FinTsStatementRequestWriter.EncodeMt940("0", 1, version: 6, accountInput);
            Verify(false, "HKKAZ with dialogueId '0' must be rejected.");
        }
        catch (FinTsFormatException ex)
        {
            Verify(ex.Error == FinTsSyntaxError.InvalidReadData, "Invalid dialogue ID must throw InvalidReadData.");
        }

        using (var cts = new CancellationTokenSource())
        {
            cts.Cancel();
            try
            {
                FinTsStatementRequestWriter.EncodeMt940("DLG-1", 1, 6, accountInput, cancellationToken: cts.Token);
                Verify(false, "Cancelled token must throw OperationCanceledException.");
            }
            catch (OperationCanceledException)
            {
                Verify(true, "Cancelled token must cancel request encoding.");
            }
        }

        // -------------------------------------------------------------
        // 2. FinTsStatementRequestWriter: CAMT (HKCAZ)
        // -------------------------------------------------------------
        byte[] camtBytes = FinTsStatementRequestWriter.EncodeCamt(
            dialogueId: "DLG-200",
            messageNumber: 2,
            version: 1,
            account: accountInput,
            camtFormat: "camt.053.001.02",
            allAccounts: false,
            fromDate: new DateOnly(2026, 3, 1),
            toDate: new DateOnly(2026, 3, 31),
            maximumEntries: 100,
            continuationToken: "CONT-TOKEN-1");

        string camtStr = Encoding.Latin1.GetString(camtBytes);
        Verify(camtStr.Contains("HKCAZ:2:1+DE89370400440532013000:TESTBIC:1234567890:00:280:10020030+camt.053.001.02+N+20260301+20260331+100+CONT-TOKEN-1'"),
            "HKCAZ v1 wire format must encode format URN and continuation token.");

        // CAMT Rejections
        try
        {
            FinTsStatementRequestWriter.EncodeCamt("DLG-1", 1, version: 3, accountInput, "camt.053.001.02");
            Verify(false, "HKCAZ version 3 must be rejected.");
        }
        catch (FinTsFormatException ex)
        {
            Verify(ex.Error == FinTsSyntaxError.UnsupportedReadDataVersion, "Unsupported HKCAZ version must throw UnsupportedReadDataVersion.");
        }

        try
        {
            FinTsStatementRequestWriter.EncodeCamt("DLG-1", 1, version: 1, accountInput, camtFormat: "   ");
            Verify(false, "HKCAZ with empty format must be rejected.");
        }
        catch (FinTsFormatException ex)
        {
            Verify(ex.Error == FinTsSyntaxError.InvalidReadData, "Empty CAMT format must throw InvalidReadData.");
        }

        // -------------------------------------------------------------
        // 3. StatementSyncCheckpoint Invariants & Transitions
        // -------------------------------------------------------------
        var accountId = new AccountId(101);
        var incarnationId = new AccountIncarnationId(202);
        var bindingId = new AccountBindingId(303);
        var now = DateTimeOffset.UtcNow;

        var initialCp = new StatementSyncCheckpoint(
            accountId,
            incarnationId,
            bindingId,
            new DateOnly(2026, 1, 1),
            new DateOnly(2026, 1, 31),
            now,
            lastContinuationToken: null,
            isComplete: false,
            observedTransactionCount: 0);

        Verify(!initialCp.IsComplete && initialCp.LastContinuationToken == null && initialCp.ObservedTransactionCount == 0,
            "Initial checkpoint must be incomplete with 0 count.");

        var contCp = initialCp.WithContinuation("TOKEN-PAGE-2", 15, now.AddSeconds(1));
        Verify(!contCp.IsComplete && contCp.LastContinuationToken == "TOKEN-PAGE-2" && contCp.ObservedTransactionCount == 15,
            "Checkpoint WithContinuation must update token and accumulate count.");

        var compCp = contCp.WithCompletion(now.AddSeconds(2), 25);
        Verify(compCp.IsComplete && compCp.LastContinuationToken == null && compCp.ObservedTransactionCount == 25,
            "Checkpoint WithCompletion must set IsComplete=true and clear continuation token.");

        try
        {
            _ = new StatementSyncCheckpoint(new AccountId(0), incarnationId, bindingId, new DateOnly(2026, 1, 1), new DateOnly(2026, 1, 2), now);
            Verify(false, "Zero AccountId must be rejected.");
        }
        catch (ArgumentException)
        {
            Verify(true, "Zero AccountId must be rejected.");
        }

        try
        {
            _ = new StatementSyncCheckpoint(accountId, incarnationId, bindingId, new DateOnly(2026, 2, 1), new DateOnly(2026, 1, 1), now);
            Verify(false, "Inverted checkpoint window must be rejected.");
        }
        catch (ArgumentException)
        {
            Verify(true, "Inverted checkpoint window must be rejected.");
        }

        // -------------------------------------------------------------
        // 4. StatementOverlapSyncPlanner: Query Window Calculation
        // -------------------------------------------------------------
        var today = new DateOnly(2026, 6, 15);

        // Initial sync
        var (initFrom, initTo) = StatementOverlapSyncPlanner.CalculateQueryWindow(today, previousCheckpoint: null, initialWindowDays: 90);
        Verify(initFrom == new DateOnly(2026, 3, 17) && initTo == today,
            "Initial sync query window must span initialWindowDays back to today.");

        // Interrupted sync resume
        var interruptedCp = new StatementSyncCheckpoint(
            accountId, incarnationId, bindingId,
            new DateOnly(2026, 2, 1), new DateOnly(2026, 2, 28),
            now, lastContinuationToken: "PAGE-RESUME", isComplete: false);
        var (resumeFrom, resumeTo) = StatementOverlapSyncPlanner.CalculateQueryWindow(today, interruptedCp);
        Verify(resumeFrom == new DateOnly(2026, 2, 1) && resumeTo == new DateOnly(2026, 2, 28),
            "Interrupted sync query window must resume the exact previous incomplete window.");

        // Incremental sync with overlap
        var completedCp = new StatementSyncCheckpoint(
            accountId, incarnationId, bindingId,
            new DateOnly(2026, 1, 1), new DateOnly(2026, 5, 1),
            now, lastContinuationToken: null, isComplete: true);
        var (incFrom, incTo) = StatementOverlapSyncPlanner.CalculateQueryWindow(today, completedCp, overlapDays: 30);
        Verify(incFrom == new DateOnly(2026, 4, 1) && incTo == today,
            "Completed incremental sync must overlap 30 days prior to previous WindowEnd.");

        // -------------------------------------------------------------
        // 5. StatementOverlapSyncPlanner: Multiplicity & Overlap Deduplication
        // -------------------------------------------------------------
        ulong currentTxId = 1000;
        ulong currentRevId = 2000;
        ulong currentObsId = 3000;

        TransactionId NextTxId() => new(++currentTxId);
        TransactionRevisionId NextRevId() => new(++currentRevId);
        ObservationId NextObsId() => new(++currentObsId);

        // MT940 statement containing 3 entries, including TWO identical rows (same amount, dates, counterparty, remittance)
        var identicalEntry1 = new Mt940Entry(
            valueDate: new DateOnly(2026, 4, 10),
            bookingDate: new DateOnly(2026, 4, 10),
            isCredit: false,
            isReversal: false,
            amount: new Money(10.00m, "EUR"),
            transactionCode: "NTRF",
            customerReference: "NONREF",
            bankReference: null,
            remittanceText: "CASH WITHDRAWAL",
            structuredSubfields: new Dictionary<string, string> { ["32"] = "ATM WITHDRAWAL" });

        var identicalEntry2 = new Mt940Entry(
            valueDate: new DateOnly(2026, 4, 10),
            bookingDate: new DateOnly(2026, 4, 10),
            isCredit: false,
            isReversal: false,
            amount: new Money(10.00m, "EUR"),
            transactionCode: "NTRF",
            customerReference: "NONREF",
            bankReference: null,
            remittanceText: "CASH WITHDRAWAL",
            structuredSubfields: new Dictionary<string, string> { ["32"] = "ATM WITHDRAWAL" });

        var uniqueEntry = new Mt940Entry(
            valueDate: new DateOnly(2026, 4, 12),
            bookingDate: new DateOnly(2026, 4, 12),
            isCredit: true,
            isReversal: false,
            amount: new Money(55.20m, "EUR"),
            transactionCode: "NTRF",
            customerReference: "SAL2026",
            bankReference: null,
            remittanceText: "SALARY",
            structuredSubfields: new Dictionary<string, string> { ["32"] = "EMPLOYER GMBH" });

        var openingBal = new Mt940Balance(isCredit: true, date: new DateOnly(2026, 4, 1), amount: new Money(1000.00m, "EUR"), balanceType: "60F");
        var closingBal = new Mt940Balance(isCredit: true, date: new DateOnly(2026, 4, 30), amount: new Money(1035.20m, "EUR"), balanceType: "62F");

        var testStatement = new Mt940Statement(
            transactionReference: "TR-2026-01",
            relatedReference: null,
            accountIdentification: "1234567890",
            statementNumber: "2026/01",
            sequenceNumber: "1",
            openingBalance: openingBal,
            closingBalance: closingBal,
            entries: [identicalEntry1, identicalEntry2, uniqueEntry]);

        // First sync pass: empty existing revisions
        var sync1 = StatementOverlapSyncPlanner.CorrelateMt940(
            testStatement, accountId, incarnationId, bindingId,
            existingRevisions: [],
            existingLinks: [],
            previousCheckpoint: null,
            NextTxId, NextRevId, NextObsId, now);

        Verify(sync1.NewTransactions.Count == 3, "First sync must create 3 new transactions.");
        Verify(sync1.ExistingTransactionsMatchedCount == 0, "First sync matched count must be 0.");

        // Verify multiplicity preserved: two distinct transactions with distinct IDs created for the two identical rows!
        var firstIdenticalTx = sync1.NewTransactions[0];
        var secondIdenticalTx = sync1.NewTransactions[1];
        Verify(firstIdenticalTx.Revision.TransactionId != secondIdenticalTx.Revision.TransactionId,
            "Multiplicity preservation: identical entries in the same statement must yield distinct TransactionIds.");
        Verify(firstIdenticalTx.Revision.Amount == secondIdenticalTx.Revision.Amount &&
               firstIdenticalTx.Revision.BookingDate == secondIdenticalTx.Revision.BookingDate,
            "Both identical transactions must retain exact decimal money and booking date.");

        // Gather existing revisions for repeated syncs
        List<TransactionRevision> accumulatedRevisions = sync1.NewTransactions.Select(t => t.Revision).ToList();

        // Perform TEN repeated overlapping syncs with the exact same statement entries
        for (int i = 1; i <= 10; i++)
        {
            var repeatSync = StatementOverlapSyncPlanner.CorrelateMt940(
                testStatement, accountId, incarnationId, bindingId,
                existingRevisions: accumulatedRevisions,
                existingLinks: [],
                previousCheckpoint: sync1.UpdatedCheckpoint,
                NextTxId, NextRevId, NextObsId, now.AddMinutes(i));

            Verify(repeatSync.NewTransactions.Count == 0,
                $"Overlapping sync run {i} must create 0 new transactions (zero duplicate transactions invariant).");
            Verify(repeatSync.ExistingTransactionsMatchedCount == 3,
                $"Overlapping sync run {i} must match all 3 existing transactions.");
        }

        // -------------------------------------------------------------
        // 6. StatementOverlapSyncPlanner: CAMT Multiplicity & Overlap
        // -------------------------------------------------------------
        var camtEntry1 = new CamtEntry(
            entryReference: "REF-101",
            amount: new Money(25.00m, "EUR"),
            isCredit: false,
            status: TransactionStatus.Booked,
            bookingDate: new DateOnly(2026, 4, 15),
            valueDate: new DateOnly(2026, 4, 15),
            bankTransactionCode: "PMNT",
            endToEndId: null,
            mandateId: null,
            creditorId: null,
            counterpartyName: "SUPERMARKET",
            counterpartyIban: "DE89370400440532013999",
            counterpartyBic: null,
            remittanceText: "GROCERIES",
            returnReasonCode: null);

        var camtEntry2 = new CamtEntry(
            entryReference: "REF-102",
            amount: new Money(25.00m, "EUR"),
            isCredit: false,
            status: TransactionStatus.Booked,
            bookingDate: new DateOnly(2026, 4, 15),
            valueDate: new DateOnly(2026, 4, 15),
            bankTransactionCode: "PMNT",
            endToEndId: null,
            mandateId: null,
            creditorId: null,
            counterpartyName: "SUPERMARKET",
            counterpartyIban: "DE89370400440532013999",
            counterpartyBic: null,
            remittanceText: "GROCERIES",
            returnReasonCode: null);

        var camtStatement = new CamtStatement(
            messageId: "MSG-001",
            creationDateTime: now,
            statementId: "CAMT-STMT-2026-001",
            accountIban: "DE89370400440532013000",
            accountCurrency: "EUR",
            openingBalance: null,
            closingBalance: null,
            entries: [camtEntry1, camtEntry2]);

        var camtSync1 = StatementOverlapSyncPlanner.CorrelateCamt(
            camtStatement, accountId, incarnationId, bindingId,
            existingRevisions: [],
            existingLinks: [],
            previousCheckpoint: null,
            NextTxId, NextRevId, NextObsId, now);

        Verify(camtSync1.NewTransactions.Count == 2, "CAMT first sync must create 2 new transactions.");
        Verify(camtSync1.NewTransactions[0].Revision.TransactionId != camtSync1.NewTransactions[1].Revision.TransactionId,
            "CAMT multiplicity preservation: identical rows must yield distinct TransactionIds.");

        var camtRevisions = camtSync1.NewTransactions.Select(t => t.Revision).ToList();
        for (int i = 1; i <= 10; i++)
        {
            var camtRepeatSync = StatementOverlapSyncPlanner.CorrelateCamt(
                camtStatement, accountId, incarnationId, bindingId,
                existingRevisions: camtRevisions,
                existingLinks: [],
                previousCheckpoint: camtSync1.UpdatedCheckpoint,
                NextTxId, NextRevId, NextObsId, now.AddMinutes(i));

            Verify(camtRepeatSync.NewTransactions.Count == 0,
                $"CAMT overlapping sync run {i} must create 0 new transactions.");
            Verify(camtRepeatSync.ExistingTransactionsMatchedCount == 2,
                $"CAMT overlapping sync run {i} must match 2 existing transactions.");
        }

        // -------------------------------------------------------------
        // 7. StatementOverlapSyncPlanner: Pending-to-Booked Reconciliation
        // -------------------------------------------------------------
        var pendingTxId = NextTxId();
        var pendingRevId = NextRevId();
        var pendingObsId = NextObsId();

        var pendingRevision = new TransactionRevision(
            id: pendingRevId,
            transactionId: pendingTxId,
            observationId: pendingObsId,
            accountId: accountId,
            accountIncarnationId: incarnationId,
            accountBindingId: bindingId,
            status: TransactionStatus.Pending,
            kind: TransactionKind.Debit,
            amount: new Money(49.99m, "EUR"),
            isCredit: false,
            bookingDate: new DateOnly(2026, 4, 1),
            valueDate: new DateOnly(2026, 4, 1),
            bankTimestamp: now,
            retrievedAt: now,
            counterparty: new Counterparty("ONLINE STORE", "DE12345678901234567890", null),
            remittance: new RemittanceInformation(unstructuredText: "ORDER 123", endToEndId: "E2E-ONLINE-999"));

        // Booked incoming entry matching the pending EndToEndId
        var bookedEntryWithE2E = new Mt940Entry(
            valueDate: new DateOnly(2026, 4, 3),
            bookingDate: new DateOnly(2026, 4, 3),
            isCredit: false,
            isReversal: false,
            amount: new Money(49.99m, "EUR"),
            transactionCode: "NTRF",
            customerReference: "NONREF",
            bankReference: null,
            remittanceText: "ORDER 123",
            structuredSubfields: new Dictionary<string, string>
            {
                ["32"] = "ONLINE STORE",
                ["34"] = "DE12345678901234567890"
            });

        // Add EndToEndId into entry via CustomerReference or RemittanceText representation
        // (In Mt940Entry, EndToEndId is read from StructuredSubfields["31"])
        var bookedEntryWithE2ESubfield = new Mt940Entry(
            valueDate: new DateOnly(2026, 4, 3),
            bookingDate: new DateOnly(2026, 4, 3),
            isCredit: false,
            isReversal: false,
            amount: new Money(49.99m, "EUR"),
            transactionCode: "NTRF",
            customerReference: "NONREF",
            bankReference: null,
            remittanceText: "ORDER 123",
            structuredSubfields: new Dictionary<string, string>
            {
                ["21"] = "E2E-ONLINE-999",
                ["32"] = "ONLINE STORE",
                ["31"] = "DE12345678901234567890"
            });

        var bookedStmt = new Mt940Statement(
            transactionReference: "TR-2026-02",
            relatedReference: null,
            accountIdentification: "1234567890",
            statementNumber: "2026/02",
            sequenceNumber: "1",
            openingBalance: openingBal,
            closingBalance: closingBal,
            entries: [bookedEntryWithE2ESubfield]);

        var reconcileSync = StatementOverlapSyncPlanner.CorrelateMt940(
            bookedStmt, accountId, incarnationId, bindingId,
            existingRevisions: [pendingRevision],
            existingLinks: [],
            previousCheckpoint: null,
            NextTxId, NextRevId, NextObsId, now);

        Verify(reconcileSync.NewTransactions.Count == 1, "Booked transaction must be recorded.");
        Verify(reconcileSync.NewLinks.Count == 1, "Reconciliation link must be established.");
        var link = reconcileSync.NewLinks[0];
        Verify(link.PendingTransactionId == pendingTxId, "Link must reference original pending TransactionId.");
        Verify(link.BookedTransactionId == reconcileSync.NewTransactions[0].Revision.TransactionId,
            "Link must reference newly booked TransactionId.");
        Verify(link.MatchCriterion == "EndToEndId", "Link criterion must be EndToEndId.");

        // Fallback heuristic pending reconciliation (within 7 days, same amount/counterparty, no E2E)
        var pendingHeuristicTxId = NextTxId();
        var pendingHeuristicRev = new TransactionRevision(
            id: NextRevId(),
            transactionId: pendingHeuristicTxId,
            observationId: NextObsId(),
            accountId: accountId,
            accountIncarnationId: incarnationId,
            accountBindingId: bindingId,
            status: TransactionStatus.Pending,
            kind: TransactionKind.CardPayment,
            amount: new Money(14.50m, "EUR"),
            isCredit: false,
            bookingDate: new DateOnly(2026, 4, 5),
            valueDate: new DateOnly(2026, 4, 5),
            bankTimestamp: now,
            retrievedAt: now,
            counterparty: new Counterparty("COFFEE SHOP", null, null));

        var bookedHeuristicEntry = new Mt940Entry(
            valueDate: new DateOnly(2026, 4, 7), // 2 days diff <= 7 days
            bookingDate: new DateOnly(2026, 4, 7),
            isCredit: false,
            isReversal: false,
            amount: new Money(14.50m, "EUR"),
            transactionCode: "CCRD",
            customerReference: "NONREF",
            bankReference: null,
            remittanceText: "COFFEE",
            structuredSubfields: new Dictionary<string, string> { ["32"] = "COFFEE SHOP" });

        var heuristicStmt = new Mt940Statement(
            transactionReference: "TR-2026-03",
            relatedReference: null,
            accountIdentification: "1234567890",
            statementNumber: "2026/03",
            sequenceNumber: "1",
            openingBalance: openingBal,
            closingBalance: closingBal,
            entries: [bookedHeuristicEntry]);

        var heuristicSync = StatementOverlapSyncPlanner.CorrelateMt940(
            heuristicStmt, accountId, incarnationId, bindingId,
            existingRevisions: [pendingHeuristicRev],
            existingLinks: [],
            previousCheckpoint: null,
            NextTxId, NextRevId, NextObsId, now);

        Verify(heuristicSync.NewLinks.Count == 1, "Heuristic match within 7 days must create link.");
        Verify(heuristicSync.NewLinks[0].MatchCriterion == "ExactAmountAndCounterparty",
            "Heuristic link criterion must be ExactAmountAndCounterparty.");

        // Non-matching pending: date diff exceeds 7 days
        var stalePendingRev = new TransactionRevision(
            id: NextRevId(),
            transactionId: NextTxId(),
            observationId: NextObsId(),
            accountId: accountId,
            accountIncarnationId: incarnationId,
            accountBindingId: bindingId,
            status: TransactionStatus.Pending,
            kind: TransactionKind.Debit,
            amount: new Money(14.50m, "EUR"),
            isCredit: false,
            bookingDate: new DateOnly(2026, 3, 1), // 37 days earlier
            valueDate: new DateOnly(2026, 3, 1),
            bankTimestamp: now,
            retrievedAt: now,
            counterparty: new Counterparty("COFFEE SHOP", null, null));

        var noMatchSync = StatementOverlapSyncPlanner.CorrelateMt940(
            heuristicStmt, accountId, incarnationId, bindingId,
            existingRevisions: [stalePendingRev],
            existingLinks: [],
            previousCheckpoint: null,
            NextTxId, NextRevId, NextObsId, now);

        Verify(noMatchSync.NewLinks.Count == 0, "Pending entry outside 7-day window must NOT be heuristically linked.");

        // -------------------------------------------------------------
        // 8. StatementContinuationCoordinator
        // -------------------------------------------------------------
        var deadline = now.AddMinutes(2);
        var coordinator = new StatementContinuationCoordinator(accountId, bindingId, deadline);

        Verify(coordinator.Status == StatementPaginationStatus.Active, "Initial pagination status must be Active.");
        Verify(!coordinator.CanContinue, "CanContinue must be false when no continuation token is present.");
        Verify(coordinator.PageCount == 0, "Initial page count must be 0.");

        // Page 1
        coordinator.RecordPageResponse(hasMoreEntries: true, nextContinuationToken: "TOKEN-PAGE-2", now);
        Verify(coordinator.PageCount == 1 && coordinator.CanContinue && coordinator.CurrentContinuationToken == "TOKEN-PAGE-2",
            "Page 1 recording must advance count and update continuation token.");

        // Page 2
        coordinator.RecordPageResponse(hasMoreEntries: true, nextContinuationToken: "TOKEN-PAGE-3", now.AddSeconds(5));
        Verify(coordinator.PageCount == 2 && coordinator.ConsumedPageTokens.Count == 1 && coordinator.ConsumedPageTokens[0] == "TOKEN-PAGE-2",
            "Page 2 recording must append consumed page token.");

        // Page 3 (final)
        coordinator.RecordPageResponse(hasMoreEntries: false, nextContinuationToken: null, now.AddSeconds(10));
        Verify(coordinator.PageCount == 3 && coordinator.Status == StatementPaginationStatus.Completed && !coordinator.CanContinue,
            "Final page recording must set Status=Completed and clear token.");

        // Terminal state guards
        try
        {
            coordinator.RecordPageResponse(true, "TOKEN-4", now);
            Verify(false, "Recording on completed coordinator must throw InvalidOperationException.");
        }
        catch (InvalidOperationException)
        {
            Verify(true, "Recording on completed coordinator must throw InvalidOperationException.");
        }

        // Page limit reached (128 pages)
        var limitCoordinator = new StatementContinuationCoordinator(accountId, bindingId, deadline);
        for (int p = 1; p < 128; p++)
        {
            limitCoordinator.RecordPageResponse(hasMoreEntries: true, nextContinuationToken: $"PAGE-{p + 1}", now);
        }
        Verify(limitCoordinator.PageCount == 127 && limitCoordinator.Status == StatementPaginationStatus.Active,
            "Coordinator at page 127 must remain Active.");

        limitCoordinator.RecordPageResponse(hasMoreEntries: true, nextContinuationToken: "PAGE-129", now);
        Verify(limitCoordinator.PageCount == 128 && limitCoordinator.Status == StatementPaginationStatus.PageLimitReached,
            "Coordinator at page 128 with more entries must reach PageLimitReached.");
        Verify(limitCoordinator.CurrentContinuationToken == "PAGE-129",
            "PageLimitReached must retain nextContinuationToken for future continuation.");

        // Monotonic deadline expiration
        var deadlineCoordinator = new StatementContinuationCoordinator(accountId, bindingId, deadline);
        deadlineCoordinator.RecordPageResponse(hasMoreEntries: true, nextContinuationToken: "LATE-TOKEN", currentTimestamp: deadline.AddSeconds(1));
        Verify(deadlineCoordinator.Status == StatementPaginationStatus.DeadlineExpired,
            "Timestamp after deadline must set DeadlineExpired.");
        Verify(deadlineCoordinator.CurrentContinuationToken == "LATE-TOKEN",
            "DeadlineExpired must preserve next token for resumption.");

        // Terminal failure
        var failCoordinator = new StatementContinuationCoordinator(accountId, bindingId, deadline);
        failCoordinator.RecordPageResponse(hasMoreEntries: true, nextContinuationToken: "TOKEN", currentTimestamp: now, hasTerminalError: true);
        Verify(failCoordinator.Status == StatementPaginationStatus.Failed && failCoordinator.CurrentContinuationToken == null,
            "Terminal error must set Status=Failed and clear token.");

        // MarkInterrupted
        var interruptCoordinator = new StatementContinuationCoordinator(accountId, bindingId, deadline, initialContinuationToken: "RESUME-TOKEN");
        Verify(interruptCoordinator.CanContinue && interruptCoordinator.CurrentContinuationToken == "RESUME-TOKEN",
            "Coordinator initialized with continuation token can continue.");
        interruptCoordinator.MarkInterrupted();
        Verify(interruptCoordinator.Status == StatementPaginationStatus.Interrupted && interruptCoordinator.CurrentContinuationToken == "RESUME-TOKEN",
            "MarkInterrupted must transition status to Interrupted and preserve token.");

        Console.WriteLine($"Statement pagination, overlap sync, and reconciliation (M2-04): {count} checks completed.");
    }
}
