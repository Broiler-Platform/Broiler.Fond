using System.Globalization;
using System.Text;
using System.Xml;
using Broiler.Fond.Kernel.Domain.Payments;

namespace Broiler.Fond.Kernel.FinTs;

/// <summary>
/// Deterministic generator for ISO 20022 pain.001.001.03 Customer Credit Transfer Initiation documents.
/// Implemented strictly with .NET 10 shared framework APIs (XmlWriter) and zero third-party dependencies.
/// </summary>
public static class SepaPainDocumentWriter
{
    public const string PainNamespace = "urn:iso:std:iso:20022:tech:xsd:pain.001.001.03";
    public const int MaxNameLength = 70;
    public const int MaxIdLength = 35;

    /// <summary>
    /// Generates a complete UTF-8 XML document string for a Single SEPA Credit Transfer.
    /// </summary>
    public static string GenerateXml(
        string messageId,
        string initiatingPartyName,
        string debtorName,
        string debtorIban,
        string? debtorBic,
        string paymentInfoId,
        DateOnly executionDate,
        string endToEndId,
        decimal amount,
        string creditorName,
        string creditorIban,
        string? creditorBic,
        PaymentRemittance remittance,
        DateTimeOffset? creationDateTime = null,
        PaymentExecutionMode executionMode = PaymentExecutionMode.StandardCreditTransfer)
    {
        byte[] bytes = GenerateXmlBytes(
            messageId,
            initiatingPartyName,
            debtorName,
            debtorIban,
            debtorBic,
            paymentInfoId,
            executionDate,
            endToEndId,
            amount,
            creditorName,
            creditorIban,
            creditorBic,
            remittance,
            creationDateTime,
            executionMode);

        return Encoding.UTF8.GetString(bytes);
    }

    /// <summary>
    /// Generates the raw UTF-8 encoded bytes (without BOM) of the ISO 20022 pain.001.001.03 XML document.
    /// </summary>
    public static byte[] GenerateXmlBytes(
        string messageId,
        string initiatingPartyName,
        string debtorName,
        string debtorIban,
        string? debtorBic,
        string paymentInfoId,
        DateOnly executionDate,
        string endToEndId,
        decimal amount,
        string creditorName,
        string creditorIban,
        string? creditorBic,
        PaymentRemittance remittance,
        DateTimeOffset? creationDateTime = null,
        PaymentExecutionMode executionMode = PaymentExecutionMode.StandardCreditTransfer)
    {
        // 1. Invariant Validation
        if (string.IsNullOrWhiteSpace(messageId) || messageId.Length > MaxIdLength)
        {
            throw new ArgumentException($"MessageId must be between 1 and {MaxIdLength} characters.", nameof(messageId));
        }

        if (string.IsNullOrWhiteSpace(initiatingPartyName) || initiatingPartyName.Length > MaxNameLength)
        {
            throw new ArgumentException($"InitiatingPartyName must be between 1 and {MaxNameLength} characters.", nameof(initiatingPartyName));
        }

        if (string.IsNullOrWhiteSpace(debtorName) || debtorName.Length > MaxNameLength)
        {
            throw new ArgumentException($"DebtorName must be between 1 and {MaxNameLength} characters.", nameof(debtorName));
        }

        if (!SepaIbanValidator.Validate(debtorIban, out string normDebtorIban))
        {
            throw new ArgumentException("Invalid debtor IBAN.", nameof(debtorIban));
        }

        if (string.IsNullOrWhiteSpace(paymentInfoId) || paymentInfoId.Length > MaxIdLength)
        {
            throw new ArgumentException($"PaymentInfoId must be between 1 and {MaxIdLength} characters.", nameof(paymentInfoId));
        }

        if (string.IsNullOrWhiteSpace(endToEndId) || endToEndId.Length > MaxIdLength)
        {
            throw new ArgumentException($"EndToEndId must be between 1 and {MaxIdLength} characters.", nameof(endToEndId));
        }

        if (amount <= 0m)
        {
            throw new ArgumentException("Amount must be strictly positive.", nameof(amount));
        }

        if (string.IsNullOrWhiteSpace(creditorName) || creditorName.Length > MaxNameLength)
        {
            throw new ArgumentException($"CreditorName must be between 1 and {MaxNameLength} characters.", nameof(creditorName));
        }

        if (!SepaIbanValidator.Validate(creditorIban, out string normCreditorIban))
        {
            throw new ArgumentException("Invalid creditor IBAN.", nameof(creditorIban));
        }

        DateTimeOffset created = creationDateTime ?? DateTimeOffset.UtcNow;
        string creDtTm = created.ToUniversalTime().ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture);
        string amountStr = amount.ToString("0.00", CultureInfo.InvariantCulture);

        var settings = new XmlWriterSettings
        {
            Encoding = new UTF8Encoding(false),
            Indent = false,
            OmitXmlDeclaration = false
        };

        using var ms = new MemoryStream();
        using (var writer = XmlWriter.Create(ms, settings))
        {
            writer.WriteStartDocument();
            writer.WriteStartElement("Document", PainNamespace);
            writer.WriteAttributeString("xmlns", "xsi", null, "http://www.w3.org/2001/XMLSchema-instance");

            writer.WriteStartElement("CstmrCdtTrfInitn", PainNamespace);

            // <GrpHdr>
            writer.WriteStartElement("GrpHdr", PainNamespace);
            writer.WriteElementString("MsgId", PainNamespace, messageId.Trim());
            writer.WriteElementString("CreDtTm", PainNamespace, creDtTm);
            writer.WriteElementString("NbOfTxs", PainNamespace, "1");
            writer.WriteElementString("CtrlSum", PainNamespace, amountStr);

            writer.WriteStartElement("InitgPty", PainNamespace);
            writer.WriteElementString("Nm", PainNamespace, initiatingPartyName.Trim());
            writer.WriteEndElement(); // </InitgPty>

            writer.WriteEndElement(); // </GrpHdr>

            // <PmtInf>
            writer.WriteStartElement("PmtInf", PainNamespace);
            writer.WriteElementString("PmtInfId", PainNamespace, paymentInfoId.Trim());
            writer.WriteElementString("PmtMtd", PainNamespace, "TRF");
            writer.WriteElementString("NbOfTxs", PainNamespace, "1");
            writer.WriteElementString("CtrlSum", PainNamespace, amountStr);

            writer.WriteStartElement("PmtTpInf", PainNamespace);
            writer.WriteStartElement("SvcLvl", PainNamespace);
            writer.WriteElementString("Cd", PainNamespace, "SEPA");
            writer.WriteEndElement(); // </SvcLvl>
            if (executionMode == PaymentExecutionMode.InstantCreditTransfer)
            {
                writer.WriteStartElement("LclInstrm", PainNamespace);
                writer.WriteElementString("Cd", PainNamespace, "INST");
                writer.WriteEndElement(); // </LclInstrm>
            }
            writer.WriteEndElement(); // </PmtTpInf>

            writer.WriteElementString("ReqdExctnDt", PainNamespace, executionDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));

            // <Dbtr>
            writer.WriteStartElement("Dbtr", PainNamespace);
            writer.WriteElementString("Nm", PainNamespace, debtorName.Trim());
            writer.WriteEndElement(); // </Dbtr>

            // <DbtrAcct>
            writer.WriteStartElement("DbtrAcct", PainNamespace);
            writer.WriteStartElement("Id", PainNamespace);
            writer.WriteElementString("IBAN", PainNamespace, normDebtorIban);
            writer.WriteEndElement(); // </Id>
            writer.WriteEndElement(); // </DbtrAcct>

            // <DbtrAgt>
            writer.WriteStartElement("DbtrAgt", PainNamespace);
            writer.WriteStartElement("FinInstnId", PainNamespace);
            if (!string.IsNullOrWhiteSpace(debtorBic))
            {
                writer.WriteElementString("BIC", PainNamespace, debtorBic.Trim().ToUpperInvariant());
            }
            else
            {
                writer.WriteStartElement("Othr", PainNamespace);
                writer.WriteElementString("Id", PainNamespace, "NOTPROVIDED");
                writer.WriteEndElement(); // </Othr>
            }
            writer.WriteEndElement(); // </FinInstnId>
            writer.WriteEndElement(); // </DbtrAgt>

            writer.WriteElementString("ChrgBr", PainNamespace, "SLEV");

            // <CdtTrfTxInf>
            writer.WriteStartElement("CdtTrfTxInf", PainNamespace);

            writer.WriteStartElement("PmtId", PainNamespace);
            writer.WriteElementString("EndToEndId", PainNamespace, endToEndId.Trim());
            writer.WriteEndElement(); // </PmtId>

            writer.WriteStartElement("Amt", PainNamespace);
            writer.WriteStartElement("InstdAmt", PainNamespace);
            writer.WriteAttributeString("Ccy", "EUR");
            writer.WriteString(amountStr);
            writer.WriteEndElement(); // </InstdAmt>
            writer.WriteEndElement(); // </Amt>

            if (!string.IsNullOrWhiteSpace(creditorBic))
            {
                writer.WriteStartElement("CdtrAgt", PainNamespace);
                writer.WriteStartElement("FinInstnId", PainNamespace);
                writer.WriteElementString("BIC", PainNamespace, creditorBic.Trim().ToUpperInvariant());
                writer.WriteEndElement(); // </FinInstnId>
                writer.WriteEndElement(); // </CdtrAgt>
            }

            // <Cdtr>
            writer.WriteStartElement("Cdtr", PainNamespace);
            writer.WriteElementString("Nm", PainNamespace, creditorName.Trim());
            writer.WriteEndElement(); // </Cdtr>

            // <CdtrAcct>
            writer.WriteStartElement("CdtrAcct", PainNamespace);
            writer.WriteStartElement("Id", PainNamespace);
            writer.WriteElementString("IBAN", PainNamespace, normCreditorIban);
            writer.WriteEndElement(); // </Id>
            writer.WriteEndElement(); // </CdtrAcct>

            // <RmtInf>
            if (remittance is not null && (!string.IsNullOrWhiteSpace(remittance.UnstructuredText) || !string.IsNullOrWhiteSpace(remittance.StructuredReference)))
            {
                writer.WriteStartElement("RmtInf", PainNamespace);
                if (!string.IsNullOrWhiteSpace(remittance.UnstructuredText))
                {
                    writer.WriteElementString("Ustrd", PainNamespace, remittance.UnstructuredText.Trim());
                }
                else if (!string.IsNullOrWhiteSpace(remittance.StructuredReference))
                {
                    writer.WriteStartElement("Strd", PainNamespace);
                    writer.WriteStartElement("CdtrRefInf", PainNamespace);
                    writer.WriteStartElement("Tp", PainNamespace);
                    writer.WriteStartElement("CdOrPrtry", PainNamespace);
                    writer.WriteElementString("Cd", PainNamespace, "SCOR");
                    writer.WriteEndElement(); // </CdOrPrtry>
                    writer.WriteEndElement(); // </Tp>
                    writer.WriteElementString("Ref", PainNamespace, remittance.StructuredReference.Trim());
                    writer.WriteEndElement(); // </CdtrRefInf>
                    writer.WriteEndElement(); // </Strd>
                }
                writer.WriteEndElement(); // </RmtInf>
            }

            writer.WriteEndElement(); // </CdtTrfTxInf>

            writer.WriteEndElement(); // </PmtInf>

            writer.WriteEndElement(); // </CstmrCdtTrfInitn>
            writer.WriteEndElement(); // </Document>
            writer.WriteEndDocument();
        }

        return ms.ToArray();
    }
}
