using System.Text;
using Broiler.Fond.Kernel.FinTs;
using Broiler.Fond.Kernel.FinTs.Transport;
using Broiler.Fond.Presentation.Services;
using Broiler.Fond.Presentation.Sessions;
using Broiler.Fond.Presentation.Views;
using Broiler.Fond.TestSupport;

namespace Broiler.Fond.Presentation.Tests;

internal static class BankParameterAcquisitionTests
{
    internal static async Task RunAsync(Action<bool, string> check)
    {
        string directory = Path.Combine(Path.GetTempPath(), "BroilerFond_Parameters_" + Guid.NewGuid().ToString("N"));
        int factories = 0, prompts = 0;
        string mode = "ok";
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var transports = new List<FinTsSimulatedTransport>();
        var alerts = new List<string>();
        try
        {
            using var session = new HostSession(BankProductConfiguration.FromSettings("ASSIGNED-APP-ID", "2.1"), null, null, null, _ => Transport());
            session.CreateNewProfile(directory, "ParameterTests123!", "Parameters");
            ulong id = session.AddInstitutionAndConnection("Bank", "PUBLIC-BANK", "https://bank.example/fints", "PUBLIC-USER", "PUBLIC-CUSTOMER").ConnectionId;
            var dialogs = new BroilerDialogService
            {
                HeadlessBankCredentialHandler = (_, _) => { prompts++; return Task.FromResult<FinTsSessionCredential?>(null); },
                HeadlessAlertHandler = (_, text) => { alerts.Add(text); return Task.CompletedTask; }
            };
            using var view = new ConnectionsView(session, dialogs);
            view.ConnectionsList.SelectedItemId = id.ToString();
            await view.ConnectSelectedAsync();
            check(factories == 0 && prompts == 0, "Anonymous parameter acquisition requires endpoint confirmation before transport creation.");
            session.ConfirmConnectionEndpoint(session.GetConnectionSetupContext(id), "https://bank.example/fints");
            view.ConnectionsList.SelectedItemId = id.ToString();
            ulong generation = session.Metadata!.Generation;
            await view.ConnectSelectedAsync();
            var context = session.GetConnectionSetupContext(id);
            check(session.BankParameters.GetCurrent(context)?.Parameters.Bank?.Version == 1 && factories == 1 && transports[0].TransmittedCount == 2,
                "Connect downloads and retains a closed public parameter exchange through the ordinary action.");
            check(alerts.Last().Contains("Procedure selection") && alerts.Last().Contains("no PIN") && prompts == 0 && session.Metadata.Generation == generation &&
                !context.Connection.IsSynchronized && session.Accounts.Count == 0 && await session.BankProcedures.GetBankProcedureAsync(context) is null,
                "Public BPD download neither signs in, selects a procedure, changes the encrypted profile nor fabricates account capabilities.");
            string sent = string.Join("\n", transports[0].RecordedTransmissions.Select(t => Encoding.Latin1.GetString(t.Payload)));
            check(sent.Contains("ASSIGNED-APP-ID+2.1") && !sent.Contains("PUBLIC-USER") && !sent.Contains("PUBLIC-CUSTOMER") && !sent.Contains("HNSHA"),
                "The parameter request carries product identity but no configured login, PIN or signed credential envelope.");
            await view.ConnectSelectedAsync();
            check(factories == 1 && alerts.Last().Contains("Current public bank parameters"), "Repeated Connect reuses current scoped public observations without a download loop.");
            EditLogin("OTHER-USER");
            check(session.BankParameters.GetCurrent(session.GetConnectionSetupContext(id)) is null, "Editing login identity invalidates scoped public parameter state.");
            mode = "bad-close";
            var failed = await session.BankParameters.AcquireAsync(session.GetConnectionSetupContext(id));
            check(failed.Outcome == BankParameterAcquisitionOutcome.CloseFailed && failed.Observation is null && session.BankParameters.GetCurrent(session.GetConnectionSetupContext(id)) is null,
                "Failed close never publishes reusable parameter state.");
            mode = "hold";
            var active = session.BankParameters.AcquireAsync(session.GetConnectionSetupContext(id));
            await started.Task.WaitAsync(TimeSpan.FromSeconds(5));
            var duplicate = await session.BankParameters.AcquireAsync(session.GetConnectionSetupContext(id));
            check(duplicate.Outcome == BankParameterAcquisitionOutcome.Busy, "Parameter acquisition shares per-connection admission with other bank operations.");
            session.Lock(); session.Unlock(directory, "ParameterTests123!");
            var cancelled = await active.WaitAsync(TimeSpan.FromSeconds(5));
            check(cancelled.Outcome == BankParameterAcquisitionOutcome.Cancelled && session.BankParameters.GetCurrent(session.GetConnectionSetupContext(id)) is null,
                "Lock/reopen cancels an active parameter download and cannot retain its stale response.");
            mode = "ok";
            var downloaded = await session.BankParameters.AcquireAsync(session.GetConnectionSetupContext(id));
            check(downloaded.Outcome == BankParameterAcquisitionOutcome.Acquired, "A cancelled download releases admission so a fresh attempt can complete.");
            var beforeEdit = session.GetConnectionSetupContext(id);
            session.UpdateConnectionSetup(beforeEdit, new(true, "Bank", "PUBLIC-BANK", "https://other.example/fints", false, "OTHER-USER", "PUBLIC-CUSTOMER"));
            check(session.BankParameters.GetCurrent(session.GetConnectionSetupContext(id)) is null, "Changing endpoint invalidates previously downloaded public parameters.");
            session.ConfirmConnectionEndpoint(session.GetConnectionSetupContext(id), "https://other.example/fints");
            mode = "hold"; started = new(TaskCreationOptions.RunContinuationsAsynchronously);
            active = session.BankParameters.AcquireAsync(session.GetConnectionSetupContext(id));
            await started.Task.WaitAsync(TimeSpan.FromSeconds(5));
            EditLogin("THIRD-USER");
            check((await active.WaitAsync(TimeSpan.FromSeconds(5))).Outcome == BankParameterAcquisitionOutcome.Cancelled,
                "Editing bank login while downloading cancels the pending context before retention.");
            check(prompts == 0 && alerts.All(a => !a.Contains("SECRET-MARKER")), "Parameter-only setup never requests credentials or displays raw backend failure text.");

            void EditLogin(string login)
            {
                var setup = session.GetConnectionSetupContext(id);
                session.UpdateConnectionSetup(setup, new(true, "Bank", "PUBLIC-BANK", setup.Institution.Endpoint, false, login, "PUBLIC-CUSTOMER"));
            }
        }
        finally { if (Directory.Exists(directory)) Directory.Delete(directory, true); }

        FinTsSimulatedTransport Transport()
        {
            factories++;
            var transport = new FinTsSimulatedTransport(); transports.Add(transport);
            if (mode == "hold") transport.EnqueueHandler(async (_, _, ct) => { started.TrySetResult(); await Task.Delay(Timeout.InfiniteTimeSpan, ct); throw new InvalidOperationException("SECRET-MARKER"); });
            else transport.EnqueueResponse(BankParameterTestWire.Initialization());
            transport.EnqueueResponse(BankParameterTestWire.Closing(mode == "bad-close" ? "OTHER" : "BOOTSTRAP"));
            return transport;
        }
    }
}
