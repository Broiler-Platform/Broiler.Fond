using System.Reflection;
using System.Text;
using System.Text.Json;
using Broiler.Fond.Kernel.FinTs;
using Broiler.Fond.Kernel.FinTs.Transport;
using Broiler.Fond.TestSupport;

namespace Broiler.Fond.Kernel.Tests;

internal static class FinTsBankParameterBootstrapTests
{
    internal static void Run(Action<bool, string> check) => RunAsync(check).GetAwaiter().GetResult();
    private static async Task RunAsync(Action<bool, string> check)
    {
        var endpoint = new Uri("https://bank.example/fints");
        using var valid = Transport(BankParameterTestWire.Initialization());
        var acquired = await Execute(valid);
        check(acquired.Status == FinTsBankBootstrapStatus.Acquired && acquired.Observation?.Request.Identification.IsAnonymous == true &&
            acquired.Observation.Parameters.Bank?.Version == 1 && acquired.Observation.Parameters.User is null && valid.TransmittedCount == 2,
            "Anonymous bank bootstrap validates public BPD and a closed dialogue without user permissions.");
        string sent = string.Join("\n", valid.RecordedTransmissions.Select(t => Encoding.Latin1.GetString(t.Payload)));
        check(sent.Contains("9999999999+0+0") && sent.Contains("ASSIGNED-APP-ID+2.1") && !sent.Contains("HNSHA") && !sent.Contains("PUBLIC-USER"),
            "Bank bootstrap sends explicit product identity and anonymous markers without signing, user login or credentials.");
        foreach (string extra in new[] { "HIPINS:6:1:3+1+1+0+4:12:6:USER:CUSTOMER:HKSAL:N'", "HISALS:6:7:3+1+1+0'" })
        {
            using var typed = Transport(BankParameterTestWire.Initialization(extra: extra));
            check((await Execute(typed)).Status == FinTsBankBootstrapStatus.Acquired, "Anonymous bootstrap parses supported PIN/TAN/read parameter advertisements.");
        }
        using (var anonymousPermissions = Transport(BankParameterTestWire.Initialization(identityExtra: "+3920::PUBLIC-PROCEDURES:900")))
            check((await Execute(anonymousPermissions)).Status == FinTsBankBootstrapStatus.Acquired,
                "Scoped anonymous procedure reports can accompany BPD without becoming login-specific permission.");
        using var fixture = JsonDocument.Parse(Assembly.GetExecutingAssembly().GetManifestResourceStream("FinTs.pin-tan-synchronization-session-attempt-v1.json")!);
        string response = Encoding.Latin1.GetString(Convert.FromBase64String(fixture.RootElement.GetProperty("vectors")[0].GetProperty("syncCandidate").GetProperty("procedureResponseBase64").GetString()!));
        string tan = response.Split('\'').Single(s => s.StartsWith("HITANS:", StringComparison.Ordinal));
        tan = "HITANS:6:7:3" + tan[tan.IndexOf('+')..] + "'";
        using (var typed = Transport(BankParameterTestWire.Initialization(extra: tan)))
        {
            var result = await Execute(typed);
            check(result.Status == FinTsBankBootstrapStatus.Acquired && result.Observation!.Tan.Advertisements.Count == 1,
                "Anonymous bootstrap retains typed advertised TAN procedures without selecting one.");
        }
        foreach (byte[] responseBytes in new[]
        {
            BankParameterTestWire.Initialization(bankCode: "OTHER-BANK"),
            BankParameterTestWire.Initialization(protocol: "220"),
            BankParameterTestWire.Initialization(status: "9000"),
            BankParameterTestWire.Initialization(status: "3930"),
            BankParameterTestWire.Initialization(extra: "HITANS:6:8:3+1+1+0'"),
            BankParameterTestWire.Initialization(extra: "HIZZZS:6:1:3+PUBLIC-UNKNOWN'"),
            BankParameterTestWire.Initialization(extra: "HIUPA:6:4:3+PUBLIC-USER+1+0'")
        })
        {
            using var rejected = Transport(responseBytes);
            var result = await Execute(rejected);
            bool conflicting = Encoding.Latin1.GetString(responseBytes).Contains("9000::", StringComparison.Ordinal);
            check(result.Status == (conflicting ? FinTsBankBootstrapStatus.RejectedResponse : FinTsBankBootstrapStatus.RejectedParameters) &&
                result.Observation is null && rejected.TransmittedCount == (conflicting ? 1 : 2),
                $"Unsafe public bootstrap is rejected; only a qualified correlated dialogue can be closed ({result.Status}).");
        }
        using (var mismatch = Transport(BankParameterTestWire.Frame("BOOTSTRAP", 2, "HIRMG:2:2+0020::WRONG-MESSAGE'", 3)))
            check((await Execute(mismatch)).Status == FinTsBankBootstrapStatus.RejectedResponse && mismatch.TransmittedCount == 1,
                "Wrong-message bootstrap evidence is not accepted or used to close an unbound dialogue.");
        using (var badClose = Transport(BankParameterTestWire.Initialization(), BankParameterTestWire.Closing("OTHER")))
            check((await Execute(badClose)).Status == FinTsBankBootstrapStatus.CloseFailed && badClose.TransmittedCount == 2,
                "A mismatched close prevents parameter retention and is never blindly retried.");
        using (var malformed = Transport("invalid"u8.ToArray()))
            check((await Execute(malformed)).Status == FinTsBankBootstrapStatus.RejectedResponse, "Malformed bootstrap response fails closed.");
        using var cancelled = new CancellationTokenSource(); cancelled.Cancel();
        using (var noDispatch = Transport(BankParameterTestWire.Initialization()))
            check((await Execute(noDispatch, cancelled.Token)).Status == FinTsBankBootstrapStatus.Cancelled && noDispatch.TransmittedCount == 0,
                "Pre-cancelled bootstrap never dispatches.");
        using (var timeout = new FinTsSimulatedTransport())
        {
            timeout.EnqueueFailure(new FinTsTransportException(FinTsTransportIssue.Timeout, "SECRET-MARKER", endpoint));
            check((await Execute(timeout)).Status == FinTsBankBootstrapStatus.TransportFailed, "Transport failures return fixed bootstrap codes, without exception text.");
        }
        using (var cancellation = new CancellationTokenSource())
        using (var closingPending = new FinTsSimulatedTransport())
        {
            closingPending.EnqueueResponse(BankParameterTestWire.Initialization());
            closingPending.EnqueueHandler((_, _, _) => { cancellation.Cancel(); throw new OperationCanceledException(cancellation.Token); });
            var result = await Execute(closingPending, cancellation.Token);
            check(result.Status == FinTsBankBootstrapStatus.Cancelled && result.Observation is null && closingPending.TransmittedCount == 2,
                "Cancellation during close retains no parameters and does not replay the dispatched close.");
        }
        Console.WriteLine("Anonymous bank parameter bootstrap checks passed.");
        Task<FinTsBankBootstrapResult> Execute(IFinTsTransport transport, CancellationToken ct = default) =>
            FinTsBankParameterBootstrap.ExecuteAsync(transport, endpoint, "280", "PUBLIC-BANK", "ASSIGNED-APP-ID", "2.1", ct);
    }
    private static FinTsSimulatedTransport Transport(byte[] response, byte[]? closing = null)
    {
        var transport = new FinTsSimulatedTransport();
        transport.EnqueueResponse(response); transport.EnqueueResponse(closing ?? BankParameterTestWire.Closing());
        return transport;
    }
}
