using System.Text;
using System.Xml;
using Broiler.Fond.Kernel.Domain.Transactions;
using Broiler.Fond.Kernel.Formats.Camt;
using Broiler.Fond.Kernel.Formats.Mt940;

namespace Broiler.Fond.Kernel.Tests;

internal static class StatementWireParserTests
{
    internal static void Run(Action<bool, string> check)
    {
        int count = 0;
        void Verify(bool result, string message) { count++; check(result, message); }

        TestMt940(Verify);
        TestCamt(Verify);

        Console.WriteLine($"Statement wire parsers (MT940 and CAMT XML): {count} checks completed.");
    }

    private static void TestMt940(Action<bool, string> verify)
    {
        // 1. Standard MT940 single statement with structured SEPA Field 86
        string mt940Text =
            ":20:START20261014\r\n" +
            ":25:DE89370400440532013000\r\n" +
            ":28C:00042/001\r\n" +
            ":60F:C261013EUR1000,00\r\n" +
            ":61:2610141014D150,50NTRFCUSTREF123//BANKREF456\r\n" +
            ":86:?00LASTSCHRIFT?109999?20RECHNUNG 12345?21KUNDENNR 789\r\n" +
            "?30MARKDEF1100?31DE02100200301234567890?32ACME CORP GMBH\r\n" +
            "?34MANDATE-REF-999?35DE98ZZZ09999999999\r\n" +
            ":61:261014C500,00NTRFE2EREF//B2BREF\r\n" +
            ":86:?00UEBERWEISUNG?20GEHALT OKTOBER?30BYLADEM1001?31DE88700900001234567890?32MAX MUSTERMANN\r\n" +
            ":62F:C261014EUR1349,50\r\n" +
            "-";

        var statements = Mt940Parser.Parse(mt940Text);
        verify(statements.Count == 1, "MT940 batch must yield 1 statement.");

        var stmt = statements[0];
        verify(stmt.TransactionReference == "START20261014", "Transaction reference must match.");
        verify(stmt.AccountIdentification == "DE89370400440532013000", "Account ID must match.");
        verify(stmt.StatementNumber == "00042" && stmt.SequenceNumber == "001", "Statement/sequence number must parse.");
        verify(stmt.OpeningBalance.IsCredit && stmt.OpeningBalance.Amount.Amount == 1000.00m && stmt.OpeningBalance.Amount.Currency == "EUR", "Opening balance must match.");
        verify(stmt.OpeningBalance.Date == new DateOnly(2026, 10, 13), "Opening balance date must parse.");
        verify(stmt.ClosingBalance != null && stmt.ClosingBalance.IsCredit && stmt.ClosingBalance.Amount.Amount == 1349.50m, "Closing balance must match.");
        verify(stmt.Entries.Count == 2, "Statement must contain exactly 2 entries.");

        // Entry 1 (Debit, structured Field 86)
        var e1 = stmt.Entries[0];
        verify(!e1.IsCredit && !e1.IsReversal, "Entry 1 must be a normal debit.");
        verify(e1.Amount.Amount == 150.50m && e1.Amount.Currency == "EUR", "Entry 1 amount must match.");
        verify(e1.ValueDate == new DateOnly(2026, 10, 14) && e1.BookingDate == new DateOnly(2026, 10, 14), "Entry 1 dates must match.");
        verify(e1.CustomerReference == "CUSTREF123" && e1.BankReference == "BANKREF456", "Entry 1 references must parse.");
        verify(e1.CounterpartyName == "ACME CORP GMBH", "Counterparty name from ?32 must parse.");
        verify(e1.CounterpartyIban == "DE02100200301234567890", "Counterparty IBAN from ?31 must parse.");
        verify(e1.CounterpartyBic == "MARKDEF1100", "Counterparty BIC from ?30 must parse.");
        verify(e1.MandateId == "MANDATE-REF-999", "Mandate ID from ?34 must parse.");
        verify(e1.CreditorId == "DE98ZZZ09999999999", "Creditor ID from ?35 must parse.");
        verify(e1.RemittanceText == "RECHNUNG 12345 KUNDENNR 789", "Remittance text from ?20 and ?21 must be combined.");

        // Entry 2 (Credit)
        var e2 = stmt.Entries[1];
        verify(e2.IsCredit && !e2.IsReversal, "Entry 2 must be credit.");
        verify(e2.Amount.Amount == 500.00m, "Entry 2 amount must match.");
        verify(e2.CounterpartyName == "MAX MUSTERMANN", "Entry 2 counterparty name must parse.");
        verify(e2.CounterpartyIban == "DE88700900001234567890", "Entry 2 counterparty IBAN must parse.");
        verify(e2.RemittanceText == "GEHALT OKTOBER", "Entry 2 remittance text must parse.");

        // 2. Reversal entries (RC, RD)
        string reversalText =
            ":20:REVTEST\r\n" +
            ":25:DE89370400440532013000\r\n" +
            ":28:1\r\n" +
            ":60F:C261014EUR500,00\r\n" +
            ":61:261014RC50,00NCHGNONREF\r\n" +
            ":86:STORNO GUTSCHRIFT\r\n" +
            ":61:261014RD25,00NCHGNONREF\r\n" +
            ":86:STORNO LASTSCHRIFT\r\n" +
            ":62F:C261014EUR475,00\r\n";

        var revStmts = Mt940Parser.Parse(reversalText);
        verify(revStmts.Count == 1 && revStmts[0].Entries.Count == 2, "Reversal MT940 must parse.");
        var rc = revStmts[0].Entries[0];
        verify(!rc.IsCredit && rc.IsReversal && rc.Amount.Amount == 50.00m, "RC must be debited reversal.");
        verify(rc.RemittanceText == "STORNO GUTSCHRIFT", "Unstructured remittance text must be retained.");

        var rd = revStmts[0].Entries[1];
        verify(rd.IsCredit && rd.IsReversal && rd.Amount.Amount == 25.00m, "RD must be credited reversal.");

        // 3. Multi-statement batch
        string multiBatch =
            ":20:STMT1\r\n:25:ACC1\r\n:28:1\r\n:60F:C261014EUR100,00\r\n:62F:C261014EUR100,00\r\n-\r\n" +
            ":20:STMT2\r\n:25:ACC2\r\n:28:2\r\n:60F:C261014EUR200,00\r\n:62F:C261014EUR200,00\r\n-";
        var multiStmts = Mt940Parser.Parse(multiBatch);
        verify(multiStmts.Count == 2 && multiStmts[0].TransactionReference == "STMT1" && multiStmts[1].TransactionReference == "STMT2",
            "Multi-statement batch must parse separate statements.");

        // 4. Byte overload
        var byteStmts = Mt940Parser.Parse(Encoding.Latin1.GetBytes(mt940Text));
        verify(byteStmts.Count == 1 && byteStmts[0].Entries.Count == 2, "Byte span overload must parse identical content.");

        // 5. Error handling / fail-closed
        try
        {
            _ = Mt940Parser.Parse(":25:ONLYACCOUNT\r\n:60F:C261014EUR100,00\r\n");
            verify(false, "Missing :20: must fail.");
        }
        catch (FormatException)
        {
            verify(true, "Missing :20: fails safely.");
        }

        try
        {
            _ = Mt940Parser.Parse(":20:TRN\r\n:28:1\r\n:60F:C261014EUR100,00\r\n");
            verify(false, "Missing :25: must fail.");
        }
        catch (FormatException)
        {
            verify(true, "Missing :25: fails safely.");
        }

        try
        {
            _ = Mt940Parser.Parse(":20:TRN\r\n:25:ACC\r\n:60F:C261014EURBADAMOUNT\r\n");
            verify(false, "Corrupted balance amount must fail.");
        }
        catch (FormatException)
        {
            verify(true, "Corrupted balance amount fails safely.");
        }
    }

    private static void TestCamt(Action<bool, string> verify)
    {
        // 1. camt.053 booked statement XML
        string camt053Xml =
            """
            <?xml version="1.0" encoding="UTF-8"?>
            <Document xmlns="urn:iso:std:iso:20022:tech:xsd:camt.053.001.02">
              <BkToCstmrStmt>
                <GrpHdr>
                  <MsgId>MSG-20261014-001</MsgId>
                  <CreDtTm>2026-10-14T12:00:00Z</CreDtTm>
                </GrpHdr>
                <Stmt>
                  <Id>STMT-2026-042</Id>
                  <Acct>
                    <Id><IBAN>DE89370400440532013000</IBAN></Id>
                    <Ccy>EUR</Ccy>
                  </Acct>
                  <Bal>
                    <Tp><CdOrPrtry><Cd>OPBD</Cd></CdOrPrtry></Tp>
                    <Amt Ccy="EUR">2500.00</Amt>
                    <CdtDbtInd>CRDT</CdtDbtInd>
                    <Dt><Dt>2026-10-13</Dt></Dt>
                  </Bal>
                  <Bal>
                    <Tp><CdOrPrtry><Cd>CLBD</Cd></CdOrPrtry></Tp>
                    <Amt Ccy="EUR">2875.50</Amt>
                    <CdtDbtInd>CRDT</CdtDbtInd>
                    <Dt><Dt>2026-10-14</Dt></Dt>
                  </Bal>
                  <Ntry>
                    <Amt Ccy="EUR">124.50</Amt>
                    <CdtDbtInd>DBIT</CdtDbtInd>
                    <Sts>BOOK</Sts>
                    <BookgDt><Dt>2026-10-14</Dt></BookgDt>
                    <ValDt><Dt>2026-10-14</Dt></ValDt>
                    <AcctSvcrRef>BNKREF-111</AcctSvcrRef>
                    <BkTxCd><Prtry><Cd>NTRF</Cd></Prtry></BkTxCd>
                    <NtryDtls>
                      <TxDtls>
                        <Refs>
                          <EndToEndId>E2E-BUY-001</EndToEndId>
                          <MndtId>MANDATE-555</MndtId>
                        </Refs>
                        <CdtrSchmeId><Id><PrvtId><Othr><Id>DE99ZZZ00000000001</Id></Othr></PrvtId></Id></CdtrSchmeId>
                        <RltdPties>
                          <Cdtr><Nm>ONLINE SHOP SE</Nm></Cdtr>
                          <CdtrAcct><Id><IBAN>DE44500105175407324931</IBAN></Id></CdtrAcct>
                        </RltdPties>
                        <RltdAgts>
                          <CdtrAgt><FinInstnId><BIC>INGDDEFFXXX</BIC></FinInstnId></CdtrAgt>
                        </RltdAgts>
                        <RmtInf>
                          <Ustrd>ORDER 987654</Ustrd>
                          <Ustrd>THANK YOU</Ustrd>
                        </RmtInf>
                      </TxDtls>
                    </NtryDtls>
                  </Ntry>
                  <Ntry>
                    <Amt Ccy="EUR">500.00</Amt>
                    <CdtDbtInd>CRDT</CdtDbtInd>
                    <Sts>BOOK</Sts>
                    <BookgDt><Dt>2026-10-14</Dt></BookgDt>
                    <ValDt><Dt>2026-10-14</Dt></ValDt>
                    <NtryDtls>
                      <TxDtls>
                        <Refs><EndToEndId>E2E-REFUND-002</EndToEndId></Refs>
                        <RltdPties>
                          <Dbtr><Nm>REVENUE OFFICE</Nm></Dbtr>
                          <DbtrAcct><Id><IBAN>DE02100200301234567890</IBAN></Id></DbtrAcct>
                        </RltdPties>
                        <RmtInf><Ustrd>TAX REFUND 2025</Ustrd></RmtInf>
                      </TxDtls>
                    </NtryDtls>
                  </Ntry>
                </Stmt>
              </BkToCstmrStmt>
            </Document>
            """;

        var camtStmts = CamtXmlParser.Parse(camt053Xml);
        verify(camtStmts.Count == 1, "CAMT.053 document must yield 1 statement.");

        var cStmt = camtStmts[0];
        verify(cStmt.MessageId == "MSG-20261014-001", "MessageId must match.");
        verify(cStmt.StatementId == "STMT-2026-042", "StatementId must match.");
        verify(cStmt.AccountIban == "DE89370400440532013000" && cStmt.AccountCurrency == "EUR", "Account IBAN and Currency must match.");
        verify(cStmt.OpeningBalance != null && cStmt.OpeningBalance.Amount.Amount == 2500.00m && cStmt.OpeningBalance.IsCredit, "Opening balance must match.");
        verify(cStmt.ClosingBalance != null && cStmt.ClosingBalance.Amount.Amount == 2875.50m && cStmt.ClosingBalance.IsCredit, "Closing balance must match.");
        verify(cStmt.Entries.Count == 2, "Statement must have 2 entries.");

        // Entry 1 (Debit)
        var ce1 = cStmt.Entries[0];
        verify(!ce1.IsCredit && ce1.Status == TransactionStatus.Booked, "Entry 1 must be booked debit.");
        verify(ce1.Amount.Amount == 124.50m && ce1.Amount.Currency == "EUR", "Entry 1 amount must match.");
        verify(ce1.EntryReference == "BNKREF-111", "Entry reference must parse.");
        verify(ce1.BankTransactionCode == "NTRF", "Bank transaction code must parse.");
        verify(ce1.EndToEndId == "E2E-BUY-001", "EndToEndId must parse.");
        verify(ce1.MandateId == "MANDATE-555", "MandateId must parse.");
        verify(ce1.CreditorId == "DE99ZZZ00000000001", "CreditorId must parse.");
        verify(ce1.CounterpartyName == "ONLINE SHOP SE", "Creditor name must parse for debit.");
        verify(ce1.CounterpartyIban == "DE44500105175407324931", "Creditor IBAN must parse.");
        verify(ce1.CounterpartyBic == "INGDDEFFXXX", "Creditor BIC must parse.");
        verify(ce1.RemittanceText == "ORDER 987654 THANK YOU", "Multiple Ustrd lines must be combined.");

        // Entry 2 (Credit)
        var ce2 = cStmt.Entries[1];
        verify(ce2.IsCredit && ce2.Status == TransactionStatus.Booked, "Entry 2 must be booked credit.");
        verify(ce2.Amount.Amount == 500.00m, "Entry 2 amount must match.");
        verify(ce2.CounterpartyName == "REVENUE OFFICE", "Debtor name must parse for credit.");
        verify(ce2.CounterpartyIban == "DE02100200301234567890", "Debtor IBAN must parse for credit.");
        verify(ce2.RemittanceText == "TAX REFUND 2025", "Remittance text must parse.");

        // 2. camt.052 pending / interim account report
        string camt052Xml =
            """
            <?xml version="1.0" encoding="UTF-8"?>
            <Document xmlns="urn:iso:std:iso:20022:tech:xsd:camt.052.001.02">
              <BkToCstmrAcctRpt>
                <GrpHdr><MsgId>INTERIM-001</MsgId></GrpHdr>
                <Rpt>
                  <Id>RPT-001</Id>
                  <Acct><Id><IBAN>DE89370400440532013000</IBAN></Id><Ccy>EUR</Ccy></Acct>
                  <Ntry>
                    <Amt Ccy="EUR">45.00</Amt>
                    <CdtDbtInd>DBIT</CdtDbtInd>
                    <Sts>PDNG</Sts>
                    <BookgDt><Dt>2026-10-15</Dt></BookgDt>
                    <ValDt><Dt>2026-10-15</Dt></ValDt>
                    <NtryDtls>
                      <TxDtls>
                        <RltdPties><Cdtr><Nm>CARD RESERVATION</Nm></Cdtr></RltdPties>
                        <RmtInf><Ustrd>POS AUTHORIZATION</Ustrd></RmtInf>
                      </TxDtls>
                    </NtryDtls>
                  </Ntry>
                </Rpt>
              </BkToCstmrAcctRpt>
            </Document>
            """;

        var interimStmts = CamtXmlParser.Parse(camt052Xml);
        verify(interimStmts.Count == 1 && interimStmts[0].Entries.Count == 1, "CAMT.052 interim report must parse.");
        var pendingEntry = interimStmts[0].Entries[0];
        verify(pendingEntry.Status == TransactionStatus.Pending, "Pending status (PDNG) must map to TransactionStatus.Pending.");
        verify(!pendingEntry.IsCredit && pendingEntry.Amount.Amount == 45.00m, "Pending entry amount must match.");
        verify(pendingEntry.CounterpartyName == "CARD RESERVATION", "Pending entry counterparty must parse.");

        // 3. Return Reason Code
        string returnXml =
            """
            <?xml version="1.0" encoding="UTF-8"?>
            <Document xmlns="urn:iso:std:iso:20022:tech:xsd:camt.053.001.02">
              <BkToCstmrStmt>
                <Stmt>
                  <Id>S1</Id>
                  <Acct><Id><IBAN>DE89370400440532013000</IBAN></Id><Ccy>EUR</Ccy></Acct>
                  <Ntry>
                    <Amt Ccy="EUR">80.00</Amt>
                    <CdtDbtInd>CRDT</CdtDbtInd>
                    <Sts>BOOK</Sts>
                    <NtryDtls>
                      <TxDtls>
                        <RtrInf><Rsn><Cd>MS02</Cd></Rsn></RtrInf>
                        <RmtInf><Ustrd>RETURN DEBIT</Ustrd></RmtInf>
                      </TxDtls>
                    </NtryDtls>
                  </Ntry>
                </Stmt>
              </BkToCstmrStmt>
            </Document>
            """;
        var retStmts = CamtXmlParser.Parse(returnXml);
        verify(retStmts[0].Entries[0].ReturnReasonCode == "MS02", "Return reason code (MS02) must parse.");

        // 4. Security & DTD injection protection
        string maliciousDtd =
            """
            <?xml version="1.0" encoding="UTF-8"?>
            <!DOCTYPE test [ <!ENTITY xxe SYSTEM "file:///etc/passwd"> ]>
            <Document xmlns="urn:iso:std:iso:20022:tech:xsd:camt.053.001.02">
              <BkToCstmrStmt><Stmt><Id>&xxe;</Id></Stmt></BkToCstmrStmt>
            </Document>
            """;
        try
        {
            _ = CamtXmlParser.Parse(maliciousDtd);
            verify(false, "DTD processing must be prohibited.");
        }
        catch (XmlException)
        {
            verify(true, "DTD processing prohibited successfully.");
        }
    }
}
