using System.Collections.ObjectModel;
using Broiler.Fond.Kernel.Domain.Identifiers;
using Broiler.Fond.Kernel.Storage;

namespace Broiler.Fond.Kernel.Domain.Transactions.Import;

/// <summary>
/// Result of an offline statement file import or preview.
/// </summary>
public sealed class StatementImportResult
{
    public bool IsSuccess { get; }
    public StatementFileFormat DetectedFormat { get; }
    public AccountId? AccountId { get; }
    public AccountBindingId? AccountBindingId { get; }
    public int TransactionsImportedCount { get; }
    public int TransactionsMatchedCount { get; }
    public int LinksCreatedCount { get; }
    public ReadOnlyCollection<string> Issues { get; }
    public StoreSnapshot? ResultingSnapshot { get; }

    internal StatementImportResult(
        bool isSuccess,
        StatementFileFormat detectedFormat,
        AccountId? accountId,
        AccountBindingId? accountBindingId,
        int transactionsImportedCount,
        int transactionsMatchedCount,
        int linksCreatedCount,
        IList<string>? issues,
        StoreSnapshot? resultingSnapshot)
    {
        IsSuccess = isSuccess;
        DetectedFormat = detectedFormat;
        AccountId = accountId;
        AccountBindingId = accountBindingId;
        TransactionsImportedCount = transactionsImportedCount;
        TransactionsMatchedCount = transactionsMatchedCount;
        LinksCreatedCount = linksCreatedCount;
        Issues = new ReadOnlyCollection<string>(issues ?? []);
        ResultingSnapshot = resultingSnapshot;
    }

    public static StatementImportResult Success(
        StatementFileFormat detectedFormat,
        AccountId accountId,
        AccountBindingId accountBindingId,
        int transactionsImportedCount,
        int transactionsMatchedCount,
        int linksCreatedCount,
        IList<string>? issues = null,
        StoreSnapshot? resultingSnapshot = null) =>
        new(
            isSuccess: true,
            detectedFormat: detectedFormat,
            accountId: accountId,
            accountBindingId: accountBindingId,
            transactionsImportedCount: transactionsImportedCount,
            transactionsMatchedCount: transactionsMatchedCount,
            linksCreatedCount: linksCreatedCount,
            issues: issues,
            resultingSnapshot: resultingSnapshot);

    public static StatementImportResult Failure(
        string issue,
        StatementFileFormat detectedFormat = StatementFileFormat.Unknown,
        IList<string>? additionalIssues = null)
    {
        List<string> issues = [issue];
        if (additionalIssues is not null)
        {
            issues.AddRange(additionalIssues);
        }

        return new(
            isSuccess: false,
            detectedFormat: detectedFormat,
            accountId: null,
            accountBindingId: null,
            transactionsImportedCount: 0,
            transactionsMatchedCount: 0,
            linksCreatedCount: 0,
            issues: issues,
            resultingSnapshot: null);
    }
}
