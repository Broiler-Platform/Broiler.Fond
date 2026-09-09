namespace Broiler.Fond.Kernel.Domain.Institutions;

/// <summary>User-review state of a manually entered FinTS endpoint.</summary>
public enum EndpointVerificationState
{
    Unconfirmed,
    Confirmed,
    Quarantined,
}
