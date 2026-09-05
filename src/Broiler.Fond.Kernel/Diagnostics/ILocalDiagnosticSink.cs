namespace Broiler.Fond.Kernel.Diagnostics;

/// <summary>
/// Reserves an on-device diagnostic seam. It is not a telemetry or upload seam,
/// and Milestone 0 provides no implementation.
/// </summary>
internal interface ILocalDiagnosticSink
{
    /// <summary>Gets whether explicitly enabled local diagnostics are active.</summary>
    bool IsEnabled { get; }
}
