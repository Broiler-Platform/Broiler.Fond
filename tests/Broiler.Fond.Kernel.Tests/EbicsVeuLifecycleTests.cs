using Broiler.Fond.Kernel.Domain;
using Broiler.Fond.Kernel.Professional;

namespace Broiler.Fond.Kernel.Tests;

public static class EbicsVeuLifecycleTests
{
    public static void RunAllTests()
    {
        Console.WriteLine("Running Distributed Electronic Signatures (VEU) & Order Lifecycle verification (P-04)...");
        int checks = 0;

        // 1. Signature class & order type mappings
        Assert(VeuSignatureClass.Transport.ToCode() == "T", "T code matches");
        Assert(VeuSignatureClass.SingleAuthorization.ToCode() == "E", "E code matches");
        Assert(VeuSignatureClass.FirstSignature.ToCode() == "A", "A code matches");
        Assert(VeuSignatureClass.SecondSignature.ToCode() == "B", "B code matches");
        checks += 4;

        Assert(VeuSignatureClassExtensions.ParseVeuClass("a") == VeuSignatureClass.FirstSignature, "ParseVeuClass A matches");
        Assert(VeuSignatureClassExtensions.ParseVeuClass("b") == VeuSignatureClass.SecondSignature, "ParseVeuClass B matches");
        Assert(VeuSignatureClassExtensions.ParseVeuClass("e") == VeuSignatureClass.SingleAuthorization, "ParseVeuClass E matches");
        Assert(VeuSignatureClassExtensions.ParseVeuClass("t") == VeuSignatureClass.Transport, "ParseVeuClass T matches");
        checks += 4;

        Assert(VeuOrderType.HVZ.ToOrderCode() == "HVZ", "HVZ code matches");
        Assert(VeuOrderType.HVD.ToOrderCode() == "HVD", "HVD code matches");
        Assert(VeuOrderType.HVE.ToOrderCode() == "HVE", "HVE code matches");
        Assert(VeuOrderType.HVS.ToOrderCode() == "HVS", "HVS code matches");
        Assert(VeuOrderType.HVT.ToOrderCode() == "HVT", "HVT code matches");
        checks += 5;

        // 2. User setup and staging
        var coordinator = new EbicsVeuCoordinator();
        var operatorUser = new ProfessionalUser("usr-op", "Bob Operator", ProfessionalRole.Operator);
        var viewer = new ProfessionalUser("usr-view", "Alice Viewer", ProfessionalRole.Viewer);
        var approver1 = new ProfessionalUser("usr-app1", "Carol Approver 1", ProfessionalRole.Approver);
        var approver2 = new ProfessionalUser("usr-app2", "Dave Approver 2", ProfessionalRole.Approver);
        var admin = new ProfessionalUser("usr-adm", "Eve Admin", ProfessionalRole.Administrator);

        DateTimeOffset now = DateTimeOffset.UtcNow;

        // Viewer cannot stage orders
        bool viewerStageCaught = false;
        try
        {
            coordinator.StageOrder("ORD-001", "CCT", "PRT01", new Money(10000.00m, "EUR"), viewer, VeuSignatureScheme.DualSignatureAB, "SIG-T", "DIGEST-1", now);
        }
        catch (InvalidOperationException)
        {
            viewerStageCaught = true;
        }
        Assert(viewerStageCaught, "Viewer cannot stage VEU orders");
        checks++;

        // Operator stages order
        var stagedOrder = coordinator.StageOrder(
            "ORD-VEU-100",
            "CCT",
            "PRT01",
            new Money(500000.00m, "EUR"),
            operatorUser,
            VeuSignatureScheme.DualSignatureAB,
            "SIG-TRANSPORT-BASE64",
            "DIGEST-PAYROLL-500K",
            now);

        Assert(stagedOrder.CurrentState == VeuOrderState.PendingFirstSignature, "Staged order is in PendingFirstSignature");
        Assert(stagedOrder.Signatures.Count == 1, "Order has transport signature");
        Assert(stagedOrder.Signatures[0].SignatureClass == VeuSignatureClass.Transport, "First recorded signature is Transport");
        Assert(!stagedOrder.IsTerminal, "Pending first signature is not terminal");
        checks += 4;

        // 3. Transport signature cannot be applied as authorization signature
        bool transportAuthCaught = false;
        try
        {
            coordinator.ApplySignature(stagedOrder, approver1, VeuSignatureClass.Transport, "DIGEST", "SIG", now);
        }
        catch (InvalidOperationException)
        {
            transportAuthCaught = true;
        }
        Assert(transportAuthCaught, "Transport signature cannot be applied as authorization signature");
        checks++;

        // 4. Applying second signature (B) before first signature (A) is rejected
        bool bBeforeACaught = false;
        try
        {
            coordinator.ApplySignature(stagedOrder, approver1, VeuSignatureClass.SecondSignature, "DIGEST", "SIG-B", now);
        }
        catch (InvalidOperationException)
        {
            bBeforeACaught = true;
        }
        Assert(bBeforeACaught, "B signature before A signature must fail");
        checks++;

        // 5. Applying First Signature (A)
        var orderWithA = coordinator.ApplySignature(
            stagedOrder,
            approver1,
            VeuSignatureClass.FirstSignature,
            "DIGEST-PAYROLL-500K",
            "SIG-A-BASE64",
            now.AddMinutes(10));

        Assert(orderWithA.CurrentState == VeuOrderState.PendingSecondSignature, "State must transition to PendingSecondSignature");
        Assert(orderWithA.Signatures.Count == 2, "Order has 2 signatures now (T + A)");
        Assert(!orderWithA.IsTerminal, "Pending second signature is not terminal");
        checks += 3;

        // 6. Distinct Signatories Invariant: Same user (approver1) cannot apply second signature (B)
        bool selfSecondSigCaught = false;
        try
        {
            coordinator.ApplySignature(
                orderWithA,
                approver1,
                VeuSignatureClass.SecondSignature,
                "DIGEST-PAYROLL-500K",
                "SIG-B-REPEAT-BASE64",
                now.AddMinutes(15));
        }
        catch (InvalidOperationException ex) when (ex.Message.Contains("cannot provide both the first and second signature"))
        {
            selfSecondSigCaught = true;
        }
        Assert(selfSecondSigCaught, "Same signatory cannot provide both A and B signatures under four-eyes principle");
        checks++;

        // 7. Applying Second Signature (B) by distinct approver2
        var completelySignedOrder = coordinator.ApplySignature(
            orderWithA,
            approver2,
            VeuSignatureClass.SecondSignature,
            "DIGEST-PAYROLL-500K",
            "SIG-B-BASE64",
            now.AddMinutes(20));

        Assert(completelySignedOrder.CurrentState == VeuOrderState.CompletelySigned, "Order must transition to CompletelySigned");
        Assert(completelySignedOrder.Signatures.Count == 3, "Order has 3 signatures (T + A + B)");
        Assert(completelySignedOrder.IsTerminal, "CompletelySigned is terminal for signing");
        checks += 3;

        // 8. Confirm bank release
        var releasedOrder = coordinator.ConfirmRelease(completelySignedOrder, now.AddMinutes(25));
        Assert(releasedOrder.CurrentState == VeuOrderState.Released, "Order transitions to Released");
        Assert(releasedOrder.IsTerminal, "Released is terminal");
        checks += 2;

        // 9. Post-terminal signature fails closed
        bool postTerminalSigCaught = false;
        try
        {
            coordinator.ApplySignature(releasedOrder, admin, VeuSignatureClass.SecondSignature, "DIGEST", "SIG", now);
        }
        catch (InvalidOperationException)
        {
            postTerminalSigCaught = true;
        }
        Assert(postTerminalSigCaught, "Applying signature to terminal order must fail closed");
        checks++;

        // 10. Single authorization workflow (E)
        var singleSchemeOrder = coordinator.StageOrder(
            "ORD-VEU-SINGLE",
            "CCT",
            "PRT01",
            new Money(5000.00m, "EUR"),
            operatorUser,
            VeuSignatureScheme.SingleSignatureE,
            "SIG-T-2",
            "DIGEST-5K",
            now);

        var executiveSignedOrder = coordinator.ApplySignature(
            singleSchemeOrder,
            approver1,
            VeuSignatureClass.SingleAuthorization,
            "DIGEST-5K",
            "SIG-E-BASE64",
            now.AddMinutes(5));

        Assert(executiveSignedOrder.CurrentState == VeuOrderState.CompletelySigned, "Single authorization (E) transitions directly to CompletelySigned");
        checks++;

        // E signature rejected on dual-signature scheme
        bool eOnDualSchemeCaught = false;
        try
        {
            coordinator.ApplySignature(stagedOrder, approver1, VeuSignatureClass.SingleAuthorization, "DIGEST", "SIG-E", now);
        }
        catch (InvalidOperationException)
        {
            eOnDualSchemeCaught = true;
        }
        Assert(eOnDualSchemeCaught, "E signature must be rejected on dual-signature scheme");
        checks++;

        // 11. Rejection workflow
        var rejectTargetOrder = coordinator.StageOrder(
            "ORD-VEU-REJ",
            "CDD",
            "PRT01",
            new Money(12000.00m, "EUR"),
            operatorUser,
            VeuSignatureScheme.DualSignatureAB,
            "SIG-T-REJ",
            "DIGEST-REJ",
            now);

        var rejectedOrder = coordinator.RejectOrder(rejectTargetOrder, approver1, "Suspicious direct debit batch", now.AddMinutes(2));
        Assert(rejectedOrder.CurrentState == VeuOrderState.Rejected, "Order must transition to Rejected");
        Assert(rejectedOrder.IsTerminal, "Rejected order is terminal");
        checks += 2;

        // 12. Cancellation workflow
        var cancelTargetOrder = coordinator.StageOrder(
            "ORD-VEU-CNC",
            "CCT",
            "PRT01",
            new Money(8000.00m, "EUR"),
            operatorUser,
            VeuSignatureScheme.DualSignatureAB,
            "SIG-T-CNC",
            "DIGEST-CNC",
            now);

        // Viewer cannot cancel
        bool viewerCancelCaught = false;
        try
        {
            coordinator.CancelOrder(cancelTargetOrder, viewer, "Attempted cancel", now);
        }
        catch (InvalidOperationException)
        {
            viewerCancelCaught = true;
        }
        Assert(viewerCancelCaught, "Viewer cannot cancel VEU order");
        checks++;

        // Submitter can cancel
        var cancelledOrder = coordinator.CancelOrder(cancelTargetOrder, operatorUser, "Retracted before signature", now.AddMinutes(1));
        Assert(cancelledOrder.CurrentState == VeuOrderState.Cancelled, "Order must transition to Cancelled");
        Assert(cancelledOrder.IsTerminal, "Cancelled order is terminal");
        checks += 2;

        // 13. HVZ Query XML generation
        var hostConfig = new EbicsHostConfig("EBICS01", "PRT01", "USR01", new Uri("https://ebics.bank.example/server"), EbicsVersion.H004);
        string hvzXml = coordinator.BuildHvzQueryRequest(hostConfig, now);
        Assert(hvzXml.Contains("ebicsRequest"), "HVZ contains ebicsRequest");
        Assert(hvzXml.Contains("OrderType>HVZ<"), "HVZ contains OrderType HVZ");
        Assert(hvzXml.Contains("HostID>EBICS01<"), "HVZ contains HostID");
        checks += 3;

        Console.WriteLine($"Distributed Electronic Signatures (VEU) & Order Lifecycle checks passed ({checks} checks).");
    }

    private static void Assert(bool condition, string message)
    {
        if (!condition)
        {
            throw new InvalidOperationException($"Assertion failed: {message}");
        }
    }
}
