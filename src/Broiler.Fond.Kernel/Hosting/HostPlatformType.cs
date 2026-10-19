namespace Broiler.Fond.Kernel.Hosting;

/// <summary>
/// Broad categorization of host execution environments.
/// Invariant: Pure metadata enum; kernel logic never branches on host platform.
/// </summary>
public enum HostPlatformType
{
    Unknown = 0,
    Windows = 1,
    Linux = 2,
    MacOS = 3,
    Android = 4,
    IOS = 5,
    Other = 99
}
