using Broiler.Fond.Kernel.FinTs;

namespace Broiler.Fond.Kernel.Tests;

// Rebuild fixture requests from evidence actually handed out by the session.
internal static class FinTsReadSessionCandidates
{
    internal static FinTsPinTanAllAccountRequestBinding Bind(FinTsPinTanAllAccountRequestBinding candidate, FinTsPinTanReadSessionAttempt attempt)
    {
        if (attempt.RequirementsEvidence is not { } initialization) { return candidate; }
        var original = candidate.Context.Signature;
        var signature = FinTsPinTanReadSignatureContext.Evaluate(initialization, original.Request, original.Header, original.Selection, original.ControlReference);
        return FinTsPinTanAllAccountRequestBinding.ForEnvelopeCandidate(FinTsPinTanAllAccountCapabilityContext.Evaluate(signature));
    }

    internal static FinTsPinTanReadRequestBinding Bind(FinTsPinTanReadRequestBinding candidate, FinTsPinTanReadSessionAttempt attempt)
    {
        if (attempt.RequirementsEvidence is not { } initialization) { return candidate; }
        var original = candidate.Context.Signature;
        var signature = FinTsPinTanReadSignatureContext.Evaluate(initialization, original.Request, original.Header, original.Selection, original.ControlReference);
        var reads = initialization.ReadParameters!;
        var national = candidate.Context.NationalAdvertisement is null ? null : reads.Advertisements.Single(a => a.Operation == FinTsReadOperation.SepaAccountDetails);
        return FinTsPinTanReadRequestBinding.ForEnvelopeCandidate(FinTsPinTanReadCapabilityContext.Evaluate(signature, reads.Source.Accounts[0], national));
    }

    internal static FinTsPinTanReadContinuationRequestBinding Bind(FinTsPinTanReadContinuationRequestBinding candidate, FinTsPinTanReadSessionAttempt attempt)
    {
        if (attempt.BalanceFirstPageEvidence is null) { return candidate; }
        var original = candidate.Context;
        var pages = attempt.BalanceContinuationPages;
        var continuation = pages.Count == 0
            ? FinTsPinTanReadContinuationContext.Evaluate(attempt.BalanceFirstPageEvidence, original.Request)
            : FinTsPinTanReadContinuationContext.Evaluate(pages[^1], original.Request);
        return FinTsPinTanReadContinuationRequestBinding.ForEnvelopeCandidate(FinTsPinTanReadContinuationSignatureContext.Evaluate(
            continuation, original.Header, original.Selection, original.ControlReference));
    }

    internal static FinTsPinTanDialogueEndRequestBinding Bind(FinTsPinTanDialogueEndRequestBinding candidate, FinTsPinTanReadSessionAttempt attempt)
    {
        if (attempt.InitializationEvidence is not { } initialization) { return candidate; }
        var original = candidate.Context;
        return FinTsPinTanDialogueEndRequestBinding.ForEnvelopeCandidate(FinTsPinTanDialogueEndContext.EvaluateForInitialization(
            initialization, original.Request, original.Header, candidate.MessageNumber, candidate.ExpectedBankMessageNumber));
    }
}
