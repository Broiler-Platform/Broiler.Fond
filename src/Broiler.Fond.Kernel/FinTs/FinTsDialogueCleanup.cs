using System.Security.Cryptography;
using Broiler.Fond.Kernel.FinTs.Transport;

namespace Broiler.Fond.Kernel.FinTs;

public enum FinTsDialogueCleanupOutcome { NotRequired, Closed, Failed }

/// <summary>One bounded cleanup attempt for an accepted dialogue whose next request was never dispatched.</summary>
internal static class FinTsDialogueCleanup
{
    internal static async Task<FinTsDialogueCleanupOutcome> CloseAsync(Func<FinTsPinTanDialogueEndRequestBinding> candidateFactory,
        IFinTsTransport transport, Uri endpoint, FinTsSessionCredential pin)
    {
        using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(2));
        byte[] buffer = new byte[FinTsPinTanDialogueEndWriter.MaximumEncodedLength];
        try
        {
            var candidate = candidateFactory();
            if (FinTsPinTanDialogueEndWriter.TryEncode(candidate.Context, pin, buffer, out int written, cancellation.Token) != FinTsPinTanRequestWriteResult.Written)
                return FinTsDialogueCleanupOutcome.Failed;
            var response = await transport.SendAsync(endpoint, buffer.AsMemory(0, written), cancellation.Token).ConfigureAwait(false);
            if (!response.IsSuccess) return FinTsDialogueCleanupOutcome.Failed;
            var parsed = FinTsReadExecutionPipeline.ParseWireResponse(response.Payload.Span, cancellation.Token);
            var evidence = FinTsPinTanDialogueEndEvidence.Evaluate(FinTsPinTanDialogueEndResponseBinding.Evaluate(candidate, parsed, cancellation.Token), cancellation.Token);
            return evidence.Outcome == FinTsDialogueEndOutcome.ClosureReported ? FinTsDialogueCleanupOutcome.Closed : FinTsDialogueCleanupOutcome.Failed;
        }
        catch (Exception) { return FinTsDialogueCleanupOutcome.Failed; }
        finally { CryptographicOperations.ZeroMemory(buffer); }
    }

    internal static void Notify(Action<FinTsDialogueCleanupOutcome>? observer, FinTsDialogueCleanupOutcome outcome)
    {
        // A diagnostics observer must not hide the primary bank/request-construction failure.
        try { observer?.Invoke(outcome); } catch (Exception) { }
    }
}
