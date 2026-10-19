using System.Text;
using Broiler.Fond.Kernel.Domain.Accounts;
using Broiler.Fond.Kernel.Domain.Identifiers;
using Broiler.Fond.Kernel.Domain.Institutions;
using Broiler.Fond.Kernel.FinTs;
using Broiler.Fond.Kernel.FinTs.Transport;
using Broiler.Fond.Kernel.Product;
using Broiler.Fond.Kernel.Storage;

namespace Broiler.Fond.Kernel.Domain.Connections.Orchestration;

/// <summary>
/// Status of an individual bank connection synchronization run.
/// </summary>
public enum ConnectionSyncStatus
{
    /// <summary>Synchronization completed successfully and data was committed to store.</summary>
    Success,

    /// <summary>Connection was skipped because it is deactivated or disabled.</summary>
    SkippedDisabled,

    /// <summary>Connection was skipped because an active backoff window is in effect.</summary>
    SkippedBackoffActive,

    /// <summary>Execution stopped because human review or credentials (PIN/SCA) are required; unattended dialogs prohibited.</summary>
    UserActionRequired,

    /// <summary>A transient transport, network, or bank maintenance condition occurred; backoff recorded.</summary>
    TransientFailure,

    /// <summary>The bank rejected credentials or required re-synchronization.</summary>
    ReauthenticationRequired,

    /// <summary>A protocol framing error, parse failure, or unrecoverable error occurred.</summary>
    ProtocolFailure,

    /// <summary>The operation was cancelled by the user or deadline.</summary>
    Cancelled,
}

/// <summary>
/// Immutable result of an individual connection synchronization run.
/// </summary>
public sealed record ConnectionSyncResult(
    ConnectionId ConnectionId,
    InstitutionId InstitutionId,
    ConnectionSyncStatus Status,
    string Message,
    TimeSpan? RemainingBackoff,
    DateTimeOffset CompletedAt,
    int DiscoveredAccountsCount,
    int UpdatedBalancesCount,
    int UpdatedTransactionsCount)
{
    public bool IsSuccess => Status == ConnectionSyncStatus.Success;

    public static ConnectionSyncResult Success(
        ConnectionId connId,
        InstitutionId instId,
        DateTimeOffset now,
        int discoveredAccounts,
        int updatedBalances,
        int updatedTransactions,
        string message = "Synchronization completed successfully.") =>
        new(connId, instId, ConnectionSyncStatus.Success, message, null, now, discoveredAccounts, updatedBalances, updatedTransactions);

    public static ConnectionSyncResult SkippedDisabled(
        ConnectionId connId,
        InstitutionId instId,
        DateTimeOffset now) =>
        new(connId, instId, ConnectionSyncStatus.SkippedDisabled, "Connection is deactivated.", null, now, 0, 0, 0);

    public static ConnectionSyncResult SkippedBackoffActive(
        ConnectionId connId,
        InstitutionId instId,
        TimeSpan remaining,
        DateTimeOffset now) =>
        new(connId, instId, ConnectionSyncStatus.SkippedBackoffActive, $"Connection is in backoff window ({remaining.TotalSeconds:F0}s remaining).", remaining, now, 0, 0, 0);

    public static ConnectionSyncResult UserActionRequired(
        ConnectionId connId,
        InstitutionId instId,
        string message,
        DateTimeOffset now) =>
        new(connId, instId, ConnectionSyncStatus.UserActionRequired, message, null, now, 0, 0, 0);

    public static ConnectionSyncResult TransientFailure(
        ConnectionId connId,
        InstitutionId instId,
        string message,
        TimeSpan remainingBackoff,
        DateTimeOffset now) =>
        new(connId, instId, ConnectionSyncStatus.TransientFailure, message, remainingBackoff, now, 0, 0, 0);

    public static ConnectionSyncResult ReauthenticationRequired(
        ConnectionId connId,
        InstitutionId instId,
        string message,
        DateTimeOffset now) =>
        new(connId, instId, ConnectionSyncStatus.ReauthenticationRequired, message, null, now, 0, 0, 0);

    public static ConnectionSyncResult ProtocolFailure(
        ConnectionId connId,
        InstitutionId instId,
        string message,
        DateTimeOffset now) =>
        new(connId, instId, ConnectionSyncStatus.ProtocolFailure, message, null, now, 0, 0, 0);

    public static ConnectionSyncResult Cancelled(
        ConnectionId connId,
        InstitutionId instId,
        DateTimeOffset now) =>
        new(connId, instId, ConnectionSyncStatus.Cancelled, "Operation cancelled.", null, now, 0, 0, 0);
}

/// <summary>
/// Aggregated batch result across all synchronized institutions and connections.
/// </summary>
public sealed record MultibankSyncBatchResult(
    IReadOnlyList<ConnectionSyncResult> Results,
    DateTimeOffset StartedAt,
    DateTimeOffset CompletedAt)
{
    public TimeSpan Duration => CompletedAt - StartedAt;
    public int TotalConnections => Results.Count;
    public int SuccessCount => Results.Count(static r => r.Status == ConnectionSyncStatus.Success);
    public int FailureCount => Results.Count(static r => r.Status is ConnectionSyncStatus.TransientFailure or ConnectionSyncStatus.ProtocolFailure);
    public int UserActionCount => Results.Count(static r => r.Status is ConnectionSyncStatus.UserActionRequired or ConnectionSyncStatus.ReauthenticationRequired);
    public int SkippedCount => Results.Count(static r => r.Status is ConnectionSyncStatus.SkippedDisabled or ConnectionSyncStatus.SkippedBackoffActive);

    public bool HasFailures => FailureCount > 0;
    public bool HasUserActionRequired => UserActionCount > 0;
}

/// <summary>
/// Configuration options controlling multibank execution and foreground scheduling.
/// </summary>
public sealed class MultibankSyncOptions
{
    public bool IsForegroundScheduled { get; init; }
    public bool AllowScheduledRefresh { get; init; } = true;
    public bool SkipBackoffConnections { get; init; } = true;
    public TimeSpan TimeoutPerConnection { get; init; } = TimeSpan.FromSeconds(60);
    public FinTsProductRegistration ProductRegistration { get; init; } = KernelProduct.FinTsRegistration;
    public Func<BankConnection, IFinTsTransport>? TransportFactory { get; init; }
    public Func<FinTsTanChallenge, FinTsTanProcedure?, CancellationToken, ValueTask<FinTsSessionCredential?>>? ScaChallengeHandler { get; init; }
    public TimeProvider? Clock { get; init; }
    public Func<BankConnection, FinTsPinTanRequestBinding>? InitializationCandidateFactory { get; init; }
    public Func<FinTsPinTanReadSessionAttempt, FinTsPinTanAllAccountRequestBinding>? AllAccountCandidateFactory { get; init; }
    public Func<FinTsPinTanReadSessionAttempt, FinTsPinTanDialogueEndRequestBinding>? ClosingCandidateFactory { get; init; }
}

/// <summary>
/// Multibank connection orchestrator executing per-institution concurrency,
/// transient failure backoff, permission-aware foreground refresh, and independent atomic local store commits.
/// </summary>
public sealed class MultibankSyncCoordinator
{
    private readonly IProfileStore? _profileStore;
    private readonly InstitutionConcurrencyCoordinator _concurrencyCoordinator;
    private readonly ConnectionBackoffTracker _backoffTracker;
    private readonly ConnectionRetryPolicy _retryPolicy;
    private readonly ISessionCredentialProvider _credentialProvider;

    public MultibankSyncCoordinator(
        IProfileStore? profileStore,
        InstitutionConcurrencyCoordinator? concurrencyCoordinator = null,
        ConnectionBackoffTracker? backoffTracker = null,
        ConnectionRetryPolicy? retryPolicy = null,
        ISessionCredentialProvider? credentialProvider = null)
    {
        _profileStore = profileStore;
        _concurrencyCoordinator = concurrencyCoordinator ?? new InstitutionConcurrencyCoordinator();
        _backoffTracker = backoffTracker ?? new ConnectionBackoffTracker();
        _retryPolicy = retryPolicy ?? ConnectionRetryPolicy.Default;
        _credentialProvider = credentialProvider ?? new EphemeralSessionCredentialStore();
    }

    public InstitutionConcurrencyCoordinator ConcurrencyCoordinator => _concurrencyCoordinator;
    public ConnectionBackoffTracker BackoffTracker => _backoffTracker;
    public ConnectionRetryPolicy RetryPolicy => _retryPolicy;
    public ISessionCredentialProvider CredentialProvider => _credentialProvider;

    /// <summary>
    /// Synchronizes multiple connections in parallel across distinct institutions,
    /// respecting per-institution serialization, backoff windows, and atomic commits.
    /// </summary>
    public async Task<MultibankSyncBatchResult> SynchronizeAllAsync(
        IReadOnlyList<BankConnection> connections,
        MultibankSyncOptions options,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(connections);
        ArgumentNullException.ThrowIfNull(options);

        var clock = options.Clock ?? TimeProvider.System;
        DateTimeOffset startedAt = clock.GetUtcNow();

        if (options.IsForegroundScheduled && !options.AllowScheduledRefresh)
        {
            return new MultibankSyncBatchResult(Array.Empty<ConnectionSyncResult>(), startedAt, startedAt);
        }

        var tasks = connections
            .Select(c => SynchronizeConnectionAsync(c, options, cancellationToken))
            .ToList();

        ConnectionSyncResult[] results = await Task.WhenAll(tasks).ConfigureAwait(false);
        DateTimeOffset completedAt = clock.GetUtcNow();

        return new MultibankSyncBatchResult(results, startedAt, completedAt);
    }

    /// <summary>
    /// Synchronizes an individual connection inside its institution and connection concurrency scope.
    /// </summary>
    public async Task<ConnectionSyncResult> SynchronizeConnectionAsync(
        BankConnection connection,
        MultibankSyncOptions options,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(connection);
        ArgumentNullException.ThrowIfNull(options);

        var clock = options.Clock ?? TimeProvider.System;
        DateTimeOffset now = clock.GetUtcNow();

        // 1. Connection state check
        if (connection.State == BankConnectionState.Disabled)
        {
            return ConnectionSyncResult.SkippedDisabled(connection.Id, connection.InstitutionId, now);
        }

        // 2. Backoff check
        if (options.SkipBackoffConnections && _backoffTracker.IsBackoffActive(connection.Id, now, out var remaining))
        {
            return ConnectionSyncResult.SkippedBackoffActive(connection.Id, connection.InstitutionId, remaining, now);
        }

        // 3. Ephemeral credential check
        if (!_credentialProvider.TryGetPin(connection.Id, out var pin) || string.IsNullOrWhiteSpace(pin))
        {
            return ConnectionSyncResult.UserActionRequired(
                connection.Id,
                connection.InstitutionId,
                "Banking PIN required for session refresh; unattended dialogs prohibited.",
                now);
        }

        // 4. Scoped execution under per-institution concurrency lock
        return await _concurrencyCoordinator.ExecuteScopedAsync(
            connection.InstitutionId,
            connection.Id,
            async ct =>
            {
                // Recheck backoff within lock
                DateTimeOffset execTime = clock.GetUtcNow();
                if (options.SkipBackoffConnections && _backoffTracker.IsBackoffActive(connection.Id, execTime, out var rem))
                {
                    return ConnectionSyncResult.SkippedBackoffActive(connection.Id, connection.InstitutionId, rem, execTime);
                }

                using var credential = FinTsSessionCredential.CaptureAndClear(
                    Encoding.UTF8.GetBytes(pin),
                    FinTsCredentialKind.Pin,
                    TimeSpan.FromMinutes(15),
                    clock);

                var transport = options.TransportFactory?.Invoke(connection)
                    ?? new FinTsHttpsTransport(timeout: options.TimeoutPerConnection);

                // Configure SCA handling: unattended scheduled refresh aborts cleanly without prompts
                Func<FinTsTanChallenge, FinTsTanProcedure?, CancellationToken, ValueTask<FinTsSessionCredential?>>? scaHandler;
                if (options.IsForegroundScheduled)
                {
                    scaHandler = static (_, _, _) => ValueTask.FromResult<FinTsSessionCredential?>(null);
                }
                else
                {
                    scaHandler = options.ScaChallengeHandler;
                }

                var readOptions = new FinTsReadExecutionOptions
                {
                    Kind = FinTsReadExecutionKind.AllAccountDiscovery,
                    Timeout = options.TimeoutPerConnection,
                    ProductRegistration = options.ProductRegistration,
                    ScaChallengeHandler = scaHandler,
                    Clock = clock,
                    InitializationCandidateFactory = options.InitializationCandidateFactory,
                    AllAccountCandidateFactory = options.AllAccountCandidateFactory,
                    ClosingCandidateFactory = options.ClosingCandidateFactory,
                };

                LocalIdentityLedger ledger = _profileStore?.IdentityLedger ?? GetFallbackLedger();
                IReadOnlyList<KnownAccountBinding> knownBindings = ExtractKnownBindings(_profileStore?.CurrentSnapshot, connection.Id);

                FinTsReadExecutionResult readResult;
                try
                {
                    readResult = await FinTsReadExecutionPipeline.ExecuteAsync(
                        connection,
                        transport,
                        credential,
                        ledger,
                        knownBindings,
                        readOptions,
                        ct).ConfigureAwait(false);
                }
                catch (OperationCanceledException) when (ct.IsCancellationRequested)
                {
                    return ConnectionSyncResult.Cancelled(connection.Id, connection.InstitutionId, clock.GetUtcNow());
                }
                catch (Exception ex)
                {
                    DateTimeOffset errTime = clock.GetUtcNow();
                    if (ConnectionRetryPolicy.IsTransientException(ex))
                    {
                        var state = _backoffTracker.RecordTransientFailure(connection.Id, ex.Message, errTime);
                        CommitConnectionState(connection.Id, BankConnectionState.TemporarilyUnavailable, errTime);
                        return ConnectionSyncResult.TransientFailure(connection.Id, connection.InstitutionId, ex.Message, state.RemainingBackoff(errTime), errTime);
                    }

                    return ConnectionSyncResult.ProtocolFailure(connection.Id, connection.InstitutionId, ex.Message, errTime);
                }

                DateTimeOffset finishTime = clock.GetUtcNow();

                // 5. Evaluate pipeline outcome
                if (readResult.IsSuccess)
                {
                    _backoffTracker.Reset(connection.Id);

                    int accountsCount = readResult.IngestionResult?.BoundAccounts.Count ?? 0;
                    int balancesCount = readResult.IngestionResult?.IngestedSnapshots.Count ?? 0;

                    // Atomic commit to store
                    if (_profileStore is not null && !_profileStore.IsReadOnly)
                    {
                        _profileStore.MutateAndSave(
                            snapshot => MultibankSnapshotMutator.ApplyIngestionResult(snapshot, readResult.UpdatedConnection, readResult.IngestionResult, finishTime),
                            finishTime);
                    }

                    return ConnectionSyncResult.Success(connection.Id, connection.InstitutionId, finishTime, accountsCount, balancesCount, 0);
                }

                string issueMessage = readResult.Issues.Count > 0 ? readResult.Issues[0] : readResult.Status.ToString();

                // 6. Handle specific failure statuses
                if (readResult.Status == FinTsReadExecutionStatus.RejectedTransportFailure)
                {
                    var backoff = _backoffTracker.RecordTransientFailure(connection.Id, issueMessage, finishTime);
                    CommitConnectionState(connection.Id, BankConnectionState.TemporarilyUnavailable, finishTime);
                    return ConnectionSyncResult.TransientFailure(connection.Id, connection.InstitutionId, issueMessage, backoff.RemainingBackoff(finishTime), finishTime);
                }

                if (readResult.Status == FinTsReadExecutionStatus.RejectedDialogueOutcome)
                {
                    // Check if credentials rejected
                    if (readResult.Issues.Any(ConnectionRetryPolicy.IsAuthenticationFailureCode))
                    {
                        if (_credentialProvider is EphemeralSessionCredentialStore mutableStore)
                        {
                            mutableStore.RemovePin(connection.Id);
                        }

                        CommitConnectionState(connection.Id, BankConnectionState.ReauthenticationRequired, finishTime);
                        return ConnectionSyncResult.ReauthenticationRequired(connection.Id, connection.InstitutionId, issueMessage, finishTime);
                    }

                    // SCA challenge required / cancelled
                    CommitConnectionState(connection.Id, BankConnectionState.ChallengeRequired, finishTime);
                    return ConnectionSyncResult.UserActionRequired(connection.Id, connection.InstitutionId, "Strong customer authentication challenge requires user authorization.", finishTime);
                }

                if (readResult.Status == FinTsReadExecutionStatus.Cancelled)
                {
                    return ConnectionSyncResult.Cancelled(connection.Id, connection.InstitutionId, finishTime);
                }

                // General protocol or ingestion failure
                return ConnectionSyncResult.ProtocolFailure(connection.Id, connection.InstitutionId, issueMessage, finishTime);
            },
            cancellationToken).ConfigureAwait(false);
    }

    private void CommitConnectionState(ConnectionId connectionId, BankConnectionState state, DateTimeOffset timestamp)
    {
        if (_profileStore is not null && !_profileStore.IsReadOnly)
        {
            try
            {
                _profileStore.MutateAndSave(
                    snapshot => MultibankSnapshotMutator.ApplyConnectionState(snapshot, connectionId, state, timestamp),
                    timestamp);
            }
            catch
            {
                // Store commit errors must not suppress connection failure reporting
            }
        }
    }

    private static LocalIdentityLedger GetFallbackLedger()
    {
        _ = LocalIdentityLedger.TryOpen(IdentitySnapshot.Empty(), out var ledger);
        return ledger!;
    }

    private static IReadOnlyList<KnownAccountBinding> ExtractKnownBindings(StoreSnapshot? snapshot, ConnectionId connectionId)
    {
        if (snapshot is null)
        {
            return Array.Empty<KnownAccountBinding>();
        }

        var result = new List<KnownAccountBinding>();
        var connBindings = snapshot.Accounts.Bindings.Where(b => b.ConnectionId == connectionId.Value);

        foreach (var b in connBindings)
        {
            var inst = snapshot.Institutions.Institutions.FirstOrDefault(i => i.Id == b.InstitutionId);
            string endpointStr = b.Endpoint;
            if (string.IsNullOrWhiteSpace(endpointStr) && inst is not null)
            {
                endpointStr = inst.Endpoint;
            }

            var config = ManualInstitutionConfiguration.Create(
                inst?.DisplayName ?? "Bank",
                endpointStr,
                b.DomesticBankCode,
                inst?.Bic);

            var bankIds = b.BankIdentifiers
                .Select(bi => new BankAccountIdentifier(bi.Issuer, bi.Scope, bi.Kind, bi.Value))
                .ToList();

            var locator = new AccountSourceLocator(
                new InstitutionId(b.InstitutionId),
                connectionId,
                b.Connector,
                b.ConnectorVersion,
                config,
                b.Iban,
                b.DomesticAccountNumber,
                b.DomesticBankCode,
                b.Subaccount,
                b.Currency,
                bankIds);

            var state = Enum.TryParse<AccountBindingState>(b.State, out var parsedState) ? parsedState : AccountBindingState.Active;

            result.Add(new KnownAccountBinding(
                new AccountId(b.AccountId),
                new AccountIncarnationId(b.IncarnationId),
                new AccountBindingId(b.Id),
                locator,
                state));
        }

        return result.AsReadOnly();
    }
}
