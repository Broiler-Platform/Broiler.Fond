using Broiler.Fond.Kernel.Domain;
using Broiler.Fond.Kernel.Domain.Accounts;
using Broiler.Fond.Kernel.Domain.Identifiers;
using Broiler.Fond.Kernel.FinTs;

namespace Broiler.Fond.Kernel.Tests;

public static class FinTs41AccountOperationsTests
{
    public static void RunAllTests()
    {
        Console.WriteLine("Running FinTS 4.1 Account Operations verification (M9-04)...");
        int checks = 0;

        // 1. Account Discovery Request (HKSPA v3)
        var hkspa = FinTs41AccountOperations.BuildHkspaRequest(1);
        Assert(hkspa.Tag == "HKSPA" && hkspa.Version == 3 && hkspa.SegmentNumber == 1, "HKSPA v3 segment header must match");
        Assert(hkspa.GetElementValue("allAccounts") == "true", "allAccounts must be true");
        checks += 2;

        // 2. Account Discovery Response (HISPA v3)
        var hispa = new FinTs41Segment("HISPA", 3, 1,
        [
            FinTs41Element.Leaf("iban", "DE89370400440532013000"),
            FinTs41Element.Leaf("bic", "DBBDEFFXXX"),
            FinTs41Element.Leaf("accountNumber", "0532013000"),
            FinTs41Element.Leaf("subAccount", "01"),
            FinTs41Element.Leaf("currency", "EUR"),
            FinTs41Element.Leaf("holderName", "Erika Mustermann"),
            FinTs41Element.Leaf("accountType", "Girokonto")
        ]);

        var discoveredAccounts = FinTs41AccountOperations.ParseHispaResponse(hispa);
        Assert(discoveredAccounts.Count == 1, "Must parse 1 account");
        var acct = discoveredAccounts[0];
        Assert(acct.Iban == "DE89370400440532013000", "IBAN must match");
        Assert(acct.Bic == "DBBDEFFXXX", "BIC must match");
        Assert(acct.Currency == "EUR", "Currency must match");
        Assert(acct.SubAccount == "01", "SubAccount must match");
        Assert(acct.HolderName == "Erika Mustermann", "HolderName must match");

        var config = Broiler.Fond.Kernel.Domain.Institutions.ManualInstitutionConfiguration.Create("Test Bank", "https://fints.example.com/fints")
            .ConfirmEndpoint("https://fints.example.com/fints");
        var locator = acct.ToSourceLocator(new InstitutionId(101), new ConnectionId(102), config);
        Assert(locator.ConnectorVersion == "FinTS-4.1", "Source locator connector version must be FinTS-4.1");
        Assert(locator.Iban == "DE89370400440532013000", "Source locator IBAN must match");
        Assert(locator.Subaccount == "01", "Source locator subaccount must match");
        checks += 9;

        // 3. Balance Query Request (HKSAL v8)
        var hksal = FinTs41AccountOperations.BuildHksalRequest(2, "DE89370400440532013000", "DBBDEFFXXX");
        Assert(hksal.Tag == "HKSAL" && hksal.Version == 8 && hksal.SegmentNumber == 2, "HKSAL v8 segment header must match");
        Assert(hksal.GetElementValue("iban") == "DE89370400440532013000", "HKSAL IBAN must match");
        Assert(hksal.GetElementValue("bic") == "DBBDEFFXXX", "HKSAL BIC must match");
        checks += 3;

        // 4. Balance Query Response (HISAL v8)
        var now = DateTimeOffset.UtcNow;
        var hisal = new FinTs41Segment("HISAL", 8, 2,
        [
            FinTs41Element.Leaf("iban", "DE89370400440532013000"),
            FinTs41Element.Leaf("currency", "EUR"),
            FinTs41Element.Leaf("bookedBalance", "1450.75"),
            FinTs41Element.Leaf("availableBalance", "1950.75"),
            FinTs41Element.Leaf("creditLine", "500.00"),
            FinTs41Element.Leaf("timestamp", now.ToString("O"))
        ]);

        var balance = FinTs41AccountOperations.ParseHisalResponse(hisal);
        Assert(balance != null, "HISAL balance must parse");
        Assert(balance!.Iban == "DE89370400440532013000", "Balance IBAN must match");
        Assert(balance.BookedBalance == new Money(1450.75m, "EUR"), "Booked balance must match exactly");
        Assert(balance.AvailableBalance == new Money(1950.75m, "EUR"), "Available balance must match exactly");
        Assert(balance.CreditLine == new Money(500.00m, "EUR"), "Credit line must match exactly");
        checks += 5;

        // 5. Embedded CAMT Statement Response (HICAZ)
        var hicaz = new FinTs41Segment("HICAZ", 1, 3,
        [
            FinTs41Element.Leaf("entryReference", "TX-CAMT-001"),
            FinTs41Element.Leaf("endToEndId", "E2E-CAMT-REF"),
            FinTs41Element.Leaf("uetr", "550e8400-e29b-41d4-a716-446655440000"),
            FinTs41Element.Leaf("amount", "250.00"),
            FinTs41Element.Leaf("currency", "EUR"),
            FinTs41Element.Leaf("creditDebitIndicator", "CRDT"),
            FinTs41Element.Leaf("bookingDate", "2026-10-18"),
            FinTs41Element.Leaf("valueDate", "2026-10-18"),
            FinTs41Element.Leaf("counterpartyName", "Arbeitgeber GmbH"),
            FinTs41Element.Leaf("counterpartyIban", "DE02100100100123456789"),
            FinTs41Element.Leaf("remittanceInformation", "Gehalt Oktober 2026")
        ]);

        var camtTxList = FinTs41AccountOperations.ParseHicazResponse(hicaz);
        Assert(camtTxList.Count == 1, "Must parse 1 CAMT transaction");
        var tx = camtTxList[0];
        Assert(tx.EntryReference == "TX-CAMT-001", "EntryReference must match");
        Assert(tx.EndToEndId == "E2E-CAMT-REF", "EndToEndId must match");
        Assert(tx.Uetr == "550e8400-e29b-41d4-a716-446655440000", "UETR must match");
        Assert(tx.Amount == new Money(250.00m, "EUR"), "Amount must match");
        Assert(tx.IsCredit, "Must be credit");
        Assert(tx.BookingDate == new DateOnly(2026, 10, 18), "Booking date must match");
        Assert(tx.CounterpartyName == "Arbeitgeber GmbH", "Counterparty name must match");
        Assert(tx.RemittanceInformation == "Gehalt Oktober 2026", "Remittance info must match");
        checks += 9;

        // 6. SEPA Credit Transfer Request (HKCCS v2)
        var hkccs = FinTs41AccountOperations.BuildHkccsRequest(
            4,
            "DE89370400440532013000",
            "DE02100100100123456789",
            "Max Mustermann",
            new Money(42.50m, "EUR"),
            "Rechnung 1234",
            "E2E-SEPA-999",
            vopChecked: true);

        Assert(hkccs.Tag == "HKCCS" && hkccs.Version == 2 && hkccs.SegmentNumber == 4, "HKCCS v2 header must match");
        Assert(hkccs.GetElementValue("sourceIban") == "DE89370400440532013000", "Source IBAN must match");
        Assert(hkccs.GetElementValue("recipientIban") == "DE02100100100123456789", "Recipient IBAN must match");
        Assert(hkccs.GetElementValue("amount") == "42.50", "Amount must match");
        Assert(hkccs.GetElementValue("currency") == "EUR", "Currency must match");
        Assert(hkccs.GetElementValue("vopChecked") == "true", "vopChecked must be true");
        Assert(hkccs.GetElementValue("endToEndId") == "E2E-SEPA-999", "EndToEndId must match");
        checks += 7;

        Console.WriteLine($"FinTS 4.1 Account Operations (M9-04): {checks} checks completed successfully.");
    }

    private static void Assert(bool condition, string message)
    {
        if (!condition)
        {
            throw new InvalidOperationException($"Assertion failed: {message}");
        }
    }
}
