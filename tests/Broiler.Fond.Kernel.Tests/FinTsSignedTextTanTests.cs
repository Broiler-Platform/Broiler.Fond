using System.Reflection;
using System.Text;
using System.Text.Json;
using Broiler.Fond.Kernel.Domain.Connections;
using Broiler.Fond.Kernel.Domain.Identifiers;
using Broiler.Fond.Kernel.Domain.Institutions;
using Broiler.Fond.Kernel.FinTs;
using Broiler.Fond.Kernel.FinTs.Transport;
using Broiler.Fond.Kernel.Product;
using Broiler.Fond.TestSupport;

namespace Broiler.Fond.Kernel.Tests;

internal static class FinTsSignedTextTanTests
{
    internal static void Run(Action<bool, string> check) => RunAsync(check).GetAwaiter().GetResult();
    private static async Task RunAsync(Action<bool, string> check)
    {
        using var doc = JsonDocument.Parse(Assembly.GetExecutingAssembly().GetManifestResourceStream("FinTs.pin-tan-synchronization-session-attempt-v1.json")!);
        var vector = doc.RootElement.GetProperty("vectors")[0];
        byte[] source = Bytes(vector.GetProperty("syncCandidate"), "procedureResponseBase64"), template = Bytes(vector.GetProperty("syncResponse"), "wireBase64");
        var origin = FinTsUnsignedInitializationRequest.Parse(FinTsMessageFrame.Parse(Bytes(vector.GetProperty("syncCandidate"), "originRequestBase64")));
        var response = FinTsResponse.Parse(FinTsMessageFrame.Parse(source));
        var procedures = new FinTsPinTanProcedureContext(origin, FinTsTanParameterSet.Parse(FinTsParameterSet.Parse(response)), FinTsPermittedProcedureSet.Parse(response));
        var connection = BankConnection.Create(new ConnectionId(2), new InstitutionId(1), ManualInstitutionConfiguration.Create("Bank", "https://bank.example/fints", "PUBLIC-BANK")
            .ConfirmEndpoint("https://bank.example/fints"), "PUBLIC-USER", "PUBLIC-CUSTOMER");
        var registration = new FinTsProductRegistration("ASSIGNED-APP-ID", "2.1");
        var selection = new FinTsPinTanSignatureSelection(2, 900, 7);
        byte[] initial = SignedTanTestWire.Initial(template, source);
        var clock = new Clock();
        FinTsProductionCandidateFactory Factory() => new(connection, registration, procedures, selection, clock);
        FinTsSignedTextTanRequest Request()
        {
            var factory = Factory(); var actual = factory.CreateSynchronizationApprovalRequest();
            return factory.CreateTextTanContinuation(actual, FinTsReadExecutionPipeline.ParseWireResponse(initial));
        }
        using (var pin = Credential("PIN-MARKER", FinTsCredentialKind.Pin))
        using (var tan = Credential("123456", FinTsCredentialKind.Tan))
        {
            var request = Request(); var small = new byte[1];
            check(request.TryEncode(pin, tan, small, out _) == FinTsPinTanRequestWriteResult.DestinationTooSmall && tan.GetSnapshot().State == FinTsCredentialState.Available,
                "TAN encoder requires full reserve before consuming credentials.");
            var buffer = new byte[FinTsSignedTextTanRequest.MaximumEncodedLength];
            check(request.TryEncode(pin, tan, buffer, out int written) == FinTsPinTanRequestWriteResult.Written && tan.GetSnapshot().State == FinTsCredentialState.Consumed,
                "A bound process-2 signed envelope consumes the TAN exactly once.");
            var frame = FinTsMessageFrame.Parse(buffer.AsSpan(0, written));
            var body = FinTsSyntax.ParseSegments(frame.Syntax.Segments[2].Fields[0].Elements[0].CopyValueBytes());
            check(frame.MessageNumber == 2 && body.Segments.Single(s => s.Code == "HKTAN").Number == 3 &&
                Encoding.Latin1.GetString(body.CopyWireBytes()).Contains("+2++++PUBLIC-ORDER+N'"),
                "Signed TAN request has the real next counter, actual segment position and exact bank order reference.");
            check(request.TryEncode(pin, tan, buffer, out _) == FinTsPinTanRequestWriteResult.ContextNeedsReview, "A signed TAN continuation cannot be replayed.");
            System.Security.Cryptography.CryptographicOperations.ZeroMemory(buffer);
        }
        FinTsSessionCredential? acquired = null;
        using (var pin = Credential("PIN-MARKER", FinTsCredentialKind.Pin))
        using (var transport = new FinTsSimulatedTransport())
        {
            transport.EnqueueResponse(initial); transport.EnqueueResponse(SignedTanTestWire.Confirm(template)); transport.EnqueueResponse(SignedTanTestWire.Close(template));
            var factory = Factory();
            var result = await FinTsSynchronizationExecutionPipeline.ExecuteAsync(connection, transport, pin, factory.SynchronizationOptions(textTanPrompt: (_, _) =>
            { acquired = Credential("123456", FinTsCredentialKind.Tan); return ValueTask.FromResult<FinTsSessionCredential?>(acquired); }));
            check(result.IsSuccess && result.UpdatedConnection.IsSynchronized && transport.TransmittedCount == 3 && acquired?.GetSnapshot().State == FinTsCredentialState.Consumed,
                "Actual HKTAN request, signed TAN confirmation and message-3 close produce qualified synchronization with consumed TAN ownership.");
        }
        foreach (byte[] confirmation in new[] { SignedTanTestWire.Confirm(template, "OTHER-ORDER"), SignedTanTestWire.Confirm(template, status: "0030"),
            BankParameterTestWire.Edit(SignedTanTestWire.Confirm(template), s => s.Replace("SYNTHETIC", "OTHER-DIALOGUE", StringComparison.Ordinal)),
            BankParameterTestWire.Edit(SignedTanTestWire.Confirm(template), s => s.Replace("PUBLIC-USER", "OTHER-USER", StringComparison.Ordinal)) })
        {
            using var pin = Credential("PIN-MARKER", FinTsCredentialKind.Pin);
            using var transport = new FinTsSimulatedTransport(); transport.EnqueueResponse(confirmation);
            var result = await FinTsSignedTextTanExecution.ExecuteAsync(Request(), transport, new Uri("https://bank.example/fints"), pin,
                (_, _) => ValueTask.FromResult<FinTsSessionCredential?>(Credential("123456", FinTsCredentialKind.Tan)));
            check(result.Status == FinTsSignedTanStatus.Rejected && result.Confirmation is null, "Wrong order/dialogue/user or pending reply cannot become TAN approval success.");
        }
        using (var pin = Credential("PIN-MARKER", FinTsCredentialKind.Pin))
        using (var transport = new FinTsSimulatedTransport())
        {
            int prompts = 0; var expired = Request(); clock.Now += 120_000;
            var result = await FinTsSignedTextTanExecution.ExecuteAsync(expired, transport, new Uri("https://bank.example/fints"), pin,
                (_, _) => { prompts++; return ValueTask.FromResult<FinTsSessionCredential?>(null); });
            check(result.Status == FinTsSignedTanStatus.Rejected && prompts == 0 && transport.TransmittedCount == 0, "Expired challenge context stops before TAN prompt or transmission.");
        }
        using (var pin = Credential("PIN-MARKER", FinTsCredentialKind.Pin))
        using (var transport = new FinTsSimulatedTransport())
        using (var cancellation = new CancellationTokenSource())
        {
            var request = Request();
            var result = await FinTsSignedTextTanExecution.ExecuteAsync(request, transport, new Uri("https://bank.example/fints"), pin, (_, _) =>
            { acquired = Credential("123456", FinTsCredentialKind.Tan); cancellation.Cancel(); return ValueTask.FromResult<FinTsSessionCredential?>(acquired); }, cancellation.Token);
            check(result.Status == FinTsSignedTanStatus.Cancelled && acquired?.GetSnapshot().State == FinTsCredentialState.Disposed && transport.TransmittedCount == 0,
                "Cancellation racing a returned TAN disposes it and prevents dispatch.");
        }
        Console.WriteLine("Signed text-TAN approval checks passed.");
    }
    private static FinTsSessionCredential Credential(string value, FinTsCredentialKind kind) => FinTsSessionCredential.CaptureAndClear(Encoding.ASCII.GetBytes(value), kind, TimeSpan.FromMinutes(5));
    private static byte[] Bytes(JsonElement element, string key) => Convert.FromBase64String(element.GetProperty(key).GetString()!);
    private sealed class Clock : TimeProvider
    {
        internal long Now;
        public override long TimestampFrequency => 1000;
        public override long GetTimestamp() => Now;
        public override DateTimeOffset GetUtcNow() => new(2026, 10, 21, 12, 0, 0, TimeSpan.Zero);
    }
}
