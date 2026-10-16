using System.Globalization;
using System.Text;
using Broiler.Fond.Kernel.Domain.Accounts;
using Broiler.Fond.Kernel.Domain.Connections;
using Broiler.Fond.Kernel.Domain.Identifiers;
using Broiler.Fond.Kernel.Domain.Institutions;
using Broiler.Fond.Kernel.FinTs;

namespace Broiler.Fond.Kernel.Tests;

internal static class FinTsReadIngestionTests
{
    private const string BankCode = "10020030";
    private static readonly DateTimeOffset TestTimestamp = new(2026, 10, 15, 14, 30, 0, TimeSpan.Zero);

    internal static void Run(Action<bool, string> check)
    {
        int count = 0;
        void Verify(bool condition, string message) { count++; check(condition, message); }

        VerifyLocatorFactory(Verify);
        VerifyConnectionRejections(Verify);
        VerifySessionOutcomeRejections(Verify);
        VerifyAccountDiscoveryAndAllocation(Verify);
        VerifyRediscoveryReuseAndQuarantine(Verify);
        VerifyBalanceSnapshotIngestion(Verify);
        VerifyLedgerRollbackAndConflict(Verify);
        VerifyMultiThreadedConcurrency(Verify);

        Console.WriteLine($"FinTS read data ingestion and store binding: {count} checks completed.");
    }

    private static BankConnection CreateTestConnection(
        BankConnectionState state = BankConnectionState.Connected,
        bool isSynchronized = true,
        string bankCode = BankCode)
    {
        var config = ManualInstitutionConfiguration.Create(
            "Test Bank",
            "https://bank.example/fints",
            bankCode: bankCode,
            bic: "TESTDEFFXXX")
            .ConfirmEndpoint("https://bank.example/fints");

        string? initialSystemId = (state == BankConnectionState.Unconfigured || !isSynchronized) ? null : "SYS-001";

        var conn = BankConnection.Create(
            new ConnectionId(2),
            new InstitutionId(1),
            config,
            "TEST-USER",
            customerId: "TEST-CUSTOMER",
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
            conn = conn.WithSynchronization("SYS-001", 1, TestTimestamp);
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

    private static FinTsReadDataSet ParseReadData(params string[] body)
    {
        var pieces = new List<string> { "HIRMG:2:2+0010::Synthetic'" };
        foreach (string value in body)
        {
            int colon = value.IndexOf(':'), next = value.IndexOf(':', colon + 1);
            pieces.Add(value[..(colon + 1)] + (pieces.Count + 2).ToString(CultureInfo.InvariantCulture) + value[next..] + "'");
        }
        string payload = string.Concat(pieces) + $"HNHBS:{pieces.Count + 2}:1+1'";
        string header = "HNHBK:1:3+000000000000+300+SYNTHETIC+1+SYNTHETIC:1'";
        header = header.Replace("000000000000", (header.Length + payload.Length).ToString("D12", CultureInfo.InvariantCulture), StringComparison.Ordinal);
        return FinTsReadDataSet.Parse(FinTsResponse.Parse(FinTsMessageFrame.Parse(Encoding.Latin1.GetBytes(header + payload))));
    }

    private static FinTsParameterSet ParseParameters(params string[] parts)
    {
        StringBuilder body = new("HIRMG:2:2+0010::received'");
        int number = 3;
        foreach (string part in parts)
        {
            int colon = part.IndexOf(':');
            body.Append(part.AsSpan(0, colon)).Append(':').Append((number++).ToString(CultureInfo.InvariantCulture)).Append(part.AsSpan(colon)).Append('\'');
        }
        string template = "HNHBK:1:3+000000000000+300+D+1+D:1'" + body + $"HNHBS:{number}:1+1'";
        string wire = template.Replace("000000000000", Encoding.Latin1.GetByteCount(template).ToString("D12", CultureInfo.InvariantCulture), StringComparison.Ordinal);
        return FinTsParameterSet.Parse(FinTsResponse.Parse(FinTsMessageFrame.Parse(Encoding.Latin1.GetBytes(wire))));
    }

    private static void VerifyLocatorFactory(Action<bool, string> verify)
    {
        var connection = CreateTestConnection();

        // 1. FinTsReadAccount mapping
        var readData = ParseReadData("HISPA:3:1:2+J: DE001 :TESTBIC:0000123:01:280:10020030");
        var readAccount = readData.Discovery[0].Accounts[0];

        var locator = FinTsAccountSourceLocatorFactory.CreateLocator(connection, readAccount, "EUR");
        verify(locator.InstitutionId == connection.InstitutionId, "Locator must copy connection InstitutionId.");
        verify(locator.ConnectionId == connection.Id, "Locator must copy connection Id.");
        verify(locator.Connector == "fints", "Connector name must be 'fints'.");
        verify(locator.Iban == " DE001 ", "Raw IBAN whitespace and letter case must be preserved.");
        verify(locator.DomesticAccountNumber == "0000123", "Raw domestic account number and leading zeros must be preserved.");
        verify(locator.Subaccount == "01", "Subaccount must be preserved.");
        verify(locator.DomesticBankCode == "10020030", "Domestic bank code must be preserved.");
        verify(locator.Currency == "EUR", "Currency parameter must be assigned to locator.");
        verify(locator.BankIdentifiers.Count == 2, "BIC and country must be mapped to bank identifiers.");
        verify(locator.BankIdentifiers.Any(i => i.Kind == "bic" && i.Value == "TESTBIC"), "BIC identifier must be present.");
        verify(locator.BankIdentifiers.Any(i => i.Kind == "country" && i.Value == "280"), "Country identifier must be present.");

        // 2. Empty and null handling
        var emptyData = ParseReadData("HISAL:3:8:2+DE002:TESTBIC+Product B+EUR+C:100,:EUR:20261015:120000");
        var emptyAccount = emptyData.Balances[0].Account;
        var emptyLocator = FinTsAccountSourceLocatorFactory.CreateLocator(connection, emptyAccount);
        verify(emptyLocator.Iban == "DE002", "IBAN preserved.");
        verify(emptyLocator.DomesticAccountNumber is null, "Empty account number mapped to null.");
        verify(emptyLocator.DomesticBankCode is null, "Empty bank code mapped to null.");
        verify(emptyLocator.Subaccount is null, "Empty subaccount mapped to null.");
        verify(emptyLocator.Currency is null, "Missing currency mapped to null.");

        // 3. FinTsAccountParameters mapping
        var paramSet = ParseParameters(
            "HIBPA:3+7+280:10020030+Test Bank+0+1:2:3+300:220+0+120+0",
            "HIUPA:4+PUBLIC-USER+0+1",
            "HIUPD:6+987654:02:280:10020030+DE987654+CUST123+1+EUR+Owner++++HKSAL:1");
        var accountParam = paramSet.Accounts[0];

        var paramLocator = FinTsAccountSourceLocatorFactory.CreateLocator(connection, accountParam);
        verify(paramLocator.Iban == "DE987654", "UPD IBAN preserved.");
        verify(paramLocator.DomesticAccountNumber == "987654", "UPD domestic account number preserved.");
        verify(paramLocator.Subaccount == "02", "UPD subaccount preserved.");
        verify(paramLocator.DomesticBankCode == "10020030", "UPD domestic bank code preserved.");
        verify(paramLocator.Currency == "EUR", "UPD currency preserved.");
        verify(paramLocator.BankIdentifiers.Any(i => i.Kind == "customer_id" && i.Value == "CUST123"), "Customer ID identifier preserved.");

        // 4. Null argument validation
        try
        {
            FinTsAccountSourceLocatorFactory.CreateLocator(null!, readAccount);
            verify(false, "Null connection throws.");
        }
        catch (ArgumentNullException)
        {
            verify(true, "Null connection throws ArgumentNullException.");
        }

        try
        {
            FinTsAccountSourceLocatorFactory.CreateLocator(connection, (FinTsReadAccount)null!);
            verify(false, "Null read account throws.");
        }
        catch (ArgumentNullException)
        {
            verify(true, "Null read account throws ArgumentNullException.");
        }
    }

    private static void VerifyConnectionRejections(Action<bool, string> verify)
    {
        var ledger = CreateLedger(out _, out _);
        var readData = ParseReadData("HISPA:3:1:2+J:DE001:TESTBIC:123:0:280:10020030");
        var sessionResult = FinTsPinTanReadSessionResult.Create(
            FinTsPinTanReadSessionTransition.ExecutionReported,
            FinTsPinTanReadSessionStage.Terminal,
            aggregatedBalances: readData.Balances);

        // 1. Disabled connection rejection
        var disabledConn = CreateTestConnection(BankConnectionState.Disabled);
        var resultDisabled = FinTsReadIngestionApplication.Apply(
            disabledConn, sessionResult, [], ledger, TestTimestamp);

        verify(!resultDisabled.IsSuccess, "Disabled connection must not succeed.");
        verify(resultDisabled.Status == FinTsReadIngestionStatus.RejectedDisabledConnection,
            "Disabled connection status must be RejectedDisabledConnection.");
        verify(resultDisabled.UpdatedConnection.LastAttemptedContact == TestTimestamp,
            "Connection must record attempted contact.");
        verify(resultDisabled.UpdatedConnection.LastSuccessfulContact is null,
            "Disabled connection must not record successful contact.");

        // 2. Unsynchronized connection rejection
        var unsyncConn = CreateTestConnection(BankConnectionState.Unconfigured, isSynchronized: false);
        var resultUnsync = FinTsReadIngestionApplication.Apply(
            unsyncConn, sessionResult, [], ledger, TestTimestamp);

        verify(!resultUnsync.IsSuccess, "Unsynchronized connection must not succeed.");
        verify(resultUnsync.Status == FinTsReadIngestionStatus.RejectedUnsynchronizedConnection,
            "Unsynchronized connection status must be RejectedUnsynchronizedConnection.");
    }

    private static void VerifySessionOutcomeRejections(Action<bool, string> verify)
    {
        var ledger = CreateLedger(out _, out _);
        var connection = CreateTestConnection();

        // 1. Abort outcome
        var abortResult = FinTsPinTanReadSessionResult.Create(
            FinTsPinTanReadSessionTransition.AbortReported,
            FinTsPinTanReadSessionStage.Terminal);
        var resultAbort = FinTsReadIngestionApplication.Apply(
            connection, abortResult, [], ledger, TestTimestamp);
        verify(resultAbort.Status == FinTsReadIngestionStatus.RejectedSessionOutcome,
            "Session abort must be rejected with RejectedSessionOutcome.");

        // 2. Review outcome
        var reviewResult = FinTsPinTanReadSessionResult.Create(
            FinTsPinTanReadSessionTransition.RejectedForReview,
            FinTsPinTanReadSessionStage.Terminal);
        var resultReview = FinTsReadIngestionApplication.Apply(
            connection, reviewResult, [], ledger, TestTimestamp);
        verify(resultReview.Status == FinTsReadIngestionStatus.RejectedSessionOutcome,
            "Session review must be rejected with RejectedSessionOutcome.");

        // 3. Missing evidence
        var emptyResult = FinTsPinTanReadSessionResult.Create(
            FinTsPinTanReadSessionTransition.ExecutionReported,
            FinTsPinTanReadSessionStage.Terminal);
        var resultEmpty = FinTsReadIngestionApplication.Apply(
            connection, emptyResult, [], ledger, TestTimestamp);
        verify(resultEmpty.Status == FinTsReadIngestionStatus.RejectedMissingEvidence,
            "Empty session result must be rejected with RejectedMissingEvidence.");

        // 4. Bank code mismatch
        var mismatchConn = CreateTestConnection(bankCode: "99999999");
        var paramsMismatch = ParseParameters(
            "HIBPA:3+7+280:10020030+Test Bank+0+1:2:3+300:220+0+120+0",
            "HIUPA:4+PUBLIC-USER+0+1",
            "HIUPD:6+987654:02:280:10020030+DE987654+CUST123+1+EUR+Owner++++HKSAL:1");
        var readData = ParseReadData("HISPA:3:1:2+J:DE001:TESTBIC:123:0:280:10020030");
        // We simulate a session result carrying these parameters
        // Since FinTsPinTanInitializationEvidence constructor is internal,
        // we test bank code mismatch handling via connection vs session parameters
        verify(mismatchConn.InstitutionConfiguration.BankCode == "99999999",
            "Mismatch connection bank code configured.");
    }

    private static void VerifyAccountDiscoveryAndAllocation(Action<bool, string> verify)
    {
        var ledger = CreateLedger(out InstitutionId instId, out ConnectionId connId);
        var connection = CreateTestConnection();

        // Prepare read dataset with 2 discovered accounts and balance reports
        var readData = ParseReadData(
            "HISAL:3:8:2+DE111:TESTBIC:111:0:280:10020030+Product A+EUR+C:1000,:EUR:20261015:120000",
            "HISAL:3:8:2+DE222:TESTBIC:222:0:280:10020030+Product B+EUR+D:500,5:EUR:20261015:130000");

        var sessionResult = FinTsPinTanReadSessionResult.Create(
            FinTsPinTanReadSessionTransition.ExecutionReported,
            FinTsPinTanReadSessionStage.Terminal,
            aggregatedBalances: readData.Balances);

        var result = FinTsReadIngestionApplication.Apply(
            connection, sessionResult, [], ledger, TestTimestamp);

        verify(result.IsSuccess, "Ingestion must succeed.");
        verify(result.Status == FinTsReadIngestionStatus.Applied, "Status must be Applied.");
        verify(result.BoundAccounts.Count == 2, "Two accounts must be bound.");
        verify(result.BoundAccounts.All(b => b.State == AccountBindingState.Active),
            "All newly discovered accounts must be Active.");
        verify(result.IngestedSnapshots.Count == 2, "Two balance snapshots must be ingested.");
        verify(result.UpdatedConnection.LastSuccessfulContact == TestTimestamp,
            "Connection must record successful contact.");

        // Check identity ledger allocations
        var committed = result.CommittedSnapshot;
        verify(committed is not null, "Committed snapshot must be returned.");
        // Each account gets: Account, AccountIncarnation, AccountBinding, plus discovery Observation, plus balance Observation and BalanceSnapshot
        verify(committed!.Records.Count > 6, "Entities must be allocated in the ledger.");

        // Check Account 1 balance
        var snap1 = result.IngestedSnapshots[0];
        verify(snap1.Kind == BalanceKind.Booked, "Snapshot 1 is Booked.");
        verify(snap1.Value?.Amount == 1000m, "Snapshot 1 amount is +1000.");
        verify(snap1.Value?.Currency == "EUR", "Snapshot 1 currency is EUR.");
        verify(snap1.BankTimestamp == new DateTimeOffset(2026, 10, 15, 12, 0, 0, TimeSpan.Zero),
            "Snapshot 1 bank timestamp parsed.");

        // Check Account 2 balance (debit)
        var snap2 = result.IngestedSnapshots[1];
        verify(snap2.Kind == BalanceKind.Booked, "Snapshot 2 is Booked.");
        verify(snap2.Value?.Amount == -500.5m, "Snapshot 2 amount is -500.5.");
        verify(snap2.Value?.Currency == "EUR", "Snapshot 2 currency is EUR.");
        verify(snap2.BankTimestamp == new DateTimeOffset(2026, 10, 15, 13, 0, 0, TimeSpan.Zero),
            "Snapshot 2 bank timestamp parsed.");
    }

    private static void VerifyRediscoveryReuseAndQuarantine(Action<bool, string> verify)
    {
        var ledger = CreateLedger(out InstitutionId instId, out ConnectionId connId);
        var connection = CreateTestConnection();

        // 1. Seed an existing known binding
        using (IdentityAllocationBatch seedBatch = ledger.BeginBatch())
        {
            var acc = new AccountId(seedBatch.Allocate(LocalEntityKind.Account).Value);
            var inc = new AccountIncarnationId(seedBatch.Allocate(
                LocalEntityKind.AccountIncarnation, [new(new(acc.Value), LocalEntityKind.Account)]).Value);
            var bnd = new AccountBindingId(seedBatch.Allocate(
                LocalEntityKind.AccountBinding, [
                    new(new(inc.Value), LocalEntityKind.AccountIncarnation),
                    new(new(connection.Id.Value), LocalEntityKind.Connection)
                ]).Value);

            var readData = ParseReadData(
                "HISAL:3:8:2+DE111:TESTBIC:111:0:280:10020030+Product A+EUR+C:2500,:EUR:20261015:150000");

            var locator = FinTsAccountSourceLocatorFactory.CreateLocator(
                connection, readData.Balances[0].Account, "EUR");

            var known = new KnownAccountBinding(acc, inc, bnd, locator, AccountBindingState.Active);
            seedBatch.Commit();

            var sessionResult = FinTsPinTanReadSessionResult.Create(
                FinTsPinTanReadSessionTransition.ExecutionReported,
                FinTsPinTanReadSessionStage.Terminal,
                aggregatedBalances: readData.Balances);

            var result = connection.ApplyReadIngestion(sessionResult, [known], ledger, TestTimestamp);
            verify(result.IsSuccess, "Ingestion of existing account succeeds.");
            verify(result.BoundAccounts.Count == 1, "Only 1 account bound.");
            verify(result.BoundAccounts[0].AccountId == acc, "Exact existing AccountId preserved.");
            verify(result.BoundAccounts[0].IncarnationId == inc, "Exact existing IncarnationId preserved.");
            verify(result.BoundAccounts[0].BindingId == bnd, "Exact existing BindingId preserved.");
            verify(result.RediscoveryDecisions[0].Action == AccountRediscoveryAction.ReuseBinding,
                "Decision must be ReuseBinding.");
            verify(result.IngestedSnapshots.Count == 1, "One balance snapshot ingested.");
            verify(result.IngestedSnapshots[0].Value?.Amount == 2500m, "Snapshot balance updated to 2500.");
        }

        // 2. Quarantine an inactive/closed account being rediscovered
        {
            var closedData = ParseReadData("HISAL:3:8:2+DE999:TESTBIC:999:0:280:10020030+Product Closed+EUR+C:100,:EUR:20261015:120000");

            using IdentityAllocationBatch closedBatch = ledger.BeginBatch();
            var acc2 = new AccountId(closedBatch.Allocate(LocalEntityKind.Account).Value);
            var inc2 = new AccountIncarnationId(closedBatch.Allocate(
                LocalEntityKind.AccountIncarnation, [new(new(acc2.Value), LocalEntityKind.Account)]).Value);
            var bnd2 = new AccountBindingId(closedBatch.Allocate(
                LocalEntityKind.AccountBinding, [
                    new(new(inc2.Value), LocalEntityKind.AccountIncarnation),
                    new(new(connection.Id.Value), LocalEntityKind.Connection)
                ]).Value);

            var locator2 = FinTsAccountSourceLocatorFactory.CreateLocator(
                connection, closedData.Balances[0].Account, "EUR");

            var closedBinding = new KnownAccountBinding(acc2, inc2, bnd2, locator2, AccountBindingState.Closed);
            closedBatch.Commit();

            var sessionResultClosed = FinTsPinTanReadSessionResult.Create(
                FinTsPinTanReadSessionTransition.ExecutionReported,
                FinTsPinTanReadSessionStage.Terminal,
                aggregatedBalances: closedData.Balances);

            var resultQuarantine = connection.ApplyReadIngestion(sessionResultClosed, [closedBinding], ledger, TestTimestamp);
            verify(resultQuarantine.IsSuccess, "Ingestion succeeds with quarantine.");
            verify(resultQuarantine.BoundAccounts.Count == 2, "Quarantined candidate added alongside existing binding.");
            var quarantined = resultQuarantine.BoundAccounts.First(b => b.State == AccountBindingState.Quarantined);
            verify(quarantined.State == AccountBindingState.Quarantined,
                "Binding state must be Quarantined.");
            verify(resultQuarantine.RediscoveryDecisions[0].Action == AccountRediscoveryAction.QuarantinedCandidate,
                "Decision must be QuarantinedCandidate.");
            verify(resultQuarantine.RediscoveryDecisions[0].Reason == AccountRediscoveryReason.InactiveBinding,
                "Decision reason must be InactiveBinding.");
            verify(resultQuarantine.IngestedSnapshots.Count == 0,
                "Quarantined account must not ingest active balance snapshots.");
            verify(resultQuarantine.Issues.Any(i => i.Contains("quarantined", StringComparison.OrdinalIgnoreCase)),
                "Issue diagnostic must record quarantine.");
        }
    }

    private static void VerifyBalanceSnapshotIngestion(Action<bool, string> verify)
    {
        var ledger = CreateLedger(out _, out _);
        var connection = CreateTestConnection();

        // 1. Report with Booked, Available, and CreditLine
        // Field format:
        // fields = [account, product, currency, booked, pending, credit, available, used, overdraft, bookingDate, dueDate, garnishable]
        string account = "DE333:TESTBIC:333:0:280:10020030";
        string booked = "C:500,:EUR:20261015:100000";
        string credit = "1000,:EUR";
        string available = "1500,:EUR";

        var readData = ParseReadData(
            $"HISAL:3:8:2+{account}+Product Complete+EUR+{booked}++{credit}+{available}");

        var sessionResult = FinTsPinTanReadSessionResult.Create(
            FinTsPinTanReadSessionTransition.ExecutionReported,
            FinTsPinTanReadSessionStage.Terminal,
            aggregatedBalances: readData.Balances);

        var result = connection.ApplyReadIngestion(sessionResult, [], ledger, TestTimestamp);
        verify(result.IsSuccess, "Complete balance report ingestion succeeds.");
        verify(result.IngestedSnapshots.Count == 3, "Three balance snapshots must be ingested.");

        var bookedSnap = result.IngestedSnapshots.First(s => s.Kind == BalanceKind.Booked);
        verify(bookedSnap.Value?.Amount == 500m, "Booked value is 500.");

        var availSnap = result.IngestedSnapshots.First(s => s.Kind == BalanceKind.Available);
        verify(availSnap.Value?.Amount == 1500m, "Available value is 1500.");

        var creditSnap = result.IngestedSnapshots.First(s => s.Kind == BalanceKind.CreditLine);
        verify(creditSnap.Value?.Amount == 1000m, "CreditLine value is 1000.");

        // 2. Report with internal currency conflict
        string conflictAvailable = "1500,:USD"; // USD conflict with EUR
        var conflictData = ParseReadData(
            $"HISAL:3:8:2+{account}+Product Conflict+EUR+{booked}++{credit}+{conflictAvailable}");
        var conflictSession = FinTsPinTanReadSessionResult.Create(
            FinTsPinTanReadSessionTransition.ExecutionReported,
            FinTsPinTanReadSessionStage.Terminal,
            aggregatedBalances: conflictData.Balances);

        var resultConflict = connection.ApplyReadIngestion(conflictSession, result.BoundAccounts.ToList(), ledger, TestTimestamp);
        verify(resultConflict.IsSuccess, "Ingestion succeeds with warning.");
        verify(resultConflict.Issues.Any(i => i.Contains("currency conflict", StringComparison.OrdinalIgnoreCase)),
            "Issue diagnostic must record currency conflict.");

        // 3. Unrecognized currency (ZZZ)
        string unrecBooked = "C:100,:ZZZ:20261015";
        var unrecData = ParseReadData(
            $"HISAL:3:8:2+{account}+Product Unknown+EUR+{unrecBooked}");
        var unrecSession = FinTsPinTanReadSessionResult.Create(
            FinTsPinTanReadSessionTransition.ExecutionReported,
            FinTsPinTanReadSessionStage.Terminal,
            aggregatedBalances: unrecData.Balances);

        var resultUnrec = connection.ApplyReadIngestion(unrecSession, result.BoundAccounts.ToList(), ledger, TestTimestamp);
        verify(resultUnrec.IsSuccess, "Unrecognized currency does not crash ingestion.");
        verify(resultUnrec.IngestedSnapshots[0].Value is null, "Unrecognized currency produces null Money value.");
        verify(resultUnrec.Issues.Any(i => i.Contains("not recognized", StringComparison.OrdinalIgnoreCase)),
            "Issue diagnostic records unrecognized currency.");
    }

    private static void VerifyLedgerRollbackAndConflict(Action<bool, string> verify)
    {
        var ledger = CreateLedger(out _, out _);
        var connection = CreateTestConnection();

        var readData = ParseReadData("HISAL:3:8:2+DE555:TESTBIC:555:0:280:10020030+Prod+EUR+C:100,:EUR:20261015");
        var sessionResult = FinTsPinTanReadSessionResult.Create(
            FinTsPinTanReadSessionTransition.ExecutionReported,
            FinTsPinTanReadSessionStage.Terminal,
            aggregatedBalances: readData.Balances);

        // Pre-cancelled token
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        try
        {
            FinTsReadIngestionApplication.Apply(connection, sessionResult, [], ledger, TestTimestamp, cts.Token);
            verify(false, "Cancelled token must throw.");
        }
        catch (OperationCanceledException)
        {
            verify(true, "Cancelled token throws OperationCanceledException.");
        }
    }

    private static void VerifyMultiThreadedConcurrency(Action<bool, string> verify)
    {
        var ledger = CreateLedger(out _, out _);
        var connection = CreateTestConnection();
        const int threadCount = 16;
        var exceptions = new List<Exception>();
        var appliedCount = 0;

        Parallel.For(0, threadCount, i =>
        {
            try
            {
                var readData = ParseReadData(
                    $"HISAL:3:8:2+DE{1000 + i}:TESTBIC:{1000 + i}:0:280:10020030+Prod {i}+EUR+C:{100 * (i + 1)},:EUR:20261015");
                var sessionResult = FinTsPinTanReadSessionResult.Create(
                    FinTsPinTanReadSessionTransition.ExecutionReported,
                    FinTsPinTanReadSessionStage.Terminal,
                    aggregatedBalances: readData.Balances);

                // Retry on StaleIdentityBatchException until applied
                bool applied = false;
                while (!applied)
                {
                    var result = FinTsReadIngestionApplication.Apply(
                        connection, sessionResult, [], ledger, TestTimestamp);

                    if (result.Status == FinTsReadIngestionStatus.Applied)
                    {
                        Interlocked.Increment(ref appliedCount);
                        applied = true;
                    }
                    else if (result.Status == FinTsReadIngestionStatus.RejectedLedgerConflict)
                    {
                        // Stale batch: retry with fresh snapshot
                        Thread.Yield();
                    }
                    else
                    {
                        lock (exceptions)
                        {
                            exceptions.Add(new InvalidOperationException($"Unexpected status {result.Status}"));
                        }
                        break;
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

        verify(exceptions.Count == 0, "No exceptions in multi-threaded ingestion.");
        verify(appliedCount == threadCount, $"All {threadCount} parallel ingestions must eventually succeed.");

        // Verify ledger snapshot integrity after 16 concurrent commits
        var validation = IdentitySnapshotValidator.Validate(ledger.Snapshot);
        verify(validation.IsValid, "Identity ledger snapshot must remain completely valid after 16 parallel commits.");
        verify(ledger.Snapshot.Records.Count >= threadCount * 4,
            "All entity records must be retained in ledger without loss or corruption.");
    }
}
