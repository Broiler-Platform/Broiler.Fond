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

internal static class FinTsProcedureQualificationTests
{
    internal static void Run(Action<bool, string> check) => RunAsync(check).GetAwaiter().GetResult();
    private static async Task RunAsync(Action<bool, string> check)
    {
        using var source = JsonDocument.Parse(Assembly.GetExecutingAssembly().GetManifestResourceStream("FinTs.pin-tan-synchronization-session-attempt-v1.json")!);
        byte[] response = Convert.FromBase64String(source.RootElement.GetProperty("vectors")[0].GetProperty("syncCandidate").GetProperty("procedureResponseBase64").GetString()!);
        var connection = BankConnection.Create(new ConnectionId(2), new InstitutionId(1),
            ManualInstitutionConfiguration.Create("Bank", "https://bank.example/fints", "PUBLIC-BANK").ConfirmEndpoint("https://bank.example/fints"), "PUBLIC-USER", "PUBLIC-CUSTOMER");
        var product = new FinTsProductRegistration("ASSIGNED-APP-ID", "2.1");
        var selected = new FinTsPinTanSignatureSelection(2, 900, 7);
        using (var transport = Transport(response))
        {
            var result = await Execute(transport);
            check(result.Status == FinTsProcedureQualificationStatus.Qualified && result.Procedures?.Initialization.Identification.IsAnonymous == false && transport.TransmittedCount == 2,
                "An identified closed parameter exchange qualifies the explicitly selected procedure for its actual login context.");
            string sent = string.Join("\n", transport.RecordedTransmissions.Select(t => Encoding.Latin1.GetString(t.Payload)));
            check(sent.Contains("PUBLIC-CUSTOMER+0+1") && sent.Contains("ASSIGNED-APP-ID+2.1") && !sent.Contains("HNSHA"),
                "Procedure qualification sends the configured customer/product identity without a PIN or signed credential envelope.");
        }
        foreach (Func<string, string> edit in new Func<string, string>[]
        {
            s => s.Replace("PUBLIC-USER", "OTHER-USER", StringComparison.Ordinal),
            s => s.Replace("280:PUBLIC-BANK", "280:OTHER-BANK", StringComparison.Ordinal),
            s => s.Replace(":900:999'", ":901:999'", StringComparison.Ordinal),
            s => s.Replace("PUBLIC-USER+2+0", "PUBLIC-USER+0+0", StringComparison.Ordinal),
            s => s.Replace("HITANS:7:7:3", "HITANS:7:8:3", StringComparison.Ordinal),
            s => s.Replace("+3920::PUBLIC-PERMISSIONS:900:999", "", StringComparison.Ordinal)
        })
        {
            using var rejected = Transport(BankParameterTestWire.Edit(response, edit));
            var result = await Execute(rejected);
            check(result.Status == FinTsProcedureQualificationStatus.Rejected && result.Procedures is null,
                "Wrong bank/user, unpermitted choice, dialogue-only UPD, unsupported TAN schema and missing permission evidence cannot qualify a procedure.");
        }
        using (var closeFails = Transport(response, BankParameterTestWire.Closing("OTHER")))
            check((await Execute(closeFails)).Status == FinTsProcedureQualificationStatus.CloseFailed && closeFails.TransmittedCount == 2,
                "Failed identified close prevents qualification without replaying the close.");
        foreach (string advertisement in new[] { "HIPINS:8:1:3+1+1+0+4:12:6:USER:CUSTOMER:HKSAL:N'", "HISALS:8:7:3+1+1+0'" })
        {
            using var typed = Transport(BankParameterTestWire.Edit(response, s => s.Replace("HNHBS:8:1", advertisement + "HNHBS:9:1", StringComparison.Ordinal)));
            check((await Execute(typed)).Status == FinTsProcedureQualificationStatus.Qualified,
                "Login-scoped qualification accepts additional supported typed PIN/TAN/read advertisements without activating accounts.");
        }
        using var cancel = new CancellationTokenSource(); cancel.Cancel();
        using (var noRequest = Transport(response))
            check((await Execute(noRequest, cancel.Token)).Status == FinTsProcedureQualificationStatus.Cancelled && noRequest.TransmittedCount == 0,
                "Cancelled procedure qualification sends no request.");
        Console.WriteLine("Login-scoped procedure qualification checks passed.");
        Task<FinTsProcedureQualificationResult> Execute(IFinTsTransport transport, CancellationToken ct = default) =>
            FinTsProcedureQualification.ExecuteAsync(transport, connection, product, selected, ct);
    }
    private static FinTsSimulatedTransport Transport(byte[] response, byte[]? closing = null)
    {
        var transport = new FinTsSimulatedTransport();
        transport.EnqueueResponse(response); transport.EnqueueResponse(closing ?? BankParameterTestWire.Closing("SYNTHETIC")); return transport;
    }
}
