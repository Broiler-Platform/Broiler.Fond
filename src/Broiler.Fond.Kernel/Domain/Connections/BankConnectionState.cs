namespace Broiler.Fond.Kernel.Domain.Connections;

/// <summary>
/// Explicit operational state of a bank connection.
/// Corresponds to the states required by the product specification.
/// </summary>
public enum BankConnectionState
{
    /// <summary>Configured with endpoint and credentials, but not yet synchronized or assigned a system ID.</summary>
    Unconfigured,

    /// <summary>Authentication or synchronization dialogue is actively in progress.</summary>
    Authenticating,

    /// <summary>Strong customer authentication challenge (e.g. decoupled TAN) is pending user action.</summary>
    ChallengeRequired,

    /// <summary>Synchronized and ready for authorized financial dialogues.</summary>
    Connected,

    /// <summary>Credentials expired, changed, or dialogue reviewed; fresh synchronization/authentication required.</summary>
    ReauthenticationRequired,

    /// <summary>Bank system reported maintenance, abort, or temporary network failure; retry later without credential change.</summary>
    TemporarilyUnavailable,

    /// <summary>Explicitly deactivated by the user or security policy; no dialogues permitted until reactivated.</summary>
    Disabled,
}
