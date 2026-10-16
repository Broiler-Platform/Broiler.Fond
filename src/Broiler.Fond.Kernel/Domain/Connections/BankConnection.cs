using Broiler.Fond.Kernel.Domain.Accounts;
using Broiler.Fond.Kernel.Domain.Identifiers;
using Broiler.Fond.Kernel.Domain.Institutions;
using Broiler.Fond.Kernel.FinTs;
using Broiler.Fond.Kernel.FinTs.Transport;

namespace Broiler.Fond.Kernel.Domain.Connections;

/// <summary>
/// Immutable representation of one bank-access context and its operational state.
/// Banking credentials (PIN/TAN) are never stored on this entity or in local archives.
/// </summary>
public sealed class BankConnection : IEquatable<BankConnection>
{
    public const int MaximumUserIdLength = 35;
    public const int MaximumCustomerIdLength = 35;
    public const int MaximumSystemIdLength = 30;
    public const int MaximumConnectorVersionLength = 64;

    private BankConnection(
        ConnectionId id,
        InstitutionId institutionId,
        ManualInstitutionConfiguration institutionConfiguration,
        string userId,
        string? customerId,
        BankConnectionState state,
        string? systemId,
        FinTsCustomerSystemStatus systemStatus,
        int nextClientMessageNumber,
        bool isSynchronized,
        DateTimeOffset? lastSuccessfulContact,
        DateTimeOffset? lastAttemptedContact,
        string connectorVersion)
    {
        Id = id;
        InstitutionId = institutionId;
        InstitutionConfiguration = institutionConfiguration;
        UserId = userId;
        CustomerId = customerId;
        State = state;
        SystemId = systemId;
        SystemStatus = systemStatus;
        NextClientMessageNumber = nextClientMessageNumber;
        IsSynchronized = isSynchronized;
        LastSuccessfulContact = lastSuccessfulContact;
        LastAttemptedContact = lastAttemptedContact;
        ConnectorVersion = connectorVersion;
    }

    public ConnectionId Id { get; }
    public InstitutionId InstitutionId { get; }
    public ManualInstitutionConfiguration InstitutionConfiguration { get; }
    public string UserId { get; }
    public string? CustomerId { get; }
    public BankConnectionState State { get; }
    public string? SystemId { get; }
    public FinTsCustomerSystemStatus SystemStatus { get; }
    public int NextClientMessageNumber { get; }
    public bool IsSynchronized { get; }
    public DateTimeOffset? LastSuccessfulContact { get; }
    public DateTimeOffset? LastAttemptedContact { get; }
    public string ConnectorVersion { get; }

    /// <summary>Gets a plain-language recovery action corresponding to the current connection state.</summary>
    public string RecoveryAction => State switch
    {
        BankConnectionState.Unconfigured => "Initial synchronization required to establish customer system identification.",
        BankConnectionState.Authenticating => "Authentication in progress.",
        BankConnectionState.ChallengeRequired => "Complete strong customer authentication challenge on your registered device.",
        BankConnectionState.Connected => "Connection ready for operations.",
        BankConnectionState.ReauthenticationRequired => "Re-synchronization and credential review required.",
        BankConnectionState.TemporarilyUnavailable => "Bank system temporarily unavailable; retry dialogue later.",
        BankConnectionState.Disabled => "Connection deactivated; manual reactivation required.",
        _ => "Status under review.",
    };

    public static BankConnection Create(
        ConnectionId id,
        InstitutionId institutionId,
        ManualInstitutionConfiguration institutionConfiguration,
        string userId,
        string? customerId = null,
        string? systemId = null,
        FinTsCustomerSystemStatus systemStatus = FinTsCustomerSystemStatus.Required,
        string connectorVersion = "FinTS 3.0 PIN/TAN")
    {
        if (id.Value == 0)
        {
            throw new ArgumentException("A nonzero connection ID is required.", nameof(id));
        }

        if (institutionId.Value == 0)
        {
            throw new ArgumentException("A nonzero institution ID is required.", nameof(institutionId));
        }

        ArgumentNullException.ThrowIfNull(institutionConfiguration);

        if (string.IsNullOrWhiteSpace(userId))
        {
            throw new ArgumentException("A non-empty user ID is required.", nameof(userId));
        }

        if (userId.Length > MaximumUserIdLength || userId.Any(static c => char.IsControl(c)))
        {
            throw new ArgumentException($"User ID must not exceed {MaximumUserIdLength} characters or contain control characters.", nameof(userId));
        }

        if (customerId is not null && (customerId.Length > MaximumCustomerIdLength || customerId.Any(static c => char.IsControl(c))))
        {
            throw new ArgumentException($"Customer ID must not exceed {MaximumCustomerIdLength} characters or contain control characters.", nameof(customerId));
        }

        if (systemId is not null)
        {
            if (systemId.Length is < 1 or > MaximumSystemIdLength || systemId.Any(static c => char.IsControl(c)))
            {
                throw new ArgumentException($"System ID must not exceed {MaximumSystemIdLength} characters or contain control characters.", nameof(systemId));
            }
        }

        if (string.IsNullOrWhiteSpace(connectorVersion) || connectorVersion.Length > MaximumConnectorVersionLength)
        {
            throw new ArgumentException($"Connector version must be non-empty and not exceed {MaximumConnectorVersionLength} characters.", nameof(connectorVersion));
        }

        bool hasValidSystemId = !string.IsNullOrEmpty(systemId) && systemId != "0" && !string.Equals(systemId, "unbekannt", StringComparison.OrdinalIgnoreCase);
        var initialState = hasValidSystemId ? BankConnectionState.Connected : BankConnectionState.Unconfigured;

        return new(
            id,
            institutionId,
            institutionConfiguration,
            userId,
            customerId,
            initialState,
            systemId,
            systemStatus,
            nextClientMessageNumber: 1,
            isSynchronized: hasValidSystemId,
            lastSuccessfulContact: null,
            lastAttemptedContact: null,
            connectorVersion);
    }

    public BankConnection WithState(BankConnectionState newState, DateTimeOffset? attemptedAt = null)
    {
        return new(
            Id,
            InstitutionId,
            InstitutionConfiguration,
            UserId,
            CustomerId,
            newState,
            SystemId,
            SystemStatus,
            NextClientMessageNumber,
            IsSynchronized,
            LastSuccessfulContact,
            attemptedAt ?? LastAttemptedContact,
            ConnectorVersion);
    }

    public BankConnection WithSuccessfulContact(DateTimeOffset contactTime)
    {
        return new(
            Id,
            InstitutionId,
            InstitutionConfiguration,
            UserId,
            CustomerId,
            State,
            SystemId,
            SystemStatus,
            NextClientMessageNumber,
            IsSynchronized,
            contactTime,
            contactTime,
            ConnectorVersion);
    }

    public BankConnection WithAttemptedContact(DateTimeOffset contactTime)
    {
        return new(
            Id,
            InstitutionId,
            InstitutionConfiguration,
            UserId,
            CustomerId,
            State,
            SystemId,
            SystemStatus,
            NextClientMessageNumber,
            IsSynchronized,
            LastSuccessfulContact,
            contactTime,
            ConnectorVersion);
    }

    public BankConnection WithSynchronization(string systemId, int nextMessageNumber, DateTimeOffset contactTime)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(systemId);
        if (systemId.Length > MaximumSystemIdLength || systemId == "0" || string.Equals(systemId, "unbekannt", StringComparison.OrdinalIgnoreCase))
        {
            throw new ArgumentException("A valid non-zero system ID is required.", nameof(systemId));
        }

        if (nextMessageNumber is < 1 or > 9999)
        {
            throw new ArgumentOutOfRangeException(nameof(nextMessageNumber), "Next message number must be between 1 and 9999.");
        }

        return new(
            Id,
            InstitutionId,
            InstitutionConfiguration,
            UserId,
            CustomerId,
            BankConnectionState.Connected,
            systemId,
            SystemStatus,
            nextMessageNumber,
            isSynchronized: true,
            contactTime,
            contactTime,
            ConnectorVersion);
    }

    public BankConnection WithSystemId(string systemId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(systemId);
        if (systemId.Length > MaximumSystemIdLength || systemId == "0" || string.Equals(systemId, "unbekannt", StringComparison.OrdinalIgnoreCase))
        {
            throw new ArgumentException("A valid non-zero system ID is required.", nameof(systemId));
        }

        return new(
            Id,
            InstitutionId,
            InstitutionConfiguration,
            UserId,
            CustomerId,
            State,
            systemId,
            SystemStatus,
            NextClientMessageNumber,
            isSynchronized: true,
            LastSuccessfulContact,
            LastAttemptedContact,
            ConnectorVersion);
    }

    public BankConnection WithNextClientMessageNumber(int nextMessageNumber)
    {
        if (nextMessageNumber is < 1 or > 9999)
        {
            throw new ArgumentOutOfRangeException(nameof(nextMessageNumber), "Next message number must be between 1 and 9999.");
        }

        return new(
            Id,
            InstitutionId,
            InstitutionConfiguration,
            UserId,
            CustomerId,
            State,
            SystemId,
            SystemStatus,
            nextMessageNumber,
            IsSynchronized,
            LastSuccessfulContact,
            LastAttemptedContact,
            ConnectorVersion);
    }

    public BankConnection WithDisabled()
    {
        return new(
            Id,
            InstitutionId,
            InstitutionConfiguration,
            UserId,
            CustomerId,
            BankConnectionState.Disabled,
            SystemId,
            SystemStatus,
            NextClientMessageNumber,
            isSynchronized: false,
            LastSuccessfulContact,
            LastAttemptedContact,
            ConnectorVersion);
    }

    public BankConnection WithReactivated(BankConnectionState targetState = BankConnectionState.Unconfigured)
    {
        if (targetState == BankConnectionState.Disabled)
        {
            throw new ArgumentException("Target state cannot be Disabled when reactivating.", nameof(targetState));
        }

        bool hasValidSystemId = !string.IsNullOrEmpty(SystemId) && SystemId != "0" && !string.Equals(SystemId, "unbekannt", StringComparison.OrdinalIgnoreCase);

        return new(
            Id,
            InstitutionId,
            InstitutionConfiguration,
            UserId,
            CustomerId,
            targetState,
            SystemId,
            SystemStatus,
            NextClientMessageNumber,
            isSynchronized: hasValidSystemId,
            LastSuccessfulContact,
            LastAttemptedContact,
            ConnectorVersion);
    }

    /// <summary>
    /// Applies a verified synchronization session result to this connection using pure recovery application.
    /// </summary>
    public FinTsSynchronizationRecoveryResult ApplySynchronization(
        FinTsPinTanSynchronizationSessionResult sessionResult,
        DateTimeOffset? contactTime = null)
    {
        return FinTsSynchronizationRecoveryApplication.Apply(this, sessionResult, contactTime);
    }

    /// <summary>
    /// Applies a verified read session result to this connection and local store using pure ingestion and account rediscovery.
    /// </summary>
    public FinTsReadIngestionResult ApplyReadIngestion(
        FinTsPinTanReadSessionResult sessionResult,
        IReadOnlyList<KnownAccountBinding> knownBindings,
        LocalIdentityLedger identityLedger,
        DateTimeOffset? retrievedAt = null,
        CancellationToken cancellationToken = default)
    {
        return FinTsReadIngestionApplication.Apply(
            this,
            sessionResult,
            knownBindings,
            identityLedger,
            retrievedAt ?? DateTimeOffset.UtcNow,
            cancellationToken);
    }

    /// <summary>
    /// Executes the complete FinTS read execution pipeline over the supplied transport, session attempt state machine, and store ingestion.
    /// </summary>
    public ValueTask<FinTsReadExecutionResult> ExecuteReadPipelineAsync(
        IFinTsTransport transport,
        FinTsSessionCredential credential,
        LocalIdentityLedger identityLedger,
        IReadOnlyList<KnownAccountBinding> knownBindings,
        FinTsReadExecutionOptions options,
        CancellationToken cancellationToken = default)
    {
        return FinTsReadExecutionPipeline.ExecuteAsync(
            this,
            transport,
            credential,
            identityLedger,
            knownBindings,
            options,
            cancellationToken);
    }

    /// <summary>
    /// Executes the complete FinTS synchronization execution pipeline over the supplied transport, dialogue state machine, and recovery application.
    /// </summary>
    public ValueTask<FinTsSynchronizationExecutionResult> ExecuteSynchronizationPipelineAsync(
        IFinTsTransport transport,
        FinTsSessionCredential credential,
        FinTsSynchronizationExecutionOptions options,
        CancellationToken cancellationToken = default)
    {
        return FinTsSynchronizationExecutionPipeline.ExecuteAsync(
            this,
            transport,
            credential,
            options,
            cancellationToken);
    }

    public bool Equals(BankConnection? other)
    {
        if (other is null) { return false; }
        if (ReferenceEquals(this, other)) { return true; }

        return Id == other.Id &&
               InstitutionId == other.InstitutionId &&
               string.Equals(UserId, other.UserId, StringComparison.Ordinal) &&
               string.Equals(CustomerId, other.CustomerId, StringComparison.Ordinal) &&
               State == other.State &&
               string.Equals(SystemId, other.SystemId, StringComparison.Ordinal) &&
               SystemStatus == other.SystemStatus &&
               NextClientMessageNumber == other.NextClientMessageNumber &&
               IsSynchronized == other.IsSynchronized &&
               LastSuccessfulContact == other.LastSuccessfulContact &&
               LastAttemptedContact == other.LastAttemptedContact &&
               string.Equals(ConnectorVersion, other.ConnectorVersion, StringComparison.Ordinal);
    }

    public override bool Equals(object? obj) => Equals(obj as BankConnection);

    public override int GetHashCode()
    {
        var hash = new HashCode();
        hash.Add(Id);
        hash.Add(InstitutionId);
        hash.Add(UserId, StringComparer.Ordinal);
        hash.Add(CustomerId, StringComparer.Ordinal);
        hash.Add(State);
        hash.Add(SystemId, StringComparer.Ordinal);
        hash.Add(SystemStatus);
        hash.Add(NextClientMessageNumber);
        hash.Add(IsSynchronized);
        hash.Add(LastSuccessfulContact);
        hash.Add(LastAttemptedContact);
        hash.Add(ConnectorVersion, StringComparer.Ordinal);
        return hash.ToHashCode();
    }

    public override string ToString() => $"BankConnection({Id.Value}, State={State}, Synchronized={IsSynchronized})";
}
