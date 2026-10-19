namespace Broiler.Fond.Kernel.Domain.Payments;

/// <summary>
/// Immutable value object representing remittance information (Verwendungszweck) in a SEPA credit transfer.
/// </summary>
public sealed record PaymentRemittance
{
    public const int MaxUnstructuredLength = 140;

    public string? UnstructuredText { get; }
    public string? StructuredReference { get; }

    public PaymentRemittance(string? unstructuredText = null, string? structuredReference = null)
    {
        UnstructuredText = string.IsNullOrWhiteSpace(unstructuredText) ? null : unstructuredText.Trim();
        StructuredReference = string.IsNullOrWhiteSpace(structuredReference) ? null : structuredReference.Trim();
    }

    /// <summary>
    /// Gets a single display summary of the remittance details.
    /// </summary>
    public string DisplayText =>
        UnstructuredText ?? StructuredReference ?? "(No Remittance Specified)";
}
