using Broiler.Fond.Kernel.Domain.Identifiers;

namespace Broiler.Fond.Kernel.Domain.Transactions.Import;

/// <summary>
/// Caller-supplied options for offline statement file import.
/// </summary>
public sealed class StatementImportOptions
{
    /// <summary>
    /// Explicit target account identifier. When set, restricts matching or overrides auto-detection.
    /// </summary>
    public AccountId? TargetAccountId { get; init; }

    /// <summary>
    /// Explicit target account binding identifier. When set, directly binds the import to this specific connection path.
    /// </summary>
    public AccountBindingId? TargetAccountBindingId { get; init; }

    /// <summary>
    /// Optional user-visible file name or label for provenance and diagnostics.
    /// </summary>
    public string? FileName { get; init; }

    /// <summary>
    /// Import timestamp; defaults to DateTimeOffset.UtcNow if null.
    /// </summary>
    public DateTimeOffset? ImportTimestamp { get; init; }

    /// <summary>
    /// When true, parses and correlates the file and calculates counts without committing changes to the profile store.
    /// </summary>
    public bool PreviewOnly { get; init; }
}
