using System.Globalization;
using System.Text;
using Broiler.Fond.Kernel.Domain;
using Broiler.Fond.Kernel.Domain.Payments;
using Broiler.Fond.Kernel.FinTs;
using Broiler.Fond.Kernel.FinTs.Transport;

namespace Broiler.Fond.Kernel.Tests.Payments;

public static class SepaCreditTransferTests
{
    public static void RunAll()
    {
        int checkCount = 0;
        Action<bool, string> check = (condition, message) =>
        {
            if (!condition)
            {
                throw new InvalidOperationException($"SEPA credit transfer assertion failed: {message}");
            }
            checkCount++;
        };

        TestPain001DocumentWriter(check);
        TestFinTsSepaTransferRequestWriter(check);
        TestFinTsSepaTransferResponseParser(check);
        TestSepaTransferExecutionCoordinatorSuccess(check).GetAwaiter().GetResult();
        TestSepaTransferExecutionCoordinatorScaFlow(check).GetAwaiter().GetResult();
        TestFailureInjectionResilience(check).GetAwaiter().GetResult();

        Console.WriteLine($"SEPA credit transfer wire codecs and SCA (M4-04): {checkCount} checks completed successfully.");
    }

    private static void TestPain001DocumentWriter(Action<bool, string> check)
    {
        DateTimeOffset now = new(2026, 10, 19, 11, 0, 0, TimeSpan.Zero);
        DateOnly execDate = new(2026, 10, 25);
        string endToEndId = "BF-20261019-1122334455667788";

        // 1. Valid pain.001 with unstructured remittance
        string xml = SepaPainDocumentWriter.GenerateXml(
            messageId: endToEndId,
            initiatingPartyName: "Max Mustermann",
            debtorName: "Max Mustermann",
            debtorIban: "DE89370400440532013000",
            debtorBic: "DBBADE33XXX",
            paymentInfoId: endToEndId,
            executionDate: execDate,
            endToEndId: endToEndId,
            amount: 149.99m,
            creditorName: "Erika Mustermann",
            creditorIban: "DE89370400440532013000",
            creditorBic: "DBBADE33XXX",
            remittance: new PaymentRemittance("Invoice 2026-9901"),
            creationDateTime: now);

        check(xml.Contains("xmlns=\"urn:iso:std:iso:20022:tech:xsd:pain.001.001.03\"", StringComparison.Ordinal), "XML contains standard pain.001.001.03 namespace.");
        check(xml.Contains("<MsgId>BF-20261019-1122334455667788</MsgId>", StringComparison.Ordinal), "XML contains MsgId matching attempt EndToEndId.");
        check(xml.Contains("<CtrlSum>149.99</CtrlSum>", StringComparison.Ordinal), "XML contains exact decimal CtrlSum with 2 decimal places.");
        check(xml.Contains("<InstdAmt Ccy=\"EUR\">149.99</InstdAmt>", StringComparison.Ordinal), "XML contains EUR InstdAmt.");
        check(xml.Contains("<ReqdExctnDt>2026-10-25</ReqdExctnDt>", StringComparison.Ordinal), "XML contains correct requested execution date.");
        check(xml.Contains("<Ustrd>Invoice 2026-9901</Ustrd>", StringComparison.Ordinal), "XML contains unstructured remittance text.");
        check(xml.Contains("<Cd>SEPA</Cd>", StringComparison.Ordinal), "XML specifies SEPA service level.");

        // 2. Structured creditor reference
        string xmlStrd = SepaPainDocumentWriter.GenerateXml(
            messageId: endToEndId,
            initiatingPartyName: "Max Mustermann",
            debtorName: "Max Mustermann",
            debtorIban: "DE89370400440532013000",
            debtorBic: null, // Debtor BIC omitted -> NOTPROVIDED
            paymentInfoId: endToEndId,
            executionDate: execDate,
            endToEndId: endToEndId,
            amount: 50.00m,
            creditorName: "Telecom Corp",
            creditorIban: "DE89370400440532013000",
            creditorBic: null,
            remittance: new PaymentRemittance(structuredReference: "RF18539007547034"),
            creationDateTime: now);

        check(xmlStrd.Contains("<Id>NOTPROVIDED</Id>", StringComparison.Ordinal), "Debtor BIC omitted generates NOTPROVIDED placeholder.");
        check(xmlStrd.Contains("<Cd>SCOR</Cd>", StringComparison.Ordinal), "Structured remittance specifies SCOR code.");
        check(xmlStrd.Contains("<Ref>RF18539007547034</Ref>", StringComparison.Ordinal), "Structured creditor reference is present.");

        // 3. Invariant validation checks
        bool invalidAmountThrew = false;
        try
        {
            SepaPainDocumentWriter.GenerateXmlBytes(
                messageId: endToEndId,
                initiatingPartyName: "Max",
                debtorName: "Max",
                debtorIban: "DE89370400440532013000",
                debtorBic: null,
                paymentInfoId: endToEndId,
                executionDate: execDate,
                endToEndId: endToEndId,
                amount: 0.00m, // zero amount
                creditorName: "Erika",
                creditorIban: "DE89370400440532013000",
                creditorBic: null,
                remittance: new PaymentRemittance("Test"));
        }
        catch (ArgumentException)
        {
            invalidAmountThrew = true;
        }
        check(invalidAmountThrew, "Zero amount rejected by pain document generator.");

        bool invalidIbanThrew = false;
        try
        {
            SepaPainDocumentWriter.GenerateXmlBytes(
                messageId: endToEndId,
                initiatingPartyName: "Max",
                debtorName: "Max",
                debtorIban: "DE88370400440532013000", // invalid checksum
                debtorBic: null,
                paymentInfoId: endToEndId,
                executionDate: execDate,
                endToEndId: endToEndId,
                amount: 10.00m,
                creditorName: "Erika",
                creditorIban: "DE89370400440532013000",
                creditorBic: null,
                remittance: new PaymentRemittance("Test"));
        }
        catch (ArgumentException)
        {
            invalidIbanThrew = true;
        }
        check(invalidIbanThrew, "Invalid debtor IBAN rejected by pain document generator.");
    }

    private static void TestFinTsSepaTransferRequestWriter(Action<bool, string> check)
    {
        byte[] painBytes = Encoding.UTF8.GetBytes("<Document>TestPainXml</Document>");

        // 1. Build HKCCS Segment
        byte[] segmentBytes = FinTsSepaTransferRequestWriter.BuildHkccsSegment(
            ordinal: 2,
            version: 1,
            originIban: "DE89370400440532013000",
            originBic: "DBBADE33XXX",
            painXmlBytes: painBytes);

        string segmentStr = Encoding.Latin1.GetString(segmentBytes);
        check(segmentStr.StartsWith("HKCCS:2:1+DE89370400440532013000:DBBADE33XXX+", StringComparison.Ordinal), "HKCCS segment begins with expected header and account.");
        check(segmentStr.Contains($"@{painBytes.Length}@<Document>TestPainXml</Document>'", StringComparison.Ordinal), "HKCCS segment contains @length@ and exact binary bytes followed by single-quote.");

        // 2. Encode complete unsigned message frame
        byte[] wire = FinTsSepaTransferRequestWriter.EncodeUnsigned(
            dialogueId: "D-9988-77",
            messageNumber: 1,
            version: 1,
            originIban: "DE89370400440532013000",
            originBic: "DBBADE33XXX",
            painXmlBytes: painBytes);

        check(wire.Length > 0, "Encoded wire bytes are non-empty.");
        string wireHeader = Encoding.Latin1.GetString(wire.AsSpan(0, 35));
        check(wireHeader.StartsWith("HNHBK:1:3+", StringComparison.Ordinal), "Frame starts with HNHBK.");

        // Verify total size embedded in HNHBK
        string declaredSizeStr = Encoding.ASCII.GetString(wire.AsSpan(10, 12));
        int declaredSize = int.Parse(declaredSizeStr, CultureInfo.InvariantCulture);
        check(declaredSize == wire.Length, "Declared 12-digit size in HNHBK matches total wire byte count.");

        // Parse with FinTsMessageFrame to verify lexical conformance
        FinTsMessageFrame frame = FinTsMessageFrame.Parse(wire);
        check(frame.Syntax.Segments.Count == 3, "Message frame has exactly 3 segments (HNHBK, HKCCS, HNHBS).");
        check(frame.Syntax.Segments[1].Code == "HKCCS", "Middle segment is HKCCS.");
        check(frame.Syntax.Segments[1].Fields.Count >= 3, "HKCCS segment has at least 3 fields.");

        // 3. HKTAN completion segment
        string hktan = FinTsSepaTransferRequestWriter.BuildHktanCompletionSegment(
            ordinal: 2,
            version: 1,
            orderReference: "ORDER-7788",
            tan: "123456");

        check(hktan == "HKTAN:2:1+2+ORDER-7788+123456'", "HKTAN completion segment formatted correctly.");
    }

    private static void TestFinTsSepaTransferResponseParser(Action<bool, string> check)
    {
        // 1. Immediate acceptance with HICCS and 0020
        string responseWireAccepted = Frame(
            "HIRMG:2:2+0010::Nachricht entgegengenommen'" +
            "HIRMS:3:2:2+0020::Auftrag ausgefuehrt'" +
            "HICCS:4:1:2+BANK-REF-100200'");

        FinTsMessageFrame frameAccepted = FinTsMessageFrame.Parse(Encoding.Latin1.GetBytes(responseWireAccepted));
        FinTsResponse respAccepted = FinTsResponse.Parse(frameAccepted);
        FinTsSepaTransferResponseResult resultAccepted = FinTsSepaTransferResponseParser.Parse(respAccepted);

        check(resultAccepted.IsSuccess, "Response with 0020 is success.");
        check(!resultAccepted.IsScaRequired, "0020 does not require SCA.");
        check(!resultAccepted.IsRejected, "0020 is not rejected.");
        check(resultAccepted.TargetState == PaymentState.Accepted, "Target state is Accepted.");
        check(resultAccepted.BankOrderReference == "BANK-REF-100200", "Bank order reference extracted from HICCS.");
        check(resultAccepted.PrimaryReturnCode == "0020", "Primary code is 0020.");

        // 2. Pending execution with 0030
        string responseWirePending = Frame(
            "HIRMG:2:2+0010::Nachricht entgegengenommen'" +
            "HIRMS:3:2:2+0030::Auftrag empfangen, Bearbeitung laeuft'" +
            "HICCS:4:1:2+BANK-REF-PENDING'");

        FinTsResponse respPending = FinTsResponse.Parse(FinTsMessageFrame.Parse(Encoding.Latin1.GetBytes(responseWirePending)));
        FinTsSepaTransferResponseResult resultPending = FinTsSepaTransferResponseParser.Parse(respPending);

        check(resultPending.IsSuccess, "0030 is marked as success.");
        check(resultPending.TargetState == PaymentState.BankPending, "0030 maps to BankPending.");

        // 3. SCA Challenge with HITAN and 3920
        string responseWireSca = Frame(
            "HIRMG:2:2+0010::Nachricht entgegengenommen'" +
            "HIRMS:3:2:2+3920::Zugelassene TAN-Verfahren:920'" +
            "HITAN:4:1:2+920+1+CHALLENGE-REF-42+mobileTAN+Bitte pruefen?: 149.99 EUR an Erika Mustermann'");

        FinTsResponse respSca = FinTsResponse.Parse(FinTsMessageFrame.Parse(Encoding.Latin1.GetBytes(responseWireSca)));
        FinTsSepaTransferResponseResult resultSca = FinTsSepaTransferResponseParser.Parse(respSca);

        check(resultSca.IsScaRequired, "SCA challenge detected.");
        check(resultSca.TargetState == PaymentState.BankPending, "SCA challenge maps to BankPending.");
        check(resultSca.BankOrderReference == "CHALLENGE-REF-42", "Order reference extracted from HITAN.");
        check(resultSca.ChallengeText == "Bitte pruefen: 149.99 EUR an Erika Mustermann", "Challenge text extracted.");

        // 4. Authoritative rejection with 9010
        string responseWireRejected = Frame(
            "HIRMG:2:2+9010::Verarbeitung nicht moeglich'" +
            "HIRMS:3:2:2+9210::Konto nicht verfuegbar'",
            trailerOrdinal: 4);

        FinTsResponse respRejected = FinTsResponse.Parse(FinTsMessageFrame.Parse(Encoding.Latin1.GetBytes(responseWireRejected)));
        FinTsSepaTransferResponseResult resultRejected = FinTsSepaTransferResponseParser.Parse(respRejected);

        check(resultRejected.IsRejected, "Error response marked as rejected.");
        check(resultRejected.TargetState == PaymentState.Rejected, "Rejection maps to Rejected state.");
        check(resultRejected.PrimaryReturnCode == "9210" || resultRejected.PrimaryReturnCode == "9010", "Error code recorded.");
    }

    private static async Task TestSepaTransferExecutionCoordinatorSuccess(Action<bool, string> check)
    {
        DateTimeOffset now = new(2026, 10, 19, 11, 15, 0, TimeSpan.Zero);
        DateOnly execDate = new(2026, 10, 25);

        PaymentDraftRevision revision = new(
            Id: 101,
            DraftId: 1,
            RevisionNumber: 1,
            PredecessorRevisionId: null,
            Timestamp: now,
            SourceAccountId: 10,
            SourceAccountBindingId: 20,
            SourceAccountIncarnationId: 30,
            Beneficiary: new PaymentBeneficiary("Technik Shop", "DE89370400440532013000"),
            Amount: new Money(89.00m, "EUR"),
            Remittance: new PaymentRemittance("Order 5544"),
            ExecutionMode: PaymentExecutionMode.StandardCreditTransfer,
            ExecutionDate: execDate);

        PaymentAttempt attempt = PaymentAttempt.CreatePreSendAttempt(501, revision, now);

        // Pre-configure simulated transport with bank acceptance
        using var transport = new FinTsSimulatedTransport();
        string bankResponseWire = Frame(
            "HIRMG:2:2+0010::Nachricht entgegengenommen'" +
            "HIRMS:3:2:2+0020::Auftrag ausgefuehrt'" +
            "HICCS:4:1:2+BANK-ORDER-999'");

        transport.EnqueueResponse(Encoding.Latin1.GetBytes(bankResponseWire));

        Uri endpoint = new("https://fints.testbank.de/fints");

        SepaTransferExecutionResult result = await SepaTransferExecutionCoordinator.InitiateTransferAsync(
            attempt: attempt,
            draftRevision: revision,
            debtorName: "Max Mustermann",
            debtorIban: "DE89370400440532013000",
            debtorBic: "DBBADE33XXX",
            endpoint: endpoint,
            transport: transport,
            dialogueId: "D-12345",
            messageNumber: 1,
            hkccsVersion: 1,
            transitionIdStart: 1001,
            timestamp: now);

        check(result.IsSuccess, "Transfer initiated successfully.");
        check(!result.IsUnknown, "Outcome is known.");
        check(!result.IsScaRequired, "No further SCA required.");
        check(result.UpdatedAttempt.State == PaymentState.Accepted, "Updated attempt is in Accepted state.");
        check(result.UpdatedAttempt.BankReference == "BANK-ORDER-999", "Bank reference stored on attempt.");
        check(result.UpdatedAttempt.ResponseCode == "0020", "Response code 0020 stored on attempt.");

        // Verify audit transitions: AwaitingAuthorization -> Submitting -> Accepted
        check(result.Transitions.Count == 2, "Exactly 2 transitions recorded.");
        check(result.Transitions[0].FromState == PaymentState.AwaitingAuthorization && result.Transitions[0].ToState == PaymentState.Submitting, "First transition was Pre-Send to Submitting.");
        check(result.Transitions[1].FromState == PaymentState.Submitting && result.Transitions[1].ToState == PaymentState.Accepted, "Second transition was Submitting to Accepted.");

        // Verify receipt
        PaymentSubmissionReceipt receipt = result.Receipt;
        check(receipt.AttemptId == 501, "Receipt has attempt ID.");
        check(receipt.EndToEndId == attempt.EndToEndId, "Receipt has attempt EndToEndId.");
        check(receipt.BankReference == "BANK-ORDER-999", "Receipt has bank reference.");
        check(receipt.ResponseCode == "0020", "Receipt has response code 0020.");
        check(receipt.FinalState == PaymentState.Accepted, "Receipt records Accepted final state.");

        // Verify transport sent 1 payload with HKCCS
        check(transport.TransmittedCount == 1, "Transport transmitted exactly 1 message.");
        byte[] sentPayload = transport.RecordedTransmissions.First().Payload;
        string sentText = Encoding.Latin1.GetString(sentPayload);
        check(sentText.Contains("HKCCS:2:1+", StringComparison.Ordinal), "Transmitted wire payload contains HKCCS segment.");
        check(sentText.Contains(attempt.EndToEndId, StringComparison.Ordinal), "Transmitted wire payload contains EndToEndId.");
    }

    private static async Task TestSepaTransferExecutionCoordinatorScaFlow(Action<bool, string> check)
    {
        DateTimeOffset now = new(2026, 10, 19, 11, 20, 0, TimeSpan.Zero);
        DateOnly execDate = new(2026, 10, 25);

        PaymentDraftRevision revision = new(
            Id: 102,
            DraftId: 2,
            RevisionNumber: 1,
            PredecessorRevisionId: null,
            Timestamp: now,
            SourceAccountId: 10,
            SourceAccountBindingId: 20,
            SourceAccountIncarnationId: 30,
            Beneficiary: new PaymentBeneficiary("Hardware Corp", "DE89370400440532013000"),
            Amount: new Money(350.00m, "EUR"),
            Remittance: new PaymentRemittance("Hardware order"),
            ExecutionMode: PaymentExecutionMode.StandardCreditTransfer,
            ExecutionDate: execDate);

        PaymentAttempt attempt = PaymentAttempt.CreatePreSendAttempt(502, revision, now);

        using var transport = new FinTsSimulatedTransport();

        // 1. Step 1: Bank requires SCA challenge
        string scaChallengeWire = Frame(
            "HIRMG:2:2+0010::Nachricht entgegengenommen'" +
            "HIRMS:3:2:2+3920::SCA Challenge eingeleitet'" +
            "HITAN:4:1:2+920+1+CHALLENGE-ORD-77+SMS-TAN+TAN eingeben fuer 350.00 EUR an Hardware Corp'");
        transport.EnqueueResponse(Encoding.Latin1.GetBytes(scaChallengeWire));

        Uri endpoint = new("https://fints.testbank.de/fints");

        SepaTransferExecutionResult step1Result = await SepaTransferExecutionCoordinator.InitiateTransferAsync(
            attempt: attempt,
            draftRevision: revision,
            debtorName: "Max Mustermann",
            debtorIban: "DE89370400440532013000",
            debtorBic: "DBBADE33XXX",
            endpoint: endpoint,
            transport: transport,
            dialogueId: "D-SCA-01",
            messageNumber: 1,
            transitionIdStart: 2001,
            timestamp: now);

        check(step1Result.IsScaRequired, "Step 1 indicates SCA challenge is required.");
        check(step1Result.UpdatedAttempt.State == PaymentState.BankPending, "Attempt transitions to BankPending upon SCA challenge.");
        check(step1Result.UpdatedAttempt.BankReference == "CHALLENGE-ORD-77", "Challenge reference captured.");
        check(step1Result.ChallengeText!.Contains("350.00 EUR", StringComparison.Ordinal), "Challenge text contains amount.");

        // 2. Step 2: Complete SCA authorization with user TAN
        string scaAcceptWire = Frame(
            "HIRMG:2:2+0010::Nachricht entgegengenommen'" +
            "HIRMS:3:2:2+0020::TAN gueltig, Auftrag ausgefuehrt'" +
            "HICCS:4:1:2+BANK-ORDER-EXECUTED-77'");
        transport.EnqueueResponse(Encoding.Latin1.GetBytes(scaAcceptWire));

        SepaTransferExecutionResult step2Result = await SepaTransferExecutionCoordinator.CompleteScaAuthorizationAsync(
            attempt: step1Result.UpdatedAttempt,
            orderReference: "CHALLENGE-ORD-77",
            tan: "654321",
            endpoint: endpoint,
            transport: transport,
            dialogueId: "D-SCA-01",
            messageNumber: 2,
            transitionIdStart: 2010,
            timestamp: now.AddSeconds(20));

        check(step2Result.IsSuccess, "Step 2 successfully authorized payment.");
        check(step2Result.UpdatedAttempt.State == PaymentState.Accepted, "Final attempt state is Accepted.");
        check(step2Result.Receipt.FinalState == PaymentState.Accepted, "Receipt state is Accepted.");
        check(step2Result.Receipt.BankReference == "BANK-ORDER-EXECUTED-77", "Final bank reference recorded.");
    }

    private static async Task TestFailureInjectionResilience(Action<bool, string> check)
    {
        DateTimeOffset now = new(2026, 10, 19, 11, 30, 0, TimeSpan.Zero);
        DateOnly execDate = new(2026, 10, 25);

        PaymentDraftRevision revision = new(
            Id: 103,
            DraftId: 3,
            RevisionNumber: 1,
            PredecessorRevisionId: null,
            Timestamp: now,
            SourceAccountId: 10,
            SourceAccountBindingId: 20,
            SourceAccountIncarnationId: 30,
            Beneficiary: new PaymentBeneficiary("Energy Provider", "DE89370400440532013000"),
            Amount: new Money(120.00m, "EUR"),
            Remittance: new PaymentRemittance("Electricity monthly"),
            ExecutionMode: PaymentExecutionMode.StandardCreditTransfer,
            ExecutionDate: execDate);

        PaymentAttempt attempt = PaymentAttempt.CreatePreSendAttempt(503, revision, now);
        Uri endpoint = new("https://fints.testbank.de/fints");

        // 1. Failure injection: Network drops after send initiates -> MUST transition to UNKNOWN
        using var transportFault = new FinTsSimulatedTransport();
        transportFault.EnqueueFailure(new FinTsTransportException(
            FinTsTransportIssue.NetworkFailure,
            "Connection reset by peer after sending request."));

        SepaTransferExecutionResult faultResult = await SepaTransferExecutionCoordinator.InitiateTransferAsync(
            attempt: attempt,
            draftRevision: revision,
            debtorName: "Max Mustermann",
            debtorIban: "DE89370400440532013000",
            debtorBic: "DBBADE33XXX",
            endpoint: endpoint,
            transport: transportFault,
            dialogueId: "D-FAULT-01",
            messageNumber: 1,
            transitionIdStart: 3001,
            timestamp: now);

        check(faultResult.IsUnknown, "Network drop transitions to durable Unknown.");
        check(!faultResult.IsSuccess, "Faulted execution is not marked success.");
        check(faultResult.UpdatedAttempt.State == PaymentState.Unknown, "Attempt state is durably Unknown.");
        check(faultResult.Receipt.FinalState == PaymentState.Unknown, "Receipt final state is Unknown.");
        check(faultResult.Receipt.ResponseCode == "NET_FAULT", "Receipt response code is NET_FAULT.");

        // Transitions must show: AwaitingAuthorization -> Submitting -> Unknown
        check(faultResult.Transitions.Count == 2, "Faulted execution recorded 2 transitions.");
        check(faultResult.Transitions[0].ToState == PaymentState.Submitting, "Pre-send transition to Submitting committed before failure.");
        check(faultResult.Transitions[1].FromState == PaymentState.Submitting && faultResult.Transitions[1].ToState == PaymentState.Unknown, "Second transition moved from Submitting to Unknown.");

        // 2. Failure injection: Network fault during SCA completion -> MUST transition to UNKNOWN
        PaymentAttempt scaAttempt = attempt.TransitionTo(PaymentState.Submitting, now).TransitionTo(PaymentState.BankPending, now.AddSeconds(5));
        using var transportScaFault = new FinTsSimulatedTransport();
        transportScaFault.EnqueueFailure(new FinTsTransportException(
            FinTsTransportIssue.Timeout,
            "Timeout waiting for SCA response from bank."));

        SepaTransferExecutionResult scaFaultResult = await SepaTransferExecutionCoordinator.CompleteScaAuthorizationAsync(
            attempt: scaAttempt,
            orderReference: "CHALLENGE-ORD-99",
            tan: "123456",
            endpoint: endpoint,
            transport: transportScaFault,
            dialogueId: "D-FAULT-02",
            messageNumber: 2,
            transitionIdStart: 3010,
            timestamp: now.AddSeconds(10));

        check(scaFaultResult.IsUnknown, "Timeout during SCA transitions to Unknown.");
        check(scaFaultResult.UpdatedAttempt.State == PaymentState.Unknown, "SCA attempt state is durably Unknown.");

        // 3. Failure injection: Authoritative rejection by bank
        using var transportReject = new FinTsSimulatedTransport();
        string rejectWire = Frame(
            "HIRMG:2:2+9010::Verarbeitung nicht moeglich'" +
            "HIRMS:3:2:2+9942::PIN/TAN fehlerhaft'",
            trailerOrdinal: 4);
        transportReject.EnqueueResponse(Encoding.Latin1.GetBytes(rejectWire));

        SepaTransferExecutionResult rejectResult = await SepaTransferExecutionCoordinator.InitiateTransferAsync(
            attempt: attempt,
            draftRevision: revision,
            debtorName: "Max Mustermann",
            debtorIban: "DE89370400440532013000",
            debtorBic: "DBBADE33XXX",
            endpoint: endpoint,
            transport: transportReject,
            dialogueId: "D-REJECT-01",
            messageNumber: 1,
            transitionIdStart: 3020,
            timestamp: now);

        check(rejectResult.IsRejected, "Bank rejection recognized.");
        check(rejectResult.UpdatedAttempt.State == PaymentState.Rejected, "Attempt transitioned to Rejected.");
        check(rejectResult.Receipt.FinalState == PaymentState.Rejected, "Receipt recorded Rejected state.");
        check(PaymentStateMachine.IsTerminal(rejectResult.UpdatedAttempt.State), "Attempt is in terminal Rejected state.");
    }

    private static string Frame(string wireWithoutEnvelope, int trailerOrdinal = 5)
    {
        string wire = "HNHBK:1:3+000000000000+300+D-TEST-99+1+D-TEST-99:1'" +
                      wireWithoutEnvelope +
                      $"HNHBS:{trailerOrdinal.ToString(CultureInfo.InvariantCulture)}:1+1'";

        int totalLen = Encoding.Latin1.GetByteCount(wire);
        string totalLenStr = totalLen.ToString("D12", CultureInfo.InvariantCulture);
        return wire[..10] + totalLenStr + wire[22..];
    }
}
