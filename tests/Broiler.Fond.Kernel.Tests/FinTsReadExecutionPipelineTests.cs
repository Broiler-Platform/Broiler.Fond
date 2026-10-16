using System.Globalization;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Broiler.Fond.Kernel.Domain.Accounts;
using Broiler.Fond.Kernel.Domain.Connections;
using Broiler.Fond.Kernel.Domain.Identifiers;
using Broiler.Fond.Kernel.Domain.Institutions;
using Broiler.Fond.Kernel.FinTs;
using Broiler.Fond.Kernel.FinTs.Transport;
using static Broiler.Fond.Kernel.Tests.FinTsPinTanReadSessionAttemptTests;
using static Broiler.Fond.Kernel.Tests.FinTsReadSessionCandidates;

namespace Broiler.Fond.Kernel.Tests;

internal static class FinTsReadExecutionPipelineTests
{
    private const string BankCode = "PUBLIC-BANK";
    private const string TestEndpoint = "https://bank.example/fints";
    private static readonly DateTimeOffset TestTimestamp = DateTimeOffset.Parse("2026-10-15T12:00:00Z", CultureInfo.InvariantCulture);

    private static readonly Lazy<JsonDocument> SessionsDocument = new(() =>
        JsonDocument.Parse(Assembly.GetExecutingAssembly().GetManifestResourceStream("FinTs.pin-tan-read-session-attempt-v1.json")!));

    private static JsonElement GetVector(int index) =>
        SessionsDocument.Value.RootElement.GetProperty("vectors")[index];

    internal static void Run(Action<bool, string> check)
    {
        int count = 0;
        void Verify(bool condition, string message) { count++; check(condition, message); }

        VerifyPreflightRejections(Verify);
        VerifyTransportFailureHandling(Verify);
        VerifyBankAbortAndReviewOutcomes(Verify);
        VerifyAllAccountDiscoveryExecution(Verify);
        VerifyBalanceRefreshExecutionWithPagination(Verify);
        VerifyDefaultClosingCandidateExecution(Verify);
        VerifySecretSafetyAndDiagnosticHygiene(Verify);
        VerifyMultiThreadedConcurrency(Verify);

        Console.WriteLine($"FinTS read execution pipeline: {count} checks completed.");
    }

    private static void VerifyPreflightRejections(Action<bool, string> verify)
    {
        var ledger = CreateLedger(out _, out _);
        using var transport = new FinTsSimulatedTransport();
        using var pin = CreatePin();

        // 1. Disabled connection
        var disabledConn = CreateTestConnection(BankConnectionState.Disabled);
        var options = FinTsReadExecutionOptions.Discovery();
        var resDisabled = FinTsReadExecutionPipeline.ExecuteAsync(
            disabledConn, transport, pin, ledger, [], options).AsTask().GetAwaiter().GetResult();

        verify(!resDisabled.IsSuccess, "Disabled connection must not succeed.");
        verify(resDisabled.Status == FinTsReadExecutionStatus.RejectedDisabledConnection,
            "Disabled connection status must be RejectedDisabledConnection.");
        verify(resDisabled.UpdatedConnection.LastAttemptedContact is not null,
            "Disabled connection must record attempted contact.");

        // 2. Unsynchronized connection
        var unsyncConn = CreateTestConnection(BankConnectionState.Unconfigured, isSynchronized: false);
        var resUnsync = FinTsReadExecutionPipeline.ExecuteAsync(
            unsyncConn, transport, pin, ledger, [], options).AsTask().GetAwaiter().GetResult();

        verify(!resUnsync.IsSuccess, "Unsynchronized connection must not succeed.");
        verify(resUnsync.Status == FinTsReadExecutionStatus.RejectedUnsynchronizedConnection,
            "Unsynchronized connection status must be RejectedUnsynchronizedConnection.");

        // 3. Unconfirmed endpoint
        var unconfConfig = ManualInstitutionConfiguration.Create("Test Bank", TestEndpoint, bankCode: BankCode, bic: "TESTDEFFXXX");
        var unconfConn = BankConnection.Create(new ConnectionId(10), new InstitutionId(1), unconfConfig, "USER", systemId: "SYS-001");
        var resUnconf = FinTsReadExecutionPipeline.ExecuteAsync(
            unconfConn, transport, pin, ledger, [], options).AsTask().GetAwaiter().GetResult();

        verify(!resUnconf.IsSuccess, "Unconfirmed endpoint must not succeed.");
        verify(resUnconf.Status == FinTsReadExecutionStatus.RejectedInvalidEndpoint,
            "Unconfirmed endpoint status must be RejectedInvalidEndpoint.");

        // 4. Invalid / wrong credential kind
        using var tan = FinTsSessionCredential.CaptureAndClear("123456"u8.ToArray(), FinTsCredentialKind.Tan, TimeSpan.FromMinutes(5));
        var validConn = CreateTestConnection();
        var resWrongCred = FinTsReadExecutionPipeline.ExecuteAsync(
            validConn, transport, tan, ledger, [], options).AsTask().GetAwaiter().GetResult();

        verify(!resWrongCred.IsSuccess, "TAN credential cannot drive read pipeline.");
        verify(resWrongCred.Status == FinTsReadExecutionStatus.RejectedInvalidCredential,
            "Wrong credential kind must be RejectedInvalidCredential.");

        // 5. Balance refresh without target binding
        var balanceOptionsNoTarget = new FinTsReadExecutionOptions { Kind = FinTsReadExecutionKind.BalanceRefresh };
        var resNoTarget = FinTsReadExecutionPipeline.ExecuteAsync(
            validConn, transport, pin, ledger, [], balanceOptionsNoTarget).AsTask().GetAwaiter().GetResult();

        verify(!resNoTarget.IsSuccess, "Balance refresh without target binding must fail.");
        verify(resNoTarget.Status == FinTsReadExecutionStatus.RejectedMissingCandidate,
            "Missing target binding must be RejectedMissingCandidate.");

        // 6. Missing initialization candidate factory
        var resNoInitFactory = FinTsReadExecutionPipeline.ExecuteAsync(
            validConn, transport, pin, ledger, [], options).AsTask().GetAwaiter().GetResult();

        verify(!resNoInitFactory.IsSuccess, "Missing candidate factory must fail.");
        verify(resNoInitFactory.Status == FinTsReadExecutionStatus.RejectedMissingCandidate,
            "Missing candidate factory must be RejectedMissingCandidate.");
    }

    private static void VerifyTransportFailureHandling(Action<bool, string> verify)
    {
        var ledger = CreateLedger(out _, out _);
        var conn = CreateTestConnection();
        using var pin = CreatePin();
        var options = CreateDiscoveryOptions();

        // 1. Network failure
        using (var transport = new FinTsSimulatedTransport())
        {
            transport.EnqueueFailure(new FinTsTransportException(
                FinTsTransportIssue.NetworkFailure, "Simulated connection reset", new Uri(TestEndpoint)));

            var result = FinTsReadExecutionPipeline.ExecuteAsync(
                conn, transport, pin, ledger, [], options).AsTask().GetAwaiter().GetResult();

            verify(!result.IsSuccess, "Network failure must reject execution.");
            verify(result.Status == FinTsReadExecutionStatus.RejectedTransportFailure,
                "Network failure must be RejectedTransportFailure.");
            verify(result.UpdatedConnection.LastAttemptedContact is not null, "Attempted contact recorded.");
        }

        // 2. Timeout failure
        using (var transport = new FinTsSimulatedTransport())
        {
            transport.EnqueueFailure(new FinTsTransportException(
                FinTsTransportIssue.Timeout, "Simulated request timeout", new Uri(TestEndpoint)));

            var result = FinTsReadExecutionPipeline.ExecuteAsync(
                conn, transport, pin, ledger, [], options).AsTask().GetAwaiter().GetResult();

            verify(!result.IsSuccess, "Timeout failure must reject execution.");
            verify(result.Status == FinTsReadExecutionStatus.RejectedTransportFailure,
                "Timeout must be RejectedTransportFailure.");
        }

        // 3. HTTP 500 error response
        using (var transport = new FinTsSimulatedTransport())
        {
            transport.EnqueueResponse("Internal Server Error"u8.ToArray(), statusCode: 500);

            var result = FinTsReadExecutionPipeline.ExecuteAsync(
                conn, transport, pin, ledger, [], options).AsTask().GetAwaiter().GetResult();

            verify(!result.IsSuccess, "HTTP 500 must reject execution.");
            verify(result.Status == FinTsReadExecutionStatus.RejectedTransportFailure,
                "HTTP 500 must be RejectedTransportFailure.");
        }

        // 4. Cancellation token
        using (var transport = new FinTsSimulatedTransport())
        {
            using var cts = new CancellationTokenSource();
            cts.Cancel();

            var result = FinTsReadExecutionPipeline.ExecuteAsync(
                conn, transport, pin, ledger, [], options, cts.Token).AsTask().GetAwaiter().GetResult();

            verify(!result.IsSuccess, "Pre-cancelled token must cancel execution.");
            verify(result.Status == FinTsReadExecutionStatus.Cancelled, "Status must be Cancelled.");
        }
    }

    private static void VerifyBankAbortAndReviewOutcomes(Action<bool, string> verify)
    {
        var ledger = CreateLedger(out _, out _);
        var conn = CreateTestConnection();
        using var pin = CreatePin();

        // 1. Bank abort 9800 on closing (Vector 6)
        using (var transport = new FinTsSimulatedTransport())
        {
            var v6 = GetVector(6);
            var (initBinding, proc, perm, pinTan) = FinTsPinTanInitializationRequirementsTests.Inputs(v6.GetProperty("initialization"));
            var req = FinTsPinTanInitializationRequirementsEvidence.EvaluateWithReadParameters(
                initBinding, proc, perm, pinTan, FinTsReadParameterSet.Parse(pinTan.Source));
            var initCandidate = initBinding.Request;
            var allAccountCandidate = AllAccountCandidate(v6.GetProperty("allAccount"));
            var closingCandidate = ClosingCandidate(v6.GetProperty("closing"), req.Procedures.Initialization);

            transport.EnqueueResponse(Convert.FromBase64String(v6.GetProperty("initialization").GetProperty("responseBase64").GetString()!));
            transport.EnqueueResponse(Convert.FromBase64String(v6.GetProperty("allAccount").GetProperty("responseBase64").GetString()!));
            transport.EnqueueResponse(Convert.FromBase64String(v6.GetProperty("closing").GetProperty("responseBase64").GetString()!));

            var options = FinTsReadExecutionOptions.Discovery(
                initFactory: _ => initCandidate,
                discoveryFactory: attempt => Bind(allAccountCandidate, attempt),
                closingFactory: attempt => Bind(closingCandidate, attempt));

            var result = FinTsReadExecutionPipeline.ExecuteAsync(
                conn, transport, pin, ledger, [], options).AsTask().GetAwaiter().GetResult();

            verify(!result.IsSuccess, "Bank abort must reject execution.");
            verify(result.Status == FinTsReadExecutionStatus.RejectedProtocolFailure,
                "Bank abort must map to RejectedProtocolFailure.");
            verify(result.UpdatedConnection.State == BankConnectionState.TemporarilyUnavailable,
                "Bank abort must transition connection to TemporarilyUnavailable.");
        }

        // 2. Semantic review outcome on initialization response (Vector 7)
        using (var transport = new FinTsSimulatedTransport())
        {
            var v7 = GetVector(7);
            var (initBinding, _, _, _) = FinTsPinTanInitializationRequirementsTests.Inputs(v7.GetProperty("initialization"));
            var initCandidate = initBinding.Request;

            transport.EnqueueResponse(Convert.FromBase64String(v7.GetProperty("initialization").GetProperty("responseBase64").GetString()!));

            var options = FinTsReadExecutionOptions.Discovery(initFactory: _ => initCandidate);

            var result = FinTsReadExecutionPipeline.ExecuteAsync(
                conn, transport, pin, ledger, [], options).AsTask().GetAwaiter().GetResult();

            verify(!result.IsSuccess, "Review outcome must reject execution.");
            verify(result.Status == FinTsReadExecutionStatus.RejectedDialogueOutcome,
                "Review outcome must map to RejectedDialogueOutcome.");
            verify(result.UpdatedConnection.State == BankConnectionState.ReauthenticationRequired,
                "Review outcome must transition connection to ReauthenticationRequired.");
        }
    }

    private static void VerifyAllAccountDiscoveryExecution(Action<bool, string> verify)
    {
        var ledger = CreateLedger(out _, out _);
        var conn = CreateTestConnection();
        using var pin = CreatePin();
        using var transport = new FinTsSimulatedTransport();

        var v0 = GetVector(0);
        var (initBinding, proc, perm, pinTan) = FinTsPinTanInitializationRequirementsTests.Inputs(v0.GetProperty("initialization"));
        var req = FinTsPinTanInitializationRequirementsEvidence.EvaluateWithReadParameters(
            initBinding, proc, perm, pinTan, FinTsReadParameterSet.Parse(pinTan.Source));
        var initCandidate = initBinding.Request;
        var allAccountCandidate = AllAccountCandidate(v0.GetProperty("allAccount"));
        var closingCandidate = ClosingCandidate(v0.GetProperty("closing"), req.Procedures.Initialization);

        transport.EnqueueResponse(Convert.FromBase64String(v0.GetProperty("initialization").GetProperty("responseBase64").GetString()!));
        transport.EnqueueResponse(Convert.FromBase64String(v0.GetProperty("allAccount").GetProperty("responseBase64").GetString()!));
        transport.EnqueueResponse(Convert.FromBase64String(v0.GetProperty("closing").GetProperty("responseBase64").GetString()!));

        var options = FinTsReadExecutionOptions.Discovery(
            initFactory: _ => initCandidate,
            discoveryFactory: attempt => Bind(allAccountCandidate, attempt),
            closingFactory: attempt => Bind(closingCandidate, attempt));

        var result = conn.ExecuteReadPipelineAsync(
            transport, pin, ledger, [], options).AsTask().GetAwaiter().GetResult();

        verify(result.IsSuccess, "Complete discovery pipeline must succeed.");
        verify(result.Status == FinTsReadExecutionStatus.Completed, "Status must be Completed.");
        verify(result.UpdatedConnection.LastSuccessfulContact is not null, "Successful contact recorded.");
        verify(result.UpdatedConnection.State == BankConnectionState.Connected, "Connection remains Connected.");
        verify(transport.TransmittedCount == 3, "Exactly 3 messages transmitted (init, disc, close).");

        // Verify ledger store binding
        verify(result.IngestionResult is not null, "Ingestion result must be present.");
        verify(result.IngestionResult!.BoundAccounts.Count >= 1, "Accounts bound into local store.");
        verify(result.IngestionResult.BoundAccounts.Any(b => b.Locator.Iban == "DE89370400440532013000"), "IBAN bound correctly.");
        verify(result.IngestionResult.BoundAccounts.All(b => b.State == AccountBindingState.Active), "Binding state is Active.");

        var snapshot = ledger.Snapshot;
        verify(snapshot.Records.Count >= 4, "Institution, connection, account, incarnation, and binding allocated.");
    }

    private static void VerifyBalanceRefreshExecutionWithPagination(Action<bool, string> verify)
    {
        var ledger = CreateLedger(out var instId, out var connId);
        var conn = CreateTestConnection();
        using var pin = CreatePin();
        using var transport = new FinTsSimulatedTransport();

        var v3 = GetVector(3);
        var balanceReport = BalanceResponse(v3.GetProperty("balanceInitial")).Balances[0];
        var targetLocator = FinTsAccountSourceLocatorFactory.CreateLocator(conn, balanceReport.Account, balanceReport.AccountCurrency);

        // Allocate a known active binding for balance refresh target
        using (var batch = ledger.BeginBatch())
        {
            var accId = new AccountId(batch.Allocate(LocalEntityKind.Account).Value);
            var incId = new AccountIncarnationId(batch.Allocate(LocalEntityKind.AccountIncarnation, [new(new(accId.Value), LocalEntityKind.Account)]).Value);
            var bindId = new AccountBindingId(batch.Allocate(LocalEntityKind.AccountBinding, [
                new(new(incId.Value), LocalEntityKind.AccountIncarnation),
                new(new(conn.Id.Value), LocalEntityKind.Connection)]).Value);
            batch.Commit();
        }

        var targetBinding = new KnownAccountBinding(
            new AccountId(3),
            new AccountIncarnationId(4),
            new AccountBindingId(5),
            targetLocator,
            AccountBindingState.Active);
        var (initBinding, proc, perm, pinTan) = FinTsPinTanInitializationRequirementsTests.Inputs(v3.GetProperty("initialization"));
        var req = FinTsPinTanInitializationRequirementsEvidence.EvaluateWithReadParameters(
            initBinding, proc, perm, pinTan, FinTsReadParameterSet.Parse(pinTan.Source));
        var initCandidate = initBinding.Request;
        var balanceFirstCandidate = BalanceFirstReadCandidate(v3.GetProperty("balanceInitial"));
        var balanceContCandidate = BalanceContinuationCandidate(v3.GetProperty("balanceContinuations")[0]);
        var closingCandidate = ClosingCandidate(v3.GetProperty("closing"), req.Procedures.Initialization);

        transport.EnqueueResponse(Convert.FromBase64String(v3.GetProperty("initialization").GetProperty("responseBase64").GetString()!));
        transport.EnqueueResponse(Convert.FromBase64String(v3.GetProperty("balanceInitial").GetProperty("responseBase64").GetString()!));
        transport.EnqueueResponse(Convert.FromBase64String(v3.GetProperty("balanceContinuations")[0].GetProperty("responseBase64").GetString()!));
        transport.EnqueueResponse(Convert.FromBase64String(v3.GetProperty("closing").GetProperty("responseBase64").GetString()!));

        var options = FinTsReadExecutionOptions.Balance(
            targetBinding,
            initFactory: _ => initCandidate,
            balanceFactory: attempt => Bind(balanceFirstCandidate, attempt),
            continuationFactory: attempt => Bind(balanceContCandidate, attempt),
            closingFactory: attempt => Bind(closingCandidate, attempt));

        var result = conn.ExecuteReadPipelineAsync(
            transport, pin, ledger, [targetBinding], options).AsTask().GetAwaiter().GetResult();

        verify(result.IsSuccess, "Balance refresh pipeline with continuation must succeed.");
        verify(result.Status == FinTsReadExecutionStatus.Completed, "Status must be Completed.");
        verify(result.UpdatedConnection.LastSuccessfulContact is not null, "Successful contact recorded.");
        verify(transport.TransmittedCount == 4, "Exactly 4 messages transmitted (init, page1, page2, close).");

        // Verify balance snapshots ingested
        verify(result.IngestionResult is not null, "Ingestion result present.");
        verify(result.IngestionResult!.IngestedSnapshots.Count >= 1, "Balance snapshot ingested into store.");
        verify(result.IngestionResult.IngestedSnapshots[0].Value?.Currency == "EUR", "Currency is EUR.");
        verify(result.IngestionResult.IngestedSnapshots[0].Value?.Amount == 1m, "Booked amount is 1.");
        verify(result.IngestionResult.RediscoveryDecisions.Count >= 1, "Rediscovery decision recorded.");
        verify(result.IngestionResult.RediscoveryDecisions.Any(d => d.Action == AccountRediscoveryAction.ReuseBinding), "Existing binding reused.");
    }

    private static void VerifyDefaultClosingCandidateExecution(Action<bool, string> verify)
    {
        var ledger = CreateLedger(out _, out _);
        var conn = CreateTestConnection();
        using var pin = CreatePin();
        using var transport = new FinTsSimulatedTransport();

        var v0 = GetVector(0);
        var (initBinding, _, _, _) = FinTsPinTanInitializationRequirementsTests.Inputs(v0.GetProperty("initialization"));
        var initCandidate = initBinding.Request;
        var allAccountCandidate = AllAccountCandidate(v0.GetProperty("allAccount"));

        transport.EnqueueResponse(Convert.FromBase64String(v0.GetProperty("initialization").GetProperty("responseBase64").GetString()!));
        transport.EnqueueResponse(Convert.FromBase64String(v0.GetProperty("allAccount").GetProperty("responseBase64").GetString()!));
        transport.EnqueueResponse(Convert.FromBase64String(v0.GetProperty("closing").GetProperty("responseBase64").GetString()!));

        // Note: closingFactory is null, testing CreateDefaultClosingCandidate
        var options = FinTsReadExecutionOptions.Discovery(
            initFactory: _ => initCandidate,
            discoveryFactory: attempt => Bind(allAccountCandidate, attempt),
            closingFactory: null);

        var result = conn.ExecuteReadPipelineAsync(
            transport, pin, ledger, [], options).AsTask().GetAwaiter().GetResult();

        verify(result.IsSuccess, "Pipeline with default closing candidate must succeed.");
        verify(result.Status == FinTsReadExecutionStatus.Completed, "Status is Completed.");
        verify(transport.TransmittedCount == 3, "Transmitted init, disc, and generated close.");
    }

    private static void VerifySecretSafetyAndDiagnosticHygiene(Action<bool, string> verify)
    {
        var ledger = CreateLedger(out _, out _);
        var conn = CreateTestConnection();
        using var pin = FinTsSessionCredential.CaptureAndClear("SECRET-PIN-VALUE"u8.ToArray(), FinTsCredentialKind.Pin, TimeSpan.FromMinutes(5));
        using var transport = new FinTsSimulatedTransport();

        // Enqueue error response to test failure formatting
        transport.EnqueueResponse("HIRMG:2:2+9999::Error with server'"u8.ToArray(), statusCode: 500);

        var options = CreateDiscoveryOptions();
        var result = FinTsReadExecutionPipeline.ExecuteAsync(
            conn, transport, pin, ledger, [], options).AsTask().GetAwaiter().GetResult();

        verify(!result.IsSuccess, "Pipeline failed as expected.");
        string joinedIssues = string.Join(" ", result.Issues);
        verify(!joinedIssues.Contains("SECRET-PIN-VALUE", StringComparison.Ordinal),
            "PIN value must never appear in diagnostic issues.");
        verify(!joinedIssues.Contains("HNSHA", StringComparison.Ordinal),
            "Trailer segment markup must not appear in failure message.");
    }

    private static void VerifyMultiThreadedConcurrency(Action<bool, string> verify)
    {
        const int threadCount = 16;
        int completedCount = 0;
        var exceptions = new List<Exception>();

        var v0 = GetVector(0);
        var (initBinding, proc, perm, pinTan) = FinTsPinTanInitializationRequirementsTests.Inputs(v0.GetProperty("initialization"));
        var req = FinTsPinTanInitializationRequirementsEvidence.EvaluateWithReadParameters(
            initBinding, proc, perm, pinTan, FinTsReadParameterSet.Parse(pinTan.Source));
        var initCandidate = initBinding.Request;
        var allAccountCandidate = AllAccountCandidate(v0.GetProperty("allAccount"));
        var closingCandidate = ClosingCandidate(v0.GetProperty("closing"), req.Procedures.Initialization);

        byte[] initWire = Convert.FromBase64String(v0.GetProperty("initialization").GetProperty("responseBase64").GetString()!);
        byte[] discWire = Convert.FromBase64String(v0.GetProperty("allAccount").GetProperty("responseBase64").GetString()!);
        byte[] closeWire = Convert.FromBase64String(v0.GetProperty("closing").GetProperty("responseBase64").GetString()!);

        Parallel.For(0, threadCount, i =>
        {
            try
            {
                var ledger = CreateLedger(out _, out _);
                var conn = CreateTestConnection();
                using var pin = CreatePin();
                using var transport = new FinTsSimulatedTransport();

                transport.EnqueueResponse(initWire);
                transport.EnqueueResponse(discWire);
                transport.EnqueueResponse(closeWire);

                var options = FinTsReadExecutionOptions.Discovery(
                    initFactory: _ => initCandidate,
                    discoveryFactory: attempt => Bind(allAccountCandidate, attempt),
                    closingFactory: attempt => Bind(closingCandidate, attempt));

                var result = conn.ExecuteReadPipelineAsync(
                    transport, pin, ledger, [], options).AsTask().GetAwaiter().GetResult();

                if (result.IsSuccess)
                {
                    Interlocked.Increment(ref completedCount);
                }
                else
                {
                    lock (exceptions)
                    {
                        exceptions.Add(new InvalidOperationException($"Execution failed: {string.Join(", ", result.Issues)}"));
                    }
                }
            }
            catch (Exception ex)
            {
                lock (exceptions)
                {
                    exceptions.Add(ex);
                }
            }
        });

        verify(exceptions.Count == 0, "No exceptions in multi-threaded execution.");
        verify(completedCount == threadCount, $"All {threadCount} parallel pipelines completed successfully.");
    }

    private static FinTsSessionCredential CreatePin() =>
        FinTsSessionCredential.CaptureAndClear("12345"u8.ToArray(), FinTsCredentialKind.Pin, TimeSpan.FromMinutes(5));

    private static BankConnection CreateTestConnection(
        BankConnectionState state = BankConnectionState.Connected,
        bool isSynchronized = true,
        string bankCode = BankCode)
    {
        var config = ManualInstitutionConfiguration.Create(
            "Public Bank",
            TestEndpoint,
            bankCode: bankCode,
            bic: "PUBLIC-BIC")
            .ConfirmEndpoint(TestEndpoint);

        string? initialSystemId = (state == BankConnectionState.Unconfigured || !isSynchronized) ? null : "PUBLIC-SYSTEM";

        var conn = BankConnection.Create(
            new ConnectionId(2),
            new InstitutionId(1),
            config,
            "PUBLIC-USER",
            customerId: "PUBLIC-CUSTOMER",
            systemId: initialSystemId);

        if (state == BankConnectionState.Disabled)
        {
            conn = conn.WithDisabled();
        }
        else if (state != BankConnectionState.Connected && state != BankConnectionState.Unconfigured)
        {
            conn = conn.WithState(state);
        }
        else if (isSynchronized && initialSystemId is not null)
        {
            conn = conn.WithSynchronization("PUBLIC-SYSTEM", 1, TestTimestamp);
        }

        return conn;
    }

    private static LocalIdentityLedger CreateLedger(out InstitutionId instId, out ConnectionId connId)
    {
        var snapshot = IdentitySnapshot.Empty();
        IdentityValidationResult result = LocalIdentityLedger.TryOpen(snapshot, out LocalIdentityLedger? ledger);
        if (!result.IsValid || ledger is null)
        {
            throw new InvalidOperationException("Failed to open empty ledger.");
        }

        using IdentityAllocationBatch batch = ledger.BeginBatch();
        instId = new InstitutionId(batch.Allocate(LocalEntityKind.Institution).Value);
        connId = new ConnectionId(batch.Allocate(LocalEntityKind.Connection, [new(new(instId.Value), LocalEntityKind.Institution)]).Value);
        batch.Commit();
        return ledger;
    }

    private static byte[] CreateWireResponse(string dialogueId, int bankMsgNumber, int clientMsgNumber, params string[] bodySegments)
    {
        var pieces = new List<string>();
        foreach (string value in bodySegments)
        {
            int colon = value.IndexOf(':'), next = value.IndexOf(':', colon + 1);
            pieces.Add(value[..(colon + 1)] + (pieces.Count + 2).ToString(CultureInfo.InvariantCulture) + value[next..] + "'");
        }
        string payload = string.Concat(pieces) + $"HNHBS:{pieces.Count + 2}:1+{bankMsgNumber}'";
        string header = $"HNHBK:1:3+000000000000+300+{dialogueId}+{bankMsgNumber}+{dialogueId}:{clientMsgNumber}'";
        header = header.Replace("000000000000", (header.Length + payload.Length).ToString("D12", CultureInfo.InvariantCulture), StringComparison.Ordinal);
        return Encoding.Latin1.GetBytes(header + payload);
    }

    private static FinTsReadExecutionOptions CreateDiscoveryOptions()
    {
        var v0 = GetVector(0);
        var (initBinding, proc, perm, pinTan) = FinTsPinTanInitializationRequirementsTests.Inputs(v0.GetProperty("initialization"));
        var req = FinTsPinTanInitializationRequirementsEvidence.EvaluateWithReadParameters(
            initBinding, proc, perm, pinTan, FinTsReadParameterSet.Parse(pinTan.Source));
        var initCandidate = initBinding.Request;
        var allAccountCandidate = AllAccountCandidate(v0.GetProperty("allAccount"));
        var closingCandidate = ClosingCandidate(v0.GetProperty("closing"), req.Procedures.Initialization);

        return FinTsReadExecutionOptions.Discovery(
            initFactory: _ => initCandidate,
            discoveryFactory: attempt => Bind(allAccountCandidate, attempt),
            closingFactory: attempt => Bind(closingCandidate, attempt));
    }
}
