using System.Collections.ObjectModel;
using Broiler.Fond.Kernel.Domain.Identifiers;

namespace Broiler.Fond.Kernel.Domain.Transactions;

/// <summary>
/// Defines an immutable, user-supplied annotation revision for a transaction.
/// Kept strictly separate from bank-reported facts to preserve auditability.
/// </summary>
public sealed class AnnotationRevision
{
    public const int MaximumNoteLength = 4096;
    public const int MaximumCategoryLength = 128;
    public const int MaximumTagLength = 64;
    public const int MaximumTagCount = 32;

    public AnnotationRevision(
        AnnotationRevisionId id,
        TransactionId transactionId,
        DateTimeOffset modifiedAt,
        AnnotationRevisionId? supersedesId = null,
        string? note = null,
        string? category = null,
        IEnumerable<string>? tags = null,
        bool isExcludedFromBudget = false,
        IEnumerable<TransactionSplitItem>? splits = null)
    {
        if (id.Value == 0UL || transactionId.Value == 0UL)
        {
            throw new ArgumentException("Annotation revision ID and Transaction ID must be nonzero.");
        }

        if (supersedesId.HasValue && supersedesId.Value.Value == 0UL)
        {
            throw new ArgumentException("Supersedes ID must be nonzero when specified.", nameof(supersedesId));
        }

        if (supersedesId.HasValue && supersedesId.Value == id)
        {
            throw new ArgumentException("An annotation revision cannot supersede itself.", nameof(supersedesId));
        }

        if (note?.Length > MaximumNoteLength)
        {
            throw new ArgumentException($"Note exceeds {MaximumNoteLength} characters.", nameof(note));
        }

        if (category?.Length > MaximumCategoryLength)
        {
            throw new ArgumentException($"Category exceeds {MaximumCategoryLength} characters.", nameof(category));
        }

        List<string> validatedTags = [];
        if (tags is not null)
        {
            foreach (string tag in tags)
            {
                if (string.IsNullOrWhiteSpace(tag))
                {
                    continue;
                }

                string trimmed = tag.Trim();
                if (trimmed.Length > MaximumTagLength)
                {
                    throw new ArgumentException($"Tag '{trimmed}' exceeds {MaximumTagLength} characters.", nameof(tags));
                }

                if (!validatedTags.Contains(trimmed, StringComparer.OrdinalIgnoreCase))
                {
                    if (validatedTags.Count == MaximumTagCount)
                    {
                        throw new ArgumentException($"Tag count exceeds {MaximumTagCount}.", nameof(tags));
                    }

                    validatedTags.Add(trimmed);
                }
            }
        }

        validatedTags.Sort(StringComparer.OrdinalIgnoreCase);

        List<TransactionSplitItem>? validatedSplits = null;
        if (splits is not null)
        {
            var splitList = splits.ToList();
            if (splitList.Count > 0)
            {
                if (splitList.Count < TransactionSplitValidator.MinimumSplitCount)
                {
                    throw new ArgumentException($"A split annotation requires at least {TransactionSplitValidator.MinimumSplitCount} split items.", nameof(splits));
                }

                var indices = new HashSet<int>();
                foreach (var split in splitList)
                {
                    if (!indices.Add(split.Index))
                    {
                        throw new ArgumentException($"Duplicate split item index {split.Index}.", nameof(splits));
                    }
                }

                validatedSplits = splitList.OrderBy(s => s.Index).ToList();
            }
        }

        Id = id;
        TransactionId = transactionId;
        ModifiedAt = modifiedAt;
        SupersedesId = supersedesId;
        Note = string.IsNullOrWhiteSpace(note) ? null : note.Trim();
        Category = validatedSplits != null && validatedSplits.Count > 0 && string.IsNullOrWhiteSpace(category)
            ? "[Split]"
            : (string.IsNullOrWhiteSpace(category) ? null : category.Trim());
        Tags = validatedTags.AsReadOnly();
        IsExcludedFromBudget = isExcludedFromBudget;
        Splits = validatedSplits?.AsReadOnly();
    }

    public AnnotationRevisionId Id { get; }
    public TransactionId TransactionId { get; }
    public AnnotationRevisionId? SupersedesId { get; }
    public DateTimeOffset ModifiedAt { get; }
    public string? Note { get; }
    public string? Category { get; }
    public ReadOnlyCollection<string> Tags { get; }
    public bool IsExcludedFromBudget { get; }
    public ReadOnlyCollection<TransactionSplitItem>? Splits { get; }
    public bool IsSplit => Splits is not null && Splits.Count > 1;
}
