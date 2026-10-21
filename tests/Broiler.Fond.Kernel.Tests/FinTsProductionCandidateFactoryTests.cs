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

namespace Broiler.Fond.Kernel.Tests;

internal static class FinTsProductionCandidateFactoryTests
{
    internal static void Run(Action<bool, string> check)
    {
        using var synchronization = Load("FinTs.pin-tan-synchronization-session-attempt-v1.json");
        using var reads = Load("FinTs.pin-tan-read-session-attempt-v1.json");
        JsonElement sync = synchronization.RootElement.GetProperty("vectors")[0];
        JsonElement syncContext = sync.GetProperty("syncCandidate");
        var connection = Connection();
        var registration = new FinTsProductRegistration("ASSIGNED-APP-ID", "2.1", "ASSIGNED-APP-ID");
        var factory = new FinTsProductionCandidateFactory(connection, registration, Procedures(syncContext), Selection(syncContext));
        var candidate = factory.CreateSynchronization();
        var expected = FinTsUnsignedSynchronizationWriter.Encode(new FinTsInitializationInput("280", "PUBLIC-BANK", "PUBLIC-CUSTOMER", "0",
            FinTsCustomerSystemStatus.Required, 1, 2, FinTsDialogueLanguage.German, "ASSIGNED-APP-ID", "2.1"), FinTsSynchronizationMode.SystemId);
        check(candidate.SignatureEvidence.Request.Frame.Syntax.CopyWireBytes().SequenceEqual(expected),
            "Production synchronization unsigned bytes derive from configured identity, product, and bank parameter versions.");
        check(candidate.SignatureEvidence.Header.ControlReference != factory.CreateSynchronization().SignatureEvidence.Header.ControlReference,
            "Production requests allocate fresh control references instead of fixture references.");
        using var pin = Pin();
        byte[] output = new byte[FinTsPinTanRequestEnvelopeWriter.MaximumEncodedLength];
        try
        {
            check(FinTsPinTanRequestEnvelopeWriter.TryEncode(candidate.SignatureEvidence, pin, null, output, out int written) == FinTsPinTanRequestWriteResult.Written,
                "Production synchronization candidate encodes through the bounded PIN/TAN envelope writer.");
            string wire = Encoding.Latin1.GetString(output, 0, written);
            check(wire.Contains("HKVVB:4:3+1+2+1+ASSIGNED-APP-ID+2.1'") && wire.Contains("HKSYN:5:3+0'"),
                "Actual outgoing envelope contains configured product/version and correctly numbered synchronization segment.");
        }
        finally { CryptographicOperations.ZeroMemory(output); }
        Reject(() => factory.CreateInitialization(), FinTsCandidateFailure.MissingSystemId, "Read initialization requires an assigned synchronized system ID.");
        Reject(() => factory.CreateSynchronization(FinTsSynchronizationMode.SignatureReferences), FinTsCandidateFailure.UnsupportedVersion,
            "Unsupported signature-reference recovery is rejected during construction.");
        Reject(() => new FinTsProductionCandidateFactory(Connection("OTHER-USER"), registration, Procedures(syncContext), Selection(syncContext)),
            FinTsCandidateFailure.UnqualifiedEvidence, "Procedure evidence from a different bank user cannot build a signed request.");
        Reject(() => new FinTsProductionCandidateFactory(connection, registration, Procedures(syncContext), new FinTsPinTanSignatureSelection(2, 901, 7)),
            FinTsCandidateFailure.UnqualifiedEvidence, "An unadvertised procedure is never silently replaced with the first available procedure.");
        Reject(FinTsProductionCandidateFactory.RequireScaBuilder, FinTsCandidateFailure.UnsupportedSca, "Missing interactive SCA builder is explicit.");

        using (var transport = new FinTsSimulatedTransport())
        {
            transport.EnqueueResponse(Bytes(sync.GetProperty("syncResponse"), "wireBase64"));
            transport.EnqueueResponse(Bytes(sync.GetProperty("closingResponse"), "wireBase64"));
            var result = FinTsSynchronizationExecutionPipeline.ExecuteAsync(connection, transport, pin, factory.SynchronizationOptions()).AsTask().GetAwaiter().GetResult();
            check(result.IsSuccess && result.UpdatedConnection.SystemId == "PUBLIC-ASSIGNED" && transport.RecordedTransmissions.Count == 2,
                "Production factories complete synchronization and correlated dialogue closing with scripted bank responses.");
        }

        foreach (int index in new[] { 0, 2, 3 })
        {
            var vector = reads.RootElement.GetProperty("vectors")[index];
            var origin = vector.GetProperty("initialization").GetProperty("context");
            var known = Connection().WithSynchronization("PUBLIC-SYSTEM", 1, DateTimeOffset.UtcNow);
            var f = new FinTsProductionCandidateFactory(known, registration, Procedures(origin), Selection(origin));
            using var attempt = new FinTsPinTanReadSessionAttempt(TimeSpan.FromSeconds(30));
            check(attempt.StartInitialization(f.CreateInitialization()) == FinTsPinTanReadSessionTransition.CandidateRecorded,
                "Production read initialization starts an evidence-tracked session.");
            var response = FinTsReadExecutionPipeline.ParseWireResponse(Bytes(vector.GetProperty("initialization"), "responseBase64"));
            var parameters = FinTsParameterSet.Parse(response);
            var initialization = attempt.AcceptInitializationResponse(FinTsTanParameterSet.Parse(parameters), FinTsPermittedProcedureSet.Parse(response),
                FinTsPinTanParameterSet.Parse(parameters), FinTsReadParameterSet.Parse(parameters));
            check(initialization.Transition == FinTsPinTanReadSessionTransition.DialogueOpen, "Factory initialization is qualified against real parsed bank parameters.");
            if (index == 0)
            {
                var discovery = f.CreateDiscovery(attempt);
                check(discovery.ReadNumber == 3 && discovery.MessageNumber == 2 && discovery.DialogueId == "SYNTHETIC", "Discovery uses pinned dialogue and negotiated counter/segment roles.");
                attempt.RecordAllAccountCandidate(discovery);
                attempt.AcceptAllAccountResponse(FinTsPinTanReadSessionAttemptTests.AllAccountResponse(vector.GetProperty("allAccount")));
            }
            else
            {
                var old = FinTsPinTanReadSessionAttemptTests.BalanceFirstReadCandidate(vector.GetProperty("balanceInitial"));
                var a = old.Context.Signature.Request.Request.Accounts.Single();
                var account = attempt.RequirementsEvidence!.ReadParameters!.Source.Accounts.Single();
                var balance = f.CreateBalance(attempt, account, new FinTsReadAccountInput(a.Number, a.Subaccount, a.Country, a.Institution, a.Iban, a.Bic));
                check(balance.Context.HasMatchingEvidence && balance.Context.Signature.Request.Request.Source.Version == 7,
                    "Balance factory selects a bank-advertised version for the exact account and retains HIPINS evidence.");
                attempt.RecordBalanceFirstReadCandidate(balance);
                attempt.AcceptBalanceResponse(FinTsPinTanReadSessionAttemptTests.BalanceResponse(vector.GetProperty("balanceInitial")));
                foreach (var page in vector.GetProperty("balanceContinuations").EnumerateArray())
                {
                    var continuation = f.CreateBalanceContinuation(attempt);
                    check(continuation.Context.HasMatchingEvidence && continuation.MessageNumber == 3 && continuation.ExpectedBankMessageNumber == 3,
                        "Continuation preserves exact accepted token/account provenance and advances both counters.");
                    attempt.RecordBalanceContinuationCandidate(continuation);
                    attempt.AcceptBalanceResponse(FinTsPinTanReadSessionAttemptTests.BalanceResponse(page));
                }
            }
            var closing = f.CreateClosing(attempt);
            check(attempt.RecordClosingCandidate(closing) == FinTsPinTanReadSessionTransition.CandidateRecorded,
                "Production close candidate derives from the accepted session and next counters.");
            var closed = attempt.AcceptClosingResponse(FinTsPinTanReadSessionAttemptTests.ClosingResponse(vector.GetProperty("closing")));
            check(closed.Transition == FinTsPinTanReadSessionTransition.Closed, "Production read dialogue closes with matching bank evidence.");
        }
        VerifyCleanup();
        VerifyTargetIngestion();

        void VerifyTargetIngestion()
        {
            var vector = reads.RootElement.GetProperty("vectors")[3];
            var context = vector.GetProperty("initialization").GetProperty("context");
            var connected = Connection().WithSynchronization("PUBLIC-SYSTEM", 1, DateTimeOffset.UtcNow);
            var builder = new FinTsProductionCandidateFactory(connected, registration, Procedures(context), Selection(context));
            var parameters = FinTsParameterSet.Parse(FinTsReadExecutionPipeline.ParseWireResponse(Bytes(vector.GetProperty("initialization"), "responseBase64")));
            var source = FinTsAccountSourceLocatorFactory.CreateLocator(connected, parameters.Accounts.Single());
            var locator = new AccountSourceLocator(source.InstitutionId, source.ConnectionId, source.Connector, source.ConnectorVersion,
                connected.InstitutionConfiguration, source.Iban, source.DomesticAccountNumber, source.DomesticBankCode, source.Subaccount, source.Currency,
                source.BankIdentifiers.Concat(new[] { new BankAccountIdentifier("fints", "account", "bic", "PUBLIC-BIC") }));
            var ledger = Ledger();
            using (var batch = ledger.BeginBatch())
            {
                var account = batch.Allocate(LocalEntityKind.Account);
                var incarnation = batch.Allocate(LocalEntityKind.AccountIncarnation, [new(account, LocalEntityKind.Account)]);
                batch.Allocate(LocalEntityKind.AccountBinding, [new(incarnation, LocalEntityKind.AccountIncarnation), new(new(2), LocalEntityKind.Connection)]);
                batch.Commit();
            }
            var target = new KnownAccountBinding(new(3), new(4), new(5), locator, AccountBindingState.Active);
            using var transport = new FinTsSimulatedTransport();
            transport.EnqueueResponse(Bytes(vector.GetProperty("initialization"), "responseBase64"));
            transport.EnqueueResponse(Bytes(vector.GetProperty("balanceInitial"), "responseBase64"));
            transport.EnqueueResponse(Bytes(vector.GetProperty("balanceContinuations")[0], "responseBase64"));
            transport.EnqueueResponse(Bytes(vector.GetProperty("closing"), "responseBase64"));
            var result = FinTsReadExecutionPipeline.ExecuteAsync(connected, transport, pin, ledger, new[] { target }, Options(16)).AsTask().GetAwaiter().GetResult();
            check(result.IsSuccess && result.IngestionResult!.BoundAccounts.Single().BindingId == target.BindingId &&
                result.IngestionResult.IngestedSnapshots.Count == 3 && ledger.Snapshot.Records.Count(r => r.Kind == LocalEntityKind.Account) == 1,
                "Strict target ingestion persists only the existing verified source and one set of balance observations after complete pagination.");
            if (!result.IsSuccess) return;
            var verified = result.SessionResult!;
            var detached = Ledger();
            var substitutedReports = FinTsReadDataSet.Parse(FinTsReadExecutionPipeline.ParseWireResponse(
                Bytes(reads.RootElement.GetProperty("vectors")[2].GetProperty("balanceInitial"), "responseBase64"))).Balances;
            FinTsPinTanReadSessionResult Repack(IReadOnlyList<FinTsBalanceReport> balances, FinTsPinTanReadContinuationEvidence? continuation) =>
                FinTsPinTanReadSessionResult.Create(verified.Transition, verified.Stage, verified.RequirementsEvidence,
                    balanceFirstPageEvidence: verified.BalanceFirstPageEvidence, balanceContinuationEvidence: continuation,
                    aggregatedBalances: balances, closingEvidence: verified.ClosingEvidence, procedureEvidence: verified.ProcedureEvidence,
                    initializationEvidence: verified.InitializationEvidence);
            var swapped = FinTsTargetBalanceIngestion.Apply(connected, target, Repack(substitutedReports, verified.BalanceContinuationEvidence),
                new[] { target }, detached, DateTimeOffset.UtcNow);
            check(!swapped.IsSuccess && detached.Snapshot.Records.Count == 2,
                "Equal-looking reports from another parsed response cannot substitute for accepted page evidence or allocate identities.");
            var incomplete = FinTsTargetBalanceIngestion.Apply(connected, target, Repack(verified.AggregatedBalances, null),
                new[] { target }, detached, DateTimeOffset.UtcNow);
            check(!incomplete.IsSuccess && detached.Snapshot.Records.Count == 2, "Dropping continuation provenance cannot turn an incomplete first page into a durable balance.");
            using var limited = new FinTsSimulatedTransport();
            limited.EnqueueResponse(Bytes(vector.GetProperty("initialization"), "responseBase64"));
            limited.EnqueueResponse(Bytes(vector.GetProperty("balanceInitial"), "responseBase64"));
            limited.EnqueueResponse(Bytes(reads.RootElement.GetProperty("vectors")[2].GetProperty("closing"), "responseBase64"));
            ulong before = ledger.Snapshot.NextId;
            var capped = FinTsReadExecutionPipeline.ExecuteAsync(connected, limited, pin, ledger, new[] { target }, Options(1)).AsTask().GetAwaiter().GetResult();
            check(!capped.IsSuccess && capped.IngestionResult is null && ledger.Snapshot.NextId == before,
                "Reaching the strict page budget never publishes a partial balance or consumes ledger IDs.");

            FinTsReadExecutionOptions Options(int pages) => new()
            {
                Kind = FinTsReadExecutionKind.BalanceRefresh,
                VerifiedTargetOnly = true,
                TargetAccountBinding = target,
                ProductRegistration = registration,
                MaximumBalancePages = pages,
                InitializationCandidateFactory = _ => builder.CreateInitialization(),
                BalanceFirstReadCandidateFactory = a => builder.CreateBalance(a, a.RequirementsEvidence!.ReadParameters!.Source.Accounts.Single(), new(iban: "PUBLIC-IBAN", bic: "PUBLIC-BIC")),
                BalanceContinuationCandidateFactory = a => builder.CreateBalanceContinuation(a),
                ClosingCandidateFactory = a => builder.CreateClosing(a)
            };
        }

        void VerifyCleanup()
        {
            var vector = reads.RootElement.GetProperty("vectors")[0];
            var context = vector.GetProperty("initialization").GetProperty("context");
            var known = Connection().WithSynchronization("PUBLIC-SYSTEM", 1, DateTimeOffset.UtcNow);
            var f = new FinTsProductionCandidateFactory(known, registration, Procedures(context), Selection(context));
            foreach (bool closeFails in new[] { false, true })
            {
                using var transport = new FinTsSimulatedTransport();
                transport.EnqueueResponse(Bytes(vector.GetProperty("initialization"), "responseBase64"));
                if (closeFails) transport.EnqueueFailure(new FinTsTransportException(FinTsTransportIssue.Timeout, "Close unavailable", new Uri("https://bank.example/fints")));
                else
                {
                    // Close immediately after initialization: the bank echoes message 2 rather than the normal operation/close message 3.
                    var normal = FinTsReadExecutionPipeline.ParseWireResponse(Bytes(vector.GetProperty("closing"), "responseBase64"));
                    string wire = Encoding.Latin1.GetString(normal.Frame.Syntax.CopyWireBytes()).Replace("+3+SYNTHETIC:3'", "+2+SYNTHETIC:2'").Replace("+3'", "+2'");
                    transport.EnqueueResponse(Encoding.Latin1.GetBytes(wire));
                }
                FinTsDialogueCleanupOutcome cleanup = FinTsDialogueCleanupOutcome.NotRequired;
                var options = new FinTsReadExecutionOptions
                {
                    InitializationCandidateFactory = _ => f.CreateInitialization(),
                    // Deliberately no operation factory: accepted initialization still needs a bounded close.
                    ClosingCandidateFactory = a => f.CreateClosing(a),
                    CleanupObserver = o => cleanup = o
                };
                var ledger = Ledger();
                var result = FinTsReadExecutionPipeline.ExecuteAsync(known, transport, pin, ledger, [], options).AsTask().GetAwaiter().GetResult();
                check(result.Status == FinTsReadExecutionStatus.RejectedMissingCandidate && transport.RecordedTransmissions.Count == 2,
                    "Missing operation builder preserves the primary failure and makes one cleanup attempt only after accepted initialization.");
                check(cleanup == (closeFails ? FinTsDialogueCleanupOutcome.Failed : FinTsDialogueCleanupOutcome.Closed),
                    "Cleanup records closure/failure without replacing the primary result.");
            }
        }

        void Reject(Action action, FinTsCandidateFailure code, string message)
        {
            try { action(); check(false, message); }
            catch (FinTsCandidateException ex) { check(ex.Failure == code, message); }
        }
    }

    private static JsonDocument Load(string name) => JsonDocument.Parse(Assembly.GetExecutingAssembly().GetManifestResourceStream(name)!);
    private static byte[] Bytes(JsonElement element, string property) => Convert.FromBase64String(element.GetProperty(property).GetString()!);
    private static FinTsPinTanSignatureSelection Selection(JsonElement context) => new(context.GetProperty("profileVersion").GetInt32(), context.GetProperty("securityFunction").GetInt32(), context.GetProperty("tanSegmentVersion").GetInt32());
    private static FinTsPinTanProcedureContext Procedures(JsonElement context)
    {
        var origin = FinTsUnsignedInitializationRequest.Parse(FinTsMessageFrame.Parse(Bytes(context, "originRequestBase64")));
        var response = FinTsResponse.Parse(FinTsMessageFrame.Parse(Bytes(context, "procedureResponseBase64")));
        return new(origin, FinTsTanParameterSet.Parse(FinTsParameterSet.Parse(response)), FinTsPermittedProcedureSet.Parse(response));
    }
    private static FinTsSessionCredential Pin() => FinTsSessionCredential.CaptureAndClear("PUBLIC-PIN"u8.ToArray(), FinTsCredentialKind.Pin, TimeSpan.FromMinutes(2));
    private static BankConnection Connection(string user = "PUBLIC-USER") => BankConnection.Create(new ConnectionId(2), new InstitutionId(1),
        ManualInstitutionConfiguration.Create("Bank", "https://bank.example/fints", "PUBLIC-BANK").ConfirmEndpoint("https://bank.example/fints"), user, "PUBLIC-CUSTOMER");
    private static LocalIdentityLedger Ledger()
    {
        LocalIdentityLedger.TryOpen(IdentitySnapshot.Empty(), out var ledger);
        using var batch = ledger!.BeginBatch();
        var bank = batch.Allocate(LocalEntityKind.Institution);
        batch.Allocate(LocalEntityKind.Connection, [new(bank, LocalEntityKind.Institution)]);
        batch.Commit(); return ledger;
    }
}
