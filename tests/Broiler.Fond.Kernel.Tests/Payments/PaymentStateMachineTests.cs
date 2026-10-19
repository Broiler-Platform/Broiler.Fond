using Broiler.Fond.Kernel.Domain;
using Broiler.Fond.Kernel.Domain.Payments;

namespace Broiler.Fond.Kernel.Tests.Payments;

public static class PaymentStateMachineTests
{
    public static void RunAll()
    {
        int checkCount = 0;
        Action<bool, string> check = (condition, message) =>
        {
            if (!condition)
            {
                throw new InvalidOperationException($"Payment state machine assertion failed: {message}");
            }
            checkCount++;
        };

        TestAllValidTransitions(check);
        TestInvalidTransitions(check);
        TestTerminalStates(check);
        TestVoPStatusMapping(check);
        TestPaymentAttemptPreSendCommitAndInvariants(check);
        TestPaymentStateTransitionAuditTrail(check);
        TestPaymentDuplicateGuard(check);

        Console.WriteLine($"Payment state machine and attempt allocation (M4-03): {checkCount} checks completed successfully.");
    }

    private static void TestAllValidTransitions(Action<bool, string> check)
    {
        // 1. Draft -> Validated
        check(PaymentStateMachine.CanTransition(PaymentState.Draft, PaymentState.Validated), "Draft -> Validated is allowed.");

        // 2. Validated -> PayeeCheckPending, PayeeCheckNotApplicable
        check(PaymentStateMachine.CanTransition(PaymentState.Validated, PaymentState.PayeeCheckPending), "Validated -> PayeeCheckPending is allowed.");
        check(PaymentStateMachine.CanTransition(PaymentState.Validated, PaymentState.PayeeCheckNotApplicable), "Validated -> PayeeCheckNotApplicable is allowed.");

        // 3. PayeeCheckPending -> PayeeMatch, PayeeCloseMatch, PayeeNoMatch, PayeeCheckUnavailable, Rejected
        check(PaymentStateMachine.CanTransition(PaymentState.PayeeCheckPending, PaymentState.PayeeMatch), "PayeeCheckPending -> PayeeMatch is allowed.");
        check(PaymentStateMachine.CanTransition(PaymentState.PayeeCheckPending, PaymentState.PayeeCloseMatch), "PayeeCheckPending -> PayeeCloseMatch is allowed.");
        check(PaymentStateMachine.CanTransition(PaymentState.PayeeCheckPending, PaymentState.PayeeNoMatch), "PayeeCheckPending -> PayeeNoMatch is allowed.");
        check(PaymentStateMachine.CanTransition(PaymentState.PayeeCheckPending, PaymentState.PayeeCheckUnavailable), "PayeeCheckPending -> PayeeCheckUnavailable is allowed.");
        check(PaymentStateMachine.CanTransition(PaymentState.PayeeCheckPending, PaymentState.Rejected), "PayeeCheckPending -> Rejected is allowed.");

        // 4. PayeeMatch -> AwaitingAuthorization
        check(PaymentStateMachine.CanTransition(PaymentState.PayeeMatch, PaymentState.AwaitingAuthorization), "PayeeMatch -> AwaitingAuthorization is allowed.");

        // 5. PayeeCloseMatch -> AwaitingAuthorization, AuthorizationCancelled
        check(PaymentStateMachine.CanTransition(PaymentState.PayeeCloseMatch, PaymentState.AwaitingAuthorization), "PayeeCloseMatch -> AwaitingAuthorization is allowed.");
        check(PaymentStateMachine.CanTransition(PaymentState.PayeeCloseMatch, PaymentState.AuthorizationCancelled), "PayeeCloseMatch -> AuthorizationCancelled is allowed.");

        // 6. PayeeNoMatch -> AwaitingAuthorization, AuthorizationCancelled
        check(PaymentStateMachine.CanTransition(PaymentState.PayeeNoMatch, PaymentState.AwaitingAuthorization), "PayeeNoMatch -> AwaitingAuthorization is allowed.");
        check(PaymentStateMachine.CanTransition(PaymentState.PayeeNoMatch, PaymentState.AuthorizationCancelled), "PayeeNoMatch -> AuthorizationCancelled is allowed.");

        // 7. PayeeCheckNotApplicable -> AwaitingAuthorization
        check(PaymentStateMachine.CanTransition(PaymentState.PayeeCheckNotApplicable, PaymentState.AwaitingAuthorization), "PayeeCheckNotApplicable -> AwaitingAuthorization is allowed.");

        // 8. PayeeCheckUnavailable -> AwaitingAuthorization, AuthorizationCancelled, Rejected
        check(PaymentStateMachine.CanTransition(PaymentState.PayeeCheckUnavailable, PaymentState.AwaitingAuthorization), "PayeeCheckUnavailable -> AwaitingAuthorization is allowed.");
        check(PaymentStateMachine.CanTransition(PaymentState.PayeeCheckUnavailable, PaymentState.AuthorizationCancelled), "PayeeCheckUnavailable -> AuthorizationCancelled is allowed.");
        check(PaymentStateMachine.CanTransition(PaymentState.PayeeCheckUnavailable, PaymentState.Rejected), "PayeeCheckUnavailable -> Rejected is allowed.");

        // 9. AwaitingAuthorization -> Submitting, AuthorizationCancelled, Rejected
        check(PaymentStateMachine.CanTransition(PaymentState.AwaitingAuthorization, PaymentState.Submitting), "AwaitingAuthorization -> Submitting is allowed.");
        check(PaymentStateMachine.CanTransition(PaymentState.AwaitingAuthorization, PaymentState.AuthorizationCancelled), "AwaitingAuthorization -> AuthorizationCancelled is allowed.");
        check(PaymentStateMachine.CanTransition(PaymentState.AwaitingAuthorization, PaymentState.Rejected), "AwaitingAuthorization -> Rejected is allowed.");

        // 10. Submitting -> Accepted, BankPending, Rejected, Unknown
        check(PaymentStateMachine.CanTransition(PaymentState.Submitting, PaymentState.Accepted), "Submitting -> Accepted is allowed.");
        check(PaymentStateMachine.CanTransition(PaymentState.Submitting, PaymentState.BankPending), "Submitting -> BankPending is allowed.");
        check(PaymentStateMachine.CanTransition(PaymentState.Submitting, PaymentState.Rejected), "Submitting -> Rejected is allowed.");
        check(PaymentStateMachine.CanTransition(PaymentState.Submitting, PaymentState.Unknown), "Submitting -> Unknown is allowed.");

        // 11. Accepted -> BankPending, Reconciled
        check(PaymentStateMachine.CanTransition(PaymentState.Accepted, PaymentState.BankPending), "Accepted -> BankPending is allowed.");
        check(PaymentStateMachine.CanTransition(PaymentState.Accepted, PaymentState.Reconciled), "Accepted -> Reconciled is allowed.");

        // 12. BankPending -> Accepted, Rejected, Unknown, Reconciled
        check(PaymentStateMachine.CanTransition(PaymentState.BankPending, PaymentState.Accepted), "BankPending -> Accepted is allowed.");
        check(PaymentStateMachine.CanTransition(PaymentState.BankPending, PaymentState.Rejected), "BankPending -> Rejected is allowed.");
        check(PaymentStateMachine.CanTransition(PaymentState.BankPending, PaymentState.Unknown), "BankPending -> Unknown is allowed.");
        check(PaymentStateMachine.CanTransition(PaymentState.BankPending, PaymentState.Reconciled), "BankPending -> Reconciled is allowed.");

        // 13. Unknown -> Accepted, BankPending, Rejected, Reconciled
        check(PaymentStateMachine.CanTransition(PaymentState.Unknown, PaymentState.Accepted), "Unknown -> Accepted is allowed.");
        check(PaymentStateMachine.CanTransition(PaymentState.Unknown, PaymentState.BankPending), "Unknown -> BankPending is allowed.");
        check(PaymentStateMachine.CanTransition(PaymentState.Unknown, PaymentState.Rejected), "Unknown -> Rejected is allowed.");
        check(PaymentStateMachine.CanTransition(PaymentState.Unknown, PaymentState.Reconciled), "Unknown -> Reconciled is allowed.");

        // Ensure ValidateTransition does not throw for any valid transitions
        PaymentStateMachine.ValidateTransition(PaymentState.Draft, PaymentState.Validated);
        PaymentStateMachine.ValidateTransition(PaymentState.Validated, PaymentState.PayeeCheckPending);
        PaymentStateMachine.ValidateTransition(PaymentState.PayeeCheckPending, PaymentState.PayeeMatch);
        PaymentStateMachine.ValidateTransition(PaymentState.PayeeMatch, PaymentState.AwaitingAuthorization);
        PaymentStateMachine.ValidateTransition(PaymentState.AwaitingAuthorization, PaymentState.Submitting);
        PaymentStateMachine.ValidateTransition(PaymentState.Submitting, PaymentState.Accepted);
        PaymentStateMachine.ValidateTransition(PaymentState.Accepted, PaymentState.Reconciled);
        check(true, "ValidateTransition succeeds across normative happy path.");
    }

    private static void TestInvalidTransitions(Action<bool, string> check)
    {
        // 1. Direct jumps bypassing review or authorization
        check(!PaymentStateMachine.CanTransition(PaymentState.Draft, PaymentState.Submitting), "Draft cannot jump directly to Submitting.");
        check(!PaymentStateMachine.CanTransition(PaymentState.Draft, PaymentState.Accepted), "Draft cannot jump directly to Accepted.");
        check(!PaymentStateMachine.CanTransition(PaymentState.Validated, PaymentState.Submitting), "Validated cannot jump directly to Submitting.");
        check(!PaymentStateMachine.CanTransition(PaymentState.PayeeMatch, PaymentState.Accepted), "PayeeMatch cannot jump directly to Accepted.");
        check(!PaymentStateMachine.CanTransition(PaymentState.AwaitingAuthorization, PaymentState.Accepted), "AwaitingAuthorization must pass through Submitting.");

        // 2. Illegal backward transitions
        check(!PaymentStateMachine.CanTransition(PaymentState.Submitting, PaymentState.Draft), "Submitting cannot revert to Draft.");
        check(!PaymentStateMachine.CanTransition(PaymentState.Submitting, PaymentState.AwaitingAuthorization), "Submitting cannot revert to AwaitingAuthorization.");
        check(!PaymentStateMachine.CanTransition(PaymentState.Accepted, PaymentState.Submitting), "Accepted cannot revert to Submitting.");
        check(!PaymentStateMachine.CanTransition(PaymentState.Accepted, PaymentState.Draft), "Accepted cannot revert to Draft.");

        // 3. Terminal state outbound transitions are completely prohibited
        check(!PaymentStateMachine.CanTransition(PaymentState.AuthorizationCancelled, PaymentState.Draft), "AuthorizationCancelled cannot transition to Draft.");
        check(!PaymentStateMachine.CanTransition(PaymentState.AuthorizationCancelled, PaymentState.Submitting), "AuthorizationCancelled cannot transition to Submitting.");
        check(!PaymentStateMachine.CanTransition(PaymentState.Rejected, PaymentState.Draft), "Rejected cannot transition to Draft.");
        check(!PaymentStateMachine.CanTransition(PaymentState.Rejected, PaymentState.AwaitingAuthorization), "Rejected cannot transition to AwaitingAuthorization.");
        check(!PaymentStateMachine.CanTransition(PaymentState.Reconciled, PaymentState.Draft), "Reconciled cannot transition to Draft.");
        check(!PaymentStateMachine.CanTransition(PaymentState.Reconciled, PaymentState.Accepted), "Reconciled cannot transition to Accepted.");

        // 4. Self-transitions are not allowed
        check(!PaymentStateMachine.CanTransition(PaymentState.Draft, PaymentState.Draft), "Self-transition on Draft is prohibited.");
        check(!PaymentStateMachine.CanTransition(PaymentState.Submitting, PaymentState.Submitting), "Self-transition on Submitting is prohibited.");
        check(!PaymentStateMachine.CanTransition(PaymentState.Reconciled, PaymentState.Reconciled), "Self-transition on Reconciled is prohibited.");

        // 5. ValidateTransition throws InvalidPaymentTransitionException
        bool threw = false;
        try
        {
            PaymentStateMachine.ValidateTransition(PaymentState.Draft, PaymentState.Accepted);
        }
        catch (InvalidPaymentTransitionException ex)
        {
            threw = true;
            check(ex.FromState == PaymentState.Draft, "Exception records FromState correctly.");
            check(ex.ToState == PaymentState.Accepted, "Exception records ToState correctly.");
        }
        check(threw, "ValidateTransition threw InvalidPaymentTransitionException on illegal jump.");
    }

    private static void TestTerminalStates(Action<bool, string> check)
    {
        check(PaymentStateMachine.IsTerminal(PaymentState.AuthorizationCancelled), "AuthorizationCancelled is terminal.");
        check(PaymentStateMachine.IsTerminal(PaymentState.Rejected), "Rejected is terminal.");
        check(PaymentStateMachine.IsTerminal(PaymentState.Reconciled), "Reconciled is terminal.");

        check(!PaymentStateMachine.IsTerminal(PaymentState.Draft), "Draft is non-terminal.");
        check(!PaymentStateMachine.IsTerminal(PaymentState.Validated), "Validated is non-terminal.");
        check(!PaymentStateMachine.IsTerminal(PaymentState.PayeeCheckPending), "PayeeCheckPending is non-terminal.");
        check(!PaymentStateMachine.IsTerminal(PaymentState.PayeeMatch), "PayeeMatch is non-terminal.");
        check(!PaymentStateMachine.IsTerminal(PaymentState.PayeeCloseMatch), "PayeeCloseMatch is non-terminal.");
        check(!PaymentStateMachine.IsTerminal(PaymentState.PayeeNoMatch), "PayeeNoMatch is non-terminal.");
        check(!PaymentStateMachine.IsTerminal(PaymentState.PayeeCheckNotApplicable), "PayeeCheckNotApplicable is non-terminal.");
        check(!PaymentStateMachine.IsTerminal(PaymentState.PayeeCheckUnavailable), "PayeeCheckUnavailable is non-terminal.");
        check(!PaymentStateMachine.IsTerminal(PaymentState.AwaitingAuthorization), "AwaitingAuthorization is non-terminal.");
        check(!PaymentStateMachine.IsTerminal(PaymentState.Submitting), "Submitting is non-terminal.");
        check(!PaymentStateMachine.IsTerminal(PaymentState.Accepted), "Accepted is non-terminal.");
        check(!PaymentStateMachine.IsTerminal(PaymentState.BankPending), "BankPending is non-terminal.");
        check(!PaymentStateMachine.IsTerminal(PaymentState.Unknown), "Unknown is non-terminal (requires explicit reconciliation evidence).");

        check(PaymentStateMachine.GetAllowedNextStates(PaymentState.Reconciled).Count == 0, "Terminal Reconciled has 0 next states.");
        check(PaymentStateMachine.GetAllowedNextStates(PaymentState.Rejected).Count == 0, "Terminal Rejected has 0 next states.");
        check(PaymentStateMachine.GetAllowedNextStates(PaymentState.AuthorizationCancelled).Count == 0, "Terminal AuthorizationCancelled has 0 next states.");
        check(PaymentStateMachine.GetAllowedNextStates(PaymentState.Submitting).Count == 4, "Submitting has exactly 4 allowed next states.");
    }

    private static void TestVoPStatusMapping(Action<bool, string> check)
    {
        check(PaymentStateMachine.FromVoPStatus(VerificationOfPayeeStatus.Match) == PaymentState.PayeeMatch, "VoP Match maps to PayeeMatch.");
        check(PaymentStateMachine.FromVoPStatus(VerificationOfPayeeStatus.CloseMatch) == PaymentState.PayeeCloseMatch, "VoP CloseMatch maps to PayeeCloseMatch.");
        check(PaymentStateMachine.FromVoPStatus(VerificationOfPayeeStatus.NoMatch) == PaymentState.PayeeNoMatch, "VoP NoMatch maps to PayeeNoMatch.");
        check(PaymentStateMachine.FromVoPStatus(VerificationOfPayeeStatus.NotApplicable) == PaymentState.PayeeCheckNotApplicable, "VoP NotApplicable maps to PayeeCheckNotApplicable.");
        check(PaymentStateMachine.FromVoPStatus(VerificationOfPayeeStatus.Unavailable) == PaymentState.PayeeCheckUnavailable, "VoP Unavailable maps to PayeeCheckUnavailable.");
        check(PaymentStateMachine.FromVoPStatus(VerificationOfPayeeStatus.Pending) == PaymentState.PayeeCheckPending, "VoP Pending maps to PayeeCheckPending.");
    }

    private static void TestPaymentAttemptPreSendCommitAndInvariants(Action<bool, string> check)
    {
        DateTimeOffset now = new(2026, 10, 19, 10, 0, 0, TimeSpan.Zero);

        // 1. EndToEndId generation
        string e2e1 = PaymentAttempt.GenerateEndToEndId(now);
        string e2e2 = PaymentAttempt.GenerateEndToEndId(now);
        check(e2e1.StartsWith("BF-20261019-", StringComparison.Ordinal), "EndToEndId starts with BF-yyyyMMdd- prefix.");
        check(e2e1.Length == 28, "EndToEndId length is exactly 28 characters.");
        check(e2e1.Length <= PaymentAttempt.MaxEndToEndIdLength, "EndToEndId length is within SEPA 35-character limit.");
        check(e2e1 != e2e2, "Two generated EndToEndIds are unique.");

        // 2. Draft revision setup
        PaymentDraftRevision revision = new(
            Id: 101,
            DraftId: 1,
            RevisionNumber: 1,
            PredecessorRevisionId: null,
            Timestamp: now,
            SourceAccountId: 10,
            SourceAccountBindingId: 20,
            SourceAccountIncarnationId: 30,
            Beneficiary: new PaymentBeneficiary("Max Mustermann", "DE89370400440532013000"),
            Amount: new Money(125.50m, "EUR"),
            Remittance: new PaymentRemittance("Invoice 2026-0042"),
            ExecutionMode: PaymentExecutionMode.StandardCreditTransfer,
            ExecutionDate: new DateOnly(2026, 10, 25));

        // 3. Pre-send commit creation
        PaymentAttempt attempt = PaymentAttempt.CreatePreSendAttempt(
            id: 501,
            draftRevision: revision,
            timestamp: now,
            initialState: PaymentState.AwaitingAuthorization);

        check(attempt.Id == 501, "Attempt ID matches allocated entity ID.");
        check(attempt.DraftId == 1, "Attempt retains draft ID.");
        check(attempt.DraftRevisionId == 101, "Attempt retains draft revision ID.");
        check(attempt.SourceAccountId == 10, "Attempt retains source account ID provenance.");
        check(attempt.SourceAccountBindingId == 20, "Attempt retains source binding ID provenance.");
        check(attempt.SourceAccountIncarnationId == 30, "Attempt retains source incarnation ID provenance.");
        check(attempt.State == PaymentState.AwaitingAuthorization, "Attempt starts in AwaitingAuthorization.");
        check(attempt.CreatedAt == now, "Attempt CreatedAt is recorded.");
        check(attempt.UpdatedAt == now, "Attempt UpdatedAt is recorded.");

        // 4. Immutably attach VoP result
        DateTimeOffset vopTime = now.AddSeconds(5);
        VerificationOfPayeeResult vopResult = VerificationOfPayeeResult.CreateMatch(
            suppliedName: "Max Mustermann",
            checkedAt: vopTime,
            rawBankCode: "3070");

        PaymentAttempt attemptWithVoP = attempt.WithVoPResult(vopResult, vopTime);
        check(attemptWithVoP.VoPResult != null, "VoP result attached.");
        check(attemptWithVoP.VoPResult!.Status == VerificationOfPayeeStatus.Match, "VoP status is Match.");
        check(attemptWithVoP.UpdatedAt == vopTime, "UpdatedAt updated upon VoP attachment.");
        check(attempt.VoPResult == null, "Original attempt remained unchanged (immutability).");

        // 5. Valid transition: AwaitingAuthorization -> Submitting -> Accepted
        DateTimeOffset submitTime = now.AddSeconds(15);
        PaymentAttempt submittingAttempt = attemptWithVoP.TransitionTo(
            PaymentState.Submitting,
            submitTime,
            statusDescription: "Transmitting order to bank.");
        check(submittingAttempt.State == PaymentState.Submitting, "State updated to Submitting.");
        check(submittingAttempt.StatusDescription == "Transmitting order to bank.", "Status description updated.");

        DateTimeOffset acceptTime = now.AddSeconds(18);
        PaymentAttempt acceptedAttempt = submittingAttempt.TransitionTo(
            PaymentState.Accepted,
            acceptTime,
            bankReference: "BANK-ORDER-998877",
            responseCode: "0020",
            statusDescription: "Auftrag ausgefuehrt.");
        check(acceptedAttempt.State == PaymentState.Accepted, "State updated to Accepted.");
        check(acceptedAttempt.BankReference == "BANK-ORDER-998877", "Bank reference stored.");
        check(acceptedAttempt.ResponseCode == "0020", "Response code stored.");

        // 6. Valid terminal transition: Accepted -> Reconciled
        DateTimeOffset reconcileTime = now.AddDays(1);
        PaymentAttempt reconciledAttempt = acceptedAttempt.TransitionTo(
            PaymentState.Reconciled,
            reconcileTime,
            statusDescription: "Matched with booked transaction.");
        check(reconciledAttempt.State == PaymentState.Reconciled, "State is Reconciled.");
        check(PaymentStateMachine.IsTerminal(reconciledAttempt.State), "Attempt is now in terminal state.");

        // 7. Invalid transition attempt throws
        bool illegalThrew = false;
        try
        {
            reconciledAttempt.TransitionTo(PaymentState.Submitting, reconcileTime.AddHours(1));
        }
        catch (InvalidPaymentTransitionException)
        {
            illegalThrew = true;
        }
        check(illegalThrew, "Transition from terminal Reconciled to Submitting was rejected.");

        // 8. Attempt constructor validation
        bool ctorInvalidIdThrew = false;
        try
        {
            _ = new PaymentAttempt(0, 1, 101, 10, 20, 30, e2e1, PaymentState.Draft, null, null, null, null, now, now);
        }
        catch (ArgumentOutOfRangeException)
        {
            ctorInvalidIdThrew = true;
        }
        check(ctorInvalidIdThrew, "Attempt constructor rejects zero ID.");

        bool ctorEmptyE2EThrew = false;
        try
        {
            _ = new PaymentAttempt(501, 1, 101, 10, 20, 30, "   ", PaymentState.Draft, null, null, null, null, now, now);
        }
        catch (ArgumentException)
        {
            ctorEmptyE2EThrew = true;
        }
        check(ctorEmptyE2EThrew, "Attempt constructor rejects whitespace EndToEndId.");
    }

    private static void TestPaymentStateTransitionAuditTrail(Action<bool, string> check)
    {
        DateTimeOffset now = new(2026, 10, 19, 10, 5, 0, TimeSpan.Zero);

        PaymentDraftRevision revision = new(
            Id: 101,
            DraftId: 1,
            RevisionNumber: 1,
            PredecessorRevisionId: null,
            Timestamp: now,
            SourceAccountId: 10,
            SourceAccountBindingId: 20,
            SourceAccountIncarnationId: 30,
            Beneficiary: new PaymentBeneficiary("Erika Mustermann", "DE89370400440532013000"),
            Amount: new Money(50.00m, "EUR"),
            Remittance: new PaymentRemittance("Gift"),
            ExecutionMode: PaymentExecutionMode.StandardCreditTransfer,
            ExecutionDate: new DateOnly(2026, 10, 25));

        PaymentAttempt attempt = PaymentAttempt.CreatePreSendAttempt(501, revision, now);

        // 1. Record transition to Submitting
        DateTimeOffset t1 = now.AddSeconds(10);
        PaymentStateTransition transition1 = PaymentStateTransition.Record(
            transitionId: 1001,
            attempt: attempt,
            newState: PaymentState.Submitting,
            timestamp: t1,
            reason: "User confirmed review and initiated transmission.");

        check(transition1.Id == 1001, "Transition ID recorded.");
        check(transition1.AttemptId == 501, "Attempt ID recorded.");
        check(transition1.FromState == PaymentState.AwaitingAuthorization, "FromState recorded.");
        check(transition1.ToState == PaymentState.Submitting, "ToState recorded.");
        check(transition1.Timestamp == t1, "Timestamp recorded.");
        check(transition1.Reason == "User confirmed review and initiated transmission.", "Reason recorded.");
        check(transition1.BankCode == null, "BankCode is null when none supplied.");

        // 2. Record transition to Unknown upon connection drop
        PaymentAttempt submittingAttempt = attempt.TransitionTo(PaymentState.Submitting, t1);
        DateTimeOffset t2 = t1.AddSeconds(30);
        PaymentStateTransition transition2 = PaymentStateTransition.Record(
            transitionId: 1002,
            attempt: submittingAttempt,
            newState: PaymentState.Unknown,
            timestamp: t2,
            reason: "Network timeout waiting for bank response; outcome uncertain.",
            bankCode: "NET_TIMEOUT");

        check(transition2.Id == 1002, "Second transition ID recorded.");
        check(transition2.FromState == PaymentState.Submitting, "FromState is Submitting.");
        check(transition2.ToState == PaymentState.Unknown, "ToState is Unknown.");
        check(transition2.BankCode == "NET_TIMEOUT", "BankCode recorded.");

        // 3. Invalid transition recording throws
        bool illegalRecordThrew = false;
        try
        {
            _ = PaymentStateTransition.Record(
                transitionId: 1003,
                attempt: attempt, // in AwaitingAuthorization
                newState: PaymentState.Accepted, // skipping Submitting
                timestamp: t2,
                reason: "Direct jump");
        }
        catch (InvalidPaymentTransitionException)
        {
            illegalRecordThrew = true;
        }
        check(illegalRecordThrew, "PaymentStateTransition.Record rejects invalid transition.");
    }

    private static void TestPaymentDuplicateGuard(Action<bool, string> check)
    {
        DateTimeOffset now = new(2026, 10, 19, 10, 10, 0, TimeSpan.Zero);
        DateOnly execDate = new(2026, 10, 25);

        PaymentDraftRevision candidate = new(
            Id: 201,
            DraftId: 2,
            RevisionNumber: 1,
            PredecessorRevisionId: null,
            Timestamp: now,
            SourceAccountId: 10,
            SourceAccountBindingId: 20,
            SourceAccountIncarnationId: 30,
            Beneficiary: new PaymentBeneficiary("Supplier Corp", "DE89 3704 0044 0532 0130 00"),
            Amount: new Money(250.00m, "EUR"),
            Remittance: new PaymentRemittance("Inv 9901"),
            ExecutionMode: PaymentExecutionMode.StandardCreditTransfer,
            ExecutionDate: execDate);

        // Historical revision for attempt #501
        PaymentDraftRevision matchedRevision = new(
            Id: 101,
            DraftId: 1,
            RevisionNumber: 1,
            PredecessorRevisionId: null,
            Timestamp: now.AddHours(-1),
            SourceAccountId: 10, // Same source account
            SourceAccountBindingId: 20,
            SourceAccountIncarnationId: 30,
            Beneficiary: new PaymentBeneficiary("Supplier Corp", "DE89370400440532013000"), // Same IBAN normalized
            Amount: new Money(250.00m, "EUR"), // Same amount
            Remittance: new PaymentRemittance("Inv 9901"),
            ExecutionMode: PaymentExecutionMode.StandardCreditTransfer,
            ExecutionDate: execDate); // Same execution date

        Dictionary<ulong, PaymentDraftRevision> revisions = new()
        {
            [101] = matchedRevision
        };

        // 1. Duplicate guard against in-flight Submitting attempt
        PaymentAttempt submittingAttempt = new(
            id: 501,
            draftId: 1,
            draftRevisionId: 101,
            sourceAccountId: 10,
            sourceAccountBindingId: 20,
            sourceAccountIncarnationId: 30,
            endToEndId: "BF-20261019-AAAAAAAAAAAAAAAA",
            state: PaymentState.Submitting,
            voPResult: null,
            bankReference: null,
            responseCode: null,
            statusDescription: null,
            createdAt: now.AddMinutes(-5),
            updatedAt: now.AddMinutes(-2));

        PaymentDuplicateCheckResult resultSubmitting = PaymentDuplicateGuard.CheckDuplicate(
            candidate,
            [submittingAttempt],
            revisions,
            currentTimestamp: now);

        check(resultSubmitting.IsDuplicateSuspected, "Duplicate suspected for Submitting attempt.");
        check(resultSubmitting.RequiresExplicitOverride, "Submitting duplicate requires explicit user override.");
        check(resultSubmitting.MatchedAttemptId == 501, "Matched attempt ID is 501.");
        check(resultSubmitting.MatchedAttemptState == PaymentState.Submitting, "Matched state is Submitting.");

        // 2. Duplicate guard against quarantined Unknown attempt
        PaymentAttempt unknownAttempt = submittingAttempt with { State = PaymentState.Unknown };
        PaymentDuplicateCheckResult resultUnknown = PaymentDuplicateGuard.CheckDuplicate(
            candidate,
            [unknownAttempt],
            revisions,
            currentTimestamp: now);

        check(resultUnknown.IsDuplicateSuspected, "Duplicate suspected for Unknown attempt.");
        check(resultUnknown.RequiresExplicitOverride, "Unknown duplicate requires explicit override.");
        check(resultUnknown.MatchedAttemptState == PaymentState.Unknown, "Matched state is Unknown.");
        check(resultUnknown.WarningMessage!.Contains("Unknown", StringComparison.Ordinal), "Warning references Unknown status.");

        // 3. Duplicate guard against active AwaitingAuthorization attempt
        PaymentAttempt awaitingAttempt = submittingAttempt with { State = PaymentState.AwaitingAuthorization };
        PaymentDuplicateCheckResult resultAwaiting = PaymentDuplicateGuard.CheckDuplicate(
            candidate,
            [awaitingAttempt],
            revisions,
            currentTimestamp: now);

        check(resultAwaiting.IsDuplicateSuspected, "Duplicate suspected for AwaitingAuthorization attempt.");

        // 4. Duplicate guard against recently Reconciled attempt (within 48h)
        PaymentAttempt recentReconciled = submittingAttempt with
        {
            State = PaymentState.Reconciled,
            UpdatedAt = now.AddHours(-12) // 12 hours ago
        };
        PaymentDuplicateCheckResult resultRecentReconciled = PaymentDuplicateGuard.CheckDuplicate(
            candidate,
            [recentReconciled],
            revisions,
            currentTimestamp: now);

        check(resultRecentReconciled.IsDuplicateSuspected, "Duplicate suspected for payment reconciled 12 hours ago.");

        // 5. Reconciled attempt outside lookback window (> 48h) is NOT flagged
        PaymentAttempt oldReconciled = submittingAttempt with
        {
            State = PaymentState.Reconciled,
            UpdatedAt = now.AddHours(-72) // 72 hours ago
        };
        PaymentDuplicateCheckResult resultOldReconciled = PaymentDuplicateGuard.CheckDuplicate(
            candidate,
            [oldReconciled],
            revisions,
            currentTimestamp: now);

        check(!resultOldReconciled.IsDuplicateSuspected, "Old reconciled payment outside lookback window is not flagged.");

        // 6. Terminal cancelled or rejected attempt is NOT flagged as duplicate
        PaymentAttempt cancelledAttempt = submittingAttempt with { State = PaymentState.AuthorizationCancelled };
        PaymentDuplicateCheckResult resultCancelled = PaymentDuplicateGuard.CheckDuplicate(
            candidate,
            [cancelledAttempt],
            revisions,
            currentTimestamp: now);

        check(!resultCancelled.IsDuplicateSuspected, "AuthorizationCancelled attempt is not flagged (safe to retry draft).");

        PaymentAttempt rejectedAttempt = submittingAttempt with { State = PaymentState.Rejected };
        PaymentDuplicateCheckResult resultRejected = PaymentDuplicateGuard.CheckDuplicate(
            candidate,
            [rejectedAttempt],
            revisions,
            currentTimestamp: now);

        check(!resultRejected.IsDuplicateSuspected, "Rejected attempt is not flagged (safe to retry draft).");

        // 7. Differing amount, account, or date is NOT flagged
        PaymentDraftRevision differentAmount = matchedRevision with { Amount = new Money(250.01m, "EUR") };
        Dictionary<ulong, PaymentDraftRevision> diffRevisions = new() { [101] = differentAmount };
        PaymentDuplicateCheckResult resultDiffAmount = PaymentDuplicateGuard.CheckDuplicate(
            candidate,
            [submittingAttempt],
            diffRevisions,
            currentTimestamp: now);
        check(!resultDiffAmount.IsDuplicateSuspected, "Different amount is not flagged as duplicate.");

        PaymentDraftRevision differentAccount = matchedRevision with { SourceAccountId = 99 };
        Dictionary<ulong, PaymentDraftRevision> diffAcctRevisions = new() { [101] = differentAccount };
        PaymentDuplicateCheckResult resultDiffAcct = PaymentDuplicateGuard.CheckDuplicate(
            candidate,
            [submittingAttempt],
            diffAcctRevisions,
            currentTimestamp: now);
        check(!resultDiffAcct.IsDuplicateSuspected, "Different source account is not flagged as duplicate.");

        PaymentDraftRevision differentDate = matchedRevision with { ExecutionDate = execDate.AddDays(1) };
        Dictionary<ulong, PaymentDraftRevision> diffDateRevisions = new() { [101] = differentDate };
        PaymentDuplicateCheckResult resultDiffDate = PaymentDuplicateGuard.CheckDuplicate(
            candidate,
            [submittingAttempt],
            diffDateRevisions,
            currentTimestamp: now);
        check(!resultDiffDate.IsDuplicateSuspected, "Different execution date is not flagged as duplicate.");

        // 8. CheckDuplicateDrafts between distinct unsubmitted drafts
        PaymentDraftRevision otherDraft = matchedRevision with { DraftId = 3 };
        PaymentDuplicateCheckResult draftDuplicateResult = PaymentDuplicateGuard.CheckDuplicateDrafts(
            candidate,
            [otherDraft]);
        check(draftDuplicateResult.IsDuplicateSuspected, "Identical unsubmitted draft is detected as duplicate.");
        check(draftDuplicateResult.MatchedDraftId == 3, "Matched draft ID is 3.");

        // Same draft ID is not flagged against itself
        PaymentDraftRevision sameDraftOtherRev = matchedRevision with { DraftId = candidate.DraftId };
        PaymentDuplicateCheckResult sameDraftResult = PaymentDuplicateGuard.CheckDuplicateDrafts(
            candidate,
            [sameDraftOtherRev]);
        check(!sameDraftResult.IsDuplicateSuspected, "Revisions of the same draft are not flagged against each other.");
    }
}
