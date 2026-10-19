namespace Broiler.Fond.Kernel.Domain.Transactions.Import;

/// <summary>
/// Supported offline bank statement file formats.
/// </summary>
public enum StatementFileFormat
{
    Unknown = 0,
    Mt940 = 1,
    Mt942 = 2,
    Camt052 = 3,
    Camt053 = 4,
}
