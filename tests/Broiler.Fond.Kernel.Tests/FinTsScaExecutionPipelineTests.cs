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
using Broiler.Fond.Kernel.Product;
using static Broiler.Fond.Kernel.Tests.FinTsPinTanReadSessionAttemptTests;
using static Broiler.Fond.Kernel.Tests.FinTsReadSessionCandidates;

namespace Broiler.Fond.Kernel.Tests;

internal static class FinTsScaExecutionPipelineTests
{
    private const string BankCode = "PUBLIC-BANK";
    private const string TestEndpoint = "https://bank.example/fints";
    private static readonly DateTimeOffset TestTimestamp = DateTimeOffset.Parse("2026-10-16T12:00:00Z", CultureInfo.InvariantCulture);

    private static readonly Lazy<JsonDocument> ScaDocument = new(() =>
        JsonDocument.Parse(Assembly.GetExecutingAssembly().GetManifestResourceStream("FinTs.sca-continuation-v1.json")!));

    private static JsonElement GetScaVector(int index) =>
        ScaDocument.Value.RootElement.GetProperty("vectors")[index];

    private static readonly Lazy<JsonDocument> SessionsDocument = new(() =>
        JsonDocument.Parse(Assembly.GetExecutingAssembly().GetManifestResourceStream("FinTs.pin-tan-read-session-attempt-v1.json")!));

    private static JsonElement GetReadVector(int index) =>
        SessionsDocument.Value.RootElement.GetProperty("vectors")[index];

    private static readonly Lazy<JsonDocument> SignatureContextDocument = new(() =>
        JsonDocument.Parse(Assembly.GetExecutingAssembly().GetManifestResourceStream("FinTs.pin-tan-signature-context-v1.json")!));

    private static FinTsPinTanProcedureContext GetProcedureContext(int index = 0)
    {
        var v = SignatureContextDocument.Value.RootElement.GetProperty("vectors")[index];
        var frame = FinTsMessageFrame.Parse(Convert.FromBase64String(v.GetProperty("originRequestBase64").GetString()!));
        var response = FinTsResponse.Parse(FinTsMessageFrame.Parse(Convert.FromBase64String(v.GetProperty("procedureResponseBase64").GetString()!)));
        return new FinTsPinTanProcedureContext(
            FinTsUnsignedInitializationRequest.Parse(frame),
            FinTsTanParameterSet.Parse(FinTsParameterSet.Parse(response)),
            FinTsPermittedProcedureSet.Parse(response));
    }

    private static string Text(FinTsDataElement element) => Encoding.Latin1.GetString(element.CopyValueBytes());

    internal static void Run(Action<bool, string> check)
    {
        int count = 0;
        void Verify(bool condition, string message) { count++; check(condition, message); }

        VerifyProductRegistration(Verify);
        VerifyScaCoordinatorEncoding(Verify);
        VerifyDecoupledScaPollingLifecycle(Verify);
        VerifyInteractiveTanContinuation(Verify);
        VerifyScaExemptionHandling(Verify);
        VerifyReadPipelineWithScaChallenge(Verify);
        VerifySynchronizationPipelineWithScaChallenge(Verify);
        VerifyCredentialZeroizationAndHygiene(Verify);

        Console.WriteLine($"FinTS SCA execution pipeline & product registration: {count} checks completed.");
    }

    private static void VerifyProductRegistration(Action<bool, string> verify)
    {
        // 1. KernelProduct default registration values
        var defaultReg = KernelProduct.FinTsRegistration;
        verify(defaultReg.ProductIdentifier == "BROILER-FOND", "Default product identifier must be BROILER-FOND.");
        verify(defaultReg.ProductVersion == "1.0", "Default product version must be 1.0.");
        verify(defaultReg.RegistrationCode == "9999999999", "Default registration code must be 9999999999.");
        verify(defaultReg.DisplayName == KernelProduct.FullDisplayName, "Default registration display name must match KernelProduct.FullDisplayName.");

        // 2. Custom valid registration
        var customReg = new FinTsProductRegistration("MY-APP", "2.1", "1234567890", "My App Display");
        verify(customReg.ProductIdentifier == "MY-APP", "Custom identifier preserved.");
        verify(customReg.ProductVersion == "2.1", "Custom version preserved.");
        verify(customReg.RegistrationCode == "1234567890", "Custom code preserved.");

        // 3. Validation rejections
        void RejectReg(Action action, string message)
        {
            try
            {
                action();
                verify(false, message);
            }
            catch (ArgumentException)
            {
                verify(true, message);
            }
        }

        RejectReg(() => new FinTsProductRegistration("", "1.0", "123"), "Empty product identifier rejected.");
        RejectReg(() => new FinTsProductRegistration(" ", "1.0", "123"), "Whitespace product identifier rejected.");
        RejectReg(() => new FinTsProductRegistration(new string('A', 26), "1.0", "123"), "Overlong product identifier rejected.");
        RejectReg(() => new FinTsProductRegistration("PROD+ID", "1.0", "123"), "Delimiter '+' in identifier rejected.");
        RejectReg(() => new FinTsProductRegistration("PROD:ID", "1.0", "123"), "Delimiter ':' in identifier rejected.");
        RejectReg(() => new FinTsProductRegistration("PROD'ID", "1.0", "123"), "Delimiter '\'' in identifier rejected.");
        RejectReg(() => new FinTsProductRegistration("PROD?ID", "1.0", "123"), "Delimiter '?' in identifier rejected.");

        RejectReg(() => new FinTsProductRegistration("ID", "", "123"), "Empty version rejected.");
        RejectReg(() => new FinTsProductRegistration("ID", "123456", "123"), "Overlong version (> 5 chars) rejected.");
        RejectReg(() => new FinTsProductRegistration("ID", "1+2", "123"), "Delimiter '+' in version rejected.");

        RejectReg(() => new FinTsProductRegistration("ID", "1.0", ""), "Empty registration code rejected.");
        RejectReg(() => new FinTsProductRegistration("ID", "1.0", new string('9', 26)), "Overlong registration code (> 25 chars) rejected.");
        RejectReg(() => new FinTsProductRegistration("ID", "1.0", "99+99"), "Delimiter '+' in registration code rejected.");

        // 4. Default initialization candidate incorporates product registration
        var conn = CreateTestConnection();
        var procCtx = GetProcedureContext(0);
        var initCandidate = FinTsReadExecutionPipeline.CreateDefaultInitializationCandidate(conn, procCtx, customReg);
        var initPrep = initCandidate.SignatureEvidence.Request.Initialization!.Preparation;
        verify(initPrep.ProductIdentifier == "MY-APP", "Initialization candidate must embed registered product identifier.");
        verify(initPrep.ProductVersion == "2.1", "Initialization candidate must embed registered product version.");

        // 5. Default synchronization candidate incorporates product registration
        var syncCandidate = FinTsSynchronizationExecutionPipeline.CreateDefaultSynchronizationCandidate(conn, procCtx, FinTsSynchronizationMode.SystemId, customReg);
        var syncPrep = syncCandidate.SignatureEvidence.Request.Synchronization!.Preparation;
        verify(syncPrep.ProductIdentifier == "MY-APP", "Synchronization candidate must embed registered product identifier.");
        verify(syncPrep.ProductVersion == "2.1", "Synchronization candidate must embed registered product version.");
    }

    private static void VerifyScaCoordinatorEncoding(Action<bool, string> verify)
    {
        // 1. Status query encoding matches fixture vector 1 query 0 byte-for-byte
        byte[] expectedWire = Convert.FromBase64String(GetScaVector(1).GetProperty("queries")[0].GetProperty("requestBase64").GetString()!);
        byte[] actualWire = FinTsScaExecutionCoordinator.EncodeStatusQuery("SYNTHETIC", 2, 7, "PUBLIC-ORDER");
        verify(actualWire.SequenceEqual(expectedWire), "EncodeStatusQuery must match fixture wire bytes byte-for-byte.");

        // 2. Status query parsing and structure
        var frame = FinTsMessageFrame.Parse(actualWire);
        verify(frame.MessageNumber == 2, "Status query message number must be 2.");
        verify(frame.Syntax.Segments.Count == 3, "Status query must have exactly 3 segments (HNHBK, HKTAN, HNHBS).");
        verify(frame.Syntax.Segments[1].Code == "HKTAN", "Middle segment must be HKTAN.");
        verify(Text(frame.Syntax.Segments[1].Fields[0].Elements[0]) == "S", "HKTAN process must be 'S' for status query.");
        verify(Text(frame.Syntax.Segments[1].Fields[4].Elements[0]) == "PUBLIC-ORDER", "Order reference must match.");

        // 3. Status query with optional medium name
        byte[] mediumWire = FinTsScaExecutionCoordinator.EncodeStatusQuery("SYNTHETIC", 3, 7, "PUBLIC-ORDER", "MyMedium");
        var mediumFrame = FinTsMessageFrame.Parse(mediumWire);
        verify(Text(mediumFrame.Syntax.Segments[1].Fields[10].Elements[0]) == "MyMedium", "Medium name correctly positioned.");

        // 4. TAN continuation encoding
        byte[] contWire = FinTsScaExecutionCoordinator.EncodeTanContinuation("SYNTHETIC", 2, 7, "PUBLIC-ORDER", "HKIDN");
        var contFrame = FinTsMessageFrame.Parse(contWire);
        verify(contFrame.MessageNumber == 2, "Continuation message number must be 2.");
        verify(Text(contFrame.Syntax.Segments[1].Fields[0].Elements[0]) == "2", "HKTAN process must be '2' for TAN continuation.");
        verify(Text(contFrame.Syntax.Segments[1].Fields[1].Elements[0]) == "HKIDN", "Target operation must be recorded.");
        verify(Text(contFrame.Syntax.Segments[1].Fields[4].Elements[0]) == "PUBLIC-ORDER", "Order reference must match.");

        // 5. CreateRequestContext synthetic frame
        var reqContext = FinTsScaExecutionCoordinator.CreateRequestContext("SYNTHETIC", 1, 1, 7, "HKIDN", "ORDER-123");
        verify(reqContext.Frame.MessageNumber == 1, "Synthetic request message number must match.");
        verify(reqContext.OrderReference is not null && Text(reqContext.OrderReference) == "ORDER-123", "Synthetic request order reference preserved.");
    }

    private static void VerifyDecoupledScaPollingLifecycle(Action<bool, string> verify)
    {
        var v1 = GetScaVector(1);
        var initialEvidence = ParseChallengeEvidence(v1);
        var q0Response = Convert.FromBase64String(v1.GetProperty("queries")[0].GetProperty("responseBase64").GetString()!);
        var q1Response = Convert.FromBase64String(v1.GetProperty("queries")[1].GetProperty("responseBase64").GetString()!);

        using var pin = CreatePin();
        var endpoint = new Uri(TestEndpoint);

        // Unsigned response fixtures must not authorize production status-query dispatch.
        using (var transport = new FinTsSimulatedTransport())
        {
            transport.EnqueueResponse(q0Response);
            transport.EnqueueResponse(q1Response);

            var result = FinTsScaExecutionCoordinator.ExecuteAsync(
                initialEvidence, transport, endpoint, pin,
                TimeSpan.FromSeconds(30), 10, allowAutomaticQueries: true,
                scaChallengeHandler: null, scaContinuationCandidateFactory: null).AsTask().GetAwaiter().GetResult();

            verify(!result.IsSuccess && transport.RecordedTransmissions.Count == 0, "Unsigned decoupled polling is rejected before dispatch.");
            verify(!result.IsExempt, "Decoupled SCA is not exempt.");
            verify(!result.IsCancelled, "Decoupled SCA was not cancelled.");
            verify(result.QueriesRecorded == 0, "Unsupported polling records no queries.");
            verify(result.TerminalResponse is null, "Unsupported polling cannot supply terminal evidence.");
        }

        // Missing signed builder rejects even when a transport error is queued.
        using (var transport = new FinTsSimulatedTransport())
        {
            transport.EnqueueFailure(new FinTsTransportException(FinTsTransportIssue.NetworkFailure, "Socket disconnected.", endpoint));

            var result = FinTsScaExecutionCoordinator.ExecuteAsync(
                initialEvidence, transport, endpoint, pin,
                TimeSpan.FromSeconds(30), 10, allowAutomaticQueries: true,
                scaChallengeHandler: null, scaContinuationCandidateFactory: null).AsTask().GetAwaiter().GetResult();

            verify(!result.IsSuccess, "Unsupported polling must fail.");
            verify(result.FailureReason!.Contains("signed status-query builder", StringComparison.Ordinal),
                "Unsupported polling explains the missing signed builder before any transport error.");
            verify(transport.RecordedTransmissions.Count == 0, "Unsupported polling never dispatches a query.");
        }
    }

    private static void VerifyInteractiveTanContinuation(Action<bool, string> verify)
    {
        var v0 = GetScaVector(0);
        var initialEvidence = ParseChallengeEvidence(v0);
        using var pin = CreatePin();
        var endpoint = new Uri(TestEndpoint);

        // 1. Missing handler when interactive TAN required
        using (var transport = new FinTsSimulatedTransport())
        {
            var result = FinTsScaExecutionCoordinator.ExecuteAsync(
                initialEvidence, transport, endpoint, pin,
                TimeSpan.FromSeconds(30), 10, allowAutomaticQueries: true,
                scaChallengeHandler: null, scaContinuationCandidateFactory: null).AsTask().GetAwaiter().GetResult();

            verify(!result.IsSuccess, "Missing handler must fail.");
            verify(result.FailureReason!.Contains("no ScaChallengeHandler", StringComparison.Ordinal),
                "Failure must indicate missing challenge handler.");
        }

        // 2. User cancels challenge (handler returns null)
        using (var transport = new FinTsSimulatedTransport())
        {
            var result = FinTsScaExecutionCoordinator.ExecuteAsync(
                initialEvidence, transport, endpoint, pin,
                TimeSpan.FromSeconds(30), 10, allowAutomaticQueries: true,
                scaChallengeHandler: (_, _, _) => ValueTask.FromResult<FinTsSessionCredential?>(null),
                scaContinuationCandidateFactory: null).AsTask().GetAwaiter().GetResult();

            verify(!result.IsSuccess, "Cancelled challenge must not succeed.");
            verify(result.IsCancelled, "Cancelled challenge must report IsCancelled == true.");
        }

        // 3. User cancels via OperationCanceledException
        using (var transport = new FinTsSimulatedTransport())
        {
            bool threw = false;
            try
            {
                FinTsScaExecutionCoordinator.ExecuteAsync(
                    initialEvidence, transport, endpoint, pin,
                    TimeSpan.FromSeconds(30), 10, allowAutomaticQueries: true,
                    scaChallengeHandler: (_, _, ct) => throw new OperationCanceledException(ct),
                    scaContinuationCandidateFactory: null).AsTask().GetAwaiter().GetResult();
            }
            catch (OperationCanceledException)
            {
                threw = true;
            }
            verify(threw, "OperationCanceledException from handler must be propagated.");
        }

        // 4. Handler returns wrong credential kind (PIN instead of TAN)
        using (var transport = new FinTsSimulatedTransport())
        {
            var result = FinTsScaExecutionCoordinator.ExecuteAsync(
                initialEvidence, transport, endpoint, pin,
                TimeSpan.FromSeconds(30), 10, allowAutomaticQueries: true,
                scaChallengeHandler: (_, _, _) => ValueTask.FromResult<FinTsSessionCredential?>(CreatePin()),
                scaContinuationCandidateFactory: null).AsTask().GetAwaiter().GetResult();

            verify(!result.IsSuccess, "Wrong credential kind must fail.");
            verify(result.FailureReason!.Contains("not an available TAN", StringComparison.Ordinal),
                "Failure must indicate credential is not a TAN.");
        }

        // Missing signed builder cannot fall back to an unsigned message.
        using (var transport = new FinTsSimulatedTransport())
        {
            var terminalWire = Convert.FromBase64String(GetScaVector(1).GetProperty("queries")[1].GetProperty("responseBase64").GetString()!);
            transport.EnqueueResponse(terminalWire);

            var result = FinTsScaExecutionCoordinator.ExecuteAsync(
                initialEvidence, transport, endpoint, pin,
                TimeSpan.FromSeconds(30), 10, allowAutomaticQueries: true,
                scaChallengeHandler: (ch, _, _) =>
                {
                    verify(ch.OrderReference is not null && Text(ch.OrderReference) == "PUBLIC-ORDER", "Challenge order reference delivered to handler.");
                    var tan = FinTsSessionCredential.CaptureAndClear("123456"u8.ToArray(), FinTsCredentialKind.Tan, TimeSpan.FromMinutes(5));
                    return ValueTask.FromResult<FinTsSessionCredential?>(tan);
                },
                scaContinuationCandidateFactory: null).AsTask().GetAwaiter().GetResult();

            verify(!result.IsSuccess && transport.RecordedTransmissions.Count == 0, "Missing signed TAN builder rejects without dispatch.");
            verify(!result.IsCancelled, "Not cancelled.");
            verify(result.QueriesRecorded == 0, "Unsupported continuation records no dispatch.");
        }

        using (var transport = new FinTsSimulatedTransport())
        {
            var result = FinTsScaExecutionCoordinator.ExecuteAsync(initialEvidence, transport, endpoint, pin,
                TimeSpan.FromSeconds(30), 10, true,
                (_, _, _) => throw new InvalidOperationException("secret-marker"), null).AsTask().GetAwaiter().GetResult();
            verify(!result.IsSuccess && !result.FailureReason!.Contains("secret-marker", StringComparison.Ordinal),
                "SCA interaction exceptions cannot expose secret markers in returned failures.");
            verify(transport.RecordedTransmissions.Count == 0, "Failed SCA interaction never dispatches a continuation.");
        }
    }

    private static void VerifyScaExemptionHandling(Action<bool, string> verify)
    {
        var v6 = GetScaVector(6);
        var initialEvidence = ParseChallengeEvidence(v6);
        using var pin = CreatePin();
        using var transport = new FinTsSimulatedTransport();
        var endpoint = new Uri(TestEndpoint);

        var result = FinTsScaExecutionCoordinator.ExecuteAsync(
            initialEvidence, transport, endpoint, pin,
            TimeSpan.FromSeconds(30), 10, allowAutomaticQueries: true,
            scaChallengeHandler: null, scaContinuationCandidateFactory: null).AsTask().GetAwaiter().GetResult();

        verify(result.IsSuccess, "Exempt SCA challenge must succeed.");
        verify(result.IsExempt, "Exempt SCA challenge must report IsExempt == true.");
        verify(result.QueriesRecorded == 0, "Exempt SCA challenge records zero queries.");
    }

    private static void VerifyReadPipelineWithScaChallenge(Action<bool, string> verify)
    {
        var ledger = CreateLedger(out _, out _);
        var conn = CreateTestConnection();
        using var pin = CreatePin();

        // 1. Initialization response contains SCA challenge, user cancels
        using (var transport = new FinTsSimulatedTransport())
        {
            var v0 = GetReadVector(0);
            var (initBinding, _, _, _) = FinTsPinTanInitializationRequirementsTests.Inputs(v0.GetProperty("initialization"));
            var initCandidate = initBinding.Request;

            // Enqueue challenge-bearing initialization response from SCA vector 0
            transport.EnqueueResponse(Convert.FromBase64String(GetScaVector(0).GetProperty("responseBase64").GetString()!));

            var options = FinTsReadExecutionOptions.Discovery(
                initFactory: _ => initCandidate,
                scaChallengeHandler: (_, _, _) => ValueTask.FromResult<FinTsSessionCredential?>(null));

            var result = FinTsReadExecutionPipeline.ExecuteAsync(
                conn, transport, pin, ledger, [], options).AsTask().GetAwaiter().GetResult();

            verify(!result.IsSuccess, "Cancelled SCA challenge in read pipeline must fail.");
            verify(result.Status == FinTsReadExecutionStatus.Cancelled, "Status must be Cancelled.");
        }

        // 2. Initialization response contains SCA challenge, missing handler
        using (var transport = new FinTsSimulatedTransport())
        {
            var v0 = GetReadVector(0);
            var (initBinding, _, _, _) = FinTsPinTanInitializationRequirementsTests.Inputs(v0.GetProperty("initialization"));
            var initCandidate = initBinding.Request;

            transport.EnqueueResponse(Convert.FromBase64String(GetScaVector(0).GetProperty("responseBase64").GetString()!));

            var options = FinTsReadExecutionOptions.Discovery(
                initFactory: _ => initCandidate,
                scaChallengeHandler: null);

            var result = FinTsReadExecutionPipeline.ExecuteAsync(
                conn, transport, pin, ledger, [], options).AsTask().GetAwaiter().GetResult();

            verify(!result.IsSuccess, "Missing SCA handler in read pipeline must fail.");
            verify(result.Status == FinTsReadExecutionStatus.RejectedDialogueOutcome, "Status must be RejectedDialogueOutcome.");
            verify(result.UpdatedConnection.State == BankConnectionState.ReauthenticationRequired,
                "Connection state must be ReauthenticationRequired.");
        }
    }

    private static void VerifySynchronizationPipelineWithScaChallenge(Action<bool, string> verify)
    {
        var conn = CreateTestConnection();
        using var pin = CreatePin();

        var procCtx = GetProcedureContext(0);

        // 1. Synchronization response contains SCA challenge, user cancels
        using (var transport = new FinTsSimulatedTransport())
        {
            using var syncDoc = JsonDocument.Parse(Assembly.GetExecutingAssembly().GetManifestResourceStream("FinTs.pin-tan-synchronization-session-attempt-v1.json")!);
            var syncVec = syncDoc.RootElement.GetProperty("vectors")[0];
            byte[] procBytes = Convert.FromBase64String(syncVec.GetProperty("syncCandidate").GetProperty("procedureResponseBase64").GetString()!);
            byte[] template = Convert.FromBase64String(syncVec.GetProperty("syncResponse").GetProperty("wireBase64").GetString()!);
            var factory = new FinTsProductionCandidateFactory(
                conn,
                new FinTsProductRegistration("ASSIGNED-APP-ID", "2.1"),
                procCtx,
                new FinTsPinTanSignatureSelection(2, 900, 7));
            transport.EnqueueResponse(Broiler.Fond.TestSupport.SignedTanTestWire.Initial(template, procBytes));

            var options = factory.SynchronizationOptions(
                textTanPrompt: (_, _) => ValueTask.FromResult<FinTsSessionCredential?>(null));

            var result = FinTsSynchronizationExecutionPipeline.ExecuteAsync(
                conn, transport, pin, options).AsTask().GetAwaiter().GetResult();

            verify(!result.IsSuccess, "Cancelled SCA challenge in sync pipeline must fail.");
            verify(result.Status == FinTsSynchronizationExecutionStatus.Cancelled, "Status must be Cancelled.");
        }

        // 2. Synchronization response contains SCA challenge, missing handler
        using (var transport = new FinTsSimulatedTransport())
        {
            var syncCandidate = FinTsSynchronizationExecutionPipeline.CreateDefaultSynchronizationCandidate(conn, procCtx);
            transport.EnqueueResponse(Convert.FromBase64String(GetScaVector(0).GetProperty("responseBase64").GetString()!));

            var options = FinTsSynchronizationExecutionOptions.SystemId(
                syncFactory: _ => syncCandidate,
                scaChallengeHandler: null);

            var result = FinTsSynchronizationExecutionPipeline.ExecuteAsync(
                conn, transport, pin, options).AsTask().GetAwaiter().GetResult();

            verify(!result.IsSuccess, "Missing SCA handler in sync pipeline must fail.");
            verify(result.Status == FinTsSynchronizationExecutionStatus.RejectedDialogueOutcome, "Status must be RejectedDialogueOutcome.");
            verify(result.UpdatedConnection.State == BankConnectionState.ReauthenticationRequired,
                "Connection state must be ReauthenticationRequired.");
        }
    }

    private static void VerifyCredentialZeroizationAndHygiene(Action<bool, string> verify)
    {
        // 1. Transient TAN credential buffer is zeroized after copy-out / disposal
        byte[] rawTan = "987654"u8.ToArray();
        var credential = FinTsSessionCredential.CaptureAndClear(rawTan, FinTsCredentialKind.Tan, TimeSpan.FromMinutes(1));
        verify(rawTan.All(b => b == 0), "Source buffer must be zeroized on capture.");

        credential.Dispose();
        verify(credential.GetSnapshot().State == FinTsCredentialState.Disposed, "Credential state must be Disposed.");

        // 2. FinTsProductRegistration and FinTsScaExecutionResult ToString hygiene
        var reg = new FinTsProductRegistration("MY-PRODUCT", "1.0", "REG-CODE-123", "Product Display");
        string regString = reg.ToString()!;
        verify(!regString.Contains("PIN", StringComparison.OrdinalIgnoreCase), "Registration string must not contain credentials.");

        var scaResult = FinTsScaExecutionResult.Cancelled("User cancelled.");
        string scaString = scaResult.ToString()!;
        verify(!scaString.Contains("SECRET", StringComparison.OrdinalIgnoreCase), "Result string must be diagnostic-safe.");
    }

    private static FinTsTanChallengeEvidence ParseChallengeEvidence(JsonElement vector)
    {
        byte[] requestBytes = Convert.FromBase64String(vector.GetProperty("requestBase64").GetString()!);
        byte[] responseBytes = Convert.FromBase64String(vector.GetProperty("responseBase64").GetString()!);
        var request = FinTsTanRequestContext.Parse(FinTsMessageFrame.Parse(requestBytes), 2, 1);
        var response = FinTsResponse.Parse(FinTsMessageFrame.Parse(responseBytes));
        var parameters = FinTsTanParameterSet.Parse(FinTsParameterSet.Parse(response));
        return FinTsTanChallengeEvidence.Evaluate(
            request, parameters, parameters.Advertisements[0].Procedures[0],
            FinTsPermittedProcedureSet.Parse(response), FinTsTanChallengeSet.Parse(response));
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

    private static FinTsSessionCredential CreatePin() =>
        FinTsSessionCredential.CaptureAndClear("12345"u8.ToArray(), FinTsCredentialKind.Pin, TimeSpan.FromMinutes(5));
}
