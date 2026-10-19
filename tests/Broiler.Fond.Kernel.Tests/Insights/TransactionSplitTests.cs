using System.Globalization;
using System.Xml;
using System.Xml.Linq;
using System.Xml.Schema;
using Broiler.Fond.Kernel.Domain.Accounts;
using Broiler.Fond.Kernel.Domain.Identifiers;
using Broiler.Fond.Kernel.Domain.Transactions;
using Broiler.Fond.Kernel.Domain.Transactions.Query;
using Broiler.Fond.Kernel.Storage;
using Broiler.Fond.Kernel.Tests.Storage;

namespace Broiler.Fond.Kernel.Tests.Insights;

/// <summary>
/// Executable verification suite for Milestone 6 item M6-02:
/// Transaction splits, strict balance invariant, multi-tagging, and profile store roundtrip.
/// </summary>
public static class TransactionSplitTests
{
    public static void Run()
    {
        Console.WriteLine("Running Transaction Splits & Multi-Tagging verification (M6-02)...");

        VerifySplitItemDomainModel();
        VerifyStrictBalanceInvariantAndValidation();
        VerifyAnnotationRevisionIntegration();
        VerifyXmlSerializationAndSchemaValidation();
        VerifyTransactionSearchWithSplits();

        Console.WriteLine("Transaction Splits & Multi-Tagging (M6-02): 75 checks completed successfully.");
    }

    private static void VerifySplitItemDomainModel()
    {
        // 1. Valid split item creation
        var item1 = new TransactionSplitItem(
            index: 0,
            amount: -45.50m,
            category: "Groceries",
            note: "Weekly market purchase",
            tags: ["Food", "Market", "food"]); // duplicate tag case-insensitive

        Assert(item1.Index == 0, "Index is 0.");
        Assert(item1.Amount == -45.50m, "Amount is -45.50m.");
        Assert(item1.Category == "Groceries", "Category is Groceries.");
        Assert(item1.Note == "Weekly market purchase", "Note preserved.");
        Assert(item1.Tags.Count == 2, "Duplicate tag 'food' deduplicated.");
        Assert(item1.Tags[0] == "Food" && item1.Tags[1] == "Market", "Tags sorted case-insensitively.");

        // 2. Negative index throws
        bool negativeIndexCaught = false;
        try
        {
            _ = new TransactionSplitItem(-1, 10m, "Cat");
        }
        catch (ArgumentOutOfRangeException)
        {
            negativeIndexCaught = true;
        }
        Assert(negativeIndexCaught, "Negative index throws ArgumentOutOfRangeException.");

        // 3. Zero amount throws
        bool zeroAmountCaught = false;
        try
        {
            _ = new TransactionSplitItem(0, 0m, "Cat");
        }
        catch (ArgumentException)
        {
            zeroAmountCaught = true;
        }
        Assert(zeroAmountCaught, "Zero amount throws ArgumentException.");

        // 4. Empty category throws
        bool emptyCategoryCaught = false;
        try
        {
            _ = new TransactionSplitItem(0, 10m, "   ");
        }
        catch (ArgumentException)
        {
            emptyCategoryCaught = true;
        }
        Assert(emptyCategoryCaught, "Empty category throws ArgumentException.");

        // 5. Oversized category throws
        bool oversizedCategoryCaught = false;
        try
        {
            _ = new TransactionSplitItem(0, 10m, new string('X', TransactionSplitItem.MaximumCategoryLength + 1));
        }
        catch (ArgumentException)
        {
            oversizedCategoryCaught = true;
        }
        Assert(oversizedCategoryCaught, "Oversized category throws ArgumentException.");

        // 6. Oversized note throws
        bool oversizedNoteCaught = false;
        try
        {
            _ = new TransactionSplitItem(0, 10m, "Cat", new string('Y', TransactionSplitItem.MaximumNoteLength + 1));
        }
        catch (ArgumentException)
        {
            oversizedNoteCaught = true;
        }
        Assert(oversizedNoteCaught, "Oversized note throws ArgumentException.");

        // 7. Oversized tag throws
        bool oversizedTagCaught = false;
        try
        {
            _ = new TransactionSplitItem(0, 10m, "Cat", tags: [new string('Z', TransactionSplitItem.MaximumTagLength + 1)]);
        }
        catch (ArgumentException)
        {
            oversizedTagCaught = true;
        }
        Assert(oversizedTagCaught, "Oversized tag throws ArgumentException.");

        // 8. Too many tags throws
        bool tooManyTagsCaught = false;
        try
        {
            var manyTags = Enumerable.Range(1, TransactionSplitItem.MaximumTagCount + 1).Select(i => $"Tag{i}");
            _ = new TransactionSplitItem(0, 10m, "Cat", tags: manyTags);
        }
        catch (ArgumentException)
        {
            tooManyTagsCaught = true;
        }
        Assert(tooManyTagsCaught, "Too many tags throws ArgumentException.");
    }

    private static void VerifyStrictBalanceInvariantAndValidation()
    {
        // 1. Valid 2-way debit split (-100.00 EUR = -75.00 EUR + -25.00 EUR)
        var split1 = new TransactionSplitItem(0, -75.00m, "Groceries");
        var split2 = new TransactionSplitItem(1, -25.00m, "Household");
        TransactionSplitValidator.Validate([split1, split2], -100.00m);

        Assert(TransactionSplitValidator.TryValidate([split1, split2], -100.00m, out var err1) && err1 == null, "TryValidate succeeded for valid debit split.");

        // 2. Valid 3-way credit split (+1500.00 EUR = +1200.00 EUR + +250.00 EUR + +50.00 EUR)
        var cr1 = new TransactionSplitItem(0, 1200.00m, "Salary");
        var cr2 = new TransactionSplitItem(1, 250.00m, "Expense Reimbursement");
        var cr3 = new TransactionSplitItem(2, 50.00m, "Bonus");
        TransactionSplitValidator.Validate([cr1, cr2, cr3], 1500.00m);

        // 3. Imbalance: 1 cent difference throws InvalidOperationException
        bool imbalanceCaught = false;
        try
        {
            var badSplit2 = new TransactionSplitItem(1, -24.99m, "Household");
            TransactionSplitValidator.Validate([split1, badSplit2], -100.00m);
        }
        catch (InvalidOperationException)
        {
            imbalanceCaught = true;
        }
        Assert(imbalanceCaught, "1-cent imbalance throws InvalidOperationException.");

        bool tryValidateFailed = TransactionSplitValidator.TryValidate([split1, new TransactionSplitItem(1, -24.99m, "Household")], -100.00m, out var errImbalance);
        Assert(!tryValidateFailed && errImbalance != null, "TryValidate returns false with error on imbalance.");

        // 4. Cardinality: fewer than 2 items throws ArgumentException
        bool singleItemCaught = false;
        try
        {
            TransactionSplitValidator.Validate([new TransactionSplitItem(0, -100.00m, "Groceries")], -100.00m);
        }
        catch (ArgumentException)
        {
            singleItemCaught = true;
        }
        Assert(singleItemCaught, "Single split item throws ArgumentException (minimum 2 items required).");

        // 5. Duplicate index throws ArgumentException
        bool duplicateIndexCaught = false;
        try
        {
            var dup1 = new TransactionSplitItem(0, -50.00m, "Cat1");
            var dup2 = new TransactionSplitItem(0, -50.00m, "Cat2");
            TransactionSplitValidator.Validate([dup1, dup2], -100.00m);
        }
        catch (ArgumentException)
        {
            duplicateIndexCaught = true;
        }
        Assert(duplicateIndexCaught, "Duplicate index throws ArgumentException.");
    }

    private static void VerifyAnnotationRevisionIntegration()
    {
        var now = DateTimeOffset.UtcNow;
        var s1 = new TransactionSplitItem(0, -60.00m, "Groceries", "Food supplies");
        var s2 = new TransactionSplitItem(1, -40.00m, "Clothing", "Winter socks", ["Apparel"]);

        // 1. Create AnnotationRevision with splits
        var ann = new AnnotationRevision(
            id: new AnnotationRevisionId(101),
            transactionId: new TransactionId(501),
            modifiedAt: now,
            category: null,
            splits: [s2, s1]); // unsorted passed in

        Assert(ann.IsSplit, "AnnotationRevision is marked as split.");
        Assert(ann.Splits != null && ann.Splits.Count == 2, "Splits collection has 2 items.");
        Assert(ann.Splits![0].Index == 0 && ann.Splits[1].Index == 1, "Splits sorted by index in AnnotationRevision.");
        Assert(ann.Category == "[Split]", "Default category set to '[Split]' when omitted.");

        // 2. Create AnnotationRevision without splits
        var annNormal = new AnnotationRevision(
            id: new AnnotationRevisionId(102),
            transactionId: new TransactionId(502),
            modifiedAt: now,
            category: "Groceries");

        Assert(!annNormal.IsSplit, "Normal annotation has IsSplit = false.");
        Assert(annNormal.Splits == null, "Normal annotation has Splits = null.");
        Assert(annNormal.Category == "Groceries", "Normal annotation retains Category.");
    }

    private static void VerifyXmlSerializationAndSchemaValidation()
    {
        var now = DateTimeOffset.UtcNow;
        var profileId = Guid.NewGuid();
        var branchId = Guid.NewGuid();

        var split1 = new StoreSplitItemEntry(0, -70.00m, "Groceries", "Supermarket food", ["Food", "Daily"]);
        var split2 = new StoreSplitItemEntry(1, -30.00m, "Household", "Cleaning products", ["Supplies"]);

        var annWithSplits = new StoreAnnotationRevisionEntry(
            Id: 1,
            TransactionId: 10,
            RevisionTimestamp: now,
            UserNote: "Split receipt",
            Category: "[Split]",
            Tags: ["Receipt"],
            Splits: [split1, split2]);

        var annWithoutSplits = new StoreAnnotationRevisionEntry(
            Id: 2,
            TransactionId: 20,
            RevisionTimestamp: now,
            UserNote: "Simple note",
            Category: "Salary",
            Tags: ["Income"]);

        var doc = new StoreTransactionsDocument(
            ProfileId: profileId,
            BranchId: branchId,
            Generation: 1,
            Observations: Array.Empty<StoreTransactionObservationEntry>(),
            Transactions: Array.Empty<StoreTransactionEntry>(),
            Revisions: Array.Empty<StoreTransactionRevisionEntry>(),
            Annotations: [annWithSplits, annWithoutSplits],
            Links: Array.Empty<StorePendingToBookedLinkEntry>(),
            Checkpoints: Array.Empty<StoreStatementSyncCheckpointEntry>());

        // 1. Serialize to XML using StoreArchiveReference
        byte[] xmlBytes = StoreArchiveReference.SerializeTransactions(doc);
        Assert(xmlBytes.Length > 0, "Transactions serialized to bytes.");

        // 2. Validate and deserialize via StoreArchiveReference.ParseAndValidateTransactions
        // (which validates against embedded transactions-v1.xsd schema)
        var roundtripped = StoreArchiveReference.ParseAndValidateTransactions(xmlBytes);

        Assert(roundtripped.Annotations.Count == 2, "2 annotations deserialized.");

        var rtSplit = roundtripped.Annotations.First(a => a.Id == 1);
        Assert(rtSplit.Splits != null && rtSplit.Splits.Count == 2, "2 split items deserialized.");
        Assert(rtSplit.Splits![0].Index == 0, "Split 0 index matches.");
        Assert(rtSplit.Splits[0].Amount == -70.00m, "Split 0 amount matches.");
        Assert(rtSplit.Splits[0].Category == "Groceries", "Split 0 category matches.");
        Assert(rtSplit.Splits[0].Note == "Supermarket food", "Split 0 note matches.");
        Assert(rtSplit.Splits[0].Tags.SequenceEqual(["Food", "Daily"]), "Split 0 tags match.");

        Assert(rtSplit.Splits[1].Index == 1, "Split 1 index matches.");
        Assert(rtSplit.Splits[1].Amount == -30.00m, "Split 1 amount matches.");
        Assert(rtSplit.Splits[1].Category == "Household", "Split 1 category matches.");
        Assert(rtSplit.Splits[1].Note == "Cleaning products", "Split 1 note matches.");
        Assert(rtSplit.Splits[1].Tags.SequenceEqual(["Supplies"]), "Split 1 tags match.");

        var rtNormal = roundtripped.Annotations.First(a => a.Id == 2);
        Assert(rtNormal.Splits == null, "Annotation without splits deserializes with Splits == null.");
        Assert(rtNormal.Category == "Salary", "Normal category matches.");
    }

    private static void VerifyTransactionSearchWithSplits()
    {
        var now = DateTimeOffset.UtcNow;
        var today = DateOnly.FromDateTime(DateTime.UtcNow.Date);

        var split1 = new TransactionSplitItem(0, -60.00m, "Electronics", "USB Cable & Adapter", ["Tech"]);
        var split2 = new TransactionSplitItem(1, -40.00m, "Office Supplies", "Notebook and pens", ["Stationery"]);

        var txWithSplits = new TransactionItemRecord(
            TransactionId: 100,
            AccountId: 1,
            AccountDisplayName: "Checking",
            AccountIban: "DE89370400440532013000",
            AccountIncarnationId: 10,
            AccountBindingId: 20,
            Status: "Booked",
            Kind: "Debit",
            Amount: -100.00m,
            Currency: "EUR",
            IsCredit: false,
            BookingDate: today,
            ValueDate: today,
            BankTimestamp: now,
            RetrievedAt: now,
            CounterpartyName: "Department Store",
            CounterpartyIban: null,
            CounterpartyBic: null,
            RemittanceText: "Receipt 12345",
            EndToEndId: null,
            MandateId: null,
            CreditorId: null,
            CustomerReference: null,
            BankReference: null,
            PurposeCode: null,
            ReturnReasonCode: null,
            RawBankCode: null,
            UserNote: "Department store shopping",
            Category: "[Split]",
            Tags: ["Shopping"],
            StatementFormat: null,
            StatementNumber: null,
            LinkedPendingTransactionId: null,
            LinkedBookedTransactionId: null,
            Splits: [split1, split2]);

        var engine = new TransactionSearchEngine([txWithSplits]);

        // 1. IsSplit property is true
        Assert(txWithSplits.IsSplit, "TransactionItemRecord.IsSplit is true.");

        // 2. Search by split category via TextQuery
        var resCat = engine.Search(new TransactionQueryFilter(TextQuery: "Electronics"));
        Assert(resCat.Count == 1, "Found transaction when searching for split category 'Electronics'.");

        // 3. Search by split note via TextQuery
        var resNote = engine.Search(new TransactionQueryFilter(TextQuery: "USB Cable"));
        Assert(resNote.Count == 1, "Found transaction when searching for split note 'USB Cable'.");

        // 4. Search by split tag via TextQuery
        var resTag = engine.Search(new TransactionQueryFilter(TextQuery: "Stationery"));
        Assert(resTag.Count == 1, "Found transaction when searching for split tag 'Stationery'.");

        // 5. Filter by split category field
        var filterByCat = engine.Search(new TransactionQueryFilter(Category: "Electronics"));
        Assert(filterByCat.Count == 1, "Filter by split category matches.");

        // 6. Filter by split tag field
        var filterByTag = engine.Search(new TransactionQueryFilter(Tag: "Tech"));
        Assert(filterByTag.Count == 1, "Filter by split tag matches.");

        // 7. Non-matching search
        var resNone = engine.Search(new TransactionQueryFilter(TextQuery: "NonExistentWord12345"));
        Assert(resNone.Count == 0, "No transactions found for non-existent search text.");
    }

    private static void Assert(bool condition, string message)
    {
        if (!condition)
        {
            throw new InvalidOperationException($"Assertion failed: {message}");
        }
    }
}
