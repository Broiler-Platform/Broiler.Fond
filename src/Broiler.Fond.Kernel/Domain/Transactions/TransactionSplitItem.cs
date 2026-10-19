using System.Collections.ObjectModel;

namespace Broiler.Fond.Kernel.Domain.Transactions;

/// <summary>
/// Represents a single itemized subdivision of a transaction's total amount into a distinct category and note.
/// Strict balance invariant: the sum of all split item amounts must equal the parent transaction amount.
/// </summary>
public sealed record TransactionSplitItem
{
    public const int MaximumCategoryLength = 128;
    public const int MaximumNoteLength = 1024;
    public const int MaximumTagLength = 64;
    public const int MaximumTagCount = 16;

    public int Index { get; init; }
    public decimal Amount { get; init; }
    public string Category { get; init; }
    public string? Note { get; init; }
    public ReadOnlyCollection<string> Tags { get; init; }

    public TransactionSplitItem(
        int index,
        decimal amount,
        string category,
        string? note = null,
        IEnumerable<string>? tags = null)
    {
        if (index < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(index), "Split index must be non-negative.");
        }

        if (amount == 0m)
        {
            throw new ArgumentException("Split item amount cannot be zero.", nameof(amount));
        }

        if (string.IsNullOrWhiteSpace(category))
        {
            throw new ArgumentException("Split item category cannot be empty.", nameof(category));
        }

        string trimmedCategory = category.Trim();
        if (trimmedCategory.Length > MaximumCategoryLength)
        {
            throw new ArgumentException($"Split category exceeds {MaximumCategoryLength} characters.", nameof(category));
        }

        string? trimmedNote = string.IsNullOrWhiteSpace(note) ? null : note.Trim();
        if (trimmedNote?.Length > MaximumNoteLength)
        {
            throw new ArgumentException($"Split note exceeds {MaximumNoteLength} characters.", nameof(note));
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

                string trimmedTag = tag.Trim();
                if (trimmedTag.Length > MaximumTagLength)
                {
                    throw new ArgumentException($"Tag '{trimmedTag}' exceeds {MaximumTagLength} characters.", nameof(tags));
                }

                if (!validatedTags.Contains(trimmedTag, StringComparer.OrdinalIgnoreCase))
                {
                    if (validatedTags.Count == MaximumTagCount)
                    {
                        throw new ArgumentException($"Split item tag count exceeds maximum {MaximumTagCount}.", nameof(tags));
                    }

                    validatedTags.Add(trimmedTag);
                }
            }
        }

        validatedTags.Sort(StringComparer.OrdinalIgnoreCase);

        Index = index;
        Amount = amount;
        Category = trimmedCategory;
        Note = trimmedNote;
        Tags = validatedTags.AsReadOnly();
    }
}
