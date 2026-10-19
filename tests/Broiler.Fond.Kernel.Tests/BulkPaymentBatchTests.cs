using Broiler.Fond.Kernel.Domain;
using Broiler.Fond.Kernel.Professional;

namespace Broiler.Fond.Kernel.Tests;

public static class BulkPaymentBatchTests
{
    public static void RunAllTests()
    {
        Console.WriteLine("Running ISO 20022 Batch Processing & Granular Reconciliation verification (P-03)...");
        int checks = 0;

        var executionDate = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(1));

        var item1 = new BulkPaymentItem("E2E-001", "Supplier Alpha GmbH", "DE89370400440532013000", new Money(1200.50m, "EUR"), "Invoice 2026-101");
        var item2 = new BulkPaymentItem("E2E-002", "Supplier Beta AG", "DE89370400440532013001", new Money(3450.25m, "EUR"), "Invoice 2026-102");
        var item3 = new BulkPaymentItem("E2E-003", "Supplier Gamma UG", "DE89370400440532013002", new Money(800.25m, "EUR"), "Invoice 2026-103");

        // 1. Batch creation & control sum calculation
        var batch = new BulkPaymentBatch(
            "BATCH-001",
            "MSG-20261019-01",
            "PMT-INF-01",
            "DE89370400440532013999",
            "Acme Holding GmbH",
            executionDate,
            BulkPaymentType.CreditTransfer,
            [item1, item2, item3]);

        Assert(batch.TransactionCount == 3, "Batch transaction count must be 3");
        Assert(batch.ControlSum.Amount == 5451.00m, "Batch control sum must be exact sum of items (5,451.00 EUR)");
        Assert(batch.ControlSum.Currency == "EUR", "Batch currency matches items currency");
        checks += 3;

        // Multi-currency batch rejection
        var usdItem = new BulkPaymentItem("E2E-USD", "US Vendor LLC", "DE89370400440532013003", new Money(500.00m, "USD"), "Inv 99");
        bool multiCcyCaught = false;
        try
        {
            _ = new BulkPaymentBatch("B2", "M2", "P2", "DE89370400440532013999", "Acme", executionDate, BulkPaymentType.CreditTransfer, [item1, usdItem]);
        }
        catch (InvalidOperationException)
        {
            multiCcyCaught = true;
        }
        Assert(multiCcyCaught, "Multi-currency batch must be rejected");
        checks++;

        // Empty batch rejection
        bool emptyBatchCaught = false;
        try
        {
            _ = new BulkPaymentBatch("B3", "M3", "P3", "DE89370400440532013999", "Acme", executionDate, BulkPaymentType.CreditTransfer, Array.Empty<BulkPaymentItem>());
        }
        catch (ArgumentException)
        {
            emptyBatchCaught = true;
        }
        Assert(emptyBatchCaught, "Empty batch must be rejected");
        checks++;

        // 2. Statutory validation
        BulkBatchValidator.Validate(batch);
        checks++;

        // 3. ISO 20022 pain.001.001.09 generation
        var now = DateTimeOffset.UtcNow;
        string pain001Xml = Iso20022BatchXmlCodec.BuildCreditTransferXml(batch, now);

        Assert(pain001Xml.Contains("pain.001.001.09"), "Must contain pain.001.001.09 namespace");
        Assert(pain001Xml.Contains("MsgId>MSG-20261019-01<"), "Must contain MsgId");
        Assert(pain001Xml.Contains("NbOfTxs>3<"), "Must contain NbOfTxs 3");
        Assert(pain001Xml.Contains("CtrlSum>5451.00<"), "Must contain CtrlSum 5451.00");
        Assert(pain001Xml.Contains("EndToEndId>E2E-001<"), "Must contain EndToEndId E2E-001");
        Assert(pain001Xml.Contains("InstdAmt Ccy=\"EUR\">1200.50<"), "Must contain InstdAmt 1200.50");
        Assert(pain001Xml.Contains("Ustrd>Invoice 2026-101<"), "Must contain remittance text");
        checks += 7;

        // 4. ISO 20022 pain.001.001.09 roundtrip parsing
        var parsedBatch = Iso20022BatchXmlCodec.ParseCreditTransferXml(pain001Xml, "PARSED-BATCH-001");
        Assert(parsedBatch.MessageId == "MSG-20261019-01", "Parsed MessageId must match");
        Assert(parsedBatch.PaymentInfoId == "PMT-INF-01", "Parsed PaymentInfoId must match");
        Assert(parsedBatch.TransactionCount == 3, "Parsed transaction count must be 3");
        Assert(parsedBatch.ControlSum.Amount == 5451.00m, "Parsed control sum must equal 5451.00 EUR");
        Assert(parsedBatch.Items[0].EndToEndId == "E2E-001", "Parsed item 1 EndToEndId matches");
        Assert(parsedBatch.Items[0].Amount.Amount == 1200.50m, "Parsed item 1 amount matches");
        Assert(parsedBatch.Items[1].EndToEndId == "E2E-002", "Parsed item 2 EndToEndId matches");
        Assert(parsedBatch.Items[1].Amount.Amount == 3450.25m, "Parsed item 2 amount matches");
        checks += 8;

        // 5. ISO 20022 pain.008.001.08 direct debit generation
        string pain008Xml = Iso20022BatchXmlCodec.BuildDirectDebitXml(batch, "DE98ZZZ09999999999", now);
        Assert(pain008Xml.Contains("pain.008.001.08"), "Must contain pain.008.001.08 namespace");
        Assert(pain008Xml.Contains("PmtMtd>DD<"), "Must contain PmtMtd DD");
        Assert(pain008Xml.Contains("DE98ZZZ09999999999"), "Must contain Creditor Scheme ID");
        Assert(pain008Xml.Contains("DrctDbtTxInf"), "Must contain DrctDbtTxInf");
        checks += 4;

        // 6. Collective bank statement matching (CAMT.053)
        bool matchedByMsgId = BatchReconciliationEngine.MatchCollectiveStatementEntry(
            batch,
            "SAMMELUEBERWEISUNG MSG-20261019-01 REF: 981240",
            new Money(-5451.00m, "EUR"));
        Assert(matchedByMsgId, "Statement entry with matching MessageId must match");
        checks++;

        bool matchedByPmtInf = BatchReconciliationEngine.MatchCollectiveStatementEntry(
            batch,
            "SEPA SAMMELAUFTRAG PMT-INF-01 VALUTA 2026-10-20",
            new Money(5451.00m, "EUR"));
        Assert(matchedByPmtInf, "Statement entry with matching PaymentInfoId must match");
        checks++;

        bool mismatchedAmount = BatchReconciliationEngine.MatchCollectiveStatementEntry(
            batch,
            "SAMMELUEBERWEISUNG MSG-20261019-01",
            new Money(-5000.00m, "EUR"));
        Assert(!mismatchedAmount, "Statement entry with different amount must not match");
        checks++;

        // 7. Granular reconciliation with pain.002 Payment Status Report
        string pain002Xml = @"<?xml version=""1.0"" encoding=""UTF-8""?>
<Document xmlns=""urn:iso:std:iso:20022:tech:xsd:pain.002.001.10"">
  <CstmrPmtStsRpt>
    <GrpHdr>
      <MsgId>PSR-20261020-01</MsgId>
    </GrpHdr>
    <OrgnlPmtInfAndSts>
      <OrgnlPmtInfId>PMT-INF-01</OrgnlPmtInfId>
      <TxInfAndSts>
        <OrgnlEndToEndId>E2E-002</OrgnlEndToEndId>
        <TxSts>RJCT</TxSts>
        <StsRsnInf>
          <Rsn>
            <Cd>AC04</Cd>
          </Rsn>
        </StsRsnInf>
      </TxInfAndSts>
    </OrgnlPmtInfAndSts>
  </CstmrPmtStsRpt>
</Document>";

        var (reconciledBatch, result) = BatchReconciliationEngine.IngestPaymentStatusReport(batch, pain002Xml);

        Assert(result.SubmittedCount == 3, "Submitted count is 3");
        Assert(result.SettledCount == 2, "Settled count is 2 (items 1 and 3 settled)");
        Assert(result.RejectedCount == 1, "Rejected count is 1 (item 2 rejected)");
        Assert(result.TotalSettled.Amount == 2000.75m, "Total settled is 1,200.50 + 800.25 = 2,000.75 EUR");
        Assert(result.TotalRejected.Amount == 3450.25m, "Total rejected is 3,450.25 EUR");
        Assert(result.HasPartialFailures, "Batch has partial failures");
        Assert(!result.IsCompletelySettled, "Batch is not completely settled");
        checks += 7;

        var rejectedItem = reconciledBatch.Items.First(i => i.EndToEndId == "E2E-002");
        Assert(rejectedItem.Status == BulkItemStatus.Rejected, "Item 2 must have status Rejected");
        Assert(rejectedItem.RejectionReason == "AC04", "Item 2 rejection reason must be AC04");
        var settledItem1 = reconciledBatch.Items.First(i => i.EndToEndId == "E2E-001");
        Assert(settledItem1.Status == BulkItemStatus.Settled, "Item 1 must have status Settled");
        checks += 3;

        Console.WriteLine($"ISO 20022 Batch Processing & Granular Reconciliation checks passed ({checks} checks).");
    }

    private static void Assert(bool condition, string message)
    {
        if (!condition)
        {
            throw new InvalidOperationException($"Assertion failed: {message}");
        }
    }
}
