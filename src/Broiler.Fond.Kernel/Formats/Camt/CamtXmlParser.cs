using System.Collections.ObjectModel;
using System.Globalization;
using System.Text;
using System.Xml;
using System.Xml.Linq;
using Broiler.Fond.Kernel.Domain;
using Broiler.Fond.Kernel.Domain.Transactions;

namespace Broiler.Fond.Kernel.Formats.Camt;

/// <summary>
/// Secure, bounded XML parser for ISO 20022 CAMT statement messages (camt.052 and camt.053).
/// </summary>
public static class CamtXmlParser
{
    public const int MaximumInputBytes = 10 * 1024 * 1024; // 10 MiB
    public const int MaximumStatementsPerDocument = 1000;
    public const int MaximumEntriesPerStatement = 50000;

    public static IReadOnlyList<CamtStatement> Parse(string xmlText)
    {
        ArgumentNullException.ThrowIfNull(xmlText);
        if (xmlText.Length > MaximumInputBytes)
        {
            throw new FormatException($"XML text exceeds maximum allowed size of {MaximumInputBytes} characters.");
        }

        using StringReader stringReader = new(xmlText);
        return Parse(stringReader);
    }

    public static IReadOnlyList<CamtStatement> Parse(ReadOnlySpan<byte> bytes)
    {
        if (bytes.Length > MaximumInputBytes)
        {
            throw new FormatException($"XML byte span exceeds maximum allowed size of {MaximumInputBytes} bytes.");
        }

        using MemoryStream ms = new(bytes.ToArray());
        using StreamReader streamReader = new(ms, Encoding.UTF8);
        return Parse(streamReader);
    }

    public static IReadOnlyList<CamtStatement> Parse(TextReader textReader)
    {
        ArgumentNullException.ThrowIfNull(textReader);

        XmlReaderSettings settings = new()
        {
            DtdProcessing = DtdProcessing.Prohibit,
            XmlResolver = null,
            IgnoreComments = true,
            IgnoreWhitespace = true,
            MaxCharactersInDocument = 20_000_000,
        };

        using XmlReader reader = XmlReader.Create(textReader, settings);
        XDocument document = XDocument.Load(reader, LoadOptions.None);
        return ParseDocument(document);
    }

    private static IReadOnlyList<CamtStatement> ParseDocument(XDocument doc)
    {
        XElement? root = doc.Root ?? throw new FormatException("Empty XML document.");
        XNamespace ns = root.Name.Namespace;

        XElement? reportRoot = root.Element(ns + "BkToCstmrStmt") ?? root.Element(ns + "BkToCstmrAcctRpt");
        if (reportRoot == null)
        {
            // Try matching without namespace if not qualified
            reportRoot = root.Elements().FirstOrDefault(e => e.Name.LocalName is "BkToCstmrStmt" or "BkToCstmrAcctRpt");
            if (reportRoot == null)
            {
                return [];
            }
            ns = reportRoot.Name.Namespace;
        }

        string messageId = string.Empty;
        DateTimeOffset creationDateTime = DateTimeOffset.UtcNow;

        XElement? grpHdr = reportRoot.Element(ns + "GrpHdr");
        if (grpHdr != null)
        {
            messageId = grpHdr.Element(ns + "MsgId")?.Value.Trim() ?? string.Empty;
            string? creDtTmStr = grpHdr.Element(ns + "CreDtTm")?.Value.Trim();
            if (!string.IsNullOrEmpty(creDtTmStr) &&
                DateTimeOffset.TryParse(creDtTmStr, CultureInfo.InvariantCulture, DateTimeStyles.None, out var dt))
            {
                creationDateTime = dt;
            }
        }

        List<CamtStatement> statements = [];
        var statementElements = reportRoot.Elements().Where(e => e.Name.LocalName is "Stmt" or "Rpt");

        foreach (XElement stmtElem in statementElements)
        {
            if (statements.Count >= MaximumStatementsPerDocument)
            {
                throw new FormatException($"Document exceeds maximum statement limit of {MaximumStatementsPerDocument}.");
            }

            statements.Add(ParseStatement(stmtElem, ns, messageId, creationDateTime));
        }

        if (statements.Count == 0)
        {
            throw new FormatException("Document does not contain any valid CAMT statements (<Stmt> or <Rpt>).");
        }

        return statements.AsReadOnly();
    }

    private static CamtStatement ParseStatement(XElement stmt, XNamespace ns, string defaultMsgId, DateTimeOffset defaultCreDtTm)
    {
        string stmtId = stmt.Element(ns + "Id")?.Value.Trim() ?? string.Empty;

        XElement? acctElem = stmt.Element(ns + "Acct");
        string accountIban = acctElem?.Element(ns + "Id")?.Element(ns + "IBAN")?.Value.Trim() ?? string.Empty;
        string accountCcy = acctElem?.Element(ns + "Ccy")?.Value.Trim() ?? string.Empty;

        CamtBalance? openingBalance = null;
        CamtBalance? closingBalance = null;

        foreach (XElement balElem in stmt.Elements(ns + "Bal"))
        {
            var bal = ParseBalance(balElem, ns, accountCcy);
            if (bal != null)
            {
                if (bal.BalanceType is "OPBD" or "PRCD")
                {
                    openingBalance = bal;
                }
                else if (bal.BalanceType is "CLBD" or "ITBD")
                {
                    closingBalance = bal;
                }
            }
        }

        List<CamtEntry> entries = [];
        foreach (XElement ntryElem in stmt.Elements(ns + "Ntry"))
        {
            if (entries.Count >= MaximumEntriesPerStatement)
            {
                throw new FormatException($"Statement exceeds maximum entry count of {MaximumEntriesPerStatement}.");
            }

            ParseEntry(ntryElem, ns, accountCcy, entries);
        }

        return new CamtStatement(
            defaultMsgId,
            defaultCreDtTm,
            stmtId,
            accountIban,
            accountCcy,
            openingBalance,
            closingBalance,
            entries);
    }

    private static CamtBalance? ParseBalance(XElement balElem, XNamespace ns, string fallbackCurrency)
    {
        string balType = balElem.Element(ns + "Tp")?.Element(ns + "CdOrPrtry")?.Element(ns + "Cd")?.Value.Trim() ?? string.Empty;

        XElement? amtElem = balElem.Element(ns + "Amt");
        if (amtElem == null)
        {
            return null;
        }

        string currency = (string?)amtElem.Attribute("Ccy") ?? fallbackCurrency;
        string amtText = amtElem.Value.Trim();
        if (!decimal.TryParse(amtText, NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out decimal amount))
        {
            return null;
        }

        string ind = balElem.Element(ns + "CdtDbtInd")?.Value.Trim() ?? "CRDT";
        bool isCredit = ind.Equals("CRDT", StringComparison.OrdinalIgnoreCase);

        DateOnly date = DateOnly.FromDateTime(DateTime.UtcNow);
        string? dtText = balElem.Element(ns + "Dt")?.Element(ns + "Dt")?.Value.Trim();
        if (!string.IsNullOrEmpty(dtText) && DateOnly.TryParse(dtText[..Math.Min(10, dtText.Length)], CultureInfo.InvariantCulture, DateTimeStyles.None, out var d))
        {
            date = d;
        }

        return new CamtBalance(balType, new Money(amount, currency), isCredit, date);
    }

    private static void ParseEntry(XElement ntryElem, XNamespace ns, string fallbackCurrency, List<CamtEntry> entries)
    {
        XElement? amtElem = ntryElem.Element(ns + "Amt");
        if (amtElem == null)
        {
            return;
        }

        string ntryCurrency = (string?)amtElem.Attribute("Ccy") ?? fallbackCurrency;
        string amtText = amtElem.Value.Trim();
        if (!decimal.TryParse(amtText, NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out decimal ntryAmount))
        {
            return;
        }

        string ind = ntryElem.Element(ns + "CdtDbtInd")?.Value.Trim() ?? "CRDT";
        bool isCredit = ind.Equals("CRDT", StringComparison.OrdinalIgnoreCase);

        string sts = ntryElem.Element(ns + "Sts")?.Value.Trim() ?? "BOOK";
        TransactionStatus status = sts.Equals("PDNG", StringComparison.OrdinalIgnoreCase) || sts.Equals("INFO", StringComparison.OrdinalIgnoreCase)
            ? TransactionStatus.Pending
            : TransactionStatus.Booked;

        DateOnly bookingDate = ParseDateElement(ntryElem.Element(ns + "BookgDt"), ns);
        DateOnly valueDate = ParseDateElement(ntryElem.Element(ns + "ValDt"), ns);

        string? acctSvcrRef = ntryElem.Element(ns + "AcctSvcrRef")?.Value.Trim();
        string? bkTxCd = ntryElem.Element(ns + "BkTxCd")?.Element(ns + "Prtry")?.Element(ns + "Cd")?.Value.Trim()
                      ?? ntryElem.Element(ns + "BkTxCd")?.Element(ns + "Domn")?.Element(ns + "Fmly")?.Element(ns + "SubFmlyCd")?.Value.Trim();

        var txDetails = ntryElem.Element(ns + "NtryDtls")?.Elements(ns + "TxDtls").ToList() ?? [];

        if (txDetails.Count > 0)
        {
            foreach (XElement txElem in txDetails)
            {
                XElement? txAmtElem = txElem.Element(ns + "Amt");
                decimal txAmount = ntryAmount;
                string txCurrency = ntryCurrency;
                if (txAmtElem != null)
                {
                    string? ccy = (string?)txAmtElem.Attribute("Ccy");
                    if (!string.IsNullOrEmpty(ccy))
                    {
                        txCurrency = ccy.Trim();
                    }
                    if (decimal.TryParse(txAmtElem.Value.Trim(), NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out decimal a))
                    {
                        txAmount = a;
                    }
                }

                XElement? refs = txElem.Element(ns + "Refs");
                string? endToEndId = refs?.Element(ns + "EndToEndId")?.Value.Trim();
                string? mandateId = refs?.Element(ns + "MndtId")?.Value.Trim();

                string? creditorId = txElem.Element(ns + "CdtrSchmeId")?.Element(ns + "Id")?.Element(ns + "PrvtId")?.Element(ns + "Othr")?.Element(ns + "Id")?.Value.Trim();

                XElement? rltdPties = txElem.Element(ns + "RltdPties");
                string? counterpartyName = null;
                string? counterpartyIban = null;

                if (isCredit)
                {
                    // For incoming credit, counterparty is Debtor (Dbtr)
                    counterpartyName = rltdPties?.Element(ns + "Dbtr")?.Element(ns + "Nm")?.Value.Trim();
                    counterpartyIban = rltdPties?.Element(ns + "DbtrAcct")?.Element(ns + "Id")?.Element(ns + "IBAN")?.Value.Trim();
                }
                else
                {
                    // For outgoing debit, counterparty is Creditor (Cdtr)
                    counterpartyName = rltdPties?.Element(ns + "Cdtr")?.Element(ns + "Nm")?.Value.Trim();
                    counterpartyIban = rltdPties?.Element(ns + "CdtrAcct")?.Element(ns + "Id")?.Element(ns + "IBAN")?.Value.Trim();
                }

                XElement? rltdAgts = txElem.Element(ns + "RltdAgts");
                string? counterpartyBic = isCredit
                    ? rltdAgts?.Element(ns + "DbtrAgt")?.Element(ns + "FinInstnId")?.Element(ns + "BIC")?.Value.Trim()
                      ?? rltdAgts?.Element(ns + "DbtrAgt")?.Element(ns + "FinInstnId")?.Element(ns + "BICFI")?.Value.Trim()
                    : rltdAgts?.Element(ns + "CdtrAgt")?.Element(ns + "FinInstnId")?.Element(ns + "BIC")?.Value.Trim()
                      ?? rltdAgts?.Element(ns + "CdtrAgt")?.Element(ns + "FinInstnId")?.Element(ns + "BICFI")?.Value.Trim();

                var ustrdList = txElem.Element(ns + "RmtInf")?.Elements(ns + "Ustrd").Select(u => u.Value.Trim()).Where(s => s.Length > 0).ToList() ?? [];
                string remittanceText = string.Join(" ", ustrdList);

                string? returnReasonCode = txElem.Element(ns + "RtrInf")?.Element(ns + "Rsn")?.Element(ns + "Cd")?.Value.Trim();

                entries.Add(new CamtEntry(
                    entryReference: acctSvcrRef,
                    amount: new Money(txAmount, txCurrency),
                    isCredit: isCredit,
                    status: status,
                    bookingDate: bookingDate,
                    valueDate: valueDate,
                    bankTransactionCode: bkTxCd,
                    endToEndId: endToEndId,
                    mandateId: mandateId,
                    creditorId: creditorId,
                    counterpartyName: counterpartyName,
                    counterpartyIban: counterpartyIban,
                    counterpartyBic: counterpartyBic,
                    remittanceText: remittanceText,
                    returnReasonCode: returnReasonCode));
            }
        }
        else
        {
            entries.Add(new CamtEntry(
                entryReference: acctSvcrRef,
                amount: new Money(ntryAmount, ntryCurrency),
                isCredit: isCredit,
                status: status,
                bookingDate: bookingDate,
                valueDate: valueDate,
                bankTransactionCode: bkTxCd,
                endToEndId: null,
                mandateId: null,
                creditorId: null,
                counterpartyName: null,
                counterpartyIban: null,
                counterpartyBic: null,
                remittanceText: string.Empty,
                returnReasonCode: null));
        }
    }

    private static DateOnly ParseDateElement(XElement? dateParent, XNamespace ns)
    {
        if (dateParent != null)
        {
            string? text = dateParent.Element(ns + "Dt")?.Value.Trim() ?? dateParent.Element(ns + "DtTm")?.Value.Trim();
            if (!string.IsNullOrEmpty(text) &&
                DateOnly.TryParse(text[..Math.Min(10, text.Length)], CultureInfo.InvariantCulture, DateTimeStyles.None, out var d))
            {
                return d;
            }
        }

        return DateOnly.FromDateTime(DateTime.UtcNow);
    }
}
