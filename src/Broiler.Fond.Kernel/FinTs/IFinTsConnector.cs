namespace Broiler.Fond.Kernel.FinTs;

/// <summary>
/// Reserves the internal FinTS seam. Protocol operations are added as reviewed
/// vertical slices; Milestone 0 provides no implementation.
/// </summary>
internal interface IFinTsConnector
{
    /// <summary>Gets the protocol family represented by a future connector.</summary>
    string ProtocolFamily { get; }
}
