using System.Globalization;
using System.Text;
using System.Xml;
using System.Xml.Linq;
using Broiler.Fond.Kernel.Domain;

namespace Broiler.Fond.Kernel.Professional;

/// <summary>
/// Type of bulk payment batch.
/// </summary>
public enum BulkPaymentType
{
    /// <summary>ISO 20022 pain.001.001.09 bulk credit transfer</summary>
    CreditTransfer = 0,

    /// <summary>ISO 20022 pain.008.001.08 bulk direct debit</summary>
    DirectDebit = 1
}

/// <summary>
/// Status of an individual transaction item within a bulk payment batch.
/// </summary>
public enum BulkItemStatus
{
    Pending = 0,
    Settled = 1,
    Rejected = 2
}

/// <summary>
/// An individual payment transaction item in a high-volume bulk payment batch.
/// </summary>
public sealed record BulkPaymentItem
{
    public string EndToEndId { get; }
    public string CounterpartyName { get; }
    public string CounterpartyIban { get; }
    public string? CounterpartyBic { get; }
    public Money Amount { get; }
    public string RemittanceInformation { get; }
    public BulkItemStatus Status { get; init; }
    public string? RejectionReason { get; init; }

    public BulkPaymentItem(
        string endToEndId,
        string counterpartyName,
        string counterpartyIban,
        Money amount,
        string remittanceInformation,
        string? counterpartyBic = null,
        BulkItemStatus status = BulkItemStatus.Pending,
        string? rejectionReason = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(endToEndId);
        ArgumentException.ThrowIfNullOrWhiteSpace(counterpartyName);
        ArgumentException.ThrowIfNullOrWhiteSpace(counterpartyIban);
        ArgumentNullException.ThrowIfNull(amount);
        ArgumentException.ThrowIfNullOrWhiteSpace(remittanceInformation);

        if (amount.Amount <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(amount), "Item amount must be strictly greater than zero.");
        }

        EndToEndId = endToEndId.Trim();
        CounterpartyName = counterpartyName.Trim();
        CounterpartyIban = counterpartyIban.Trim().Replace(" ", string.Empty).ToUpperInvariant();
        CounterpartyBic = counterpartyBic?.Trim().ToUpperInvariant();
        Amount = amount;
        RemittanceInformation = remittanceInformation.Trim();
        Status = status;
        RejectionReason = rejectionReason;
    }
}

/// <summary>
/// An immutable bulk payment batch maintaining EPC statutory control sums and item collections.
/// </summary>
public sealed record BulkPaymentBatch
{
    public string BatchId { get; }
    public string MessageId { get; }
    public string PaymentInfoId { get; }
    public string InitiatorIban { get; }
    public string InitiatorName { get; }
    public DateOnly ExecutionDate { get; }
    public BulkPaymentType BatchType { get; }
    public IReadOnlyList<BulkPaymentItem> Items { get; }
    public Money ControlSum { get; }
    public int TransactionCount => Items.Count;

    public BulkPaymentBatch(
        string batchId,
        string messageId,
        string paymentInfoId,
        string initiatorIban,
        string initiatorName,
        DateOnly executionDate,
        BulkPaymentType batchType,
        IEnumerable<BulkPaymentItem> items)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(batchId);
        ArgumentException.ThrowIfNullOrWhiteSpace(messageId);
        ArgumentException.ThrowIfNullOrWhiteSpace(paymentInfoId);
        ArgumentException.ThrowIfNullOrWhiteSpace(initiatorIban);
        ArgumentException.ThrowIfNullOrWhiteSpace(initiatorName);
        ArgumentNullException.ThrowIfNull(items);

        var itemList = items.ToArray();
        if (itemList.Length == 0)
        {
            throw new ArgumentException("A bulk payment batch must contain at least one payment item.", nameof(items));
        }

        string currency = itemList[0].Amount.Currency;
        decimal totalAmount = 0m;
        foreach (var item in itemList)
        {
            if (!string.Equals(item.Amount.Currency, currency, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException($"Multi-currency batches are prohibited. Item '{item.EndToEndId}' currency ({item.Amount.Currency}) differs from batch currency ({currency}).");
            }
            totalAmount += item.Amount.Amount;
        }

        BatchId = batchId.Trim();
        MessageId = messageId.Trim();
        PaymentInfoId = paymentInfoId.Trim();
        InitiatorIban = initiatorIban.Trim().Replace(" ", string.Empty).ToUpperInvariant();
        InitiatorName = initiatorName.Trim();
        ExecutionDate = executionDate;
        BatchType = batchType;
        Items = itemList;
        ControlSum = new Money(totalAmount, currency);
    }
}

/// <summary>
/// Statutory validator for ISO 20022 SEPA bulk payment batches.
/// </summary>
public static class BulkBatchValidator
{
    public static void Validate(BulkPaymentBatch batch)
    {
        ArgumentNullException.ThrowIfNull(batch);

        if (batch.Items.Count == 0)
        {
            throw new InvalidOperationException("Batch has no items.");
        }

        if (batch.InitiatorIban.Length < 15 || batch.InitiatorIban.Length > 34)
        {
            throw new InvalidOperationException($"Invalid initiator IBAN length: '{batch.InitiatorIban}'.");
        }

        decimal computedSum = 0m;
        foreach (var item in batch.Items)
        {
            if (item.CounterpartyIban.Length < 15 || item.CounterpartyIban.Length > 34)
            {
                throw new InvalidOperationException($"Invalid counterparty IBAN '{item.CounterpartyIban}' on item '{item.EndToEndId}'.");
            }
            computedSum += item.Amount.Amount;
        }

        if (computedSum != batch.ControlSum.Amount)
        {
            throw new InvalidOperationException($"Statutory control sum mismatch: expected {computedSum}, found {batch.ControlSum.Amount}.");
        }
    }
}

/// <summary>
/// Pure BCL XML codec for ISO 20022 pain.001.001.09 and pain.008.001.08 documents.
/// </summary>
public static class Iso20022BatchXmlCodec
{
    public const string Pain001Namespace = "urn:iso:std:iso:20022:tech:xsd:pain.001.001.09";
    public const string Pain008Namespace = "urn:iso:std:iso:20022:tech:xsd:pain.008.001.08";

    /// <summary>
    /// Generates an ISO 20022 pain.001.001.09 Customer Credit Transfer Initiation XML.
    /// </summary>
    public static string BuildCreditTransferXml(BulkPaymentBatch batch, DateTimeOffset creationTimeUtc)
    {
        ArgumentNullException.ThrowIfNull(batch);
        BulkBatchValidator.Validate(batch);

        var settings = new XmlWriterSettings
        {
            Encoding = Encoding.UTF8,
            OmitXmlDeclaration = false,
            Indent = false
        };

        var sb = new StringBuilder();
        using (var writer = XmlWriter.Create(sb, settings))
        {
            writer.WriteStartDocument();
            writer.WriteStartElement("Document", Pain001Namespace);
            writer.WriteStartElement("CstmrCdtTrfInitn");

            // Group Header (GrpHdr)
            writer.WriteStartElement("GrpHdr");
            writer.WriteElementString("MsgId", batch.MessageId);
            writer.WriteElementString("CreDtTm", creationTimeUtc.ToString("yyyy-MM-ddTHH:mm:ssZ"));
            writer.WriteElementString("NbOfTxs", batch.TransactionCount.ToString(CultureInfo.InvariantCulture));
            writer.WriteElementString("CtrlSum", batch.ControlSum.Amount.ToString("0.00", CultureInfo.InvariantCulture));

            writer.WriteStartElement("InitgPty");
            writer.WriteElementString("Nm", batch.InitiatorName);
            writer.WriteEndElement(); // InitgPty

            writer.WriteEndElement(); // GrpHdr

            // Payment Information (PmtInf)
            writer.WriteStartElement("PmtInf");
            writer.WriteElementString("PmtInfId", batch.PaymentInfoId);
            writer.WriteElementString("PmtMtd", "TRF");
            writer.WriteElementString("NbOfTxs", batch.TransactionCount.ToString(CultureInfo.InvariantCulture));
            writer.WriteElementString("CtrlSum", batch.ControlSum.Amount.ToString("0.00", CultureInfo.InvariantCulture));

            writer.WriteStartElement("PmtTpInf");
            writer.WriteStartElement("SvcLvl");
            writer.WriteElementString("Cd", "SEPA");
            writer.WriteEndElement(); // SvcLvl
            writer.WriteEndElement(); // PmtTpInf

            writer.WriteStartElement("ReqdExctnDt");
            writer.WriteElementString("Dt", batch.ExecutionDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));
            writer.WriteEndElement(); // ReqdExctnDt

            writer.WriteStartElement("Dbtr");
            writer.WriteElementString("Nm", batch.InitiatorName);
            writer.WriteEndElement(); // Dbtr

            writer.WriteStartElement("DbtrAcct");
            writer.WriteStartElement("Id");
            writer.WriteElementString("IBAN", batch.InitiatorIban);
            writer.WriteEndElement(); // Id
            writer.WriteEndElement(); // DbtrAcct

            writer.WriteElementString("ChrgBr", "SLEV");

            // Individual Transactions (CdtTrfTxInf)
            foreach (var item in batch.Items)
            {
                writer.WriteStartElement("CdtTrfTxInf");

                writer.WriteStartElement("PmtId");
                writer.WriteElementString("EndToEndId", item.EndToEndId);
                writer.WriteEndElement(); // PmtId

                writer.WriteStartElement("Amt");
                writer.WriteStartElement("InstdAmt");
                writer.WriteAttributeString("Ccy", item.Amount.Currency);
                writer.WriteString(item.Amount.Amount.ToString("0.00", CultureInfo.InvariantCulture));
                writer.WriteEndElement(); // InstdAmt
                writer.WriteEndElement(); // Amt

                writer.WriteStartElement("Cdtr");
                writer.WriteElementString("Nm", item.CounterpartyName);
                writer.WriteEndElement(); // Cdtr

                writer.WriteStartElement("CdtrAcct");
                writer.WriteStartElement("Id");
                writer.WriteElementString("IBAN", item.CounterpartyIban);
                writer.WriteEndElement(); // Id
                writer.WriteEndElement(); // CdtrAcct

                writer.WriteStartElement("RmtInf");
                writer.WriteElementString("Ustrd", item.RemittanceInformation);
                writer.WriteEndElement(); // RmtInf

                writer.WriteEndElement(); // CdtTrfTxInf
            }

            writer.WriteEndElement(); // PmtInf
            writer.WriteEndElement(); // CstmrCdtTrfInitn
            writer.WriteEndElement(); // Document
            writer.WriteEndDocument();
        }

        return sb.ToString();
    }

    /// <summary>
    /// Generates an ISO 20022 pain.008.001.08 Customer Direct Debit Initiation XML.
    /// </summary>
    public static string BuildDirectDebitXml(BulkPaymentBatch batch, string creditorId, DateTimeOffset creationTimeUtc)
    {
        ArgumentNullException.ThrowIfNull(batch);
        ArgumentException.ThrowIfNullOrWhiteSpace(creditorId);
        BulkBatchValidator.Validate(batch);

        var settings = new XmlWriterSettings
        {
            Encoding = Encoding.UTF8,
            OmitXmlDeclaration = false,
            Indent = false
        };

        var sb = new StringBuilder();
        using (var writer = XmlWriter.Create(sb, settings))
        {
            writer.WriteStartDocument();
            writer.WriteStartElement("Document", Pain008Namespace);
            writer.WriteStartElement("CstmrDrctDbtInitn");

            writer.WriteStartElement("GrpHdr");
            writer.WriteElementString("MsgId", batch.MessageId);
            writer.WriteElementString("CreDtTm", creationTimeUtc.ToString("yyyy-MM-ddTHH:mm:ssZ"));
            writer.WriteElementString("NbOfTxs", batch.TransactionCount.ToString(CultureInfo.InvariantCulture));
            writer.WriteElementString("CtrlSum", batch.ControlSum.Amount.ToString("0.00", CultureInfo.InvariantCulture));

            writer.WriteStartElement("InitgPty");
            writer.WriteElementString("Nm", batch.InitiatorName);
            writer.WriteEndElement(); // InitgPty
            writer.WriteEndElement(); // GrpHdr

            writer.WriteStartElement("PmtInf");
            writer.WriteElementString("PmtInfId", batch.PaymentInfoId);
            writer.WriteElementString("PmtMtd", "DD");
            writer.WriteElementString("NbOfTxs", batch.TransactionCount.ToString(CultureInfo.InvariantCulture));
            writer.WriteElementString("CtrlSum", batch.ControlSum.Amount.ToString("0.00", CultureInfo.InvariantCulture));

            writer.WriteStartElement("PmtTpInf");
            writer.WriteStartElement("SvcLvl");
            writer.WriteElementString("Cd", "SEPA");
            writer.WriteEndElement();
            writer.WriteStartElement("LclInstrm");
            writer.WriteElementString("Cd", "CORE");
            writer.WriteEndElement();
            writer.WriteElementString("SeqTp", "RCUR");
            writer.WriteEndElement(); // PmtTpInf

            writer.WriteElementString("ReqdColltnDt", batch.ExecutionDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));

            writer.WriteStartElement("Cdtr");
            writer.WriteElementString("Nm", batch.InitiatorName);
            writer.WriteEndElement(); // Cdtr

            writer.WriteStartElement("CdtrAcct");
            writer.WriteStartElement("Id");
            writer.WriteElementString("IBAN", batch.InitiatorIban);
            writer.WriteEndElement();
            writer.WriteEndElement(); // CdtrAcct

            writer.WriteStartElement("CdtrSchmeId");
            writer.WriteStartElement("Id");
            writer.WriteStartElement("PrvtId");
            writer.WriteStartElement("Othr");
            writer.WriteElementString("Id", creditorId.Trim().ToUpperInvariant());
            writer.WriteEndElement(); // Othr
            writer.WriteEndElement(); // PrvtId
            writer.WriteEndElement(); // Id
            writer.WriteEndElement(); // CdtrSchmeId

            // Direct debit transaction details
            foreach (var item in batch.Items)
            {
                writer.WriteStartElement("DrctDbtTxInf");

                writer.WriteStartElement("PmtId");
                writer.WriteElementString("EndToEndId", item.EndToEndId);
                writer.WriteEndElement();

                writer.WriteStartElement("InstdAmt");
                writer.WriteAttributeString("Ccy", item.Amount.Currency);
                writer.WriteString(item.Amount.Amount.ToString("0.00", CultureInfo.InvariantCulture));
                writer.WriteEndElement();

                writer.WriteStartElement("Dbtr");
                writer.WriteElementString("Nm", item.CounterpartyName);
                writer.WriteEndElement();

                writer.WriteStartElement("DbtrAcct");
                writer.WriteStartElement("Id");
                writer.WriteElementString("IBAN", item.CounterpartyIban);
                writer.WriteEndElement();
                writer.WriteEndElement();

                writer.WriteStartElement("RmtInf");
                writer.WriteElementString("Ustrd", item.RemittanceInformation);
                writer.WriteEndElement();

                writer.WriteEndElement(); // DrctDbtTxInf
            }

            writer.WriteEndElement(); // PmtInf
            writer.WriteEndElement(); // CstmrDrctDbtInitn
            writer.WriteEndElement(); // Document
            writer.WriteEndDocument();
        }

        return sb.ToString();
    }

    /// <summary>
    /// Parses an ISO 20022 pain.001.001.09 XML into a BulkPaymentBatch.
    /// </summary>
    public static BulkPaymentBatch ParseCreditTransferXml(string xml, string batchId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(xml);
        ArgumentException.ThrowIfNullOrWhiteSpace(batchId);

        var readerSettings = new XmlReaderSettings
        {
            DtdProcessing = DtdProcessing.Prohibit,
            XmlResolver = null,
            IgnoreWhitespace = true
        };

        using var reader = XmlReader.Create(new StringReader(xml), readerSettings);
        var doc = XDocument.Load(reader, LoadOptions.None);

        string messageId = doc.Descendants().FirstOrDefault(e => e.Name.LocalName == "MsgId")?.Value?.Trim() ?? string.Empty;
        string paymentInfoId = doc.Descendants().FirstOrDefault(e => e.Name.LocalName == "PmtInfId")?.Value?.Trim() ?? string.Empty;
        string initiatorName = doc.Descendants().FirstOrDefault(e => e.Name.LocalName == "Dbtr")?.Descendants().FirstOrDefault(e => e.Name.LocalName == "Nm")?.Value?.Trim() ?? string.Empty;
        string initiatorIban = doc.Descendants().FirstOrDefault(e => e.Name.LocalName == "DbtrAcct")?.Descendants().FirstOrDefault(e => e.Name.LocalName == "IBAN")?.Value?.Trim() ?? string.Empty;

        DateOnly executionDate = DateOnly.FromDateTime(DateTime.UtcNow);
        string? dtText = doc.Descendants().FirstOrDefault(e => e.Name.LocalName == "ReqdExctnDt")?.Descendants().FirstOrDefault(e => e.Name.LocalName == "Dt")?.Value?.Trim();
        if (!string.IsNullOrEmpty(dtText) && DateOnly.TryParseExact(dtText, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var parsedDate))
        {
            executionDate = parsedDate;
        }

        var items = new List<BulkPaymentItem>();
        foreach (var txElem in doc.Descendants().Where(e => e.Name.LocalName == "CdtTrfTxInf"))
        {
            string endToEndId = txElem.Descendants().FirstOrDefault(e => e.Name.LocalName == "EndToEndId")?.Value?.Trim() ?? string.Empty;
            var amtElem = txElem.Descendants().FirstOrDefault(e => e.Name.LocalName == "InstdAmt");
            string currency = amtElem?.Attribute("Ccy")?.Value?.Trim() ?? "EUR";
            decimal.TryParse(amtElem?.Value?.Trim(), NumberStyles.Number, CultureInfo.InvariantCulture, out decimal amount);

            string cdtrName = txElem.Descendants().FirstOrDefault(e => e.Name.LocalName == "Cdtr")?.Descendants().FirstOrDefault(e => e.Name.LocalName == "Nm")?.Value?.Trim() ?? string.Empty;
            string cdtrIban = txElem.Descendants().FirstOrDefault(e => e.Name.LocalName == "CdtrAcct")?.Descendants().FirstOrDefault(e => e.Name.LocalName == "IBAN")?.Value?.Trim() ?? string.Empty;
            string rmtInf = txElem.Descendants().FirstOrDefault(e => e.Name.LocalName == "Ustrd")?.Value?.Trim() ?? string.Empty;

            if (!string.IsNullOrEmpty(endToEndId))
            {
                items.Add(new BulkPaymentItem(
                    endToEndId,
                    cdtrName,
                    cdtrIban,
                    new Money(amount, currency),
                    rmtInf));
            }
        }

        return new BulkPaymentBatch(
            batchId,
            messageId,
            paymentInfoId,
            initiatorIban,
            initiatorName,
            executionDate,
            BulkPaymentType.CreditTransfer,
            items);
    }
}

/// <summary>
/// Summary result of granular reconciliation between bulk payment batches, CAMT statements, and status reports.
/// </summary>
public sealed record BatchReconciliationResult(
    string BatchId,
    Money TotalSubmitted,
    Money TotalSettled,
    Money TotalRejected,
    int SubmittedCount,
    int SettledCount,
    int RejectedCount)
{
    public bool IsCompletelySettled => RejectedCount == 0 && SettledCount == SubmittedCount;
    public bool HasPartialFailures => RejectedCount > 0;
}

/// <summary>
/// Reconciles collective bank statement bookings and pain.002 status reports against individual batch transactions.
/// </summary>
public static class BatchReconciliationEngine
{
    /// <summary>
    /// Matches a collective bank statement booking entry (camt.053) against a bulk payment batch.
    /// </summary>
    public static bool MatchCollectiveStatementEntry(
        BulkPaymentBatch batch,
        string statementReference,
        Money bookingAmount)
    {
        ArgumentNullException.ThrowIfNull(batch);
        ArgumentException.ThrowIfNullOrWhiteSpace(statementReference);
        ArgumentNullException.ThrowIfNull(bookingAmount);

        // Verification of reference match: contains MessageId or PaymentInfoId
        bool referenceMatches = statementReference.Contains(batch.MessageId, StringComparison.OrdinalIgnoreCase) ||
                                statementReference.Contains(batch.PaymentInfoId, StringComparison.OrdinalIgnoreCase);

        if (!referenceMatches)
        {
            return false;
        }

        // Verification of amount match
        if (!string.Equals(batch.ControlSum.Currency, bookingAmount.Currency, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        return batch.ControlSum.Amount == Math.Abs(bookingAmount.Amount);
    }

    /// <summary>
    /// Ingests an ISO 20022 pain.002.001.10 Payment Status Report XML, identifying rejected transactions (RJCT),
    /// updating item states, and calculating net settlement reconciliation.
    /// </summary>
    public static (BulkPaymentBatch UpdatedBatch, BatchReconciliationResult Result) IngestPaymentStatusReport(
        BulkPaymentBatch batch,
        string pain002Xml)
    {
        ArgumentNullException.ThrowIfNull(batch);
        ArgumentException.ThrowIfNullOrWhiteSpace(pain002Xml);

        var readerSettings = new XmlReaderSettings
        {
            DtdProcessing = DtdProcessing.Prohibit,
            XmlResolver = null,
            IgnoreWhitespace = true
        };

        var rejections = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        using var reader = XmlReader.Create(new StringReader(pain002Xml), readerSettings);
        var doc = XDocument.Load(reader, LoadOptions.None);

        foreach (var txNode in doc.Descendants().Where(e => e.Name.LocalName == "TxInfAndSts"))
        {
            string originalEndToEndId = txNode.Descendants().FirstOrDefault(e => e.Name.LocalName == "OrgnlEndToEndId")?.Value?.Trim() ?? string.Empty;
            string txStatus = txNode.Descendants().FirstOrDefault(e => e.Name.LocalName == "TxSts")?.Value?.Trim() ?? string.Empty;
            string reasonCode = txNode.Descendants().FirstOrDefault(e => e.Name.LocalName == "Cd")?.Value?.Trim() ?? "NARR";

            if (!string.IsNullOrEmpty(originalEndToEndId) && string.Equals(txStatus, "RJCT", StringComparison.OrdinalIgnoreCase))
            {
                rejections[originalEndToEndId] = reasonCode;
            }
        }

        decimal settledSum = 0m;
        decimal rejectedSum = 0m;
        int settledCount = 0;
        int rejectedCount = 0;

        var updatedItems = new List<BulkPaymentItem>(batch.Items.Count);
        foreach (var item in batch.Items)
        {
            if (rejections.TryGetValue(item.EndToEndId, out string? reason))
            {
                updatedItems.Add(item with
                {
                    Status = BulkItemStatus.Rejected,
                    RejectionReason = reason
                });
                rejectedSum += item.Amount.Amount;
                rejectedCount++;
            }
            else
            {
                updatedItems.Add(item with
                {
                    Status = BulkItemStatus.Settled
                });
                settledSum += item.Amount.Amount;
                settledCount++;
            }
        }

        var updatedBatch = new BulkPaymentBatch(
            batch.BatchId,
            batch.MessageId,
            batch.PaymentInfoId,
            batch.InitiatorIban,
            batch.InitiatorName,
            batch.ExecutionDate,
            batch.BatchType,
            updatedItems);

        var result = new BatchReconciliationResult(
            batch.BatchId,
            batch.ControlSum,
            new Money(settledSum, batch.ControlSum.Currency),
            new Money(rejectedSum, batch.ControlSum.Currency),
            batch.TransactionCount,
            settledCount,
            rejectedCount);

        return (updatedBatch, result);
    }
}
