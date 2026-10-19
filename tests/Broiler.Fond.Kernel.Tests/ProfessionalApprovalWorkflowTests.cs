using Broiler.Fond.Kernel.Domain;
using Broiler.Fond.Kernel.Professional;

namespace Broiler.Fond.Kernel.Tests;

public static class ProfessionalApprovalWorkflowTests
{
    public static void RunAllTests()
    {
        Console.WriteLine("Running Professional Roles & Four-Eyes Approval Workflow verification (P-01)...");
        int checks = 0;

        var policy = new FourEyesApprovalPolicy(new Money(5000.00m, "EUR"), requireDistinctPrincipals: true, requiredApproverCount: 2);
        var auditLog = new CorporateAuditLog();
        var engine = new FourEyesApprovalEngine(policy, auditLog);

        var viewer = new ProfessionalUser("usr-viewer", "Alice Viewer", ProfessionalRole.Viewer);
        var operatorUser = new ProfessionalUser("usr-operator", "Bob Operator", ProfessionalRole.Operator);
        var approver1 = new ProfessionalUser("usr-approver-1", "Carol Approver 1", ProfessionalRole.Approver);
        var approver2 = new ProfessionalUser("usr-approver-2", "Dave Approver 2", ProfessionalRole.Approver);
        var admin = new ProfessionalUser("usr-admin", "Eve Administrator", ProfessionalRole.Administrator);
        var deactivatedApprover = new ProfessionalUser("usr-deactivated", "Frank Inactive", ProfessionalRole.Approver, isActive: false);

        engine.RegisterUser(viewer);
        engine.RegisterUser(operatorUser);
        engine.RegisterUser(approver1);
        engine.RegisterUser(approver2);
        engine.RegisterUser(admin);
        engine.RegisterUser(deactivatedApprover);

        Assert(engine.GetUser("usr-viewer")?.Role == ProfessionalRole.Viewer, "Registered viewer must be retrievable");
        checks++;

        DateTimeOffset now = DateTimeOffset.UtcNow;

        // 1. Role permission checks: Viewer cannot initiate
        bool viewerInitiateFailed = false;
        try
        {
            engine.CreateRequest("REQ-001", "ORD-001", viewer, new Money(100.00m, "EUR"), "Invalid initiation", now);
        }
        catch (InvalidOperationException)
        {
            viewerInitiateFailed = true;
        }
        Assert(viewerInitiateFailed, "Viewer must not be permitted to initiate orders");
        checks++;

        // 2. Approver cannot initiate orders
        bool approverInitiateFailed = false;
        try
        {
            engine.CreateRequest("REQ-002", "ORD-002", approver1, new Money(100.00m, "EUR"), "Invalid initiation", now);
        }
        catch (InvalidOperationException)
        {
            approverInitiateFailed = true;
        }
        Assert(approverInitiateFailed, "Approver must not be permitted to initiate orders");
        checks++;

        // 3. Deactivated user cannot initiate
        var deactivatedOperator = new ProfessionalUser("usr-deact-op", "Ghost Operator", ProfessionalRole.Operator, isActive: false);
        bool deactInitiateFailed = false;
        try
        {
            engine.CreateRequest("REQ-003", "ORD-003", deactivatedOperator, new Money(100.00m, "EUR"), "Inactive op", now);
        }
        catch (InvalidOperationException)
        {
            deactInitiateFailed = true;
        }
        Assert(deactInitiateFailed, "Deactivated operator must not be permitted to initiate orders");
        checks++;

        // 4. Sub-threshold orders auto-qualify (e.g. 1,500.00 EUR < 5,000.00 EUR)
        var subThresholdReq = engine.CreateRequest("REQ-SUB", "ORD-SUB", operatorUser, new Money(1500.00m, "EUR"), "Small transfer", now);
        Assert(subThresholdReq.Status == ApprovalStatus.Approved, "Sub-threshold orders must auto-qualify as Approved");
        Assert(subThresholdReq.RequiredApprovals == 0, "Sub-threshold orders require 0 approvers");
        Assert(subThresholdReq.IsTerminal, "Auto-approved orders are terminal");
        checks += 3;

        // 5. Super-threshold orders require dual approval (e.g. 10,000.00 EUR >= 5,000.00 EUR)
        var superThresholdReq = engine.CreateRequest("REQ-SUPER", "ORD-SUPER", operatorUser, new Money(10000.00m, "EUR"), "Large batch", now);
        Assert(superThresholdReq.Status == ApprovalStatus.PendingApproval, "Super-threshold orders must start PendingApproval");
        Assert(superThresholdReq.RequiredApprovals == 2, "Super-threshold orders require 2 approvers");
        Assert(!superThresholdReq.IsTerminal, "Pending approval is not terminal");
        checks += 3;

        // 6. Operator cannot submit approval decisions
        bool operatorApproveFailed = false;
        try
        {
            engine.SubmitDecision(superThresholdReq, operatorUser, ApprovalDecisionType.Approve, "SIG-TOKEN-1", null, now);
        }
        catch (InvalidOperationException)
        {
            operatorApproveFailed = true;
        }
        Assert(operatorApproveFailed, "Operator must not be permitted to submit approval decisions");
        checks++;

        // 7. Viewer cannot submit approval decisions
        bool viewerApproveFailed = false;
        try
        {
            engine.SubmitDecision(superThresholdReq, viewer, ApprovalDecisionType.Approve, "SIG-TOKEN-1", null, now);
        }
        catch (InvalidOperationException)
        {
            viewerApproveFailed = true;
        }
        Assert(viewerApproveFailed, "Viewer must not be permitted to submit approval decisions");
        checks++;

        // 8. Self-approval strictly forbidden
        // If an operator is also somehow given Approver role or user id matches
        var dualRoleUser = new ProfessionalUser("usr-operator", "Bob Dual", ProfessionalRole.Approver);
        bool selfApprovalBlocked = false;
        try
        {
            engine.SubmitDecision(superThresholdReq, dualRoleUser, ApprovalDecisionType.Approve, "SIG-TOKEN-1", null, now);
        }
        catch (InvalidOperationException ex) when (ex.Message.Contains("Self-approval is strictly forbidden"))
        {
            selfApprovalBlocked = true;
        }
        Assert(selfApprovalBlocked, "Initiator attempting approval must throw InvalidOperationException forbidding self-approval");
        checks++;

        // 9. Deactivated approver rejected
        bool deactApproveFailed = false;
        try
        {
            engine.SubmitDecision(superThresholdReq, deactivatedApprover, ApprovalDecisionType.Approve, "SIG-TOKEN-1", null, now);
        }
        catch (InvalidOperationException)
        {
            deactApproveFailed = true;
        }
        Assert(deactApproveFailed, "Deactivated approver must not be permitted to submit approval");
        checks++;

        // 10. First approval transitions to PartiallyApproved
        var partialReq = engine.SubmitDecision(
            superThresholdReq,
            approver1,
            ApprovalDecisionType.Approve,
            "SIG-TOKEN-APPROVER1",
            "Looks good from accounting",
            now.AddMinutes(5));

        Assert(partialReq.Status == ApprovalStatus.PartiallyApproved, "First approval must transition to PartiallyApproved");
        Assert(partialReq.Decisions.Count == 1, "One decision recorded");
        Assert(partialReq.Decisions[0].ApproverUserId == approver1.UserId, "Decision must record approver user ID");
        Assert(!partialReq.IsTerminal, "PartiallyApproved is not terminal");
        checks += 4;

        // 11. Same approver cannot submit second approval (distinct principals invariant)
        bool duplicateApproverFailed = false;
        try
        {
            engine.SubmitDecision(partialReq, approver1, ApprovalDecisionType.Approve, "SIG-TOKEN-APPROVER1-REPEAT", null, now.AddMinutes(6));
        }
        catch (InvalidOperationException)
        {
            duplicateApproverFailed = true;
        }
        Assert(duplicateApproverFailed, "Same approver must not approve twice");
        checks++;

        // 12. Second approval by distinct approver transitions to Approved
        var fullyApprovedReq = engine.SubmitDecision(
            partialReq,
            approver2,
            ApprovalDecisionType.Approve,
            "SIG-TOKEN-APPROVER2",
            "Authorized by management",
            now.AddMinutes(10));

        Assert(fullyApprovedReq.Status == ApprovalStatus.Approved, "Second approval must transition to Approved");
        Assert(fullyApprovedReq.Decisions.Count == 2, "Two decisions recorded");
        Assert(fullyApprovedReq.IsTerminal, "Approved state is terminal");
        checks += 3;

        // 13. Submitting decision against terminal request throws
        bool postTerminalDecisionFailed = false;
        try
        {
            engine.SubmitDecision(fullyApprovedReq, admin, ApprovalDecisionType.Approve, "SIG-TOKEN-ADMIN", null, now.AddMinutes(12));
        }
        catch (InvalidOperationException)
        {
            postTerminalDecisionFailed = true;
        }
        Assert(postTerminalDecisionFailed, "Cannot submit decisions against terminal Approved request");
        checks++;

        // 14. Rejection flow transitions immediately to Rejected
        var rejectTargetReq = engine.CreateRequest("REQ-REJ", "ORD-REJ", operatorUser, new Money(25000.00m, "EUR"), "Suspicious invoice", now);
        var rejectedReq = engine.SubmitDecision(
            rejectTargetReq,
            approver1,
            ApprovalDecisionType.Reject,
            "SIG-TOKEN-REJ",
            "Beneficiary IBAN does not match vendor master data",
            now.AddMinutes(2));

        Assert(rejectedReq.Status == ApprovalStatus.Rejected, "Rejection must immediately transition status to Rejected");
        Assert(rejectedReq.IsTerminal, "Rejected state is terminal");
        checks += 2;

        bool postRejectDecisionFailed = false;
        try
        {
            engine.SubmitDecision(rejectedReq, approver2, ApprovalDecisionType.Approve, "SIG-TOKEN-REJ-2", null, now.AddMinutes(3));
        }
        catch (InvalidOperationException)
        {
            postRejectDecisionFailed = true;
        }
        Assert(postRejectDecisionFailed, "Cannot submit decisions against terminal Rejected request");
        checks++;

        // 15. Cancellation flow
        var cancelTargetReq = engine.CreateRequest("REQ-CNC", "ORD-CNC", operatorUser, new Money(8000.00m, "EUR"), "Accidental draft", now);

        // Viewer cannot cancel
        bool viewerCancelFailed = false;
        try
        {
            engine.CancelRequest(cancelTargetReq, viewer, "Cancel attempt", now);
        }
        catch (InvalidOperationException)
        {
            viewerCancelFailed = true;
        }
        Assert(viewerCancelFailed, "Viewer cannot cancel request");
        checks++;

        // Initiator can cancel
        var cancelledReq = engine.CancelRequest(cancelTargetReq, operatorUser, "Draft retracted by creator", now.AddMinutes(1));
        Assert(cancelledReq.Status == ApprovalStatus.Cancelled, "Status must transition to Cancelled");
        Assert(cancelledReq.IsTerminal, "Cancelled is terminal");
        checks += 2;

        // 16. Corporate Audit Log verification
        Assert(auditLog.Entries.Count >= 7, $"Audit log must contain all executed actions (got {auditLog.Entries.Count})");
        bool chainValid = auditLog.VerifyChainIntegrity(out string? auditFailure);
        Assert(chainValid && auditFailure == null, $"Corporate audit log hash chain must be strictly valid: {auditFailure}");
        checks += 2;

        // 17. Tamper detection on Corporate Audit Log
        var tamperedLog = new CorporateAuditLog();
        tamperedLog.Append(now, "usr-1", ProfessionalRole.Operator, "InitiateOrder", "ORD-T1", "SIG-1");
        tamperedLog.Append(now.AddMinutes(1), "usr-2", ProfessionalRole.Approver, "FirstApproval", "ORD-T1", "SIG-2");
        Assert(tamperedLog.VerifyChainIntegrity(out _), "Untampered log must verify");
        checks++;

        // Verify tampering is caught
        var reflectionEntriesField = typeof(CorporateAuditLog).GetField("_entries", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        var entriesList = (List<CorporateAuditEntry>)reflectionEntriesField!.GetValue(tamperedLog)!;
        var originalEntry = entriesList[0];
        // Mutate entry data without updating hash
        entriesList[0] = originalEntry with { Action = "TamperedAction" };
        bool caughtTampering = !tamperedLog.VerifyChainIntegrity(out string? tamperReason);
        Assert(caughtTampering && tamperReason != null && tamperReason.Contains("Tampered audit hash"), "Tampered audit entry must fail integrity verification");
        checks++;

        Console.WriteLine($"Professional Roles & Four-Eyes Approval Workflow checks passed ({checks} checks).");
    }

    private static void Assert(bool condition, string message)
    {
        if (!condition)
        {
            throw new InvalidOperationException($"Assertion failed: {message}");
        }
    }
}
