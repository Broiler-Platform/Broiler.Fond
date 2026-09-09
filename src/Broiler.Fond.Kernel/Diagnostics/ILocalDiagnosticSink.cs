namespace Broiler.Fond.Kernel.Diagnostics;

/// <summary>
/// On-device structured diagnostic seam. It accepts no free text, exception,
/// credential, account identifier or financial payload and has no upload path.
/// </summary>
internal interface ILocalDiagnosticSink
{
    /// <summary>Gets whether explicitly enabled local diagnostics are active.</summary>
    bool IsEnabled { get; }

    void Record(LocalDiagnosticCode code, LocalDiagnosticOutcome outcome, DateTimeOffset timestamp);
}
