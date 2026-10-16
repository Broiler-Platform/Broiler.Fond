using System.Globalization;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Broiler.Fond.Kernel.Domain.Connections;
using Broiler.Fond.Kernel.Domain.Identifiers;
using Broiler.Fond.Kernel.Domain.Institutions;
using Broiler.Fond.Kernel.FinTs;
using Broiler.Fond.Kernel.FinTs.Transport;

namespace Broiler.Fond.Kernel.Tests;

internal static class FinTsSynchronizationExecutionPipelineTests
{
    private const string BankCode = "PUBLIC-BANK";
    private const string TestEndpoint = "https://bank.example/fints";
    private static readonly DateTimeOffset TestTimestamp = DateTimeOffset.Parse("2026-10-15T12:00:00Z", CultureInfo.InvariantCulture);

    private static readonly Lazy<JsonDocument> SessionsDocument = new(() =>
        JsonDocument.Parse(Assembly.GetExecutingAssembly().GetManifestResourceStream("FinTs.pin-tan-synchronization-session-attempt-v1.json")!));

    private static JsonElement GetVectorNamed(string name)
    {
        foreach (var vector in SessionsDocument.Value.RootElement.GetProperty("vectors").EnumerateArray())
        {
            if (vector.GetProperty("name").GetString() == name)
            {
                return vector;
            }
        }
        throw new KeyNotFoundException($"Vector '{name}' not found.");
    }

    internal static void Run(Action<bool, string> check)
    {
        int count = 0;
        void Verify(bool condition, string message) { count++; check(condition, message); }

        VerifyPreflightRejections(Verify);
        VerifyTransportFailureHandling(Verify);
        VerifyBankAbortAndReviewOutcomes(Verify);
        VerifyCleanAssignmentExecution(Verify);
        VerifyCleanMessageRecoveryExecution(Verify);
        VerifyDefaultClosingCandidateExecution(Verify);
        VerifySecretSafetyAndDiagnosticHygiene(Verify);
        VerifyMultiThreadedConcurrency(Verify);

        Console.WriteLine($"FinTS synchronization execution pipeline: {count} checks completed.");
    }

    private static void VerifyPreflightRejections(Action<bool, string> verify)
    {
        using var transport = new FinTsSimulatedTransport();
        using var pin = CreatePin();

        // 1. Disabled connection
        var disabledConn = CreateTestConnection(BankConnectionState.Disabled);
        var options = FinTsSynchronizationExecutionOptions.SystemId();
        var resDisabled = FinTsSynchronizationExecutionPipeline.ExecuteAsync(
            disabledConn, transport, pin, options).AsTask().GetAwaiter().GetResult();

        verify(!resDisabled.IsSuccess, "Disabled connection must not succeed.");
        verify(resDisabled.Status == FinTsSynchronizationExecutionStatus.RejectedDisabledConnection,
            "Disabled connection status must be RejectedDisabledConnection.");
        verify(resDisabled.UpdatedConnection.LastAttemptedContact is not null,
            "Disabled connection must record attempted contact.");

        // 2. Unconfirmed endpoint
        var unconfConfig = ManualInstitutionConfiguration.Create("Test Bank", TestEndpoint, bankCode: BankCode, bic: "TESTDEFFXXX");
        var unconfConn = BankConnection.Create(new ConnectionId(10), new InstitutionId(1), unconfConfig, "PUBLIC-USER");
        var resUnconf = FinTsSynchronizationExecutionPipeline.ExecuteAsync(
            unconfConn, transport, pin, options).AsTask().GetAwaiter().GetResult();

        verify(!resUnconf.IsSuccess, "Unconfirmed endpoint must not succeed.");
        verify(resUnconf.Status == FinTsSynchronizationExecutionStatus.RejectedInvalidEndpoint,
            "Unconfirmed endpoint status must be RejectedInvalidEndpoint.");

        // 3. Invalid / wrong credential kind (TAN instead of PIN)
        using var tan = FinTsSessionCredential.CaptureAndClear("123456"u8.ToArray(), FinTsCredentialKind.Tan, TimeSpan.FromMinutes(5));
        var validConn = CreateTestConnection();
        var resWrongCred = FinTsSynchronizationExecutionPipeline.ExecuteAsync(
            validConn, transport, tan, options).AsTask().GetAwaiter().GetResult();

        verify(!resWrongCred.IsSuccess, "TAN credential cannot drive synchronization pipeline.");
        verify(resWrongCred.Status == FinTsSynchronizationExecutionStatus.RejectedInvalidCredential,
            "Wrong credential kind must be RejectedInvalidCredential.");

        // 4. Missing candidate factory
        var resNoFactory = FinTsSynchronizationExecutionPipeline.ExecuteAsync(
            validConn, transport, pin, options).AsTask().GetAwaiter().GetResult();

        verify(!resNoFactory.IsSuccess, "Missing candidate factory must fail.");
        verify(resNoFactory.Status == FinTsSynchronizationExecutionStatus.RejectedMissingCandidate,
            "Missing candidate factory must be RejectedMissingCandidate.");
    }

    private static void VerifyTransportFailureHandling(Action<bool, string> verify)
    {
        var conn = CreateTestConnection();
        using var pin = CreatePin();
        var cleanVector = GetVectorNamed("clean-assignment-session");
        var syncCand = MakeSyncCand(cleanVector);
        var options = FinTsSynchronizationExecutionOptions.SystemId(syncFactory: _ => syncCand);

        // 1. Network failure
        using (var transport = new FinTsSimulatedTransport())
        {
            transport.EnqueueFailure(new FinTsTransportException(
                FinTsTransportIssue.NetworkFailure, "Simulated connection reset", new Uri(TestEndpoint)));

            var result = FinTsSynchronizationExecutionPipeline.ExecuteAsync(
                conn, transport, pin, options).AsTask().GetAwaiter().GetResult();

            verify(!result.IsSuccess, "Network failure must reject execution.");
            verify(result.Status == FinTsSynchronizationExecutionStatus.RejectedTransportFailure,
                "Network failure must be RejectedTransportFailure.");
            verify(result.UpdatedConnection.LastAttemptedContact is not null, "Attempted contact recorded.");
        }

        // 2. Timeout failure
        using (var transport = new FinTsSimulatedTransport())
        {
            transport.EnqueueFailure(new FinTsTransportException(
                FinTsTransportIssue.Timeout, "Simulated request timeout", new Uri(TestEndpoint)));

            var result = FinTsSynchronizationExecutionPipeline.ExecuteAsync(
                conn, transport, pin, options).AsTask().GetAwaiter().GetResult();

            verify(!result.IsSuccess, "Timeout failure must reject execution.");
            verify(result.Status == FinTsSynchronizationExecutionStatus.RejectedTransportFailure,
                "Timeout must be RejectedTransportFailure.");
        }

        // 3. HTTP 500 error response
        using (var transport = new FinTsSimulatedTransport())
        {
            transport.EnqueueResponse("Internal Server Error"u8.ToArray(), statusCode: 500);

            var result = FinTsSynchronizationExecutionPipeline.ExecuteAsync(
                conn, transport, pin, options).AsTask().GetAwaiter().GetResult();

            verify(!result.IsSuccess, "HTTP 500 must reject execution.");
            verify(result.Status == FinTsSynchronizationExecutionStatus.RejectedTransportFailure,
                "HTTP 500 must be RejectedTransportFailure.");
        }

        // 4. Cancellation token
        using (var transport = new FinTsSimulatedTransport())
        {
            using var cts = new CancellationTokenSource();
            cts.Cancel();

            var result = FinTsSynchronizationExecutionPipeline.ExecuteAsync(
                conn, transport, pin, options, cts.Token).AsTask().GetAwaiter().GetResult();

            verify(!result.IsSuccess, "Pre-cancelled token must cancel execution.");
            verify(result.Status == FinTsSynchronizationExecutionStatus.Cancelled, "Status must be Cancelled.");
        }
    }

    private static void VerifyBankAbortAndReviewOutcomes(Action<bool, string> verify)
    {
        var conn = CreateTestConnection();
        using var pin = CreatePin();

        // 1. Bank abort 9800 on synchronization (Vector 6: bank-abort-on-synchronization)
        using (var transport = new FinTsSimulatedTransport())
        {
            var vector = GetVectorNamed("bank-abort-on-synchronization");
            var syncCand = MakeSyncCand(vector);
            transport.EnqueueResponse(Convert.FromBase64String(vector.GetProperty("syncResponse").GetProperty("wireBase64").GetString()!));

            var options = FinTsSynchronizationExecutionOptions.SystemId(syncFactory: _ => syncCand);
            var result = conn.ExecuteSynchronizationPipelineAsync(transport, pin, options).AsTask().GetAwaiter().GetResult();

            verify(!result.IsSuccess, "Bank abort on sync must fail.");
            verify(result.Status == FinTsSynchronizationExecutionStatus.RejectedProtocolFailure,
                "Bank abort on sync must be RejectedProtocolFailure.");
            verify(result.UpdatedConnection.State == BankConnectionState.TemporarilyUnavailable,
                "Abort on sync transitions to TemporarilyUnavailable.");
            verify(result.UpdatedConnection.LastAttemptedContact is not null, "Attempted contact recorded.");
        }

        // 2. Scoped review on synchronization (Vector 7: scoped-review-on-synchronization)
        using (var transport = new FinTsSimulatedTransport())
        {
            var vector = GetVectorNamed("scoped-review-on-synchronization");
            var syncCand = MakeSyncCand(vector);
            transport.EnqueueResponse(Convert.FromBase64String(vector.GetProperty("syncResponse").GetProperty("wireBase64").GetString()!));

            var options = FinTsSynchronizationExecutionOptions.SystemId(syncFactory: _ => syncCand);
            var result = conn.ExecuteSynchronizationPipelineAsync(transport, pin, options).AsTask().GetAwaiter().GetResult();

            verify(!result.IsSuccess, "Review on sync must fail.");
            verify(result.Status == FinTsSynchronizationExecutionStatus.RejectedDialogueOutcome,
                "Review on sync must be RejectedDialogueOutcome.");
            verify(result.UpdatedConnection.State == BankConnectionState.ReauthenticationRequired,
                "Review on sync transitions to ReauthenticationRequired.");
        }

        // 3. Bank abort 9800 on closing (Vector 4: bank-abort-on-closing)
        using (var transport = new FinTsSimulatedTransport())
        {
            var vector = GetVectorNamed("bank-abort-on-closing");
            var syncCand = MakeSyncCand(vector);
            var syncRespBytes = Convert.FromBase64String(vector.GetProperty("syncResponse").GetProperty("wireBase64").GetString()!);
            var closeRespBytes = Convert.FromBase64String(vector.GetProperty("closingResponse").GetProperty("wireBase64").GetString()!);

            transport.EnqueueResponse(syncRespBytes);
            transport.EnqueueResponse(closeRespBytes);

            var options = FinTsSynchronizationExecutionOptions.SystemId(
                syncFactory: _ => syncCand,
                closingFactory: (attempt, syncRes) => ClosingCandidate(vector.GetProperty("closingCandidate"), syncRes.SynchronizationEvidence!));

            var result = conn.ExecuteSynchronizationPipelineAsync(transport, pin, options).AsTask().GetAwaiter().GetResult();

            verify(!result.IsSuccess, "Bank abort on closing must fail.");
            verify(result.Status == FinTsSynchronizationExecutionStatus.RejectedProtocolFailure,
                "Bank abort on closing must be RejectedProtocolFailure.");
            verify(result.UpdatedConnection.State == BankConnectionState.TemporarilyUnavailable,
                "Abort on closing transitions to TemporarilyUnavailable.");
        }

        // 4. Scoped review on closing (Vector 5: scoped-review-on-closing)
        using (var transport = new FinTsSimulatedTransport())
        {
            var vector = GetVectorNamed("scoped-review-on-closing");
            var syncCand = MakeSyncCand(vector);
            var syncRespBytes = Convert.FromBase64String(vector.GetProperty("syncResponse").GetProperty("wireBase64").GetString()!);
            var closeRespBytes = Convert.FromBase64String(vector.GetProperty("closingResponse").GetProperty("wireBase64").GetString()!);

            transport.EnqueueResponse(syncRespBytes);
            transport.EnqueueResponse(closeRespBytes);

            var options = FinTsSynchronizationExecutionOptions.SystemId(
                syncFactory: _ => syncCand,
                closingFactory: (attempt, syncRes) => ClosingCandidate(vector.GetProperty("closingCandidate"), syncRes.SynchronizationEvidence!));

            var result = conn.ExecuteSynchronizationPipelineAsync(transport, pin, options).AsTask().GetAwaiter().GetResult();

            verify(!result.IsSuccess, "Review on closing must fail.");
            verify(result.Status == FinTsSynchronizationExecutionStatus.RejectedDialogueOutcome,
                "Review on closing must be RejectedDialogueOutcome.");
            verify(result.UpdatedConnection.State == BankConnectionState.ReauthenticationRequired,
                "Review on closing transitions to ReauthenticationRequired.");
        }
    }

    private static void VerifyCleanAssignmentExecution(Action<bool, string> verify)
    {
        var conn = CreateTestConnection(BankConnectionState.Unconfigured);
        using var pin = CreatePin();
        using var transport = new FinTsSimulatedTransport();

        var vector = GetVectorNamed("clean-assignment-session");
        var syncCand = MakeSyncCand(vector);
        var syncRespBytes = Convert.FromBase64String(vector.GetProperty("syncResponse").GetProperty("wireBase64").GetString()!);
        var closeRespBytes = Convert.FromBase64String(vector.GetProperty("closingResponse").GetProperty("wireBase64").GetString()!);

        transport.EnqueueResponse(syncRespBytes);
        transport.EnqueueResponse(closeRespBytes);

        var options = FinTsSynchronizationExecutionOptions.SystemId(
            syncFactory: _ => syncCand,
            closingFactory: (attempt, syncRes) => ClosingCandidate(vector.GetProperty("closingCandidate"), syncRes.SynchronizationEvidence!));

        var result = conn.ExecuteSynchronizationPipelineAsync(transport, pin, options).AsTask().GetAwaiter().GetResult();

        verify(result.IsSuccess, "Clean assignment synchronization pipeline must succeed.");
        verify(result.Status == FinTsSynchronizationExecutionStatus.Completed, "Status must be Completed.");
        verify(result.UpdatedConnection.IsSynchronized, "Connection must now be synchronized.");
        verify(result.UpdatedConnection.State == BankConnectionState.Connected, "State transitioned to Connected.");
        verify(result.UpdatedConnection.SystemId == "PUBLIC-ASSIGNED", "System ID assigned from HISYN report.");
        verify(result.UpdatedConnection.NextClientMessageNumber == 1, "Mode 0 resets next message number to 1.");
        verify(result.UpdatedConnection.LastSuccessfulContact is not null, "Successful contact recorded.");
        verify(transport.TransmittedCount == 2, "Exactly 2 messages transmitted (sync, close).");
    }

    private static void VerifyCleanMessageRecoveryExecution(Action<bool, string> verify)
    {
        var conn = CreateTestConnection(BankConnectionState.ReauthenticationRequired, systemId: "PUBLIC-SYSTEM");
        using var pin = CreatePin();
        using var transport = new FinTsSimulatedTransport();

        var vector = GetVectorNamed("clean-message-recovery-session");
        var syncCand = MakeSyncCand(vector);
        var syncRespBytes = Convert.FromBase64String(vector.GetProperty("syncResponse").GetProperty("wireBase64").GetString()!);
        var closeRespBytes = Convert.FromBase64String(vector.GetProperty("closingResponse").GetProperty("wireBase64").GetString()!);

        transport.EnqueueResponse(syncRespBytes);
        transport.EnqueueResponse(closeRespBytes);

        var recoveryContext = new FinTsSynchronizationRecoveryContext("PUBLIC-PREVIOUS", 10);
        var options = FinTsSynchronizationExecutionOptions.LastMessageNumber(
            recoveryContext: recoveryContext,
            syncFactory: _ => syncCand,
            closingFactory: (attempt, syncRes) => ClosingCandidate(vector.GetProperty("closingCandidate"), syncRes.SynchronizationEvidence!));

        var result = conn.ExecuteSynchronizationPipelineAsync(transport, pin, options).AsTask().GetAwaiter().GetResult();

        verify(result.IsSuccess, "Clean message recovery pipeline must succeed.");
        verify(result.Status == FinTsSynchronizationExecutionStatus.Completed, "Status must be Completed.");
        verify(result.UpdatedConnection.IsSynchronized, "Connection must now be synchronized.");
        verify(result.UpdatedConnection.State == BankConnectionState.Connected, "State transitioned to Connected.");
        verify(result.UpdatedConnection.SystemId == "PUBLIC-SYSTEM", "Existing system ID preserved.");
        verify(result.UpdatedConnection.NextClientMessageNumber == 11, "Next message number advanced to lastMsg + 1 (10 + 1 = 11).");
        verify(result.UpdatedConnection.LastSuccessfulContact is not null, "Successful contact recorded.");
    }

    private static void VerifyDefaultClosingCandidateExecution(Action<bool, string> verify)
    {
        var conn = CreateTestConnection(BankConnectionState.Unconfigured);
        using var pin = CreatePin();
        using var transport = new FinTsSimulatedTransport();

        var vector = GetVectorNamed("clean-assignment-session");
        var syncCand = MakeSyncCand(vector);
        var syncRespBytes = Convert.FromBase64String(vector.GetProperty("syncResponse").GetProperty("wireBase64").GetString()!);
        var closeRespBytes = Convert.FromBase64String(vector.GetProperty("closingResponse").GetProperty("wireBase64").GetString()!);

        transport.EnqueueResponse(syncRespBytes);
        transport.EnqueueResponse(closeRespBytes);

        // Note: ClosingCandidateFactory is left NULL to exercise CreateDefaultClosingCandidate
        var options = FinTsSynchronizationExecutionOptions.SystemId(
            syncFactory: _ => syncCand,
            closingFactory: null);

        var result = conn.ExecuteSynchronizationPipelineAsync(transport, pin, options).AsTask().GetAwaiter().GetResult();

        verify(result.IsSuccess, "Pipeline with default closing candidate generation must succeed.");
        verify(result.Status == FinTsSynchronizationExecutionStatus.Completed, "Status must be Completed.");
        verify(result.UpdatedConnection.IsSynchronized, "Connection is synchronized.");
        verify(result.UpdatedConnection.SystemId == "PUBLIC-ASSIGNED", "System ID assigned.");
        verify(transport.TransmittedCount == 2, "Transmitted sync and default closing messages.");
    }

    private static void VerifySecretSafetyAndDiagnosticHygiene(Action<bool, string> verify)
    {
        const string sentinelPin = "SENTINEL-SECRET-PIN-12345678";
        using var pin = FinTsSessionCredential.CaptureAndClear(
            Encoding.UTF8.GetBytes(sentinelPin), FinTsCredentialKind.Pin, TimeSpan.FromMinutes(5));

        var conn = CreateTestConnection();
        using var transport = new FinTsSimulatedTransport();
        transport.EnqueueFailure(new FinTsTransportException(
            FinTsTransportIssue.NetworkFailure, "Network drop", new Uri(TestEndpoint)));

        var vector = GetVectorNamed("clean-assignment-session");
        var syncCand = MakeSyncCand(vector);
        var options = FinTsSynchronizationExecutionOptions.SystemId(syncFactory: _ => syncCand);

        var result = FinTsSynchronizationExecutionPipeline.ExecuteAsync(
            conn, transport, pin, options).AsTask().GetAwaiter().GetResult();

        verify(!result.IsSuccess, "Pipeline must fail on transport error.");
        verify(!result.Issues.Any(i => i.Contains(sentinelPin, StringComparison.Ordinal)),
            "Sentinel PIN must never appear in execution result issues.");
        verify(!result.Issues.Any(i => i.Contains("HNSHA", StringComparison.Ordinal)),
            "HNSHA segment markup must never appear in issues.");
    }

    private static void VerifyMultiThreadedConcurrency(Action<bool, string> verify)
    {
        const int threadCount = 16;
        var vector = GetVectorNamed("clean-assignment-session");
        var syncCand = MakeSyncCand(vector);
        var syncRespBytes = Convert.FromBase64String(vector.GetProperty("syncResponse").GetProperty("wireBase64").GetString()!);
        var closeRespBytes = Convert.FromBase64String(vector.GetProperty("closingResponse").GetProperty("wireBase64").GetString()!);

        int completed = 0;
        var exceptions = new List<Exception>();

        Parallel.For(0, threadCount, i =>
        {
            try
            {
                using var transport = new FinTsSimulatedTransport();
                transport.EnqueueResponse(syncRespBytes);
                transport.EnqueueResponse(closeRespBytes);

                using var pin = CreatePin();
                var conn = CreateTestConnection(BankConnectionState.Unconfigured, connId: 1000 + i);

                var options = FinTsSynchronizationExecutionOptions.SystemId(
                    syncFactory: _ => syncCand,
                    closingFactory: null);

                var result = conn.ExecuteSynchronizationPipelineAsync(transport, pin, options).AsTask().GetAwaiter().GetResult();

                if (result.IsSuccess && result.UpdatedConnection.IsSynchronized)
                {
                    Interlocked.Increment(ref completed);
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

        verify(exceptions.Count == 0, $"Concurrent runs produced 0 unhandled exceptions (saw {exceptions.Count}).");
        verify(completed == threadCount, $"All {threadCount} concurrent synchronization pipelines completed successfully.");
    }

    private static BankConnection CreateTestConnection(
        BankConnectionState state = BankConnectionState.Connected,
        bool isSynchronized = true,
        int connId = 1,
        string? systemId = null)
    {
        var config = ManualInstitutionConfiguration.Create(
            "Test Bank",
            TestEndpoint,
            bankCode: BankCode,
            bic: "TESTDEFFXXX")
            .ConfirmEndpoint(TestEndpoint);

        return BankConnection.Create(
            new ConnectionId((ulong)connId),
            new InstitutionId(1),
            config,
            userId: "PUBLIC-USER",
            systemId: systemId ?? (isSynchronized ? "PUBLIC-SYSTEM" : null))
            .WithState(state);
    }

    private static FinTsSessionCredential CreatePin() =>
        FinTsSessionCredential.CaptureAndClear("PUBLIC-PIN"u8.ToArray(), FinTsCredentialKind.Pin, TimeSpan.FromMinutes(5));

    private static FinTsPinTanRequestBinding MakeSyncCand(JsonElement vector) =>
        FinTsPinTanRequestBinding.ForEnvelopeCandidate(
            FinTsPinTanSignatureTrailerTests.Evidence(vector.GetProperty("syncCandidate")));

    private static FinTsPinTanDialogueEndRequestBinding ClosingCandidate(JsonElement element, FinTsPinTanSynchronizationEvidence syncEvidence)
    {
        var unsignedClosing = FinTsUnsignedDialogueEndRequest.Parse(
            FinTsMessageFrame.Parse(Convert.FromBase64String(element.GetProperty("requestBase64").GetString()!)),
            element.GetProperty("expectedBankMessageNumber").GetInt32());
        var closingHeader = FinTsPinTanSignatureHeader.Parse(
            FinTsSyntax.ParseSegments(Convert.FromBase64String(element.GetProperty("headerBase64").GetString()!)).Segments.Single());
        var closingContext = FinTsPinTanDialogueEndContext.Evaluate(syncEvidence, unsignedClosing, closingHeader);
        return FinTsPinTanDialogueEndRequestBinding.ForEnvelopeCandidate(closingContext);
    }
}
