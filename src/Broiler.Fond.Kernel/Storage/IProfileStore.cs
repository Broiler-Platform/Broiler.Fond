namespace Broiler.Fond.Kernel.Storage;

/// <summary>
/// Reserves the internal portable-profile seam. Milestone 0 performs no file,
/// compression, XML, key-derivation, or cryptographic operation.
/// </summary>
internal interface IProfileStore
{
    /// <summary>Gets the container version represented by a future store.</summary>
    int ContainerVersion { get; }
}
