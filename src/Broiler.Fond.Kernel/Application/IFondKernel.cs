namespace Broiler.Fond.Kernel.Application;

/// <summary>
/// Describes the stable host-facing kernel boundary. Banking operations are
/// intentionally absent until their milestone contracts are reviewed.
/// </summary>
public interface IFondKernel
{
    /// <summary>Gets immutable metadata about the composed kernel.</summary>
    KernelDescriptor Descriptor { get; }
}

/// <summary>Describes a composed kernel without exposing implementation state.</summary>
public sealed record KernelDescriptor(
    string FullDisplayName,
    int Milestone,
    KernelCapability Capabilities);

/// <summary>Lists capability groups without claiming they are implemented.</summary>
[Flags]
public enum KernelCapability
{
    /// <summary>No banking capability is present.</summary>
    None = 0,

    /// <summary>Account-access capability, planned for M1.</summary>
    AccountAccess = 1 << 0,

    /// <summary>Account-list and balance capability, planned for M1.</summary>
    AccountValues = 1 << 1,
}
