using System.Reflection;
using System.Text;
using System.Text.Json;
using Broiler.Fond.Kernel.FinTs;
using Broiler.Fond.Kernel.FinTs.Transport;
using Broiler.Fond.Presentation.Services;
using Broiler.Fond.Presentation.Sessions;

namespace Broiler.Fond.Presentation.Tests;

internal static class BankBalanceTests
{
    internal static async Task RunAsync(Action<bool, string> check)
    {
        using var fixture = JsonDocument.Parse(Assembly.GetExecutingAssembly().GetManifestResourceStream("Discovery.bank-responses.json")!);
        var vectors = fixture.RootElement.GetProperty("vectors");
        var single = vectors[2]; var paged = vectors[3];
        var origin = single.GetProperty("initialization").GetProperty("context");
        var request = FinTsUnsignedInitializationRequest.Parse(FinTsMessageFrame.Parse(Bytes(origin, "originRequestBase64")));
        var response = FinTsResponse.Parse(FinTsMessageFrame.Parse(Bytes(origin, "procedureResponseBase64")));
        var procedures = new FinTsPinTanProcedureContext(request, FinTsTanParameterSet.Parse(FinTsParameterSet.Parse(response)), FinTsPermittedProcedureSet.Parse(response));
        var selection = new FinTsPinTanSignatureSelection(origin.GetProperty("profileVersion").GetInt32(), origin.GetProperty("securityFunction").GetInt32(), origin.GetProperty("tanSegmentVersion").GetInt32());
        const string iban = "DE89370400440532013000";
        string directory = Path.Combine(Path.GetTempPath(), "BroilerFond_BR06_" + Guid.NewGuid().ToString("N"));
        try
        {
            using var session = new HostSession(BankProductConfiguration.FromSettings("ASSIGNED-APP-ID", "2.1"));
            session.CreateNewProfile(directory, "BalanceTests123!", "Balances");
            ulong connection = session.AddInstitutionAndConnection("Bank", "PUBLIC-BANK", "https://bank.example/fints", "PUBLIC-USER", "PUBLIC-CUSTOMER").ConnectionId;
            session.ConfirmConnectionEndpoint(session.GetConnectionSetupContext(connection), "https://bank.example/fints");
            var manual = session.AddManualAccount(connection, "Everyday", iban, "EUR");
            bool manualBlocked = false;
            try { _ = session.GetBankBalanceContext(manual.AccountId); } catch (InvalidOperationException) { manualBlocked = true; }
            check(manualBlocked, "A manual-only account cannot enter the production balance workflow.");
            session.GetStore()!.MutateAndSave(s => s with
            {
                Connections = s.Connections with
                {
                    Connections = s.Connections.Connections.Select(c => c with
                    { IsSynchronized = true, SystemId = "PUBLIC-SYSTEM", State = "Connected", LastSuccessfulContact = DateTimeOffset.UtcNow }).ToArray()
                }
            });
            session.ReloadData();
            using var pin = FinTsSessionCredential.CaptureAndClear("PIN-MARKER"u8.ToArray(), FinTsCredentialKind.Pin, TimeSpan.FromMinutes(3));
            var discoveryTransport = new FinTsSimulatedTransport();
            discoveryTransport.EnqueueResponse(Wire(single.GetProperty("initialization"), "responseBase64"));
            discoveryTransport.EnqueueResponse(Bytes(vectors[0].GetProperty("allAccount"), "responseBase64"));
            discoveryTransport.EnqueueResponse(Bytes(vectors[0].GetProperty("closing"), "responseBase64"));
            var discovery = await session.DiscoverBankAccountsCoreAsync(session.GetConnectionSetupContext(connection), pin, procedures, selection, () => discoveryTransport);
            check(discovery.Review?.Accounts.Single().BalanceVersion == 7, "BR-06 setup uses a bank-discovered, reviewed balance capability.");
            if (discovery.Review is null) return;
            var linked = session.ConfirmBankAccountDiscovery(discovery.Review, [0]);
            check(linked.Outcome == BankDiscoveryOutcome.Saved, "BR-05 review supplies the verified target identity for BR-06.");
            var identity = session.CurrentSnapshot!.Accounts;
            ulong binding = session.GetBankBalanceContext(manual.AccountId).BindingId;
            var first = Transport(body => body.Replace("C:1,:EUR:20260910", "C:10,123:EUR:20260910+++20,456:EUR", StringComparison.Ordinal));
            var refreshed = await Refresh(first);
            check(refreshed.IsSuccess && refreshed.SavedObservations == 3, $"A production single-account balance refresh saves verified observations: {refreshed.Explanation}");
            var snapshot = session.CurrentSnapshot!;
            check(snapshot.Accounts.Accounts.SequenceEqual(identity.Accounts) && snapshot.Accounts.Bindings.Count == identity.Bindings.Count &&
                snapshot.Accounts.Bindings.All(b => identity.Bindings.Any(old => b with { BankIdentifiers = old.BankIdentifiers } == old && b.BankIdentifiers.SequenceEqual(old.BankIdentifiers))) &&
                snapshot.Accounts.Incarnations.SequenceEqual(identity.Incarnations), "Balance ingestion cannot discover, relink, activate, or allocate account identities.");
            var booked = snapshot.Balances.Balances.Single(b => b.Kind == "Booked");
            check(booked.Amount == "10.123" && snapshot.Balances.Balances.Single(b => b.Kind == "Available").Amount == "20.456" &&
                booked.BindingId == binding && booked.AccountId == manual.AccountId,
                "Durable ingestion preserves exact decimals and the reviewed account/binding identities.");
            check(booked.BankTimestamp?.Date == new DateTime(2026, 9, 10) && booked.RetrievedAt > booked.BankTimestamp,
                "The reported bank calendar date is kept separately from actual local retrieval time.");
            check(session.Accounts.Single(a => a.AccountId == manual.AccountId) is { BookedAmount: 10.123m, AvailableAmount: 20.456m, BindingState: "Active" },
                "The refreshed view uses the active bank binding and independent booked/available values.");
            check(first.RecordedTransmissions.Count == 3 && first.RecordedTransmissions.Any(t => Encoding.Latin1.GetString(t.Payload).Contains("HKSAL:3:7")),
                "Refresh sends the qualified production HKSAL version and a correlated closing request.");
            check(!JsonSerializer.Serialize(snapshot).Contains("PIN-MARKER") && !refreshed.Explanation.Contains("PIN-MARKER"), "Balance results and persisted snapshots contain no PIN marker.");
            session.Lock(); session.Unlock(directory, "BalanceTests123!");
            check(session.Accounts.Single(a => a.AccountId == manual.AccountId).BookedAmount == 10.123m && session.Accounts.Single(a => a.AccountId == manual.AccountId).AvailableAmount == 20.456m,
                "Saved balance values and ledger references survive reopening the encrypted profile.");

            var zero = await Refresh(Transport(body => body.Replace("C:1,:EUR:20260910", "C:0,:EUR:20260910", StringComparison.Ordinal)));
            check(zero.IsSuccess && session.Accounts.Single(a => a.AccountId == manual.AccountId) is { BookedAmount: 0m, AvailableAmount: null },
                "A valid zero stays zero; missing available balance writes an unavailable observation instead of showing the older value.");
            var tiny = await Refresh(Transport(b => b.Replace("C:1,:EUR", "C:0,000001:EUR", StringComparison.Ordinal)));
            check(tiny.IsSuccess && session.CurrentSnapshot!.Balances.Balances.OrderByDescending(b => b.Id).First(b => b.Kind == "Booked").Amount == "0.000001",
                "Small exact amounts persist without rounding or scientific notation that the portable decimal schema cannot read.");
            var pages = Transport(pages: true);
            var paginated = await Refresh(pages);
            check(paginated.IsSuccess && paginated.SavedObservations == 3 && pages.RecordedTransmissions.Count == 4 &&
                pages.RecordedTransmissions.Any(t => Encoding.Latin1.GetString(t.Payload).Contains("PUBLIC-NEXT")),
                "Bounded continuation pages validate tokens/counters and identical reports produce one set of target observations.");

            ulong oldGeneration = session.Metadata!.Generation, oldNextId = session.CurrentSnapshot!.Profile.NextId;
            int oldCount = session.CurrentSnapshot.Balances.Balances.Count;
            var failedSave = await session.RefreshBankAccountBalanceCoreAsync(session.GetBankBalanceContext(manual.AccountId), pin, procedures, selection,
                () => Transport(), _ => throw new IOException("PIN-MARKER"));
            check(failedSave.Outcome == BankBalanceOutcome.ReceivedNotSaved && !failedSave.Explanation.Contains("PIN-MARKER") &&
                session.Metadata.Generation == oldGeneration && session.CurrentSnapshot.Profile.NextId == oldNextId && session.CurrentSnapshot.Balances.Balances.Count == oldCount,
                "A failed save explicitly reports received-but-not-saved and leaves no balance or ledger allocations in the durable profile.");
            var conflicted = await Refresh(Transport(pages: true, secondPage: b => b.Replace("C:1,:EUR", "C:2,:EUR", StringComparison.Ordinal)));
            check(!conflicted.IsSuccess && session.Metadata.Generation == oldGeneration, "Conflicting balances across continuation pages cannot become a valid snapshot.");
            var currency = await Refresh(Transport(b => b.Replace("+EUR+", "+USD+", StringComparison.Ordinal).Replace(":EUR:", ":USD:", StringComparison.Ordinal)));
            check(!currency.IsSuccess && session.Metadata.Generation == oldGeneration, "A bank report in a conflicting account currency preserves the previous durable balance.");
            var wrongAccount = await Refresh(Transport(b => b.Replace(iban, "DE12500105170648489890", StringComparison.Ordinal)));
            check(!wrongAccount.IsSuccess && session.Metadata.Generation == oldGeneration, "A response for another IBAN cannot be ingested into the target account.");
            var closeFailed = await Refresh(Transport(closeFailure: true));
            check(!closeFailed.IsSuccess && session.Metadata.Generation == oldGeneration, "Validated balance data without a successful dialogue close cannot be committed.");
            using (var cancelled = new CancellationTokenSource())
            {
                cancelled.Cancel(); int dispatch = 0;
                var result = await session.RefreshBankAccountBalanceCoreAsync(session.GetBankBalanceContext(manual.AccountId), pin, procedures, selection,
                    () => { dispatch++; return Transport(); }, cancellationToken: cancelled.Token);
                check(result.Outcome == BankBalanceOutcome.Cancelled && dispatch == 0, "Pre-cancelled balance refresh never creates a transport.");
            }

            ulong unrelatedAccount = 0;
            var interleaved = Transport(onClose: () =>
            {
                unrelatedAccount = session.AddManualAccount(connection, "Unrelated account", "DE12500105170648489890", "EUR").AccountId;
                session.GetStore()!.MutateAndSave(s => s with { Profile = s.Profile with { Note = "Concurrent unrelated edit" } });
                session.ReloadData();
            });
            var rebased = await Refresh(interleaved);
            check(rebased.IsSuccess && session.CurrentSnapshot!.Profile.Note == "Concurrent unrelated edit" && session.CurrentSnapshot.Accounts.Accounts.Any(a => a.Id == unrelatedAccount),
                "Balance commit uses the latest profile and ledger, preserving unrelated edits and independently allocated account IDs.");
            check(session.CurrentSnapshot!.Balances.Balances.Any(b => b.Id == booked.Id && b.Amount == booked.Amount), "Refreshing appends observations without rewriting previous bank evidence.");

            FinTsSessionCredential? owned = null;
            var dialogs = new BroilerDialogService
            {
                HeadlessBankCredentialHandler = (prompt, _) =>
            {
                check(!prompt.IsTan && prompt.PromptMessage.Contains("not the profile passphrase", StringComparison.Ordinal), "Authenticated balance refresh requests a separate banking PIN.");
                owned = FinTsSessionCredential.CaptureAndClear("PIN-MARKER"u8.ToArray(), FinTsCredentialKind.Pin, TimeSpan.FromMinutes(1));
                return Task.FromResult<FinTsSessionCredential?>(owned);
            }
            };
            var authenticated = await session.AuthenticateAndRefreshBankAccountBalanceCoreAsync(session.GetBankBalanceContext(manual.AccountId), dialogs,
                procedures, selection, () => Transport(b => b.Replace("C:1,:EUR", "D:7,123:EUR", StringComparison.Ordinal)));
            check(authenticated.IsSuccess && owned!.GetSnapshot().State == FinTsCredentialState.Disposed &&
                session.Accounts.Single(a => a.AccountId == manual.AccountId).BookedAmount == -7.123m,
                "Authenticated refresh owns/disposes the banking PIN and persists the bank's signed debit balance exactly.");
            dialogs.HeadlessBankCredentialHandler = (_, _) => Task.FromResult<FinTsSessionCredential?>(null);
            int declinedDispatch = 0;
            var declined = await session.AuthenticateAndRefreshBankAccountBalanceCoreAsync(session.GetBankBalanceContext(manual.AccountId), dialogs,
                procedures, selection, () => { declinedDispatch++; return Transport(); });
            check(declined.Outcome == BankBalanceOutcome.Cancelled && declinedDispatch == 0, "Declining bank sign-in leaves the cached balance unchanged and sends no request.");

            var begun = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var slow = new FinTsSimulatedTransport();
            slow.EnqueueHandler(async (_, _, ct) => { begun.SetResult(); await Task.Delay(Timeout.InfiniteTimeSpan, ct); throw new InvalidOperationException(); });
            var active = Refresh(slow);
            await begun.Task.WaitAsync(TimeSpan.FromSeconds(5));
            var duplicate = await Refresh(Transport());
            check(duplicate.Outcome == BankBalanceOutcome.AlreadyRunning, "Single-account refresh shares connection admission with discovery/synchronization.");
            oldGeneration = session.Metadata.Generation;
            session.Lock();
            var stale = await active.WaitAsync(TimeSpan.FromSeconds(5));
            session.Unlock(directory, "BalanceTests123!");
            check(stale.Outcome == BankBalanceOutcome.ContextChanged && session.Metadata!.Generation == oldGeneration,
                "Profile lock cancels an in-flight refresh, discards all detached allocations, and preserves saved balance timestamps.");

            int beforeIdentityChange = session.CurrentSnapshot!.Balances.Balances.Count;
            ulong setupGeneration = 0;
            var changedIdentity = await Refresh(Transport(onClose: () =>
            {
                var setup = session.GetConnectionSetupContext(connection);
                session.UpdateConnectionSetup(setup, new(true, setup.Institution.DisplayName, setup.Institution.BankCode!, setup.Institution.Endpoint,
                    false, "CHANGED-USER", "PUBLIC-CUSTOMER"));
                setupGeneration = session.Metadata!.Generation;
            }));
            check(changedIdentity.Outcome == BankBalanceOutcome.ContextChanged && session.CurrentSnapshot!.Balances.Balances.Count == beforeIdentityChange &&
                session.Metadata!.Generation == setupGeneration, "Changing the bank login before commit discards the received result and preserves all previous balance timestamps.");

            Task<BankBalanceResult> Refresh(FinTsSimulatedTransport t) => session.RefreshBankAccountBalanceCoreAsync(session.GetBankBalanceContext(manual.AccountId),
                pin, procedures, selection, () => t);
        }
        finally { if (Directory.Exists(directory)) Directory.Delete(directory, true); }

        FinTsSimulatedTransport Transport(Func<string, string>? change = null, bool pages = false, Func<string, string>? secondPage = null,
            bool closeFailure = false, Action? onClose = null)
        {
            var vector = pages ? paged : single;
            var t = new FinTsSimulatedTransport();
            t.EnqueueResponse(Wire(vector.GetProperty("initialization"), "responseBase64"));
            t.EnqueueResponse(Wire(vector.GetProperty("balanceInitial"), "responseBase64", change));
            if (pages) t.EnqueueResponse(Wire(vector.GetProperty("balanceContinuations")[0], "responseBase64", secondPage ?? change));
            if (closeFailure) t.EnqueueFailure(new FinTsTransportException(FinTsTransportIssue.NetworkFailure, "PIN-MARKER", new Uri("https://bank.example/fints")));
            else t.EnqueueHandler((_, _, _) => { onClose?.Invoke(); return ValueTask.FromResult(new FinTsTransportResponse(200, Wire(vector.GetProperty("closing"), "responseBase64"), null, TimeSpan.Zero)); });
            return t;
        }
        byte[] Wire(JsonElement e, string key, Func<string, string>? change = null) => BankDiscoveryTests.ChangeWrappedBody(Bytes(e, key),
            body => (change ?? (b => b))(body.Replace("PUBLIC-IBAN", iban, StringComparison.Ordinal)));
    }
    private static byte[] Bytes(JsonElement element, string key) => Convert.FromBase64String(element.GetProperty(key).GetString()!);
}

